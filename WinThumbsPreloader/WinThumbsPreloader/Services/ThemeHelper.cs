#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinThumbsPreloader.Services
{
    /// <summary>
    /// Applies Windows DWM immersive dark/light titlebar attributes and persists the WebUI theme choice safely.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class ThemeHelper
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);

        public static string NormalizeThemeName(string? theme)
        {
            if (string.IsNullOrWhiteSpace(theme))
                return "dark";

            return theme.Trim().ToLowerInvariant() switch
            {
                "light" => "light",
                "system" => "system",
                _ => "dark"
            };
        }

        private static string GetThemePreferenceFilePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = Path.GetTempPath();

            return Path.Combine(localAppData, "WinThumbsPreloader", "ui-theme.txt");
        }

        public static string GetSavedTheme()
        {
            try
            {
                string path = GetThemePreferenceFilePath();
                if (!PathSecurity.IsSafeRegularFileWithinLimit(path, 64, out _))
                    return "dark";

                string raw = File.ReadAllText(path, Encoding.UTF8);
                return NormalizeThemeName(raw);
            }
            catch
            {
                return "dark";
            }
        }

        public static string SaveTheme(string? theme)
        {
            string normalized = NormalizeThemeName(theme);
            try
            {
                string path = GetThemePreferenceFilePath();
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    if (PathSecurity.HasReparsePointOrJunctionInPath(dir, rejectAnyLeafOrParentReparsePoint: false) ||
                        PathSecurity.IsReparsePoint(dir))
                    {
                        return normalized;
                    }

                    DirectoryInfo info = Directory.CreateDirectory(dir);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        return normalized;
                }

                PathSecurity.EnsureSafeFileForWrite(path);
                File.WriteAllText(path, normalized, Encoding.UTF8);
            }
            catch
            {
                // Non-fatal preference write failure
            }

            return normalized;
        }

        public static bool ResolveEffectiveDarkMode(string? theme)
        {
            string normalized = NormalizeThemeName(theme);
            if (normalized == "dark")
                return true;

            if (normalized == "light")
                return false;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
                object? val = key?.GetValue("AppsUseLightTheme");
                if (val is int intVal)
                    return intVal == 0;
            }
            catch
            {
                // Default to dark on registry error
            }

            return true;
        }

        public static Color GetCanvasBackColor(bool isDarkMode) =>
            isDarkMode ? Color.Black : Color.FromArgb(248, 250, 252);

        public static bool ApplyWindowTheme(Form form, string? theme = null)
        {
            if (form == null || form.IsDisposed)
                return true;

            string effectiveTheme = theme != null ? NormalizeThemeName(theme) : GetSavedTheme();
            bool useDarkMode = ResolveEffectiveDarkMode(effectiveTheme);

            try
            {
                form.BackColor = GetCanvasBackColor(useDarkMode);

                if (form.IsHandleCreated)
                {
                    int attrValue = useDarkMode ? 1 : 0;
                    int hr = DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref attrValue, sizeof(int));
                    if (hr != 0)
                    {
                        DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref attrValue, sizeof(int));
                    }
                }
            }
            catch
            {
                // Ignore on older OS builds without DWM dark mode support
            }

            return useDarkMode;
        }
    }
}
