using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace SqsExporter.Tests;

public class WorkerTests
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ISqsMetrics _metrics;
    private readonly IOptions<SqsExporterOptions> _options;

    public WorkerTests()
    {
        _sqsClient = Substitute.For<IAmazonSQS>();
        _metrics = Substitute.For<ISqsMetrics>();
        _options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [],
            PollingIntervalSeconds = 1
        });
    }

    [Fact]
    public async Task ExecuteAsync_WithNoQueues_LogsWarningAndReturns()
    {
        // Arrange
        var worker = CreateWorker(_options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(cts.Token);

        // Assert
        await _sqsClient.DidNotReceive().GetQueueAttributesAsync(
            Arg.Any<GetQueueAttributesRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithQueues_PollsEachQueue()
    {
        // Arrange
        var options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [
                "https://sqs.us-east-1.amazonaws.com/123456789/queue-1",
                "https://sqs.us-east-1.amazonaws.com/123456789/queue-2"
            ],
            PollingIntervalSeconds = 60,
            MaxConcurrentAwsCalls = 10
        });

        _sqsClient.GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "10",
                    ["ApproximateNumberOfMessagesNotVisible"] = "5",
                    ["ApproximateNumberOfMessagesDelayed"] = "2"
                }
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(cts.Token);

        // Assert
        await _sqsClient.Received(2).GetQueueAttributesAsync(
            Arg.Any<GetQueueAttributesRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RecordsMetricsForEachQueue()
    {
        // Arrange
        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/my-queue";
        var options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [queueUrl],
            PollingIntervalSeconds = 60,
            MaxConcurrentAwsCalls = 10
        });

        _sqsClient.GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "100",
                    ["ApproximateNumberOfMessagesNotVisible"] = "25",
                    ["ApproximateNumberOfMessagesDelayed"] = "10"
                }
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(cts.Token);

        // Assert
        _metrics.Received(1).RecordQueueMetrics(
            queueUrl,
            "my-queue",
            100,
            25,
            10);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingAttributes_DefaultsToZero()
    {
        // Arrange
        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/my-queue";
        var options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [queueUrl],
            PollingIntervalSeconds = 60,
            MaxConcurrentAwsCalls = 10
        });

        _sqsClient.GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "50"
                    // Missing other attributes
                }
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(cts.Token);

        // Assert
        _metrics.Received(1).RecordQueueMetrics(
            queueUrl,
            "my-queue",
            50,
            0,  // Default for missing attribute
            0); // Default for missing attribute
    }

    [Fact]
    public async Task ExecuteAsync_WhenQueueDoesNotExist_ContinuesPollingOtherQueues()
    {
        // Arrange
        var existingQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/existing-queue";
        var missingQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/missing-queue";
        var options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [missingQueueUrl, existingQueueUrl],
            PollingIntervalSeconds = 60,
            MaxConcurrentAwsCalls = 10
        });

        _sqsClient.GetQueueAttributesAsync(
            Arg.Is<GetQueueAttributesRequest>(r => r.QueueUrl == missingQueueUrl),
            Arg.Any<CancellationToken>())
            .Throws(new QueueDoesNotExistException("Queue not found"));

        _sqsClient.GetQueueAttributesAsync(
            Arg.Is<GetQueueAttributesRequest>(r => r.QueueUrl == existingQueueUrl),
            Arg.Any<CancellationToken>())
            .Returns(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "10",
                    ["ApproximateNumberOfMessagesNotVisible"] = "0",
                    ["ApproximateNumberOfMessagesDelayed"] = "0"
                }
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(cts.Token);

        // Assert - metrics should still be recorded for the existing queue
        _metrics.Received(1).RecordQueueMetrics(
            existingQueueUrl,
            "existing-queue",
            10,
            0,
            0);

        // No metrics for the missing queue
        _metrics.DidNotReceive().RecordQueueMetrics(
            missingQueueUrl,
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<long>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionOccurs_ContinuesPolling()
    {
        // Arrange
        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/my-queue";
        var options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [queueUrl],
            PollingIntervalSeconds = 60,
            MaxConcurrentAwsCalls = 10
        });

        _sqsClient.GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>())
            .Throws(new AmazonSQSException("Network error"));

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(cts.Token);

        // Assert - no metrics recorded due to error
        _metrics.DidNotReceive().RecordQueueMetrics(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<long>());
    }

    [Fact]
    public async Task ExecuteAsync_RequestsCorrectAttributes()
    {
        // Arrange
        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/my-queue";
        var options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [queueUrl],
            PollingIntervalSeconds = 60,
            MaxConcurrentAwsCalls = 10
        });

        GetQueueAttributesRequest? capturedRequest = null;
        _sqsClient.GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedRequest = call.Arg<GetQueueAttributesRequest>();
                return new GetQueueAttributesResponse
                {
                    Attributes = new Dictionary<string, string>()
                };
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(cts.Token);

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(queueUrl, capturedRequest.QueueUrl);
        Assert.Contains("ApproximateNumberOfMessages", capturedRequest.AttributeNames);
        Assert.Contains("ApproximateNumberOfMessagesNotVisible", capturedRequest.AttributeNames);
        Assert.Contains("ApproximateNumberOfMessagesDelayed", capturedRequest.AttributeNames);
    }

    [Theory]
    [InlineData("https://sqs.us-east-1.amazonaws.com/123456789/my-queue", "my-queue")]
    [InlineData("https://sqs.eu-west-1.amazonaws.com/987654321/production-orders", "production-orders")]
    [InlineData("https://sqs.us-east-1.amazonaws.com/123456789/queue-with-dashes", "queue-with-dashes")]
    public async Task ExecuteAsync_ExtractsQueueNameCorrectly(string queueUrl, string expectedQueueName)
    {
        // Arrange
        var options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [queueUrl],
            PollingIntervalSeconds = 60,
            MaxConcurrentAwsCalls = 10
        });

        _sqsClient.GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "0",
                    ["ApproximateNumberOfMessagesNotVisible"] = "0",
                    ["ApproximateNumberOfMessagesDelayed"] = "0"
                }
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(cts.Token);

        // Assert
        _metrics.Received(1).RecordQueueMetrics(
            queueUrl,
            expectedQueueName,
            Arg.Any<long>(),
            Arg.Any<long>(),
            Arg.Any<long>());
    }

    private Worker CreateWorker(IOptions<SqsExporterOptions> options)
    {
        return new Worker(
            NullLogger<Worker>.Instance,
            _sqsClient,
            _metrics,
            options);
    }

    [Fact]
    public async Task ExecuteAsync_WithAutoDiscoveryAndNoConfiguredQueues_PollsDiscoveredQueues()
    {
        // Arrange
        var discoveredQueue1 = "https://sqs.us-east-1.amazonaws.com/123456789/discovered-1";
        var discoveredQueue2 = "https://sqs.us-east-1.amazonaws.com/123456789/discovered-2";

        var options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [],
            AutoDiscoverQueues = true,
            QueueDiscoveryRefreshSeconds = 300,
            PollingIntervalSeconds = 60,
            MaxConcurrentAwsCalls = 10
        });

        _sqsClient.ListQueuesAsync(Arg.Any<ListQueuesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListQueuesResponse
            {
                QueueUrls = [discoveredQueue1, discoveredQueue2]
            });

        _sqsClient.GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "0",
                    ["ApproximateNumberOfMessagesNotVisible"] = "0",
                    ["ApproximateNumberOfMessagesDelayed"] = "0"
                }
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(cts.Token);

        // Assert
        await _sqsClient.Received(1).ListQueuesAsync(Arg.Any<ListQueuesRequest>(), Arg.Any<CancellationToken>());
        await _sqsClient.Received(2).GetQueueAttributesAsync(Arg.Any<GetQueueAttributesRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithAutoDiscoveryPrefixes_UsesPrefixRequests()
    {
        // Arrange
        var options = Options.Create(new SqsExporterOptions
        {
            QueueUrls = [],
            AutoDiscoverQueues = true,
            QueueNamePrefixes = ["prod-", "stage-"],
            QueueDiscoveryRefreshSeconds = 300,
            PollingIntervalSeconds = 60,
            MaxConcurrentAwsCalls = 10
        });

        _sqsClient.ListQueuesAsync(Arg.Any<ListQueuesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListQueuesResponse
            {
                QueueUrls = []
            });

        var worker = CreateWorker(options);
        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(200);
        await worker.StopAsync(cts.Token);

        // Assert
        await _sqsClient.Received(1).ListQueuesAsync(
            Arg.Is<ListQueuesRequest>(r => r.QueueNamePrefix == "prod-"),
            Arg.Any<CancellationToken>());

        await _sqsClient.Received(1).ListQueuesAsync(
            Arg.Is<ListQueuesRequest>(r => r.QueueNamePrefix == "stage-"),
            Arg.Any<CancellationToken>());
    }
}
