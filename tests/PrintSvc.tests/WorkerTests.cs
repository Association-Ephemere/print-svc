using PrintSvc.Contracts;

namespace PrintSvc.tests;

public class WorkerTests
{
    [Fact]
    public void DeserializeJob_Return_Job()
    {
        string message = "{\"jobId\": \"uuid\",\"batchId\": \"uuid\",\"photoStorageKey\": \"events/xxx/photo.jpg\",\"copies\": 2}";

        Jobs? job = Worker.DeserializeJob(message);

        if (job == null)
            throw new Exception("Error while deserializing message");
    }

    [Theory]
    [InlineData("{\"jobId\": \"uuid\",\"batchId\": \"uuid\",\"photoStorageKey\": \"events/xxx/photo.jpg\",\"copies\": 2")]
    [InlineData("{\"jobId\": \"uuid\"}")]
    [InlineData("{}")]
    public void DeserializeJob_Return_null(string value)
    {
        string message = value;

        Jobs? job = Worker.DeserializeJob(message);

        if (job != null)
            throw new Exception("Message should not be deserializable");
    }
}
