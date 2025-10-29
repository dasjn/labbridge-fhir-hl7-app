using System.Net.Http.Json;
using FluentAssertions;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using LabBridge.IntegrationTests.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;
using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.Data;
using LabBridge.Infrastructure.HL7;
using LabBridge.Infrastructure.FHIR;
using LabBridge.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Refit;
using Polly;
using Polly.Extensions.Http;

namespace LabBridge.IntegrationTests;

/// <summary>
/// Tests End-to-End del ecosistema completo:
/// HL7v2 Analyzer → LabBridge → RabbitMQ → FHIR Transformation → LabFlow API
///
/// PREREQUISITOS:
/// 1. Docker Desktop corriendo
/// 2. Ejecutar antes del test:
///    cd tests/LabBridge.IntegrationTests
///    docker-compose -f docker-compose.test.yml up -d
/// 3. Ejecutar después del test:
///    docker-compose -f docker-compose.test.yml down -v
/// </summary>
public class EndToEndTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly FhirJsonParser _fhirParser;
    private IHost? _labBridgeHost;
    private HttpClient? _labFlowHttpClient;

    public EndToEndTests(ITestOutputHelper output)
    {
        _output = output;
        _fhirParser = new FhirJsonParser();
    }

    /// <summary>
    /// Setup: Levanta LabBridge programáticamente
    /// (RabbitMQ y LabFlow ya están corriendo en Docker via docker-compose)
    /// </summary>
    public async System.Threading.Tasks.Task InitializeAsync()
    {
        _output.WriteLine("=== E2E Test Setup ===");
        _output.WriteLine("1. Verificando que RabbitMQ esté disponible...");
        await WaitForService("http://localhost:15672", TimeSpan.FromSeconds(30));
        _output.WriteLine("   ✓ RabbitMQ está listo");

        _output.WriteLine("2. Verificando que LabFlow API esté disponible...");
        await WaitForService("http://localhost:8080/health", TimeSpan.FromSeconds(30));
        _output.WriteLine("   ✓ LabFlow API está listo");

        _output.WriteLine("3. Configurando HTTP client para LabFlow API (sin autenticación - testing mode)...");
        _labFlowHttpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };
        _output.WriteLine("   ✓ HTTP Client configurado");

        _output.WriteLine("4. Aplicando database migrations...");
        _labBridgeHost = CreateLabBridgeHost();

        // Apply EF Core migrations before starting the service
        using (var scope = _labBridgeHost.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await context.Database.MigrateAsync();
        }
        _output.WriteLine("   ✓ Database migrations applied");

        _output.WriteLine("5. Levantando LabBridge service...");
        await _labBridgeHost.StartAsync();
        _output.WriteLine("   ✓ LabBridge está corriendo");

        // Esperar un poco para que LabBridge se conecte a RabbitMQ
        await System.Threading.Tasks.Task.Delay(2000);

        _output.WriteLine("=== Setup Completado ===\n");
    }

    /// <summary>
    /// Teardown: Detiene LabBridge
    /// </summary>
    public async System.Threading.Tasks.Task DisposeAsync()
    {
        _output.WriteLine("\n=== E2E Test Teardown ===");

        if (_labBridgeHost != null)
        {
            _output.WriteLine("Deteniendo LabBridge...");
            await _labBridgeHost.StopAsync();
            _labBridgeHost.Dispose();
            _output.WriteLine("✓ LabBridge detenido");
        }

        _labFlowHttpClient?.Dispose();

        _output.WriteLine("=== Teardown Completado ===");
    }

    /// <summary>
    /// TEST PRINCIPAL E2E
    ///
    /// Flujo completo:
    /// 1. Envía mensaje HL7v2 ORU^R01 (CBC panel con 3 resultados)
    /// 2. LabBridge lo recibe → parsea → publica a RabbitMQ → ACK inmediato
    /// 3. LabBridge consume de RabbitMQ → transforma a FHIR → POST a LabFlow API
    /// 4. Verifica que los recursos FHIR se crearon correctamente en LabFlow
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task EndToEnd_SendHL7Message_CreatesResourcesInLabFlow()
    {
        // ========================================
        // ARRANGE
        // ========================================
        _output.WriteLine("=== ARRANGE: Preparando mensaje HL7v2 ===");

        // Generate unique IDs for this test run (to avoid conflicts with previous test data)
        var testRunId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var patientId = $"E2E-{testRunId}";
        var messageControlId = $"MSG-{testRunId}";
        var orderNumber = $"ORD-{testRunId}";

        // Mensaje HL7v2 ORU^R01 (CBC panel con 3 observaciones)
        // CRÍTICO: HL7v2 usa \r (carriage return) como separador de segmentos, NO \n
        var hl7Message = $"MSH|^~\\&|PANTHER|LAB|LABFLOW|HOSPITAL|20251021120000||ORU^R01|{messageControlId}|P|2.5\r" +
                         $"PID|1||{patientId}^^^MRN||TestPatient^John^MiddleName||19850315|M|||123 Test Street^^TestCity^TC^12345||555-1234|||S\r" +
                         $"OBR|1|{orderNumber}|LAB-E2E-456|58410-2^CBC panel - Blood by Automated count^LN|||20251021115500||||||||||||||||F\r" +
                         "OBX|1|NM|718-7^Hemoglobin [Mass/volume] in Blood^LN||14.5|g/dL|13.5-17.5|N|||F|||20251021120000\r" +
                         "OBX|2|NM|6690-2^Leukocytes [#/volume] in Blood by Automated count^LN||7500|/uL|4500-11000|N|||F|||20251021120000\r" +
                         "OBX|3|NM|777-3^Platelets [#/volume] in Blood by Automated count^LN||250000|/uL|150000-400000|N|||F|||20251021120000";

        _output.WriteLine($"Mensaje HL7v2 preparado:");
        _output.WriteLine($"  - Patient ID: {patientId}");
        _output.WriteLine($"  - Message Control ID: {messageControlId}");
        _output.WriteLine($"  - Panel: CBC (3 observaciones)");
        _output.WriteLine($"  - Hemoglobin: 14.5 g/dL");
        _output.WriteLine($"  - WBC: 7500 /uL");
        _output.WriteLine($"  - Platelets: 250000 /uL\n");

        // ========================================
        // ACT
        // ========================================
        _output.WriteLine("=== ACT: Enviando mensaje HL7v2 a LabBridge ===");

        // Paso 1: Enviar mensaje por TCP (MLLP) a LabBridge
        using var mllpClient = new MllpTcpClient("localhost", 2575);
        var ackResponse = await mllpClient.SendMessageAsync(hl7Message);

        _output.WriteLine($"✓ Mensaje enviado");
        _output.WriteLine($"✓ ACK recibido: {ackResponse.Substring(0, Math.Min(100, ackResponse.Length))}...");

        // Paso 2: Esperar a que LabBridge procese el mensaje asíncronamente
        // (HL7 → RabbitMQ → Transform → POST a LabFlow API)
        _output.WriteLine($"Esperando procesamiento asíncrono (5 segundos)...");
        await System.Threading.Tasks.Task.Delay(5000);
        _output.WriteLine($"✓ Procesamiento completado\n");

        // ========================================
        // ASSERT
        // ========================================
        _output.WriteLine("=== ASSERT: Verificando recursos FHIR en LabFlow ===");

        // Paso 3: Verificar que el ACK sea exitoso (MSA|AA)
        ackResponse.Should().Contain("MSA|AA", "el ACK debe ser Application Accept");
        ackResponse.Should().Contain(messageControlId, "el ACK debe preservar el Message Control ID");
        _output.WriteLine("✓ ACK válido (MSA|AA)");

        // Paso 4: Verificar que el Patient se creó en LabFlow
        var patient = await GetPatientByIdentifier(patientId);
        patient.Should().NotBeNull("el paciente debe haberse creado en LabFlow");
        patient!.Name.Should().HaveCount(1);
        patient.Name[0].Family.Should().Be("TestPatient");
        patient.Name[0].Given.Should().Contain("John");
        patient.Name[0].Given.Should().Contain("MiddleName");
        patient.Gender.Should().Be(AdministrativeGender.Male);
        patient.BirthDate.Should().Be("1985-03-15");
        _output.WriteLine($"✓ Patient creado: {patient.Id} - {patient.Name[0].Family}, {patient.Name[0].Given.First()}");

        // Paso 5: Verificar que las Observations se crearon
        var observations = await GetObservationsByPatient(patient.Id!);
        observations.Should().HaveCount(3, "deben haberse creado 3 observaciones (Hemoglobin, WBC, Platelets)");
        _output.WriteLine($"✓ {observations.Count} Observations creadas");

        // Verificar Hemoglobin
        var hemoglobin = observations.FirstOrDefault(o =>
            o.Code?.Coding?.Any(c => c.Code == "718-7") ?? false);
        hemoglobin.Should().NotBeNull("debe existir la observación de Hemoglobin");
        hemoglobin!.Value.Should().BeOfType<Quantity>();
        ((Quantity)hemoglobin.Value!).Value.Should().Be(14.5m);
        ((Quantity)hemoglobin.Value!).Unit.Should().Be("g/dL");
        hemoglobin.Status.Should().Be(ObservationStatus.Final);
        _output.WriteLine($"  - Hemoglobin: {((Quantity)hemoglobin.Value).Value} {((Quantity)hemoglobin.Value).Unit}");

        // Verificar WBC
        var wbc = observations.FirstOrDefault(o =>
            o.Code?.Coding?.Any(c => c.Code == "6690-2") ?? false);
        wbc.Should().NotBeNull("debe existir la observación de WBC");
        ((Quantity)wbc!.Value!).Value.Should().Be(7500m);
        _output.WriteLine($"  - WBC: {((Quantity)wbc.Value).Value} {((Quantity)wbc.Value).Unit}");

        // Verificar Platelets
        var platelets = observations.FirstOrDefault(o =>
            o.Code?.Coding?.Any(c => c.Code == "777-3") ?? false);
        platelets.Should().NotBeNull("debe existir la observación de Platelets");
        ((Quantity)platelets!.Value!).Value.Should().Be(250000m);
        _output.WriteLine($"  - Platelets: {((Quantity)platelets.Value).Value} {((Quantity)platelets.Value).Unit}");

        // Paso 6: Verificar que el DiagnosticReport se creó
        var reports = await GetDiagnosticReportsByPatient(patient.Id!);
        reports.Should().HaveCountGreaterOrEqualTo(1, "debe haberse creado al menos 1 DiagnosticReport");

        var cbcReport = reports.FirstOrDefault(r =>
            r.Code?.Coding?.Any(c => c.Code == "58410-2") ?? false);
        cbcReport.Should().NotBeNull("debe existir el DiagnosticReport del CBC panel");
        cbcReport!.Status.Should().Be(DiagnosticReport.DiagnosticReportStatus.Final);
        cbcReport.Result.Should().HaveCount(3, "el reporte debe referenciar las 3 observaciones");
        _output.WriteLine($"✓ DiagnosticReport creado: {cbcReport.Id} (CBC panel)");
        _output.WriteLine($"  - Referencias: {cbcReport.Result.Count} observaciones");

        _output.WriteLine("\n=== TEST EXITOSO ✓ ===");
    }

    // ========================================
    // HELPER METHODS
    // ========================================

    /// <summary>
    /// Crea el Host de LabBridge con todos los servicios configurados
    /// </summary>
    private IHost CreateLabBridgeHost()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "MllpListener:Port", "2575" },
                { "RabbitMq:Hostname", "localhost" },
                { "RabbitMq:Port", "5672" },
                { "RabbitMq:Username", "guest" },
                { "RabbitMq:Password", "guest" },
                { "FhirApi:BaseUrl", "http://localhost:8080" },
                { "Database:ConnectionString", "Host=localhost;Database=labbridge_audit;Username=labbridge;Password=labbridge123;Port=5432" }
            })
            .Build();

        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Configuración
                services.AddSingleton<IConfiguration>(configuration);

                // HL7 services
                services.AddSingleton<IHL7Parser, NHapiParser>();
                services.AddSingleton<IAckGenerator, AckGenerator>();
                services.AddSingleton<IMllpServer, MllpServer>();

                // FHIR services
                services.AddSingleton<IHL7ToFhirTransformer, FhirTransformer>();

                // Database and Audit Logging
                var connectionString = configuration["Database:ConnectionString"]!;
                services.AddDbContext<AuditDbContext>(options =>
                    options.UseNpgsql(connectionString));
                services.AddScoped<IAuditLogger, AuditLogger>();

                // Messaging services
                services.AddSingleton<IMessageQueue, RabbitMqQueue>();

                // FHIR API client (Refit + Polly + FhirHttpContentSerializer)
                var refitSettings = new RefitSettings
                {
                    ContentSerializer = new FhirHttpContentSerializer()
                };

                services
                    .AddRefitClient<ILabFlowApi>(refitSettings)
                    .ConfigureHttpClient(c => c.BaseAddress = new Uri("http://localhost:8080"))
                    .AddPolicyHandler(GetRetryPolicy());

                services.AddSingleton<IFhirClient, LabFlowClient>();

                // Background workers
                services.AddHostedService<Service.MllpListenerWorker>();
                services.AddHostedService<Service.MessageProcessorWorker>();

                // Logging
                services.AddLogging();
            })
            .Build();
    }

    /// <summary>
    /// Polly retry policy para FHIR API calls
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    /// <summary>
    /// Espera hasta que un servicio HTTP esté disponible
    /// </summary>
    private async System.Threading.Tasks.Task WaitForService(string url, TimeSpan timeout)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Ignorar errores y reintentar
            }

            await System.Threading.Tasks.Task.Delay(1000);
        }

        throw new TimeoutException($"Service at {url} did not become available within {timeout.TotalSeconds} seconds");
    }


    /// <summary>
    /// Obtiene un Patient de LabFlow API por identifier
    /// </summary>
    private async Task<Patient?> GetPatientByIdentifier(string identifier)
    {
        var response = await _labFlowHttpClient!.GetAsync($"/Patient?identifier={identifier}");
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var bundle = _fhirParser.Parse<Bundle>(json);
        return bundle?.Entry?.FirstOrDefault()?.Resource as Patient;
    }

    /// <summary>
    /// Obtiene todas las Observations de un Patient
    /// </summary>
    private async Task<List<Observation>> GetObservationsByPatient(string patientId)
    {
        var response = await _labFlowHttpClient!.GetAsync($"/Observation?subject=Patient/{patientId}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var bundle = _fhirParser.Parse<Bundle>(json);
        return bundle?.Entry?.Select(e => e.Resource).OfType<Observation>().ToList() ?? new List<Observation>();
    }

    /// <summary>
    /// Obtiene todos los DiagnosticReports de un Patient
    /// </summary>
    private async Task<List<DiagnosticReport>> GetDiagnosticReportsByPatient(string patientId)
    {
        var response = await _labFlowHttpClient!.GetAsync($"/DiagnosticReport?subject=Patient/{patientId}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var bundle = _fhirParser.Parse<Bundle>(json);
        return bundle?.Entry?.Select(e => e.Resource).OfType<DiagnosticReport>().ToList() ?? new List<DiagnosticReport>();
    }
}
