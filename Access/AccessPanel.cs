using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PlayniteGameOverlay.Access;

namespace PlayniteGameOverlay
{
    /// <summary>
    /// The "Running apps" and "Quick settings" sections of the overlay. Every entry is a real button that
    /// takes keyboard focus, so a screen reader follows the controller as it moves, and changes are
    /// spoken as they happen.
    /// </summary>
    public partial class OverlayWindow
    {
        private const int MaxApps = 8;
        private DispatcherTimer summaryTimer;

        private void InitAccess()
        {
            // The window blocks the arrow keys so they do not fight the controller; give a keyboard
            // the same moves as the D-pad instead, plus Escape to go back to the game.
            AddHandler(PreviewKeyDownEvent, new KeyEventHandler(AccessKeyDown), true);
        }

        private void AccessKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down: FocusDown(); break;
                case Key.Up: FocusUp(); break;
                case Key.Left: FocusLeft(); break;
                case Key.Right: FocusRight(); break;
                case Key.Escape: ViewModel.ReturnToGameCommand.Execute(null); break;
            }
        }

        private Button AccessButton(string text, string spokenName, Action onClick)
        {
            var b = new Button { Content = text, Style = (Style)FindResource("AccessButtonStyle") };
            AutomationProperties.SetName(b, spokenName ?? text);
            b.Click += (s, e) => onClick();
            return b;
        }

        private static TextBlock AccessHeading(string text)
        {
            return new TextBlock { Text = text, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0x80, 0x80, 0x80)), Margin = new Thickness(8, 0, 0, 8) };
        }

        private static string Shorten(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        /// <summary>Rebuilds both sections. Called each time the overlay is shown.</summary>
        private void BuildAccessPanel()
        {
            // ----- running apps -----
            RunningAppsPanel.Children.Clear();
            RunningAppsPanel.Children.Add(AccessHeading("RUNNING APPS"));
            List<SystemControls.AppWindow> windows;
            try { windows = SystemControls.OpenWindows(); }
            catch { windows = new List<SystemControls.AppWindow>(); }
            int shown = 0;
            foreach (SystemControls.AppWindow w in windows)
            {
                if (shown >= MaxApps) break;
                IntPtr handle = w.Handle;
                string title = w.Title;
                RunningAppsPanel.Children.Add(AccessButton(Shorten(title, 34), "Switch to " + title, () =>
                {
                    Hide();
                    SystemControls.SwitchTo(handle);
                }));
                shown++;
            }
            if (shown == 0)
                RunningAppsPanel.Children.Add(new TextBlock { Text = "Nothing else is open", Foreground = Brushes.White, Margin = new Thickness(8, 4, 0, 4) });

            // ----- quick settings -----
            QuickSettingsPanel.Children.Clear();
            QuickSettingsPanel.Children.Add(AccessHeading("QUICK SETTINGS"));
            QuickSettingsPanel.Children.Add(AccessButton("Status", "Status", () => Speech.Say(this, StatusText())));

            if (AsusPower.Available)
            {
                Button power = null;
                string name = AsusPower.CurrentName;
                power = AccessButton("Power mode" + (name == null ? "" : ": " + name), "Power mode" + (name == null ? "" : ", " + name), () =>
                {
                    string now = AsusPower.Next();
                    if (now == null) { Speech.Say(this, "Power mode could not be changed"); return; }
                    power.Content = "Power mode: " + now;
                    AutomationProperties.SetName(power, "Power mode, " + now);
                    Speech.Say(this, "Power mode " + now);
                });
                QuickSettingsPanel.Children.Add(power);
            }

            QuickSettingsPanel.Children.Add(AccessButton("Volume up", null, () => SayPercent("Volume", SystemControls.ChangeVolume(+10))));
            QuickSettingsPanel.Children.Add(AccessButton("Volume down", null, () => SayPercent("Volume", SystemControls.ChangeVolume(-10))));
            QuickSettingsPanel.Children.Add(AccessButton("Mute", "Mute or unmute", () =>
            {
                bool? muted = SystemControls.ToggleMute();
                Speech.Say(this, muted == null ? "Mute could not be changed" : (muted.Value ? "Muted" : "Unmuted"));
            }));

            if (SystemControls.Brightness >= 0)
            {
                QuickSettingsPanel.Children.Add(AccessButton("Brightness up", null, () => SayPercent("Brightness", SystemControls.ChangeBrightness(+10))));
                QuickSettingsPanel.Children.Add(AccessButton("Brightness down", null, () => SayPercent("Brightness", SystemControls.ChangeBrightness(-10))));
            }
        }

        private void SayPercent(string what, int value)
        {
            Speech.Say(this, value < 0 ? what + " could not be changed" : what + " " + value + " percent");
        }

        private string StatusText()
        {
            var parts = new List<string>();
            parts.Add(ViewModel.IsGameRunning && !string.IsNullOrWhiteSpace(ViewModel.GameTitle) ? ViewModel.GameTitle : "No game running");
            if (ViewModel.IsGameRunning && !string.IsNullOrWhiteSpace(ViewModel.SessionPlaytimeText))
                parts.Add("this session " + ViewModel.SessionPlaytimeText);
            try
            {
                System.Windows.Forms.PowerStatus power = System.Windows.Forms.SystemInformation.PowerStatus;
                if (power.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery && power.BatteryLifePercent <= 1f)
                    parts.Add("battery " + (int)Math.Round(power.BatteryLifePercent * 100) + " percent, " +
                              (power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "plugged in" : "on battery"));
            }
            catch { }
            parts.Add("time " + DateTime.Now.ToString("h:mm tt"));
            int volume = SystemControls.Volume;
            if (volume >= 0) parts.Add("volume " + volume + " percent");
            int brightness = SystemControls.Brightness;
            if (brightness >= 0) parts.Add("brightness " + brightness + " percent");
            if (AsusPower.Available && AsusPower.CurrentName != null) parts.Add("power mode " + AsusPower.CurrentName);
            return string.Join(". ", parts) + ".";
        }

        /// <summary>Speaks a short summary a moment after the overlay opens, once the focus announcement is out.</summary>
        private void AnnounceOpened()
        {
            if (summaryTimer == null)
            {
                summaryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                summaryTimer.Tick += (s, e) =>
                {
                    summaryTimer.Stop();
                    if (Active) Speech.Say(this, StatusText());
                };
            }
            summaryTimer.Stop();
            summaryTimer.Start();
        }
    }
}
