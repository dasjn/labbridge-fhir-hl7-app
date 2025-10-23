using System.Net.Http.Headers;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Refit;

namespace LabBridge.Infrastructure.FHIR;

/// <summary>
/// Custom HTTP content serializer for Refit that uses FhirJsonSerializer/FhirJsonParser
/// This ensures FHIR resources are correctly serialized/deserialized according to FHIR R4 spec
/// </summary>
public class FhirHttpContentSerializer : IHttpContentSerializer
{
    private readonly FhirJsonSerializer _serializer;
    private readonly FhirJsonParser _parser;

    public FhirHttpContentSerializer()
    {
        _serializer = new FhirJsonSerializer(new SerializerSettings
        {
            Pretty = false // Compact JSON for network efficiency
        });

        _parser = new FhirJsonParser(new ParserSettings
        {
            AcceptUnknownMembers = false,
            AllowUnrecognizedEnums = false
        });
    }

    /// <summary>
    /// Serialize FHIR resource to HTTP content
    /// </summary>
    public HttpContent ToHttpContent<T>(T item)
    {
        if (item is not Base fhirResource)
        {
            throw new ArgumentException($"Item must be a FHIR resource (inherit from Base), got {typeof(T).Name}");
        }

        // Serialize using FhirJsonSerializer
        var json = _serializer.SerializeToString(fhirResource);

        var content = new StringContent(json, Encoding.UTF8, "application/fhir+json");
        return content;
    }

    /// <summary>
    /// Deserialize HTTP content to FHIR resource
    /// </summary>
    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, CancellationToken cancellationToken = default)
    {
        var json = await content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        // Deserialize using FhirJsonParser
        var resource = _parser.Parse<Base>(json);

        if (resource is T typedResource)
        {
            return typedResource;
        }

        throw new InvalidOperationException(
            $"Expected FHIR resource of type {typeof(T).Name}, but got {resource.GetType().Name}");
    }

    /// <summary>
    /// Get field name for property (FHIR uses camelCase)
    /// </summary>
    public string GetFieldNameForProperty(System.Reflection.PropertyInfo propertyInfo)
    {
        // FHIR uses camelCase for JSON properties (standard)
        return propertyInfo.Name;
    }
}
