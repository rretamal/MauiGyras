using MauiGyras.Services;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace MauiGyras
{
    public partial class MainPage : ContentPage
    {
        private const int StarCount = 1000;
        private SKPoint[] stars = new SKPoint[StarCount];
        private SKPoint shipPosition;
        private float cameraRotationX, cameraRotationY;
        private Random random = new Random();
        private float baseSpeed = 20f;
        private readonly IVoiceRecognitionService _voiceRecognitionService;
        private CancellationTokenSource tokenSource = new CancellationTokenSource();
        private List<Shot> activeShots = new List<Shot>();
        private ConcurrentQueue<DateTime> pewTimes = new ConcurrentQueue<DateTime>();
        private const double PewCooldown = 250; // milliseconds

        public MainPage(IVoiceRecognitionService voiceRecognitionService)
        {
            InitializeComponent();

            _voiceRecognitionService = voiceRecognitionService;
            canvasView.PaintSurface += CanvasView_PaintSurface;
           
            InitializeStars();
            InitializeSensors();
        }

        private void _voiceRecognitionService_OnSpeechRecognized(object? sender, string e)
        {
            var words = e.ToLower().Split(' ');
            foreach (var word in words)
            {
                if (word.Contains("pew"))
                {
                    pewTimes.Enqueue(DateTime.Now);
                }
            }
        }

        override protected async void OnAppearing()
        {
            base.OnAppearing();
            await _voiceRecognitionService.RequestPermissions();
            _= _voiceRecognitionService.Listen(CultureInfo.GetCultureInfo("en-us"), new Progress<string>(partialText =>
            {
                listenedText = partialText;

                if (!string.IsNullOrEmpty(listenedText))
                {
                    System.Diagnostics.Debug.WriteLine("*** partialText ***");
                    System.Diagnostics.Debug.WriteLine(partialText);

                    _voiceRecognitionService_OnSpeechRecognized(this, partialText);

                    OnPropertyChanged(nameof(listenedText));
                }
            }), tokenSource.Token);

            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), () =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ProcessPewQueue();
                    canvasView.InvalidateSurface();
                });
                return true; // Return true to keep the timer running
            });
        }

        private void OnSpeechRecognized(object sender, string result)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                //RecognizedTextLabel.Text = result;
            });
        }

        private void CanvasView_PaintSurface(object? sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
        {
            SKCanvas canvas = e.Surface.Canvas;
            SKImageInfo info = e.Info;

            canvas.Clear(SKColors.Black);

            // Draw the stars
            using (SKPaint starPaint = new SKPaint { Color = SKColors.White })
            {
                for (int i = 0; i < StarCount; i++)
                {
                    float x = stars[i].X - shipPosition.X;
                    float y = stars[i].Y - shipPosition.Y;

                    // Rotate the camera
                    x += cameraRotationY * 20;
                    y += cameraRotationX * 20;

                    // Simulate a 3D effect
                    float z = 1000 + (x * x + y * y) / 20000;
                    x = x * 1000 / z + info.Width / 2;
                    y = y * 1000 / z + info.Height / 2;

                    // Check position of the stars
                    if (x < 0 || x > info.Width || y < 0 || y > info.Height)
                    {
                        // Move the start to the other side of the screen
                        stars[i] = new SKPoint(
                            shipPosition.X + (float)(random.NextDouble() * 2000 - 1000),
                            shipPosition.Y + (float)(random.NextDouble() * 2000 - 1000)
                        );
                    }
                    else
                    {
                        canvas.DrawCircle(x, y, 2 / (z / 500), starPaint);
                    }
                }
            }

            // Draw ship
            DrawSpaceship(canvas, info.Width / 2, info.Height / 2);

            // Draw shots
            using (var shotPaint = new SKPaint { Color = SKColors.Red, StrokeWidth = 10 })
            {
                for (int i = activeShots.Count - 1; i >= 0; i--)
                {
                    var shot = activeShots[i];

                    canvas.DrawLine(shot.X, shot.Y, shot.X, shot.Y - 30, shotPaint);

                    shot.Move();

                    if (shot.Y < 0)
                    {
                        activeShots.RemoveAt(i);
                        Console.WriteLine("Shoot deleted");
                    }
                }
            }

            // Draw an indicator
            using (var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 30,
                IsAntialias = true
            })
            {
                canvas.DrawText($"Active shoots: {activeShots.Count}", 10, 30, textPaint);
            }

            // Motion Indicator
            using (var paint = new SKPaint
            {
                Color = SKColors.Cyan,
                TextSize = 40,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            })
            {
                float speed = (float)Math.Sqrt(shipPosition.X * shipPosition.X + shipPosition.Y * shipPosition.Y);
                string speedText = $"Speed: {speed:F1}";

                // Draw a background for the speed label
                using (var bgPaint = new SKPaint
                {
                    Color = SKColors.Black.WithAlpha(150),
                    Style = SKPaintStyle.Fill
                })
                {
                    float textWidth = paint.MeasureText(speedText);
                    float bgPadding = 20;
                    canvas.DrawRoundRect(
                        new SKRoundRect(new SKRect(
                            info.Width / 2 - textWidth / 2 - bgPadding,
                            info.Height - 60 - bgPadding,
                            info.Width / 2 + textWidth / 2 + bgPadding,
                            info.Height - 20 + bgPadding
                        ), 10, 10),
                        bgPaint
                    );
                }

                // Show a speed label
                canvas.DrawText(speedText, info.Width / 2, info.Height - 40, paint);
            }
        }

        private void InitializeStars()
        {
            for (int i = 0; i < StarCount; i++)
            {
                stars[i] = new SKPoint(
                    (float)(random.NextDouble() * 2000 - 1000),
                    (float)(random.NextDouble() * 2000 - 1000)
                );
            }
        }

        private void InitializeSensors()
        {
            if (Accelerometer.Default.IsSupported)
            {
                Accelerometer.Default.ReadingChanged += OnAccelerometerReadingChanged;
                Accelerometer.Default.Start(SensorSpeed.UI);
            }

            if (Gyroscope.Default.IsSupported)
            {
                Gyroscope.Default.ReadingChanged += OnGyroscopeReadingChanged;
                Gyroscope.Default.Start(SensorSpeed.UI);
            }
        }

        private void OnAccelerometerReadingChanged(object sender, AccelerometerChangedEventArgs e)
        {
            baseSpeed = Math.Max(5f, Math.Min(50f, 20f + e.Reading.Acceleration.Z * 30f));

            shipPosition.X += e.Reading.Acceleration.X * baseSpeed;
            shipPosition.Y -= e.Reading.Acceleration.Y * baseSpeed;
            MainThread.BeginInvokeOnMainThread(() => canvasView.InvalidateSurface());
        }

        private void OnGyroscopeReadingChanged(object sender, GyroscopeChangedEventArgs e)
        {
            cameraRotationY += e.Reading.AngularVelocity.Y * 0.3f;
            cameraRotationX += e.Reading.AngularVelocity.X * 0.3f;
            MainThread.BeginInvokeOnMainThread(() => canvasView.InvalidateSurface());
        }

        private void DrawSpaceship(SKCanvas canvas, float centerX, float centerY)
        {
            // Rotate the ship
            canvas.Save();
            canvas.RotateDegrees(-cameraRotationY * 10, centerX, centerY);

            using (var paint = new SKPaint { IsAntialias = true })
            {
                // Ship body
                paint.Color = new SKColor(41, 128, 185); // Azul acero
                var bodyPath = new SKPath();
                bodyPath.MoveTo(centerX - 30, centerY + 20);
                bodyPath.LineTo(centerX + 30, centerY + 20);
                bodyPath.LineTo(centerX, centerY - 40);
                bodyPath.Close();
                canvas.DrawPath(bodyPath, paint);

                // Wings
                paint.Color = new SKColor(52, 152, 219); // Azul claro
                var leftWing = new SKPath();
                leftWing.MoveTo(centerX - 30, centerY + 20);
                leftWing.LineTo(centerX - 60, centerY + 40);
                leftWing.LineTo(centerX - 25, centerY);
                leftWing.Close();
                canvas.DrawPath(leftWing, paint);

                var rightWing = new SKPath();
                rightWing.MoveTo(centerX + 30, centerY + 20);
                rightWing.LineTo(centerX + 60, centerY + 40);
                rightWing.LineTo(centerX + 25, centerY);
                rightWing.Close();
                canvas.DrawPath(rightWing, paint);

                // Cabin
                paint.Color = new SKColor(241, 196, 15); // Amarillo
                canvas.DrawCircle(centerX, centerY - 10, 15, paint);

                // Propulse
                paint.Color = new SKColor(231, 76, 60); // Rojo
                canvas.DrawCircle(centerX - 15, centerY + 25, 8, paint);
                canvas.DrawCircle(centerX + 15, centerY + 25, 8, paint);

                // Propulse effect
                if (Math.Abs(shipPosition.X) > 5 || Math.Abs(shipPosition.Y) > 5)
                {
                    paint.Color = new SKColor(241, 196, 15, 150); 
                    var thrustPath = new SKPath();
                    thrustPath.MoveTo(centerX - 20, centerY + 30);
                    thrustPath.LineTo(centerX, centerY + 60 + (float)new Random().NextDouble() * 20);
                    thrustPath.LineTo(centerX + 20, centerY + 30);
                    canvas.DrawPath(thrustPath, paint);
                }
            }

            canvas.Restore();
        }

        private void ProcessPewQueue()
        {
            while (pewTimes.TryPeek(out DateTime pewTime))
            {
                if ((DateTime.Now - pewTime).TotalMilliseconds >= PewCooldown)
                {
                    pewTimes.TryDequeue(out _);
                    FireShip();
                }
                else
                {
                    break;
                }
            }
        }

        private async Task FireShip()
        {
            float centerX = canvasView.CanvasSize.Width / 2;
            float centerY = canvasView.CanvasSize.Height / 2;
            activeShots.Add(new Shot(centerX, centerY));
            Console.WriteLine($"Shoot added on: ({centerX}, {centerY})");
            MainThread.BeginInvokeOnMainThread(() => canvasView.InvalidateSurface());
        }

        string listenedText = string.Empty;
        private async void Listen()
        {
            var isAuthorized = await _voiceRecognitionService.RequestPermissions();
            if (isAuthorized)
            {
                try
                {
                    listenedText = await _voiceRecognitionService.Listen(CultureInfo.GetCultureInfo("en-us"),
                        new Progress<string>(partialText =>
                        {
                            if (DeviceInfo.Platform == DevicePlatform.Android)
                            {
                                listenedText = partialText;
                            }
                            else
                            {
                                listenedText += partialText + " ";
                            }

                            OnPropertyChanged(nameof(listenedText));
                        }), tokenSource.Token);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", ex.Message, "OK");
                }
            }
            else
            {
                await DisplayAlert("Permission Error", "No microphone access", "OK");
            }
        }

        private void ListenCancel()
        {
            tokenSource?.Cancel();
        }

        private class Shot
        {
            public float X, Y;
            public const float Speed = 10f;

            public Shot(float x, float y)
            {
                X = x;
                Y = y;
            }

            public void Move()
            {
                Y -= Speed;
            }
        }
    }


}
