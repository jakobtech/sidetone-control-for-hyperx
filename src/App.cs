using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Sidetone Control for HyperX")]
[assembly: AssemblyDescription("Unofficial sidetone (mic monitoring) volume control for the HyperX Cloud III S Wireless")]
[assembly: AssemblyCompany("Jakob Hüttner")]
[assembly: AssemblyProduct("Sidetone Control for HyperX")]
[assembly: AssemblyCopyright("© 2026 Jakob Hüttner")]
[assembly: AssemblyVersion("1.3.1.0")]
[assembly: AssemblyFileVersion("1.3.1.0")]
[assembly: AssemblyInformationalVersion("1.3.1")]

// Sidetone Control: the UI. Runs on the .NET Framework that ships with Windows and drives
// SidetoneHelper (embedded in this exe), which runs on NGENUITY's .NET runtime and talks to
// NGENUITY's background service. The sidetone level is applied in the headset itself.
static class Program
{
    public const string AppName = "Sidetone Control for HyperX";
    const string ShowEventName = "SidetoneControl.Show";

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        bool created;
        using (var mutex = new Mutex(true, "SidetoneControl.SingleInstance", out created))
        {
            if (!created)
            {
                // Already running: bring the existing window to the front instead.
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                return;
            }
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                Application.Run(new MainForm(Array.IndexOf(args, "--tray") >= 0 || Packaging.LaunchedByStartupTask(), showEvent));
        }
    }
}

// MSIX (Microsoft Store) support. Inside a package, HKCU\...\Run writes are virtualized, so autostart
// goes through the manifest's windows.startupTask instead. WinRT is reached by reflection so the exe
// still builds with the plain .NET Framework compiler (no Windows SDK).
static class Packaging
{
    public const string StartupTaskId = "SidetoneControlStartup";
    const int ActivationKindStartupTask = 1020;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder name);

    public static readonly bool IsPackaged = DetectPackage();

    static bool DetectPackage()
    {
        try
        {
            var len = 0;
            return GetCurrentPackageFullName(ref len, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
        }
        catch { return false; }
    }

    static Type WinRt(string name) { return Type.GetType(name + ", Windows, ContentType=WindowsRuntime", true); }

    static object Await(object asyncOperation, Type resultType)
    {
        var ext = Type.GetType("System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
        var asTask = ext.GetMethods().First(m => m.Name == "AsTask" && m.IsGenericMethod && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1");
        var task = (System.Threading.Tasks.Task)asTask.MakeGenericMethod(resultType).Invoke(null, new[] { asyncOperation });
        task.Wait();
        return task.GetType().GetProperty("Result").GetValue(task);
    }

    static object GetStartupTask()
    {
        var type = WinRt("Windows.ApplicationModel.StartupTask");
        return Await(type.GetMethod("GetAsync").Invoke(null, new object[] { StartupTaskId }), type);
    }

    // StartupTaskState: 0 Disabled, 1 DisabledByUser, 2 Enabled, 3 DisabledByPolicy, 4 EnabledByPolicy.
    static int State(object task) { return Convert.ToInt32(task.GetType().GetProperty("State").GetValue(task)); }

    public static bool StartupTaskEnabled
    {
        get { var s = State(GetStartupTask()); return s == 2 || s == 4; }
    }

    // Returns null on success, otherwise a message for the user.
    public static string SetStartupTask(bool enable)
    {
        var task = GetStartupTask();
        if (!enable)
        {
            task.GetType().GetMethod("Disable").Invoke(task, null);
            return null;
        }
        var stateType = WinRt("Windows.ApplicationModel.StartupTaskState");
        var state = Convert.ToInt32(Await(task.GetType().GetMethod("RequestEnableAsync").Invoke(task, null), stateType));
        if (state == 2 || state == 4) return null;
        if (state == 1) return "Startup was turned off for Sidetone Control for HyperX in Windows. Turn it back on under Settings > Apps > Startup.";
        return "Startup is blocked by a Windows policy on this PC.";
    }

    public static bool LaunchedByStartupTask()
    {
        if (!IsPackaged) return false;
        try
        {
            var args = WinRt("Windows.ApplicationModel.AppInstance").GetMethod("GetActivatedEventArgs").Invoke(null, null);
            return args != null && Convert.ToInt32(args.GetType().GetProperty("Kind").GetValue(args)) == ActivationKindStartupTask;
        }
        catch { return false; }
    }
}

static class Settings
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SidetoneControl");
    static readonly string LevelFile = Path.Combine(Dir, "level.txt");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "SidetoneControl";

    public static int? LoadLevel()
    {
        try
        {
            int v;
            if (File.Exists(LevelFile) && int.TryParse(File.ReadAllText(LevelFile).Trim(), out v))
                return Math.Max(MainForm.MinDb, Math.Min(MainForm.MaxDb, v));
        }
        catch { }
        return null;
    }

    public static void SaveLevel(int v)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(LevelFile, v.ToString());
        }
        catch { }
    }

    static string RunCommand { get { return "\"" + Application.ExecutablePath + "\" --tray"; } }

    public static bool Autostart
    {
        get
        {
            if (Packaging.IsPackaged) return Packaging.StartupTaskEnabled;
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(RunValue) != null;
        }
    }

    // Returns null on success, otherwise a message for the user.
    public static string SetAutostart(bool enable)
    {
        if (Packaging.IsPackaged) return Packaging.SetStartupTask(enable);
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enable) key.SetValue(RunValue, RunCommand);
            else key.DeleteValue(RunValue, false);
        }
        return null;
    }

    // Keeps the autostart entry pointing at this exe if the user moved it.
    public static void RefreshAutostartPath()
    {
        if (Packaging.IsPackaged) return;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (key != null && key.GetValue(RunValue) != null && (string)key.GetValue(RunValue) != RunCommand)
                    key.SetValue(RunValue, RunCommand);
        }
        catch { }
    }
}

