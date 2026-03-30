namespace PrintSvc.Settings;

public sealed class StorageSettings {
    public required string Endpoint { get; set; }
    public required string AccessKey { get; set; }
    public required string SecretKey { get; set; }
    public required string Bucket { get; set; }
    public required string TempDirectory { get; set; }
    public required bool UseSSL { get; set; }
    
}
