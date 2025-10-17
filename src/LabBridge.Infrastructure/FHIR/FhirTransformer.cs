using Hl7.Fhir.Model;
using LabBridge.Core.Interfaces;
using LabBridge.Core.Models;
using NHapi.Model.V25.Message;
using NHapi.Model.V25.Segment;

namespace LabBridge.Infrastructure.FHIR;

/// <summary>
/// Transforms HL7v2 messages to FHIR R4 resources
/// </summary>
public class FhirTransformer : IHL7ToFhirTransformer
{
    public TransformationResult Transform(object hl7Message)
    {
        if (hl7Message == null)
        {
            throw new ArgumentNullException(nameof(hl7Message));
        }

        try
        {
            if (hl7Message is not ORU_R01 oruMessage)
            {
                throw new InvalidOperationException($"Unsupported message type: {hl7Message.GetType().Name}");
            }

            var result = new TransformationResult
            {
                MessageControlId = oruMessage.MSH.MessageControlID.Value,
                MessageType = "ORU^R01"
            };

            // Extract patient from PID segment
            var patientResult = oruMessage.GetPATIENT_RESULT(0);
            var pid = patientResult.PATIENT.PID;
            var patient = TransformPatient(pid);
            result.Patient = patient;

            var patientId = pid.GetPatientIdentifierList(0).IDNumber.Value;

            // Extract observations from OBX segments
            var observations = new List<Observation>();
            var observationIds = new List<string>();
            var orderObservation = patientResult.GetORDER_OBSERVATION(0);
            var obxCount = orderObservation.OBSERVATIONRepetitionsUsed;

            for (int i = 0; i < obxCount; i++)
            {
                var obx = orderObservation.GetOBSERVATION(i).OBX;
                var observation = (Observation)TransformObservation(obx, patientId);
                observations.Add(observation);
                observationIds.Add($"Observation/{i + 1}");
            }

            result.Observations = observations.Cast<object>().ToList();

            // Extract diagnostic report from OBR segment
            var obr = orderObservation.OBR;
            var diagnosticReport = TransformDiagnosticReport(obr, patientId, observationIds);
            result.DiagnosticReport = diagnosticReport;

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            return new TransformationResult
            {
                Success = false,
                ErrorMessage = $"Transformation failed: {ex.Message}"
            };
        }
    }

    public object TransformPatient(object pidSegment)
    {
        if (pidSegment is not PID pid)
        {
            throw new ArgumentException("Invalid PID segment", nameof(pidSegment));
        }

        var patient = new Patient
        {
            Identifier = new List<Identifier>
            {
                new Identifier
                {
                    System = "urn:oid:2.16.840.1.113883.4.1",
                    Value = pid.GetPatientIdentifierList(0).IDNumber.Value
                }
            }
        };

        // Patient name
        var patientName = pid.GetPatientName(0);
        if (patientName != null)
        {
            var humanName = new HumanName
            {
                Family = patientName.FamilyName.Surname.Value
            };

            var givenNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(patientName.GivenName.Value))
            {
                givenNames.Add(patientName.GivenName.Value);
            }
            if (!string.IsNullOrWhiteSpace(patientName.SecondAndFurtherGivenNamesOrInitialsThereof.Value))
            {
                givenNames.Add(patientName.SecondAndFurtherGivenNamesOrInitialsThereof.Value);
            }

            if (givenNames.Any())
            {
                humanName.Given = givenNames;
            }

            patient.Name = new List<HumanName> { humanName };
        }

        // Gender
        if (!string.IsNullOrWhiteSpace(pid.AdministrativeSex.Value))
        {
            patient.Gender = MapGenderCode(pid.AdministrativeSex.Value);
        }

        // Birth date
        if (!string.IsNullOrWhiteSpace(pid.DateTimeOfBirth.Time.Value))
        {
            patient.BirthDate = ParseHl7Date(pid.DateTimeOfBirth.Time.Value);
        }

