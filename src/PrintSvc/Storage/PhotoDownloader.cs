using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using PrintSvc.Contracts;
using PrintSvc.Publisher;
using PrintSvc.Settings;
using RabbitMQ.Client;

namespace PrintSvc.Storage;

public sealed class PhotoDownloader(
    IMinioClient client,
    IOptions<StorageSettings> storageOptions,
    ILogger<PhotoDownloader> logger,
    IResultPublisher publisher) : IPhotoDownloader
{
    private readonly IMinioClient _client = client;
    private readonly StorageSettings _storage = storageOptions.Value;
    private readonly ILogger<PhotoDownloader> _logger = logger;
    private readonly IResultPublisher _publisher = publisher;

    public async Task<bool> DownloadAsync(Job job, JobPhoto photo, int maxtries = 3, int delay = 1000, IChannel? channel = null, CancellationToken ct = default)
    {
        string extension = Path.GetExtension(photo.PhotoStorageKey).ToLowerInvariant();
        if (extension != ".jpg" && extension != ".jpeg")
        {
            _logger.LogError("Rejecting photo {Key}: unsupported format. Only JPEG files are supported.", photo.PhotoStorageKey);

            if (channel != null)
            {
                await _publisher.PublishAsync(channel, new Result
                {
                    JobId = job.JobId,
                    Status = "error",
                    Printed = 0,
                    Total = job.Photos.Count,
                    Error = $"Unsupported file format: {extension}"
                }, ct);
            }

            return false;
        }

        string fileName = Path.GetFileName(photo.PhotoStorageKey);
        string destinationFolder = _storage.TempDirectory;
        string destinationPath = Path.Combine(destinationFolder, fileName);

        Directory.CreateDirectory(destinationFolder);

        var args = new GetObjectArgs()
            .WithBucket(_storage.Bucket)
            .WithObject(photo.PhotoStorageKey)
            .WithFile(destinationPath);

        for (int attempt = 1; attempt <= maxtries; attempt++)
        {
            try
            {
                File.Create(destinationPath).Close();
                await _client.GetObjectAsync(args, ct);

                _logger.LogDebug("Downloaded photo: Filename: {FileName}, Location: {DestinationPath}", fileName, destinationPath);
                return true;
            }
            catch (MinioException) when (attempt < maxtries)
            {
                _logger.LogWarning("Download attempt {Attempt}/{MaxTries} failed for {Key}. Retrying in {Delay}ms.", attempt, maxtries, photo.PhotoStorageKey, delay);
                await Task.Delay(delay, ct);
            }
            catch (MinioException)
            {
                _logger.LogError("All {MaxTries} download attempts failed for {PhotoStorageKey}.", maxtries, photo.PhotoStorageKey);

                if (channel != null)
                {
                    await _publisher.PublishAsync(channel, new Result
                    {
                        JobId = job.JobId,
                        Status = "error",
                        Printed = 0,
                        Total = job.Photos.Count,
                        Error = $"Error while downloading the photo: {fileName}"
                    }, ct);
                }

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                    _logger.LogDebug("File deleted.");
                }

                return false;
            }
        }

        return false; // unreachable, satisfies compiler
    }
}
