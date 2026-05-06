using MudBlazor;

namespace SportRental.Admin.Services.UI
{
    public class ThemeService
    {
        // RentSpot "Coral Energy" palette — brand guidelines maj 2026.
        // Primary = Coral akcent (CTA, aktywne stany), Secondary = Navy (AppBar, headings),
        // Tertiary = Gold (akcenty drugorzędne).
        private const string Coral = "#F96167";
        private const string Navy = "#2F3C7E";
        private const string Gold = "#F9E795";
        private const string SurfaceBg = "#F4F6FA";
        private const string TextHelper = "#5B6B82";

        public MudTheme LightTheme { get; } = new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = Coral,
                PrimaryContrastText = "#FFFFFF",
                Secondary = Navy,
                SecondaryContrastText = "#FFFFFF",
                Tertiary = Gold,
                TertiaryContrastText = Navy,
                AppbarBackground = Navy,
                AppbarText = "#FFFFFF",
                Background = "#FFFFFF",
                Surface = "#FFFFFF",
                DrawerBackground = "#FFFFFF",
                DrawerText = Navy,
                DrawerIcon = Navy,
                TextPrimary = "#1F2937",
                TextSecondary = TextHelper,
                ActionDefault = TextHelper,
                LinesDefault = "#E5E7EB",
                LinesInputs = "#D1D5DB",
                TableLines = "#E5E7EB",
                Divider = "#E5E7EB",
                GrayLight = SurfaceBg,
                GrayLighter = "#FAFBFC"
            }
        };

        public MudTheme DarkTheme { get; } = new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = Coral,
                PrimaryContrastText = "#FFFFFF",
                Secondary = Gold,
                SecondaryContrastText = Navy,
                Tertiary = Gold,
                AppbarBackground = "#1B2350",
                AppbarText = "#FFFFFF",
                Background = "#0F1530",
                Surface = "#1B2350",
                DrawerBackground = "#1B2350",
                DrawerText = "#FFFFFF",
                TextPrimary = "#FFFFFF",
                TextSecondary = "#C7CCDB"
            }
        };

        private bool _isDarkMode;
        
        public bool IsDarkMode 
        { 
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    OnChanged?.Invoke();
                }
            }
        }

        public MudTheme CurrentTheme => IsDarkMode ? DarkTheme : LightTheme;

        public event Action? OnChanged;

        public void Toggle()
        {
            IsDarkMode = !IsDarkMode;
        }

        public void SetColors(string? primaryHex, string? secondaryHex)
        {
            // W nowszej wersji MudBlazor należy używać MudThemeProvider i CSS variables
            // zamiast bezpośredniego ustawiania kolorów w temacie
            OnChanged?.Invoke();
        }
    }
}