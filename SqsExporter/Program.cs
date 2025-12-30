using Amazon.SQS;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using SqsExporter;

var builder = Host.CreateApplicationBuilder(args);

// Bind configuration
builder.Services.Configure<SqsExporterOptions>(
    builder.Configuration.GetSection(SqsExporterOptions.SectionName));
builder.Services.Configure<OtlpOptions>(
    builder.Configuration.GetSection(OtlpOptions.SectionName));

// Configure AWS SQS client
var sqsOptions = builder.Configuration
    .GetSection(SqsExporterOptions.SectionName)
    .Get<SqsExporterOptions>();

var sqsConfig = new AmazonSQSConfig();
if (!string.IsNullOrEmpty(sqsOptions?.AwsRegion))
{
    sqsConfig.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(sqsOptions.AwsRegion);
}

if (!string.IsNullOrEmpty(sqsOptions?.AwsAccessKeyId) &&
    !string.IsNullOrEmpty(sqsOptions?.AwsSecretAccessKey))
{
    builder.Services.AddSingleton<IAmazonSQS>(_ =>
        new AmazonSQSClient(sqsOptions.AwsAccessKeyId, sqsOptions.AwsSecretAccessKey, sqsConfig));
}
else
{
    // Use default credentials (IAM role, environment variables, etc.)
    builder.Services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(sqsConfig));
}

// Configure OpenTelemetry
var otlpOptions = builder.Configuration
    .GetSection(OtlpOptions.SectionName)
    .Get<OtlpOptions>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
    {
        resource.AddService(serviceName: otlpOptions?.ServiceName ?? "sqs-exporter");

        if (!string.IsNullOrWhiteSpace(otlpOptions?.DeploymentEnvironment))
        {
            // SigNoz commonly uses "deployment.environment". Also emit the newer semantic convention key.
            resource.AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment", otlpOptions.DeploymentEnvironment!),
                new KeyValuePair<string, object>("deployment.environment.name", otlpOptions.DeploymentEnvironment!)
            ]);
        }
    })
    .WithMetrics(metrics => metrics
        .AddMeter(SqsMetrics.MeterName)
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpOptions?.Endpoint ?? "http://localhost:4317");
        }));

builder.Services.AddSingleton<ISqsMetrics, SqsMetrics>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
