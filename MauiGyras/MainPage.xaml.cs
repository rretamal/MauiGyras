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

        private const int EnemyCount = 3;
        private List<EnemyShip> enemies = new List<EnemyShip>();
        private List<Explosion> explosions = new List<Explosion>();

        public MainPage(IVoiceRecognitionService voiceRecognitionService)
        {
            InitializeComponent();

            _voiceRecognitionService = voiceRecognitionService;
            canvasView.PaintSurface += CanvasView_PaintSurface;
           
            InitializeStars();
            InitializeSensors();
            InitializeEnemies();
        }

        private void _voiceRecognitionService_OnSpeechRecognized(object? sender, string e)
        {
            var words = e.ToLower().Split(' ');
            foreach (var word in words)
            {
                if (word.Contains("pew") || word.Contains("pure") || word.Contains("pum") || word.Contains("boom"))
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
                    UpdateGame();
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

            // Draw enemy ships
            foreach (var enemy in enemies)
            {
                DrawEnemyShip(canvas, enemy, info);
            }

            // Draw enemy shots
            using (var shotPaint = new SKPaint { Color = SKColors.Green, StrokeWidth = 8 })
            {
                foreach (var enemy in enemies)
                {
                    foreach (var shot in enemy.Shots)
                    {
                        float x = shot.X - shipPosition.X;
                        float y = shot.Y - shipPosition.Y;
                        x += cameraRotationY * 20;
                        y += cameraRotationX * 20;
                        float z = 1000 + (x * x + y * y) / 20000;
                        x = x * 1000 / z + info.Width / 2;
                        y = y * 1000 / z + info.Height / 2;
                        canvas.DrawLine(x, y, x, y + 25, shotPaint);
                    }
                }
            }

            // Draw explosions
            foreach (var explosion in explosions)
            {
                explosion.Draw(canvas, shipPosition, info, cameraRotationX, cameraRotationY);
            }

            using (var textPaint = new SKPaint
            {
                Color = SKColors.Yellow,
                TextSize = 30,
                IsAntialias = true
            })
            {
                canvas.DrawText($"Active explosions: {explosions.Count}", 10, 60, textPaint);
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

        private void InitializeEnemies()
        {
            for (int i = 0; i < EnemyCount; i++)
            {
                enemies.Add(new EnemyShip(
                    (float)(random.NextDouble() * 2000 - 1000),
                    (float)(random.NextDouble() * 2000 - 1000)
                ));
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

        private void DrawEnemyShip(SKCanvas canvas, EnemyShip enemy, SKImageInfo info)
        {
            float x = enemy.X - shipPosition.X;
            float y = enemy.Y - shipPosition.Y;

            // Rotate the camera and apply 3D effect (similar to stars)
            x += cameraRotationY * 20;
            y += cameraRotationX * 20;
            float z = 1000 + (x * x + y * y) / 20000;
            x = x * 1000 / z + info.Width / 2;
            y = y * 1000 / z + info.Height / 2;

            // Check if the enemy is visible on screen
            if (x >= 0 && x <= info.Width && y >= 0 && y <= info.Height)
            {
                canvas.Save();
                canvas.Translate(x, y);
                canvas.Scale(1000 / z);

                using (var paint = new SKPaint { IsAntialias = true })
                {
                    // Enemy ship body
                    paint.Color = new SKColor(192, 57, 43); // Rojo oscuro
                    var bodyPath = new SKPath();
                    bodyPath.MoveTo(-20, 15);
                    bodyPath.LineTo(20, 15);
                    bodyPath.LineTo(0, -30);
                    bodyPath.Close();
                    canvas.DrawPath(bodyPath, paint);

                    // Enemy wings
                    paint.Color = new SKColor(231, 76, 60); // Rojo claro
                    var leftWing = new SKPath();
                    leftWing.MoveTo(-20, 15);
                    leftWing.LineTo(-40, 30);
                    leftWing.LineTo(-15, 0);
                    leftWing.Close();
                    canvas.DrawPath(leftWing, paint);

                    var rightWing = new SKPath();
                    rightWing.MoveTo(20, 15);
                    rightWing.LineTo(40, 30);
                    rightWing.LineTo(15, 0);
                    rightWing.Close();
                    canvas.DrawPath(rightWing, paint);

                    // Enemy cabin
                    paint.Color = new SKColor(52, 152, 219); // Azul
                    canvas.DrawCircle(0, -5, 10, paint);
                }

                canvas.Restore();
            }
        }

        private void UpdateGame()
        {
            // Update enemy positions and fire shots
            foreach (var enemy in enemies)
            {
                enemy.Move();
                enemy.UpdateShots();

                if (random.NextDouble() < 0.01) // 1% chance to fire each frame
                {
                    enemy.Fire();
                }
            }

            // Check for collisions
            CheckCollisions();

            // Update explosions
            for (int i = explosions.Count - 1; i >= 0; i--)
            {
                explosions[i].Update();
                if (explosions[i].IsFinished)
                {
                    explosions.RemoveAt(i);
                }
            }

            // Respawn enemies that are too far away
            RespawnEnemies();
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

        private void CheckCollisions()
        {
            float screenCenterX = canvasView.CanvasSize.Width / 2;
            float screenCenterY = canvasView.CanvasSize.Height / 2;

            // Check player shots against enemies
            for (int i = activeShots.Count - 1; i >= 0; i--)
            {
                var shot = activeShots[i];
                for (int j = enemies.Count - 1; j >= 0; j--)
                {
                    var enemy = enemies[j];

                    // Calculate screen positions
                    float enemyScreenX = screenCenterX + (enemy.X - shipPosition.X);
                    float enemyScreenY = screenCenterY + (enemy.Y - shipPosition.Y);
                    float shotScreenX = shot.X;//screenCenterX + (shot.X - shipPosition.X);
                    float shotScreenY = shot.Y; // screenCenterY + (shot.Y - shipPosition.Y);

                    float distance = (float)Math.Sqrt(
               Math.Pow(shotScreenX - enemyScreenX, 2) +
               Math.Pow(shotScreenY - enemyScreenY, 2));

                    Console.WriteLine($"Shot ({shotScreenX},{shotScreenY}) Enemy ({enemyScreenX},{enemyScreenY}) Distance: {distance}");

                    if (distance < 50) // Adjust this value as needed
                    {
                        explosions.Add(new Explosion(enemy.X, enemy.Y));
                        activeShots.RemoveAt(i);
                        enemies.RemoveAt(j);
                        Console.WriteLine("Enemy hit!");
                        break;
                    }

                    //if (Math.Abs(shotRelativeX - relativeX) < 50 &&
                    //    Math.Abs(shotRelativeY - relativeY) < 50)
                    //{
                    //    activeShots.RemoveAt(i);
                    //    enemies.RemoveAt(j);
                    //    explosions.Add(new Explosion(enemy.X, enemy.Y));
                    //    Console.WriteLine($"Enemy destroyed! Explosion added at ({enemy.X}, {enemy.Y})");
                    //    break;
                    //}
                }
            }

            // Check enemy shots against player (simplified collision)
            foreach (var enemy in enemies)
            {
                for (int i = enemy.Shots.Count - 1; i >= 0; i--)
                {
                    var shot = enemy.Shots[i];
                    if (Math.Abs(shot.X - shipPosition.X) < 30 &&
                        Math.Abs(shot.Y - shipPosition.Y) < 30)
                    {
                        // Player hit! Implement game over logic here
                        enemy.Shots.RemoveAt(i);
                        Console.WriteLine("Player hit!"); // Debug output

                        explosions.Add(new Explosion(shipPosition.X, shipPosition.Y));
                    }
                }
            }
        }

        private void ApplyCameraAndPerspective(ref float x, ref float y)
        {
            x += cameraRotationY * 20;
            y += cameraRotationX * 20;
            float z = 1000 + (x * x + y * y) / 20000;
            x = x * 1000 / z;
            y = y * 1000 / z;
        }

        private async Task FireShip()
        {
            float centerX = canvasView.CanvasSize.Width / 2;
            float centerY = canvasView.CanvasSize.Height / 2;
            activeShots.Add(new Shot(centerX, centerY));
            Console.WriteLine($"Shoot added on: ({centerX}, {centerY})");
            MainThread.BeginInvokeOnMainThread(() => canvasView.InvalidateSurface());
        }

        private void RespawnEnemies()
        {
            while (enemies.Count < EnemyCount)
            {
                enemies.Add(new EnemyShip(
                    shipPosition.X + (float)(random.NextDouble() * 2000 - 1000),
                    shipPosition.Y + (float)(random.NextDouble() * 2000 - 1000)
                ));
            }
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

        private class EnemyShip
        {
            public float X, Y;
            public List<Shot> Shots = new List<Shot>();
            private Random random = new Random();

            public EnemyShip(float x, float y)
            {
                X = x;
                Y = y;
            }

            public void Move()
            {
                // Simple random movement
                X += (float)(random.NextDouble() * 4 - 2);
                Y += (float)(random.NextDouble() * 4 - 2);
            }

            public void Fire()
            {
                Shots.Add(new Shot(X, Y));
            }

            public void UpdateShots()
            {
                for (int i = Shots.Count - 1; i >= 0; i--)
                {
                    Shots[i].Move(true); // true for moving down
                    if (Shots[i].Y > 1000) // Adjust based on your game's scale
                    {
                        Shots.RemoveAt(i);
                    }
                }
            }
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

            public void Move(bool down = false)
            {
                Y += down ? Speed : -Speed;
            }
        }

        private class Explosion
        {
            public float X { get; private set; }
            public float Y { get; private set; }
            private const int ParticleCount = 50;
            private List<ExplosionParticle> particles;
            private const int Duration = 60; // frames
            private int currentFrame = 0;

            public bool IsFinished => currentFrame >= Duration;

            public Explosion(float x, float y)
            {
                X = x;
                Y = y;
                particles = new List<ExplosionParticle>();
                for (int i = 0; i < ParticleCount; i++)
                {
                    particles.Add(new ExplosionParticle(x, y));
                }
            }

            public void Update()
            {
                currentFrame++;
                foreach (var particle in particles)
                {
                    particle.Update();
                }
            }

            public void Draw(SKCanvas canvas, SKPoint shipPosition, SKImageInfo info,float cameraRotationX, float cameraRotationY)
            {
                float x = X - shipPosition.X;
                float y = Y - shipPosition.Y;

                // Apply the rotation of the camera
                x += cameraRotationY * 20;
                y += cameraRotationX * 20;

                float z = 1000 + (x * x + y * y) / 20000;
                x = x * 1000 / z + info.Width / 2;
                y = y * 1000 / z + info.Height / 2;

                using (var paint = new SKPaint { IsAntialias = true })
                {
                    foreach (var particle in particles)
                    {
                        float alpha = 1 - (float)currentFrame / Duration;
                        paint.Color = particle.Color.WithAlpha((byte)(255 * alpha));
                        canvas.DrawCircle(
                            x + particle.OffsetX * (1000 / z),
                            y + particle.OffsetY * (1000 / z),
                            particle.Size * (1000 / z),
                            paint
                        );
                    }
                }
            }
        }

        private class ExplosionParticle
        {
            public float OffsetX { get; private set; }
            public float OffsetY { get; private set; }
            public float VelocityX { get; private set; }
            public float VelocityY { get; private set; }
            public float Size { get; private set; }
            public SKColor Color { get; private set; }

            private static Random random = new Random();

            public ExplosionParticle(float x, float y)
            {
                OffsetX = 0;
                OffsetY = 0;
                float angle = (float)(random.NextDouble() * Math.PI * 2);

                // Increase the speed
                float speed = (float)(random.NextDouble() * 4 + 2); 
                VelocityX = (float)Math.Cos(angle) * speed;
                VelocityY = (float)Math.Sin(angle) * speed;

                // Increase the size
                Size = (float)(random.NextDouble() * 5 + 2); 
                Color = new SKColor(
                    (byte)random.Next(200, 256),
                    (byte)random.Next(100, 200),
                    (byte)random.Next(50)
                );
            }

            public void Update()
            {
                OffsetX += VelocityX;
                OffsetY += VelocityY;
                VelocityY += 0.1f; 
                Size *= 0.98f; 
            }
        }
    }


}
