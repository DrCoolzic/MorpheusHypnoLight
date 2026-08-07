// Ignore Spelling: App

using System.Diagnostics;
using Microsoft.Extensions.Logging;


namespace MPHEditor.Utilities
{
    /// <summary>
    /// Static class containing utility methods for working with application directories.
    /// </summary>
    public static class AppDirectories
    {
        // Keep this in sync with the <ApplicationId> value in MPHEditor.csproj.
        private const string ApplicationId = "com.drcoolzic.mpheditor";

        private static string? _cachedAppDataPath;
        private static ILogger? _logger;

        public static void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets the correct application data directory path without profile name issues
        /// </summary>
        public static string GetAppDataDirectory()
        {
            if (_cachedAppDataPath != null)
                return _cachedAppDataPath;
#if ANDROID
            // Get the app-specific external directory
            var context = Android.App.Application.Context;
            var finalDataPath = context.GetExternalFilesDir(null)?.AbsolutePath;

            if (string.IsNullOrEmpty(finalDataPath))
            {
                _logger?.LogWarning("External storage not available, falling back to internal storage");
                throw new InvalidOperationException("External storage not available");
            }
#else
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // append the application id to the app data path to match the documented layout
            var finalDataPath = Path.Combine(appDataPath, ApplicationId);
#endif

            // Cache the result
            _cachedAppDataPath = finalDataPath;
            _logger?.LogInformation("Using application data directory: {Path}", finalDataPath);
            return finalDataPath;
        }


        /// <summary>
        /// Returns the path to the directory where user settings are stored.
        /// </summary>
        /// <returns>The path to the directory where user settings are stored.</returns>
        public static string GetUserSettingsDirectory()
        {
            return GetAppDataDirectory();
        }

        /// <summary>
        /// Returns the path to the directory where individual realtime-editor steps are
        /// saved/loaded (as standalone JSON files), creating it if it does not exist yet.
        /// </summary>
        /// <returns>The path to the "Steps" directory under the app data directory.</returns>
        public static string GetStepsDirectory()
        {
            var stepsPath = Path.Combine(GetAppDataDirectory(), "Steps");
            Directory.CreateDirectory(stepsPath);
            return stepsPath;
        }
    }
}
