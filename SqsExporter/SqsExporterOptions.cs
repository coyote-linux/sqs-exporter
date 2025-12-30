namespace SqsExporter;

public class SqsExporterOptions
{
    public const string SectionName = "SqsExporter";

    public List<string> QueueUrls { get; set; } = [];
    public int PollingIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// If true, queues are automatically enumerated via SQS ListQueues and merged with any configured QueueUrls.
    /// </summary>
    public bool AutoDiscoverQueues { get; set; } = false;

    /// <summary>
    /// Optional queue name prefixes used for discovery. If empty and AutoDiscoverQueues is true, all queues are enumerated.
    /// </summary>
    public List<string> QueueNamePrefixes { get; set; } = [];

    /// <summary>
    /// How often (in seconds) to refresh queue discovery when AutoDiscoverQueues is enabled.
    /// </summary>
    public int QueueDiscoveryRefreshSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum number of concurrent AWS API calls (GetQueueAttributes) during polling.
    /// </summary>
    public int MaxConcurrentAwsCalls { get; set; } = 20;

    public string? AwsRegion { get; set; }
    public string? AwsAccessKeyId { get; set; }
    public string? AwsSecretAccessKey { get; set; }
}

public class OtlpOptions
{
    public const string SectionName = "Otlp";

    public required string Endpoint { get; set; }
    public string ServiceName { get; set; } = "sqs-exporter";

    /// <summary>
    /// Deployment environment (e.g. production, stage). Exported as an OpenTelemetry resource attribute.
    /// </summary>
    public string? DeploymentEnvironment { get; set; }
}
