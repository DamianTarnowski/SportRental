using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportRental.Admin.Services;
using SportRental.Admin.Services.Contracts;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRentalShared = SportRental.Shared.Models;

namespace SportRental.Admin.Payments;

/// <summary>
/// Retry-safe finalization of a paid Stripe Checkout session. All tenant rentals and
/// their items are persisted atomically; the checkout holds are consumed in the same
/// transaction. Contract generation and notifications run after commit and are best effort.
/// </summary>
public sealed class CheckoutFinalizationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IContractGenerator _contracts;
    private readonly IRentalConfirmationService _confirmations;
    private readonly IPaymentGateway _paymentGateway;
    private readonly StripeOptions _stripe;
    private readonly ILogger<CheckoutFinalizationService> _logger;

    public CheckoutFinalizationService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IContractGenerator contracts,
        IRentalConfirmationService confirmations,
        IPaymentGateway paymentGateway,
        IOptions<StripeOptions> stripeOptions,
        ILogger<CheckoutFinalizationService> logger)
    {
        _dbFactory = dbFactory;
        _contracts = contracts;
        _confirmations = confirmations;
        _paymentGateway = paymentGateway;
        _stripe = stripeOptions.Value;
        _logger = logger;
    }

    public async Task<SportRentalShared.FinalizeSessionResponse> FinalizeAsync(
        string stripeSessionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeSessionId))
            return Failure("Missing Stripe session id.");
        if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
            return Failure("Stripe is not configured.");

        Stripe.StripeConfiguration.ApiKey = _stripe.SecretKey;

        try
        {
            var stripeSession = await new Stripe.Checkout.SessionService()
                .GetAsync(stripeSessionId, cancellationToken: ct);
            if (!string.Equals(stripeSession.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
                return Failure(
                    $"Płatność nie została jeszcze potwierdzona. Status: {stripeSession.PaymentStatus}",
                    retryable: true);

            if (!stripeSession.Metadata.TryGetValue("checkout_session_id", out var checkoutSessionIdText) ||
                !Guid.TryParse(checkoutSessionIdText, out var checkoutSessionId))
            {
                return Failure("Missing checkout_session_id in metadata.");
            }

            var persisted = await PersistRentalsAsync(
                checkoutSessionId,
                stripeSessionId,
                stripeSession.PaymentIntentId,
                stripeSession.AmountTotal,
                stripeSession.Currency,
                ct);
            if (!persisted.Success)
            {
                var error = persisted.Error ?? "Rental finalization failed.";
                if (persisted.AlreadyRefunded)
                    return RefundedFailure("Rezerwacja nie powstała, a płatność została zwrócona.");

                if (persisted.CanAutoRefund &&
                    !string.IsNullOrWhiteSpace(stripeSession.PaymentIntentId) &&
                    stripeSession.AmountTotal is > 0)
                {
                    var refundAmount = stripeSession.AmountTotal.Value / 100m;
                    var refunded = await _paymentGateway.RefundPaymentAsync(
                        Guid.Empty,
                        stripeSession.PaymentIntentId,
                        refundAmount,
                        "requested_by_customer",
                        $"checkout-finalization-refund:{checkoutSessionId:N}");
                    await RecordFinalizationFailureAsync(
                        checkoutSessionId,
                        error,
                        refunded,
                        refunded ? CancellationToken.None : ct);

                    if (refunded)
                    {
                        _logger.LogError(
                            "Finalizacja checkout {CheckoutSessionId} nie powiodła się; płatność {PaymentIntentId} została automatycznie zwrócona. Powód: {Reason}",
                            checkoutSessionId,
                            stripeSession.PaymentIntentId,
                            error);
                        return RefundedFailure(
                            "Nie udało się utworzyć rezerwacji. Pobrana kwota została automatycznie zwrócona.");
                    }
                }
                else
                {
                    await RecordFinalizationFailureAsync(checkoutSessionId, error, refunded: false, ct);
                }

                return Failure(
                    "Płatność została przyjęta, ale rezerwacja wymaga wyjaśnienia. Skontaktuj się z obsługą i podaj identyfikator płatności.");
            }

            // Webhook i redirect klienta zwykle docierają w odstępie sekund. Drugi
            // finalizer nie może ponownie wysyłać tych samych wiadomości. Starszy retry
            // może jednak naprawić brakujący kontrakt/email po awarii procesu.
            var shouldEnsureArtifacts = !persisted.AlreadyExisted ||
                                        await NeedsArtifactRetryAsync(persisted.RentalIds, ct);
            if (shouldEnsureArtifacts)
            {
                foreach (var rentalId in persisted.RentalIds)
                    await EnsureRentalArtifactsAsync(rentalId, ct);
            }

            return new SportRentalShared.FinalizeSessionResponse(
                true,
                persisted.AlreadyExisted ? "Rental already created." : "Rental created successfully.",
                persisted.RentalIds.FirstOrDefault(),
                persisted.RentalIds,
                MarketplaceOrderId: persisted.MarketplaceOrderId,
                MarketplaceOrderNumber: persisted.MarketplaceOrderNumber);
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Stripe error during finalize {SessionId}", stripeSessionId);
            return Failure(
                "Nie udało się potwierdzić płatności w Stripe. Spróbuj ponownie za chwilę.",
                retryable: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during finalize {SessionId}", stripeSessionId);
            return Failure(
                "Nie udało się sfinalizować rezerwacji. Płatność zostanie ponownie sprawdzona automatycznie.",
                retryable: true);
        }
    }

    internal async Task<PersistResult> PersistRentalsAsync(
        Guid checkoutSessionId,
        string stripeSessionId,
        string? paymentIntentId,
        long? paidAmountMinor,
        string? paidCurrency,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var checkoutSession = await db.CheckoutSessions
            .FirstOrDefaultAsync(cs => cs.Id == checkoutSessionId, ct);
        if (checkoutSession is null)
            return PersistResult.Failed("Checkout session not found.", canAutoRefund: true);
        if (checkoutSession.RefundedAtUtc.HasValue)
            return PersistResult.Refunded("Checkout payment was already refunded.");
        if (!string.IsNullOrWhiteSpace(checkoutSession.StripeSessionId) &&
            !string.Equals(checkoutSession.StripeSessionId, stripeSessionId, StringComparison.Ordinal))
        {
            return PersistResult.Failed("Stripe session does not match checkout.", canAutoRefund: true);
        }

        var payload = System.Text.Json.JsonSerializer.Deserialize<CheckoutRentalPayload>(
            checkoutSession.PayloadJson);
        if (payload is null || payload.Tenants.Count == 0)
            return PersistResult.Failed("Invalid checkout payload.", canAutoRefund: true);

        var expectedPaidAmountMinor = (long)Math.Round(
            payload.DepositAmount * 100m,
            MidpointRounding.AwayFromZero);
        if (string.IsNullOrWhiteSpace(paymentIntentId) ||
            paidAmountMinor != expectedPaidAmountMinor ||
            !string.Equals(paidCurrency, "pln", StringComparison.OrdinalIgnoreCase))
        {
            return PersistResult.Failed("Paid amount or currency does not match the checkout.", canAutoRefund: true);
        }

        if (payload.TotalAmount <= 0 || payload.DepositAmount <= 0 ||
            payload.Tenants.Sum(t => t.TotalAmount) != payload.TotalAmount ||
            payload.Tenants.Sum(t => t.DepositAmount) != payload.DepositAmount)
        {
            return PersistResult.Failed("Checkout totals are inconsistent.", canAutoRefund: true);
        }

        var expectedTenantIds = payload.Tenants.Select(t => t.TenantId).Distinct().ToList();
        if (expectedTenantIds.Count != payload.Tenants.Count)
            return PersistResult.Failed("Checkout payload contains duplicate tenants.", canAutoRefund: true);

        var marketplaceOrder = await db.MarketplaceOrders
            .FirstOrDefaultAsync(order => order.CheckoutSessionId == checkoutSessionId, ct);
        var existingRentals = marketplaceOrder is not null
            ? await db.Rentals.IgnoreQueryFilters()
                .Where(rental => rental.MarketplaceOrderId == marketplaceOrder.Id)
                .ToListAsync(ct)
            : await db.Rentals.IgnoreQueryFilters()
                .Where(rental => rental.IdempotencyKey == payload.IdempotencyKey &&
                                 expectedTenantIds.Contains(rental.TenantId))
                .ToListAsync(ct);
        if (existingRentals.GroupBy(r => r.TenantId).Any(g => g.Count() > 1))
            return PersistResult.Failed("Duplicate rentals detected for checkout.", canAutoRefund: false);

        var nowUtc = DateTime.UtcNow;
        if (marketplaceOrder is null)
        {
            marketplaceOrder = new MarketplaceOrder
            {
                Id = Guid.NewGuid(),
                OrderNumber = CreateOrderNumber(checkoutSession),
                CustomerId = payload.Customer.CustomerId,
                CustomerEmailSnapshot = payload.Customer.Email?.Trim().ToLowerInvariant(),
                CheckoutSessionId = checkoutSession.Id,
                IdempotencyKey = payload.IdempotencyKey,
                StripeSessionId = stripeSessionId,
                PaymentIntentId = paymentIntentId,
                TotalAmount = payload.TotalAmount,
                DepositAmount = payload.DepositAmount,
                RefundedDepositAmount = 0m,
                Currency = "PLN",
                Status = "Confirmed",
                PaymentStatus = "DepositPaid",
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PaidAtUtc = nowUtc,
                AcceptedTermsVersion = payload.AcceptedTermsVersion,
                AcknowledgedPrivacyVersion = payload.AcknowledgedPrivacyVersion,
                LegalAcceptedAtUtc = checkoutSession.LegalAcceptedAtUtc
            };
            db.MarketplaceOrders.Add(marketplaceOrder);

            // Kompatybilność z finalizacją rozpoczętą przed dodaniem agregatu.
            foreach (var existingRental in existingRentals)
                existingRental.MarketplaceOrderId = marketplaceOrder.Id;
        }
        else if (marketplaceOrder.CustomerId != payload.Customer.CustomerId ||
                 marketplaceOrder.TotalAmount != payload.TotalAmount ||
                 marketplaceOrder.DepositAmount != payload.DepositAmount ||
                 !string.Equals(marketplaceOrder.PaymentIntentId, paymentIntentId, StringComparison.Ordinal))
        {
            return PersistResult.Failed("Marketplace order does not match the paid checkout.", canAutoRefund: false);
        }

        marketplaceOrder.StripeSessionId = stripeSessionId;
        marketplaceOrder.PaymentIntentId = paymentIntentId;
        marketplaceOrder.PaidAtUtc ??= nowUtc;
        marketplaceOrder.UpdatedAtUtc = nowUtc;

        var existingByTenant = existingRentals.ToDictionary(r => r.TenantId);
        var rentalIds = existingRentals.Select(r => r.Id).ToList();
        var ownHoldIds = payload.HoldIds.Distinct().ToList();

        for (var tenantIndex = 0; tenantIndex < payload.Tenants.Count; tenantIndex++)
        {
            var tenantPayload = payload.Tenants[tenantIndex];
            var orderSequence = tenantPayload.Sequence > 0
                ? tenantPayload.Sequence
                : tenantIndex + 1;
            var groupStartDateUtc = payload.SchemaVersion >= 2
                ? tenantPayload.StartDateUtc
                : payload.StartDateUtc;
            var groupEndDateUtc = payload.SchemaVersion >= 2
                ? tenantPayload.EndDateUtc
                : payload.EndDateUtc;
            var groupRentalType = payload.SchemaVersion >= 2
                ? tenantPayload.RentalType
                : payload.RentalType;
            var groupHoursRented = payload.SchemaVersion >= 2
                ? tenantPayload.HoursRented
                : payload.HoursRented;

            if (groupEndDateUtc <= groupStartDateUtc ||
                groupEndDateUtc - groupStartDateUtc > TimeSpan.FromDays(365))
            {
                return PersistResult.Failed(
                    "Checkout payload contains an invalid rental period.",
                    existingRentals.Count == 0);
            }

            if (groupRentalType == SportRentalShared.RentalTypeDto.Hourly)
            {
                var duration = groupEndDateUtc - groupStartDateUtc;
                var billedHours = (int)Math.Ceiling(duration.TotalHours);
                if (groupHoursRented is not (>= 1 and <= 24) ||
                    duration > TimeSpan.FromHours(24) ||
                    billedHours != groupHoursRented)
                {
                    return PersistResult.Failed(
                        "Checkout payload contains invalid hourly rental data.",
                        existingRentals.Count == 0);
                }
            }

            if (existingByTenant.ContainsKey(tenantPayload.TenantId))
            {
                var existingRental = existingByTenant[tenantPayload.TenantId];
                existingRental.MarketplaceOrderId = marketplaceOrder.Id;
                existingRental.OrderSequence ??= orderSequence;
                continue;
            }
            if (tenantPayload.Items.Count == 0 || tenantPayload.Items.Any(i => i.Quantity <= 0))
                return PersistResult.Failed("Checkout payload contains invalid items.", existingRentals.Count == 0);
            if (tenantPayload.TotalAmount <= 0 || tenantPayload.DepositAmount <= 0 ||
                tenantPayload.Items.Sum(i => i.Subtotal) != tenantPayload.TotalAmount)
                return PersistResult.Failed("Checkout item totals do not match the paid quote.", existingRentals.Count == 0);

            var rental = new Rental
            {
                Id = Guid.NewGuid(),
                TenantId = tenantPayload.TenantId,
                CustomerId = payload.Customer.CustomerId,
                MarketplaceOrderId = marketplaceOrder.Id,
                OrderSequence = orderSequence,
                StartDateUtc = groupStartDateUtc,
                EndDateUtc = groupEndDateUtc,
                TotalAmount = tenantPayload.TotalAmount,
                DepositAmount = tenantPayload.DepositAmount,
                PaidAmount = 0m,
                Status = RentalStatus.Confirmed,
                PaymentStatus = "DepositPaid",
                PaymentMethod = "Online",
                PaidAtUtc = nowUtc,
                DepositPaidAtUtc = nowUtc,
                PaymentIntentId = paymentIntentId,
                IdempotencyKey = payload.IdempotencyKey,
                Notes = payload.Notes,
                RegulationsTextSnapshot = tenantPayload.RegulationsTextSnapshot,
                RegulationsHash = string.IsNullOrWhiteSpace(tenantPayload.RegulationsHash)
                    ? null
                    : tenantPayload.RegulationsHash,
                RegulationsVersion = string.IsNullOrWhiteSpace(tenantPayload.RegulationsVersion)
                    ? null
                    : tenantPayload.RegulationsVersion,
                RegulationsSource = string.IsNullOrWhiteSpace(tenantPayload.RegulationsSource)
                    ? null
                    : tenantPayload.RegulationsSource,
                Source = RentalSource.Online,
                RentalType = (RentalType)(int)groupRentalType,
                HoursRented = groupRentalType == SportRentalShared.RentalTypeDto.Hourly
                    ? groupHoursRented
                    : null,
                CreatedAtUtc = nowUtc
            };

            foreach (var item in tenantPayload.Items)
            {
                var product = await db.Products.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId &&
                                              p.TenantId == tenantPayload.TenantId &&
                                              p.IsActive && p.Available && !p.Disabled && !p.IsDeleted, ct);
                if (product is null)
                    return PersistResult.Failed($"Product {item.ProductId} is no longer available.", existingRentals.Count == 0);

                var reservedQty = await db.RentalItems.IgnoreQueryFilters()
                    .Where(ri => ri.ProductId == item.ProductId)
                    .Join(db.Rentals.IgnoreQueryFilters().WhereInventoryBlocking(), ri => ri.RentalId, r => r.Id, (ri, r) => new { ri, r })
                    .Where(x => x.r.IdempotencyKey != payload.IdempotencyKey &&
                                x.r.EndDateUtc > groupStartDateUtc &&
                                x.r.StartDateUtc < groupEndDateUtc)
                    .SumAsync(x => (int?)x.ri.Quantity, ct) ?? 0;
                var otherHoldsQty = await db.ReservationHolds.IgnoreQueryFilters()
                    .Where(h => h.ProductId == item.ProductId &&
                                !ownHoldIds.Contains(h.Id) &&
                                h.ExpiresAtUtc > nowUtc &&
                                h.EndDateUtc > groupStartDateUtc &&
                                h.StartDateUtc < groupEndDateUtc)
                    .SumAsync(h => (int?)h.Quantity, ct) ?? 0;

                if (reservedQty + otherHoldsQty + item.Quantity > product.AvailableQuantity)
                    return PersistResult.Failed($"Insufficient availability for product {product.Name}.", existingRentals.Count == 0);

                rental.Items.Add(new RentalItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    PricePerDay = item.PricePerDay,
                    PricePerHour = item.PricePerHour,
                    Subtotal = item.Subtotal
                });
            }

            db.Rentals.Add(rental);
            rentalIds.Add(rental.Id);
        }

        if (ownHoldIds.Count > 0)
        {
            var holds = await db.ReservationHolds.IgnoreQueryFilters()
                .Where(h => ownHoldIds.Contains(h.Id))
                .ToListAsync(ct);
            db.ReservationHolds.RemoveRange(holds);
        }

        checkoutSession.IsProcessed = true;
        checkoutSession.StripeSessionId = stripeSessionId;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return PersistResult.Completed(
            rentalIds.Distinct().OrderBy(id => id).ToList(),
            existingRentals.Count == expectedTenantIds.Count,
            marketplaceOrder.Id,
            marketplaceOrder.OrderNumber);
    }

    private async Task EnsureRentalArtifactsAsync(Guid rentalId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rental = await db.Rentals.IgnoreQueryFilters()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);
        if (rental is null)
            return;

        var customer = await db.Customers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == rental.CustomerId, ct);
        if (customer is null)
        {
            _logger.LogWarning("Brak klienta {CustomerId} dla wynajmu {RentalId}", rental.CustomerId, rental.Id);
            return;
        }

        // Umowa i e-mail muszą odpowiadać danym zaakceptowanym przy checkout,
        // a nie profilowi zmienionemu w czasie płatności Stripe.
        var contractCustomer = customer;
        if (!string.IsNullOrWhiteSpace(rental.IdempotencyKey))
        {
            var payloadJson = await db.CheckoutSessions.AsNoTracking()
                .Where(cs => cs.IdempotencyKey == rental.IdempotencyKey)
                .Select(cs => cs.PayloadJson)
                .FirstOrDefaultAsync(ct);
            var payload = string.IsNullOrWhiteSpace(payloadJson)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<CheckoutRentalPayload>(payloadJson);
            if (payload?.Customer.CustomerId == customer.Id &&
                !string.IsNullOrWhiteSpace(payload.Customer.FullName))
            {
                contractCustomer = new Customer
                {
                    Id = customer.Id,
                    TenantId = customer.TenantId,
                    FullName = payload.Customer.FullName,
                    Email = payload.Customer.Email,
                    PhoneNumber = payload.Customer.PhoneNumber,
                    Address = payload.Customer.Address,
                    DocumentNumber = payload.Customer.DocumentNumber,
                    CreatedAtUtc = customer.CreatedAtUtc
                };
            }
        }

        var productIds = rental.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products.IgnoreQueryFilters()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(ct);
        var companyInfo = await db.CompanyInfos.IgnoreQueryFilters()
            .Include(ci => ci.Tenant)
            .FirstOrDefaultAsync(ci => ci.TenantId == rental.TenantId, ct);
        var contractTemplate = await db.ContractTemplates.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == rental.TenantId, ct);
        var isDemoTenant = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == rental.TenantId)
            .Select(t => (bool?)t.IsDemo)
            .FirstOrDefaultAsync(ct) ?? true;

        try
        {
            if (string.IsNullOrWhiteSpace(rental.ContractUrl))
            {
                rental.ContractUrl = await _contracts.GenerateAndSaveRentalContractAsync(
                    rental,
                    rental.Items,
                    contractCustomer,
                    products,
                    companyInfo,
                    contractTemplate?.Content,
                    ct);
                await db.SaveChangesAsync(ct);
            }

            if (!isDemoTenant && !rental.IsEmailSent && !string.IsNullOrWhiteSpace(contractCustomer.Email))
            {
                await _contracts.SendRentalConfirmationEmailAsync(
                    rental,
                    rental.Items,
                    contractCustomer,
                    products,
                    companyInfo,
                    contractTemplate?.Content,
                    ct);
                rental.IsEmailSent = true;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Generowanie umowy/emaila nie powiodło się dla wynajmu {RentalId}", rental.Id);
        }

        if (isDemoTenant || companyInfo?.SmsConfirmationEnabled != true ||
            (string.IsNullOrWhiteSpace(customer.PhoneNumber) && string.IsNullOrWhiteSpace(customer.Email)))
        {
            return;
        }

        try
        {
            var token = await _confirmations.CreateConfirmationForTenantAsync(rental.TenantId, rental.Id, ct);
            var delivery = await _confirmations.SendConfirmationLinkForTenantAsync(
                rental.TenantId, rental.Id, token, ct);
            if (!delivery.AnySent)
            {
                _logger.LogWarning("Nie udało się wysłać linku potwierdzenia dla wynajmu {RentalId}", rental.Id);
            }
            else if (!delivery.AllAttemptedSent)
            {
                _logger.LogWarning(
                    "Link potwierdzenia dla wynajmu {RentalId} wysłano tylko częścią kanałów (SMS={SmsSent}, Email={EmailSent})",
                    rental.Id, delivery.SmsSent, delivery.EmailSent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wysyłka linku potwierdzenia nie powiodła się dla wynajmu {RentalId}", rental.Id);
        }
    }

    private async Task<bool> NeedsArtifactRetryAsync(
        IReadOnlyList<Guid> rentalIds,
        CancellationToken ct)
    {
        if (rentalIds.Count == 0)
            return false;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rentals = await db.Rentals.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => rentalIds.Contains(r.Id))
            .Select(r => new
            {
                r.CreatedAtUtc,
                r.ContractUrl,
                r.IsEmailSent,
                CustomerHasEmail = db.Customers.IgnoreQueryFilters()
                    .Any(c => c.Id == r.CustomerId && c.Email != null && c.Email != string.Empty),
                IsDemo = db.Tenants.IgnoreQueryFilters()
                    .Any(t => t.Id == r.TenantId && t.IsDemo)
            })
            .ToListAsync(ct);

        if (rentals.Count != rentalIds.Count ||
            rentals.Any(r => r.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-5)))
        {
            return false;
        }

        return rentals.Any(r =>
            string.IsNullOrWhiteSpace(r.ContractUrl) ||
            (!r.IsDemo && r.CustomerHasEmail && !r.IsEmailSent));
    }

    private static SportRentalShared.FinalizeSessionResponse Failure(
        string message,
        bool retryable = false) =>
        new(false, message, null, Array.Empty<Guid>(), Retryable: retryable);

    private static SportRentalShared.FinalizeSessionResponse RefundedFailure(string message) =>
        new(false, message, null, Array.Empty<Guid>(), Refunded: true);

    private static string CreateOrderNumber(CheckoutSession checkoutSession) =>
        $"RS-{checkoutSession.CreatedAtUtc:yyyyMMdd}-{checkoutSession.Id.ToString("N")[..8].ToUpperInvariant()}";

    private async Task RecordFinalizationFailureAsync(
        Guid checkoutSessionId,
        string error,
        bool refunded,
        CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var checkout = await db.CheckoutSessions
                .FirstOrDefaultAsync(cs => cs.Id == checkoutSessionId, ct);
            if (checkout is null) return;

            checkout.FailureReason = error.Length <= 500 ? error : error[..500];
            if (refunded)
            {
                checkout.RefundedAtUtc = DateTime.UtcNow;
                checkout.IsProcessed = true;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się zapisać błędu finalizacji checkout {CheckoutSessionId}", checkoutSessionId);
        }
    }

    internal sealed record PersistResult(
        bool Success,
        IReadOnlyList<Guid> RentalIds,
        bool AlreadyExisted,
        string? Error,
        bool CanAutoRefund,
        bool AlreadyRefunded,
        Guid? MarketplaceOrderId,
        string? MarketplaceOrderNumber)
    {
        public static PersistResult Failed(string error, bool canAutoRefund) =>
            new(false, Array.Empty<Guid>(), false, error, canAutoRefund, false, null, null);

        public static PersistResult Refunded(string error) =>
            new(false, Array.Empty<Guid>(), false, error, false, true, null, null);

        public static PersistResult Completed(
            IReadOnlyList<Guid> rentalIds,
            bool alreadyExisted,
            Guid marketplaceOrderId,
            string marketplaceOrderNumber) =>
            new(
                true,
                rentalIds,
                alreadyExisted,
                null,
                false,
                false,
                marketplaceOrderId,
                marketplaceOrderNumber);
    }
}
