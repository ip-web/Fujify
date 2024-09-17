using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Media;
using MaterialDesignColors;
using MaterialDesignColors.ColorManipulation;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FujifyNET.Properties;

namespace FujifyNET
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public static bool IsDarkTheme
        {
            get => FujifyNET.Properties.Settings.Default.IsDarkTheme;
            set
            {
                FujifyNET.Properties.Settings.Default.IsDarkTheme = value;
                FujifyNET.Properties.Settings.Default.Save();
            }
        }


        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            IsDarkTheme = FujifyNET.Properties.Settings.Default.IsDarkTheme;
            ApplyTheme(IsDarkTheme);
        }
        public static void ApplyTheme(bool isDark)
        {
            IsDarkTheme = isDark;
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);

            if (isDark)
            {
                SetDarkColors(theme);
            }
            else
            {
                SetLightColors(theme);
            }

            paletteHelper.SetTheme(theme);
                // Update custom brushes
            System.Windows.Application.Current.Resources["TitleBarBackgroundBrush"] = new SolidColorBrush(
                Color.FromRgb(
                    isDark ? (byte)0x21 : (byte)0x00,
                    isDark ? (byte)0x21 : (byte)0x50,
                    isDark ? (byte)0x21 : (byte)0x00
                )
            );
            // Add PreferencesBorderBrush
            System.Windows.Application.Current.Resources["PreferencesBorderBrush"] = new SolidColorBrush(
                Color.FromRgb(
                    isDark ? (byte)0x3F : (byte)0xB7,
                    isDark ? (byte)0x3F : (byte)0xB7,
                    isDark ? (byte)0x3F : (byte)0xB7
                )
            );


        }



        private static void SetDarkColors(Theme theme)
        {
            // Primary color: Dark Green (#006400)
            Color primaryColor = (Color)ColorConverter.ConvertFromString("#006400");
            theme.PrimaryLight = primaryColor;
            theme.PrimaryMid = primaryColor;
            theme.PrimaryDark = primaryColor;

            // Accent color: Slightly lighter green
            Color accentColor = (Color)ColorConverter.ConvertFromString("#008000");
            theme.SecondaryLight = accentColor;
            theme.SecondaryMid = accentColor;
            theme.SecondaryDark = accentColor;

            // Background color: Dark Gray (#323232)
            Color backgroundColor = (Color)ColorConverter.ConvertFromString("#323232");
            theme.Background = backgroundColor;
        }

        private static void SetLightColors(Theme theme)
        {
            Color primaryColor = (Color)ColorConverter.ConvertFromString("#4CAF50");
            theme.PrimaryLight = primaryColor;
            theme.PrimaryMid = primaryColor;
            theme.PrimaryDark = primaryColor;

            // Accent color: Dark Green
            Color accentColor = (Color)ColorConverter.ConvertFromString("#006400");
            theme.SecondaryLight = accentColor;
            theme.SecondaryMid = accentColor;
            theme.SecondaryDark = accentColor;

            theme.Background = Colors.White;
        }
    }
}