using PrintSvc.Contracts;
using RabbitMQ.Client;

namespace PrintSvc.Publisher;

public interface IResultPublisher
{
    Task PublishAsync(IChannel channel, Result result, CancellationToken ct = default);
}
