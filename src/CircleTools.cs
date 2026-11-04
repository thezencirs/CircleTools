// Copyright (c) 2026 thezencirs (https://github.com/thezencirs)
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// CircleTools by thezencirs - Win + Ctrl + T : aktif pencereyi "her zaman üstte" yapar / geri alır.
// Sabitlenen pencerenin etrafında mavi bir çerçeve görünür.
static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x8;
    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

    // Pencerenin gerçekten görünen sınırları (gölge/görünmez kenarlar hariç)
    public static bool VisibleBounds(IntPtr h, out Rectangle r)
    {
        RECT rc;
        if (DwmGetWindowAttribute(h, 9, out rc, Marshal.SizeOf(typeof(RECT))) != 0 && !GetWindowRect(h, out rc)) { r = Rectangle.Empty; return false; }
        r = Rectangle.FromLTRB(rc.L, rc.T, rc.R, rc.B);
        return r.Width > 0 && r.Height > 0;
    }
}

// Sabitlenen pencerenin etrafındaki mavi çerçeve: tıklamaları geçiren, etkinleşmeyen şeffaf bir pencere.
class BorderForm : Form
{
    const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    public static readonly Color Blue = Color.FromArgb(0, 120, 255);
    public const int Thickness = 4;

    public BorderForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Size = new Size(10, 10);
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int t = Thickness, w = Width, h = Height;
        using (Brush b = new SolidBrush(Blue))
        {
            e.Graphics.FillRectangle(b, 0, 0, w, t);
            e.Graphics.FillRectangle(b, 0, h - t, w, t);
            e.Graphics.FillRectangle(b, 0, 0, t, h);
            e.Graphics.FillRectangle(b, w - t, 0, t, h);
        }
        // ince beyaz iç çizgi: koyu arka planlarda da net görünsün
        using (Pen p = new Pen(Color.FromArgb(235, 245, 255), 1))
            e.Graphics.DrawRectangle(p, t, t, w - 2 * t - 1, h - 2 * t - 1);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
}

class AlwaysOnTop : Form
{
    const uint MOD_CONTROL = 0x0002, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "CircleTools";

    NotifyIcon tray;
    ToolStripMenuItem startupItem, countItem, updateItem, autoItem;
    Updater.Release pending;
    readonly Dictionary<IntPtr, BorderForm> pinned = new Dictionary<IntPtr, BorderForm>();
    System.Windows.Forms.Timer follow;

