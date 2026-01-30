using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using InputSimulatorStandard;
using InputSimulatorStandard.Native;

class Program
{
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const string CERTIFICATE_PASSWORD = "***REMOVED-CERTIFICATE-PASSWORD***";

    static void Main()
    {
        ChromeOptions options = new ChromeOptions();
        options.AddExcludedArgument("enable-automation");
        options.AddArgument("--disable-blink-features=AutomationControlled");
        
        IWebDriver driver = new ChromeDriver(options);

        try
        {
            Console.WriteLine("=== Soliq.uz avtomatik login ===\n");
            
            driver.Navigate().GoToUrl("https://my3.soliq.uz/login?type=legal");
            Thread.Sleep(3000);

            Console.WriteLine("1. Yuridik shaxs...");
            driver.FindElement(By.XPath("//span[text()='Юридик шахс']")).Click();
            Thread.Sleep(1500);

            Console.WriteLine("2. Kompaniya nomi...");
            var search = driver.FindElement(By.XPath("//input[@placeholder='Ф.И.Ш. ёки корхона номини киритинг...']"));
            search.SendKeys("kargo-logistik-trade mchj");
            Thread.Sleep(1500);

            Console.WriteLine("3. Kompaniya tanlash...");
            driver.FindElement(By.XPath("//div[contains(@class,'KeyCard_key-card')]")).Click();
            Thread.Sleep(1500);

            Console.WriteLine("4. Kirish tugmasi...");
            driver.FindElement(By.XPath("//button/span[text()='Кириш']")).Click();

            Console.WriteLine("\n5. E-IMZO parol dialogini kutmoqda...\n");
            
            bool success = WaitAndEnterPassword(CERTIFICATE_PASSWORD);

            if (success)
            {
                Console.WriteLine("\n✅ MUVAFFAQIYATLI! Tizimga kirildi!");
                Thread.Sleep(5000);
            }
            else
            {
                Console.WriteLine("\n❌ XATOLIK yuz berdi!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ XATOLIK: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
        finally
        {
            Console.WriteLine("\nYopish uchun Enter bosing...");
            Console.ReadLine();
            driver.Quit();
        }
    }

    static bool WaitAndEnterPassword(string password)
    {
        try
        {
            IntPtr hwnd = IntPtr.Zero;
            string foundTitle = "";
            string foundClass = "";
            
            Console.Write("E-IMZO parol dialogini qidiryapman");
            
            // 30 soniya kutish
            for (int attempt = 0; attempt < 300; attempt++)
            {
                EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
                {
                    if (IsWindowVisible(hWnd))
                    {
                        StringBuilder className = new StringBuilder(256);
                        GetClassName(hWnd, className, 256);
                        string classStr = className.ToString();

                        // Java Swing dialog - E-IMZO parol oynasi
                        if (classStr == "SunAwtDialog")
                        {
                            // O'lchamni tekshirish (kichik dialog bo'lishi kerak)
                            RECT rect;
                            GetWindowRect(hWnd, out rect);
                            int width = rect.Right - rect.Left;
                            int height = rect.Bottom - rect.Top;

                            // Parol dialogi odatda 400x250 atrofida
                            if (width > 200 && width < 600 && height > 150 && height < 400)
                            {
                                hwnd = hWnd;
                                foundClass = classStr;
                                
                                StringBuilder title = new StringBuilder(512);
                                GetWindowText(hWnd, title, 512);
                                foundTitle = title.ToString();
                                
                                return false; // Topildi, to'xtatish
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                if (hwnd != IntPtr.Zero)
                {
                    Console.WriteLine($"\n✓ E-IMZO dialog topildi!");
                    Console.WriteLine($"   Class: {foundClass}");
                    break;
                }

                if (attempt % 10 == 0)
                {
                    Console.Write(".");
                }

                Thread.Sleep(100);
            }

            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine("\n✗ E-IMZO parol dialogi topilmadi!");
                return false;
            }

            // Dialogni aktivlashtirish
            Console.WriteLine("\n✓ Dialogni aktivlashtirmoqda...");
            SetForegroundWindow(hwnd);
            Thread.Sleep(1000);

            var sim = new InputSimulator();

            // Eski matnni o'chirish
            Console.WriteLine("✓ Maydonni tozalash...");
            sim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
            Thread.Sleep(150);
            sim.Keyboard.KeyPress(VirtualKeyCode.DELETE);
            Thread.Sleep(300);

            // Parolni kiritish
            Console.WriteLine($"✓ Parol kiritilmoqda: {new string('*', password.Length)}");
            
            foreach (char c in password)
            {
                sim.Keyboard.TextEntry(c.ToString());
                Thread.Sleep(50); // Har bir belgidan keyin ozgina kutish
            }
            
            Thread.Sleep(500);

            // OK tugmasini bosish (Enter)
            Console.WriteLine("✓ OK tugmasini bosish...");
            sim.Keyboard.KeyPress(VirtualKeyCode.RETURN);
            Thread.Sleep(500);

            Console.WriteLine("✓ Parol muvaffaqiyatli kiritildi!");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Xatolik: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
            return false;
        }
    }
}