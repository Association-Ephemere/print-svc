using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PrintSvc.Contracts;
using PrintSvc.Printer;
using PrintSvc.Publisher;
using PrintSvc.Settings;
using PrintSvc.Storage;
using RabbitMQ.Client;

namespace PrintSvc.tests;

public class WorkerTests
{
    private static readonly Guid TestJobId = new("12345678-1234-1234-1234-123456789abc");

    [Fact]
    public void DeserializeJob_Return_Job()
    {
        string message = $"{{\"jobId\":\"{TestJobId}\",\"photos\":[{{\"photoStorageKey\":\"events/xxx/photo.jpg\",\"copies\":2}}],\"startFromIndex\":0}}";

        Job? job = Worker.DeserializeJob(message);

        if (job == null)
            throw new Exception("Error while deserializing message");
    }

    [Theory]
    [InlineData("{\"jobId\": \"not-a-guid\",\"photos\":[{\"photoStorageKey\":\"events/xxx/photo.jpg\",\"copies\":2}],\"startFromIndex\":0")]
    [InlineData("{\"jobId\": \"not-a-guid\"}")]
    [InlineData("{}")]
    public void DeserializeJob_Return_null(string value)
    {
        Job? job = Worker.DeserializeJob(value);

        if (job != null)
            throw new Exception("Message should not be deserializable");
    }

    [Fact]
    public void CreateErrorResult_HasErrorStatus()
    {
        Result result = Worker.CreateErrorResult(TestJobId, 2, 5, "print failed");

        Assert.Equal("error", result.Status);
        Assert.Equal(TestJobId, result.JobId);
        Assert.Equal(2, result.Printed);
        Assert.Equal(5, result.Total);
        Assert.Equal("print failed", result.Error);
    }

    [Fact]
    public async Task ProcessPhotosAsync_WhenDownloadFails_PublishesErrorWithCurrentPrintedCount()
    {
        var mockDownloader = new Mock<IPhotoDownloader>();
        mockDownloader
            .Setup(d => d.DownloadAsync(It.IsAny<Job>(), It.IsAny<JobPhoto>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadResult(false, "download failed"));

        Result? publishedResult = null;
        var mockPublisher = new Mock<IResultPublisher>();
        mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<IChannel>(), It.IsAny<Result>(), It.IsAny<CancellationToken>()))
            .Callback<IChannel, Result, CancellationToken>((_, r, _) => publishedResult = r)
            .Returns(Task.CompletedTask);

