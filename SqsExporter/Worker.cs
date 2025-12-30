using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;

namespace SqsExporter;

public class Worker(
    ILogger<Worker> logger,
    IAmazonSQS sqsClient,
    ISqsMetrics metrics,
    IOptions<SqsExporterOptions> options) : BackgroundService
{
    private static readonly List<string> QueueAttributeNames =
    [
        "ApproximateNumberOfMessages",
        "ApproximateNumberOfMessagesNotVisible",
        "ApproximateNumberOfMessagesDelayed"
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        var configuredQueueUrls = config.QueueUrls ?? [];

        if (configuredQueueUrls.Count == 0 && !config.AutoDiscoverQueues)
        {
            logger.LogWarning("No SQS queue URLs configured. Add queue URLs to the SqsExporter:QueueUrls configuration.");
            return;
        }

        logger.LogInformation(
            "Starting SQS exporter. Polling interval={IntervalSeconds}s, AutoDiscoverQueues={AutoDiscoverQueues}, MaxConcurrentAwsCalls={MaxConcurrentAwsCalls}",
            config.PollingIntervalSeconds,
            config.AutoDiscoverQueues,
            config.MaxConcurrentAwsCalls);

        var currentQueueUrls = new List<string>(configuredQueueUrls);
        var lastDiscoveryUtc = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (config.AutoDiscoverQueues &&
                (DateTimeOffset.UtcNow - lastDiscoveryUtc) >= TimeSpan.FromSeconds(Math.Max(1, config.QueueDiscoveryRefreshSeconds)))
            {
                var discovered = await DiscoverQueueUrlsAsync(config, stoppingToken);
                var merged = new HashSet<string>(StringComparer.Ordinal);
                foreach (var url in configuredQueueUrls)
                {
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        merged.Add(url);
                    }
                }

                foreach (var url in discovered)
                {
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        merged.Add(url);
                    }
                }

                currentQueueUrls = merged.ToList();
                lastDiscoveryUtc = DateTimeOffset.UtcNow;

                logger.LogInformation(
                    "Queue discovery refresh complete. Configured={ConfiguredCount}, Discovered={DiscoveredCount}, Total={TotalCount}",
                    configuredQueueUrls.Count,
                    discovered.Count,
                    currentQueueUrls.Count);
            }

            if (currentQueueUrls.Count == 0)
            {
                logger.LogWarning(
                    "No queues to poll (configured=0 and discovery returned 0). Next discovery refresh in {RefreshSeconds}s.",
                    config.QueueDiscoveryRefreshSeconds);
            }
            else
            {
                await PollQueuesAsync(currentQueueUrls, config.MaxConcurrentAwsCalls, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(config.PollingIntervalSeconds), stoppingToken);
        }
    }

    private async Task PollQueuesAsync(List<string> queueUrls, int maxConcurrentAwsCalls, CancellationToken cancellationToken)
    {
        var maxConcurrency = Math.Max(1, maxConcurrentAwsCalls);
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = queueUrls.Select(async url =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await PollQueueAsync(url, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task PollQueueAsync(string queueUrl, CancellationToken cancellationToken)
    {
        try
        {
            var request = new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = QueueAttributeNames
            };

            var response = await sqsClient.GetQueueAttributesAsync(request, cancellationToken);
            var queueName = ExtractQueueName(queueUrl);

            var messagesAvailable = GetAttributeValue(response.Attributes, "ApproximateNumberOfMessages");
            var messagesNotVisible = GetAttributeValue(response.Attributes, "ApproximateNumberOfMessagesNotVisible");
            var messagesDelayed = GetAttributeValue(response.Attributes, "ApproximateNumberOfMessagesDelayed");

            metrics.RecordQueueMetrics(queueUrl, queueName, messagesAvailable, messagesNotVisible, messagesDelayed);

            logger.LogDebug("Queue {QueueName}: Available={Available}, NotVisible={NotVisible}, Delayed={Delayed}",
                queueName, messagesAvailable, messagesNotVisible, messagesDelayed);
        }
        catch (QueueDoesNotExistException)
        {
            logger.LogError("Queue does not exist: {QueueUrl}", queueUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error polling queue {QueueUrl}", queueUrl);
        }
    }

    private static long GetAttributeValue(Dictionary<string, string> attributes, string attributeName)
    {
        return attributes.TryGetValue(attributeName, out var value) && long.TryParse(value, out var result)
            ? result
            : 0;
    }

    private static string ExtractQueueName(string queueUrl)
    {
        // Queue URL format: https://sqs.{region}.amazonaws.com/{account-id}/{queue-name}
        var uri = new Uri(queueUrl);
        return uri.Segments.Length > 0 ? uri.Segments[^1].TrimEnd('/') : queueUrl;
    }

    private async Task<HashSet<string>> DiscoverQueueUrlsAsync(SqsExporterOptions config, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var prefixes = config.QueueNamePrefixes?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToList() ?? [];

        if (prefixes.Count == 0)
        {
            // List all queues (may be large; prefer prefixes when possible).
            var response = await sqsClient.ListQueuesAsync(new ListQueuesRequest(), cancellationToken);
            foreach (var url in response.QueueUrls ?? [])
            {
                result.Add(url);
            }

            return result;
        }

        foreach (var prefix in prefixes)
        {
            var response = await sqsClient.ListQueuesAsync(new ListQueuesRequest
            {
                QueueNamePrefix = prefix
            }, cancellationToken);

            foreach (var url in response.QueueUrls ?? [])
            {
                result.Add(url);
            }
        }

        return result;
    }
}
