using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;

namespace SportRental.Admin.Services.Contracts;

internal sealed record RentalContractLine(
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal Total);

internal sealed record RentalContractRegulations(
    string Text,
    string? Version,
    string? Hash,
    string? Source);

internal sealed record RentalContractBranding(
    string PrimaryColor,
    string SecondaryColor,
    string PrimaryTextColor,
    string PrimaryInkColor,
    string SecondaryInkColor,
    byte[]? LogoBytes = null);

internal sealed record RentalContractDocumentModel(
    string Reference,
    DateTime IssuedAt,
    DateTime StartsAt,
    DateTime EndsAt,
    string DurationText,
    string PriceUnitLabel,
    string CompanyName,
    IReadOnlyList<string> CompanyDetails,
    string CustomerName,
    IReadOnlyList<string> CustomerDetails,
    IReadOnlyList<RentalContractLine> Items,
    decimal TotalAmount,
    decimal DepositAmount,
    IReadOnlyList<string> Terms,
    string? Notes,
    RentalContractRegulations? Regulations,
    RentalContractBranding Branding);

internal static class RentalContractDocument
{
    private const string ContractFontFamily = "Outfit";
    private static readonly string[] ContractFontResources =
    [
        "SportRental.Assets.Outfit-Regular.ttf",
        "SportRental.Assets.Outfit-SemiBold.ttf",
        "SportRental.Assets.Outfit-Bold.ttf"
    ];
    private static readonly object FontRegistrationLock = new();
    private static bool _fontRegistered;
    private const string Ink = "#182230";
    private const string Muted = "#667085";
    private const string Border = "#D9E0EA";
    private const string Surface = "#F7F9FC";

    public static byte[] Generate(RentalContractDocumentModel model)
    {
        EnsureContractFontRegistered();
        var pl = CultureInfo.GetCultureInfo("pl-PL");
        using var logo = model.Branding.LogoBytes is { Length: > 0 }
            ? QuestPDF.Infrastructure.Image.FromBinaryData(model.Branding.LogoBytes.ToArray())
            : null;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(1.45f, Unit.Centimetre);
                page.MarginVertical(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(style => style
                    .FontFamily(ContractFontFamily)
                    .FontSize(9)
                    .FontColor(Ink));

                page.Header().Column(header =>
                {
                    header.Item().Height(5).Background(model.Branding.SecondaryColor);
                    header.Item().PaddingVertical(9).Row(row =>
                    {
                        row.RelativeItem().Column(company =>
                        {
                            if (model.Branding.LogoBytes is { Length: > 0 })
                            {
                                company.Item().Height(34).Width(130).Element(container => ComposeLogo(container, logo));
                                company.Item().PaddingTop(3).Text(model.CompanyName)
                                    .Bold().FontSize(9).FontColor(model.Branding.PrimaryInkColor);
                            }
                            else
                            {
                                company.Item().Text(model.CompanyName)
                                    .Bold().FontSize(16).FontColor(model.Branding.PrimaryInkColor);
                                company.Item().Text("Wypożyczalnia sprzętu sportowego")
                                    .FontSize(8).FontColor(Muted);
                            }
                        });

                        row.ConstantItem(18);
                        row.ConstantItem(235).AlignRight().Column(title =>
                        {
                            title.Item().AlignRight().Text("UMOWA WYPOŻYCZENIA")
                                .Bold().FontSize(17).FontColor(model.Branding.PrimaryInkColor);
                            title.Item().AlignRight().Text($"NR {model.Reference}")
                                .Bold().FontSize(10).FontColor(model.Branding.SecondaryInkColor);
                            title.Item().AlignRight().Text($"Zawarta {model.IssuedAt:dd.MM.yyyy}")
                                .FontSize(8).FontColor(Muted);
                        });
                    });
                    header.Item().LineHorizontal(1).LineColor(model.Branding.PrimaryColor);
                });

                page.Content().PaddingVertical(10).Column(content =>
                {
                    ComposeSectionHeader(content.Item(), "STRONY UMOWY", model.Branding);
                    content.Item().Row(row =>
                    {
                        row.RelativeItem().Element(card => ComposePartyCard(
                            card,
                            "WYPOŻYCZAJĄCY",
                            model.CompanyName,
                            model.CompanyDetails,
                            model.Branding));
                        row.ConstantItem(10);
                        row.RelativeItem().Element(card => ComposePartyCard(
                            card,
                            "NAJEMCA",
                            model.CustomerName,
                            model.CustomerDetails,
                            model.Branding));
                    });

                    content.Item().PaddingTop(10);
                    ComposeSectionHeader(content.Item(), "OKRES WYPOŻYCZENIA", model.Branding);
                    content.Item().Row(row =>
                    {
                        row.RelativeItem().Element(card => ComposeMetric(
                            card, "ODBIÓR SPRZĘTU", model.StartsAt.ToString("dd.MM.yyyy, HH:mm"), model.Branding));
                        row.ConstantItem(8);
                        row.RelativeItem().Element(card => ComposeMetric(
                            card, "PLANOWANY ZWROT", model.EndsAt.ToString("dd.MM.yyyy, HH:mm"), model.Branding));
                        row.ConstantItem(8);
                        row.ConstantItem(112).Element(card => ComposeMetric(
                            card, "CZAS NAJMU", model.DurationText, model.Branding));
                    });

                    content.Item().PaddingTop(10);
                    ComposeSectionHeader(content.Item(), "WYPOŻYCZONY SPRZĘT", model.Branding);
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(28);
                            columns.RelativeColumn(5);
                            columns.ConstantColumn(48);
                            columns.ConstantColumn(82);
                            columns.ConstantColumn(82);
                        });

                        table.Header(header =>
                        {
                            HeaderCell(header.Cell(), "LP.", model.Branding);
                            HeaderCell(header.Cell(), "SPRZĘT", model.Branding);
                            HeaderCell(header.Cell(), "ILOŚĆ", model.Branding, center: true);
                            HeaderCell(header.Cell(), model.PriceUnitLabel, model.Branding, right: true);
                            HeaderCell(header.Cell(), "WARTOŚĆ", model.Branding, right: true);
                        });

                        for (var index = 0; index < model.Items.Count; index++)
                        {
                            var item = model.Items[index];
                            var background = index % 2 == 0 ? "#FFFFFF" : Surface;
                            BodyCell(table.Cell(), (index + 1).ToString(pl), background);
                            BodyCell(table.Cell(), item.Name, background, bold: true);
                            BodyCell(table.Cell(), item.Quantity.ToString(pl), background, center: true);
                            BodyCell(table.Cell(), $"{item.UnitPrice.ToString("N2", pl)} zł", background, right: true);
                            BodyCell(table.Cell(), $"{item.Total.ToString("N2", pl)} zł", background, bold: true, right: true);
                        }
                    });

