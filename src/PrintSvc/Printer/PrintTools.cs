using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.Versioning;


namespace PrintSvc.Printer;

[SupportedOSPlatform("windows")]
public static class PrintTools
{

    public static void Print(
        this FileInfo value,
        string? printerName = null,
        int copies = 1,
        float paperWidthInches = 6f,
        float paperHeightInches = 4f)
    {
        if (!value.Exists)
        {
            throw new FileNotFoundException($"Photo to print not found: {value.FullName}", value.FullName);
        }

        if (copies < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(copies), "Copies must be greater than zero.");
        }

        if (paperWidthInches <= 0 || paperHeightInches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paperWidthInches), "Paper dimensions must be greater than zero.");
        }

        short requestedCopies = checked((short)copies);

        using Image image = Image.FromFile(value.FullName);
        (float orientedWidth, float orientedHeight) = GetOrientedPaperSize(image.Size, paperWidthInches, paperHeightInches);

        using PrintDocument document = CreatePrintDocument(printerName, requestedCopies, orientedWidth, orientedHeight);

        document.PrintPage += (_, printEventArgs) =>
        {
            if (printEventArgs.Graphics == null)
            {
                throw new InvalidOperationException("Print graphics context is not available.");
            }

            Rectangle targetBounds = printEventArgs.PageBounds;
            Rectangle sourceCropBounds = CalculateCropBounds(image.Size, targetBounds.Size);

            printEventArgs.Graphics.DrawImage(image, targetBounds, sourceCropBounds, GraphicsUnit.Pixel);
            printEventArgs.HasMorePages = false;
        };

        document.Print();
    }

    internal static PrintDocument CreatePrintDocument(
        string? printerName,
        short copies,
        float paperWidthInches = 4f,
        float paperHeightInches = 6f)
    {
        int width = (int)Math.Round(paperWidthInches * 100f);
        int height = (int)Math.Round(paperHeightInches * 100f);

        var paperSize = new PaperSize($"Photo {paperWidthInches:0.##}x{paperHeightInches:0.##}in", width, height);

        var document = new PrintDocument
        {
            PrintController = new StandardPrintController()
        };

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            document.PrinterSettings.PrinterName = printerName;
        }

        document.PrinterSettings.Copies = copies;
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        document.DefaultPageSettings.PaperSize = paperSize;
        document.DefaultPageSettings.Landscape = false;

        return document;
    }

    internal static (float Width, float Height) GetOrientedPaperSize(Size imageSize, float paperWidthInches, float paperHeightInches)
    {
        bool imageIsLandscape = imageSize.Width >= imageSize.Height;
        bool paperIsLandscape = paperWidthInches >= paperHeightInches;

        if (imageIsLandscape == paperIsLandscape)
        {
            return (paperWidthInches, paperHeightInches);
        }

        return (paperHeightInches, paperWidthInches);
    }

    internal static Rectangle CalculateCropBounds(Size imageSize, Size targetSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0)
        {
            throw new ArgumentException("Image size must be greater than zero.", nameof(imageSize));
        }

        if (targetSize.Width <= 0 || targetSize.Height <= 0)
        {
            throw new ArgumentException("Target size must be greater than zero.", nameof(targetSize));
        }

        float imageRatio = (float)imageSize.Width / imageSize.Height;
        float targetRatio = (float)targetSize.Width / targetSize.Height;

        if (imageRatio > targetRatio)
        {
            int cropWidth = (int)Math.Round(imageSize.Height * targetRatio);
            int x = (imageSize.Width - cropWidth) / 2;
            return new Rectangle(x, 0, cropWidth, imageSize.Height);
        }

        int cropHeight = (int)Math.Round(imageSize.Width / targetRatio);
        int y = (imageSize.Height - cropHeight) / 2;
        return new Rectangle(0, y, imageSize.Width, cropHeight);
    }

}