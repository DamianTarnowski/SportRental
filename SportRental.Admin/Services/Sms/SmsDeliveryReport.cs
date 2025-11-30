namespace SportRental.Admin.Services.Sms
{
    /// <summary>
    /// Model raportu doręczenia SMS z SMSAPI (callback)
    /// Dokumentacja: https://www.smsapi.pl/docs#9-raporty-callback
    /// </summary>
    public class SmsDeliveryReport
    {
        /// <summary>
        /// Unikalny identyfikator wiadomości w SMSAPI
        /// </summary>
        public string? MsgId { get; set; }

        /// <summary>
        /// Status doręczenia wiadomości
        /// Możliwe wartości: DELIVERED, UNDELIVERED, EXPIRED, SENT, UNKNOWN, REJECTED, PENDING
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Numer telefonu odbiorcy
        /// </summary>
        public string? To { get; set; }

        /// <summary>
        /// Data i czas zdarzenia (format: YYYY-MM-DD HH:MM:SS)
        /// </summary>
        public string? DoneDate { get; set; }

        /// <summary>
        /// Wartość parametru idx przekazanego podczas wysyłki
        /// </summary>
        public string? Idx { get; set; }

        /// <summary>
        /// Nazwa użytkownika wysyłającego
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Ilość części SMS (dla długich wiadomości)
        /// </summary>
        public int? Parts { get; set; }
    }

    /// <summary>
    /// Statusy doręczenia SMS zgodne z dokumentacją SMSAPI
    /// </summary>
    public static class SmsDeliveryStatus
    {
        public const string Delivered = "DELIVERED";
        public const string Undelivered = "UNDELIVERED";
        public const string Expired = "EXPIRED";
        public const string Sent = "SENT";
        public const string Unknown = "UNKNOWN";
        public const string Rejected = "REJECTED";
        public const string Pending = "PENDING";
    }
}

