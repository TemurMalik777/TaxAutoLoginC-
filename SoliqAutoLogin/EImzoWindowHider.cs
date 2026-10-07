using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using InputSimulatorStandard;
using InputSimulatorStandard.Native;

/// <summary>
/// E-IMZO Java (SunAwtDialog) parol dialogini foydalanuvchiga ko'rsatmasdan ushlab,
/// parolni kiritib, OK bosadi.
///
/// Dialog ekranda paydo bo'lgandan keyin qidirilmaydi — SetWinEventHook orqali oyna
/// YARATILGAN paytdayoq (EVENT_OBJECT_CREATE, hali chizilmagan) ushlanadi va darhol
/// to'liq shaffof qilinib ekrandan tashqariga suriladi. Shu sabab dialog rabochiy
/// stolda bir lahza ham ko'rinmaydi.
///
/// XAVFSIZ KIRITISH: klaviatura hodisalari (SendInput) har doim FOKUSDAGI oynaga
/// boradi. Foydalanuvchi shu paytda Telegram/Word/Excel da yozayotgan bo'lsa,
/// Windows fon jarayonga fokusni olishga ruxsat bermaydi va parol (hamda Ctrl+A,
/// Delete, Enter) foydalanuvchining oynasiga yozilib ketardi. Shuning uchun:
///   - har bir tugmadan OLDIN fokus aynan E-IMZO dialogida ekani tekshiriladi;
///   - fokus boshqa oynada bo'lsa — hech narsa yuborilmaydi;
///   - yozish davomida (≈1 s) foydalanuvchi kiritishi vaqtincha to'xtatiladi
///     (BlockInput), shunda uning tugmalari parolga aralashmaydi va fokusni
///     o'g'irlay olmaydi;
///   - fokusni umuman olib bo'lmasa — dialog foydalanuvchiga ko'rsatiladi va parol
///     yozilmaydi (qo'lda kiritish mumkin).
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
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
    [DllImport("user32.dll", SetLastError = true)] static extern bool BlockInput(bool fBlockIt);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int ptX, ptY; }

    [StructLayout(LayoutKind.Sequential)]
    struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    const uint SWP_NOSIZE         = 0x0001;
    const uint SWP_NOZORDER       = 0x0004;
    const uint SWP_NOACTIVATE     = 0x0010;
    const uint SWP_FRAMECHANGED   = 0x0020;  // stil o'zgarishini darhol qo'llaydi
    const int  GWL_EXSTYLE        = -20;
    const int  WS_EX_TOOLWINDOW   = 0x00000080;  // taskbar va Alt+Tab dan yashiradi
    const int  WS_EX_APPWINDOW    = 0x00040000;  // taskbarda ko'rsatadi
    const int  WS_EX_LAYERED      = 0x00080000;
    const uint LWA_ALPHA          = 0x00000002;

    const uint EVENT_OBJECT_CREATE = 0x8000;
    const uint EVENT_OBJECT_SHOW   = 0x8002;
    const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
    const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    const int  OBJID_WINDOW = 0;
    const uint PM_REMOVE = 0x0001;

    const string DIALOG_CLASS = "SunAwtDialog";

    const int SM_CXSCREEN = 0;
    const int SM_CYSCREEN = 1;
    const uint FLASHW_ALL = 0x00000003;
    const uint FLASHW_TIMERNOFG = 0x0000000C;

    /// <summary>Fokusni olish uchun urinishlar soni (har biri ≈0.5 s).</summary>
    const int MAX_FOCUS_ATTEMPTS = 20;

    /// <summary>Foydalanuvchi ushlab turishi mumkin bo'lgan modifikator tugmalar.</summary>
    static readonly VirtualKeyCode[] MODIFIERS =
    {
        VirtualKeyCode.CONTROL, VirtualKeyCode.LCONTROL, VirtualKeyCode.RCONTROL,
        VirtualKeyCode.SHIFT, VirtualKeyCode.LSHIFT, VirtualKeyCode.RSHIFT,
        VirtualKeyCode.MENU, VirtualKeyCode.LMENU, VirtualKeyCode.RMENU,
        VirtualKeyCode.LWIN, VirtualKeyCode.RWIN,
    };

    static bool IsDialogClass(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, 256);
        return sb.ToString() == DIALOG_CLASS;
    }

    /// <summary>Parol dialogi o'lchami (200-600 x 150-400 px)</summary>
    static bool IsPasswordDialogSize(IntPtr hWnd)
    {
        GetWindowRect(hWnd, out RECT r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        return w > 200 && w < 600 && h > 150 && h < 400;
    }

    /// <summary>
    /// Oynani ko'rinmas qiladi: 100% shaffof (alpha=0), taskbar/Alt+Tab dan yashirin,
    /// ekrandan tashqarida. Oyna fokus va klaviatura kiritishini qabul qilishda davom etadi.
    /// </summary>
    static void Conceal(IntPtr hwnd)
    {
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        int wanted = (exStyle & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
        if (wanted != exStyle) SetWindowLong(hwnd, GWL_EXSTYLE, wanted);
        SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
        SetWindowPos(hwnd, IntPtr.Zero, -5000, -5000, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>Allaqachon ochiq turgan parol dialogini qidiradi (hook o'rnatilishidan oldin chiqqan bo'lsa)</summary>
    static IntPtr FindEImzoDialog()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd) || !IsDialogClass(hWnd)) return true;
            if (IsPasswordDialogSize(hWnd)) { found = hWnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Hozir fokusda turgan oyna shu dialogmi (yoki E-IMZO ning o'z oynasi)?</summary>
    static bool IsDialogFocused(IntPtr hwnd)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (fg == hwnd) return true;

        // Java dialog ichidagi oyna ham fokusni olishi mumkin — faqat shu E-IMZO
        // jarayoniga tegishli bo'lsa qabul qilamiz (foydalanuvchi dasturi EMAS).
        GetWindowThreadProcessId(fg, out uint fgPid);
        GetWindowThreadProcessId(hwnd, out uint dlgPid);
        return fgPid != 0 && fgPid == dlgPid;
    }

    /// <summary>
    /// Dialogni fokusga olib chiqadi. Windows fon jarayonga fokusni olishni
    /// cheklaydi, shuning uchun joriy fokus egasining input navbatiga vaqtincha
    /// ulanamiz (AttachThreadInput). ALT bosish kabi usullar ishlatilmaydi —
    /// ular foydalanuvchining oynasiga tugma yuborgan bo'lardi.
    /// </summary>
    static bool ForceForeground(IntPtr hwnd)
    {
        if (IsDialogFocused(hwnd)) return true;

        uint myThread = GetCurrentThreadId();
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint dlgThread = GetWindowThreadProcessId(hwnd, out _);

        bool attachedFg = fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, true);
        bool attachedDlg = dlgThread != 0 && dlgThread != myThread && AttachThreadInput(myThread, dlgThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
        }
        finally
        {
            if (attachedDlg) AttachThreadInput(myThread, dlgThread, false);
            if (attachedFg) AttachThreadInput(myThread, fgThread, false);
        }

        Thread.Sleep(150);
        if (IsDialogFocused(hwnd)) return true;

        // Zaxira usul (hujjatlashtirilmagan, lekin fokus cheklovidan o'tadi)
        SwitchToThisWindow(hwnd, true);
        Thread.Sleep(200);
        return IsDialogFocused(hwnd);
    }

    /// <summary>
    /// Tugmani FAQAT fokus dialogda bo'lsa yuboradi. Fokus boshqa oynaga o'tgan
    /// bo'lsa hech narsa yuborilmaydi va false qaytadi.
    /// </summary>
    static bool SendGuarded(IntPtr hwnd, Action send)
    {
        if (!IsWindow(hwnd) || !IsDialogFocused(hwnd)) return false;
        send();
        return true;
    }

    /// <summary>Foydalanuvchi ushlab turgan Ctrl/Shift/Alt/Win — parol belgilariga qo'shilmasin.</summary>
    static void ReleaseHeldModifiers(InputSimulator sim)
    {
        foreach (var key in MODIFIERS)
        {
            if ((GetAsyncKeyState((int)key) & 0x8000) != 0) sim.Keyboard.KeyUp(key);
        }
    }

    /// <summary>
    /// Bitta urinish: maydonni tozalash → parol → Enter. Har bir tugma oldidan fokus
    /// tekshiriladi; fokus yo'qolsa urinish to'xtaydi (qolgan belgilar hech qayerga
    /// yuborilmaydi) va keyingi urinish maydonni qaytadan tozalab boshlaydi.
    /// </summary>
    static bool TryTypePassword(IntPtr hwnd, string pinCode, InputSimulator sim)
    {
        ReleaseHeldModifiers(sim);

        if (!SendGuarded(hwnd, () => sim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A))) return false;
        Thread.Sleep(80);
        if (!SendGuarded(hwnd, () => sim.Keyboard.KeyPress(VirtualKeyCode.DELETE))) return false;
        Thread.Sleep(120);

        foreach (char c in pinCode)
        {
            string ch = c.ToString();
            if (!SendGuarded(hwnd, () => sim.Keyboard.TextEntry(ch))) return false;
            Thread.Sleep(30);
        }
        Thread.Sleep(200);

        return SendGuarded(hwnd, () => sim.Keyboard.KeyPress(VirtualKeyCode.RETURN));
    }

    /// <summary>
    /// Avtomatik kiritib bo'lmadi — dialogni foydalanuvchiga ko'rsatamiz (ekran
    /// markazida, to'liq ko'rinadigan, taskbarda miltillab), parolni qo'lda kiritish mumkin.
    /// </summary>
    static void Reveal(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return;

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

        GetWindowRect(hwnd, out RECT r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int x = Math.Max(0, (GetSystemMetrics(SM_CXSCREEN) - w) / 2);
        int y = Math.Max(0, (GetSystemMetrics(SM_CYSCREEN) - h) / 2);
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        var flash = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount = 0,
            dwTimeout = 0,
        };
        FlashWindowEx(ref flash);
    }

    /// <summary>
    /// Yashirilgan dialogga parolni XAVFSIZ kiritadi va Enter bosadi.
    /// Parol hech qachon boshqa oynaga yozilmaydi.
    /// </summary>
    static bool EnterPassword(IntPtr hwnd, string pinCode)
    {
        Conceal(hwnd);
        Thread.Sleep(300);

        var sim = new InputSimulator();

        for (int attempt = 1; attempt <= MAX_FOCUS_ATTEMPTS; attempt++)
        {
            if (!IsWindow(hwnd))
            {
                Console.WriteLine("[EImzoWindowHider] Dialog yopilgan — kiritish to'xtatildi.");
                return false;
            }

            if (!ForceForeground(hwnd))
            {
                if (attempt == 1)
                    Console.WriteLine("[EImzoWindowHider] Fokus boshqa oynada — dialog fokusga olinmaguncha hech narsa yozilmaydi...");
                Thread.Sleep(300);
                continue;
            }

            // Yozish davomida (≈1 s) foydalanuvchi klaviatura/sichqonchasi vaqtincha
            // to'xtatiladi: uning tugmalari parolga aralashmaydi va fokusni boshqa
            // oynaga o'tkaza olmaydi. Jarayon qulasa ham Windows kiritishni o'zi tiklaydi.
            bool blocked = BlockInput(true);
            try
            {
                if (TryTypePassword(hwnd, pinCode, sim))
                {
                    Thread.Sleep(300);
                    Console.WriteLine($"[EImzoWindowHider] ✅ Parol dialogga kiritildi (urinish {attempt}), dialog fonda yopildi.");
                    return true;
                }
            }
            finally
            {
                if (blocked) BlockInput(false);
            }

            Console.WriteLine($"[EImzoWindowHider] ⚠ Yozish paytida fokus yo'qoldi (urinish {attempt}) — qolgan belgilar yuborilmadi, qayta uriniladi");
            Thread.Sleep(300);
        }

        Console.WriteLine("[EImzoWindowHider] ✗ Dialogni fokusga olib bo'lmadi — parol YOZILMADI. Dialog ekranda ko'rsatildi, parolni qo'lda kiriting.");
        Reveal(hwnd);
        return false;
    }

    /// <summary>
    /// Dialog paydo bo'lishini kutadi: oyna yaratilishi bilan yashiradi, ko'rsatilganda
    /// parolni kiritadi. timeoutMs (default 30s) ichida dialog chiqmasa false qaytaradi.
    /// </summary>
    public static bool WaitAndHandle(string pinCode, int timeoutMs = 30_000)
    {
        IntPtr target = IntPtr.Zero;
        bool caughtAtCreate = false;

        // Delegate GC tomonidan yig'ib olinmasligi uchun lokal o'zgaruvchida ushlanadi
        WinEventDelegate callback = (hook, eventType, hwnd, idObject, idChild, thread, time) =>
        {
            if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;
            try
            {
                if (!IsDialogClass(hwnd)) return;
                // CREATE: hali chizilmagan — darhol yashiramiz (o'lcham hali yakuniy emas,
                // shuning uchun barcha SunAwtDialog lar yashiriladi)
                if (eventType == EVENT_OBJECT_CREATE) caughtAtCreate = true;
                Conceal(hwnd);
                if (eventType == EVENT_OBJECT_SHOW && target == IntPtr.Zero && IsPasswordDialogSize(hwnd))
                    target = hwnd;
            }
            catch { /* ignore */ }
        };

        IntPtr hookHandle = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW, IntPtr.Zero, callback,
            0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hookHandle == IntPtr.Zero)
            Console.WriteLine("[EImzoWindowHider] ⚠ SetWinEventHook o'rnatilmadi — polling rejimi");
        else
            Console.WriteLine("[EImzoWindowHider] Hook o'rnatildi, dialog kutilmoqda...");

        // Node shu qatorni kutadi va faqat undan keyin "Кириш" ni bosadi — aks holda exe
        // sekin ishga tushsa dialog hook o'rnatilishidan oldin chiqib, ekranda ko'rinib qoladi.
        Console.WriteLine("READY");
        Console.Out.Flush();

        try
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            long lastPoll = 0;
            while (DateTime.Now < deadline)
            {
                // Out-of-context hook callbacklari shu thread'ning message loop'i orqali keladi
                while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                if (target != IntPtr.Zero && IsWindow(target)) break;

                // Zaxira: hook o'tkazib yuborgan yoki oldin ochilgan dialog
                if (Environment.TickCount64 - lastPoll > 200)
                {
                    lastPoll = Environment.TickCount64;
                    var found = FindEImzoDialog();
                    if (found != IntPtr.Zero)
                    {
                        Conceal(found);
                        target = found;
                        Console.WriteLine("[EImzoWindowHider] ⚠ Dialog zaxira qidiruv orqali topildi — ekranda qisqa ko'rinib qolgan bo'lishi mumkin");
                        break;
                    }
                }

                Thread.Sleep(5);
            }

            if (target == IntPtr.Zero)
            {
                Console.WriteLine("[EImzoWindowHider] ✗ Timeout — dialog topilmadi.");
                return false;
            }

            if (caughtAtCreate)
                Console.WriteLine("[EImzoWindowHider] Dialog yaratilgan paytdayoq ushlandi (ko'rinmadi)");
            return EnterPassword(target, pinCode);
        }
        finally
        {
            if (hookHandle != IntPtr.Zero) UnhookWinEvent(hookHandle);
            GC.KeepAlive(callback);
        }
    }

    /// <summary>
    /// Fon threadida kuzatib turadi va dialog chiqishi bilan yashirib parolni kiritadi.
    /// Login tugmasini bosishdan oldin chaqiring.
    /// </summary>
    public static void StartMonitoring(string pinCode, int timeoutMs = 60_000)
    {
        Thread t = new Thread(() =>
        {
            try { WaitAndHandle(pinCode, timeoutMs); }
            catch { /* ignore */ }
        });
        t.IsBackground = true;
        t.Start();
        Console.WriteLine("[EImzoWindowHider] Monitoring thread ishga tushdi.");
    }
}
