using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using InputSimulatorStandard;
using InputSimulatorStandard.Native;

/// <summary>
/// E-IMZO Java (SunAwtDialog) dialog oynasini fon da ushlab,
/// ekrandan tashqariga surib, parolni kiritib, OK bosadi.
/// </summary>
public class EImzoWindowHider
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc fn, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    const uint SWP_NOSIZE       = 0x0001;
    const uint SWP_NOZORDER     = 0x0004;
    const uint SWP_FRAMECHANGED = 0x0020;  // stil o'zgarishini darhol qo'llaydi
    const int  GWL_EXSTYLE      = -20;
    const int  WS_EX_TOOLWINDOW = 0x00000080;  // taskbar va Alt+Tab dan yashiradi
    const int  WS_EX_APPWINDOW  = 0x00040000;  // taskbarda ko'rsatadi

    /// <summary>
    /// E-IMZO parol dialogini qidiradi (SunAwtDialog, 200-600 x 150-400 px).
    /// </summary>
    static IntPtr FindEImzoDialog()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, 256);
            if (sb.ToString() != "SunAwtDialog") return true;

            GetWindowRect(hWnd, out RECT r);
            int w = r.Right - r.Left, h = r.Bottom - r.Top;
            if (w > 200 && w < 600 && h > 150 && h < 400)
            {
                found = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// Dialogni topib ekrandan tashqariga suradi, parolni kiritadi, Enter bosadi.
    /// Topilsa true, topilmasa false qaytaradi.
    /// </summary>
    public static bool HandleEImzoDialog(string pinCode)
    {
        var hwnd = FindEImzoDialog();
        if (hwnd == IntPtr.Zero) return false;

        // Taskbar, Alt+Tab va desktop dan to'liq yashirish
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW);

        // Ekrandan tashqariga surish + stil o'zgarishini qo'llash
        SetWindowPos(hwnd, IntPtr.Zero, -5000, -5000, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        Thread.Sleep(300);

        SetForegroundWindow(hwnd);
        Thread.Sleep(600);

        var sim = new InputSimulator();

        // Avvalgi matnni o'chirish
        sim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
        Thread.Sleep(150);
        sim.Keyboard.KeyPress(VirtualKeyCode.DELETE);
        Thread.Sleep(300);

        // Parolni kiritish
        foreach (char c in pinCode)
        {
            sim.Keyboard.TextEntry(c.ToString());
            Thread.Sleep(50);
        }
        Thread.Sleep(400);

        // OK (Enter)
        sim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
        Thread.Sleep(300);

        Console.WriteLine($"[EImzoWindowHider] ✅ Dialog fonda yopildi.");
        return true;
    }

    /// <summary>
    /// Dialog paydo bo'lishini kutib, paydo bo'lgach HandleEImzoDialog ni chaqiradi.
    /// timeoutMs (default 30s) dan keyin false qaytaradi.
    /// </summary>
    public static bool WaitAndHandle(string pinCode, int timeoutMs = 30_000)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
        Console.Write("[EImzoWindowHider] Dialog kutilmoqda");
        while (DateTime.Now < deadline)
        {
            if (HandleEImzoDialog(pinCode)) return true;
            Console.Write(".");
            Thread.Sleep(100);
        }
        Console.WriteLine("\n[EImzoWindowHider] ✗ Timeout — dialog topilmadi.");
        return false;
    }

    /// <summary>
    /// Fon threadida kuzatib turadi va dialog chiqishi bilan darhol yopadi.
    /// Login tugmasini bosishdan oldin chaqiring.
    /// </summary>
    public static void StartMonitoring(string pinCode, int intervalMs = 80)
    {
        Thread t = new Thread(() =>
        {
            while (true)
            {
                try { if (HandleEImzoDialog(pinCode)) return; }
                catch { /* ignore */ }
                Thread.Sleep(intervalMs);
            }
        });
        t.IsBackground = true;
        t.Start();
        Console.WriteLine("[EImzoWindowHider] Monitoring thread ishga tushdi.");
    }
}
