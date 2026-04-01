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
using PrintSvc.Settings;
using PrintSvc.Storage;
using Xunit;

namespace PrintSvc.tests.Storage
{
    public class PhotoDownloaderTests
    {
        private static IPhotoDownloader CreateDownloader(IMinioClient client, string bucket = "photos", string tmpDir = "tmp") =>
       new PhotoDownloader(
           client,
           Options.Create(new StorageSettings
           {
               Bucket = bucket,
               TempDirectory = tmpDir
           }),
           NullLogger<PhotoDownloader>.Instance
       );

        private static ObjectStat OkResponse() =>
            null;

        [Fact]
        public async Task DownloadAsync_DownloadExistingImage()
        {
            var capturedArgs = new List<GetObjectArgs>();
            var mock = new Mock<IMinioClient>();
            mock.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
                .Callback<GetObjectArgs, CancellationToken>((a, _) => capturedArgs.Add(a))
                .ReturnsAsync(OkResponse());

            var uploader = CreateDownloader(mock.Object);

            await uploader.DownloadAsync("image.jpg");

            Assert.Single(capturedArgs);
            
        }
    }
}