// A Windows job object that kills every process put into it when Sidetone Control exits, even after a crash,
// so the helper and a headset service we started never outlive the app.
static class ChildJob
{
    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; // IO_COUNTERS
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    const int ExtendedLimitInformation = 9;
    const uint KillOnJobClose = 0x2000;
    static readonly IntPtr Job = Create();

    static IntPtr Create()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var info = new ExtendedLimits();
        info.Basic.LimitFlags = KillOnJobClose;
        SetInformationJobObject(job, ExtendedLimitInformation, ref info, Marshal.SizeOf(typeof(ExtendedLimits)));
        return job; // intentionally never closed: Windows closes it when this process ends
    }

    public static void Add(Process p)
    {
        try { AssignProcessToJobObject(Job, p.Handle); } catch { }
    }
}

// Lets Sidetone Control work while the NGENUITY window is closed: if neither NGENUITY nor its headset service
// is running, start that service ourselves, hidden, exactly as NGENUITY does ("hxn-srv-communication-hub.exe server").
// When NGENUITY is opened later it ends this copy and starts its own, and the helper simply reconnects.
static class HubLauncher
{
    const string HubName = "hxn-srv-communication-hub";
    static Process started;
    static DateTime lastStart = DateTime.MinValue;

    public static bool StartedByUs { get { return started != null && !started.HasExited; } }

    // Returns true if a service is running or was just started (the caller should retry shortly).
    public static bool EnsureRunning()
    {
        if (Process.GetProcessesByName(HubName).Length > 0) return true;
        if (Process.GetProcessesByName("NGENUITY").Length > 0) return true; // NGENUITY is about to start its own
        var exe = Path.Combine(HelperProcess.NgenuityDir, "Assets", "Services", HubName + ".exe");
        if (!File.Exists(exe)) return false;
        if (DateTime.UtcNow - lastStart < TimeSpan.FromSeconds(15)) return true; // don't hammer a service that keeps failing
        lastStart = DateTime.UtcNow;
        started = Process.Start(new ProcessStartInfo(exe, "server")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)
        });
        ChildJob.Add(started);
        return true;
    }
}

