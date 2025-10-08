# Stripe Payment Integration - SportRental

## 📋 Przegląd

Kompletna integracja Stripe dla wypożyczalni sportowej z następującymi funkcjami:
- ✅ **Payment Intents** dla depozytów (kaucji) i płatności końcowych
- ✅ **Automatic Payment Methods** (karty, BLIK, przelewy - wszystko co wspiera Stripe)
- ✅ **Webhooks** dla asynchronicznych zdarzeń płatności
- ✅ **Refunds** - zwroty płatności
- ✅ **Multi-tenant** - pełne wsparcie dla wielu wypożyczalni
- ✅ **Metadata tracking** - szczegóły rezerwacji w Stripe

## 🔧 Konfiguracja

### 1. Klucze API

Dodaj klucze Stripe do `appsettings.Development.json` (lub User Secrets):

```json
{
  "Stripe": {
    "SecretKey": "sk_test_YOUR_SECRET_KEY",
    "PublishableKey": "pk_test_YOUR_PUBLISHABLE_KEY",
    "WebhookSecret": "whsec_YOUR_WEBHOOK_SECRET",
    "Currency": "pln"
  }
}
```

**⚠️ WAŻNE:** W produkcji użyj **Azure Key Vault** lub zmiennych środowiskowych!

### 2. Stripe CLI (Testowanie lokalnie)

```bash
# Zaloguj się do Stripe
stripe login

# Przekieruj webhooks lokalne
stripe listen --forward-to https://localhost:7142/api/webhooks/stripe

# Skopiuj webhook secret (whsec_...) do appsettings
```

## 📝 API Endpoints

### POST /api/payments/quote
Oblicza kwotę płatności (bez tworzenia PaymentIntent)

**Request:**
```json
{
  "startDateUtc": "2025-10-10T10:00:00Z",
  "endDateUtc": "2025-10-15T10:00:00Z",
  "items": [
    { "productId": "guid", "quantity": 1 }
  ]
}
```

**Response:**
```json
{
  "totalAmount": 500.00,
  "depositAmount": 150.00,
  "currency": "PLN",
  "rentalDays": 5
}
```

### POST /api/payments/intents
Tworzy Stripe Payment Intent

**Request:**
```json
{
  "startDateUtc": "2025-10-10T10:00:00Z",
  "endDateUtc": "2025-10-15T10:00:00Z",
  "items": [
    { "productId": "guid", "quantity": 1 }
  ],
  "currency": "PLN"
}
```

**Response:**
```json
{
  "id": "pi_3ABC...",
  "amount": 500.00,
  "depositAmount": 150.00,
  "currency": "PLN",
  "status": "RequiresPaymentMethod",
  "createdAtUtc": "2025-10-05T12:00:00Z",
  "expiresAtUtc": "2025-10-06T12:00:00Z",
  "clientSecret": "pi_3ABC_secret_DEF"
}
```

**Client Secret** jest używany przez frontend (Stripe.js) do dokończenia płatności.

### GET /api/payments/intents/{id}
Pobiera status PaymentIntent

**Response:**
```json
{
  "id": "pi_3ABC...",
  "amount": 500.00,
  "status": "Succeeded",
  ...
}
```

## 🔐 Payment Flow

### 1. Klient wybiera produkty i daty

```
Frontend → POST /api/payments/quote → Otrzymuje total/deposit
```

### 2. Frontend tworzy Payment Intent

```
Frontend → POST /api/payments/intents → Otrzymuje clientSecret
```

### 3. Frontend używa Stripe.js do zebrania płatności

```javascript
const stripe = Stripe('pk_test_...');

const { error } = await stripe.confirmPayment({
  elements,
  clientSecret: paymentIntent.clientSecret,
  confirmParams: {
    return_url: 'https://yourdomain.com/checkout/success'
  }
});
```

### 4. Stripe wysyła webhook do backend

```
Stripe → POST /api/webhooks/stripe → Backend aktualizuje status rezerwacji
```

## 📡 Webhooks

Backend obsługuje następujące zdarzenia:

| Zdarzenie Stripe | Akcja Backend |
|------------------|---------------|
| `payment_intent.succeeded` | Oznacz rezerwację jako opłaconą, wyślij email potwierdzenia |
| `payment_intent.payment_failed` | Oznacz jako niepowodzenie, powiadom klienta |
| `payment_intent.canceled` | Anuluj rezerwację |
| `charge.refunded` | Oznacz jako zwróconą, powiadom klienta |

