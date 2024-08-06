using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Speech;
using MauiGyras.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace MauiGyras.Platforms.Android
{
    public class AndroidVoiceRecognitionService : Java.Lang.Object, IRecognitionListener
    {
        private readonly ILogger<AndroidVoiceRecognitionService> _logger;
        private Action<string> _onResult;
        private Action<Exception> _onError;
        private SpeechRecognizer _speechRecognizer;
        private Intent _recognizerIntent;
        private bool _isListening;

        public AndroidVoiceRecognitionService(ILogger<AndroidVoiceRecognitionService> logger)
        {
            _logger = logger;
        }

        public void StartListening(CultureInfo culture, Action<string> onResult, Action<Exception> onError)
        {
            if (_isListening)
            {
                _logger.LogWarning("Already listening. Ignoring start request.");
                return;
            }

            _onResult = onResult;
            _onError = onError;
            _isListening = true;

            InitializeSpeechRecognizer();
            CreateRecognizerIntent(culture);
            _speechRecognizer.StartListening(_recognizerIntent);
        }

        public void StopListening()
        {
            if (!_isListening)
            {
                _logger.LogWarning("Not currently listening. Ignoring stop request.");
                return;
            }

            _isListening = false;
            _speechRecognizer?.StopListening();
            DisposeSpeechRecognizer();
        }

        private void InitializeSpeechRecognizer()
        {
            _speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(global::Android.App.Application.Context);
            _speechRecognizer.SetRecognitionListener(this);
        }

        private void CreateRecognizerIntent(CultureInfo culture)
        {
            _recognizerIntent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            _recognizerIntent.PutExtra(RecognizerIntent.ExtraLanguagePreference,Java.Util.Locale.Default);

            var javaLocale = Java.Util.Locale.ForLanguageTag(culture.Name);
            _recognizerIntent.PutExtra(RecognizerIntent.ExtraLanguage, javaLocale);
            _recognizerIntent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            _recognizerIntent.PutExtra(RecognizerIntent.ExtraCallingPackage, global::Android.App.Application.Context.PackageName);
            _recognizerIntent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
            _recognizerIntent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
            _recognizerIntent.PutExtra(RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis, 1000);
            _recognizerIntent.PutExtra(RecognizerIntent.ExtraSpeechInputPossiblyCompleteSilenceLengthMillis, 1000);
            _recognizerIntent.PutExtra(RecognizerIntent.ExtraSpeechInputMinimumLengthMillis, 5000);
        }

        public void OnResults(Bundle results)
        {
            ProcessResults(results);
            if (_isListening)
            {
                _speechRecognizer.StartListening(_recognizerIntent);
            }
        }

        public void OnPartialResults(Bundle partialResults)
        {
            ProcessResults(partialResults);
        }

        private void ProcessResults(Bundle bundle)
        {
            var matches = bundle?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            if (matches?.Count > 0)
            {
                var result = matches[0];
                _onResult?.Invoke(result);
            }
        }

        public void OnError([GeneratedEnum] SpeechRecognizerError error)
        {
            _logger.LogWarning($"Speech recognition error: {error}");

            switch (error)
            {
                case SpeechRecognizerError.NoMatch:
                case SpeechRecognizerError.SpeechTimeout:
                    if (_isListening)
                    {
                        _speechRecognizer.StartListening(_recognizerIntent);
                    }
                    break;
                case SpeechRecognizerError.NetworkTimeout:
                case SpeechRecognizerError.Network:
                    _onError?.Invoke(new Exception("Network error during speech recognition. Please check your internet connection."));
                    break;
                default:
                    _onError?.Invoke(new Exception($"Speech recognition error: {error}"));
                    break;
            }
        }

        private void DisposeSpeechRecognizer()
        {
            _speechRecognizer?.Destroy();
            _speechRecognizer = null;
        }

        // Implement other IRecognitionListener methods
        public void OnBeginningOfSpeech() { }
        public void OnBufferReceived(byte[] buffer) { }
        public void OnEndOfSpeech() { }
        public void OnEvent(int eventType, Bundle @params) { }
        public void OnReadyForSpeech(Bundle @params) { }
        public void OnRmsChanged(float rmsdB) { }
    }

    public class SpeechToTextImplementation : IVoiceRecognitionService
    {
        private readonly ILogger<SpeechToTextImplementation> _logger;
        private readonly AndroidVoiceRecognitionService _recognitionService;
        private CancellationTokenSource _cts;

        public SpeechToTextImplementation(
            ILogger<SpeechToTextImplementation> logger,
            AndroidVoiceRecognitionService recognitionService)
        {
            _logger = logger;
            _recognitionService = recognitionService;
        }

        public Task<string> Listen(CultureInfo culture, IProgress<string> recognitionResult, CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tcs = new TaskCompletionSource<string>();

            _recognitionService.StartListening(
                culture,
                result =>
                {
                    recognitionResult.Report(result);
                    if (_cts.IsCancellationRequested)
                    {
                        tcs.TrySetResult(result);
                    }
                },
                error =>
                {
                    _logger.LogError(error, "Error during continuous speech recognition");
                    tcs.TrySetException(error);
                });

            _cts.Token.Register(() =>
            {
                _recognitionService.StopListening();
                tcs.TrySetCanceled();
            });

            return tcs.Task;
        }

        public async Task<bool> RequestPermissions()
        {
            var status = await Permissions.RequestAsync<Permissions.Microphone>();
            var isAvailable = SpeechRecognizer.IsRecognitionAvailable(global::Android.App.Application.Context);
            return status == PermissionStatus.Granted && isAvailable;
        }
    }
}