        return patient;
    }

    public object TransformObservation(object obxSegment, string patientId)
    {
        if (obxSegment is not OBX obx)
        {
            throw new ArgumentException("Invalid OBX segment", nameof(obxSegment));
        }

        var observation = new Observation
        {
            Status = ObservationStatus.Final,
            Subject = new ResourceReference($"Patient/{patientId}")
        };

        // Observation code (LOINC)
        var observationId = obx.ObservationIdentifier;
        observation.Code = new CodeableConcept
        {
            Coding = new List<Coding>
            {
                new Coding
                {
                    System = "http://loinc.org",
                    Code = observationId.Identifier.Value,
                    Display = observationId.Text.Value
                }
            }
        };

        // Value - handle numeric (NM) and coded (CE) types
        var valueType = obx.ValueType.Value;
        if (valueType == "NM")
        {
            var numericValue = obx.GetObservationValue(0).Data.ToString();
            if (decimal.TryParse(numericValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                observation.Value = new Quantity
                {
                    Value = value,
                    Unit = obx.Units.Identifier.Value,
                    System = "http://unitsofmeasure.org",
                    Code = obx.Units.Identifier.Value
                };
            }
        }
        else if (valueType == "CE")
        {
            var codedValue = obx.GetObservationValue(0).Data.ToString();
            observation.Value = new CodeableConcept
            {
                Text = codedValue
            };
        }

        // Reference range
        if (!string.IsNullOrWhiteSpace(obx.ReferencesRange.Value))
        {
            observation.ReferenceRange = new List<Observation.ReferenceRangeComponent>
            {
                new Observation.ReferenceRangeComponent
                {
                    Text = obx.ReferencesRange.Value
                }
            };
        }

        // Interpretation (abnormal flags)
        if (obx.AbnormalFlagsRepetitionsUsed > 0 && !string.IsNullOrWhiteSpace(obx.GetAbnormalFlags(0).Value))
        {
            observation.Interpretation = new List<CodeableConcept>
            {
                new CodeableConcept
                {
                    Coding = new List<Coding>
                    {
                        new Coding
                        {
                            System = "http://terminology.hl7.org/CodeSystem/v3-ObservationInterpretation",
                            Code = obx.GetAbnormalFlags(0).Value
                        }
                    }
                }
            };
        }

        // Status
        if (!string.IsNullOrWhiteSpace(obx.ObservationResultStatus.Value))
        {
            observation.Status = MapObservationStatus(obx.ObservationResultStatus.Value);
        }

        // Effective date/time
        if (!string.IsNullOrWhiteSpace(obx.DateTimeOfTheObservation.Time.Value))
        {
            observation.Effective = new FhirDateTime(ParseHl7DateTime(obx.DateTimeOfTheObservation.Time.Value));
        }

        return observation;
    }

    public object TransformDiagnosticReport(object obrSegment, string patientId, List<string> observationIds)
    {
        if (obrSegment is not OBR obr)
        {
            throw new ArgumentException("Invalid OBR segment", nameof(obrSegment));
        }

        var report = new DiagnosticReport
        {
            Status = DiagnosticReport.DiagnosticReportStatus.Final,
            Subject = new ResourceReference($"Patient/{patientId}")
        };

        // Identifiers
        report.Identifier = new List<Identifier>();

        if (!string.IsNullOrWhiteSpace(obr.PlacerOrderNumber.EntityIdentifier.Value))
        {
            report.Identifier.Add(new Identifier
            {
                System = "urn:oid:PlacerOrderNumber",
                Value = obr.PlacerOrderNumber.EntityIdentifier.Value
            });
        }

        if (!string.IsNullOrWhiteSpace(obr.FillerOrderNumber.EntityIdentifier.Value))
        {
            report.Identifier.Add(new Identifier
            {
                System = "urn:oid:FillerOrderNumber",
                Value = obr.FillerOrderNumber.EntityIdentifier.Value
            });
        }

        // Code (panel/test code - LOINC)
        var universalServiceId = obr.UniversalServiceIdentifier;
        report.Code = new CodeableConcept
        {
            Coding = new List<Coding>
            {
                new Coding
                {
                    System = "http://loinc.org",
                    Code = universalServiceId.Identifier.Value,
                    Display = universalServiceId.Text.Value
                }
            }
        };

        // Effective date/time
        if (!string.IsNullOrWhiteSpace(obr.ObservationDateTime.Time.Value))
        {
            report.Effective = new FhirDateTime(ParseHl7DateTime(obr.ObservationDateTime.Time.Value));
        }

        // Issued date/time (when report was finalized)
        report.Issued = DateTimeOffset.UtcNow;

        // Status
        if (!string.IsNullOrWhiteSpace(obr.ResultStatus.Value))
        {
            report.Status = MapDiagnosticReportStatus(obr.ResultStatus.Value);
        }

        // Results - references to observations
        report.Result = observationIds.Select(id => new ResourceReference(id)).ToList();

        return report;
    }

    #region Helper Methods

    private AdministrativeGender MapGenderCode(string hl7Gender)
    {
        return hl7Gender?.ToUpper() switch
        {
            "M" => AdministrativeGender.Male,
            "F" => AdministrativeGender.Female,
            "O" => AdministrativeGender.Other,
            "U" => AdministrativeGender.Unknown,
            _ => AdministrativeGender.Unknown
        };
    }

    private ObservationStatus MapObservationStatus(string hl7Status)
    {
        return hl7Status?.ToUpper() switch
        {
            "F" => ObservationStatus.Final,
            "P" => ObservationStatus.Preliminary,
            "C" => ObservationStatus.Corrected,
            "X" => ObservationStatus.Cancelled,
            _ => ObservationStatus.Final
        };
    }

    private DiagnosticReport.DiagnosticReportStatus MapDiagnosticReportStatus(string hl7Status)
    {
        return hl7Status?.ToUpper() switch
        {
            "F" => DiagnosticReport.DiagnosticReportStatus.Final,
            "P" => DiagnosticReport.DiagnosticReportStatus.Preliminary,
            "C" => DiagnosticReport.DiagnosticReportStatus.Corrected,
            "X" => DiagnosticReport.DiagnosticReportStatus.Cancelled,
            _ => DiagnosticReport.DiagnosticReportStatus.Final
        };
    }

    private string? ParseHl7Date(string hl7Date)
    {
        if (string.IsNullOrWhiteSpace(hl7Date) || hl7Date.Length < 8)
        {
            return null;
        }

        // HL7 format: YYYYMMDD
        var year = hl7Date.Substring(0, 4);
        var month = hl7Date.Substring(4, 2);
        var day = hl7Date.Substring(6, 2);

        return $"{year}-{month}-{day}";
    }

    private DateTimeOffset ParseHl7DateTime(string hl7DateTime)
    {
        if (string.IsNullOrWhiteSpace(hl7DateTime))
        {
            return DateTimeOffset.UtcNow;
        }

        // HL7 format: YYYYMMDDHHMMSS
        if (hl7DateTime.Length >= 14)
        {
            var year = int.Parse(hl7DateTime.Substring(0, 4));
            var month = int.Parse(hl7DateTime.Substring(4, 2));
            var day = int.Parse(hl7DateTime.Substring(6, 2));
            var hour = int.Parse(hl7DateTime.Substring(8, 2));
            var minute = int.Parse(hl7DateTime.Substring(10, 2));
            var second = int.Parse(hl7DateTime.Substring(12, 2));

            return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
        }
        else if (hl7DateTime.Length >= 8)
        {
            // Date only
            var year = int.Parse(hl7DateTime.Substring(0, 4));
            var month = int.Parse(hl7DateTime.Substring(4, 2));
            var day = int.Parse(hl7DateTime.Substring(6, 2));

            return new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        }

        return DateTimeOffset.UtcNow;
    }

    #endregion
}
