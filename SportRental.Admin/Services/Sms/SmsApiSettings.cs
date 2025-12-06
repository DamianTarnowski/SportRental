namespace SportRental.Admin.Services.Sms
{
    /// <summary>
    /// Konfiguracja integracji z SMSAPI.pl
    /// </summary>
    public class SmsApiSettings
    {
        public const string SectionName = "smsApi";

        /// <summary>
        /// Token OAuth do autoryzacji w SMSAPI
        /// </summary>
        public string AuthToken { get; set; } = string.Empty;

        /// <summary>
        /// Flaga włączająca/wyłączająca wysyłanie SMS przez SMSAPI.
        /// Gdy false, wiadomości są logowane do konsoli.
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// Liczba prób ponownego wysłania SMS w przypadku błędu
        /// </summary>
        public int SendConfirmationAttempts { get; set; } = 5;

        /// <summary>
        /// Nazwa nadawcy SMS (pole "from" w SMSAPI, max 11 znaków)
        /// </summary>
        public string SenderName { get; set; } = "Test";
    }
}

