namespace PrintSvc.Contracts;

public class Results
{
    public required string jobId {get; set;}
    public required string status {get; set;}
    public required string errorMessage {get; set;}
}