### Konfiguracja Webhooks (Produkcja)

1. W Stripe Dashboard → **Developers → Webhooks**
2. Dodaj endpoint: `https://yourdomain.com/api/webhooks/stripe`
3. Wybierz zdarzenia: `payment_intent.*`, `charge.refunded`
4. Skopiuj **Signing Secret** i dodaj do konfiguracji

## 💰 Kwoty i Waluty

- Wszystkie kwoty w API są w **złotych** (PLN)
- Stripe przechowuje kwoty w **groszach** (conversion: PLN * 100)
- Deposit (kaucja) = **30% całkowitej kwoty**
- Final payment (pozostała kwota) = **70%**

## 🔄 Refundy

Zwrot pieniędzy za anulowaną rezerwację:

```csharp
var gateway = serviceProvider.GetRequiredService<IPaymentGateway>();
await gateway.RefundPaymentAsync(tenantId, paymentIntentId, amount: 100.00m, reason: "requested_by_customer");
```

Powody zwrotu:
- `"requested_by_customer"` - Klient poprosił
- `"duplicate"` - Duplikat płatności
- `"fraudulent"` - Podejrzana transakcja

## 🧪 Testowanie

### Test Cards (Stripe Test Mode)

| Karta | Wynik |
|-------|-------|
| `4242 4242 4242 4242` | Sukces ✅ |
| `4000 0000 0000 0002` | Card declined ❌ |
| `4000 0000 0000 9995` | Insufficient funds ❌ |
| `4000 0025 0000 3155` | Requires authentication (3D Secure) 🔐 |

**CVV:** Dowolne 3 cyfry  
**Data wygaśnięcia:** Dowolna przyszła data  
**ZIP:** Dowolny

### Testowanie w Swagger UI

1. Otwórz `https://localhost:7142/swagger`
2. Zaloguj się (JWT token z `/api/auth/login`)
3. Utwórz Payment Intent: `POST /api/payments/intents`
4. Skopiuj `clientSecret`
5. Użyj Stripe Dashboard → **Payments** → Test payment

## 📊 Metadata w Payment Intents

Backend automatycznie dodaje metadata do każdego PaymentIntent:

```json
{
  "tenant_id": "guid-tenanta",
  "deposit_amount": "15000",
  "total_amount": "50000",
  "source": "sport_rental_api",
  "rental_start": "2025-10-10T10:00:00.0000000Z",
  "rental_end": "2025-10-15T10:00:00.0000000Z",
  "items_count": "2",
  "rental_days": "5"
}
```

To pozwala na łatwe powiązanie płatności z rezerwacją w Stripe Dashboard.

## 🚨 Error Handling

Backend zwraca błędy w formacie:

```json
{
  "error": "Opis błędu po polsku"
}
```

Typowe błędy:
- `400` - Nieprawidłowe dane (np. brak produktów, złe daty)
- `401` - Brak autoryzacji
- `404` - Nie znaleziono PaymentIntent
- `500` - Błąd Stripe API

## 🔒 Bezpieczeństwo

✅ **Webhook Signature Verification** - Wszystkie webhooks są weryfikowane przez Stripe  
✅ **Tenant Isolation** - Payment Intents zawierają tenant_id, sprawdzany przy każdym żądaniu  
✅ **HTTPS Only** - Stripe wymaga HTTPS w produkcji  
✅ **API Keys w Environment** - Nigdy nie commituj kluczy do repozytorium  

## 📚 Dokumentacja Stripe

- [Payment Intents Guide](https://stripe.com/docs/payments/payment-intents)
- [Webhooks Guide](https://stripe.com/docs/webhooks)
- [Testing](https://stripe.com/docs/testing)
- [Stripe CLI](https://stripe.com/docs/stripe-cli)

## 🚀 Następne kroki

- [ ] Dodać Stripe Checkout Session dla uproszczonego flow
- [ ] Implementować recurring payments dla długoterminowych wypożyczeń
- [ ] Dodać Stripe Connect dla multi-vendor marketplace
- [ ] Integrować z Apple Pay / Google Pay
- [ ] Dodać dispute handling (chargebacks)
