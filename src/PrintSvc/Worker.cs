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
        await channel.BasicAckAsync(deliveryTag: @event.DeliveryTag, multiple: false);


        Job? job = DeserializeJob(message, _logger);


        if (job != null)
        {
            int totalToPrint = job.Photos.Skip(job.StartFromIndex).Sum(p => p.Copies);
            int printed = 0;

            foreach (JobPhoto photo in job.Photos.Skip(job.StartFromIndex))
            {
                bool res = await _downloader.DownloadAsync(job, photo, channel: channel);

                if (res == true)
                {
                    try
                    {
                        SendDownloadedPhotoToPrinter(photo);
                        printed += photo.Copies;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while printing {PhotoStorageKey} for job {JobId}.", photo.PhotoStorageKey, job.JobId);

                        Result result = CreateErrorResult(
                            jobId: job.JobId,
                            printed: printed,
                            total: totalToPrint,
                            error: $"Error while printing the photo: {Path.GetFileName(photo.PhotoStorageKey)}");

                        await PublishResultAsync(channel, result);
                        break;
                    }
                }

            }
        }

    }

    internal static Result CreateErrorResult(string jobId, int printed, int total, string error)
    {
        return new Result
        {
            JobId = jobId,
            Status = "error",
            Printed = printed,
            Total = total,
            Error = error
        };
    }

    private async Task PublishResultAsync(IChannel channel, Result result)
    {
        await channel.QueueDeclareAsync(queue: _broker.ResultsQueue,
                             durable: true,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null,
                             cancellationToken: default);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
        await channel.BasicPublishAsync("", _broker.ResultsQueue, body, cancellationToken: default);
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
