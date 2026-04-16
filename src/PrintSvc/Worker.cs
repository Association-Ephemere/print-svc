using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrintSvc.Contracts;
using PrintSvc.Printer;
using PrintSvc.Publisher;
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
    private readonly IResultPublisher _publisher;

    private IConnection? _connection;
    private IChannel? _channel;
    private readonly SemaphoreSlim _processingGate = new(1, 1);
    private CancellationToken _stoppingToken;

    public Worker(
        IOptions<BrokerSettings> brokerOptions,
        IOptions<StorageSettings> storageOptions,
        IOptions<PrintingSettings> printingOptions,
        ILogger<Worker> logger,
        IPhotoDownloader downloader,
        IResultPublisher publisher)
    {
        _broker = brokerOptions.Value;
        _storage = storageOptions.Value;
        _printing = printingOptions.Value;
        _logger = logger;
        _downloader = downloader;
        _publisher = publisher;
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
        _stoppingToken = stoppingToken;

        var factory = new ConnectionFactory
        {
            HostName = _broker.Host,
            Port = _broker.Port,
            UserName = _broker.Username,
            Password = _broker.Password,
            AutomaticRecoveryEnabled = true
        };

        Task<IConnection> brokerTask = ConnectToBrokerAsync(factory, stoppingToken);
        Task storageTask = WaitForStorageAsync(stoppingToken);
        await Task.WhenAll(brokerTask, storageTask);

        _connection = brokerTask.Result;
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        _connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;

        await _channel.QueueDeclareAsync(
            queue: _broker.JobsQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += Consumer_ReceivedAsync;

        string consumerTag = await _channel.BasicConsumeAsync(
            queue: _broker.JobsQueue,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation("Waiting for messages on {Queue}...", _broker.JobsQueue);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
                await Task.Delay(1000, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _logger.LogInformation("Shutdown signal received, waiting for current job to complete...");
            if (_channel != null)
            {
                try { await _channel.BasicCancelAsync(consumerTag); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to cancel consumer {ConsumerTag}.", consumerTag); }
            }
            await _processingGate.WaitAsync();
            _processingGate.Release();
            _logger.LogInformation("Graceful shutdown complete.");
            if (_channel != null) await _channel.CloseAsync();
            if (_connection != null) await _connection.CloseAsync();
        }
    }

    private async Task<IConnection> ConnectToBrokerAsync(ConnectionFactory factory, CancellationToken ct)
    {
        int attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                IConnection connection = await factory.CreateConnectionAsync(ct);
                _logger.LogInformation("Connected to broker.");
                return connection;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                attempt++;
                int delay = ExponentialDelayMs(attempt);
                _logger.LogError(ex, "Failed to connect to broker (attempt {Attempt}). Retrying in {Delay}ms.", attempt, delay);
                await Task.Delay(delay, ct);
            }
        }
    }

    private async Task WaitForStorageAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _downloader.PingAsync(ct);
                _logger.LogInformation("Storage is available.");
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                attempt++;
                int delay = ExponentialDelayMs(attempt);
                _logger.LogError(ex, "Failed to reach storage (attempt {Attempt}). Retrying in {Delay}ms.", attempt, delay);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static int ExponentialDelayMs(int attempt) =>
        (int)Math.Min(1000 * Math.Pow(2, attempt - 1), 30_000);

    private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs args)
    {
        if (args.Initiator != ShutdownInitiator.Application)
            _logger.LogWarning("Broker connection lost: {Reason}. Automatic recovery in progress.", args.ReplyText);
        return Task.CompletedTask;
    }

    private async Task Consumer_ReceivedAsync(object sender, BasicDeliverEventArgs @event)
    {
        var body = @event.Body.ToArray();
        var message = Encoding.UTF8.GetString(body);

        _logger.LogDebug("Raw Message : {Message}", message);

        var channel = _channel;

        if (channel == null)
        {
            _logger.LogError("Dropping message {DeliveryTag}: channel unavailable during shutdown.", @event.DeliveryTag);
            return;
        }

        await _processingGate.WaitAsync();
        try
        {
            if (_stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Shutdown in progress, requeueing message {DeliveryTag}.", @event.DeliveryTag);
                await channel.BasicNackAsync(deliveryTag: @event.DeliveryTag, multiple: false, requeue: true);
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

            await channel.BasicAckAsync(deliveryTag: @event.DeliveryTag, multiple: false);
            await ProcessPhotosAsync(job, channel);
        }
        finally
        {
            _processingGate.Release();
        }
    }

    internal async Task ProcessPhotosAsync(Job job, IChannel channel)
    {
        int printed = job.StartFromIndex;
        bool hasError = false;

        foreach (JobPhoto photo in job.Photos.Skip(job.StartFromIndex))
        {
            DownloadResult downloadResult = await _downloader.DownloadAsync(job, photo);
            if (!downloadResult.Success)
            {
                hasError = true;
                await _publisher.PublishAsync(channel, new Result
                {
                    JobId = job.JobId,
                    Status = "error",
                    Printed = printed,
                    Total = job.Photos.Count,
                    Error = downloadResult.Error
                });
                continue;
            }

            string tempPath = Path.Combine(_storage.TempDirectory, Path.GetFileName(photo.PhotoStorageKey));
            try
            {
                SendDownloadedPhotoToPrinter(photo);
                printed++;

                await _publisher.PublishAsync(channel, new Result
                {
                    JobId = job.JobId,
                    Status = "printing",
                    Printed = printed,
                    Total = job.Photos.Count
                });
            }
            catch (Exception ex)
            {
                hasError = true;
                _logger.LogError(ex, "Failed to print photo {Key}.", photo.PhotoStorageKey);
                await _publisher.PublishAsync(channel, CreateErrorResult(job.JobId, printed, job.Photos.Count, ex.Message));
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                    _logger.LogDebug("Deleted temp photo {Path}.", tempPath);
                }
            }
        }

        if (!hasError)
        {
            await _publisher.PublishAsync(channel, new Result
            {
                JobId = job.JobId,
                Status = "done",
                Printed = printed,
                Total = job.Photos.Count
            });
        }
    }

    internal static Result CreateErrorResult(Guid jobId, int printed, int total, string error)
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
    }

    public override void Dispose()
    {
        _processingGate.Dispose();
        base.Dispose();
    }
}
