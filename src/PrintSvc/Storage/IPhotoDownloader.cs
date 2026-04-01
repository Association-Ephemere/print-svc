using System;
using System.Collections.Generic;
using System.Text;

namespace PrintSvc.Storage
{
    public interface IPhotoDownloader
    {
        Task<bool> DownloadAsync(string key, int maxtries = 3, int delay = 1000, CancellationToken ct = default);
    }
}
