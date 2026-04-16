namespace PrintSvc.Printer;

public interface IPrintQueueTracker
{
    Task WaitForCompletionAsync(string documentName, CancellationToken ct = default);
}