// Starts SidetoneHelper on NGENUITY's .NET runtime and exchanges one line per command with it.
class HelperProcess : IDisposable
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SidetoneControl");
    public static readonly string NgenuityDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NGENUITY", "current");
    Process process;

    public string Send(string command)
    {
        if (process == null || process.HasExited) Start();
        process.StandardInput.WriteLine(command);
        process.StandardInput.Flush();
        var reply = process.StandardOutput.ReadLine();
        if (reply == null) throw new IOException("Helper stopped.");
        return reply;
    }

    void Start()
    {
        Dispose();
        var dotnet = FindDotnet();
        if (dotnet == null) throw new FileNotFoundException(".NET runtime not found. Install or update HyperX NGENUITY.");
        // Always run the embedded copy from our data folder. dotnet.exe cannot read files inside the MSIX install
        // folder (WindowsApps: "Access is denied"), and a SidetoneHelper.exe planted next to the exe is never run.
        var helperExe = ExtractHelper();
        var helperDir = Path.GetDirectoryName(helperExe);
        var psi = new ProcessStartInfo(dotnet, "exec --runtimeconfig \"" + Path.Combine(helperDir, "SidetoneHelper.runtimeconfig.json") + "\" \"" + helperExe + "\" \"" + NgenuityDir + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = helperDir
        };
        process = Process.Start(psi);
        ChildJob.Add(process);
        process.ErrorDataReceived += (s, e) => { };
        process.BeginErrorReadLine();
    }

    static string ExtractHelper()
    {
        Directory.CreateDirectory(Dir);
        var exe = Path.Combine(Dir, "SidetoneHelper.exe");
        byte[] bytes;
        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("SidetoneHelper.exe"))
        using (var ms = new MemoryStream())
        {
            res.CopyTo(ms);
            bytes = ms.ToArray();
        }
        if (!File.Exists(exe) || !BytesEqual(File.ReadAllBytes(exe), bytes)) File.WriteAllBytes(exe, bytes);
        // NGENUITY.dll targets a recent .NET; LatestMajor picks the newest installed runtime.
        File.WriteAllText(Path.Combine(Dir, "SidetoneHelper.runtimeconfig.json"),
            "{\"runtimeOptions\":{\"tfm\":\"net8.0\",\"rollForward\":\"LatestMajor\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"8.0.0\"}}}");
        return exe;
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static string FindDotnet()
    {
        try
        {
            using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64"))
            {
                var loc = key == null ? null : key.GetValue("InstallLocation") as string;
                if (loc != null && File.Exists(Path.Combine(loc, "dotnet.exe"))) return Path.Combine(loc, "dotnet.exe");
            }
        }
        catch { }
        var fallback = Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    public void Dispose()
    {
        if (process == null) return;
        try { process.StandardInput.Close(); } catch { }
        try { if (!process.WaitForExit(1000)) process.Kill(); } catch { }
        process.Dispose();
        process = null;
    }
}

// Dark look modelled on NGENUITY: near-black backdrop, rounded grey cards, bold uppercase headings.
static class Theme
{
    // System DPI scale (the process is system-DPI-aware); DeviceDpi reads 96 until a handle exists.
    public static readonly float Scale = ReadScale();

    static float ReadScale()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
    }

    public static readonly Color Back = Color.FromArgb(13, 13, 14);
    public static readonly Color Glow = Color.FromArgb(46, 46, 49);
    public static readonly Color Card = Color.FromArgb(31, 31, 33);
    public static readonly Color CardBorder = Color.FromArgb(52, 52, 55);
    public static readonly Color Text = Color.FromArgb(242, 242, 242);
    public static readonly Color SubText = Color.FromArgb(150, 150, 154);
    public static readonly Color Dim = Color.FromArgb(95, 95, 99);
    public static readonly Color Track = Color.FromArgb(78, 78, 82);
    public static readonly Color Good = Color.FromArgb(96, 200, 72);
    public static readonly Color Warn = Color.FromArgb(226, 35, 45);

    static readonly string HeadingFamily = Has("Bahnschrift SemiBold") ? "Bahnschrift SemiBold" : "Segoe UI Semibold";
    static readonly string BodyFamily = Has("Bahnschrift") ? "Bahnschrift" : "Segoe UI";

    public static Font Heading(float size) { return new Font(HeadingFamily, size, FontStyle.Regular); }
    public static Font Body(float size) { return new Font(BodyFamily, size, FontStyle.Regular); }

    static bool Has(string family)
    {
        using (var f = new Font(family, 10f)) return f.Name == family;
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // Draws text with extra letter spacing, like the NGENUITY wordmark.
    public static float SpacedText(Graphics g, string text, Font font, Color color, float x, float y, float spacing)
    {
        using (var b = new SolidBrush(color))
            foreach (var ch in text)
            {
                var s = ch.ToString();
                g.DrawString(s, font, b, x, y, StringFormat.GenericTypographic);
                x += (ch == ' ' ? g.MeasureString("n", font, PointF.Empty, StringFormat.GenericTypographic).Width : g.MeasureString(s, font, PointF.Empty, StringFormat.GenericTypographic).Width) + spacing;
            }
        return x - spacing;
    }
}

