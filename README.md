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

## systemd (Linux)

### Publish the app

Example (framework-dependent):

```bash
dotnet publish SqsExporter/SqsExporter.csproj -c Release -o /opt/sqs-exporter
```

### Install service + config

```bash
sudo useradd --system --no-create-home --shell /usr/sbin/nologin sqs-exporter || true
sudo install -d -o sqs-exporter -g sqs-exporter /etc/sqs-exporter /var/log/sqs-exporter

sudo install -m 0644 deploy/sqs-exporter.service /etc/systemd/system/sqs-exporter.service
sudo install -m 0640 deploy/sqs-exporter.env.example /etc/sqs-exporter/sqs-exporter.env
sudo chown root:sqs-exporter /etc/sqs-exporter/sqs-exporter.env

sudo systemctl daemon-reload
sudo systemctl enable --now sqs-exporter
```

Edit `/etc/sqs-exporter/sqs-exporter.env` and set at least `Otlp__Endpoint` and (optionally) `Otlp__DeploymentEnvironment`.
