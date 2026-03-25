namespace PrintSvc.Settings;

public class BrokerSettings {
    public required string Host { get; set; }
    public required int Port { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
    public required string JobsQueue { get; set; }
    public required string ResultsQueue { get; set; }
}