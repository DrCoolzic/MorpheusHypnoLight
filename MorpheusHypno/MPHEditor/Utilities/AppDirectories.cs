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
            // Create a "Settings" directory in AppDataDirectory
            string settingsDir = Path.Combine(GetAppDataDirectory(), "Settings");
            if (!Directory.Exists(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }
            return settingsDir;
        }

        ///// <summary>
        ///// Returns the path to the temporary directory used by the application.
        ///// </summary>
        ///// <returns>The path to the temporary directory used by the application.</returns>
        //public static string GetTempDirectory()
        //{
        //    // Use the cache directory for temporary files
        //    var tempDir = Path.Combine(GetAppDataDirectory(), "Temp");
        //    if (!Directory.Exists(tempDir))
        //    {
        //        Directory.CreateDirectory(tempDir);
        //    }
        //    return tempDir;
        //}

        //public static string GetCacheDirectory()
        //{
        //    var cacheDir = Path.Combine(GetAppDataDirectory(), "Cache");
        //    if (!Directory.Exists(cacheDir))
        //    {
        //        Directory.CreateDirectory(cacheDir);
        //    }
        //    return cacheDir;
        //}

        //    /// <summary>
        //    /// Gets the storage directory path for Android
        //    /// </summary>
        //    public static string GetStorageDirectory()
        //    {
        //#if ANDROID
        //        // Get the app-specific external directory
        //        var context = Android.App.Application.Context;
        //        var externalDir = context.GetExternalFilesDir(null)?.AbsolutePath;

        //        if (string.IsNullOrEmpty(externalDir))
        //        {
        //            _logger?.LogWarning("External storage not available, falling back to internal storage");
        //            return GetAppDataDirectory();
        //        }
        //        return externalDir;

        //#else
        //        return GetAppDataDirectory();
        //#endif
        //    }


        /// <summary>
        /// Logs the directories used by the application to the console.
        /// </summary>
        public static void LogDirectories()
        {
            if (_logger == null) return;
            _logger.LogInformation("Application Directories:");
            _logger.LogInformation(" - AppDataDirectory: {}", GetAppDataDirectory());
            //_logger.LogInformation(" - CacheDirectory: {}", GetCacheDirectory());
            _logger.LogInformation(" - User Settings Directory: {}", GetUserSettingsDirectory());
            //_logger.LogInformation(" - Temp Directory: {}", GetTempDirectory());
        }


        //public static void CleanTempDirectory()
        //{
        //    try
        //    {
        //        var tempDir = GetTempDirectory();
        //        if (Directory.Exists(tempDir))
        //        {
        //            Directory.Delete(tempDir, true);
        //            Directory.CreateDirectory(tempDir);
        //            _logger?.LogInformation("Temp directory cleaned successfully");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger?.LogError(ex, "Error cleaning temp directory");
        //    }
        //}
    }
}
