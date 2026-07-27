using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using MPHEditor.Services;
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

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
