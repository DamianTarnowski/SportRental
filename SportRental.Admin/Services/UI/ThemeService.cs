using MudBlazor;

namespace SportRental.Admin.Services.UI
{
    public class ThemeService
    {
        public MudTheme LightTheme { get; } = new MudTheme();
        public MudTheme DarkTheme { get; } = new MudTheme();

        public bool IsDarkMode { get; private set; }

        public MudTheme CurrentTheme => IsDarkMode ? DarkTheme : LightTheme;

        public event Action? OnChanged;

        public void Toggle()
        {
            IsDarkMode = !IsDarkMode;
            OnChanged?.Invoke();
        }

        public void SetColors(string? primaryHex, string? secondaryHex)
        {
            // W nowszej wersji MudBlazor należy używać MudThemeProvider i CSS variables
            // zamiast bezpośredniego ustawiania kolorów w temacie
            OnChanged?.Invoke();
        }
    }
}