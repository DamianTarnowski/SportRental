using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SportRental.Admin.Services.QrCode;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services;

/// <summary>
/// Generates printable PDF labels with QR codes for products
/// </summary>
public interface IQrLabelGenerator
{
    /// <summary>
    /// Generates PDF with QR code labels for products
    /// </summary>
    /// <param name="products">Products with quantities</param>
    /// <param name="labelSize">Size of each label in mm</param>
    /// <returns>PDF as byte array</returns>
    Task<byte[]> GenerateLabelsAsync(IEnumerable<(Product Product, int Quantity)> products, LabelSize labelSize = LabelSize.Medium);
}

public enum LabelSize
{
    Small,   // 30x30mm - 5 columns x 9 rows = 45 labels per page
    Medium,  // 50x50mm - 3 columns x 5 rows = 15 labels per page
    Large    // 70x70mm - 2 columns x 4 rows = 8 labels per page
}

public class QrLabelGenerator : IQrLabelGenerator
{
    private readonly IQrCodeGenerator _qrCodeGenerator;
    
    public QrLabelGenerator(IQrCodeGenerator qrCodeGenerator)
    {
        _qrCodeGenerator = qrCodeGenerator;
    }
    
    public async Task<byte[]> GenerateLabelsAsync(IEnumerable<(Product Product, int Quantity)> products, LabelSize labelSize = LabelSize.Medium)
    {
        var (labelWidth, labelHeight, columns, rows, qrSize, fontSize) = GetLabelDimensions(labelSize);
        
        // Flatten products into individual labels
        var labels = products
            .SelectMany(p => Enumerable.Range(0, p.Quantity).Select(_ => p.Product))
            .ToList();
        
        if (!labels.Any())
        {
            // Return empty PDF
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Content().AlignCenter().AlignMiddle().Text("Brak etykiet do wygenerowania").FontSize(14);
                });
            }).GeneratePdf();
        }
        
        // Pre-generate all QR codes
        var qrCodes = new Dictionary<Guid, byte[]>();
        foreach (var product in labels.DistinctBy(p => p.Id))
        {
            var qrData = _qrCodeGenerator.GenerateProductQrCodeData(product.Id, product.Name, product.Sku ?? "");
            var qrBytes = await _qrCodeGenerator.GenerateQrCodeBytesAsync(qrData, 400);
            qrCodes[product.Id] = qrBytes;
        }
        
        var labelsPerPage = columns * rows;
        var pageCount = (int)Math.Ceiling((double)labels.Count / labelsPerPage);
        
        return Document.Create(container =>
        {
            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var pageLabels = labels.Skip(pageIndex * labelsPerPage).Take(labelsPerPage).ToList();
                
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginTop(10, Unit.Millimetre);
                    page.MarginBottom(10, Unit.Millimetre);
                    page.MarginLeft(10, Unit.Millimetre);
                    page.MarginRight(10, Unit.Millimetre);
                    
                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            for (int i = 0; i < columns; i++)
                            {
                                cols.ConstantColumn(labelWidth, Unit.Millimetre);
                            }
                        });
                        
                        int labelIndex = 0;
                        for (int row = 0; row < rows && labelIndex < pageLabels.Count; row++)
                        {
                            for (int col = 0; col < columns && labelIndex < pageLabels.Count; col++)
                            {
                                var product = pageLabels[labelIndex];
                                var qrBytes = qrCodes[product.Id];
                                
                                table.Cell()
                                    .Row((uint)(row + 1))
                                    .Column((uint)(col + 1))
                                    .Height(labelHeight, Unit.Millimetre)
                                    .Border(0.5f)
                                    .BorderColor(Colors.Grey.Lighten2)
                                    .Padding(2, Unit.Millimetre)
                                    .Column(column =>
                                    {
                                        column.Item()
                                            .AlignCenter()
                                            .Height(qrSize, Unit.Millimetre)
                                            .Image(qrBytes);
                                        
                                        column.Item()
                                            .AlignCenter()
                                            .Text(text =>
                                            {
                                                text.Line(TruncateName(product.Name, labelSize))
                                                    .FontSize(fontSize)
                                                    .Bold();
                                            });
                                        
                                        column.Item()
                                            .AlignCenter()
                                            .Text(text =>
                                            {
                                                text.Line(product.Sku ?? "")
                                                    .FontSize(fontSize - 1)
                                                    .FontColor(Colors.Grey.Darken1);
                                            });
                                    });
                                
                                labelIndex++;
                            }
                        }
                    });
                });
            }
        }).GeneratePdf();
    }
    
    private static (float LabelWidth, float LabelHeight, int Columns, int Rows, float QrSize, float FontSize) GetLabelDimensions(LabelSize size)
    {
        return size switch
        {
            LabelSize.Small => (38f, 30f, 5, 9, 18f, 6f),
            LabelSize.Medium => (62f, 50f, 3, 5, 32f, 8f),
            LabelSize.Large => (92f, 70f, 2, 4, 48f, 10f),
            _ => (62f, 50f, 3, 5, 32f, 8f)
        };
    }
    
    private static string TruncateName(string name, LabelSize size)
    {
        var maxLength = size switch
        {
            LabelSize.Small => 12,
            LabelSize.Medium => 20,
            LabelSize.Large => 30,
            _ => 20
        };
        
        if (string.IsNullOrEmpty(name)) return "";
        return name.Length <= maxLength ? name : name.Substring(0, maxLength - 2) + "..";
    }
}
