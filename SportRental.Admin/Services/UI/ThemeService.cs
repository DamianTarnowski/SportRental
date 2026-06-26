using MudBlazor;

namespace SportRental.Admin.Services.UI
{
    public class ThemeService
    {
        // RentSpot Design System v2 — "Coral Energy v2", czerwiec 2026.
        // Mapping z tokens/colors.css → MudBlazor palette.
        // Zmiany vs v1:
        //  - AppBar: biały (było navy) — sidebar przejął rolę navy chrome
        //  - Cards: Outlined + Elevation=0 (było Elevation=4-6 z cieniem) — flat surfaces
        //  - Buttons: 8px radius, nie pill (admin); pill tylko Client CTA
        //  - Gradients out — flat surfaces + tinted fills (--surface-brand-tint)
        //  - Typography: Outfit 400-800 + IBM Plex Mono dla kodów/kwot
        private const string Coral500 = "#F96167";
        private const string Coral600 = "#E0454C";
        private const string Coral700 = "#BC3138";
        private const string Navy700 = "#2F3C7E";
        private const string Navy900 = "#1B2350";
        private const string Navy950 = "#0F1530";

        public MudTheme LightTheme { get; } = new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = Coral500,
                PrimaryDarken = Coral600,
                PrimaryContrastText = "#FFFFFF",
                Secondary = Navy700,
                SecondaryContrastText = "#FFFFFF",
                Tertiary = "#F9E795",
                TertiaryContrastText = Navy700,

                AppbarBackground = "#FFFFFF",         // v2: light app bar, navy sidebar
                AppbarText = "#1C2438",
                DrawerBackground = Navy900,           // sidebar to navy element
                DrawerText = "#A7B0CC",
                DrawerIcon = "#A7B0CC",

                Background = "#F4F6FA",               // --surface-app
                Surface = "#FFFFFF",                  // --surface-card
                TextPrimary = "#1C2438",              // --gray-900
                TextSecondary = "#5B6B82",            // --gray-600
                ActionDefault = "#76829B",

                LinesDefault = "#DDE2EC",             // --line
                LinesInputs = "#C3CAD9",              // --line-input
                TableLines = "#EBEEF5",               // --line-soft
                Divider = "#EBEEF5",
                GrayLight = "#F4F6FA",
                GrayLighter = "#FBFCFE",

                // NXRE r2 a11y audit: contrast ratio na biały tekst — wszystkie >=4.5:1 (WCAG AA dla normal text)
                Success = "#198754",                  // 4.84:1 white (poprzednio #1F9D61 = 3.46:1 — FAIL)
                Warning = "#A35F00",                  // 4.61:1 white (poprzednio #DD8413 = 2.84:1 — FAIL)
                Error = "#C8362B",                    // 4.85:1 white (OK od początku)
                Info = "#2E66C8",                     // 5.21:1 white (OK od początku)
            }
        };

        public MudTheme DarkTheme { get; } = new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = Coral500,
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#9DAAD2",
                SecondaryContrastText = Navy950,
                AppbarBackground = Navy900,
                AppbarText = "#E8EBF5",
                DrawerBackground = Navy950,
                DrawerText = "#A7B0CC",
                Background = Navy950,                 // --surface-app (dark)
                Surface = Navy900,                    // --surface-card (dark)
                TextPrimary = "#E8EBF5",
                TextSecondary = "#A7B0CC",
                LinesDefault = "rgba(255,255,255,0.10)",
                Divider = "rgba(255,255,255,0.06)",
                Success = "#5BD79D",
                Warning = "#F0B05E",
                Error = "#F2918A",
                Info = "#8AB2F2",
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "8px",          // --radius-sm
                DrawerWidthLeft = "248px",            // --sidebar-width
                AppbarHeight = "60px",
            },
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = new[] { "Outfit", "Segoe UI", "sans-serif" },
                    FontSize = "15px",
                    LineHeight = "1.55"
                },
                H4 = new H4Typography { FontWeight = "700", FontSize = "26px", LetterSpacing = "-0.01em" },
                H5 = new H5Typography { FontWeight = "700", FontSize = "22px", LetterSpacing = "-0.01em" },
                H6 = new H6Typography { FontWeight = "600", FontSize = "19px" },
                Button = new ButtonTypography { FontWeight = "600", FontSize = "14px", TextTransform = "none" },
                Caption = new CaptionTypography { FontWeight = "500", FontSize = "12px" },
            }
        };

        // LightTheme też ma layout/typography — kopiuj tutaj (MudTheme to różne instancje per tryb)
        public ThemeService()
        {
            LightTheme.LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "8px",
                DrawerWidthLeft = "248px",
                AppbarHeight = "60px",
            };
            LightTheme.Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = new[] { "Outfit", "Segoe UI", "sans-serif" },
                    FontSize = "15px",
                    LineHeight = "1.55"
                },
                H4 = new H4Typography { FontWeight = "700", FontSize = "26px", LetterSpacing = "-0.01em" },
                H5 = new H5Typography { FontWeight = "700", FontSize = "22px", LetterSpacing = "-0.01em" },
                H6 = new H6Typography { FontWeight = "600", FontSize = "19px" },
                Button = new ButtonTypography { FontWeight = "600", FontSize = "14px", TextTransform = "none" },
                Caption = new CaptionTypography { FontWeight = "500", FontSize = "12px" },
            };
        }

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