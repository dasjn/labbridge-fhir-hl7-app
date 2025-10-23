using LabBridge.Core.Interfaces;
using LabBridge.Infrastructure.FHIR;
using LabBridge.Infrastructure.HL7;
using LabBridge.Infrastructure.Messaging;
using LabBridge.Service;
using Polly;
using Polly.Extensions.Http;
using Refit;

var builder = Host.CreateApplicationBuilder(args);

// Register HL7 services
builder.Services.AddSingleton<IHL7Parser, NHapiParser>();
builder.Services.AddSingleton<IAckGenerator, AckGenerator>();
builder.Services.AddSingleton<IMllpServer, MllpServer>();

// Register FHIR services
builder.Services.AddSingleton<IHL7ToFhirTransformer, FhirTransformer>();

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

var host = builder.Build();
host.Run();

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
