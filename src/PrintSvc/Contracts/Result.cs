using System.Text.Json.Serialization;

namespace PrintSvc.Contracts;

public class Result
{
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = string.Empty;
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;
}
