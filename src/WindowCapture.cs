using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace SendClipboard;

/// <summary>撮影対象として選べるトップレベルウインドウ。</summary>
internal sealed record CaptureTarget(IntPtr Handle, string Title);

/// <summary>ウインドウ単位のスクリーンショット。</summary>
internal static class WindowCapture
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_CLOAKED = 14;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>タスクバーに出るような、表示中でタイトルのあるウインドウを Z オーダー順に列挙する。</summary>
    public static List<CaptureTarget> EnumerateTargets()
    {
        var list = new List<CaptureTarget>();
        int ownPid = Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero)
            {
                return true;
            }

            if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0)
            {
                return true;
            }

            // 別の仮想デスクトップや UWP の非表示ウインドウは cloaked になっている
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid)
            {
                return true;
            }

            int length = GetWindowTextLength(hwnd);
            if (length == 0)
            {
                return true;
            }

            var sb = new StringBuilder(length + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            list.Add(new CaptureTarget(hwnd, sb.ToString()));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>ウインドウを撮影する。他のウインドウに隠れていても内容を取得できる。</summary>
    public static Bitmap Capture(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out RECT window))
        {
            throw new InvalidOperationException("ウインドウが見つかりません");
        }

        // Windows 10 以降は GetWindowRect に透明な枠が含まれるので、見た目どおりの範囲で切り抜く
        RECT visible = window;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) == 0)
        {
            visible = frame;
        }

        int width = window.Right - window.Left;
        int height = window.Bottom - window.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("ウインドウのサイズが不正です");
        }

        using var full = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(full))
        {
            IntPtr hdc = g.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                {
                    throw new InvalidOperationException("ウインドウを撮影できません");
                }
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        var crop = Rectangle.Intersect(
            new Rectangle(visible.Left - window.Left, visible.Top - window.Top, visible.Right - visible.Left, visible.Bottom - visible.Top),
            new Rectangle(0, 0, width, height));
        return full.Clone(crop, PixelFormat.Format24bppRgb);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
}
