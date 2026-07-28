using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using MPHCore.Services;
using MPHEditor.Pages;
using MPHEditor.Services;
using MPHEditor.Utilities;
using MPHEditor.ViewModels;
using Plugin.Maui.Audio;

namespace MPHEditor;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        builder.Services.AddSingleton(AudioManager.Current);

        builder.Services.AddSingleton<IBleService, BleService>();
        builder.Services.AddSingleton<ISequencePlayerService, SequencePlayerService>();
        builder.Services.AddSingleton<MetadataService>();
        builder.Services.AddSingleton<IMPHElementService, MPHElementService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        // Configure debug logging
        builder.Logging
            .ClearProviders()
            .AddConsole()
            .AddDebug();

        // Add file logging
        string appData;
        try
        {
            appData = AppDirectories.GetAppDataDirectory();
        }
        catch
        {
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "com.drcoolzic.mpheditor");
        }

        // var logDir = Path.Combine(appData, "logs");
        // Directory.CreateDirectory(logDir);
        // var logPath = Path.Combine(logDir, "mpheditor.log");
        var logPath = Path.Combine(appData, "mpheditor.log");

        builder.Logging.AddProvider(new FileLoggerProvider(logPath));
        var loggerFactory = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        var startupLogger = loggerFactory.CreateLogger("Startup");
        startupLogger.LogInformation("=== MPHEditor Log Started {Time} ===", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        try
        {
            startupLogger.LogInformation("Application Version: {Version}", AppInfo.Current.VersionString);
        }
        catch
        {
            // AppInfo may not be available in unpackaged mode during startup
        }
        startupLogger.LogInformation("OS Platform: {OS}", GetPlatform());
#endif

        MauiApp app = builder.Build();

#if DEBUG
        // Set up loggers for static services
        var logger = app.Services.GetRequiredService<ILogger<MauiApp>>();
        AppDirectories.SetLogger(logger);
        logger.LogInformation(" - AppDataDirectory: {}", AppDirectories.GetAppDataDirectory());
#endif

        return app;
    }

    private static string GetPlatform()
    {
        if (OperatingSystem.IsAndroid()) return "Android";
        if (OperatingSystem.IsIOS()) return "iOS";
        if (OperatingSystem.IsMacCatalyst()) return "macOS";
        if (OperatingSystem.IsWindows()) return "Windows";
        return "Unknown";
    }
}
