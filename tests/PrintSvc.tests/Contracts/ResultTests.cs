using System.Text.Json;
using PrintSvc.Contracts;

namespace PrintSvc.tests.Contracts;

public class ResultTests
{
    [Fact]
    public void Result_Serializes_NewSchema()
    {
        var result = new Result
        {
            JobId = "job-123",
            Status = "error",
            Printed = 0,
            Total = 2,
            Error = "download failed"
        };

        string json = JsonSerializer.Serialize(result);

        Assert.Contains("\"jobId\":", json);
        Assert.Contains("\"status\":\"error\"", json);
        Assert.Contains("\"printed\":0", json);
        Assert.Contains("\"total\":2", json);
        Assert.Contains("\"error\":\"download failed\"", json);
    }
}