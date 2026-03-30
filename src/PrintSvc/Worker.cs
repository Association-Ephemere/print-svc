using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrintSvc.Contracts;
using PrintSvc.Settings;
using PrintSvc.Storage;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PrintSvc;

public class Worker : BackgroundService
{
    private readonly BrokerSettings _broker;
    private readonly StorageSettings _storage;
    private readonly ILogger<Worker> _logger;
    private readonly IPhotoDownloader _downloader;

    private IConnection? _connection;
    private IChannel? _channel;
    public Worker(
        IOptions<BrokerSettings> brokerOptions, 
        IOptions<StorageSettings> storageOptions,
        ILogger<Worker> logger,
        IPhotoDownloader downloader)
    {
        _broker = brokerOptions.Value;
        _storage = storageOptions.Value;
        _logger = logger;
        _downloader = downloader;

    }

    internal static Jobs? DeserializeJob(string message, ILogger<Worker> logger = null) {
        try
        {
            Jobs? job = JsonSerializer.Deserialize<Jobs>(message);
            return job;
        }
        catch (Exception)
        {
            string errorText = $"Error while deserializing message: {message.Replace("\n", "")}";

            if (logger != null)
                logger.LogError(errorText);
            else
                Console.WriteLine("Error: " + errorText);
            return null;
        }
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

        consumer.ReceivedAsync += Consumer_ReceivedAsync;

        await _channel.BasicConsumeAsync(queue: _broker.JobsQueue,
                             autoAck: false,
                             consumer: consumer);

        _logger.LogInformation("Waiting for messages on {Queue}...", _broker.JobsQueue);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task Consumer_ReceivedAsync(object sender, BasicDeliverEventArgs @event) {
        var body = @event.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);

        _logger.LogDebug("Raw Message : {Message}", message);

        if (_channel == null)
        {
            // TODO: null channel, Issue #6
        }
        await _channel.BasicAckAsync(deliveryTag: @event.DeliveryTag, multiple: false);

        Jobs? job = DeserializeJob(message, _logger);

        if (job != null) {
            await _downloader.DownloadAsync(job.photoStorageKey);

            // TODO: Send Job to printer
        }

    }
}
