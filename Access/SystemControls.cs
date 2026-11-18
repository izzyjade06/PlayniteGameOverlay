using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayniteGameOverlay.Access
{
    /// <summary>Volume, screen brightness and the list of open application windows.</summary>
    public static class SystemControls
    {
        // ---------- volume (Core Audio) ----------

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr notify);
            int UnregisterControlChangeNotify(IntPtr notify);
            int GetChannelCount(out int count);
            int SetMasterVolumeLevel(float level, ref Guid context);
            int SetMasterVolumeLevelScalar(float level, ref Guid context);
            int GetMasterVolumeLevel(out float level);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(int channel, float level, ref Guid context);
            int SetChannelVolumeLevelScalar(int channel, float level, ref Guid context);
            int GetChannelVolumeLevel(int channel, out float level);
            int GetChannelVolumeLevelScalar(int channel, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        private static IAudioEndpointVolume Endpoint()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice device;
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return null;
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return null;
            return o as IAudioEndpointVolume;
        }

        /// <summary>Volume in percent, or -1 if it cannot be read.</summary>
        public static int Volume
        {
            get
            {
                try
                {
                    IAudioEndpointVolume v = Endpoint();
                    float level;
                    if (v == null || v.GetMasterVolumeLevelScalar(out level) != 0) return -1;
                    return (int)Math.Round(level * 100);
                }
                catch { return -1; }
            }
        }

        public static int ChangeVolume(int deltaPercent)
        {
            try
            {
                IAudioEndpointVolume v = Endpoint();
                float level;
                if (v == null || v.GetMasterVolumeLevelScalar(out level) != 0) return -1;
                int target = Math.Max(0, Math.Min(100, (int)Math.Round(level * 100) + deltaPercent));
                Guid none = Guid.Empty;
                v.SetMasterVolumeLevelScalar(target / 100f, ref none);
                if (target > 0) v.SetMute(false, ref none);
                return target;
            }
            catch { return -1; }
        }

        /// <summary>Toggles mute; returns true if now muted, false if not, null on failure.</summary>
        public static bool? ToggleMute()
        {
            try
            {
                IAudioEndpointVolume v = Endpoint();
                bool muted;
                if (v == null || v.GetMute(out muted) != 0) return null;
                Guid none = Guid.Empty;
                v.SetMute(!muted, ref none);
                return !muted;
            }
            catch { return null; }
        }

        // ---------- brightness (WMI, built-in panels only) ----------

        /// <summary>Brightness in percent, or -1 when the display does not support it (desktop monitors).</summary>
        public static int Brightness
        {
            get
            {
                try
                {
                    using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                        foreach (ManagementObject o in s.Get())
                            return Convert.ToInt32(o["CurrentBrightness"]);
                }
                catch { }
                return -1;
            }
        }

        public static int ChangeBrightness(int deltaPercent)
        {
            int now = Brightness;
            if (now < 0) return -1;
            int target = Math.Max(5, Math.Min(100, now + deltaPercent));
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods"))
                    foreach (ManagementObject o in s.Get())
                    {
                        o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)target });
                        return target;
                    }
            }
            catch { }
            return -1;
        }

        // ---------- open application windows ----------

        public sealed class AppWindow
        {
            public IntPtr Handle;
            public string Title;
            public int ProcessId;
        }

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLong64(IntPtr hwnd, int index);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        private static long ExStyle(IntPtr hwnd)
        {
            return IntPtr.Size == 8 ? GetWindowLong64(hwnd, -20).ToInt64() : GetWindowLong32(hwnd, -20);
        }

        /// <summary>Visible top-level application windows, front to back, leaving out this process and shell surfaces.</summary>
        public static List<AppWindow> OpenWindows()
        {
            var result = new List<AppWindow>();
            int self = Process.GetCurrentProcess().Id;
            EnumWindows((hwnd, l) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                if (GetWindow(hwnd, 4) != IntPtr.Zero) return true;            // owned windows (dialogs, tool windows)
                if ((ExStyle(hwnd) & 0x80) != 0) return true;                   // WS_EX_TOOLWINDOW
                int cloaked;
                if (DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0) return true;  // hidden store-app shells
                var text = new StringBuilder(256);
                if (GetWindowText(hwnd, text, text.Capacity) == 0) return true;
                uint pid;
                WindowHelper.GetWindowThreadProcessId(hwnd, out pid);
                string title = text.ToString();
                if (title == "Program Manager") return true;
                if (pid == self && title == "Playnite Overlay") return true;
                result.Add(new AppWindow { Handle = hwnd, Title = title, ProcessId = (int)pid });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static void SwitchTo(IntPtr hwnd)
        {
            if (WindowHelper.IsIconic(hwnd)) WindowHelper.ShowWindow(hwnd, WindowHelper.SW_RESTORE);
            WindowHelper.ForceFocusWindow(hwnd);
        }
    }
}
