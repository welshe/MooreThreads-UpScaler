using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MooreThreadsUpScaler.Core.Windowing
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width  => Right  - Left;
        public int Height => Bottom - Top;
    }

    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsBorderless { get; set; }

        public override string ToString() => $"{Title} ({ProcessName}) — {Width}×{Height}";
    }

    public sealed class WindowManager
    {
        #region Win32

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool   EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool   IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int    GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern bool   GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern uint   GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool   IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int    GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")]  private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int w, int h);
        [DllImport("gdi32.dll")]  private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
        [DllImport("gdi32.dll")]  private static extern bool   BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int xs, int ys, uint op);
        [DllImport("gdi32.dll")]  private static extern bool   DeleteObject(IntPtr hObj);
        [DllImport("gdi32.dll")]  private static extern bool   DeleteDC(IntPtr hDC);

        private const int  GWL_EXSTYLE      = -20;
        private const int  WS_EX_TOOLWINDOW = 0x00000080;
        private const int  WS_EX_APPWINDOW  = 0x00040000;
        private const uint SRCCOPY          = 0x00CC0020;

        #endregion

        private static readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase)
        {
            "MooreThreadsUpScaler", "explorer", "ShellExperienceHost", "SearchUI",
            "StartMenuExperienceHost", "TextInputHost", "ApplicationFrameHost",
            "SystemSettings", "LockApp", "RuntimeBroker"
        };

        public List<WindowInfo> GetAvailableWindows()
        {
            var list = new List<WindowInfo>();

            EnumWindows((hWnd, _) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd) || IsToolWindow(hWnd) || IsIconic(hWnd)) return true;

                    int len = GetWindowTextLength(hWnd);
                    if (len == 0) return true;

                    var sb = new StringBuilder(len + 1);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (string.IsNullOrWhiteSpace(title)) return true;

                    GetWindowThreadProcessId(hWnd, out uint pid);
                    var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                    if (_excluded.Contains(proc.ProcessName)) return true;

                    if (!GetWindowRect(hWnd, out RECT rect)) return true;
                    if (rect.Width < 100 || rect.Height < 100) return true;

                    list.Add(new WindowInfo
                    {
                        Handle       = hWnd,
                        Title        = title,
                        ProcessName  = proc.ProcessName,
                        Width        = rect.Width,
                        Height       = rect.Height,
                        IsBorderless = IsFullscreen(rect)
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WindowManager] Failed to enumerate window: {ex.Message}");
                    /* skip inaccessible windows */
                }
                return true;
            }, IntPtr.Zero);

            return list;
        }

        public BitmapSource? CaptureWindow(IntPtr hWnd)
        {
            try
            {
                if (!GetWindowRect(hWnd, out RECT rect)) return null;
                if (rect.Width <= 0 || rect.Height <= 0) return null;

                IntPtr hDesk = GetDesktopWindow();
                IntPtr hSrc  = GetWindowDC(hDesk);
                IntPtr hDst  = CreateCompatibleDC(hSrc);
                IntPtr hBmp  = CreateCompatibleBitmap(hSrc, rect.Width, rect.Height);
                IntPtr hOld  = SelectObject(hDst, hBmp);

                BitBlt(hDst, 0, 0, rect.Width, rect.Height, hSrc, rect.Left, rect.Top, SRCCOPY);
                SelectObject(hDst, hOld);

                var bmpSrc = Imaging.CreateBitmapSourceFromHBitmap(
                    hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                DeleteObject(hBmp);
                DeleteDC(hDst);
                ReleaseDC(hDesk, hSrc);

                bmpSrc.Freeze();
                return bmpSrc;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowManager] CaptureWindow failed: {ex.Message}");
                return null;
            }
        }

        public WindowInfo? GetForegroundWindowInfo()
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return null;
            try
            {
                int len = GetWindowTextLength(hWnd);
                if (len == 0) return null;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                GetWindowThreadProcessId(hWnd, out uint pid);
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                if (!GetWindowRect(hWnd, out RECT rect)) return null;

                return new WindowInfo
                {
                    Handle       = hWnd,
                    Title        = sb.ToString(),
                    ProcessName  = proc.ProcessName,
                    Width        = rect.Width,
                    Height       = rect.Height,
                    IsBorderless = IsFullscreen(rect)
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WindowManager] GetForegroundWindowInfo failed: {ex.Message}");
                return null;
            }
        }

        private static bool IsToolWindow(IntPtr hWnd)
        {
            int style = GetWindowLong(hWnd, GWL_EXSTYLE);
            return (style & WS_EX_TOOLWINDOW) != 0 && (style & WS_EX_APPWINDOW) == 0;
        }

        private static bool IsFullscreen(RECT rect)
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                if (screen.Bounds.Width == rect.Width && screen.Bounds.Height == rect.Height)
                    return true;
            return false;
        }
    }
}
