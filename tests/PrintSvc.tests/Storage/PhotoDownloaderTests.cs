using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Response;
using Moq;
using PrintSvc.Settings;
using PrintSvc.Storage;
using Xunit;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PrintSvc.tests.Storage
{
    public class PhotoDownloaderTests
    {
        [Fact]
        public async Task DownloadAsync_CreatesDirectory_AndDownloadsFile()
        {
            // Arrange
            var minioClientMock = new Mock<IMinioClient>();
            var settings = new StorageSettings
            {
                Endpoint = "localhost",
                AccessKey = "user",
                SecretKey = "pass",
                Bucket = "photos",
                TempDirectory = "test_temp",
                UseSSL = false
            };
            var options = Options.Create(settings);

            var downloader = new PhotoDownloader(minioClientMock.Object, options, NullLogger<PhotoDownloader>.Instance);
            string photoKey = "image.png";

            // Act
            await downloader.DownloadAsync(photoKey, ct : default);

            // Assert
            minioClientMock.Verify(c => c.GetObjectAsync(
                It.Is<GetObjectArgs>(args => args.BucketName == settings.Bucket && args.ObjectName == photoKey), 
                It.IsAny<CancellationToken>()), 
                Times.Once);

            if (Directory.Exists(settings.TempDirectory))
            {
                Directory.Delete(settings.TempDirectory, true);
            }
        }
    }
}