                    content.Item().PaddingTop(7).AlignRight().Width(250).Border(1).BorderColor(Border)
                        .Background(Surface).Padding(9).Column(summary =>
                        {
                            summary.Item().Row(row =>
                            {
                                row.RelativeItem().Text("Wartość wypożyczenia").FontColor(Muted);
                                row.ConstantItem(100).AlignRight().Text($"{model.TotalAmount.ToString("N2", pl)} zł").SemiBold();
                            });
                            summary.Item().PaddingTop(3).Row(row =>
                            {
                                row.RelativeItem().Text("Kaucja zwrotna").FontColor(Muted);
                                row.ConstantItem(100).AlignRight().Text(
                                    model.DepositAmount > 0
                                        ? $"{model.DepositAmount.ToString("N2", pl)} zł"
                                        : "brak").SemiBold();
                            });
                            summary.Item().PaddingTop(6).LineHorizontal(1).LineColor(Border);
                            summary.Item().PaddingTop(6).Row(row =>
                            {
                                row.RelativeItem().Text("WARTOŚĆ UMOWY").Bold().FontSize(10);
                                row.ConstantItem(110).AlignRight().Text($"{model.TotalAmount.ToString("N2", pl)} zł")
                                    .Bold().FontSize(12).FontColor(model.Branding.PrimaryInkColor);
                            });
                        });

                    content.Item().PaddingTop(10);
                    ComposeSectionHeader(content.Item(), "WARUNKI UMOWY", model.Branding);
                    content.Item().Border(1).BorderColor(Border).Padding(9).Column(terms =>
                    {
                        for (var index = 0; index < model.Terms.Count; index++)
                        {
                            var term = model.Terms[index];
                            terms.Item().PaddingBottom(index == model.Terms.Count - 1 ? 0 : 4).Row(row =>
                            {
                                row.ConstantItem(22).Text($"{index + 1}.")
                                    .Bold().FontColor(model.Branding.PrimaryInkColor);
                                row.RelativeItem().Text(term).FontSize(8.5f).LineHeight(1.2f);
                            });
                        }
                    });

