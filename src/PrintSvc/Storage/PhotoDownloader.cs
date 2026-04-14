using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using PrintSvc.Contracts;
using PrintSvc.Settings;

namespace PrintSvc.Storage;

public sealed class PhotoDownloader(
    IMinioClient client,
    IOptions<StorageSettings> storageOptions,
    ILogger<PhotoDownloader> logger) : IPhotoDownloader
{
    private readonly IMinioClient _client = client;
    private readonly StorageSettings _storage = storageOptions.Value;
    private readonly ILogger<PhotoDownloader> _logger = logger;

    public async Task<DownloadResult> DownloadAsync(Job job, JobPhoto photo, int maxtries = 3, int delay = 1000, CancellationToken ct = default)
    {
        string extension = Path.GetExtension(photo.PhotoStorageKey).ToLowerInvariant();
        if (extension != ".jpg" && extension != ".jpeg")
        {
            _logger.LogError("Rejecting photo {Key}: unsupported format. Only JPEG files are supported.", photo.PhotoStorageKey);
            return new DownloadResult(false, $"Unsupported file format: {extension}");
        }

        string fileName = Path.GetFileName(photo.PhotoStorageKey);
        string destinationPath = Path.Combine(_storage.TempDirectory, fileName);

        Directory.CreateDirectory(_storage.TempDirectory);

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
                return new DownloadResult(true);
            }
            catch (MinioException) when (attempt < maxtries)
            {
                _logger.LogWarning("Download attempt {Attempt}/{MaxTries} failed for {Key}. Retrying in {Delay}ms.", attempt, maxtries, photo.PhotoStorageKey, delay);
                await Task.Delay(delay, ct);
            }
            catch (MinioException)
            {
                _logger.LogError("All {MaxTries} download attempts failed for {PhotoStorageKey}.", maxtries, photo.PhotoStorageKey);

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                    _logger.LogDebug("File deleted.");
                }

                return new DownloadResult(false, $"Error while downloading the photo: {fileName}");
            }
        }

        return new DownloadResult(false, "Download failed."); // unreachable, satisfies compiler
    }
}
