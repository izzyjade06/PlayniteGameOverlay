using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PlayniteGameOverlay.Access
{
    /// <summary>
    /// Operating mode (Silent / Performance / Turbo) on ASUS devices such as the ROG Ally, through the
    /// ATKACPI device that Armoury Crate and G-Helper also use. Absent on other hardware, where
    /// <see cref="Available"/> is false and nothing is offered.
    /// </summary>
    public static class AsusPower
    {
        private const uint ControlCode = 0x0022240C;
        private const uint DEVS = 0x53564544;
        private const uint DSTS = 0x53545344;
        private const uint PerformanceMode = 0x00120075;

        // firmware values, in the order the button cycles through them
        private static readonly int[] Values = { 2, 0, 1 };
        private static readonly string[] Names = { "Silent", "Performance", "Turbo" };

        private static int current = -1;   // index into Values, -1 when not known

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, uint inputSize, byte[] output, uint outputSize, ref uint returned, IntPtr overlapped);

        private static SafeFileHandle Open()
        {
            SafeFileHandle h = CreateFile(@"\\.\ATKACPI", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            return h;
        }

        public static bool Available
        {
            get
            {
                try { using (SafeFileHandle h = Open()) return !h.IsInvalid; }
                catch { return false; }
            }
        }

        private static byte[] Call(uint method, uint device, uint value)
        {
            using (SafeFileHandle h = Open())
            {
                if (h.IsInvalid) return null;
                byte[] input = new byte[16];
                BitConverter.GetBytes(method).CopyTo(input, 0);
                BitConverter.GetBytes((uint)8).CopyTo(input, 4);
                BitConverter.GetBytes(device).CopyTo(input, 8);
                BitConverter.GetBytes(value).CopyTo(input, 12);
                byte[] output = new byte[16];
                uint returned = 0;
                return DeviceIoControl(h, ControlCode, input, (uint)input.Length, output, (uint)output.Length, ref returned, IntPtr.Zero) ? output : null;
            }
        }

        /// <summary>The mode name if it is known, otherwise null.</summary>
        public static string CurrentName
        {
            get
            {
                if (current < 0)
                {
                    try
                    {
                        byte[] status = Call(DSTS, PerformanceMode, 0);
                        if (status != null)
                        {
                            int v = BitConverter.ToInt32(status, 0) - 65536;
                            int i = Array.IndexOf(Values, v);
                            if (i >= 0) current = i;
                        }
                    }
                    catch { }
                }
                return current < 0 ? null : Names[current];
            }
        }

        /// <summary>Switches to the next mode and returns its name, or null if the device refused.</summary>
        public static string Next()
        {
            int next = current < 0 ? 1 : (current + 1) % Values.Length;
            try
            {
                if (Call(DEVS, PerformanceMode, (uint)Values[next]) == null) return null;
            }
            catch { return null; }
            current = next;
            return Names[next];
        }
    }
}
