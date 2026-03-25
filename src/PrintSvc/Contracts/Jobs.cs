namespace PrintSvc.Contracts;

public class Jobs
{
    public required string jobId {get; set;}
    public required string batchId {get; set;}
    public required string photoStorageKey {get; set;}
    public int copies {get; set;}
}