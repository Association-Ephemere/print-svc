using System.Text.Json.Serialization;

namespace PrintSvc.Contracts;

public class Results
{
    [JsonPropertyName("jobId")]
    public required string JobId {get; set;}
    [JsonPropertyName("status")]
    public required string Status {get; set;}
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage {get; set;}
}
