using System.Management;
using System.Runtime.Versioning;

namespace PrintSvc.Printer;

[SupportedOSPlatform("windows")]
public class PrintQueueTracker : IPrintQueueTracker
{
    private readonly ILogger<PrintQueueTracker> _logger;

    public PrintQueueTracker(ILogger<PrintQueueTracker> logger) => _logger = logger;

    public async Task WaitForCompletionAsync(string documentName, CancellationToken ct = default)
    {
        _logger.LogDebug("Waiting for print job '{DocumentName}' to leave the queue.", documentName);

        while (!ct.IsCancellationRequested)
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_PrintJob WHERE Document = '{documentName}'");
            using ManagementObjectCollection jobs = searcher.Get();

            if (jobs.Count == 0)
            {
                _logger.LogDebug("Print job '{DocumentName}' left the queue.", documentName);
                return;
            }

            await Task.Delay(500, ct);
        }
    }
}