                    if (model.Regulations != null)
                    {
                        content.Item().PaddingTop(8).Background("#EEF6FF").BorderLeft(3)
                            .BorderColor(model.Branding.PrimaryColor).Padding(8).Text(text =>
                            {
                                text.Span("ZAAKCEPTOWANY REGULAMIN  ").Bold()
                                    .FontColor(model.Branding.PrimaryInkColor);
                                text.Span(
                                    $"Regulamin w wersji {model.Regulations.Version ?? "bez oznaczenia wersji"} " +
                                    "stanowi integralną część umowy i znajduje się w załączniku.")
                                    .FontSize(8.5f);
                            });
                    }

                    if (!string.IsNullOrWhiteSpace(model.Notes))
                    {
                        content.Item().PaddingTop(8).Background("#FFF8E7").BorderLeft(3)
                            .BorderColor(model.Branding.SecondaryColor).Padding(8).Column(notes =>
                            {
                                notes.Item().Text("UWAGI DO WYDANIA").Bold().FontSize(8)
                                    .FontColor(model.Branding.PrimaryInkColor);
                                notes.Item().PaddingTop(2).Text(model.Notes).FontSize(8.5f);
                            });
                    }

                    content.Item().PaddingTop(16).ShowEntire().Row(row =>
                    {
                        row.RelativeItem().Element(signature => ComposeSignature(
                            signature, "Wypożyczający", model.CompanyName));
                        row.ConstantItem(36);
                        row.RelativeItem().Element(signature => ComposeSignature(
                            signature, "Najemca", model.CustomerName));
                    });

