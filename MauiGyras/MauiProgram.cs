using MauiGyras.Services;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace MauiGyras
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                 .UseSkiaSharp()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            builder.Services.AddTransient<MainPage>();

            // Register the VoiceRecognitionService
            builder.Services.AddTransient<MainPage>();

#if ANDROID
            builder.Services.AddSingleton<MauiGyras.Platforms.Android.AndroidVoiceRecognitionService>();
            builder.Services.AddSingleton<IVoiceRecognitionService, MauiGyras.Platforms.Android.SpeechToTextImplementation>();
#endif

            return builder.Build();
        }
    }
}
