using PrintSvc.Printer;
using System.Drawing;
using System.Runtime.Versioning;

namespace PrintSvc.tests.Printer;

[SupportedOSPlatform("windows")]
public class PrintToolsTests
{
    [Fact]
    public void CreatePrintDocument_UsesStandardPrintController_WhenPrinterNameIsEmpty()
    {
        using var document = PrintTools.CreatePrintDocument(null, 1, 6f, 4f);

        Assert.NotNull(document.PrintController);
        Assert.IsType<System.Drawing.Printing.StandardPrintController>(document.PrintController);
        Assert.Equal((short)1, document.PrinterSettings.Copies);
        Assert.Equal(600, document.DefaultPageSettings.PaperSize.Width);
        Assert.Equal(400, document.DefaultPageSettings.PaperSize.Height);
        Assert.Equal(0, document.DefaultPageSettings.Margins.Left);
        Assert.False(document.DefaultPageSettings.Landscape);
    }

    [Fact]
    public void CreatePrintDocument_AssignsRequestedPrinterName()
    {
        const string printerName = "Office Printer";
        using var document = PrintTools.CreatePrintDocument(printerName, 2, 6f, 4f);

        Assert.Equal(printerName, document.PrinterSettings.PrinterName);
        Assert.Equal((short)2, document.PrinterSettings.Copies);
    }

    [Fact]
    public void Print_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        var fileInfo = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg"));

        Assert.Throws<FileNotFoundException>(() => fileInfo.Print());
    }

    [Fact]
    public void Print_ThrowsArgumentOutOfRangeException_WhenCopiesIsLowerThanOne()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllText(tempFile, string.Empty);

        try
        {
            var fileInfo = new FileInfo(tempFile);

            Assert.Throws<ArgumentOutOfRangeException>(() => fileInfo.Print(copies: 0));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void CalculateCropBounds_CropsLeftAndRight_WhenImageIsWiderThanTarget()
    {
        var imageSize = new Size(4000, 2000);
        var targetSize = new Size(1200, 800);

        Rectangle crop = PrintTools.CalculateCropBounds(imageSize, targetSize);

        Assert.Equal(500, crop.X);
        Assert.Equal(0, crop.Y);
        Assert.Equal(3000, crop.Width);
        Assert.Equal(2000, crop.Height);
    }

    [Fact]
    public void CalculateCropBounds_Throws_WhenImageSizeIsInvalid()
    {
        var targetSize = new Size(500, 500);

        Assert.Throws<ArgumentException>(() => PrintTools.CalculateCropBounds(new Size(0, 200), targetSize));
    }

    [Fact]
    public void CalculateCropBounds_Throws_WhenTargetSizeIsInvalid()
    {
        var imageSize = new Size(2000, 1000);

        Assert.Throws<ArgumentException>(() => PrintTools.CalculateCropBounds(imageSize, new Size(0, 500)));
    }

    [Fact]
    public void GetOrientedPaperSize_SwapsToPortrait_WhenImageIsPortraitAndPaperIsLandscape()
    {
        var imageSize = new Size(1200, 1800);

        (float width, float height) = PrintTools.GetOrientedPaperSize(imageSize, 6f, 4f);

        Assert.Equal(4f, width);
        Assert.Equal(6f, height);
    }

    [Fact]
    public void GetOrientedPaperSize_StaysLandscape_WhenImageIsLandscapeAndPaperIsLandscape()
    {
        var imageSize = new Size(1800, 1200);

        (float width, float height) = PrintTools.GetOrientedPaperSize(imageSize, 6f, 4f);

        Assert.Equal(6f, width);
        Assert.Equal(4f, height);
    }
}