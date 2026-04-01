namespace PrintSvc.Contracts;

public class Jobs
{
    [JsonPropertyName("jobId")]
    public required string JobId {get; set;}
    [JsonPropertyName("batchId")]
    public required string BatchId {get; set;}
    [JsonPropertyName("photoStorageKey")]
    public required string PhotoStorageKey {get; set;}
    [JsonPropertyName("copies")]
    public int Copies {get; set;}
}