abstract class ThemedControl : Control
{
    protected ThemedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected float UiScale { get { return Theme.Scale; } }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected static Color Fade(Color c, bool enabled) { return enabled ? c : Color.FromArgb(90, c); }
}

// Slider with a white ring thumb on a thin track, one dot per step.
class DarkSlider : ThemedControl
{
    int minimum = -6, maximum = 6, value;
    bool dragging;

    public event EventHandler ValueChanged;

    public DarkSlider() { AccessibleRole = AccessibleRole.Slider; }

    public int Minimum { get { return minimum; } set { minimum = value; Invalidate(); } }
    public int Maximum { get { return maximum; } set { maximum = value; Invalidate(); } }

    public int Value
    {
        get { return value; }
        set
        {
            var v = Math.Max(minimum, Math.Min(maximum, value));
            if (v == this.value) return;
            this.value = v;
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    float Pad { get { return 12 * UiScale; } }
    float XFor(int v) { return Pad + (Width - 2 * Pad) * (v - minimum) / (float)(maximum - minimum); }
    int ValueAt(int x) { return minimum + (int)Math.Round((x - Pad) / (Width - 2 * Pad) * (maximum - minimum)); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var cy = Height / 2f;
        var k = UiScale;
        using (var track = new SolidBrush(Fade(Theme.Track, Enabled)))
        {
            g.FillPath(track, Theme.Rounded(new RectangleF(Pad, cy - 1.5f * k, Width - 2 * Pad, 3 * k), 1.5f * k));
            for (int v = minimum; v <= maximum; v++)
            {
                var r = (v == 0 ? 2.6f : 1.8f) * k;
                g.FillEllipse(track, XFor(v) - r, cy + 7 * k - r, 2 * r, 2 * r);
            }
        }
        using (var fill = new SolidBrush(Fade(Color.FromArgb(205, 205, 208), Enabled)))
        {
            var zero = XFor(Math.Max(minimum, Math.Min(maximum, 0)));
            var x = XFor(value);
            g.FillRectangle(fill, Math.Min(zero, x), cy - 1.5f * k, Math.Abs(x - zero), 3 * k);
        }
        var tx = XFor(value);
        var outer = 10.5f * k;
        var inner = 6f * k;
        using (var ring = new SolidBrush(Fade(Color.White, Enabled)))
            g.FillEllipse(ring, tx - outer, cy - outer, 2 * outer, 2 * outer);
        using (var hole = new SolidBrush(Color.FromArgb(18, 18, 19)))
            g.FillEllipse(hole, tx - inner, cy - inner, 2 * inner, 2 * inner);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f * k))
                g.DrawEllipse(pen, tx - outer - 3 * k, cy - outer - 3 * k, 2 * outer + 6 * k, 2 * outer + 6 * k);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        dragging = true;
        Value = ValueAt(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) Value = ValueAt(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End: return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left: case Keys.Down: Value--; break;
            case Keys.Right: case Keys.Up: Value++; break;
            case Keys.Home: Value = minimum; break;
            case Keys.End: Value = maximum; break;
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessible(this); }

    class SliderAccessible : ControlAccessibleObject
    {
        readonly DarkSlider owner;
        public SliderAccessible(DarkSlider owner) : base(owner) { this.owner = owner; }
        public override string Value { get { return owner.value + " dB"; } set { } }
    }
}

// Pill switch: dark track with a white knob when on, grey knob on a grey track when off.
class ToggleSwitch : ThemedControl
{
    bool isOn;

    public event EventHandler CheckedChanged;

    public ToggleSwitch() { AccessibleRole = AccessibleRole.CheckButton; }

