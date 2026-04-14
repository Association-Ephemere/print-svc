using System.Text.Json.Serialization;

namespace PrintSvc.Contracts;

public class Result
{
    [JsonPropertyName("jobId")]
    public Guid JobId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("printed")]
    public int Printed { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
