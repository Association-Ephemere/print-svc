using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrintSvc.Contracts;
using PrintSvc.Settings;
using RabbitMQ.Client;

namespace PrintSvc.Publisher;

public sealed class ResultPublisher(IOptions<BrokerSettings> brokerOptions) : IResultPublisher
{
    private readonly BrokerSettings _broker = brokerOptions.Value;
    private bool _queueDeclared;

    public async Task PublishAsync(IChannel channel, Result result, CancellationToken ct = default)
    {
        if (!_queueDeclared)
        {
            await channel.QueueDeclareAsync(
                queue: _broker.ResultsQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);
            _queueDeclared = true;
        }

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
        await channel.BasicPublishAsync("", _broker.ResultsQueue, body, cancellationToken: ct);
    }
}
