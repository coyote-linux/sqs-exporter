using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace SqsExporter;

public interface ISqsMetrics
{
    void RecordQueueMetrics(string queueUrl, string queueName, long messagesAvailable, long messagesNotVisible, long messagesDelayed);
}

public class SqsMetrics : ISqsMetrics
{
    public const string MeterName = "SqsExporter";

    private readonly ObservableGauge<long> _approximateNumberOfMessages;
    private readonly ObservableGauge<long> _approximateNumberOfMessagesNotVisible;
    private readonly ObservableGauge<long> _approximateNumberOfMessagesDelayed;

    private readonly ConcurrentDictionary<QueueKey, QueueState> _state = new();

    public SqsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _approximateNumberOfMessages = meter.CreateObservableGauge<long>(
            name: "sqs_approximate_number_of_messages",
            observeValues: ObserveAvailable,
            unit: "{messages}",
            description: "Approximate number of messages available for retrieval from the queue");

        _approximateNumberOfMessagesNotVisible = meter.CreateObservableGauge<long>(
            name: "sqs_approximate_number_of_messages_not_visible",
            observeValues: ObserveNotVisible,
            unit: "{messages}",
            description: "Approximate number of messages that are in flight (sent but not yet deleted or timed out)");

        _approximateNumberOfMessagesDelayed = meter.CreateObservableGauge<long>(
            name: "sqs_approximate_number_of_messages_delayed",
            observeValues: ObserveDelayed,
            unit: "{messages}",
            description: "Approximate number of messages that are delayed and not available for reading");
    }

    public void RecordQueueMetrics(string queueUrl, string queueName, long messagesAvailable, long messagesNotVisible, long messagesDelayed)
    {
        var key = new QueueKey(queueUrl, queueName);
        _state[key] = new QueueState(messagesAvailable, messagesNotVisible, messagesDelayed);
    }

    private IEnumerable<Measurement<long>> ObserveAvailable()
        => Observe(state => state.MessagesAvailable);

    private IEnumerable<Measurement<long>> ObserveNotVisible()
        => Observe(state => state.MessagesNotVisible);

    private IEnumerable<Measurement<long>> ObserveDelayed()
        => Observe(state => state.MessagesDelayed);

    private IEnumerable<Measurement<long>> Observe(Func<QueueState, long> selector)
    {
        foreach (var kvp in _state)
        {
            var tags = new TagList
            {
                { "queue_url", kvp.Key.QueueUrl },
                { "queue_name", kvp.Key.QueueName }
            };

            yield return new Measurement<long>(selector(kvp.Value), tags);
        }
    }

    private readonly record struct QueueKey(string QueueUrl, string QueueName);

    private readonly record struct QueueState(long MessagesAvailable, long MessagesNotVisible, long MessagesDelayed);
}
