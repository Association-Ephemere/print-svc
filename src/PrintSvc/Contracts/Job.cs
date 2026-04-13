using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace PrintSvc.Contracts;

public class Job
{
    [JsonPropertyName("jobId")]
    public required string JobId { get; set; }

    [JsonPropertyName("photos")]
    public required List<JobPhoto> Photos { get; set; }

    [JsonPropertyName("startFromIndex")]
    public int StartFromIndex { get; set; }
}

public class JobPhoto
{
    [JsonPropertyName("photoStorageKey")]
    public required string PhotoStorageKey { get; set; }

    [JsonPropertyName("copies")]
    public int Copies { get; set; }
}