    public bool Checked
    {
        get { return isOn; }
        set
        {
            if (value == isOn) return;
            isOn = value;
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var k = UiScale;
        var r = new RectangleF(1.5f * k, 1.5f * k, Width - 3 * k, Height - 3 * k);
        using (var path = Theme.Rounded(r, r.Height / 2))
        {
            using (var fill = new SolidBrush(Fade(isOn ? Color.FromArgb(24, 24, 25) : Color.FromArgb(62, 62, 66), Enabled)))
                g.FillPath(fill, path);
            if (isOn || (Focused && ShowFocusCues))
                using (var pen = new Pen(Fade(Color.FromArgb(isOn ? 170 : 120, 210, 210, 214), Enabled), 1.5f * k))
                    g.DrawPath(pen, path);
        }
        var d = r.Height - 8 * k;
        var x = isOn ? r.Right - 4 * k - d : r.X + 4 * k;
        using (var knob = new SolidBrush(Fade(isOn ? Color.White : Color.FromArgb(185, 185, 189), Enabled)))
            g.FillEllipse(knob, x, r.Y + 4 * k, d, d);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Checked = !Checked; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Checked = !Checked;
    }

    protected override AccessibleObject CreateAccessibilityInstance() { return new ToggleAccessible(this); }

    class ToggleAccessible : ControlAccessibleObject
    {
        readonly ToggleSwitch owner;
        public ToggleAccessible(ToggleSwitch owner) : base(owner) { this.owner = owner; }
        public override AccessibleStates State { get { return base.State | (owner.isOn ? AccessibleStates.Checked : AccessibleStates.None); } }
    }
}

// Check box with a white filled square and a black tick, label to the right.
class DarkCheckBox : ToggleSwitch
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        var k = UiScale;
        var box = new RectangleF(1 * k, (Height - 18 * k) / 2, 18 * k, 18 * k);
        using (var path = Theme.Rounded(box, 2.5f * k))
        {
            if (Checked)
            {
                using (var fill = new SolidBrush(Fade(Color.White, Enabled))) g.FillPath(fill, path);
                using (var tick = new Pen(Color.FromArgb(18, 18, 19), 2.2f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(tick, new[] { new PointF(box.X + 4.5f * k, box.Y + 9.5f * k), new PointF(box.X + 7.8f * k, box.Y + 12.8f * k), new PointF(box.X + 13.5f * k, box.Y + 5.5f * k) });
            }
            else
                using (var pen = new Pen(Fade(Color.FromArgb(150, 150, 154), Enabled), 1.5f * k)) g.DrawPath(pen, path);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f * k)) g.DrawRectangle(pen, box.X - 3 * k, box.Y - 3 * k, box.Width + 6 * k, box.Height + 6 * k);
        }
        using (var b = new SolidBrush(Fade(ForeColor, Enabled)))
            g.DrawString(Text, Font, b, new RectangleF(box.Right + 10 * k, 0, Width - box.Right - 10 * k, Height), new StringFormat { LineAlignment = StringAlignment.Center });
    }
}

class MainForm : Form
{
    public const int MinDb = -6, MaxDb = 6;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    readonly DarkSlider slider = new DarkSlider();
    readonly ToggleSwitch monitoringSwitch = new ToggleSwitch();
    readonly DarkCheckBox autostartBox = new DarkCheckBox();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly ToolTip tips = new ToolTip();
    readonly AutoResetEvent wake = new AutoResetEvent(false);
    readonly object sync = new object();
    readonly bool startInTray;

    readonly Font wordmarkFont = Theme.Heading(15f);
    readonly Font headingFont = Theme.Heading(12f);
    readonly Font valueFont = Theme.Heading(12f);
    readonly Font bodyFont = Theme.Body(10f);
    readonly Font smallFont = Theme.Body(8.5f);

    // Layout in 96-dpi units; scaled when painting and placing controls.
    static readonly Rectangle MonitorCard = new Rectangle(24, 104, 572, 156);
    static readonly Rectangle OptionsCard = new Rectangle(24, 274, 572, 64);

    string status = "Connecting to NGENUITY…";
    bool headsetOnline;
    int? battery;
    int? desiredDb; // null until the user picks a level or we adopt the headset's current one
    bool? pendingMonitoring;
    bool levelDirty, updatingUi, exiting, trayHintShown;

