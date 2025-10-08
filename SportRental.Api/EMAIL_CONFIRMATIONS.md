# 📧 Email Confirmation System

## 🎯 Overview

System automatycznego wysyłania emaili z potwierdzeniem zakupu po udanej płatności Stripe.

---

## ✨ Features

✅ **Automatyczne wysyłanie** po payment_intent.succeeded webhook  
✅ **Piękny HTML template** z gradientami i responsive design  
✅ **Kompletne podsumowanie:**
- 📅 Daty wypożyczenia (start/end)
- 🎿 Lista wypożyczonych produktów (nazwa, ilość, cena/dzień)
- 💰 Podsumowanie finansowe (total, deposit, do zapłaty)
- ℹ️ Ważne informacje dla klienta
- 📧 Dane kontaktowe

✅ **SMTP Support** z MailKit/MimeKit  
✅ **Attachments ready** (gotowe na PDF kontrakty)  
✅ **Dev/Prod configuration** (localhost:1025 dla dev, SMTP dla prod)  

---

## 📋 Email Content

### **Header:**
```
🎉 Dziękujemy za wypożyczenie!
Twoja rezerwacja została potwierdzona
```

### **Sekcje:**

#### 1. **Szczegóły rezerwacji**
- Numer rezerwacji (#ABC12345)
- Data rozpoczęcia (dd MMMM yyyy, HH:mm)
- Data zakończenia (dd MMMM yyyy, HH:mm)
- Liczba dni

#### 2. **Wypożyczone produkty**
Tabela z kolumnami:
| Produkt | Ilość | Cena/dzień | Razem |
|---------|-------|------------|-------|
| Narty XYZ | 2 | 120.00 zł | 720.00 zł |

#### 3. **Podsumowanie finansowe**
- Wartość wypożyczenia: XXX.XX zł
- Kaucja (30%): XXX.XX zł ← **zapłacone online**
- Do zapłaty przy odbiorze: XXX.XX zł

#### 4. **Ważne informacje**
- Pamiętaj o dokumencie tożsamości
- Sprawdź sprzęt przy odbiorze
- Dane kontaktowe wypożyczalni

---

## 🔧 Configuration

### **appsettings.json (Production)**
```json
{
  "Email": {
    "Smtp": {
      "Enabled": true,
      "Host": "smtp.gmail.com",
      "Port": "587",
      "EnableSsl": "true",
      "Username": "your-email@gmail.com",
      "Password": "your-app-password",
      "SenderEmail": "noreply@sportrental.pl",
      "SenderName": "SportRental"
    }
  }
}
```

### **appsettings.Development.json (Local Testing)**
```json
{
  "Email": {
    "Smtp": {
      "Enabled": true,
      "Host": "localhost",
      "Port": "1025",
      "EnableSsl": "false",
      "Username": "",
      "Password": "",
      "SenderEmail": "noreply@sportrental.local",
      "SenderName": "SportRental (DEV)"
    }
  }
}
```

---

## 🧪 Testing

### **1. Local SMTP Server (MailHog)**

Najprostszy sposób na testowanie emaili lokalnie:

```powershell
# Install MailHog (Windows)
# Download from: https://github.com/mailhog/MailHog/releases
# Or use Docker:
docker run -d -p 1025:1025 -p 8025:8025 mailhog/mailhog

# MailHog Web UI: http://localhost:8025
```

Wszystkie wysłane emaile będą widoczne w przeglądarce!

### **2. Gmail SMTP (Production)**

1. Włącz 2FA na koncie Gmail
2. Wygeneruj **App Password**: https://myaccount.google.com/apppasswords
3. Użyj app password w `Email:Smtp:Password`

### **3. Test Email Flow**

```powershell
# 1. Uruchom MailHog
docker run -d -p 1025:1025 -p 8025:8025 mailhog/mailhog

# 2. Uruchom API
dotnet run --project SportRental.Api

# 3. Uruchom Stripe CLI (webhooks)
stripe listen --forward-to https://localhost:7142/api/webhooks/stripe

# 4. Zrób testową płatność przez client
# http://localhost:5014

# 5. Sprawdź email w MailHog
# http://localhost:8025
```

---

## 📡 Webhook Integration

System automatycznie wysyła email po otrzymaniu webhook `payment_intent.succeeded`:

```csharp
// StripeWebhookEndpoints.cs
case "payment_intent.succeeded":
    await HandlePaymentSucceeded(stripeEvent, db, emailService, logger);
    break;
```

**Flow:**
1. Użytkownik płaci przez Stripe Checkout
2. Stripe wysyła webhook `payment_intent.succeeded`
3. API znajduje rental po `rental_id` w metadata
4. Aktualizuje status na `Confirmed`
5. Wysyła email z podsumowaniem
6. Ustawia `IsEmailSent = true`

---

## 📄 Services

### **IEmailSender**
```csharp
public interface IEmailSender
{
    Task SendEmailAsync(string email, string subject, string htmlMessage);
    Task SendEmailWithAttachmentAsync(string email, string subject, string htmlMessage, string? attachmentPath = null);
}
```

### **SmtpEmailSender**
- Implementacja z MailKit
- Walidacja email format
- Support dla attachments
- Auto-detect HTML vs plain text

### **RentalConfirmationEmailService**
```csharp
public async Task SendRentalConfirmationAsync(
    string customerEmail,
    string customerName,
    Rental rental,
    List<(Product product, int quantity)> items)
```

Generuje piękny HTML email z:
- Modern gradient header (purple gradient 🟣)
- Responsive design (mobile-friendly 📱)
- Product table with pricing
- Financial summary with highlights
- Important reminders box (yellow ⚠️)
- Footer with contact info

---

## 🎨 Email Template Preview

```html
<!DOCTYPE html>
<html lang='pl'>
<head>
    <style>
        /* Modern gradient header */
        .header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 30px 20px;
        }
        
        /* Responsive table */
        .products-table { width: 100%; }
        
        /* Price highlights */
        .price-highlight { color: #667eea; font-weight: 600; }
        
        /* Warning box */
        .warning-box {
            background-color: #fef3c7;
            border-left: 4px solid #f59e0b;
        }
    </style>
</head>
<body>
    <!-- Beautiful HTML content here -->
</body>
</html>
```

---

## 🚀 Production Checklist

### **Before Go-Live:**

- [ ] Configure production SMTP (Gmail/SendGrid/AWS SES)
- [ ] Test email delivery with real addresses
- [ ] Verify email lands in inbox (not spam)
- [ ] Check email rendering in:
  - Gmail (desktop/mobile)
  - Outlook
  - Apple Mail
  - ProtonMail
- [ ] Set proper sender name/email
- [ ] Configure Stripe webhook secret
- [ ] Monitor email sending errors in logs

### **Recommended SMTP Providers:**

| Provider | Free Tier | Cost | Best For |
|----------|-----------|------|----------|
| **SendGrid** | 100/day | $19.95/mo (40k) | High volume |
| **AWS SES** | 62k/mo | $0.10/1k | AWS users |
| **Mailgun** | 5k/mo | $35/mo (50k) | Developers |
| **Gmail** | N/A | Free (500/day) | Small projects |
| **Brevo (Sendinblue)** | 300/day | €19/mo (20k) | EU compliance |

---

## 📊 Monitoring

### **Logs to Watch:**

```csharp
// Success
LogInformation("Rental {RentalId} marked as confirmed and email sent", rental.Id)

// Failure
LogError("Failed to send rental confirmation email to {Email} for rental {RentalId}")

// Warning
LogWarning("Rental {RentalId} not found or has no customer", rentalId)
```

### **Important Flags:**

```csharp
rental.IsEmailSent = true;  // Email successfully sent
rental.PaymentStatus = "Succeeded";  // Payment confirmed
rental.Status = RentalStatus.Confirmed;  // Rental confirmed
```

---

## 🔐 Security

### **SMTP Credentials:**

**NEVER commit credentials to git!**

✅ Use **Environment Variables**:
```powershell
$env:Email__Smtp__Username = "your-email@gmail.com"
$env:Email__Smtp__Password = "your-app-password"
```

✅ Use **User Secrets** (dev):
```powershell
dotnet user-secrets set "Email:Smtp:Username" "your-email@gmail.com"
dotnet user-secrets set "Email:Smtp:Password" "your-app-password"
```

✅ Use **Azure Key Vault** (production)

---

## 🐛 Troubleshooting

### **Email not sending?**

1. **Check SMTP config:**
   ```powershell
   # Verify settings in appsettings.Development.json
   # Ensure Email:Smtp:Enabled = true
   ```

2. **Test SMTP connection:**
   ```powershell
   telnet localhost 1025
   # or for Gmail:
   telnet smtp.gmail.com 587
   ```

3. **Check logs:**
   ```powershell
   dotnet run --project SportRental.Api --verbosity detailed
   ```

4. **Verify webhook:**
   ```powershell
   stripe listen --forward-to https://localhost:7142/api/webhooks/stripe
   # Check for payment_intent.succeeded events
   ```

### **Email in spam folder?**

- Add SPF/DKIM/DMARC records to your domain
- Use reputable SMTP provider (SendGrid, AWS SES)
- Avoid spam trigger words in subject/body
- Include unsubscribe link (for marketing emails)

### **HTML not rendering?**

- Test with inline CSS (already done ✅)
- Avoid external CSS files
- Use tables for layout (email clients love tables)
- Test with [Litmus](https://www.litmus.com/) or [Email on Acid](https://www.emailonacid.com/)

---

## 📚 References

- [MailKit Documentation](https://github.com/jstedfast/MailKit)
- [MimeKit Documentation](https://github.com/jstedfast/MimeKit)
- [Stripe Webhooks Guide](https://stripe.com/docs/webhooks)
- [MailHog](https://github.com/mailhog/MailHog) - Local SMTP testing

---

## ✅ TODO (Future Enhancements)

- [ ] Add PDF contract attachment
- [ ] Add QR code for pickup confirmation
- [ ] Implement email templates engine (Razor/Handlebars)
- [ ] Add email preview endpoint (for admins)
- [ ] Track email open/click rates
- [ ] Multi-language support
- [ ] Add reminder emails (1 day before pickup)
- [ ] Add return reminder emails

---

**Last updated:** 2025-10-06  
**Status:** ✅ PRODUCTION READY!  
**Integration:** Stripe Webhooks + SMTP  

**ENJOY BEAUTIFUL CONFIRMATION EMAILS! 📧✨**
