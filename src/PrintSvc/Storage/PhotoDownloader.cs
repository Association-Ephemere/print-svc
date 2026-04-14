using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using PrintSvc.Contracts;
using PrintSvc.Settings;
using RabbitMQ.Client;

namespace PrintSvc.Storage
{
    public sealed class PhotoDownloader(IMinioClient client, IOptions<StorageSettings> storageOptions, IOptions<BrokerSettings> broker, ILogger<PhotoDownloader> logger) : IPhotoDownloader
    {
        private readonly IMinioClient _client = client;
        private readonly StorageSettings _storage = storageOptions.Value;
        private readonly BrokerSettings _broker = broker.Value;
        private readonly ILogger<PhotoDownloader> _logger = logger;

        public async Task<bool> DownloadAsync(Job job, JobPhoto photo, int maxtries = 3, int delay = 1000, IChannel? channel = null, CancellationToken ct = default)
        {

            string fileName = Path.GetFileName(photo.PhotoStorageKey);
            string destinationFolder = _storage.TempDirectory;

            Directory.CreateDirectory(destinationFolder);


            string destinationPath = Path.Combine(destinationFolder, fileName);

            try
            {

                var args = new GetObjectArgs()
                    .WithBucket(_storage.Bucket)
                    .WithObject(photo.PhotoStorageKey)
                    .WithFile(destinationPath);

                Directory.CreateDirectory(destinationFolder);
                File.Create(destinationPath).Close();

                await _client.GetObjectAsync(args, ct);

                _logger.LogDebug("Downloaded photo: Filename: {FileName}, Location: {DestinationPath}", fileName, destinationPath);

                return true;
            }
            catch (MinioException)
            {
                _logger.LogError("Error while downloading {PhotoStorageKey}", photo.PhotoStorageKey);

                if (channel != null)
                {
                    await channel.QueueDeclareAsync(queue: _broker.ResultsQueue,
                             durable: true,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null,
                             cancellationToken: default);


                    Result r = new Result()
                    {
                        JobId = job.JobId,
                        Status = "error",
                        Printed = 0,
                        Total = photo.Copies,
                        Error = $"Error while downloading the photo: {fileName}"
                    };
                    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(r));
                    await channel.BasicPublishAsync("", _broker.ResultsQueue, body, cancellationToken: default);
                }

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                    _logger.LogDebug("File deleted.");
                }

                return false;
            }
        }

        
    }
}