    public MainForm(bool startInTray, EventWaitHandle showEvent)
    {
        this.startInTray = startInTray;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Text = Program.AppName;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = bodyFont;
        ClientSize = S(new Size(620, 384));

        slider.Minimum = MinDb;
        slider.Maximum = MaxDb;
        slider.AccessibleName = "Sidetone volume";
        slider.Bounds = S(new Rectangle(MonitorCard.X + 12, MonitorCard.Y + 92, MonitorCard.Width - 24, 34));
        slider.Enabled = false;

        monitoringSwitch.AccessibleName = "Mic monitoring";
        monitoringSwitch.Bounds = S(new Rectangle(MonitorCard.Right - 20 - 50, MonitorCard.Y + 16, 50, 28));
        monitoringSwitch.Enabled = false;
        tips.SetToolTip(monitoringSwitch, "Hear your own voice in the headset (sidetone).");

        autostartBox.Text = "Start with Windows";
        autostartBox.Font = bodyFont;
        autostartBox.AccessibleName = "Start with Windows";
        autostartBox.Bounds = S(new Rectangle(OptionsCard.X + 20, OptionsCard.Y + 18, 220, 28));
        try { autostartBox.Checked = Settings.Autostart; } catch { }
        tips.SetToolTip(autostartBox, "Starts in the tray when you log in and keeps your sidetone level.");

        Controls.AddRange(new Control[] { slider, monitoringSwitch, autostartBox });

        desiredDb = Settings.LoadLevel();
        if (desiredDb.HasValue) { updatingUi = true; slider.Value = desiredDb.Value; updatingUi = false; }

        slider.ValueChanged += (s, e) =>
        {
            Invalidate(S(MonitorCard));
            if (updatingUi) return;
            lock (sync) { desiredDb = slider.Value; levelDirty = true; }
            Settings.SaveLevel(slider.Value);
            wake.Set();
        };
        monitoringSwitch.CheckedChanged += (s, e) =>
        {
            Invalidate(S(MonitorCard));
            if (updatingUi) return;
            lock (sync) pendingMonitoring = monitoringSwitch.Checked;
            wake.Set();
        };
        autostartBox.CheckedChanged += (s, e) =>
        {
            if (updatingUi) return;
            string problem;
            try { problem = Settings.SetAutostart(autostartBox.Checked); }
            catch (Exception ex) { problem = "Could not change autostart: " + ex.GetBaseException().Message; }
            if (problem == null) return;
            MessageBox.Show(this, problem, Program.AppName);
            updatingUi = true;
            try { autostartBox.Checked = Settings.Autostart; } catch { autostartBox.Checked = false; }
            updatingUi = false;
        };
        Settings.RefreshAutostartPath();

        // Closing the window keeps the app in the tray so it can re-apply the level after the headset reconnects.
        tray.Icon = Icon;
        tray.Text = Program.AppName;
        tray.Visible = true;
        tray.DoubleClick += (s, e) => RestoreFromTray();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (s, e) => RestoreFromTray());
        menu.Items.Add("Exit", null, (s, e) => { exiting = true; Close(); });
        tray.ContextMenuStrip = menu;
        FormClosing += (s, e) =>
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!trayHintShown)
                {
                    tray.ShowBalloonTip(3000, Program.AppName, "Still running in the tray and keeping your sidetone level. Right-click the icon to exit.", ToolTipIcon.Info);
                    trayHintShown = true;
                }
                return;
            }
            tray.Visible = false;
        };

        // A second launch signals this event to bring the window back.
        ThreadPool.RegisterWaitForSingleObject(showEvent, (s, timedOut) => Ui(RestoreFromTray), null, -1, false);
        new Thread(WorkerLoop) { IsBackground = true, Name = "Sidetone worker" }.Start();
    }

    float K { get { return Theme.Scale; } }
    int S(int v) { return (int)Math.Round(v * K); }
    Size S(Size s) { return new Size(S(s.Width), S(s.Height)); }
    Rectangle S(Rectangle r) { return new Rectangle(S(r.X), S(r.Y), S(r.Width), S(r.Height)); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Dark title bar (Windows 10 20H1+ uses 20, older builds 19).
        var on = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4);
    }

    protected override void SetVisibleCore(bool value)
    {
        // Autostart launches straight into the tray without flashing the window.
        if (startInTray && !IsHandleCreated)
        {
            CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var k = K;

        // Backdrop: black with a soft grey glow, like NGENUITY's blurred background.
        g.Clear(Theme.Back);
        using (var glowPath = new GraphicsPath())
        {
            glowPath.AddEllipse(-ClientSize.Width * 0.2f, -ClientSize.Height * 0.5f, ClientSize.Width * 1.4f, ClientSize.Height * 1.3f);
            using (var glow = new PathGradientBrush(glowPath) { CenterColor = Theme.Glow, SurroundColors = new[] { Theme.Back } })
                g.FillPath(glow, glowPath);
        }

        // Header: wordmark, device line, battery pill.
        var wordEnd = Theme.SpacedText(g, "SIDETONE CONTROL", wordmarkFont, Theme.Text, 24 * k, 24 * k, 3.2f * k);
        Theme.SpacedText(g, "FOR HYPERX", smallFont, Theme.SubText, wordEnd + 10 * k, 33 * k, 1.6f * k);
        using (var dot = new SolidBrush(headsetOnline ? Theme.Good : Theme.Dim))
            g.FillEllipse(dot, 25 * k, 70 * k, 8 * k, 8 * k);
        using (var b = new SolidBrush(Theme.SubText))
            g.DrawString("Cloud III S Wireless", bodyFont, b, 40 * k, 64 * k);
        DrawBattery(g, k);

        // Mic monitoring card.
        DrawCard(g, MonitorCard, k);
        using (var b = new SolidBrush(Theme.Text))
        {
            g.DrawString("MIC MONITORING", headingFont, b, (MonitorCard.X + 20) * k, (MonitorCard.Y + 19) * k);
            g.DrawString("SIDETONE VOLUME", headingFont, b, (MonitorCard.X + 20) * k, (MonitorCard.Y + 64) * k);
            var v = slider.Value;
            var text = (v > 0 ? "+" : v < 0 ? "−" : "") + Math.Abs(v) + " dB";
            var size = g.MeasureString(text, valueFont);
            g.DrawString(text, valueFont, b, (MonitorCard.Right - 20) * k - size.Width, (MonitorCard.Y + 64) * k);
        }
        using (var b = new SolidBrush(Theme.SubText))
        {
            g.DrawString("QUIETER", smallFont, b, (MonitorCard.X + 22) * k, (MonitorCard.Y + 128) * k);
            var size = g.MeasureString("LOUDER", smallFont);
            g.DrawString("LOUDER", smallFont, b, (MonitorCard.Right - 22) * k - size.Width, (MonitorCard.Y + 128) * k);
        }

        // Options card.
        DrawCard(g, OptionsCard, k);
        using (var b = new SolidBrush(Theme.SubText))
            g.DrawString("Runs in the tray and re-applies your level when the headset reconnects.", smallFont, b,
                new RectangleF((OptionsCard.X + 250) * k, OptionsCard.Y * k, (OptionsCard.Width - 270) * k, OptionsCard.Height * k),
                new StringFormat { LineAlignment = StringAlignment.Center });

        // Footer: status left, disclaimer right.
        using (var b = new SolidBrush(Theme.SubText))
            g.DrawString(status, smallFont, b, 24 * k, 352 * k);
        using (var b = new SolidBrush(Theme.Dim))
        {
            const string note = "Unofficial · requires HyperX NGENUITY installed";
            var size = g.MeasureString(note, smallFont);
            g.DrawString(note, smallFont, b, ClientSize.Width - 24 * k - size.Width, 352 * k);
        }
    }

    void DrawCard(Graphics g, Rectangle r, float k)
    {
        using (var path = Theme.Rounded(new RectangleF(r.X * k, r.Y * k, r.Width * k, r.Height * k), 6 * k))
        {
            using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
            using (var pen = new Pen(Theme.CardBorder, 1f)) g.DrawPath(pen, path);
        }
    }

    void DrawBattery(Graphics g, float k)
    {
        var pill = new RectangleF(ClientSize.Width - 24 * k - 112 * k, 20 * k, 112 * k, 36 * k);
        using (var path = Theme.Rounded(pill, 18 * k))
        {
            using (var fill = new SolidBrush(Color.FromArgb(20, 20, 21))) g.FillPath(fill, path);
            using (var pen = new Pen(Theme.CardBorder, 1.2f * k)) g.DrawPath(pen, path);
        }
        var body = new RectangleF(pill.X + 16 * k, pill.Y + 11 * k, 30 * k, 14 * k);
        using (var pen = new Pen(Theme.Text, 1.4f * k)) g.DrawPath(pen, Theme.Rounded(body, 2 * k));
        using (var tip = new SolidBrush(Theme.Text)) g.FillRectangle(tip, body.Right + 1.5f * k, body.Y + 4 * k, 2.5f * k, 6 * k);
        if (battery.HasValue && headsetOnline)
        {
            var pct = Math.Max(0, Math.Min(100, battery.Value));
            using (var fill = new SolidBrush(pct <= 15 ? Theme.Warn : Theme.Good))
                g.FillRectangle(fill, body.X + 2.5f * k, body.Y + 2.5f * k, (body.Width - 5 * k) * pct / 100f, body.Height - 5 * k);
        }
        using (var b = new SolidBrush(Theme.Text))
            g.DrawString(battery.HasValue && headsetOnline ? battery.Value + "%" : "—", headingFont, b, body.Right + 10 * k, pill.Y + 8 * k);
    }

    void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void WorkerLoop()
    {
        if (!Directory.Exists(HelperProcess.NgenuityDir))
        {
            Ui(() => SetStatus("HyperX NGENUITY is not installed.", false));
            return;
        }
        using (var helper = new HelperProcess())
        {
            FormClosed += (s, e) => helper.Dispose();
            while (true)
            {
                try { Sync(helper); }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    Ui(() => { SetStatus("Error: " + msg, false); SetControlsEnabled(false); });
                    helper.Dispose();
                }
                wake.WaitOne(3000);
            }
        }
    }

    void Sync(HelperProcess helper)
    {
        int? wanted;
        bool dirty;
        bool? monitoringRequest;
        lock (sync)
        {
            wanted = desiredDb;
            dirty = levelDirty;
            levelDirty = false;
            monitoringRequest = pendingMonitoring;
            pendingMonitoring = null;
        }

        if (monitoringRequest.HasValue) helper.Send("monitoring " + (monitoringRequest.Value ? "1" : "0"));
        if (dirty && wanted.HasValue) helper.Send("level " + wanted.Value);

        var state = Parse(helper.Send("status"));
        if (state.Error != null) throw new Exception(state.Error);
        if (!state.Hub)
        {
            var starting = HubLauncher.EnsureRunning();
            Ui(() => { SetStatus(starting ? "Connecting to the headset service…" : "HyperX NGENUITY's headset service was not found.", false); SetControlsEnabled(false); });
            return;
        }
        if (!state.Headset) { Ui(() => { SetStatus("Headset is off or not connected.", false); SetControlsEnabled(false); }); return; }

        var level = state.Level;
        if (!wanted.HasValue && level.HasValue)
        {
            // First run: take over whatever the headset currently uses.
            wanted = Math.Max(MinDb, Math.Min(MaxDb, level.Value));
            lock (sync) { if (!desiredDb.HasValue) desiredDb = wanted; }
            Settings.SaveLevel(wanted.Value);
        }
        var reapplied = false;
        if (wanted.HasValue && level.HasValue && level.Value != wanted.Value)
        {
            // The headset forgets the level when it powers off; put it back.
            level = Parse(helper.Send("level " + wanted.Value)).Level;
            reapplied = true;
        }

        var monitoring = state.Monitoring;
        var batteryNow = state.Battery;
        Ui(() =>
        {
            SetControlsEnabled(true);
            updatingUi = true;
            if (wanted.HasValue && slider.Value != wanted.Value) slider.Value = wanted.Value;
            if (monitoring.HasValue) monitoringSwitch.Checked = monitoring.Value;
            updatingUi = false;
            if (batteryNow.HasValue && battery != batteryNow) { battery = batteryNow; Invalidate(); }
            if (wanted.HasValue && level.HasValue && level.Value != wanted.Value)
                SetStatus("The headset did not accept the level (reports " + level.Value + " dB).", true);
            else if (monitoring == false)
                SetStatus("Connected · mic monitoring is off", true);
            else
                SetStatus(reapplied ? "Connected · sidetone level applied" : "Connected", true);
        });
    }

    class HelperState
    {
        public bool Hub, Headset;
        public int? Level, Battery;
        public bool? Monitoring;
        public string Error;
    }

    static HelperState Parse(string reply)
    {
        var st = new HelperState();
        if (reply.StartsWith("ERR")) { st.Error = reply.Length > 4 ? reply.Substring(4) : "Unknown error"; return st; }
        foreach (var part in reply.Split(' '))
        {
            var kv = part.Split('=');
            if (kv.Length != 2) continue;
            int n;
            var known = int.TryParse(kv[1], out n);
            switch (kv[0])
            {
                case "hub": st.Hub = kv[1] == "1"; break;
                case "headset": st.Headset = kv[1] == "1"; break;
                case "level": if (known) st.Level = n; break;
                case "monitoring": if (known) st.Monitoring = n == 1; break;
                case "battery": if (known) st.Battery = n; break;
            }
        }
        return st;
    }

    void SetControlsEnabled(bool on)
    {
        slider.Enabled = on;
        monitoringSwitch.Enabled = on;
    }

    void SetStatus(string text, bool online)
    {
        if (status == text && headsetOnline == online) return;
        status = text;
        headsetOnline = online;
        var tip = Program.AppName + ": " + text;
        tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        Invalidate();
    }

    void Ui(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { }
    }
}
