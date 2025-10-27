using Prometheus;

namespace LabBridge.Infrastructure.Observability;

/// <summary>
/// Prometheus metrics definitions for LabBridge application.
///
/// Metrics categories:
/// - Counters: Total counts (messages received, processed, failed)
/// - Histograms: Duration distributions (processing time, API call time)
/// - Gauges: Current values (queue depth, active connections)
/// </summary>
public static class LabBridgeMetrics
{
    // ====================
    // COUNTERS (monotonic increasing values)
    // ====================

    /// <summary>
    /// Total number of HL7v2 messages received via MLLP.
    /// Labels: message_type (ORU^R01, ADT^A01, etc.)
    /// </summary>
    public static readonly Counter MessagesReceived = Metrics.CreateCounter(
        "labbridge_messages_received_total",
        "Total number of HL7v2 messages received via MLLP",
        new CounterConfiguration
        {
            LabelNames = new[] { "message_type" }
        });

    /// <summary>
    /// Total number of messages successfully processed and sent to FHIR API.
    /// Labels: message_type
    /// </summary>
    public static readonly Counter MessagesProcessedSuccess = Metrics.CreateCounter(
        "labbridge_messages_processed_success_total",
        "Total number of messages successfully processed and sent to FHIR API",
        new CounterConfiguration
        {
            LabelNames = new[] { "message_type" }
        });

    /// <summary>
    /// Total number of messages that failed processing.
    /// Labels: message_type, error_type (parse_error, transform_error, api_error, etc.)
    /// </summary>
    public static readonly Counter MessagesProcessedFailure = Metrics.CreateCounter(
        "labbridge_messages_processed_failure_total",
        "Total number of messages that failed processing",
        new CounterConfiguration
        {
            LabelNames = new[] { "message_type", "error_type" }
        });

    /// <summary>
    /// Total number of ACK messages sent back to analyzers.
    /// Labels: ack_code (AA=accept, AE=error, AR=reject)
    /// </summary>
    public static readonly Counter AcksSent = Metrics.CreateCounter(
        "labbridge_acks_sent_total",
        "Total number of ACK messages sent",
        new CounterConfiguration
        {
            LabelNames = new[] { "ack_code" }
        });

    /// <summary>
    /// Total number of FHIR API calls made.
    /// Labels: resource_type (Patient, Observation, DiagnosticReport), method (POST, PUT, GET)
    /// </summary>
    public static readonly Counter FhirApiCalls = Metrics.CreateCounter(
        "labbridge_fhir_api_calls_total",
        "Total number of FHIR API calls made",
        new CounterConfiguration
        {
            LabelNames = new[] { "resource_type", "method", "status_code" }
        });

    // ====================
    // HISTOGRAMS (duration distributions with buckets)
    // ====================

    /// <summary>
    /// Duration of message processing from RabbitMQ dequeue to FHIR API completion.
    /// Buckets: 100ms, 250ms, 500ms, 1s, 2.5s, 5s, 10s
    /// Labels: message_type
    /// </summary>
    public static readonly Histogram MessageProcessingDuration = Metrics.CreateHistogram(
        "labbridge_message_processing_duration_seconds",
        "Duration of message processing from RabbitMQ to FHIR API",
        new HistogramConfiguration
        {
            LabelNames = new[] { "message_type" },
            Buckets = new[] { 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0 }
        });

    /// <summary>
    /// Duration of FHIR API HTTP calls.
    /// Buckets: 50ms, 100ms, 250ms, 500ms, 1s, 2s, 5s
    /// Labels: resource_type, method
    /// </summary>
    public static readonly Histogram FhirApiCallDuration = Metrics.CreateHistogram(
        "labbridge_fhir_api_call_duration_seconds",
        "Duration of FHIR API HTTP calls",
        new HistogramConfiguration
        {
            LabelNames = new[] { "resource_type", "method" },
            Buckets = new[] { 0.05, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0 }
        });

    /// <summary>
    /// Duration of HL7v2 parsing operations.
    /// Buckets: 1ms, 5ms, 10ms, 25ms, 50ms, 100ms, 250ms
    /// Labels: message_type
    /// </summary>
    public static readonly Histogram Hl7ParsingDuration = Metrics.CreateHistogram(
        "labbridge_hl7_parsing_duration_seconds",
        "Duration of HL7v2 message parsing",
        new HistogramConfiguration
        {
            LabelNames = new[] { "message_type" },
            Buckets = new[] { 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25 }
        });

    // ====================
    // GAUGES (current values that can go up and down)
    // ====================

    /// <summary>
    /// Current number of active MLLP TCP connections.
    /// </summary>
    public static readonly Gauge ActiveMllpConnections = Metrics.CreateGauge(
        "labbridge_active_mllp_connections",
        "Current number of active MLLP TCP connections");

    /// <summary>
    /// Current RabbitMQ queue depth (messages waiting to be processed).
    /// Labels: queue_name
    /// </summary>
    public static readonly Gauge RabbitMqQueueDepth = Metrics.CreateGauge(
        "labbridge_rabbitmq_queue_depth",
        "Current number of messages in RabbitMQ queue",
        new GaugeConfiguration
        {
            LabelNames = new[] { "queue_name" }
        });

    /// <summary>
    /// Application uptime in seconds since last start.
    /// </summary>
    public static readonly Gauge ApplicationUptime = Metrics.CreateGauge(
        "labbridge_uptime_seconds",
        "Application uptime in seconds since last start");

    // ====================
    // SUMMARY (alternative to histogram with quantiles)
    // ====================

    /// <summary>
    /// End-to-end message latency from MLLP receipt to FHIR API completion.
    /// Tracks p50, p90, p95, p99 quantiles.
    /// </summary>
    public static readonly Summary E2EMessageLatency = Metrics.CreateSummary(
        "labbridge_e2e_message_latency_seconds",
        "End-to-end message latency from MLLP to FHIR API",
        new SummaryConfiguration
        {
            LabelNames = new[] { "message_type" },
            Objectives = new[]
            {
                new QuantileEpsilonPair(0.5, 0.05),   // p50 ± 5%
                new QuantileEpsilonPair(0.9, 0.05),   // p90 ± 5%
                new QuantileEpsilonPair(0.95, 0.01),  // p95 ± 1%
                new QuantileEpsilonPair(0.99, 0.01)   // p99 ± 1%
            }
        });
}
