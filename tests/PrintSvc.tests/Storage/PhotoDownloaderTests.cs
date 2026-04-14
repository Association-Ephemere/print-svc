using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
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
        private static readonly Guid TestJobId = new("12345678-1234-1234-1234-123456789abc");

        private static PhotoDownloader CreateDownloader(IMinioClient client, string bucket = "photos", string tmpDir = TestTempDirectory) =>
            new(
                client,
                Options.Create(new StorageSettings { Bucket = bucket, TempDirectory = tmpDir }),
                NullLogger<PhotoDownloader>.Instance
            );

        public static T? CreateInstanceNonPublic<T>(params object[] args) =>
            (T?)Activator.CreateInstance(
                typeof(T),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                args,
                null);

        [Fact]
        public async Task DownloadAsync_DownloadFailure()
        {
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
                .Throws(new MinioException());

            var downloader = CreateDownloader(mock.Object);

            Job job = new() { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = "image.jpg", Copies = 1 }], StartFromIndex = 0 };

            DownloadResult res = await downloader.DownloadAsync(job, job.Photos[0], maxtries: 1);

            Assert.False(res.Success);
            Assert.NotNull(res.Error);
            Assert.False(File.Exists(Path.Combine(TestTempDirectory, Path.GetFileName(job.Photos[0].PhotoStorageKey))));
        }

        [Fact]
        public async Task DownloadAsync_DownloadWorks_FileLeftForCaller()
        {
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateInstanceNonPublic<ObjectStat>());

            var downloader = CreateDownloader(mock.Object);

            Job job = new() { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = "image.jpg", Copies = 1 }], StartFromIndex = 0 };

            DownloadResult res = await downloader.DownloadAsync(job, job.Photos[0]);

            Assert.True(res.Success);
            // Cleanup is Worker's responsibility — file must still exist after download
            Assert.True(File.Exists(Path.Combine(TestTempDirectory, Path.GetFileName(job.Photos[0].PhotoStorageKey))));

            File.Delete(Path.Combine(TestTempDirectory, "image.jpg"));
        }

        [Fact]
        public async Task DownloadAsync_RetriesOnFailure_SucceedsOnSecondAttempt()
        {
            int callCount = 0;
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    if (callCount == 1) throw new MinioException();
                    return CreateInstanceNonPublic<ObjectStat>()!;
                });

            var downloader = CreateDownloader(mock.Object);
            Job job = new() { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = "image.jpg", Copies = 1 }], StartFromIndex = 0 };

            DownloadResult res = await downloader.DownloadAsync(job, job.Photos[0], maxtries: 3, delay: 0);

            Assert.True(res.Success);
            Assert.Equal(2, callCount);

            string tempFile = Path.Combine(TestTempDirectory, "image.jpg");
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }

        [Fact]
        public async Task DownloadAsync_ReturnsEarlyFalse_AfterAllRetriesExhausted()
        {
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
                .Throws(new MinioException());

            var downloader = CreateDownloader(mock.Object);
            Job job = new() { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = "image.jpg", Copies = 1 }], StartFromIndex = 0 };

            DownloadResult res = await downloader.DownloadAsync(job, job.Photos[0], maxtries: 3, delay: 0);

            Assert.False(res.Success);
            Assert.NotNull(res.Error);
            mock.Verify(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Theory]
        [InlineData("photo.png")]
        [InlineData("photo.bmp")]
        [InlineData("photo.gif")]
        [InlineData("photo")]
        public async Task DownloadAsync_RejectsNonJpeg_ReturnsFalseWithoutDownloading(string storageKey)
        {
            var mock = new Mock<IMinioClient>();
            var downloader = CreateDownloader(mock.Object);

            Job job = new() { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = storageKey, Copies = 1 }], StartFromIndex = 0 };

            DownloadResult res = await downloader.DownloadAsync(job, job.Photos[0]);

            Assert.False(res.Success);
            Assert.NotNull(res.Error);
            mock.Verify(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("photo.jpg")]
        [InlineData("photo.jpeg")]
        [InlineData("photo.JPG")]
        [InlineData("photo.JPEG")]
        public async Task DownloadAsync_AcceptsJpegExtensions(string storageKey)
        {
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateInstanceNonPublic<ObjectStat>());

            var downloader = CreateDownloader(mock.Object);

            Job job = new() { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = storageKey, Copies = 1 }], StartFromIndex = 0 };

            DownloadResult res = await downloader.DownloadAsync(job, job.Photos[0]);

            Assert.True(res.Success);

            string tempFile = Path.Combine(TestTempDirectory, Path.GetFileName(storageKey));
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }

        [Fact]
        public async Task PingAsync_WhenBucketExists_DoesNotThrow()
        {
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var downloader = CreateDownloader(mock.Object);

            await downloader.PingAsync();
        }

        [Fact]
        public async Task PingAsync_WhenBucketDoesNotExist_Throws()
        {
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var downloader = CreateDownloader(mock.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(() => downloader.PingAsync());
        }

        [Fact]
        public async Task PingAsync_WhenStorageUnreachable_Throws()
        {
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new MinioException());

            var downloader = CreateDownloader(mock.Object);

            await Assert.ThrowsAsync<MinioException>(() => downloader.PingAsync());
        }
    }
}