        var worker = CreateWorker(mockDownloader.Object, mockPublisher.Object);
        var job = new Job { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = "photo.jpg", Copies = 1 }], StartFromIndex = 0 };

        await worker.ProcessPhotosAsync(job, new Mock<IChannel>().Object);

        Assert.NotNull(publishedResult);
        Assert.Equal("error", publishedResult.Status);
        Assert.Equal(TestJobId, publishedResult.JobId);
        Assert.Equal(0, publishedResult.Printed);
        Assert.Equal("download failed", publishedResult.Error);
    }

    [Fact]
    public async Task ProcessPhotosAsync_WhenPrintFails_PublishesErrorResult()
    {
        var mockDownloader = new Mock<IPhotoDownloader>();
        mockDownloader
            .Setup(d => d.DownloadAsync(It.IsAny<Job>(), It.IsAny<JobPhoto>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadResult(true)); // Download succeeds but file won't exist on disk → Print throws

        Result? publishedResult = null;
        var mockPublisher = new Mock<IResultPublisher>();
        mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<IChannel>(), It.IsAny<Result>(), It.IsAny<CancellationToken>()))
            .Callback<IChannel, Result, CancellationToken>((_, r, _) => publishedResult = r)
            .Returns(Task.CompletedTask);

        var worker = CreateWorker(mockDownloader.Object, mockPublisher.Object);
        var job = new Job { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = "photo.jpg", Copies = 1 }], StartFromIndex = 0 };

        await worker.ProcessPhotosAsync(job, new Mock<IChannel>().Object);

        Assert.NotNull(publishedResult);
        Assert.Equal("error", publishedResult.Status);
        Assert.Equal(TestJobId, publishedResult.JobId);
        Assert.Equal(0, publishedResult.Printed);
        Assert.Equal(1, publishedResult.Total);
    }

    [Fact]
    public async Task ProcessPhotosAsync_WhenPrintFails_DeletesTempFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string tempFilePath = Path.Combine(tempDir, "photo.jpg");
        File.WriteAllBytes(tempFilePath, [0xFF, 0xD8, 0xFF]);

        try
        {
            var mockDownloader = new Mock<IPhotoDownloader>();
            mockDownloader
                .Setup(d => d.DownloadAsync(It.IsAny<Job>(), It.IsAny<JobPhoto>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DownloadResult(true));

            var mockPublisher = new Mock<IResultPublisher>();
            var worker = CreateWorker(mockDownloader.Object, mockPublisher.Object, tempDir);

            var job = new Job { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = "photo.jpg", Copies = 1 }], StartFromIndex = 0 };
            await worker.ProcessPhotosAsync(job, new Mock<IChannel>().Object);

            Assert.False(File.Exists(tempFilePath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ProcessPhotosAsync_WhenPrintFails_PublishesErrorAndSuppressesDone()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllBytes(Path.Combine(tempDir, "photo.jpg"), [0xFF, 0xD8, 0xFF]);

        try
        {
            var mockDownloader = new Mock<IPhotoDownloader>();
            mockDownloader
                .Setup(d => d.DownloadAsync(It.IsAny<Job>(), It.IsAny<JobPhoto>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DownloadResult(true));

            var published = new List<Result>();
            var mockPublisher = new Mock<IResultPublisher>();
            mockPublisher
                .Setup(p => p.PublishAsync(It.IsAny<IChannel>(), It.IsAny<Result>(), It.IsAny<CancellationToken>()))
                .Callback<IChannel, Result, CancellationToken>((_, r, _) => published.Add(r))
                .Returns(Task.CompletedTask);

            var worker = CreateWorker(mockDownloader.Object, mockPublisher.Object, tempDir);
            var job = new Job { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = "photo.jpg", Copies = 1 }], StartFromIndex = 0 };

            await worker.ProcessPhotosAsync(job, new Mock<IChannel>().Object);

            // file is an invalid JPEG → Print throws → error published, done suppressed
            Assert.Contains(published, r => r.Status == "error");
            Assert.DoesNotContain(published, r => r.Status == "done");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ProcessPhotosAsync_WhenNoErrors_PublishesDone()
    {
        var mockDownloader = new Mock<IPhotoDownloader>();
        var published = new List<Result>();
        var mockPublisher = new Mock<IResultPublisher>();
        mockPublisher
            .Setup(p => p.PublishAsync(It.IsAny<IChannel>(), It.IsAny<Result>(), It.IsAny<CancellationToken>()))
            .Callback<IChannel, Result, CancellationToken>((_, r, _) => published.Add(r))
            .Returns(Task.CompletedTask);

        var worker = CreateWorker(mockDownloader.Object, mockPublisher.Object);
        // StartFromIndex == Photos.Count → skip all photos, no errors → publishes done
        var job = new Job { JobId = TestJobId, Photos = [new JobPhoto { PhotoStorageKey = "photo.jpg", Copies = 1 }], StartFromIndex = 1 };

        await worker.ProcessPhotosAsync(job, new Mock<IChannel>().Object);

        Assert.Single(published);
        Assert.Equal("done", published[0].Status);
        Assert.Equal(TestJobId, published[0].JobId);
        Assert.Equal(1, published[0].Printed);
        Assert.Equal(1, published[0].Total);
    }

    private static Worker CreateWorker(IPhotoDownloader downloader, IResultPublisher publisher, string tempDir = "tmp")
    {
        var mockTracker = new Mock<IPrintQueueTracker>();
        mockTracker
            .Setup(t => t.WaitForCompletionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new Worker(
            Options.Create(new BrokerSettings { ResultsQueue = "print.status" }),
            Options.Create(new StorageSettings { TempDirectory = tempDir }),
            Options.Create(new PrintingSettings { PrinterName = "", PaperWidthInches = 6f, PaperHeightInches = 4f }),
            NullLogger<Worker>.Instance,
            downloader,
            publisher,
            mockTracker.Object
        );
    }
}
