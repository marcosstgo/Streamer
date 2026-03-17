using System.Windows;

namespace Streamer
{
    /// <summary>
    /// Localization helper — resolves the current language string from the app's ResourceDictionary.
    /// </summary>
    public static class Str
    {
        /// <summary>Returns the localized string for the given key, or the key itself as fallback.</summary>
        public static string G(string key)
        {
            try { return System.Windows.Application.Current.FindResource(key) as string ?? key; }
            catch { return key; }
        }
    }
}
