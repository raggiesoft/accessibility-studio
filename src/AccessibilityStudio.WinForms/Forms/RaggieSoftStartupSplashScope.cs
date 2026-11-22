// ================================================================================
// FILE: Forms/RaggieSoftStartupSplashScope.cs
// PROJECT: AccessibilityStudio.WinForms & RaggieSoft.HelpViewer (Shared WinForms Helper)
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL DELAYED-THRESHOLD STARTUP SPLASH GUIDE):
//   What is this file, and why does it exist?
//     When a desktop application launches from a warm SSD cache in 80 milliseconds,
//     flashing a splash screen on and off feels jarring. Conversely, on a cold boot
//     or first launch—when initializing Chromium (`WebView2`), scanning a large folder,
//     or unpacking offline documentation takes half a second or more—leaving the user
//     staring at nothing makes the app feel unresponsive!
//
//     `RaggieSoftStartupSplashScope` solves both cases with a **250 ms Delay Threshold**
//     paired with a **600 ms Minimum Display Hold**:
//       1. **Fast Launch (< 250 ms)**:
//          `Begin(...)` starts a lightweight 250 ms UI timer. If startup finishes before
//          250 ms elapses, `CompleteAsync()` cancels the timer and exits immediately—the
//          splash window is NEVER created or shown!
//       2. **Extended Data/Engine Setup (>= 250 ms)**:
//          As soon as startup crosses 250 ms, a borderless High-DPI `520x300` RaggieSoft
//          card (`Assets\AppSplashBackdrop.png`) appears centered on screen with live
//          status updates in its bottom footer strip and polite `UIAutomation` labels
//          for screen-reader users. Once shown, `CompleteAsync()` keeps it visible for
//          at least 600 ms total so it never flickers, then closes it cleanly.
//
//   Inputs & Outputs:
//     - Inputs: `appDisplayName`, `initialStatusMessage`, and `Assets\AppSplashBackdrop.png`.
//     - Output: Smooth, flicker-free RaggieSoft startup feedback only when needed!
// ================================================================================

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AccessibilityStudio.WinForms.Forms;

/// <summary>
/// Manages an on-demand RaggieSoft startup splash card that appears only if initialization
/// takes longer than 250 milliseconds, and stays visible for at least 600 milliseconds once shown.
/// </summary>
internal sealed class RaggieSoftStartupSplashScope : IDisposable
{
    // Wait 250 ms before showing the splash card so fast warm launches never flash.
    private const int ShowDelayMs = 250;

    // Once visible, hold for at least 600 ms so the card never pops up and vanishes in 1 frame.
    private const int MinimumVisibleHoldMs = 600;

    private readonly string _appDisplayName;
    private string _currentStatus;
    private readonly System.Windows.Forms.Timer _delayTimer;
    private StartupSplashCardForm? _splashForm;
    private Stopwatch? _visibleStopwatch;
    private bool _completed;

    private RaggieSoftStartupSplashScope(string appDisplayName, string initialStatusMessage)
    {
        _appDisplayName = appDisplayName;
        _currentStatus = initialStatusMessage;

        // Start a single-shot 250 ms timer on the STA UI message loop.
        _delayTimer = new System.Windows.Forms.Timer { Interval = ShowDelayMs };
        _delayTimer.Tick += OnDelayTimerTick;
        _delayTimer.Start();
    }

    /// <summary>
    /// Starts monitoring an application startup sequence with a 250 ms threshold.
    /// </summary>
    public static RaggieSoftStartupSplashScope Begin(string appDisplayName, string initialStatusMessage) =>
        new(appDisplayName, initialStatusMessage);

    /// <summary>
    /// Updates the live status line rendered along the bottom strip of the splash card.
    /// </summary>
    public void UpdateStatus(string statusMessage)
    {
        if (_completed || string.IsNullOrWhiteSpace(statusMessage))
        {
            return;
        }

        _currentStatus = statusMessage;
        if (_splashForm is { IsDisposed: false })
        {
            _splashForm.SetStatusText(statusMessage);
        }
    }

