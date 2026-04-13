using System;
using System.Collections.Generic;
using System.Text;
using PrintSvc.Contracts;
using RabbitMQ.Client;

namespace PrintSvc.Storage
{
    public interface IPhotoDownloader
    {
        Task<bool> DownloadAsync(Job job, JobPhoto photo, int maxtries = 3, int delay = 1000, IChannel? channel = null, CancellationToken ct = default);
    }
}
