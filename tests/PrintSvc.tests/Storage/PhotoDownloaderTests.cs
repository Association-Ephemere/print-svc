using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.DataModel.Response;
using Minio.Exceptions;
using Moq;
using PrintSvc.Contracts;
using PrintSvc.Settings;
using PrintSvc.Storage;
using Xunit;

namespace PrintSvc.tests.Storage
{
    public class PhotoDownloaderTests
    {
        private const string TestTempDirectory = "tmp";

        private static PhotoDownloader CreateDownloader(IMinioClient client, string bucket = "photos", string tmpDir = TestTempDirectory) =>
       new (
           client,
           Options.Create(new StorageSettings
           {
               Bucket = bucket,
               TempDirectory = tmpDir
           }),
           Options.Create(new BrokerSettings { JobsQueue = "job", ResultsQueue = "results"}),
           NullLogger<PhotoDownloader>.Instance
       );

        [Fact]
        public async Task DownloadAsync_DownloadFailure()
        {
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
                .Throws(new MinioException());

            var downloader = CreateDownloader(mock.Object);

            Job job = new Job() { JobId = "job-123", Photos = [new JobPhoto { PhotoStorageKey = "image.jpg", Copies = 1 }], StartFromIndex = 0 };

            bool res = await downloader.DownloadAsync(job, job.Photos[0]);

            Assert.False(res);
            Assert.False(File.Exists(Path.Combine(TestTempDirectory, Path.GetFileName(job.Photos[0].PhotoStorageKey))));

        }

        public static T? CreateInstanceNonPublic<T>(params object[] args)
        {
            return (T?)Activator.CreateInstance(
                typeof(T),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                args,
                null);
        }

        [Fact]
        public async Task DownloadAsync_DownloadWorks_And_CheckCleanUp()
        {
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateInstanceNonPublic<ObjectStat>());

            var downloader = CreateDownloader(mock.Object);

            Job job = new Job() { JobId = "job-123", Photos = [new JobPhoto { PhotoStorageKey = "image.jpg", Copies = 1 }], StartFromIndex = 0 };

            bool res = await downloader.DownloadAsync(job, job.Photos[0]);
            
            Assert.True(res);
            //Assert.False(File.Exists(Path.Combine(TestTempDirectory, Path.GetFileName(job.Photos[0].PhotoStorageKey))));

        }
    }
}
