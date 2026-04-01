using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using PrintSvc.Settings;

namespace PrintSvc.Storage
{
    public sealed class PhotoDownloader : IPhotoDownloader
    {
        private readonly IMinioClient _client;
        private readonly StorageSettings _storage;
        private readonly ILogger<PhotoDownloader> _logger;

        public PhotoDownloader(IMinioClient client, IOptions<StorageSettings> storageOptions, ILogger<PhotoDownloader> logger)
        {
            _client = client;
            _storage = storageOptions.Value;
            _logger = logger;
        }

        public async Task<bool> DownloadAsync(string key, int maxtries = 3, int delay = 1000, CancellationToken ct = default)
        {
            
            string fileName = Path.GetFileName(key);
            string destinationFolder = _storage.TempDirectory;
            Directory.CreateDirectory(destinationFolder);

            string destinationPath = Path.Combine(destinationFolder, fileName);

            for (int i = 0; i < maxtries; i++)
            {
                try
                {

                    var args = new GetObjectArgs()
                        .WithBucket(_storage.Bucket)
                        .WithObject(key)
                        .WithFile(destinationPath);

                    Directory.CreateDirectory(destinationFolder);
                    File.Create(destinationPath).Close();

                    await _client.GetObjectAsync(args, ct);

                    _logger.LogDebug($"Downloaded photo:\n\t - Filename: {fileName}\n\t - Location: {(destinationPath)}");
                    return true;
                }
                catch (MinioException e)
                {
                    _logger.LogError($"Error while downloading {key}, Attempt: {i+1}");
                    await Task.Delay(delay);
                }
            }

            _logger.LogError($"Error while downloading {key}");
            return false;
        }
    }
}
