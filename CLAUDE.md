# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SQS Telemetry Exporter for Signoz - a C# application that exports AWS SQS telemetry data to Signoz for observability.

## Build and Development Commands

```bash
# Build the solution
dotnet build

# Run the worker service
dotnet run --project SqsExporter

# Run all tests
dotnet test

# Run a specific test
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Build for release
dotnet publish -c Release
```

## Architecture

- **SqsExporter/** - Worker Service that polls SQS queues and exports telemetry to Signoz via OTLP
- **SqsExporter.Tests/** - xUnit test project

### Key Files

- `Worker.cs` - Background service that polls SQS queues on a configurable interval
- `SqsMetrics.cs` - OpenTelemetry metrics instruments (gauges for queue depth)
- `SqsExporterOptions.cs` - Configuration models for SQS and OTLP settings

### Metrics Exported

- `sqs_approximate_number_of_messages` - Messages available for retrieval
- `sqs_approximate_number_of_messages_not_visible` - Messages in flight
- `sqs_approximate_number_of_messages_delayed` - Delayed messages

### Configuration

Configure via `appsettings.json` or environment variables:

```json
{
  "SqsExporter": {
    "QueueUrls": ["https://sqs.us-east-1.amazonaws.com/123456789/my-queue"],
    "PollingIntervalSeconds": 60,
    "AwsRegion": "us-east-1"
  },
  "Otlp": {
    "Endpoint": "http://localhost:4317",
    "ServiceName": "sqs-exporter"
  }
}
```

AWS credentials can be set via `AwsAccessKeyId`/`AwsSecretAccessKey` config or standard AWS credential chain (environment variables, IAM role, etc.).