                    if (model.Regulations != null)
                    {
                        content.Item().PageBreak();
                        ComposeSectionHeader(
                            content.Item(),
                            "ZAŁĄCZNIK 1 — ZAAKCEPTOWANY REGULAMIN",
                            model.Branding);
                        content.Item().Border(1).BorderColor(Border).Background(Surface).Padding(9)
                            .Column(metadata =>
                            {
                                MetadataLine(metadata, "Wersja", model.Regulations.Version ?? "brak oznaczenia");
                                MetadataLine(metadata, "Źródło", FormatRegulationsSource(model.Regulations.Source));
                                if (!string.IsNullOrWhiteSpace(model.Regulations.Hash))
                                    MetadataLine(metadata, "SHA-256", FormatHash(model.Regulations.Hash));
                            });
                        content.Item().PaddingTop(10).Column(regulations =>
                            ComposeRegulations(regulations, model.Regulations.Text, model.Branding));
                    }
                });

                page.Footer().Column(footer =>
                {
                    footer.Item().LineHorizontal(0.6f).LineColor(Border);
                    footer.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem().Text(model.CompanyName).Bold().FontSize(7).FontColor(Muted);
                        row.RelativeItem().AlignCenter().Text(text =>
                        {
                            text.Span("Strona ").FontSize(7).FontColor(Muted);
                            text.CurrentPageNumber().FontSize(7).FontColor(Muted);
                            text.Span(" z ").FontSize(7).FontColor(Muted);
                            text.TotalPages().FontSize(7).FontColor(Muted);
                        });
                        row.RelativeItem().AlignRight().Text("Dokument wygenerowany w RentSpot")
                            .FontSize(7).FontColor(Muted);
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void EnsureContractFontRegistered()
    {
        if (_fontRegistered)
            return;

        lock (FontRegistrationLock)
        {
            if (_fontRegistered)
                return;

            foreach (var resource in ContractFontResources)
            {
                using var stream = typeof(RentalContractDocument).Assembly
                    .GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException(
                        $"Brak osadzonego fontu umów: {resource}");
                FontManager.RegisterFont(stream);
            }
            _fontRegistered = true;
        }
    }

    private static void ComposeLogo(IContainer container, QuestPDF.Infrastructure.Image? logo)
    {
        if (logo == null)
            return;

        // Jawnie utworzony obiekt Image ma własny cykl życia dokumentu i nie
        // korzysta z globalnego cache bajtów pomiędzy kolejnymi umowami.
        container.Image(logo).FitArea();
    }

    private static void ComposeSectionHeader(
        IContainer container,
        string title,
        RentalContractBranding branding)
    {
        container.PaddingBottom(5).Row(row =>
        {
            row.ConstantItem(5).Height(15).Background(branding.SecondaryColor);
            row.ConstantItem(8);
            row.RelativeItem().Text(title).Bold().FontSize(10).FontColor(branding.PrimaryInkColor);
        });
    }

    private static void ComposePartyCard(
        IContainer container,
        string role,
        string name,
        IReadOnlyList<string> details,
        RentalContractBranding branding)
    {
        container.Border(1).BorderColor(Border).Background(Surface).Padding(9).Column(card =>
        {
            card.Item().Text(role).Bold().FontSize(7.5f).FontColor(branding.SecondaryInkColor);
            card.Item().PaddingTop(2).Text(name).Bold().FontSize(10).FontColor(branding.PrimaryInkColor);

            foreach (var detail in details.Where(value => !string.IsNullOrWhiteSpace(value)))
                card.Item().PaddingTop(1).Text(detail).FontSize(8).FontColor(Muted);
        });
    }

    private static void ComposeMetric(
        IContainer container,
        string label,
        string value,
        RentalContractBranding branding)
    {
        container.Border(1).BorderColor(Border).Padding(8).Column(metric =>
        {
            metric.Item().Text(label).Bold().FontSize(7).FontColor(Muted);
            metric.Item().PaddingTop(3).Text(value).Bold().FontSize(9.5f)
                .FontColor(branding.PrimaryInkColor);
        });
    }

    private static void HeaderCell(
        IContainer container,
        string text,
        RentalContractBranding branding,
        bool center = false,
        bool right = false)
    {
        var cell = container.Background(branding.PrimaryColor).PaddingVertical(6).PaddingHorizontal(5);
        if (center)
            cell = cell.AlignCenter();
        else if (right)
            cell = cell.AlignRight();

        cell.Text(text).Bold().FontSize(7).FontColor(branding.PrimaryTextColor);
    }

    private static void BodyCell(
        IContainer container,
        string text,
        string background,
        bool bold = false,
        bool center = false,
        bool right = false)
    {
        var cell = container.Background(background).BorderBottom(0.5f).BorderColor(Border)
            .PaddingVertical(6).PaddingHorizontal(5);
        if (center)
            cell = cell.AlignCenter();
        else if (right)
            cell = cell.AlignRight();

        var descriptor = cell.Text(text).FontSize(8.5f);
        if (bold)
            descriptor.SemiBold();
    }

    private static void ComposeSignature(IContainer container, string role, string name)
    {
        container.PaddingTop(20).Column(signature =>
        {
            signature.Item().LineHorizontal(0.7f).LineColor(Muted);
            signature.Item().PaddingTop(4).AlignCenter().Text(role).Bold().FontSize(8);
            signature.Item().AlignCenter().Text(name).FontSize(7.5f).FontColor(Muted);
        });
    }

    private static void MetadataLine(ColumnDescriptor column, string label, string value)
    {
        column.Item().PaddingBottom(2).Text(text =>
        {
            text.Span($"{label}: ").SemiBold().FontColor(Muted);
            text.Span(value).FontSize(8);
        });
    }

    private static void ComposeRegulations(
        ColumnDescriptor column,
        string text,
        RentalContractBranding branding)
    {
        var paragraphs = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var paragraph in paragraphs)
        {
            var lines = paragraph
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
                continue;

            var firstLine = lines[0];
            var isNumberedHeading = System.Text.RegularExpressions.Regex.IsMatch(
                firstLine,
                @"^\d+[.)]\s+\S",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            var isDocumentTitle = firstLine.All(character =>
                !char.IsLetter(character) || char.IsUpper(character));

            if (isNumberedHeading || isDocumentTitle)
            {
                column.Item().PaddingTop(4).Text(firstLine).Bold().FontSize(isDocumentTitle ? 11 : 9)
                    .FontColor(branding.PrimaryInkColor);
                if (lines.Length > 1)
                    column.Item().PaddingTop(2).PaddingBottom(4)
                        .Text(string.Join("\n", lines.Skip(1))).FontSize(8.2f).LineHeight(1.25f);
            }
            else
            {
                column.Item().PaddingBottom(5).Text(string.Join("\n", lines))
                    .FontSize(8.2f).LineHeight(1.25f);
            }
        }
    }

    private static string FormatHash(string hash)
    {
        var compact = new string(hash.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        return compact.Length == 0
            ? hash
            : string.Join(" ", Enumerable.Range(0, (compact.Length + 7) / 8)
                .Select(index => compact.Substring(index * 8, Math.Min(8, compact.Length - index * 8))));
    }

    private static string FormatRegulationsSource(string? source)
        => source?.Trim().ToLowerInvariant() switch
        {
            "tenant" or "tenantcustom" or "tenant-custom" => "regulamin wypożyczalni",
            "platformdefault" or "platform-default" or "default" => "standard RentSpot",
            { Length: > 0 } value => value,
            _ => "nieoznaczone"
        };
}
