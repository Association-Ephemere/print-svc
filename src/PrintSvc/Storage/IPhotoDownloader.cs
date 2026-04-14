using PrintSvc.Contracts;

namespace PrintSvc.Storage;

public record DownloadResult(bool Success, string? Error = null);

public interface IPhotoDownloader
{
    Task<DownloadResult> DownloadAsync(Job job, JobPhoto photo, int maxtries = 3, int delay = 1000, CancellationToken ct = default);
    Task PingAsync(CancellationToken ct = default);
}
