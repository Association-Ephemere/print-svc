namespace PrintSvc.Settings;

public sealed class StorageSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string TempDirectory { get; set; } = string.Empty;
    public bool UseSSL { get; set; } = false;

}
