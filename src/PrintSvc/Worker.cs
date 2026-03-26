using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrintSvc.Contracts;
using PrintSvc.Settings;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PrintSvc;

public class Worker : BackgroundService
{
    private readonly BrokerSettings _broker;
    private readonly StorageSettings _storage;
    private readonly ILogger<Worker> _logger;

    private IConnection? _connection;
    private IChannel? _channel;
    public Worker(
        IOptions<BrokerSettings> brokerOptions, 
        IOptions<StorageSettings> storageOptions,
        ILogger<Worker> logger)
    {
        _broker = brokerOptions.Value;
        _storage = storageOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _broker.Host,
            Port = _broker.Port,
            UserName = _broker.Username,
            Password = _broker.Password
        };

        _connection = await factory.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();

        await _channel.QueueDeclareAsync(queue: _broker.JobsQueue,
                             durable: true,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        
        consumer.ReceivedAsync += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            
            _logger.LogDebug("Raw Message : {Message}", message);

            try 
            {
                Jobs? job = JsonSerializer.Deserialize<Jobs>(message);

                if (job != null)
                {
                    _logger.LogInformation($"Received:\n\t - Job ID: {job.jobId}\n\t - Batch Id: {job.batchId}\n\t - Photo Storage Key: {job.photoStorageKey}\n\t - Copies: {job.copies}");
                }
                await _channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error happened");
                await _channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);

                throw new NotImplementedException("Not yet implemented");
                
                // TODO :
                // - Retry if printer error
                // - Do nothing if message malformed
            }
        };

        await _channel.BasicConsumeAsync(queue: _broker.JobsQueue,
                             autoAck: false,
                             consumer: consumer);

        _logger.LogInformation("Waiting for messages on {Queue}...", _broker.JobsQueue);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}