    /// <summary>
    /// Completes startup: cancels the splash immediately if not yet visible, or waits out
    /// the remainder of the 600 ms minimum visibility window before closing smoothly.
    /// </summary>
    public async Task CompleteAsync()
    {
        if (_completed)
        {
            return;
        }

        _delayTimer.Stop();

        if (_splashForm is { IsDisposed: false } && _visibleStopwatch is not null)
        {
            int elapsedMs = (int)_visibleStopwatch.ElapsedMilliseconds;
            int remainingMs = MinimumVisibleHoldMs - elapsedMs;
            if (remainingMs > 0)
            {
                await Task.Delay(remainingMs);
            }
        }

        Dispose();
    }

    private void OnDelayTimerTick(object? sender, EventArgs e)
    {
        _delayTimer.Stop();
        if (_completed)
        {
            return;
        }

        _splashForm = new StartupSplashCardForm(_appDisplayName, _currentStatus);
        _splashForm.Show();
        _splashForm.Refresh();
        _visibleStopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _delayTimer.Stop();
        _delayTimer.Tick -= OnDelayTimerTick;
        _delayTimer.Dispose();

        if (_splashForm is { IsDisposed: false })
        {
            _splashForm.Close();
            _splashForm.Dispose();
        }
        _splashForm = null;
    }

    /// <summary>
    /// Borderless High-DPI `520x300` RaggieSoft splash card painted from `Assets\AppSplashBackdrop.png`
    /// with a fallback vector Navy+Gold theme if the image asset is ever absent.
    /// </summary>
    private sealed class StartupSplashCardForm : Form
    {
        private readonly Image? _backdropImage;
        private readonly Label _lblStatus;
        private readonly ProgressBar _progressBar;

        public StartupSplashCardForm(string appDisplayName, string initialStatus)
        {
            // Configure a borderless, non-taskbar, screen-centered card scaled cleanly via PerMonitorV2 DPI.
            Text = $"{appDisplayName} — Starting";
            AccessibleName = $"{appDisplayName} Startup Progress";
            AccessibleRole = AccessibleRole.Dialog;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 300);
            BackColor = Color.FromArgb(11, 19, 43); // RaggieSoft Deep Cosmic Navy `#0B132B`

            // Load our High-DPI `1040x600` (2x) backdrop PNG from `Assets\AppSplashBackdrop.png`.
            string backdropPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppSplashBackdrop.png");
            if (File.Exists(backdropPath))
            {
                try
                {
                    using FileStream fs = File.OpenRead(backdropPath);
                    using Image raw = Image.FromStream(fs);
                    _backdropImage = new Bitmap(raw);
                }
                catch
                {
                    _backdropImage = null;
                }
            }

            // Live status label positioned inside the clean 48px footer strip below the cyan-to-gold divider bar.
            _lblStatus = new Label
            {
                AutoSize = false,
                BackColor = Color.FromArgb(11, 19, 43),
                ForeColor = Color.FromArgb(241, 245, 249),
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Location = new Point(26, 260),
                Size = new Size(340, 22),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = initialStatus,
                AccessibleName = "Startup Status",
                AccessibleRole = AccessibleRole.StatusBar
            };

            // Compact marquee activity bar on the bottom-right of the footer strip.
            // Honors Windows Remote Desktop / Reduced-Animation sessions by switching to Continuous blocks.
            bool reduceMotion = SystemInformation.TerminalServerSession || !SystemInformation.IsMenuAnimationEnabled;
            _progressBar = new ProgressBar
            {
                Location = new Point(374, 265),
                Size = new Size(120, 10),
                Style = reduceMotion ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = reduceMotion ? 0 : 28,
                Value = reduceMotion ? 65 : 0,
                AccessibleName = "Loading Indicator"
            };

            Controls.Add(_lblStatus);
            Controls.Add(_progressBar);
        }

        public void SetStatusText(string text)
        {
            _lblStatus.Text = text;
            _lblStatus.Update();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            if (_backdropImage is not null)
            {
                // Draw the 2x `1040x600` RaggieSoft backdrop scaled crisply into our current DPI bounds.
                g.DrawImage(_backdropImage, ClientRectangle);
                return;
            }

            // Clean vector fallback if `AppSplashBackdrop.png` is unavailable
            g.Clear(Color.FromArgb(11, 19, 43));
            using Pen outerPen = new(Color.FromArgb(56, 189, 248), 3F);
            using Pen innerPen = new(Color.FromArgb(251, 191, 36), 1F);
            g.DrawRectangle(outerPen, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
            g.DrawRectangle(innerPen, 5, 5, ClientSize.Width - 11, ClientSize.Height - 11);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _backdropImage?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
