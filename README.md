# sqs-exporter
SQS Telemetry Exporter for Signoz written in C#

## Configuration

Configuration is via `appsettings.json` / environment variables.

### Required

- `Otlp:Endpoint`: Your SigNoz / OTEL Collector OTLP endpoint (gRPC), e.g. `http://signoz-otel-collector:4317`

### Recommended

- `Otlp:DeploymentEnvironment`: Environment name (e.g. `production`, `stage`). Exported as OTEL resource attributes:
  - `deployment.environment`
  - `deployment.environment.name`

### Queues

You can either list queue URLs explicitly or enable auto-discovery.

- **Explicit list**: set `SqsExporter:QueueUrls` to an array of queue URLs.
- **Auto-discovery**: set `SqsExporter:AutoDiscoverQueues=true`.
  - Optionally set `SqsExporter:QueueNamePrefixes` to limit discovery (recommended for large accounts).
  - Discovered queues are **merged** with any configured `QueueUrls`.

### Scaling / rate limiting

- `SqsExporter:MaxConcurrentAwsCalls` limits concurrent `GetQueueAttributes` calls (useful when polling 100s of queues).
- `SqsExporter:QueueDiscoveryRefreshSeconds` controls how often discovery is refreshed.

## Metric dimensions

All SQS metrics are emitted with:

- `queue_name`
- `queue_url`
