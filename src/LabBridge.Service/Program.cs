using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.Data;
using LabBridge.Infrastructure.FHIR;
using LabBridge.Infrastructure.HL7;
using LabBridge.Infrastructure.Messaging;
using LabBridge.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Prometheus;
using Refit;

// Use WebApplicationBuilder to enable HTTP endpoints (for Prometheus /metrics)
var builder = WebApplication.CreateBuilder(args);

// Register HL7 services
builder.Services.AddSingleton<IHL7Parser, NHapiParser>();
builder.Services.AddSingleton<IAckGenerator, AckGenerator>();
builder.Services.AddSingleton<IMllpServer, MllpServer>();

// Register FHIR services
builder.Services.AddSingleton<IHL7ToFhirTransformer, FhirTransformer>();

// Register database and audit logging
var connectionString = builder.Configuration.GetValue<string>("Database:ConnectionString")
    ?? "Host=localhost;Database=labbridge_audit;Username=postgres;Password=dev_password;Port=5432";

builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IAuditLogger, AuditLogger>();

// Register messaging services
builder.Services.AddSingleton<IMessageQueue, RabbitMqQueue>();

// Register FHIR API client with Refit + Polly retry policies
var fhirApiBaseUrl = builder.Configuration.GetValue<string>("FhirApi:BaseUrl") ?? "http://localhost:5000";

// Configure Refit with custom FhirHttpContentSerializer for proper FHIR R4 serialization
var refitSettings = new RefitSettings
{
    ContentSerializer = new FhirHttpContentSerializer()
};

builder.Services
    .AddRefitClient<ILabFlowApi>(refitSettings)
    .ConfigureHttpClient(c => c.BaseAddress = new Uri(fhirApiBaseUrl))
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

builder.Services.AddSingleton<IFhirClient, LabFlowClient>();

// Register background workers
builder.Services.AddHostedService<MllpListenerWorker>();
builder.Services.AddHostedService<MessageProcessorWorker>();

var app = builder.Build();

// Configure Prometheus metrics endpoint
// Expose metrics at http://localhost:5000/metrics
app.UseRouting();
app.MapMetrics(); // Prometheus scraping endpoint

// Health check endpoint (optional)
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    service = "LabBridge"
}));

// Apply EF Core migrations on startup (for development)
// In production, migrations should be applied separately during deployment
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AuditDbContext>();
        context.Database.Migrate(); // Apply pending migrations
        Console.WriteLine("Database migrations applied successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error applying database migrations: {ex.Message}");
        // Don't crash the application - audit logging is important but not critical for message processing
    }
}

Console.WriteLine("LabBridge starting...");
Console.WriteLine("Prometheus metrics available at: http://localhost:5000/metrics");
Console.WriteLine("Health check available at: http://localhost:5000/health");

app.Run();

// Polly retry policy: Exponential backoff for transient errors
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError() // 5xx and 408 (timeout)
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests) // 429
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 2s, 4s, 8s
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Console.WriteLine($"Retry {retryAttempt} after {timespan.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
            });
}

// Circuit breaker: Stop trying if API is down
static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5, // Open circuit after 5 consecutive failures
            durationOfBreak: TimeSpan.FromSeconds(30), // Stay open for 30 seconds
            onBreak: (outcome, duration) =>
            {
                Console.WriteLine($"Circuit breaker opened for {duration.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
            },
            onReset: () =>
            {
                Console.WriteLine("Circuit breaker reset - API is healthy again");
            });
}
