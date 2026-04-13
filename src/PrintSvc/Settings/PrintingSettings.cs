namespace PrintSvc.Settings;

public class PrintingSettings {
    public required string PrinterName { get; set; }
    public float PaperWidthInches { get; set; } = 6f;
    public float PaperHeightInches { get; set; } = 4f;
}