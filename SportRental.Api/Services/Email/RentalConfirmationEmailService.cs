using SportRental.Infrastructure.Domain;
using System.Text;
using SportRental.Api.Services.Contracts;

namespace SportRental.Api.Services.Email;

/// <summary>
/// Service for generating and sending rental confirmation emails
/// </summary>
public class RentalConfirmationEmailService
{
    private readonly IEmailSender _emailSender;
    private readonly IPdfContractService _pdfService;
    private readonly ILogger<RentalConfirmationEmailService> _logger;

    public RentalConfirmationEmailService(
        IEmailSender emailSender,
        IPdfContractService pdfService,
        ILogger<RentalConfirmationEmailService> logger)
    {
        _emailSender = emailSender;
        _pdfService = pdfService;
        _logger = logger;
    }

    /// <summary>
    /// Send rental confirmation email after successful payment (with PDF contract attachment)
    /// </summary>
    public async Task SendRentalConfirmationAsync(
        string customerEmail,
        string customerName,
        Customer customer,
        Rental rental,
        List<(Product product, int quantity)> items,
        CompanyInfo? companyInfo = null)
    {
        try
        {
            var subject = $"Potwierdzenie wypożyczenia - SportRental #{rental.Id.ToString()[..8]}";
            var htmlBody = GenerateConfirmationEmailHtml(customerName, rental, items);

            // Generate PDF contract
            byte[]? contractPdf = null;
            string? tempPdfPath = null;
            
            try
            {
                contractPdf = await _pdfService.GenerateContractPdfAsync(rental, customer, items, companyInfo);
                
                // Save to temp file for attachment
                tempPdfPath = Path.Combine(Path.GetTempPath(), $"umowa_{rental.Id}_{Guid.NewGuid()}.pdf");
                await File.WriteAllBytesAsync(tempPdfPath, contractPdf);
                
                _logger.LogInformation("Generated PDF contract for rental {RentalId}, size: {Size} bytes", 
                    rental.Id, contractPdf.Length);
            }
            catch (Exception pdfEx)
            {
                _logger.LogWarning(pdfEx, "Failed to generate PDF for rental {RentalId}, sending email without attachment", rental.Id);
            }

            // Send email with or without PDF attachment
            if (!string.IsNullOrEmpty(tempPdfPath) && File.Exists(tempPdfPath))
            {
                await _emailSender.SendEmailWithAttachmentAsync(customerEmail, subject, htmlBody, tempPdfPath);
                _logger.LogInformation("Email sent WITH PDF attachment to {Email} for rental {RentalId}", 
                    customerEmail, rental.Id);
            }
            else
            {
                await _emailSender.SendEmailAsync(customerEmail, subject, htmlBody);
                _logger.LogInformation("Email sent WITHOUT PDF attachment to {Email} for rental {RentalId}", 
                    customerEmail, rental.Id);
            }

            // Cleanup temp PDF
            try
            {
                if (!string.IsNullOrEmpty(tempPdfPath) && File.Exists(tempPdfPath))
                {
                    File.Delete(tempPdfPath);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to cleanup temp PDF file: {Path}", tempPdfPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send rental confirmation email to {Email} for rental {RentalId}",
                customerEmail,
                rental.Id);
            // Don't throw - email failure shouldn't fail the payment
        }
    }

    /// <summary>
    /// Generate beautiful HTML email template
    /// </summary>
    private string GenerateConfirmationEmailHtml(
        string customerName,
        Rental rental,
        List<(Product product, int quantity)> items)
    {
        var rentalDays = (rental.EndDateUtc - rental.StartDateUtc).Days;
        if (rentalDays <= 0) rentalDays = 1;

        var html = new StringBuilder();
        html.AppendLine(@"
<!DOCTYPE html>
<html lang='pl'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Potwierdzenie wypożyczenia</title>
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }
        .email-container {
            background-color: #ffffff;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            overflow: hidden;
        }
        .header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 30px 20px;
            text-align: center;
        }
        .header h1 {
            margin: 0;
            font-size: 28px;
            font-weight: 600;
        }
        .header .subtitle {
            margin-top: 8px;
            font-size: 14px;
            opacity: 0.9;
        }
        .content {
            padding: 30px 20px;
        }
        .success-badge {
            background-color: #10b981;
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            display: inline-block;
            font-weight: 600;
            font-size: 14px;
            margin-bottom: 20px;
        }
        .section {
            margin-bottom: 30px;
        }
        .section-title {
            font-size: 18px;
            font-weight: 600;
            color: #1f2937;
            margin-bottom: 12px;
            border-bottom: 2px solid #e5e7eb;
            padding-bottom: 8px;
        }
        .info-box {
            background-color: #f9fafb;
            border-left: 4px solid #667eea;
            padding: 16px;
            border-radius: 4px;
            margin-bottom: 16px;
        }
        .info-row {
            display: flex;
            justify-content: space-between;
            padding: 8px 0;
            border-bottom: 1px solid #e5e7eb;
        }
        .info-row:last-child {
            border-bottom: none;
        }
        .info-label {
            font-weight: 600;
            color: #6b7280;
        }
        .info-value {
            color: #1f2937;
            text-align: right;
        }
        .products-table {
            width: 100%;
            border-collapse: collapse;
            margin-top: 12px;
        }
        .products-table th {
            background-color: #f3f4f6;
            padding: 12px;
            text-align: left;
            font-weight: 600;
            color: #374151;
            border-bottom: 2px solid #e5e7eb;
        }
        .products-table td {
            padding: 12px;
            border-bottom: 1px solid #e5e7eb;
        }
        .products-table tr:last-child td {
            border-bottom: none;
        }
        .price-highlight {
            font-weight: 600;
            color: #667eea;
        }
        .total-row {
            background-color: #f9fafb;
            font-weight: 700;
            font-size: 16px;
        }
        .footer {
            background-color: #f9fafb;
            padding: 20px;
            text-align: center;
            border-top: 1px solid #e5e7eb;
        }
        .footer p {
            margin: 5px 0;
            color: #6b7280;
            font-size: 14px;
        }
        .cta-button {
            display: inline-block;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 12px 24px;
            text-decoration: none;
            border-radius: 6px;
            font-weight: 600;
            margin: 20px 0;
        }
        .warning-box {
            background-color: #fef3c7;
            border-left: 4px solid #f59e0b;
            padding: 16px;
            border-radius: 4px;
            margin-top: 20px;
        }
        .emoji {
            font-size: 24px;
            margin-right: 8px;
        }
        @media only screen and (max-width: 600px) {
            .products-table th,
            .products-table td {
                padding: 8px;
                font-size: 14px;
            }
        }
    </style>
</head>
<body>
    <div class='email-container'>
        <div class='header'>
            <h1>🎉 Dziękujemy za wypożyczenie!</h1>
            <div class='subtitle'>Twoja rezerwacja została potwierdzona</div>
        </div>
        
        <div class='content'>
            <div class='success-badge'>✓ Płatność potwierdzona</div>
            
            <p style='font-size: 16px; margin-bottom: 24px;'>
                Cześć <strong>" + customerName + @"</strong>,
            </p>
            
            <p style='margin-bottom: 24px;'>
                Cieszymy się, że wybrałeś nasz wypożyczalnię! Twoja płatność została pomyślnie przetworzona, 
                a rezerwacja jest już potwierdzona.
            </p>");

        // SZCZEGÓŁY REZERWACJI
        html.AppendLine(@"
            <div class='section'>
                <div class='section-title'>📅 Szczegóły rezerwacji</div>
                <div class='info-box'>
                    <div class='info-row'>
                        <span class='info-label'>Numer rezerwacji:</span>
                        <span class='info-value'><strong>#" + rental.Id.ToString()[..8] + @"</strong></span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Data rozpoczęcia:</span>
                        <span class='info-value'>" + rental.StartDateUtc.ToString("dd MMMM yyyy, HH:mm") + @"</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Data zakończenia:</span>
                        <span class='info-value'>" + rental.EndDateUtc.ToString("dd MMMM yyyy, HH:mm") + @"</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Liczba dni:</span>
                        <span class='info-value'><strong>" + rentalDays + @" dni</strong></span>
                    </div>
                </div>
            </div>");

        // WYPOŻYCZONE PRODUKTY
        html.AppendLine(@"
            <div class='section'>
                <div class='section-title'>🎿 Wypożyczone produkty</div>
                <table class='products-table'>
                    <thead>
                        <tr>
                            <th>Produkt</th>
                            <th style='text-align: center;'>Ilość</th>
                            <th style='text-align: right;'>Cena/dzień</th>
                            <th style='text-align: right;'>Razem</th>
                        </tr>
                    </thead>
                    <tbody>");

        foreach (var (product, quantity) in items)
        {
            var itemTotal = product.DailyPrice * quantity * rentalDays;
            html.AppendLine($@"
                        <tr>
                            <td><strong>{product.Name}</strong></td>
                            <td style='text-align: center;'>{quantity}</td>
                            <td style='text-align: right;'>{product.DailyPrice:F2} zł</td>
                            <td style='text-align: right;' class='price-highlight'>{itemTotal:F2} zł</td>
                        </tr>");
        }

        html.AppendLine(@"
                    </tbody>
                </table>
            </div>");

        // PODSUMOWANIE FINANSOWE
        html.AppendLine($@"
            <div class='section'>
                <div class='section-title'>💰 Podsumowanie finansowe</div>
                <div class='info-box'>
                    <div class='info-row'>
                        <span class='info-label'>Wartość wypożyczenia:</span>
                        <span class='info-value'>{rental.TotalAmount:F2} zł</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Kaucja (30%):</span>
                        <span class='info-value price-highlight'>{rental.DepositAmount:F2} zł</span>
                    </div>
                    <div class='info-row total-row'>
                        <span class='info-label'>Zapłacono:</span>
                        <span class='info-value price-highlight'>{rental.DepositAmount:F2} zł</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Do zapłaty przy odbiorze:</span>
                        <span class='info-value'>{(rental.TotalAmount - rental.DepositAmount):F2} zł</span>
                    </div>
                </div>
            </div>");

        // INFORMACJE O ODBIORZE
        html.AppendLine(@"
            <div class='warning-box'>
                <p style='margin: 0; font-weight: 600;'>
                    <span class='emoji'>ℹ️</span> Ważne informacje:
                </p>
                <ul style='margin: 12px 0 0 0; padding-left: 24px;'>
                    <li>Pamiętaj, aby stawić się w punkcie wypożyczalni w dniu <strong>" + rental.StartDateUtc.ToString("dd.MM.yyyy") + @"</strong></li>
                    <li>Zabierz ze sobą <strong>dokument tożsamości</strong></li>
                    <li>Sprawdź sprzęt przy odbiorze</li>
                    <li>Pozostała kwota do zapłaty: <strong>" + (rental.TotalAmount - rental.DepositAmount).ToString("F2") + @" zł</strong></li>
                </ul>
            </div>
            
            <div style='text-align: center; margin-top: 30px;'>
                <p style='color: #6b7280; font-size: 14px;'>
                    W razie pytań, skontaktuj się z nami:<br>
                    📧 <a href='mailto:kontakt@sportrental.pl' style='color: #667eea;'>kontakt@sportrental.pl</a><br>
                    📞 <strong>+48 123 456 789</strong>
                </p>
            </div>
        </div>
        
        <div class='footer'>
            <p><strong>SportRental</strong></p>
            <p>Profesjonalne wypożyczalnie sprzętu sportowego</p>
            <p style='font-size: 12px; color: #9ca3af; margin-top: 16px;'>
                Ten email został wysłany automatycznie. Prosimy nie odpowiadać.
            </p>
        </div>
    </div>
</body>
</html>");

        return html.ToString();
    }
}
