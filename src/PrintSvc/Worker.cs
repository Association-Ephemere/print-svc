using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrintSvc.Contracts;
using PrintSvc.Printer;
using PrintSvc.Settings;
using PrintSvc.Storage;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PrintSvc;

public class Worker : BackgroundService
{
    private readonly BrokerSettings _broker;
    private readonly StorageSettings _storage;
    private readonly PrintingSettings _printing;
    private readonly ILogger<Worker> _logger;
    private readonly IPhotoDownloader _downloader;

    private IConnection? _connection;
    private IChannel? _channel;
    public Worker(
        IOptions<BrokerSettings> brokerOptions,
        IOptions<StorageSettings> storageOptions,
        IOptions<PrintingSettings> printingOptions,
        ILogger<Worker> logger,
        IPhotoDownloader downloader)
    {
        _broker = brokerOptions.Value;
        _storage = storageOptions.Value;
        _printing = printingOptions.Value;
        _logger = logger;
        _downloader = downloader;

    }

    internal static Job? DeserializeJob(string message, ILogger<Worker>? logger = null)
    {
        try
        {
            Job? job = JsonSerializer.Deserialize<Job>(message);
            return job;
        }
        catch (Exception ex)
        {
            string messagePreview = CreateMessagePreview(message);

            logger?.LogError(ex, "Error while deserializing broker message. Preview: {MessagePreview}", messagePreview);
            return null;
        }
    }

    private static string CreateMessagePreview(string message)
    {
        const int MaxPreviewLength = 256;
        string sanitized = message.Replace("\r", "").Replace("\n", "");

        if (sanitized.Length <= MaxPreviewLength)
            return sanitized;

        return sanitized.Substring(0, MaxPreviewLength) + "...";
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

    private async Task Consumer_ReceivedAsync(object sender, BasicDeliverEventArgs @event)
    {
        var body = @event.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);

        _logger.LogDebug("Raw Message : {Message}", message);

        var channel = _channel;

        if (channel == null)
        {
            _logger.LogError("RabbitMQ channel is not available.");
            return;
        }

        Job? job = DeserializeJob(message, _logger);

        if (job == null)
        {
            _logger.LogError("Rejecting message {DeliveryTag}: failed to deserialize job.", @event.DeliveryTag);
            await channel.BasicRejectAsync(deliveryTag: @event.DeliveryTag, requeue: false);
            return;
        }

        var photos = job.Photos;
        if (photos == null)
        {
            _logger.LogError("Rejecting message {DeliveryTag}: job contains null Photos collection.", @event.DeliveryTag);
            await channel.BasicRejectAsync(deliveryTag: @event.DeliveryTag, requeue: false);
            return;
        }

        var photoCount = photos.Count;
        if (job.StartFromIndex < 0 || job.StartFromIndex > photoCount)
        {
            _logger.LogError(
                "Rejecting message {DeliveryTag}: StartFromIndex {StartFromIndex} is out of bounds for Photos count {PhotoCount}.",
                @event.DeliveryTag,
                job.StartFromIndex,
                photoCount);
            await channel.BasicRejectAsync(deliveryTag: @event.DeliveryTag, requeue: false);
            return;
        }

        foreach (JobPhoto photo in photos.Skip(job.StartFromIndex))
        {
            bool res = await _downloader.DownloadAsync(job, photo, channel: channel);

            if (res == true)
            {
                SendDownloadedPhotoToPrinter(photo);
            }
        }

        await channel.BasicAckAsync(deliveryTag: @event.DeliveryTag, multiple: false);
    }

    private void SendDownloadedPhotoToPrinter(JobPhoto photo)
    {
        string fileName = Path.GetFileName(photo.PhotoStorageKey);
        string downloadedPhotoPath = Path.Combine(_storage.TempDirectory, fileName);

        _logger.LogInformation("Sending photo {FileName} to printer.", fileName);

        new FileInfo(downloadedPhotoPath).Print(
            _printing.PrinterName,
            photo.Copies,
            _printing.PaperWidthInches,
            _printing.PaperHeightInches);

        if (File.Exists(downloadedPhotoPath))
        {
            File.Delete(downloadedPhotoPath);
            _logger.LogDebug("Deleted printed photo {FileName}.", fileName);
        }
    }
}