    AlwaysOnTop()
    {
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;

        var menu = new ContextMenuStrip();
        var info = new ToolStripMenuItem("Win + Ctrl + T = Pencereyi sabitle / bırak") { Enabled = false };
        countItem = new ToolStripMenuItem("Sabitlenen pencere: 0") { Enabled = false };
        var unpinAll = new ToolStripMenuItem("Tüm sabitlemeleri kaldır");
        unpinAll.Click += (s, e) => UnpinAll();
        startupItem = new ToolStripMenuItem("Windows ile başlat") { Checked = IsStartup() };
        startupItem.Click += (s, e) => { SetStartup(!IsStartup()); startupItem.Checked = IsStartup(); };
        var exit = new ToolStripMenuItem("Çıkış");
        exit.Click += (s, e) => { UnpinAll(); tray.Visible = false; Application.Exit(); };

        updateItem = new ToolStripMenuItem("Güncelleme var") { Visible = false };
        updateItem.Click += (s, e) => { if (pending != null) PromptUpdate(pending); };
        var checkItem = new ToolStripMenuItem("Güncellemeleri denetle...");
        checkItem.Click += (s, e) => CheckUpdates(true);
        autoItem = new ToolStripMenuItem("Otomatik güncelleme denetimi (günde bir, GitHub'a bağlanır)") { Checked = AutoUpdateEnabled() };
        autoItem.Click += (s, e) => { SetAutoUpdate(!AutoUpdateEnabled()); autoItem.Checked = AutoUpdateEnabled(); };
        var aboutItem = new ToolStripMenuItem("Hakkında — CircleTools v" + Updater.CurrentText + " (by thezencirs)");
        aboutItem.Click += (s, e) =>
        {
            if (MessageBox.Show(
                "CircleTools v" + Updater.CurrentText + "\n" +
                "Geliştirici & İmza: thezencirs (https://github.com/thezencirs)\n" +
                "Lisans: MIT License — Copyright (c) 2026 thezencirs\n\n" +
                "Aktif pencereyi Win + Ctrl + T kısayolu ile her zaman üstte sabitler ve etrafında mavi çerçeve gösterir.\n\n" +
                "GitHub proje sayfasını açmak ister misiniz?",
                "CircleTools — by thezencirs", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try { Process.Start("https://github.com/" + Updater.Repo); } catch { }
            }
        };
        menu.Items.AddRange(new ToolStripItem[] { info, countItem, unpinAll, new ToolStripSeparator(), startupItem,
            new ToolStripSeparator(), updateItem, checkItem, autoItem, aboutItem, new ToolStripSeparator(), exit });

        tray = new NotifyIcon { Icon = MakeIcon(), Text = "CircleTools (Win+Ctrl+T)", ContextMenuStrip = menu, Visible = true };
        tray.BalloonTipClicked += (s, e) => { if (pending != null) PromptUpdate(pending); };

        // Form hiç gösterilmediği için tanıtıcı kendiliğinden oluşmaz; kısayolun kaydı için zorla oluştur.
        CreateHandle();

        // Çerçeveleri sabitlenen pencerelerle birlikte hareket ettir
        follow = new System.Windows.Forms.Timer { Interval = 40 };
        follow.Tick += (s, e) => Follow();
        follow.Start();

        // Pencere olmayan bir tepsi programı olduğu için, açıldığını kullanıcıya açıkça göster.
        var t = new System.Windows.Forms.Timer { Interval = 700 };
        t.Tick += (s, e) => { t.Stop(); t.Dispose(); Welcome(); };
        t.Start();

        // Günde en fazla bir kez, açılıştan 20 sn sonra sessizce yeni sürüm ara
        var ut = new System.Windows.Forms.Timer { Interval = 20000 };
        ut.Tick += (s, e) =>
        {
            ut.Stop(); ut.Dispose();
            if (Updater.Configured && AutoUpdateEnabled() && (DateTime.UtcNow - LastCheck()).TotalHours >= 24) CheckUpdates(false);
        };
        ut.Start();
    }

    // ---- Güncelleme ----
    static DateTime LastCheck()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\CircleTools"))
        {
            long ticks;
            return long.TryParse(k.GetValue("LastCheck") as string, out ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue;
        }
    }

    static bool AutoUpdateEnabled()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\CircleTools")) return !"0".Equals(k.GetValue("AutoUpdate") as string);
    }

    static void SetAutoUpdate(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\CircleTools")) k.SetValue("AutoUpdate", on ? "1" : "0");
    }

    void CheckUpdates(bool manual)
    {
        if (!Updater.Configured) { if (manual) MessageBox.Show("Güncelleme deposu ayarlanmamış.", "CircleTools"); return; }
        ThreadPool.QueueUserWorkItem(delegate
        {
            string err;
            Updater.Release r = Updater.Fetch(out err);
            try { BeginInvoke(new Action(delegate { OnUpdateResult(r, err, manual); })); } catch { }
        });
    }

