namespace PrintRxerV3.Documents;

internal sealed class PdfPageImage
{
    public required byte[] ImageBytes { get; init; }
    public double WidthPoints { get; init; }
    public double HeightPoints { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
}
