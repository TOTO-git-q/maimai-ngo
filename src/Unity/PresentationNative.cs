using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PartnerLive2D.Unity
{
    static class PresentationNative
    {
        // dwmapi.h uses pshpack1.h, including on x64. QPC_TIME and frame counts are unsigned 64 bit.
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct Ratio { public uint Numerator, Denominator; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct Timing
        {
            public uint Size;
            public Ratio Refresh;
            public ulong RefreshPeriod;
            public Ratio Compose;
            public ulong VBlank, RefreshCount;
            public uint DXRefresh;
            public ulong QpcCompose, Frame;
            public uint DXPresent;
            public ulong RefreshFrame, FrameSubmitted;
            public uint DXPresentSubmitted;
            public ulong FrameConfirmed;
            public uint DXPresentConfirmed;
            public ulong RefreshConfirmed;
            public uint DXRefreshConfirmed;
            public ulong FramesLate;
            public uint FramesOutstanding;
            public ulong FrameDisplayed, QpcFrameDisplayed, RefreshFrameDisplayed, FrameComplete, QpcFrameComplete,
                FramePending, QpcFramePending, FramesDisplayed, FramesComplete, FramesPending, FramesAvailable,
                FramesDropped, FramesMissed, RefreshNextDisplayed, RefreshNextPresented, RefreshesDisplayed,
                RefreshesPresented, RefreshStarted, PixelsReceived, PixelsDrawn, BuffersEmpty;
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref Timing timing);
        internal static int Snapshot(out Timing timing)
        {
            timing = new Timing { Size = (uint)Marshal.SizeOf(typeof(Timing)) };
            try { return DwmGetCompositionTimingInfo(IntPtr.Zero, ref timing); }
            catch (DllNotFoundException) { return unchecked((int)0x8007007E); }
            catch (EntryPointNotFoundException) { return unchecked((int)0x8007007F); }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MonitorInfo
        {
            public uint Size;
            public Rect Monitor, Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);

        internal static string WindowIdentity()
        {
            try
            {
                IntPtr hwnd;
                using (var p = System.Diagnostics.Process.GetCurrentProcess()) hwnd = p.MainWindowHandle;
                if (hwnd == IntPtr.Zero) return "hwnd=unavailable";
                var info = new MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(MonitorInfo)) };
                Rect client, window;
                if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info) || !GetClientRect(hwnd, out client) || !GetWindowRect(hwnd, out window))
                    return "hwnd=" + hwnd + ",geometry=unavailable";
                return string.Format("hwnd={0},monitor={1},primary={2},dpi={3},client={4}x{5},window={6},{7},{8},{9},monitorRect={10},{11},{12},{13}",
                    hwnd, info.Device, (info.Flags & 1) != 0, GetDpiForWindow(hwnd), client.Right, client.Bottom,
                    window.Left, window.Top, window.Right, window.Bottom, info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
            }
            catch (Exception e) { return "window=unavailable:" + e.GetType().Name; }
        }
    }
}