    void OnUpdateResult(Updater.Release r, string err, bool manual)
    {
        if (err != null) { if (manual) MessageBox.Show("Güncelleme denetlenemedi:\n" + err, "CircleTools", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\CircleTools")) k.SetValue("LastCheck", DateTime.UtcNow.Ticks.ToString());
        if (Updater.IsNewer(r))
        {
            pending = r;
            updateItem.Text = "Güncelle: v" + r.Version + " (şu an v" + Updater.CurrentText + ")";
            updateItem.Visible = true;
            if (manual) PromptUpdate(r);
            else tray.ShowBalloonTip(10000, "CircleTools yeni sürüm: v" + r.Version, "Güncellemek için buraya tıklayın.", ToolTipIcon.Info);
        }
        else if (manual) MessageBox.Show("CircleTools güncel (v" + Updater.CurrentText + ").", "CircleTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    void PromptUpdate(Updater.Release r)
    {
        string notes = string.IsNullOrEmpty(r.Notes) ? "" : "\n\nYenilikler:\n" + (r.Notes.Length > 700 ? r.Notes.Substring(0, 700) + "..." : r.Notes);
        if (MessageBox.Show("Yeni sürüm var: v" + r.Version + " (sizde v" + Updater.CurrentText + ").\n\nİndirilip doğrulanarak (SHA256) güncellensin mi? Program yeniden başlar; sabitlenen pencereler serbest bırakılır." + notes,
            "CircleTools güncelleme", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Cursor.Current = Cursors.WaitCursor;
        string err;
        bool ok = Updater.Apply(r, out err);
        Cursor.Current = Cursors.Default;
        if (!ok) { MessageBox.Show("Güncellenemedi:\n" + err, "CircleTools", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        UnpinAll(); tray.Visible = false; Application.Exit();
    }

    void Welcome()
    {
        bool first;
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\CircleTools"))
        {
            first = k.GetValue("FirstRun") == null;
            if (first) k.SetValue("FirstRun", 1);
        }

        if (!first)
        {
            tray.ShowBalloonTip(3000, "CircleTools çalışıyor", "Bir pencereyi seçip Win + Ctrl + T ile üstte sabitleyin. Sabitlenen pencerenin etrafında mavi çerçeve görünür.", ToolTipIcon.Info);
            return;
        }

        bool auto = MessageBox.Show(
            "CircleTools (by thezencirs) çalışıyor ve arka planda (sağ alttaki tepsi simgesinde) bekliyor.\n\n" +
            "Nasıl kullanılır:\n" +
            "  1. Üstte tutmak istediğiniz pencereye tıklayın (klasör, tarayıcı vb.)\n" +
            "  2. Win + Ctrl + T tuşlarına basın: pencerenin etrafında MAVİ ÇERÇEVE belirir\n" +
            "  3. Bırakmak için aynı tuşlara tekrar basın: çerçeve kaybolur\n\n" +
            "Simgeyi göremezseniz görev çubuğunun sağındaki ^ okuna bakın.\n\n" +
            "Bilgisayar her açıldığında otomatik başlasın mı?",
            "CircleTools — by thezencirs", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes;
        if (auto) { SetStartup(true); startupItem.Checked = true; }
    }

    static Icon MakeIcon()
    {
        using (var bmp = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.FillEllipse(Brushes.OrangeRed, 3, 3, 26, 26);
            g.FillEllipse(Brushes.White, 11, 11, 10, 10);
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!Native.RegisterHotKey(Handle, 1, MOD_WIN | MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.T))
        {
            MessageBox.Show(
                "Win + Ctrl + T kısayolu başka bir program tarafından kullanılıyor.\n" +
                "Kısayolu kullanan diğer pencere sabitleme aracını kapatın, sonra CircleTools'u yeniden başlatın.",
                "CircleTools — by thezencirs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            tray.Visible = false;
            Environment.Exit(1);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == 1) Toggle();
        base.WndProc(ref m);
    }

    void Toggle()
    {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return;

        // Masaüstü ve görev çubuğunu sabitleme
        var cls = new StringBuilder(256);
        Native.GetClassName(h, cls, 256);
        string c = cls.ToString();
        if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return;

        bool isTop = (Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
        bool ok = Native.SetWindowPos(h, isTop ? Native.HWND_NOTOPMOST : Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

        var title = new StringBuilder(256);
        Native.GetWindowText(h, title, 256);
        if (!ok)
        {
            tray.ShowBalloonTip(2500, "Sabitlenemedi", "Bu pencere yönetici yetkisiyle çalışıyor olabilir; CircleTools'u da yönetici olarak çalıştırmanız gerekir.", ToolTipIcon.Warning);
            return;
        }

        if (isTop) RemovePin(h); else AddPin(h);
        tray.ShowBalloonTip(1200, isTop ? "Sabitleme kaldırıldı" : "Üstte sabitlendi",
            title.Length > 0 ? title.ToString() : c, ToolTipIcon.None);
    }

    void AddPin(IntPtr h)
    {
        if (pinned.ContainsKey(h)) return;
        BorderForm b = new BorderForm();
        pinned[h] = b;
        b.Show();
        PositionBorder(h, b, true);
        UpdateCount();
    }

    void RemovePin(IntPtr h)
    {
        BorderForm b;
        if (pinned.TryGetValue(h, out b)) { pinned.Remove(h); b.Close(); b.Dispose(); }
        UpdateCount();
    }

    void UnpinAll()
    {
        foreach (IntPtr h in new List<IntPtr>(pinned.Keys))
        {
            if (Native.IsWindow(h))
                Native.SetWindowPos(h, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            RemovePin(h);
        }
    }

    void UpdateCount()
    {
        countItem.Text = "Sabitlenen pencere: " + pinned.Count;
        tray.Text = pinned.Count > 0 ? "CircleTools - " + pinned.Count + " pencere sabit" : "CircleTools (Win+Ctrl+T)";
    }

    // Çerçeveyi pencerenin üzerine yerleştirir; pencere kapanmış/küçültülmüşse gizler.
    void PositionBorder(IntPtr h, BorderForm b, bool force)
    {
        Rectangle r = Rectangle.Empty;
        bool visible = Native.IsWindow(h) && Native.IsWindowVisible(h) && !Native.IsIconic(h) && Native.VisibleBounds(h, out r);
        if (!visible)
        {
            if (Native.IsWindowVisible(b.Handle)) Native.SetWindowPos(b.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_HIDEWINDOW);
            return;
        }
        bool fg = Native.GetForegroundWindow() == h;
        if (force || !Native.IsWindowVisible(b.Handle) || b.Bounds != r || fg)
            Native.SetWindowPos(b.Handle, Native.HWND_TOPMOST, r.X, r.Y, r.Width, r.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    void Follow()
    {
        if (pinned.Count == 0) return;
        foreach (IntPtr h in new List<IntPtr>(pinned.Keys))
        {
            // Pencere kapandıysa veya başka bir yolla "üstte" olmaktan çıktıysa çerçeveyi kaldır
            if (!Native.IsWindow(h) || (Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0)
            {
                RemovePin(h);
                continue;
            }
            PositionBorder(h, pinned[h], false);
        }
    }

    static bool IsStartup()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(AppName) != null;
    }

    static void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue(AppName, false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (follow != null) follow.Dispose();
            foreach (var b in pinned.Values) b.Dispose();
            if (IsHandleCreated) Native.UnregisterHotKey(Handle, 1);
            if (tray != null) tray.Dispose();
        }
        base.Dispose(disposing);
    }

    [STAThread]
    static void Main(string[] args)
    {
        // Yüksek DPI'da çerçevenin pencereyle hizalı olması için
        try { if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) Native.SetProcessDPIAware(); }
        catch { try { Native.SetProcessDPIAware(); } catch { } }

        Updater.CleanupOld();
        bool afterUpdate = Array.IndexOf(args, "--after-update") >= 0;

        bool created = false;
        Mutex mutex = null;
        for (int i = 0; i < (afterUpdate ? 20 : 1); i++)
        {
            mutex = new Mutex(true, "AlwaysOnTopHotkey_SingleInstance", out created);
            if (created) break;
            mutex.Dispose();
            Thread.Sleep(500); // güncelleme sonrası: eski örneğin kapanmasını bekle
        }
        if (!created)
        {
            MessageBox.Show("CircleTools zaten çalışıyor.\n\nSağ alttaki tepsi simgesine (veya ^ okunun içine) bakın.\nPencere sabitlemek için: pencereye tıklayıp Win + Ctrl + T.",
                "CircleTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using (mutex)
        {
            Application.EnableVisualStyles();
            Application.Run(new AlwaysOnTop());
        }
    }
}
