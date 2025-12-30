using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SqsExporter;

public interface ISqsMetrics
{
    void RecordQueueMetrics(string queueUrl, string queueName, long messagesAvailable, long messagesNotVisible, long messagesDelayed);
}

public class SqsMetrics : ISqsMetrics
{
    public const string MeterName = "SqsExporter";

    private readonly Gauge<long> _approximateNumberOfMessages;
    private readonly Gauge<long> _approximateNumberOfMessagesNotVisible;
    private readonly Gauge<long> _approximateNumberOfMessagesDelayed;

    public SqsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _approximateNumberOfMessages = meter.CreateGauge<long>(
            name: "sqs_approximate_number_of_messages",
            unit: "{messages}",
            description: "Approximate number of messages available for retrieval from the queue");

        _approximateNumberOfMessagesNotVisible = meter.CreateGauge<long>(
            name: "sqs_approximate_number_of_messages_not_visible",
            unit: "{messages}",
            description: "Approximate number of messages that are in flight (sent but not yet deleted or timed out)");

        _approximateNumberOfMessagesDelayed = meter.CreateGauge<long>(
            name: "sqs_approximate_number_of_messages_delayed",
            unit: "{messages}",
            description: "Approximate number of messages that are delayed and not available for reading");
    }

    public void RecordQueueMetrics(string queueUrl, string queueName, long messagesAvailable, long messagesNotVisible, long messagesDelayed)
    {
        var tags = new TagList
        {
            { "queue_url", queueUrl },
            { "queue_name", queueName }
        };

        _approximateNumberOfMessages.Record(messagesAvailable, tags);
        _approximateNumberOfMessagesNotVisible.Record(messagesNotVisible, tags);
        _approximateNumberOfMessagesDelayed.Record(messagesDelayed, tags);
    }
}
