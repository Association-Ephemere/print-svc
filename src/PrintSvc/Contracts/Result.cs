using System.Text.Json.Serialization;

namespace PrintSvc.Contracts;

public class Result
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("printed")]
    public int Printed { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
