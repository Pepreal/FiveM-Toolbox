// FiveM Toolbox - frame cap per FiveM build (driver level), DNS tools, FiveM cache cleaner. Copyright (c) 2026 Pepreal. MIT License (see the file LICENSE).
// FiveM Toolbox: frame cap for AMD and NVIDIA, DNS tools, FiveM cache, graphics. C# 5 on purpose: it compiles with the compiler built into Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("FiveM Toolbox")]
[assembly: AssemblyDescription("Frame cap, DNS tools and a cache cleaner for FiveM")]
[assembly: AssemblyCompany("Pepreal")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Pepreal. MIT License.")]
[assembly: AssemblyProduct("FiveM Toolbox")]
[assembly: AssemblyVersion("0.8.1.0")]
[assembly: AssemblyFileVersion("0.8.1.0")]

namespace FiveMFrameCap
{
    // Everything the program says goes through here: the small box in the window, the big log window, and log.txt.
    class LogEntry { public string Text; public string Time; public string Message; public string Tool; public bool Debug; public int Group; }

    // Everything the program says goes through here: the log at the bottom of the window, the pop-out log, and the log file.
    static class AppLog
    {
        static readonly object Gate = new object();
        static readonly List<LogEntry> Entries = new List<LogEntry>();
        static readonly Dictionary<int, bool> done = new Dictionary<int, bool>();
        static string path;
        static int nextGroup = 0;
        public static event Action<LogEntry> Added;
        public static event Action GroupEnded;

        // Which tool the current thread is working for. Lines written on that thread get this tag.
        [ThreadStatic] public static string Context;
        // The job (search, apply, clean ...) the current thread is in. The log folds a finished job into one line.
        [ThreadStatic] public static int Group;

        // The log file sits next to the exe (easy to find and send). If that folder is not writable it goes to %LOCALAPPDATA%\<app folder>.
        public static string FilePath
        {
            get
            {
                lock (Gate)
                {
                    if (path != null) return path;
                    string[] tries = new string[] {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Info.ExeName + "-log.txt"),
                        Path.Combine(Path.GetDirectoryName(Blb.BackupDir), "log.txt") };
                    foreach (string t in tries)
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(t));
                            FileInfo fi = new FileInfo(t);
                            if (fi.Exists && fi.Length > 2 * 1024 * 1024) File.Copy(t, t + ".old", true);
                            if (fi.Exists && fi.Length > 2 * 1024 * 1024) File.WriteAllText(t, "");
                            File.AppendAllText(t, "");
                            path = t; break;
                        }
                        catch (Exception) { }
                    }
                    if (path == null) path = tries[1];
                    return path;
                }
            }
        }

        public static void BeginGroup() { lock (Gate) { nextGroup++; Group = nextGroup; } }

        public static void EndGroup()
        {
            int g = Group;
            Group = 0;
            if (g == 0) return;
            lock (Gate) { done[g] = true; }
            Action h = GroupEnded;
            if (h != null) { try { h(); } catch (Exception) { } }
        }

        public static bool IsDone(int g) { lock (Gate) { return done.ContainsKey(g); } }

        static void Write(string s, bool debug)
        {
            LogEntry e = new LogEntry();
            e.Tool = string.IsNullOrEmpty(Context) ? "General" : Context;
            e.Debug = debug;
            e.Group = Group;
            e.Time = DateTime.Now.ToString("HH:mm:ss");
            e.Message = s;
            e.Text = e.Time + "  [" + e.Tool + "] " + (debug ? "[debug] " : "") + s;
            lock (Gate) { Entries.Add(e); }
            try { File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd ") + e.Text + "\r\n"); }
            catch (Exception) { }
            Action<LogEntry> h = Added;
            if (h != null) { try { h(e); } catch (Exception) { } }
        }
        public static void Add(string s) { Write(s, false); }
        public static void Debug(string s) { Write(s, true); }
        public static List<LogEntry> Snapshot()
        {
            lock (Gate) { return new List<LogEntry>(Entries); }
        }
    }

    // One block of facts about this computer at start-up. This is what makes a log from another PC useful.
    static class Sysinfo
    {
        static bool done = false;

        static string DllInfo(string name)
        {
            try
            {
                string p = Path.Combine(Environment.SystemDirectory, name);
                if (!File.Exists(p)) return name + ": not present";
                return name + ": " + FileVersionInfo.GetVersionInfo(p).FileVersion + " (" + new FileInfo(p).Length + " bytes)";
            }
            catch (Exception ex) { return name + ": error " + ex.Message; }
        }

        public static void Dump()
        {
            if (done) return;
            done = true;
            try
            {
                AppLog.Debug("==== " + Info.AppName + " " + Info.Version + " started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
                AppLog.Debug("exe: " + Assembly.GetExecutingAssembly().Location);
                AppLog.Debug("log file: " + AppLog.FilePath);
                AppLog.Debug("Windows: " + Environment.OSVersion + " | 64-bit OS: " + Environment.Is64BitOperatingSystem + " | 64-bit process: " + Environment.Is64BitProcess + " | .NET: " + Environment.Version + " | culture: " + System.Globalization.CultureInfo.CurrentCulture.Name);
                AppLog.Debug("elevated (admin): " + new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
                try
                {
                    using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Caption, BuildNumber, Version FROM Win32_OperatingSystem"))
                        foreach (ManagementObject mo in s.Get()) AppLog.Debug("OS: " + mo["Caption"] + " build " + mo["BuildNumber"] + " (" + mo["Version"] + ")");
                }
                catch (Exception ex) { AppLog.Debug("OS query failed: " + ex.Message); }
                try
                {
                    using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, DriverVersion, AdapterRAM, Status FROM Win32_VideoController"))
                        foreach (ManagementObject mo in s.Get())
                            AppLog.Debug("video controller: " + mo["Name"] + " | " + mo["PNPDeviceID"] + " | driver " + mo["DriverVersion"] + " | RAM " + mo["AdapterRAM"] + " | " + mo["Status"]);
                }
                catch (Exception ex) { AppLog.Debug("video controller query failed: " + ex.Message); }
                foreach (string dll in new string[] { "atiadlxx.dll", "amdadlx64.dll", "nvapi64.dll", "nvml.dll", "nvcuda.dll" }) AppLog.Debug("driver file " + DllInfo(dll));
                AppLog.Debug("Radeon Software exe: " + Radeon.Exe + " exists=" + File.Exists(Radeon.Exe));
                foreach (string n in new string[] { "RadeonSoftware", "cncmd", "AMDRSServ", "FiveM", "GTA5" })
                    foreach (Process p in Process.GetProcessesByName(n)) AppLog.Debug("process running: " + n + " pid " + p.Id);
                string blb = Blb.Path;
                if (File.Exists(blb))
                {
                    FileInfo fi = new FileInfo(blb);
                    string md5 = "";
                    try { using (System.Security.Cryptography.MD5 m = System.Security.Cryptography.MD5.Create()) md5 = BitConverter.ToString(m.ComputeHash(File.ReadAllBytes(blb))).Replace("-", ""); } catch (Exception) { }
                    AppLog.Debug("Radeon profile file: " + blb + " | " + fi.Length + " bytes | changed " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + " | md5 " + md5);
                    try { AppLog.Debug("Radeon profile file layout: " + Blb.Layout(File.ReadAllBytes(blb)).Count + " game profiles"); }
                    catch (Exception ex) { AppLog.Debug("Radeon profile file layout: " + ex.Message); }
                }
                else AppLog.Debug("Radeon profile file not found: " + blb);
                foreach (DriveInfo di in DriveInfo.GetDrives())
                {
                    try { AppLog.Debug("drive " + di.Name + " type " + di.DriveType + (di.IsReady ? " ready, " + di.DriveFormat : " not ready")); } catch (Exception) { }
                }
                AppLog.Debug("backup folder: " + Blb.BackupDir);
                try
                {
                    Font icf = IconButton.IconFont;
                    AppLog.Debug("icon font: " + (icf != null ? icf.FontFamily.Name : "NONE found (Segoe Fluent Icons and Segoe MDL2 Assets are both missing): the program draws the shield, monitor, sun and moon itself"));
                    List<string> segoe = new List<string>();
                    foreach (FontFamily ff in FontFamily.Families) if (ff.Name.StartsWith("Segoe", StringComparison.OrdinalIgnoreCase)) segoe.Add(ff.Name);
                    AppLog.Debug("Segoe fonts installed: " + string.Join(", ", segoe.ToArray()));
                    string iconHow;
                    Icon iconTry = Info.IconOfExe(Assembly.GetExecutingAssembly().Location, out iconHow);
                    AppLog.Debug("app icon from the exe: " + (iconTry != null ? "found" : "NOT found, the program draws its own") + " (" + iconHow + ") | exe: " + Assembly.GetExecutingAssembly().Location + (Assembly.GetExecutingAssembly().Location.StartsWith("\\\\") ? " (a network path)" : ""));
                    using (Graphics gd = Graphics.FromHwnd(IntPtr.Zero)) AppLog.Debug("screen scaling: " + gd.DpiX + " x " + gd.DpiY + " dpi (96 = 100 percent)");
                    foreach (Screen sc in Screen.AllScreens) AppLog.Debug("screen " + sc.DeviceName + ": " + sc.Bounds.Width + "x" + sc.Bounds.Height + ", working area " + sc.WorkingArea.Width + "x" + sc.WorkingArea.Height + (sc.Primary ? " (primary)" : ""));
                }
                catch (Exception ex) { AppLog.Debug("icon diagnostics failed: " + ex.Message); }
            }
            catch (Exception ex) { AppLog.Debug("system info failed: " + ex); }
        }
    }

    // ---------------------------------------------------------------- screen scaling
    // A program that does not say it knows about screen scaling gets its whole picture stretched by Windows
    // (125 percent = soft letters). So a normal start says it knows (SetProcessDPIAware). Then every size in the
    // code is a "logical" size (the size at 100 percent). The window is built in logical sizes, and when it loads
    // it is scaled once (Control.Scale). Code that draws or places things by hand, after that, goes through
    // Dpi.S (whole pixels) and Dpi.F (fractions). The test switches stay NOT aware, so the old tests see the old layout.
    static class Dpi
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);

        public static bool Aware = false;
        public static float Scale = 1f;      // 1.25 = 125 percent

        // returns the arguments without --dpi-aware
        public static string[] Init(string[] args)
        {
            List<string> rest = new List<string>();
            bool force = false, normal = true, never = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--dpi-aware") { force = true; continue; }
                if (args[i] == "--no-dpi") { never = true; continue; }
                if (args[i].StartsWith("--") && args[i] != "--page" && args[i] != "--relaunch" && args[i] != "--dpi-test") normal = false;
                rest.Add(args[i]);
            }
            if (!never && (normal || force))
            {
                try { Aware = SetProcessDPIAware(); } catch (Exception) { Aware = false; }
                if (Aware)
                {
                    try
                    {
                        IntPtr dc = GetDC(IntPtr.Zero);
                        int dpi = GetDeviceCaps(dc, 88);       // LOGPIXELSX
                        ReleaseDC(IntPtr.Zero, dc);
                        if (dpi >= 96 && dpi <= 480) Scale = dpi / 96f;
                    }
                    catch (Exception) { }
                }
            }
            return rest.ToArray();
        }

        // for the tests: pretend the screen scaling is this (only the layout; the letters keep the real size)
        public static void Force(float scale) { Aware = scale > 1.001f; Scale = scale; }

        // logical -> screen pixels
        public static int S(int v) { return Aware ? (int)Math.Round(v * Scale) : v; }
        public static float F(float v) { return Aware ? v * Scale : v; }
        public static Rectangle S(Rectangle r) { return Aware ? new Rectangle(S(r.X), S(r.Y), S(r.Width), S(r.Height)) : r; }
        // screen pixels -> logical
        public static int L(int px) { return Aware ? (int)Math.Round(px / Scale) : px; }

        // the width of a text in LOGICAL pixels. A size that is worked out before the window is scaled must be logical,
        // and MeasureText answers in real pixels (the letters are already at the real size).
        public static int TextW(string text, Font font) { return L(TextRenderer.MeasureText(text, font).Width); }

        // the work area of the main screen in logical pixels (so "fits on the screen" is worked out in the same unit as the layout)
        public static Rectangle LogicalWorkArea()
        {
            Rectangle w = Screen.PrimaryScreen.WorkingArea;
            return Aware ? new Rectangle(L(w.X), L(w.Y), L(w.Width), L(w.Height)) : w;
        }
    }

    // ---------------------------------------------------------------- for --dpi-test: what screen scaling can break in a window
    // Parts that stick out of their block, parts on top of each other, text that does not fit, blocks that overlap, grids with the wrong heights.
    static class DpiCheck
    {
        static string NameOf(Control c)
        {
            string t = c.Text;
            if (t != null && t.Length > 30) t = t.Substring(0, 30) + "...";
            return c.GetType().Name + (string.IsNullOrEmpty(t) ? "" : " \"" + t.Replace("\r", " ").Replace("\n", " ") + "\"");
        }

        // null when the text of this control fits in it
        static string Fit(Control c)
        {
            Label l = c as Label;
            if (l != null && !l.AutoSize && !string.IsNullOrEmpty(l.Text))
            {
                int need = TextRenderer.MeasureText(l.Text.TrimEnd('\r', '\n', ' '), l.Font, new Size(l.Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
                if (need > l.Height + 3) return NameOf(l) + " needs " + need + " px of height but is " + l.Height + " px high";
            }
            IconButton b = c as IconButton;
            if (b != null && !string.IsNullOrEmpty(b.Text))
            {
                int w = TextRenderer.MeasureText(b.Text, b.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + (b.Glyph.Length > 0 ? Dpi.S(22) + Dpi.S(8) : 0) + Dpi.S(8) + Dpi.S(6);
                if (w > b.Width + 1) return NameOf(b) + " needs " + w + " px of width but is " + b.Width + " px wide";
            }
            return null;
        }

        // the controls of one block: all inside it, not on top of each other, their text fits
        public static void Container(Control box, string where, Action<bool, string> ck)
        {
            List<Control> kids = new List<Control>();
            foreach (Control c in box.Controls) if (c.Visible && c.Width > 0 && c.Height > 0) kids.Add(c);
            List<string> bad = new List<string>();
            int tol = 2;
            foreach (Control c in kids)
            {
                bool inside = c.Left >= -tol && c.Top >= -tol && c.Right <= box.ClientSize.Width + tol && c.Bottom <= box.ClientSize.Height + tol;
                if (!inside) bad.Add(NameOf(c) + " sticks out of " + NameOf(box) + " (" + c.Bounds + " in " + box.ClientSize + ")");
                string fit = Fit(c);
                if (fit != null) bad.Add(fit);
            }
            for (int i = 0; i < kids.Count; i++)
                for (int j = i + 1; j < kids.Count; j++)
                {
                    Rectangle x = Rectangle.Intersect(kids[i].Bounds, kids[j].Bounds);
                    if (!x.IsEmpty && x.Width > 3 && x.Height > 3) bad.Add(NameOf(kids[i]) + " is on top of " + NameOf(kids[j]) + " " + x);
                }
            ck(bad.Count == 0, where + ": " + kids.Count + " controls in " + NameOf(box) + " are inside, apart, and their text fits" + (bad.Count == 0 ? "" : "   PROBLEMS: " + string.Join(" | ", bad.ToArray())));
        }

        static void Grid(DataGridView g, string where, Action<bool, string> ck)
        {
            int rows = 0, wrong = 0;
            foreach (DataGridViewRow r in g.Rows) { rows++; if (Math.Abs(r.Height - Dpi.S(24)) > 1) wrong++; }
            ck(Math.Abs(g.ColumnHeadersHeight - Dpi.S(26)) <= 1 && Math.Abs(g.RowTemplate.Height - Dpi.S(24)) <= 1 && wrong == 0,
               where + ": grid header " + g.ColumnHeadersHeight + " px (expected " + Dpi.S(26) + "), row " + g.RowTemplate.Height + " px (expected " + Dpi.S(24) + "), " + wrong + " of " + rows + " rows with another height");
        }

        static void Walk(Control c, string where, Action<bool, string> ck)
        {
            if (c is Card || c is TableLayoutPanel) Container(c, where, ck);
            DataGridView g = c as DataGridView;
            if (g != null) Grid(g, where, ck);
            foreach (Control k in c.Controls) if (k.Visible) Walk(k, where, ck);
        }

        // one tab: its blocks one under the other without overlap, then what is inside each block
        public static void Tree(Control page, string where, Action<bool, string> ck)
        {
            List<Control> blocks = new List<Control>();
            foreach (Control c in page.Controls) if (c.Visible && (c is Card || c is TableLayoutPanel)) blocks.Add(c);
            blocks.Sort(delegate(Control a, Control b) { return a.Top.CompareTo(b.Top); });
            for (int i = 0; i + 1 < blocks.Count; i++)
                ck(blocks[i + 1].Top >= blocks[i].Bottom - 1, where + ": block " + (i + 2) + " starts at " + blocks[i + 1].Top + " px and the block above ends at " + blocks[i].Bottom + " px");
            Walk(page, where, ck);
        }

        // a small window: it fits the screen, it was scaled, everything in it is inside and apart
        public static void Dialog(ChromeForm f, string name, Action<bool, string> ck)
        {
            Rectangle wa = Screen.FromControl(f).WorkingArea;
            ck(f.Width <= wa.Width && f.Height <= wa.Height, name + ": " + f.Width + "x" + f.Height + " fits the work area " + wa.Width + "x" + wa.Height);
            ck(!(Dpi.Aware && Dpi.Scale > 1.001f) || f.IsDpiScaled, name + ": was scaled by " + Dpi.Scale);
            ck(f.TitleBar.Height == Dpi.S(40) || Math.Abs(f.TitleBar.Height - Dpi.S(40)) <= 1, name + ": title bar " + f.TitleBar.Height + " px (expected " + Dpi.S(40) + ")");
            Container(f.Body, name, ck);
            foreach (Control c in f.Body.Controls) if (c.Visible) Walk(c, name, ck);
        }
    }

    static class Info
    {
        // The order of the tabs, the tiles of the start window and the bullets of the welcome window, left to right: FiveM cache, Frame cap, DNS tools, Graphics.
        // The numbers are the page numbers and stay as they are (0 frame cap, 1 DNS tools, 2 FiveM cache, 3 graphics), because the settings file ("startpage") and "--page N" use them.
        public static readonly int[] PageOrder = new int[] { 2, 0, 1, 3 };
        public static int FirstPage { get { return PageOrder[0]; } }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        // The icon inside an exe. Icon.ExtractAssociatedIcon refuses network paths; ExtractIconEx (shell32) does not.
        public static Icon IconOfExe(string exe, out string how)
        {
            how = "";
            try { Icon ic = Icon.ExtractAssociatedIcon(exe); if (ic != null) { how = "ExtractAssociatedIcon"; return ic; } }
            catch (Exception ex) { how = "ExtractAssociatedIcon failed (" + ex.Message + "); "; }
            try
            {
                IntPtr[] large = new IntPtr[1];
                if (ExtractIconEx(exe, 0, large, null, 1) > 0 && large[0] != IntPtr.Zero)
                {
                    Icon ic2 = (Icon)Icon.FromHandle(large[0]).Clone();
                    DestroyIcon(large[0]);
                    how += "ExtractIconEx";
                    return ic2;
                }
                how += "ExtractIconEx found no icon";
            }
            catch (Exception ex2) { how += "ExtractIconEx failed (" + ex2.Message + ")"; }
            return null;
        }

        public static Icon AppIcon()
        {
            string how;
            Icon ic = IconOfExe(Assembly.GetExecutingAssembly().Location, out how);
            return ic != null ? ic : DrawnIcon();   // last resort: the program draws its own icon
        }

        static Icon drawnIcon;
        static Icon DrawnIcon()
        {
            if (drawnIcon != null) return drawnIcon;
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                        using (GraphicsPath p = Gfx.Rounded(new Rectangle(1, 1, 29, 29), 8)) using (Brush b = new SolidBrush(Color.FromArgb(24, 24, 28))) g.FillPath(b, p);
                        Gfx.DrawGauge(g, new Rectangle(3, 3, 26, 26), Color.FromArgb(17, 119, 181));
                    }
                    drawnIcon = Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch (Exception) { }
            return drawnIcon;
        }

        public const string Version = "0.8.1";
        public const string AppName = "FiveM Toolbox";
        public const string ExeName = "FiveMToolbox";
        public const string Folder = "FiveMToolbox";
        public const string Owner = "Pepreal";
        public const string Year = "2026";
        public const int MaxCap = 140;   // higher caps can micro-stutter, so 140 is the top for now
        public const int MinCap = 30;
        public const int DefaultCap = 140;
    }

    // ---------------------------------------------------------------- AMD ADL (read only)
    static class Adl
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr Alloc(int size);
        static readonly Alloc AllocCb = new Alloc(delegate(int s) { return Marshal.AllocHGlobal(s); });
        const string D = "atiadlxx.dll";

        [DllImport(D, CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Main_Control_Create(Alloc cb, int enumConnected, out IntPtr ctx);
        [DllImport(D, CallingConvention = CallingConvention.Cdecl)]
        static extern int ADL2_Main_Control_Destroy(IntPtr ctx);
        [DllImport(D, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern int ADL2_ApplicationProfiles_ProfileOfAnApplicationX2_Search(IntPtr ctx, string file, string path, string version, string area, out IntPtr profile);
        [DllImport(D, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern int ADL2_ApplicationProfiles_Applications_Get(IntPtr ctx, string area, out int num, out IntPtr apps);

        public class ProfileValues { public bool Found; public int Rc; public string Pf = ""; public string Min = ""; public string Max = ""; }
        public class AppInfo { public int Index; public string File; public string Path; public string Profile; }

        public static bool Available()
        {
            try
            {
                IntPtr c; int rc = ADL2_Main_Control_Create(AllocCb, 1, out c);
                if (rc == 0) ADL2_Main_Control_Destroy(c);
                AppLog.Debug("AMD library (ADL) start: result code " + rc + (rc == 0 ? " (ok)" : " (not ok)"));
                return rc == 0;
            }
            catch (Exception ex) { AppLog.Debug("AMD library (ADL) cannot be loaded: " + ex.GetType().Name + ": " + ex.Message); return false; }
        }

        static string Ptr(IntPtr p) { return p == IntPtr.Zero ? "" : Marshal.PtrToStringUni(p); }

        public static List<AppInfo> Apps()
        {
            List<AppInfo> list = new List<AppInfo>();
            IntPtr ctx;
            if (ADL2_Main_Control_Create(AllocCb, 1, out ctx) != 0) return list;
            try
            {
                int n; IntPtr apps;
                if (ADL2_ApplicationProfiles_Applications_Get(ctx, "3D_User", out n, out apps) != 0 || apps == IntPtr.Zero) return list;
                int stride = 8 + 7 * 8;
                for (int k = 0; k < n && k < 5000; k++)
                {
                    long b = apps.ToInt64() + (long)k * stride;
                    AppInfo a = new AppInfo();
                    a.Index = k;
                    a.Path = Ptr(Marshal.ReadIntPtr(new IntPtr(b + 8 + 0 * 8)));
                    a.File = Ptr(Marshal.ReadIntPtr(new IntPtr(b + 8 + 1 * 8)));
                    a.Profile = Ptr(Marshal.ReadIntPtr(new IntPtr(b + 8 + 5 * 8)));
                    list.Add(a);
                }
            }
            finally { ADL2_Main_Control_Destroy(ctx); }
            return list;
        }

        // Ask AMD for the Radeon Chill values of one exe.
        public static ProfileValues Read(string file, string dir)
        {
            ProfileValues pv = ReadRaw(file, dir);
            AppLog.Debug("ADL read " + file + " in " + dir + ": result " + pv.Rc + ", found=" + pv.Found + ", pf=[" + pv.Pf + "] min=[" + pv.Min + "] max=[" + pv.Max + "]");
            return pv;
        }

        static ProfileValues ReadRaw(string file, string dir)
        {
            ProfileValues pv = new ProfileValues();
            IntPtr ctx;
            if (ADL2_Main_Control_Create(AllocCb, 1, out ctx) != 0) { pv.Rc = -99; return pv; }
            try
            {
                IntPtr p;
                int rc = ADL2_ApplicationProfiles_ProfileOfAnApplicationX2_Search(ctx, file, dir, "", "3D_User", out p);
                pv.Rc = rc;
                if (rc != 0 || p == IntPtr.Zero) return pv;
                int count = Marshal.ReadInt32(p, 0);
                if (count <= 0 || count > 500) return pv;
                pv.Found = true;
                int off = 4;
                for (int i = 0; i < count; i++)
                {
                    byte[] nb = new byte[64];
                    Marshal.Copy(new IntPtr(p.ToInt64() + off), nb, 0, 64);
                    string name = Encoding.ASCII.GetString(nb);
                    int z = name.IndexOf('\0'); if (z >= 0) name = name.Substring(0, z);
                    int size = Marshal.ReadInt32(p, off + 68);
                    if (size < 0 || size > 8192) break;
                    string val = "";
                    if (size > 0)
                    {
                        byte[] db = new byte[size];
                        Marshal.Copy(new IntPtr(p.ToInt64() + off + 72), db, 0, size);
                        val = Encoding.Unicode.GetString(db);
                        int zz = val.IndexOf('\0'); if (zz >= 0) val = val.Substring(0, zz);
                    }
                    if (name == "Chil_PFEnable") pv.Pf = val;
                    else if (name == "Chil_MinFRate") pv.Min = val;
                    else if (name == "Chil_MaxFRate") pv.Max = val;
                    off += 72 + size;
                }
            }
            finally { ADL2_Main_Control_Destroy(ctx); }
            return pv;
        }
    }

    // ---------------------------------------------------------------- value text like 0x0300::140;;0x0e00::140;;
    static class Val
    {
        public static List<string[]> Pairs(string s)
        {
            List<string[]> r = new List<string[]>();
            if (string.IsNullOrEmpty(s)) return r;
            foreach (string part in s.Split(new string[] { ";;" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = part.IndexOf("::");
                if (i > 0) r.Add(new string[] { part.Substring(0, i), part.Substring(i + 2) });
            }
            return r;
        }
        public static int First(string s)
        {
            List<string[]> p = Pairs(s); int v;
            if (p.Count > 0 && int.TryParse(p[0][1], out v)) return v;
            return -1;
        }
        // New text with the cap in every pair; drops trailing pairs until it fits into the old slot.
        public static string Make(string old, int cap, int maxBytes)
        {
            List<string[]> p = Pairs(old);
            for (int n = p.Count; n >= 1; n--)
            {
                StringBuilder sb = new StringBuilder();
                for (int k = 0; k < n; k++) sb.Append(p[k][0]).Append("::").Append(cap).Append(";;");
                if (Encoding.Unicode.GetByteCount(sb.ToString()) <= maxBytes) return sb.ToString();
            }
            return null;
        }
    }

    // ---------------------------------------------------------------- AMD profile file (User.blb)
    class Block { public int Lo; public int Hi; public string Min = ""; public string Max = ""; public string Pf = ""; public int MinLen; public int MaxLen; }

    static class Blb
    {
        public static string Path
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ATI\ACE\APL\User.blb"); }
        }
        public static string BackupDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Info.Folder + @"\backup"); }
        }

        static int Find(byte[] d, int lo, int hi, string name)
        {
            byte[] pat = new byte[16];
            Encoding.ASCII.GetBytes(name).CopyTo(pat, 0);
            for (int i = lo; i + 20 <= hi; i++)
            {
                if (d[i] != pat[0]) continue;
                bool ok = true;
                for (int k = 1; k < 16; k++) if (d[i + k] != pat[k]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }
        static string ReadAt(byte[] d, int rec, out int len)
        {
            len = 0;
            if (rec < 0) return "";
            len = (int)BitConverter.ToUInt32(d, rec + 16);
            if (len <= 0 || len > 8192 || rec + 20 + len > d.Length) { len = 0; return ""; }
            string s = Encoding.Unicode.GetString(d, rec + 20, len);
            int z = s.IndexOf('\0'); if (z >= 0) s = s.Substring(0, z);
            return s;
        }

        // Split the profile area of the file into one block per game profile.
        public static List<Block> Layout(byte[] d)
        {
            if (d.Length < 0x100 || d[0] != 0x42 || d[1] != 0x57 || d[2] != 0x4A || d[3] != 0x45)
                throw new Exception("Radeon profile file has an unknown format (no BWJE mark).");
            uint[] w = new uint[13];
            for (int i = 0; i < 13; i++) w[i] = BitConverter.ToUInt32(d, 0x2C + 4 * i);
            long dstart = (long)w[7] * 8, dend = ((long)w[7] + w[8]) * 8;
            long estart = (long)w[9] * 8, elen = (long)w[10] * 8, flen = (long)w[12] * 8;
            if (dend > d.Length || dstart >= dend || estart + elen + flen > d.Length)
                throw new Exception("Radeon profile file layout is different from what this tool knows.");
            int words = (int)((elen + flen) / 4);
            uint[] tab = new uint[words];
            for (int i = 0; i < words; i++) tab[i] = BitConverter.ToUInt32(d, (int)estart + 4 * i);
            List<long> offs = new List<long>();
            for (int i = 0; i < words - 1; )
            {
                if (tab[i + 1] == 0xFFFFFFFF && (long)tab[i] >= dstart / 8 && (long)tab[i] < dend / 8) { offs.Add((long)tab[i] * 8); i += 2; }
                else i++;
            }
            if (offs.Count < 5) throw new Exception("Could not read the profile list in the Radeon file.");
            for (int i = 1; i < offs.Count; i++) if (offs[i] <= offs[i - 1]) throw new Exception("Profile list in the Radeon file is not in the expected order.");
            List<Block> blocks = new List<Block>();
            for (int i = 0; i < offs.Count; i++)
            {
                Block b = new Block();
                b.Lo = (int)offs[i]; b.Hi = (int)(i + 1 < offs.Count ? offs[i + 1] : dend);
                int r = Find(d, b.Lo, b.Hi, "Chil_MinFRate"); b.Min = ReadAt(d, r, out b.MinLen);
                r = Find(d, b.Lo, b.Hi, "Chil_MaxFRate"); b.Max = ReadAt(d, r, out b.MaxLen);
                r = Find(d, b.Lo, b.Hi, "Chil_PFEnable"); int dummy; b.Pf = ReadAt(d, r, out dummy);
                blocks.Add(b);
            }
            return blocks;
        }

        // Write the cap into one block. Same slot size, padded with empty bytes, so nothing else moves.
        public static void Patch(byte[] d, Block b, int cap)
        {
            Patch(d, b, delegate(string name, string old, int len) { return Val.Make(old, cap, len); });
        }

        // make(name, oldText, slotBytes) gives the new text for that value (null = it does not fit)
        public static void Patch(byte[] d, Block b, Func<string, string, int, string> make)
        {
            foreach (string name in new string[] { "Chil_MinFRate", "Chil_MaxFRate" })
            {
                int r = Find(d, b.Lo, b.Hi, name);
                int len; string old = ReadAt(d, r, out len);
                if (r < 0 || len <= 0) throw new Exception("This profile has no value for " + name + " yet.");
                string text = make(name, old, len);
                if (text == null) throw new Exception("The new value does not fit into the profile slot.");
                byte[] nb = Encoding.Unicode.GetBytes(text);
                for (int k = 0; k < len; k++) d[r + 20 + k] = 0;
                Array.Copy(nb, 0, d, r + 20, nb.Length);
            }
        }

        public static void BumpReload()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\ATI\ACE\Settings\ADL\AppProfiles", true))
                {
                    if (k == null) return;
                    int cur = 0; object o = k.GetValue("AplReloadCounter"); if (o is int) cur = (int)o;
                    k.SetValue("AplReloadCounter", cur + 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------- Radeon Software process
    static class Radeon
    {
        public static string Exe = @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe";
        public static string Args = "";

        public static void Capture()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE Name='RadeonSoftware.exe'"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string exe = mo["ExecutablePath"] as string, cl = mo["CommandLine"] as string;
                        if (!string.IsNullOrEmpty(exe)) Exe = exe;
                        if (!string.IsNullOrEmpty(cl) && !string.IsNullOrEmpty(exe))
                        {
                            string rest = cl.Trim();
                            if (rest.StartsWith("\"")) { int q = rest.IndexOf('"', 1); if (q > 0) rest = rest.Substring(q + 1); }
                            else { int sp = rest.IndexOf(' '); rest = sp > 0 ? rest.Substring(sp + 1) : ""; }
                            Args = rest.Trim();
                        }
                        break;
                    }
            }
            catch (Exception) { }
        }
        public static bool Installed() { return File.Exists(Exe); }
        public static bool Running() { return Process.GetProcessesByName("RadeonSoftware").Length > 0; }
        public static void Stop()
        {
            foreach (string n in new string[] { "RadeonSoftware", "cncmd" })
                foreach (Process p in Process.GetProcessesByName(n))
                {
                    try { p.Kill(); p.WaitForExit(10000); } catch (Exception) { }
                }
            Thread.Sleep(2000);
        }
        public static void Start()
        {
            Launch.Normal(Exe, Args, Path.GetDirectoryName(Exe));
        }
    }

    // ---------------------------------------------------------------- finding FiveM
    class Build
    {
        public string Exe; public string Dir; public string FileName; public string Root;
        public int AdlIndex = -1;
        public bool Missing = false;     // remembered from before, the file is not on disk now
        public string Status = "";
    }

    class Gpu { public string Name; public string Vendor; public string Pnp = ""; public string Driver = ""; public string DriverDate = ""; public string Kind = ""; public string KindFrom = ""; public long Vram = 0; }   // Kind: integrated, dedicated or "" (not sure); KindFrom: "Windows" or "name"

    // Asks Windows itself which chip is the dedicated one and which is the integrated one (the information behind "Power saving" and "High performance" in Windows Settings > System > Display > Graphics).
    // gdi32.dll: D3DKMTEnumAdapters2 lists the adapters, D3DKMTQueryAdapterInfo gives the adapter name (type 8, the registry info) and the adapter type bits (type 15).
    // Of the type bits, 16 = HybridDiscrete (the dedicated chip of a switchable pair) and 32 = HybridIntegrated (the integrated chip of that pair). Tested on a laptop with an AMD 680M and an RTX 4060.
    // Not every PC sets these bits (it is meant for switchable pairs), so the name is the fallback.
    static class WinGpu
    {
        [DllImport("gdi32.dll")] static extern int D3DKMTEnumAdapters2(ref KmtEnum e);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref KmtQuery q);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref KmtClose c);
        [StructLayout(LayoutKind.Sequential)] struct KmtEnum { public uint Num; public IntPtr Adapters; }
        [StructLayout(LayoutKind.Sequential)] struct KmtQuery { public uint Adapter; public int Type; public IntPtr Data; public uint Size; }
        [StructLayout(LayoutKind.Sequential)] struct KmtClose { public uint Adapter; }
        const int AdapterInfoSize = 20;           // D3DKMT_ADAPTERINFO: handle (4), LUID (8), number of sources (4), present move regions preferred (4)
        const int TypeRegistryInfo = 8, TypeAdapterType = 15, RegistryInfoSize = 2080;   // D3DKMT_ADAPTERREGISTRYINFO: four strings of 260 UTF-16 characters, the first is the adapter name
        public const uint FlagSoftware = 4, FlagHybridDiscrete = 16, FlagHybridIntegrated = 32;

        public class Adapter { public string Name = ""; public uint Flags; public string Luid = ""; }   // Luid like "0x00000000_0x00010ce8" (high_low), the same spelling as in the counter names

        public static string Describe(uint f)
        {
            string[] names = new string[] { "Render", "Display", "Software", "Post", "HybridDiscrete (dedicated)", "HybridIntegrated (integrated)", "Indirect display", "Paravirtualized" };
            List<string> on = new List<string>();
            for (int i = 0; i < names.Length; i++) if ((f & (1u << i)) != 0) on.Add(names[i]);
            return on.Count == 0 ? "none" : string.Join(", ", on.ToArray());
        }

        static bool Ask(uint adapter, int type, IntPtr buf, int size)
        {
            KmtQuery q = new KmtQuery(); q.Adapter = adapter; q.Type = type; q.Data = buf; q.Size = (uint)size;
            return D3DKMTQueryAdapterInfo(ref q) == 0;
        }

        // READ ONLY. An empty list means Windows could not tell (an old Windows, or the call failed): the names decide then.
        public static List<Adapter> Query()
        {
            List<Adapter> list = new List<Adapter>();
            try
            {
                KmtEnum e = new KmtEnum();
                int st1 = D3DKMTEnumAdapters2(ref e);                // the first call only gives the number
                AppLog.Debug("D3DKMTEnumAdapters2 (count): status 0x" + st1.ToString("X8") + ", adapters " + e.Num);
                if (e.Num == 0 || e.Num > 512) return list;      // a PC with virtual displays can list dozens
                IntPtr arr = Marshal.AllocHGlobal(AdapterInfoSize * (int)e.Num);
                try
                {
                    Marshal.Copy(new byte[AdapterInfoSize * (int)e.Num], 0, arr, AdapterInfoSize * (int)e.Num);
                    e.Adapters = arr;
                    int st2 = D3DKMTEnumAdapters2(ref e);
                    AppLog.Debug("D3DKMTEnumAdapters2 (list): status 0x" + st2.ToString("X8") + ", adapters " + e.Num);
                    if (st2 != 0) return list;
                    for (int i = 0; i < (int)e.Num; i++)
                    {
                        uint h = unchecked((uint)Marshal.ReadInt32(arr, i * AdapterInfoSize));
                        try
                        {
                            Adapter a = new Adapter();
                            a.Luid = "0x" + unchecked((uint)Marshal.ReadInt32(arr, i * AdapterInfoSize + 8)).ToString("x8") + "_0x" + unchecked((uint)Marshal.ReadInt32(arr, i * AdapterInfoSize + 4)).ToString("x8");
                            IntPtr reg = Marshal.AllocHGlobal(RegistryInfoSize);
                            try
                            {
                                Marshal.Copy(new byte[RegistryInfoSize], 0, reg, RegistryInfoSize);
                                if (Ask(h, TypeRegistryInfo, reg, RegistryInfoSize)) a.Name = (Marshal.PtrToStringUni(reg) ?? "").Trim();
                            }
                            finally { Marshal.FreeHGlobal(reg); }
                            IntPtr ty = Marshal.AllocHGlobal(4);
                            try
                            {
                                Marshal.WriteInt32(ty, 0, 0);
                                if (Ask(h, TypeAdapterType, ty, 4)) a.Flags = unchecked((uint)Marshal.ReadInt32(ty, 0));
                                else continue;
                            }
                            finally { Marshal.FreeHGlobal(ty); }
                            list.Add(a);
                        }
                        finally { KmtClose c = new KmtClose(); c.Adapter = h; try { D3DKMTCloseAdapter(ref c); } catch (Exception) { } }
                    }
                }
                finally { Marshal.FreeHGlobal(arr); }
            }
            catch (Exception ex) { AppLog.Debug("asking Windows about the graphics chips failed: " + ex.Message); list.Clear(); }
            return list;
        }
    }

    static class Finder
    {
        static readonly Regex ExeRx = new Regex(@"^FiveM(_b\d+)?_GTAProcess\.exe$", RegexOptions.IgnoreCase);

        // integrated = the chip inside the processor (shares the PC memory), dedicated = a separate card with its own memory.
        // Decided from the name (the way these chips are named is stable); "" means not sure, and then nothing is claimed.
        public static string KindOf(string vendor, string name)
        {
            string n = " " + (name ?? "").ToLowerInvariant() + " ";
            if (vendor == "NVIDIA") return n.Contains("tegra") ? "" : "dedicated";
            if (vendor == "AMD")
            {
                if (Regex.IsMatch(n, @"\b(rx|pro|vii|fury|hd|frontier)\b") || Regex.IsMatch(n, @"\br[579]\b")) return "dedicated";
                if (Regex.IsMatch(n, @"\b\d{3}m\b") || n.Contains("vega") || n.TrimEnd().EndsWith("graphics")) return "integrated";
                return "";
            }
            if (vendor == "Intel")
            {
                if (n.Contains("arc") && Regex.IsMatch(n, @"\b[ab]\d{2,3}m?\b")) return "dedicated";
                return "integrated";
            }
            return "";
        }

        // Windows first: when Windows says this chip is the dedicated or the integrated one of a switchable pair, that is trusted (source "Windows").
        // Without a clear answer (no flags, no hybrid bits, or both bits) the name decides (source "name").
        public static string KindOf(string vendor, string name, uint? windowsFlags, out string source)
        {
            if (windowsFlags.HasValue)
            {
                bool disc = (windowsFlags.Value & WinGpu.FlagHybridDiscrete) != 0, integ = (windowsFlags.Value & WinGpu.FlagHybridIntegrated) != 0;
                if (disc && !integ) { source = "Windows"; return "dedicated"; }
                if (integ && !disc) { source = "Windows"; return "integrated"; }
            }
            source = "name";
            return KindOf(vendor, name);
        }

        // The memory that belongs to the card itself (not the PC memory it may borrow), from the driver's registry entry. 0 = not known.
        static long VramOf(string name)
        {
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return 0;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        int num;
                        if (!int.TryParse(sub, out num)) continue;
                        using (RegistryKey k = cls.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            string desc = k.GetValue("DriverDesc") as string;
                            if (desc == null || !string.Equals(desc, name, StringComparison.OrdinalIgnoreCase)) continue;
                            object v = k.GetValue("HardwareInformation.qwMemorySize");
                            if (v == null) v = k.GetValue("HardwareInformation.MemorySize");
                            long n = 0;
                            if (v is long) n = (long)v;
                            else if (v is int) n = (long)(uint)(int)v;
                            else if (v is byte[] && ((byte[])v).Length >= 8) n = BitConverter.ToInt64((byte[])v, 0);
                            else if (v is byte[] && ((byte[])v).Length >= 4) n = BitConverter.ToUInt32((byte[])v, 0);
                            if (n > 0) return n;
                        }
                    }
                }
            }
            catch (Exception) { }
            return 0;
        }

        public static List<Gpu> Gpus()
        {
            List<Gpu> list = new List<Gpu>();
            List<WinGpu.Adapter> win = WinGpu.Query();
            AppLog.Debug("Windows lists " + win.Count + " graphics adapter(s) with type bits");
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, DriverVersion, DriverDate FROM Win32_VideoController"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string name = (mo["Name"] as string) ?? "", pnp = ((mo["PNPDeviceID"] as string) ?? "").ToUpperInvariant();
                        string v = "Other";
                        if (pnp.Contains("VEN_1002")) v = "AMD";
                        else if (pnp.Contains("VEN_10DE")) v = "NVIDIA";
                        else if (pnp.Contains("VEN_8086")) v = "Intel";
                        if (pnp.StartsWith("ROOT\\") || name.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Parsec", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AppLog.Debug("skipping video controller (not a real card): " + name + " | " + pnp);
                            continue;
                        }
                        Gpu g = new Gpu(); g.Name = name; g.Vendor = v; g.Pnp = pnp; g.Driver = (mo["DriverVersion"] as string) ?? "";
                        try { string dd = mo["DriverDate"] as string; if (!string.IsNullOrEmpty(dd)) g.DriverDate = ManagementDateTimeConverter.ToDateTime(dd).ToString("d"); } catch (Exception) { }
                        string cardName = name;
                        WinGpu.Adapter wa = win.Find(delegate(WinGpu.Adapter a) { return a.Name.Length > 0 && string.Equals(a.Name, cardName.Trim(), StringComparison.OrdinalIgnoreCase); });
                        string src;
                        g.Kind = KindOf(v, name, wa != null ? (uint?)wa.Flags : null, out src); g.KindFrom = src; g.Vram = VramOf(name);
                        if (src == "Windows") { string byName = KindOf(v, name); if (byName.Length > 0 && byName != g.Kind) AppLog.Debug("Windows says " + g.Kind + " but the name says " + byName + " for " + name + ": Windows is trusted"); }
                        list.Add(g);
                        AppLog.Debug("graphics card: vendor=" + v + " kind=" + (g.Kind.Length > 0 ? g.Kind : "unknown") + " (decided by " + src + ") own memory=" + (g.Vram > 0 ? CacheTool.Size(g.Vram) : "unknown") + " name=" + name + " driver=" + g.Driver + " pnp=" + pnp);
                    }
            }
            catch (Exception ex) { AppLog.Debug("graphics card query failed: " + ex); }
            ApplyPretend(list, PretendVramMb);
            return list;
        }

        // Normalise any path that sits inside a FiveM install to the ...\FiveM.app folder.
        static string RootFromPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            p = p.TrimEnd('*', '\\', '/');
            int i = p.IndexOf(@"\FiveM.app", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                string root = p.Substring(0, i) + @"\FiveM.app";
                if (Directory.Exists(root)) return root;
            }
            if (Directory.Exists(Path.Combine(p, "FiveM.app"))) return Path.Combine(p, "FiveM.app");
            return null;
        }

        public static int PretendVramMb = 0;     // test mode: --pretend-vram <MB>
        public static int PretendUseMb = 0;      // test mode: --pretend-vram-use <MB>: the meter acts as if the FiveM game holds this much video memory

        // A dedicated Intel card (Arc) is not supported at the moment, because it could not be tested: no frame cap, no advice for the budget, no meter, and the window says so.
        public static bool IsUnsupportedDedicated(Gpu g) { return g != null && g.Vendor == "Intel" && g.Kind == "dedicated"; }
        public static bool HasUnsupportedDedicated(List<Gpu> list)
        {
            if (list == null) return false;
            foreach (Gpu g in list) if (IsUnsupportedDedicated(g)) return true;
            return false;
        }
        public static string TestModeText()
        {
            string t = "";
            if (PretendVramMb > 0) t += "pretending " + PretendVramMb + " MB of video memory";
            if (PretendUseMb > 0) t += (t.Length > 0 ? ", " : "pretending ") + "FiveM uses " + PretendUseMb + " MB";
            return t.Length > 0 ? "  -  TEST MODE: " + t : "";
        }
        public static void ApplyPretend(List<Gpu> list, int mb)
        {
            if (mb <= 0 || list == null) return;
            foreach (Gpu g in list) if (g.Kind == "dedicated") g.Vram = mb * 1048576L;
        }

        static string ShortcutTarget(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                return sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string;
            }
            catch (Exception) { return null; }
        }

        // Find files in a folder tree, skipping folders Windows will not let us read (like the Start Menu "Programma's" link).
        static void SafeFiles(string dir, string pattern, int depth, List<string> result)
        {
            if (depth < 0) return;
            try { result.AddRange(Directory.GetFiles(dir, pattern)); } catch (Exception) { }
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch (Exception) { return; }
            foreach (string s in subs) SafeFiles(s, pattern, depth - 1, result);
        }

        static void Scan(string dir, int depth, List<string> found, DateTime stop)
        {
            if (depth < 0 || DateTime.UtcNow > stop) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch (Exception) { return; }
            foreach (string s in subs)
            {
                string n = Path.GetFileName(s);
                if (string.Equals(n, "FiveM.app", StringComparison.OrdinalIgnoreCase)) { found.Add(s); continue; }
                if (n.StartsWith("$") || string.Equals(n, "Windows", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "System Volume Information", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n, "ProgramData", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                Scan(s, depth - 1, found, stop);
            }
        }

        public static List<string> Roots(Action<string> log, List<Adl.AppInfo> adlApps)
        {
            List<string> roots = new List<string>();
            Action<string, string> add = delegate(string root, string why)
            {
                if (root == null) return;
                foreach (string r in roots) if (string.Equals(r, root, StringComparison.OrdinalIgnoreCase)) return;
                roots.Add(root); log("Found FiveM (" + why + "): " + root);
            };
            // 1. default place
            string defFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM");
            AppLog.Debug("search 1 (default folder): " + defFolder + " exists=" + Directory.Exists(defFolder));
            add(RootFromPath(defFolder), "default folder");
            // 2. FiveM running right now
            int runCount = 0;
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    if (!p.ProcessName.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.Id == Process.GetCurrentProcess().Id) continue;   // that is this program, not the game
                    runCount++;
                    try { string f = p.MainModule.FileName; AppLog.Debug("  running: " + p.ProcessName + " pid " + p.Id + " " + f); add(RootFromPath(f) ?? RootFromPath(Path.GetDirectoryName(f)), "running"); }
                    catch (Exception ex) { AppLog.Debug("  could not read the path of " + p.ProcessName + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { AppLog.Debug("search 2 failed: " + ex.Message); }
            AppLog.Debug("search 2 (running FiveM): " + runCount + " process(es)");
            // 3. what Radeon Software already knows
            int adlFive = 0;
            if (adlApps != null)
                foreach (Adl.AppInfo a in adlApps)
                    if (a.File != null && a.File.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase)) { adlFive++; AppLog.Debug("  Radeon list entry #" + a.Index + ": " + a.File + " in " + a.Path + " profile " + a.Profile); add(RootFromPath(a.Path), "Radeon Software list"); }
            AppLog.Debug("search 3 (Radeon Software list): " + (adlApps == null ? "not used" : adlApps.Count + " games, " + adlFive + " FiveM entries"));
            // 4. shortcuts
            List<string> dirs = new List<string>();
            foreach (Environment.SpecialFolder sf in new Environment.SpecialFolder[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
                try { dirs.Add(Environment.GetFolderPath(sf)); } catch (Exception) { }
            int lnkCount = 0;
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir)) { AppLog.Debug("search 4 (shortcuts): folder missing " + dir); continue; }
                List<string> lnks = new List<string>();
                SafeFiles(dir, "*FiveM*.lnk", 4, lnks);
                foreach (string lnk in lnks)
                {
                    lnkCount++;
                    string t = ShortcutTarget(lnk);
                    AppLog.Debug("  shortcut " + lnk + " -> " + t);
                    if (!string.IsNullOrEmpty(t)) add(RootFromPath(t) ?? RootFromPath(Path.GetDirectoryName(t)), "shortcut");
                }
            }
            AppLog.Debug("search 4 (shortcuts): " + lnkCount + " FiveM shortcut(s)");
            // 5. look through the fixed drives (a few folders deep)
            DateTime stop = DateTime.UtcNow.AddSeconds(45);
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try { if (di.DriveType != DriveType.Fixed || !di.IsReady) continue; } catch (Exception) { continue; }
                log("Looking on drive " + di.Name + " ...");
                DateTime t0 = DateTime.UtcNow;
                List<string> found = new List<string>();
                Scan(di.RootDirectory.FullName, 3, found, stop);
                AppLog.Debug("search 5 (drive " + di.Name + "): " + found.Count + " FiveM.app folder(s) in " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms" + (DateTime.UtcNow > stop ? " (time limit reached)" : ""));
                foreach (string f in found) add(f, "drive search");
            }
            AppLog.Debug("search finished: " + roots.Count + " FiveM folder(s)");
            return roots;
        }

        public static List<string> RootsFromFolder(string folder)
        {
            List<string> found = new List<string>();
            string r = RootFromPath(folder);
            if (r != null) { found.Add(r); return found; }
            if (string.Equals(Path.GetFileName(folder.TrimEnd('\\')), "FiveM.app", StringComparison.OrdinalIgnoreCase)) { found.Add(folder.TrimEnd('\\')); return found; }
            Scan(folder, 4, found, DateTime.UtcNow.AddSeconds(30));
            return found;
        }

        // ---- builds seen before, so they still show after a cache clean has deleted their files
        static string MemoryFile { get { return Path.Combine(Path.GetDirectoryName(Blb.BackupDir), "builds.txt"); } }

        static Dictionary<string, List<string>> LoadMemory()
        {
            Dictionary<string, List<string>> m = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(MemoryFile))
                    foreach (string line in File.ReadAllLines(MemoryFile))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length != 2 || p[0].Length == 0 || p[1].Length == 0) continue;
                        if (!m.ContainsKey(p[0])) m[p[0]] = new List<string>();
                        m[p[0]].Add(p[1]);
                    }
            }
            catch (Exception) { }
            return m;
        }

        static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (string x in a)
            {
                string one = x;
                if (!b.Exists(delegate(string y) { return string.Equals(one, y, StringComparison.OrdinalIgnoreCase); })) return false;
            }
            return true;
        }

        // builds the user removed with "Reset the list": they stay out of the list until their file is on disk again
        static string ForgottenFile { get { return Path.Combine(Path.GetDirectoryName(Blb.BackupDir), "builds-forgotten.txt"); } }

        static List<string> LoadForgotten()
        {
            List<string> l = new List<string>();
            try { if (File.Exists(ForgottenFile)) foreach (string line in File.ReadAllLines(ForgottenFile)) if (line.Trim().Length > 0) l.Add(line.Trim()); }
            catch (Exception) { }
            return l;
        }

        static void SaveForgotten(List<string> l)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(ForgottenFile)); File.WriteAllLines(ForgottenFile, l.ToArray()); }
            catch (Exception ex) { AppLog.Debug("builds: could not save the removed list: " + ex.Message); }
        }

        static bool Has(List<string> l, string x) { return l.Exists(delegate(string y) { return string.Equals(x, y, StringComparison.OrdinalIgnoreCase); }); }

        static bool RemoveFrom(List<string> l, string x) { return l.RemoveAll(delegate(string y) { return string.Equals(x, y, StringComparison.OrdinalIgnoreCase); }) > 0; }

        // Takes the builds that are not on disk out of the list (and forgets what was written down). Returns how many were removed.
        public static int ForgetMissing(List<Build> current)
        {
            List<string> forgot = LoadForgotten();
            int n = 0;
            foreach (Build b in current) if (b.Missing) { AddUnique(forgot, b.Exe); n++; }
            SaveForgotten(forgot);
            try { if (File.Exists(MemoryFile)) File.Delete(MemoryFile); } catch (Exception) { }
            return n;
        }

        static void AddUnique(List<string> list, string x)
        {
            if (!list.Exists(delegate(string y) { return string.Equals(x, y, StringComparison.OrdinalIgnoreCase); })) list.Add(x);
        }

        // Builds that appeared since the last search (not the very first search). The window tells you about them.
        public static List<string> LastNew = new List<string>();

        // Writes down the builds that are on disk now and returns the builds that are gone from the disk but can still be capped.
        // With AMD that is every build Radeon Software still has a profile for in that folder (a cache clean deletes the files, not the profiles).
        // Without a list from the driver, the builds written down before are used.
        public static List<Build> RememberAndRecall(List<string> roots, List<Build> onDisk, List<Adl.AppInfo> adlApps)
        {
            List<Build> gone = new List<Build>();
            LastNew = new List<string>();
            Dictionary<string, List<string>> m = LoadMemory();
            bool changed = false;
            List<string> forgot = LoadForgotten();
            bool forgotChanged = false;
            foreach (string root in roots)
            {
                string sub = Path.Combine(root, @"data\cache\subprocess");
                List<string> now = new List<string>();
                foreach (Build d in onDisk)
                    if (!d.Missing && string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase)) now.Add(d.Exe);
                List<string> old;
                if (!m.TryGetValue(root, out old)) old = new List<string>();
                foreach (string back in now) if (RemoveFrom(forgot, back)) forgotChanged = true;   // on disk again: no longer forgotten
                // what is new since last time
                if (old.Count > 0)
                    foreach (string exe in now)
                    {
                        string one = exe;
                        if (!one.EndsWith(@"\FiveM.exe", StringComparison.OrdinalIgnoreCase) && !old.Exists(delegate(string y) { return string.Equals(one, y, StringComparison.OrdinalIgnoreCase); })) AddUnique(LastNew, one);
                    }
                // builds that can still be capped, whose file is not on disk now
                List<string> candidates = new List<string>();
                if (adlApps != null)
                {
                    foreach (Adl.AppInfo ra in adlApps)
                        if (!string.IsNullOrEmpty(ra.File) && ExeRx.IsMatch(ra.File) && string.Equals((ra.Path ?? "").TrimEnd('*', '\\'), sub, StringComparison.OrdinalIgnoreCase)) AddUnique(candidates, Path.Combine(sub, ra.File));
                }
                else foreach (string exe in old) AddUnique(candidates, exe);
                List<string> keep = new List<string>(now);
                foreach (string exe in candidates)
                {
                    string one = exe;
                    if (keep.Exists(delegate(string y) { return string.Equals(one, y, StringComparison.OrdinalIgnoreCase); })) continue;
                    if (File.Exists(one)) continue;
                    if (Has(forgot, one)) continue;
                    AddUnique(keep, one);
                    if (onDisk.Exists(delegate(Build q) { return string.Equals(q.Exe, one, StringComparison.OrdinalIgnoreCase); })) continue;
                    Build b = new Build(); b.Exe = one; b.Dir = Path.GetDirectoryName(one); b.FileName = Path.GetFileName(one); b.Root = root; b.Missing = true;
                    if (adlApps != null)
                        foreach (Adl.AppInfo a in adlApps)
                            if (string.Equals(a.File, b.FileName, StringComparison.OrdinalIgnoreCase) && string.Equals((a.Path ?? "").TrimEnd('*', '\\'), b.Dir, StringComparison.OrdinalIgnoreCase)) b.AdlIndex = a.Index;
                    gone.Add(b);
                    AppLog.Debug("builds: not on disk now, but still known: " + one);
                }
                if (keep.Count > 0 && !SameList(old, keep)) { m[root] = keep; changed = true; }
            }
            if (forgotChanged) SaveForgotten(forgot);
            if (changed)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(MemoryFile));
                    List<string> lines = new List<string>();
                    foreach (KeyValuePair<string, List<string>> kv in m) foreach (string e in kv.Value) lines.Add(kv.Key + "\t" + e);
                    File.WriteAllLines(MemoryFile, lines.ToArray());
                }
                catch (Exception ex) { AppLog.Debug("builds: could not save the list: " + ex.Message); }
            }
            return gone;
        }

        public static List<Build> Builds(List<string> roots, List<Adl.AppInfo> adlApps)
        {
            List<Build> list = new List<Build>();
            foreach (string root in roots)
            {
                // FiveM.exe sits next to the FiveM.app folder. Radeon Software (and NVIDIA) have a profile for it too, so it gets a cap like the builds.
                string launcher = CacheTool.FiveMExe(root);
                if (launcher != null && !list.Exists(delegate(Build q) { return string.Equals(q.Exe, launcher, StringComparison.OrdinalIgnoreCase); }))
                {
                    Build lb = new Build(); lb.Exe = launcher; lb.Dir = Path.GetDirectoryName(launcher); lb.FileName = Path.GetFileName(launcher); lb.Root = root;
                    if (adlApps != null)
                        foreach (Adl.AppInfo la in adlApps)
                            if (string.Equals(la.File, lb.FileName, StringComparison.OrdinalIgnoreCase) && string.Equals((la.Path ?? "").TrimEnd('*', '\\'), lb.Dir, StringComparison.OrdinalIgnoreCase)) lb.AdlIndex = la.Index;
                    list.Add(lb);
                    AppLog.Debug("builds: launcher " + launcher + " (adl index " + lb.AdlIndex + ")");
                }
                string sub = Path.Combine(root, @"data\cache\subprocess");
                if (!Directory.Exists(sub)) { AppLog.Debug("builds: no subprocess folder in " + root); continue; }
                string[] files;
                try { files = Directory.GetFiles(sub, "FiveM*_GTAProcess.exe"); } catch (Exception ex) { AppLog.Debug("builds: cannot read " + sub + ": " + ex.Message); continue; }
                AppLog.Debug("builds: " + files.Length + " matching file(s) in " + sub);
                foreach (string f in files)
                {
                    string fn = Path.GetFileName(f);
                    if (!ExeRx.IsMatch(fn)) continue;
                    bool dup = false;
                    foreach (Build x in list) if (string.Equals(x.Exe, f, StringComparison.OrdinalIgnoreCase)) dup = true;
                    if (dup) continue;
                    Build b = new Build(); b.Exe = f; b.Dir = sub; b.FileName = fn; b.Root = root;
                    if (adlApps != null)
                        foreach (Adl.AppInfo a in adlApps)
                            if (string.Equals(a.File, fn, StringComparison.OrdinalIgnoreCase) && string.Equals((a.Path ?? "").TrimEnd('*', '\\'), sub, StringComparison.OrdinalIgnoreCase)) b.AdlIndex = a.Index;
                    list.Add(b);
                }
            }
            list.Sort(delegate(Build x, Build y) { return string.Compare(x.Exe, y.Exe, StringComparison.OrdinalIgnoreCase); });
            return list;
        }
    }

    // ---------------------------------------------------------------- the work
    // ---------------------------------------------------------------- the two cap values a build had before this tool last changed them ("Undo last change")
    static class CapMemory
    {
        public static string PathOverride = null;   // for tests
        static string FilePath { get { return PathOverride ?? Path.Combine(Path.GetDirectoryName(Blb.BackupDir), "caps-before.txt"); } }

        static Dictionary<string, string[]> Load()
        {
            Dictionary<string, string[]> d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length == 3 && p[0].Length > 0) d[p[0]] = new string[] { p[1], p[2] };
                    }
            }
            catch (Exception) { }
            return d;
        }

        static void Store(Dictionary<string, string[]> d)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string[]> kv in d) lines.Add(kv.Key + "\t" + kv.Value[0] + "\t" + kv.Value[1]);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception ex) { AppLog.Debug("could not save the old cap values: " + ex.Message); }
        }

        // {min, max} as Radeon wrote them, or null when this tool has not changed that build
        public static string[] Get(string exe)
        {
            string[] v;
            return Load().TryGetValue(exe, out v) ? v : null;
        }

        public static void Save(string exe, string min, string max)
        {
            Dictionary<string, string[]> d = Load();
            d[exe] = new string[] { min, max };
            Store(d);
            AppLog.Debug("old cap values saved for " + exe + ": min=[" + min + "] max=[" + max + "]");
        }

        public static void Remove(string exe)
        {
            Dictionary<string, string[]> d = Load();
            if (d.Remove(exe)) Store(d);
        }

        // forget the old cap of every build with this file name (a build whose profile was removed)
        public static int RemoveByFileName(string fileName)
        {
            Dictionary<string, string[]> d = Load();
            List<string> del = new List<string>();
            foreach (string k in d.Keys) if (string.Equals(Path.GetFileName(k), fileName, StringComparison.OrdinalIgnoreCase)) del.Add(k);
            foreach (string k in del) d.Remove(k);
            if (del.Count > 0) Store(d);
            return del.Count;
        }
    }

    // ---------------------------------------------------------------- NVIDIA: the frame cap lives in the driver's profile database (NvAPI "DRS")
    // The program asks nvapi64.dll (part of the NVIDIA driver) for the functions it needs. The function numbers, the struct sizes and the
    // setting number below were read from NVIDIA's own public header files (github.com/NVIDIA/nvapi: nvapi_interface.h, nvapi.h, NvApiDriverSettings.h).
    // NOT verified yet: that the old NVIDIA Control Panel and the new NVIDIA app show the same profile data, and if saving needs administrator rights.
    // Every call is checked: a struct version the driver does not know gives "incompatible struct version" before anything is written.
    interface ICapWorker
    {
        string Apply(Build b, int cap);
        string Revert(Build b, string wantMin, string wantMax);
    }

    class NvProfInfo { public string Name; public List<string> Apps = new List<string>(); public int AppCount; public int Settings; }   // a profile of the driver, as the clean-up sees it

    interface INvDrs : IDisposable
    {
        // every method returns an NvAPI status: 0 = OK, a negative number = an error (see NvApi.Describe)
        int Open();                                                 // start the driver library, open a session, load the profile database
        int FindApp(string exeName, out IntPtr profile);            // OK = this exe is already in a profile; NvApi.ExeNotFound = it is in none
        int FindProfile(string profileName, out IntPtr profile);    // OK, or NvApi.ProfileNotFound
        string NameOf(IntPtr profile);                              // the name of a profile, or null
        int CreateProfile(string profileName, out IntPtr profile);
        int AddApp(IntPtr profile, string exeName);
        int GetCap(IntPtr profile, out int fps, out bool inThisProfile);   // fps = -1: no limit at all. inThisProfile: the limit is set by this profile itself
        int SetCap(IntPtr profile, int fps);
        int DeleteCap(IntPtr profile);
        int Save();                                                 // makes the changes permanent in the driver's database
        int DumpSettings(string exeName, Action<string> log);       // READ ONLY: every setting (number, name, value) of the profile this exe belongs to
        int ProfileCount(out int count);                            // how many profiles the driver holds (all of them)
        int OurProfiles(string namePrefix, List<NvProfInfo> found); // READ ONLY: the user made profiles whose name starts with the prefix (name, programs, number of settings)
        int DeleteProfile(string profileName);                      // removes the profile with exactly this name (found again by name first). Save() makes it permanent
        int List(Action<string> log, bool all);                     // READ ONLY: every profile with its programs and its frame limit (all = also the predefined ones)
    }

    static class NvApi
    {
        // NvAPI_Status values (nvapi_lite_common.h)
        public const int Ok = 0, Error = -1, LibraryNotFound = -2, NoImplementation = -3, NotInitialized = -4, InvalidArgument = -5, DeviceNotFound = -6, EndEnumeration = -7, IncompatibleStructVersion = -9;
        public const int InvalidUserPrivilege = -137, SettingNotFound = -160, ProfileNotFound = -163, ProfileNameInUse = -164, ExeNotFound = -166, ExeAlreadyInUse = -167, AccessDenied = -175;
        public const uint FrlFpsId = 0x10835002;   // "Frame Rate Limiter" (NvApiDriverSettings.h: FRL_FPS_ID): a DWORD, 0 = off, 1 to 1023 = frames per second

        public static Func<INvDrs> Factory = delegate { return new NvDrsNative(); };
        public static string DbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"NVIDIA Corporation\Drs");
        public static string BackupTo = null;      // default: the Toolbox backup folder

        public static string Describe(int rc)
        {
            string n;
            switch (rc)
            {
                case 0: n = "OK"; break;
                case -1: n = "generic error"; break;
                case -2: n = "library not found"; break;
                case -3: n = "no implementation"; break;
                case -4: n = "NvAPI not initialized"; break;
                case -5: n = "invalid argument"; break;
                case -6: n = "no NVIDIA device found"; break;
                case -9: n = "incompatible struct version"; break;
                case -137: n = "needs administrator privileges"; break;
                case -160: n = "setting not found"; break;
                case -163: n = "profile not found"; break;
                case -164: n = "profile name in use"; break;
                case -166: n = "application not found in the profile"; break;
                case -167: n = "application already in use"; break;
                case -175: n = "access denied"; break;
                default: n = "unknown status"; break;
            }
            return n + " (NvAPI status " + rc + ")";
        }

        // Can the program talk to the NVIDIA driver profiles? (read only)
        public static bool Available()
        {
            try
            {
                using (INvDrs d = Factory())
                {
                    int rc = d.Open();
                    AppLog.Debug("NVIDIA driver profile database: " + Describe(rc));
                    return rc == Ok;
                }
            }
            catch (Exception ex) { AppLog.Debug("NVIDIA driver check failed: " + ex.Message); return false; }
        }

        // A copy of the driver's profile database files before the first change. If it cannot be copied the change still goes on:
        // the old limit is remembered for Undo, and only one setting of the FiveM builds is changed.
        public static void BackupDb(Action<string> log)
        {
            try
            {
                string dest = BackupTo ?? Blb.BackupDir;
                Directory.CreateDirectory(dest);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                int n = 0;
                foreach (string f in new string[] { "nvdrsdb0.bin", "nvdrsdb1.bin" })
                {
                    string src = Path.Combine(DbDir, f);
                    if (File.Exists(src)) { File.Copy(src, Path.Combine(dest, f + "." + stamp), true); n++; }
                }
                log(n > 0 ? "  Backup of the NVIDIA driver profile database saved (" + n + " file(s) in " + dest + ")."
                          : "  The NVIDIA driver profile database file was not found, so no copy was made (the old limit is still remembered for Undo).");
            }
            catch (Exception ex) { log("  Could not copy the NVIDIA driver profile database: " + ex.Message + " (going on: the old limit is remembered for Undo)."); }
        }
    }

    // The real driver, through nvapi64.dll
    class NvDrsNative : INvDrs
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FnQuery(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnCreateSession(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnSession(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnFindApp(IntPtr session, IntPtr appName, out IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnFindProfile(IntPtr session, IntPtr name, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnCreateProfile(IntPtr session, IntPtr info, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnCreateApp(IntPtr session, IntPtr profile, IntPtr app);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetSetting(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnSetSetting(IntPtr session, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnDeleteSetting(IntPtr session, IntPtr profile, uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnEnumProfiles(IntPtr session, uint index, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetProfileInfo(IntPtr session, IntPtr profile, IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnEnumApps(IntPtr session, IntPtr profile, uint start, ref uint count, IntPtr apps);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnEnumSettings(IntPtr session, IntPtr profile, uint start, ref uint count, IntPtr settings);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetSettingName(uint id, IntPtr nameBuf);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnGetNumProfiles(IntPtr session, out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnDeleteProfile(IntPtr session, IntPtr profile);

        // Struct sizes and offsets, from nvapi.h. All members are 4 bytes wide or arrays of them, so every offset is plain.
        public const int Str = 4096;                                      // NvAPI_UnicodeString: 2048 UTF-16 characters
        public const int SettingSize = 12320, SettingVer = SettingSize | (1 << 16);   // NVDRS_SETTING_V1 (pack 4); version 0x13020
        public const int SetId = 4100, SetType = 4104, SetLoc = 4108, SetCur = 8220;   // settingId, settingType, settingLocation, current value (u32)
        public const int ProfileSize = 4116, ProfileVer = ProfileSize | (1 << 16);    // NVDRS_PROFILE_V1; version 0x11014
        public const int ProfName = 4, ProfGpu = 4100;                    // profileName, gpuSupport bit flags
        public static readonly int[] AppSize = new int[] { 20492, 16396, 16392 };     // NVDRS_APPLICATION V4, V3, V2 (the newest the driver knows is used)
        public static readonly int[] AppVer = new int[] { 20492 | (4 << 16), 16396 | (3 << 16), 16392 | (2 << 16) };
        const int AppName = 8, AppFriendly = 8 + 4096;                    // appName, userFriendlyName

        FnQuery query;
        FnInit fInit; FnCreateSession fCreate; FnSession fDestroy, fLoad, fSave; FnFindApp fFindApp; FnFindProfile fFindProfile; FnCreateProfile fCreateProfile;
        FnCreateApp fCreateApp; FnGetSetting fGetSetting; FnSetSetting fSetSetting; FnDeleteSetting fDeleteSetting;
        FnEnumSettings fEnumSettings; FnGetSettingName fGetSettingName;   // only used by DumpSettings
        FnEnumProfiles fEnumProfiles; FnGetProfileInfo fGetProfileInfo; FnEnumApps fEnumApps; FnGetNumProfiles fGetNumProfiles;   // only used by List and the clean-up
        FnDeleteProfile fDeleteProfile;                                                                                                  // only used by the clean-up
        IntPtr session = IntPtr.Zero;
        int appVariant = 0;

        T Get<T>(uint id) where T : class
        {
            IntPtr p = query(id);
            if (p == IntPtr.Zero) return null;
            return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        int Load()
        {
            if (query != null) return NvApi.Ok;
            IntPtr lib = LoadLibraryW("nvapi64.dll");
            if (lib == IntPtr.Zero) return NvApi.LibraryNotFound;
            IntPtr q = GetProcAddress(lib, "nvapi_QueryInterface");
            if (q == IntPtr.Zero) return NvApi.NoImplementation;
            FnQuery fq = (FnQuery)Marshal.GetDelegateForFunctionPointer(q, typeof(FnQuery));
            query = fq;
            fInit = Get<FnInit>(0x0150e828); fCreate = Get<FnCreateSession>(0x0694d52e); fDestroy = Get<FnSession>(0xdad9cff8);
            fLoad = Get<FnSession>(0x375dbd6b); fSave = Get<FnSession>(0xfcbc7e14); fFindApp = Get<FnFindApp>(0xeee566b2);
            fFindProfile = Get<FnFindProfile>(0x7e4a9a0b); fCreateProfile = Get<FnCreateProfile>(0xcc176068); fCreateApp = Get<FnCreateApp>(0x4347a9de);
            fGetSetting = Get<FnGetSetting>(0x73bf8338); fSetSetting = Get<FnSetSetting>(0x577dd202); fDeleteSetting = Get<FnDeleteSetting>(0xe4a26362);
            fEnumSettings = Get<FnEnumSettings>(0xae3039da); fGetSettingName = Get<FnGetSettingName>(0xd61cbe6e);
            fEnumProfiles = Get<FnEnumProfiles>(0xbc371ee0); fGetProfileInfo = Get<FnGetProfileInfo>(0x61cd6fd6); fEnumApps = Get<FnEnumApps>(0x7fa2173a); fGetNumProfiles = Get<FnGetNumProfiles>(0x1dae4fbc);
            fDeleteProfile = Get<FnDeleteProfile>(0x17093206);
            if (fInit == null || fCreate == null || fDestroy == null || fLoad == null || fSave == null || fFindApp == null || fFindProfile == null || fCreateProfile == null
                || fCreateApp == null || fGetSetting == null || fSetSetting == null || fDeleteSetting == null) { query = null; return NvApi.NoImplementation; }
            return NvApi.Ok;
        }

        static IntPtr NewBuf(int size) { IntPtr p = Marshal.AllocHGlobal(size); Marshal.Copy(new byte[size], 0, p, size); return p; }
        static void PutStr(IntPtr buf, int offset, string text)
        {
            if (text == null) text = "";
            if (text.Length > 2047) text = text.Substring(0, 2047);
            byte[] b = Encoding.Unicode.GetBytes(text);
            Marshal.Copy(b, 0, IntPtr.Add(buf, offset), b.Length);
        }

        public int Open()
        {
            int rc = Load(); if (rc != 0) return rc;
            rc = fInit(); if (rc != 0) return rc;
            rc = fCreate(out session); if (rc != 0) { session = IntPtr.Zero; return rc; }
            rc = fLoad(session);
            if (rc != 0) { Dispose(); return rc; }
            return NvApi.Ok;
        }

        public int FindApp(string exeName, out IntPtr profile)
        {
            profile = IntPtr.Zero;
            IntPtr name = NewBuf(Str);
            try
            {
                PutStr(name, 0, exeName);
                int rc = NvApi.IncompatibleStructVersion;
                for (int i = 0; i < AppSize.Length; i++)
                {
                    IntPtr app = NewBuf(AppSize[i]);
                    try
                    {
                        Marshal.WriteInt32(app, 0, AppVer[i]);
                        IntPtr pr;
                        rc = fFindApp(session, name, out pr, app);
                        if (rc != NvApi.IncompatibleStructVersion)
                        {
                            if (rc == 0) profile = pr;
                            if (rc == 0 || rc == NvApi.ExeNotFound) appVariant = i;
                            return rc;
                        }
                    }
                    finally { Marshal.FreeHGlobal(app); }
                }
                return rc;
            }
            finally { Marshal.FreeHGlobal(name); }
        }

        public string NameOf(IntPtr profile)
        {
            if (fGetProfileInfo == null) return null;
            IntPtr info = NewBuf(ProfileSize);
            try { Marshal.WriteInt32(info, 0, ProfileVer); return fGetProfileInfo(session, profile, info) == 0 ? Marshal.PtrToStringUni(IntPtr.Add(info, ProfName)) : null; }
            finally { Marshal.FreeHGlobal(info); }
        }

        public int FindProfile(string profileName, out IntPtr profile)
        {
            profile = IntPtr.Zero;
            IntPtr name = NewBuf(Str);
            try { PutStr(name, 0, profileName); IntPtr pr; int rc = fFindProfile(session, name, out pr); if (rc == 0) profile = pr; return rc; }
            finally { Marshal.FreeHGlobal(name); }
        }

        public int CreateProfile(string profileName, out IntPtr profile)
        {
            profile = IntPtr.Zero;
            IntPtr info = NewBuf(ProfileSize);
            try
            {
                Marshal.WriteInt32(info, 0, ProfileVer);
                PutStr(info, ProfName, profileName);
                Marshal.WriteInt32(info, ProfGpu, 7);   // GeForce, Quadro and NVS
                IntPtr pr; int rc = fCreateProfile(session, info, out pr);
                if (rc == 0) profile = pr;
                return rc;
            }
            finally { Marshal.FreeHGlobal(info); }
        }

        public int AddApp(IntPtr profile, string exeName)
        {
            int rc = NvApi.IncompatibleStructVersion;
            for (int i = appVariant; i < AppSize.Length; i++)
            {
                IntPtr app = NewBuf(AppSize[i]);
                try
                {
                    Marshal.WriteInt32(app, 0, AppVer[i]);
                    PutStr(app, AppName, exeName);
                    PutStr(app, AppFriendly, "FiveM");
                    rc = fCreateApp(session, profile, app);
                    if (rc != NvApi.IncompatibleStructVersion) return rc;
                }
                finally { Marshal.FreeHGlobal(app); }
            }
            return rc;
        }

        public int GetCap(IntPtr profile, out int fps, out bool inThisProfile)
        {
            fps = -1; inThisProfile = false;
            IntPtr st = NewBuf(SettingSize);
            try
            {
                Marshal.WriteInt32(st, 0, SettingVer);
                int rc = fGetSetting(session, profile, NvApi.FrlFpsId, st);
                if (rc == NvApi.SettingNotFound) return NvApi.Ok;
                if (rc != 0) return rc;
                int loc = Marshal.ReadInt32(st, SetLoc);
                int val = Marshal.ReadInt32(st, SetCur);
                fps = val > 0 ? val : -1;
                inThisProfile = loc == 0 && val > 0;     // location 0 = NVDRS_CURRENT_PROFILE_LOCATION
                return NvApi.Ok;
            }
            finally { Marshal.FreeHGlobal(st); }
        }

        public int SetCap(IntPtr profile, int fps)
        {
            IntPtr st = NewBuf(SettingSize);
            try
            {
                Marshal.WriteInt32(st, 0, SettingVer);
                Marshal.WriteInt32(st, SetId, unchecked((int)NvApi.FrlFpsId));
                Marshal.WriteInt32(st, SetType, 0);      // NVDRS_DWORD_TYPE
                Marshal.WriteInt32(st, SetLoc, 0);       // NVDRS_CURRENT_PROFILE_LOCATION
                Marshal.WriteInt32(st, SetCur, fps);
                return fSetSetting(session, profile, st);
            }
            finally { Marshal.FreeHGlobal(st); }
        }

        public int DeleteCap(IntPtr profile)
        {
            int rc = fDeleteSetting(session, profile, NvApi.FrlFpsId);
            return rc == NvApi.SettingNotFound ? NvApi.Ok : rc;
        }

        // READ ONLY: what does the driver hold? Every profile with its programs and its frame limit. Shows what the Control Panel and the NVIDIA app should be able to show.
        public int List(Action<string> log, bool all)
        {
            if (fEnumProfiles == null || fGetProfileInfo == null || fEnumApps == null || fGetNumProfiles == null) return NvApi.NoImplementation;
            uint total;
            int rc = fGetNumProfiles(session, out total);
            if (rc != 0) return rc;
            log("profiles in the driver database: " + total);
            int predefined = 0, user = 0, shown = 0;
            for (uint i = 0; i < total + 4; i++)
            {
                IntPtr h;
                rc = fEnumProfiles(session, i, out h);
                if (rc == NvApi.EndEnumeration) break;
                if (rc != 0) { log("EnumProfiles failed at " + i + ": " + NvApi.Describe(rc)); return rc; }
                IntPtr info = NewBuf(ProfileSize);
                try
                {
                    Marshal.WriteInt32(info, 0, ProfileVer);
                    rc = fGetProfileInfo(session, h, info);
                    if (rc != 0) { log("GetProfileInfo failed for profile " + i + ": " + NvApi.Describe(rc)); continue; }
                    string name = Marshal.PtrToStringUni(IntPtr.Add(info, ProfName));
                    int gpu = Marshal.ReadInt32(info, ProfGpu), isPre = Marshal.ReadInt32(info, 4104), nApps = Marshal.ReadInt32(info, 4108), nSet = Marshal.ReadInt32(info, 4112);
                    if (isPre != 0) predefined++; else user++;
                    // the programs of this profile
                    List<string> apps = new List<string>();
                    if (nApps > 0)
                    {
                        uint want = (uint)Math.Min(nApps, 16);
                        for (int v = 0; v < AppSize.Length; v++)
                        {
                            uint cnt = want;
                            IntPtr arr = NewBuf(AppSize[v] * (int)cnt);
                            try
                            {
                                for (int k = 0; k < (int)cnt; k++) Marshal.WriteInt32(arr, k * AppSize[v], AppVer[v]);
                                int r2 = fEnumApps(session, h, 0, ref cnt, arr);
                                if (r2 == NvApi.IncompatibleStructVersion) continue;
                                if (r2 == 0) for (int k = 0; k < (int)cnt; k++) apps.Add(Marshal.PtrToStringUni(IntPtr.Add(arr, k * AppSize[v] + AppName)));
                                break;
                            }
                            finally { Marshal.FreeHGlobal(arr); }
                        }
                    }
                    int fps; bool own;
                    int rcCap = GetCap(h, out fps, out own);
                    bool mine = (name != null && name.IndexOf("FiveM", StringComparison.OrdinalIgnoreCase) >= 0);
                    foreach (string a in apps) if (a != null && (a.IndexOf("FiveM", StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("GTA", StringComparison.OrdinalIgnoreCase) >= 0)) mine = true;
                    if (all || isPre == 0 || mine)
                    {
                        shown++;
                        log((isPre != 0 ? "[predefined] " : "[user made]  ") + "\"" + name + "\" | programs " + nApps + ": " + string.Join(", ", apps.ToArray()) + (nApps > apps.Count ? ", ..." : "")
                            + " | settings " + nSet + " | gpu flags " + gpu + " | frame limit " + (rcCap == 0 && own ? fps.ToString() : "none of its own"));
                    }
                }
                finally { Marshal.FreeHGlobal(info); }
            }
            log("predefined profiles: " + predefined + ", user made profiles: " + user + ", lines shown: " + shown + (all ? " (all)" : " (user made profiles and anything with FiveM or GTA in it)"));
            return 0;
        }

        // READ ONLY: every setting of the profile this exe belongs to (the number, the name, the kind and the value). This is how we find out what the NVIDIA Control Panel
        // and the NVIDIA app write, for example when the preferred graphics processor of a program is changed.
        public int DumpSettings(string exeName, Action<string> log)
        {
            if (fEnumSettings == null) return NvApi.NoImplementation;
            IntPtr prof;
            int rc = FindApp(exeName, out prof);
            if (rc == NvApi.ExeNotFound) { log(exeName + ": is in no driver profile."); return 0; }
            if (rc != 0) return rc;
            string pname = "?";
            if (fGetProfileInfo != null)
            {
                IntPtr info = NewBuf(ProfileSize);
                try { Marshal.WriteInt32(info, 0, ProfileVer); if (fGetProfileInfo(session, prof, info) == 0) pname = Marshal.PtrToStringUni(IntPtr.Add(info, ProfName)); }
                finally { Marshal.FreeHGlobal(info); }
            }
            const int cap = 64;
            uint count = cap;
            IntPtr arr = NewBuf(SettingSize * cap);
            try
            {
                for (int k = 0; k < cap; k++) Marshal.WriteInt32(arr, k * SettingSize, SettingVer);
                rc = fEnumSettings(session, prof, 0, ref count, arr);
                if (rc == NvApi.EndEnumeration) { log(exeName + " => profile \"" + pname + "\": no settings."); return 0; }
                if (rc != 0) return rc;
                log(exeName + " => profile \"" + pname + "\": " + count + " setting(s)");
                for (int k = 0; k < (int)count && k < cap; k++)
                {
                    int o = k * SettingSize;
                    uint id = unchecked((uint)Marshal.ReadInt32(arr, o + SetId));
                    string name = Marshal.PtrToStringUni(IntPtr.Add(arr, o + 4));
                    if (string.IsNullOrEmpty(name) && fGetSettingName != null)
                    {
                        IntPtr nb = NewBuf(Str);
                        try { if (fGetSettingName(id, nb) == 0) name = Marshal.PtrToStringUni(nb); }
                        finally { Marshal.FreeHGlobal(nb); }
                    }
                    int type = Marshal.ReadInt32(arr, o + SetType), loc = Marshal.ReadInt32(arr, o + SetLoc);
                    string value;
                    if (type == 0) { int v = Marshal.ReadInt32(arr, o + SetCur); value = "DWORD 0x" + unchecked((uint)v).ToString("X8") + " (" + v + ")"; }
                    else if (type == 4) { long v = Marshal.ReadInt64(arr, o + SetCur); value = "QWORD 0x" + v.ToString("X16") + " (" + v + ")"; }
                    else if (type == 3) value = "WSTRING \"" + Marshal.PtrToStringUni(IntPtr.Add(arr, o + SetCur)) + "\"";
                    else if (type == 2) value = "STRING \"" + Marshal.PtrToStringAnsi(IntPtr.Add(arr, o + SetCur)) + "\"";
                    else { int len = Math.Min(Marshal.ReadInt32(arr, o + SetCur), 16); byte[] bytes = new byte[Math.Max(0, len)]; if (len > 0) Marshal.Copy(IntPtr.Add(arr, o + SetCur + 4), bytes, 0, len); value = "BINARY " + BitConverter.ToString(bytes); }
                    log("    id 0x" + id.ToString("X8") + " \"" + name + "\" | " + value + " | location " + (loc == 0 ? "this profile" : loc == 1 ? "global profile" : loc == 2 ? "base profile" : "default"));
                }
                return 0;
            }
            finally { Marshal.FreeHGlobal(arr); }
        }

        public int ProfileCount(out int count)
        {
            count = -1;
            if (fGetNumProfiles == null) return NvApi.NoImplementation;
            uint total; int rc = fGetNumProfiles(session, out total);
            if (rc == 0) count = (int)total;
            return rc;
        }

        // the programs of one profile (names as stored; at most 16 are read)
        List<string> AppsOf(IntPtr h, int nApps)
        {
            List<string> apps = new List<string>();
            if (nApps <= 0) return apps;
            uint want = (uint)Math.Min(nApps, 16);
            for (int v = 0; v < AppSize.Length; v++)
            {
                uint cnt = want;
                IntPtr arr = NewBuf(AppSize[v] * (int)cnt);
                try
                {
                    for (int k = 0; k < (int)cnt; k++) Marshal.WriteInt32(arr, k * AppSize[v], AppVer[v]);
                    int r2 = fEnumApps(session, h, 0, ref cnt, arr);
                    if (r2 == NvApi.IncompatibleStructVersion) continue;
                    if (r2 == 0) for (int k = 0; k < (int)cnt; k++) apps.Add(Marshal.PtrToStringUni(IntPtr.Add(arr, k * AppSize[v] + AppName)));
                    break;
                }
                finally { Marshal.FreeHGlobal(arr); }
            }
            return apps;
        }

        // READ ONLY: the user made profiles whose name starts with the prefix
        public int OurProfiles(string prefix, List<NvProfInfo> found)
        {
            if (fEnumProfiles == null || fGetProfileInfo == null || fEnumApps == null || fGetNumProfiles == null) return NvApi.NoImplementation;
            uint total;
            int rc = fGetNumProfiles(session, out total);
            if (rc != 0) return rc;
            for (uint i = 0; i < total + 4; i++)
            {
                IntPtr h;
                rc = fEnumProfiles(session, i, out h);
                if (rc == NvApi.EndEnumeration) break;
                if (rc != 0) return rc;
                IntPtr info = NewBuf(ProfileSize);
                try
                {
                    Marshal.WriteInt32(info, 0, ProfileVer);
                    if (fGetProfileInfo(session, h, info) != 0) continue;
                    string name = Marshal.PtrToStringUni(IntPtr.Add(info, ProfName));
                    int isPre = Marshal.ReadInt32(info, 4104), nApps = Marshal.ReadInt32(info, 4108), nSet = Marshal.ReadInt32(info, 4112);
                    if (isPre != 0 || name == null || !name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    NvProfInfo p = new NvProfInfo(); p.Name = name; p.Settings = nSet; p.AppCount = nApps;
                    p.Apps.AddRange(AppsOf(h, nApps));
                    found.Add(p);
                }
                finally { Marshal.FreeHGlobal(info); }
            }
            return 0;
        }

        public int DeleteProfile(string profileName)
        {
            if (fDeleteProfile == null) return NvApi.NoImplementation;
            IntPtr h; int rc = FindProfile(profileName, out h);
            if (rc != 0) return rc;
            return fDeleteProfile(session, h);
        }

        public int Save() { return fSave(session); }

        public void Dispose()
        {
            if (session != IntPtr.Zero)
            {
                try { fDestroy(session); } catch (Exception) { }
                session = IntPtr.Zero;
            }
        }
    }

    // Reading, applying and undoing the frame limit of a FiveM build in the NVIDIA driver profiles
    class NvWorker : ICapWorker
    {
        public const string ProfilePrefix = "FiveM Toolbox - ";
        public static string ProfileNameFor(string exeFileName) { return ProfilePrefix + Path.GetFileNameWithoutExtension(exeFileName); }   // one profile per build
        public const string FailRead = "Could not read the NVIDIA driver profiles";
        public const string FailChange = "Could not change the NVIDIA driver profile. Open the full log for details.";
        public const string FailAdmin = "Could not change the NVIDIA driver profile. Try again as administrator (the shield button at the top).";

        public Action<string> Log;
        public Action<string, double> Step;
        bool backedUp = false;
        void L(string t) { if (Log != null) Log(t); }
        void S(string text, double sub) { if (Step != null) Step(text, sub); }

        // for the grid: what limit does the driver have for this build now? (read only)
        public static string StatusOf(Build b, Action<string> log)
        {
            try
            {
                using (INvDrs d = NvApi.Factory())
                {
                    int rc = d.Open();
                    if (rc != 0) { log("NVIDIA: could not open the driver profiles for " + b.FileName + ": " + NvApi.Describe(rc)); return FailRead; }
                    IntPtr prof;
                    rc = d.FindApp(b.Exe, out prof);   // the FULL path: the driver then gives the profile it really applies to this program
                    if (rc == NvApi.ExeNotFound) { log("NVIDIA: " + b.FileName + " is in no driver profile yet (no frame limit)."); return "NVIDIA: no frame limit set"; }
                    if (rc != 0) { log("NVIDIA: could not look for " + b.FileName + ": " + NvApi.Describe(rc)); return FailRead; }
                    int fps; bool own;
                    rc = d.GetCap(prof, out fps, out own);
                    if (rc != 0) { log("NVIDIA: could not read the frame limit of " + b.FileName + ": " + NvApi.Describe(rc)); return FailRead; }
                    log("NVIDIA: " + b.FileName + ": profile \"" + d.NameOf(prof) + "\" found, frame limit " + (fps > 0 ? fps.ToString() : "none") + (fps > 0 && !own ? " (comes from the global setting, not from this profile)" : ""));
                    return own && fps > 0 ? "NVIDIA: frame limit " + fps + " FPS" : "NVIDIA: no frame limit set";
                }
            }
            catch (Exception ex) { log("NVIDIA: error while reading " + b.FileName + ": " + ex.Message); AppLog.Debug("NVIDIA read failed: " + ex); return FailRead; }
        }

        public string Apply(Build b, int cap) { return Change(b, cap, true); }
        public string Revert(Build b, string wantMin, string wantMax) { return Change(b, Val.First(wantMin), false); }   // -1 = there was no limit before

        string Fail(string step, int rc)
        {
            L("  NVIDIA driver error while " + step + ": " + NvApi.Describe(rc));
            return (rc == NvApi.InvalidUserPrivilege || rc == NvApi.AccessDenied) ? FailAdmin : FailChange;
        }

        string Change(Build b, int want, bool isApply)
        {
            try
            {
                S("Talking to the NVIDIA driver ...", 0.1); L(b.FileName + ": opening the NVIDIA driver profiles ...");
                int curOwn = -1, oldForUndo = -1;
                bool added = false;   // the build was put into a profile just now: that change MUST be saved, even when the limit in the profile is already right
                using (INvDrs d = NvApi.Factory())
                {
                    int rc = d.Open(); if (rc != 0) return Fail("opening the profile database", rc);
                    IntPtr prof;
                    rc = d.FindApp(b.Exe, out prof);   // the FULL path: the profile the driver really applies to this program (FiveM may have made its own "Cfx.re: ..." profile)
                    if (rc == NvApi.ExeNotFound)
                    {
                        if (!isApply) return "Nothing to undo: the cap is already back.";
                        if (b.Missing) { L("  This build is not on disk now and it is in no NVIDIA profile. No profile is made for a build that is gone."); return "Skipped: this build is not on disk now."; }
                        string pname = ProfileNameFor(b.FileName);
                        L("  This build is in no NVIDIA profile yet. It goes into the profile \"" + pname + "\".");
                        rc = d.FindProfile(pname, out prof);
                        if (rc == NvApi.ProfileNotFound) { rc = d.CreateProfile(pname, out prof); if (rc == 0) L("  Made the profile \"" + pname + "\"."); }
                        if (rc != 0) return Fail("finding or making the profile of this build", rc);
                        rc = d.AddApp(prof, b.FileName); if (rc != 0) return Fail("adding the build to the profile", rc);
                        added = true;
                    }
                    else if (rc != 0) return Fail("looking for the build", rc);
                    else L("  The driver uses the profile \"" + d.NameOf(prof) + "\" for this build. That profile is used.");
                    int cur; bool own;
                    rc = d.GetCap(prof, out cur, out own); if (rc != 0) return Fail("reading the frame limit", rc);
                    curOwn = own ? cur : -1;
                    oldForUndo = added ? -1 : curOwn;   // a build that was in no profile had no limit before
                    L("  NVIDIA says: " + (curOwn > 0 ? "frame limit " + curOwn + " FPS in this profile" : "no frame limit in this profile") + (cur > 0 && !own ? " (the global setting gives " + cur + ")" : ""));
                    if (curOwn == want && !added) return isApply ? "Already set to " + want + "." : "Nothing to undo: the cap is already back.";
                    if (!backedUp) { NvApi.BackupDb(L); backedUp = true; }
                    S("Writing the new frame limit ...", 0.5); L("  Writing the frame limit " + (want > 0 ? want + " FPS" : "(none)") + " ...");
                    rc = want > 0 ? d.SetCap(prof, want) : d.DeleteCap(prof); if (rc != 0) return Fail("writing the frame limit", rc);
                    rc = d.Save(); if (rc != 0) return Fail("saving the profile database", rc);
                }
                S("Checking what the driver kept ...", 0.8);
                using (INvDrs d2 = NvApi.Factory())
                {
                    int rc2 = d2.Open(); if (rc2 != 0) return Fail("opening the database again to check", rc2);
                    IntPtr p2; rc2 = d2.FindApp(b.Exe, out p2); if (rc2 != 0) return Fail("finding the build again to check", rc2);
                    int c2; bool o2; rc2 = d2.GetCap(p2, out c2, out o2); if (rc2 != 0) return Fail("reading the frame limit again to check", rc2);
                    int got = o2 ? c2 : -1;
                    L("  Checked: the driver now has " + (got > 0 ? got + " FPS" : "no limit") + " for this build.");
                    if (got != want) { L("  The driver did not keep the new value (wanted " + want + ", it has " + got + ")."); return FailChange; }
                }
                if (isApply) CapMemory.Save(b.Exe, "nv::" + oldForUndo + ";;", "nv::" + oldForUndo + ";;"); else CapMemory.Remove(b.Exe);
                return isApply ? "Set to " + want + "." : (want > 0 ? "Cap put back to " + want + " / " + want + "." : "Cap put back: no limit set.");
            }
            catch (Exception ex) { L("  Error: " + ex.Message); AppLog.Debug("NVIDIA change failed: " + ex); return FailChange; }
        }
    }

    // The same work as the Apply and Undo buttons, without a window: for a Claude that runs the program itself, and for the self test (with the pretend driver).
    // With the real driver (the default) this CHANGES the real NVIDIA driver profiles, exactly like the buttons.
    static class NvCli
    {
        // returns how many builds failed
        public static int Apply(int cap, List<Build> builds, Action<string> log)
        {
            NvWorker w = new NvWorker(); w.Log = log;
            int bad = 0;
            foreach (Build b in builds)
            {
                string res = w.Apply(b, cap);
                log(b.FileName + ": " + res);
                if (!(res.StartsWith("Set to") || res.StartsWith("Already set") || res.StartsWith("Skipped"))) bad++;
            }
            return bad;
        }

        public static int Undo(List<Build> builds, Action<string> log)
        {
            NvWorker w = new NvWorker(); w.Log = log;
            int bad = 0;
            foreach (Build b in builds)
            {
                string[] before = CapMemory.Get(b.Exe);
                if (before == null) { log(b.FileName + ": Nothing to undo: this tool has not changed it yet."); continue; }
                string res = w.Revert(b, before[0], before[1]);
                log(b.FileName + ": " + res);
                if (!(res.StartsWith("Cap put back") || res.StartsWith("Nothing to undo"))) bad++;
            }
            return bad;
        }
    }

    // The Windows setting "Graphics performance preference" per program (Settings > System > Display > Graphics).
    // It lives in HKCU\Software\Microsoft\DirectX\UserGpuPreferences: value name = the full path of the exe, data like "GpuPreference=2;"
    // (0 = let Windows decide, 1 = power saving = the integrated chip, 2 = high performance = the dedicated card). Other parts of the data, like "SwapEffectUpgradeEnable=1;", are kept.
    // Used when a PC has an integrated chip AND a dedicated card (a laptop, or a desktop with a chip in the processor): the frame cap of a driver only works when FiveM runs on that driver's card.
    static class GpuPref
    {
        public static string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";   // the self test uses another key
        public static string MemoryOverride = null;                                          // the self test uses another file
        const string None = "!none!";
        static string MemoryFile { get { return MemoryOverride ?? Path.Combine(Path.GetDirectoryName(Blb.BackupDir), "gpupref-before.txt"); } }

        // an integrated chip and a dedicated card are both there
        public static bool Wanted(List<Gpu> gpus)
        {
            bool integrated = false, dedicated = false;
            foreach (Gpu g in gpus) { if (g.Kind == "integrated") integrated = true; else if (g.Kind == "dedicated") dedicated = true; }
            return integrated && dedicated;
        }

        public static string Raw(string exe)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath)) { return k == null ? null : k.GetValue(exe) as string; } }
            catch (Exception) { return null; }
        }

        public static int PrefOf(string raw)
        {
            if (raw == null) return -1;
            Match m = Regex.Match(raw, @"GpuPreference=(\d+);?", RegexOptions.IgnoreCase);
            int v;
            return m.Success && int.TryParse(m.Groups[1].Value, out v) ? v : -1;
        }

        // the data with GpuPreference set, everything else kept
        public static string With(string raw, int pref)
        {
            string t = raw ?? "";
            if (Regex.IsMatch(t, @"GpuPreference=\d+;?", RegexOptions.IgnoreCase)) return Regex.Replace(t, @"GpuPreference=\d+;?", "GpuPreference=" + pref + ";", RegexOptions.IgnoreCase);
            if (t.Length > 0 && !t.EndsWith(";")) t += ";";
            return t + "GpuPreference=" + pref + ";";
        }

        static Dictionary<string, string> Load()
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(MemoryFile))
                    foreach (string line in File.ReadAllLines(MemoryFile))
                    {
                        int i = line.IndexOf('\t');
                        if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                    }
            }
            catch (Exception) { }
            return d;
        }

        static void Store(Dictionary<string, string> d)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MemoryFile));
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in d) lines.Add(kv.Key + "\t" + kv.Value);
                File.WriteAllLines(MemoryFile, lines.ToArray());
            }
            catch (Exception ex) { AppLog.Debug("could not save the old graphics preference: " + ex.Message); }
        }

        public static bool HasMemory(string exe) { return Load().ContainsKey(exe); }

        public static List<string> ValueNames()
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath)) { return k == null ? new List<string>() : new List<string>(k.GetValueNames()); } }
            catch (Exception) { return new List<string>(); }
        }

        // Remove one entry from the Windows graphics list. The old data is written down first (gpupref-removed.txt, next to the backups). true = it is gone.
        public static bool RemoveEntry(string exe, Action<string> log)
        {
            string name = Path.GetFileName(exe);
            try
            {
                string raw = Raw(exe);
                if (raw == null) return true;
                string dir = MemoryOverride != null ? Path.GetDirectoryName(MemoryOverride) : Blb.BackupDir;
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "gpupref-removed.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + exe + "\t" + raw + "\r\n");
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, true)) { if (k == null) return false; k.DeleteValue(exe, false); }
                Dictionary<string, string> mem = Load();
                if (mem.Remove(exe)) Store(mem);
                bool gone = Raw(exe) == null;
                log(gone ? "  Windows graphics list: removed the entry of " + exe + " (the old data was [" + raw + "], written down in gpupref-removed.txt)." : "  Windows graphics list: could not remove the entry of " + exe + ".");
                return gone;
            }
            catch (Exception ex) { log("  Windows graphics list: the entry of " + name + " could not be removed: " + ex.Message); AppLog.Debug("gpu preference remove failed: " + ex); return false; }
        }

        // Put the program on the dedicated card (pref 2). The old data is remembered for Undo. true = the program has that preference afterwards.
        public static bool Set(string exe, int pref, Action<string> log)
        {
            string name = Path.GetFileName(exe);
            try
            {
                string raw = Raw(exe);
                if (PrefOf(raw) == pref) { log("  " + name + ": Windows already has graphics preference " + pref + " for it (the data in the list is [" + raw + "])."); return true; }
                Dictionary<string, string> mem = Load();
                mem[exe] = raw ?? None;
                Store(mem);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (k == null) { log("  " + name + ": could not open the Windows graphics preference list."); return false; }
                    k.SetValue(exe, With(raw, pref), RegistryValueKind.String);
                }
                bool ok = PrefOf(Raw(exe)) == pref;
                log(ok ? "  " + name + ": Windows graphics preference set to " + (pref == 2 ? "high performance (the dedicated card)" : pref.ToString()) + "." : "  " + name + ": could not confirm the Windows graphics preference.");
                if (!ok) { mem = Load(); mem.Remove(exe); Store(mem); }
                return ok;
            }
            catch (Exception ex) { log("  " + name + ": the Windows graphics preference could not be set: " + ex.Message); AppLog.Debug("gpu preference failed: " + ex); return false; }
        }

        // Put back what the program had before the last change of this tool. true = something was put back.
        public static bool Undo(string exe, Action<string> log)
        {
            string name = Path.GetFileName(exe);
            try
            {
                Dictionary<string, string> mem = Load();
                string old;
                if (!mem.TryGetValue(exe, out old)) return false;
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (k == null) { log("  " + name + ": could not open the Windows graphics preference list."); return false; }
                    if (old == None) k.DeleteValue(exe, false); else k.SetValue(exe, old, RegistryValueKind.String);
                }
                mem.Remove(exe);
                Store(mem);
                log("  " + name + ": Windows graphics preference put back" + (old == None ? " (it had none)." : " to: " + old));
                return true;
            }
            catch (Exception ex) { log("  " + name + ": the Windows graphics preference could not be put back: " + ex.Message); return false; }
        }
    }

    // ---------------------------------------------------------------- "Clean up old entries": what this tool left behind in the NVIDIA driver and in the Windows graphics list
    class TidyItem
    {
        public string Kind;                    // "nvidia" (a profile of the driver) or "winlist" (an entry of the Windows graphics list)
        public string Name;                    // the profile name, or the full path of the exe in the Windows list
        public string Why;                     // one of the Tidy.Why texts (English, translated when it is shown)
        public List<string> Apps = new List<string>();
    }

    static class Tidy
    {
        public const string WhyGone = "the build is not on disk", WhyEmpty = "it holds no settings", WhyIgnored = "the driver uses another profile for this build", WhyFile = "the file is not on disk";
        public const string FailWin = "Could not remove an entry of the Windows graphics list. Open the full log for details.";
        static readonly Regex BuildExeRx = new Regex(@"(^|[\\/])FiveM(_b\d+)?_GTAProcess\.exe$", RegexOptions.IgnoreCase);

        static string FileOf(string app) { return Path.GetFileName((app ?? "").Replace('/', '\\')); }
        static string Problem(int rc) { return (rc == NvApi.InvalidUserPrivilege || rc == NvApi.AccessDenied) ? NvWorker.FailAdmin : NvWorker.FailChange; }

        // READ ONLY. Only what this tool made: profiles called "FiveM Toolbox - ...", and entries of the Windows graphics list for FiveM game programs that carry a GpuPreference and whose file is gone.
        // A profile of ours is old when (1) none of its programs is a build on disk now, (2) it holds no settings, or (3) the driver uses another profile for every build in it (FiveM made its own).
        // Never touched: profiles of FiveM itself ("Cfx.re: ..."), of other programs and of the NVIDIA app, entries of other programs, entries on a drive that is not there now.
        // refuse: "build" = no FiveM build is on disk (nothing can be judged), "driver" = the NVIDIA profiles could not be read; null = fine.
        public static List<TidyItem> Look(List<Build> builds, bool nvidia, Action<string> log, out string refuse)
        {
            refuse = null;
            List<TidyItem> items = new List<TidyItem>();
            List<Build> onDisk = builds.FindAll(delegate(Build x) { return !x.Missing; });
            if (nvidia)
            {
                if (onDisk.Count == 0) { refuse = "build"; log("Clean up: no FiveM build is on disk, so nothing can be judged."); return items; }
                using (INvDrs d = NvApi.Factory())
                {
                    int rc = d.Open();
                    List<NvProfInfo> ours = new List<NvProfInfo>();
                    if (rc == 0) rc = d.OurProfiles(NvWorker.ProfilePrefix, ours);
                    if (rc != 0) { refuse = "driver"; log("Clean up: could not read the NVIDIA driver profiles: " + NvApi.Describe(rc)); return items; }
                    log("Clean up: " + ours.Count + " profile(s) made by this tool in the NVIDIA driver.");
                    foreach (NvProfInfo p in ours)
                    {
                        if (p.AppCount > p.Apps.Count) { log("  \"" + p.Name + "\": more programs than can be listed, left alone."); continue; }
                        List<Build> mine = new List<Build>();
                        foreach (string a in p.Apps) foreach (Build ob in onDisk) if (string.Equals(ob.FileName, FileOf(a), StringComparison.OrdinalIgnoreCase)) mine.Add(ob);
                        string why = null;
                        if (mine.Count == 0) why = WhyGone;
                        else if (p.Settings == 0) why = WhyEmpty;
                        else
                        {
                            bool shadowed = true;
                            foreach (Build mb in mine)
                            {
                                IntPtr eff; string en = null;
                                if (d.FindApp(mb.Exe, out eff) == 0) en = d.NameOf(eff);
                                if (en == null || string.Equals(en, p.Name, StringComparison.OrdinalIgnoreCase)) { shadowed = false; break; }
                            }
                            if (shadowed) why = WhyIgnored;
                        }
                        if (why == null) { log("  \"" + p.Name + "\": in use, stays."); continue; }
                        TidyItem it = new TidyItem(); it.Kind = "nvidia"; it.Name = p.Name; it.Why = why; it.Apps.AddRange(p.Apps);
                        items.Add(it);
                        log("  \"" + p.Name + "\": old (" + why + ").");
                    }
                }
            }
            foreach (string v in GpuPref.ValueNames())
            {
                if (!BuildExeRx.IsMatch(v)) continue;
                if (GpuPref.PrefOf(GpuPref.Raw(v)) < 0) continue;             // only entries with a graphics preference (what this tool writes)
                string root = null; try { root = Path.GetPathRoot(v); } catch (Exception) { }
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { log("  Windows graphics list: " + v + " is on a drive that is not there now, left alone."); continue; }
                if (File.Exists(v)) continue;
                TidyItem wi = new TidyItem(); wi.Kind = "winlist"; wi.Name = v; wi.Why = WhyFile;
                items.Add(wi);
                log("  Windows graphics list: " + v + " is old (" + WhyFile + "), the data is [" + GpuPref.Raw(v) + "].");
            }
            return items;
        }

        // Removes what Look found. A copy of the driver database is saved first. Every profile is checked again in a NEW session afterwards.
        // Returns null when all is well, else a text for the user (English, translated by Loc.TS). removed = how many entries are really gone.
        public static string Remove(List<TidyItem> items, Action<string> log, out int removed)
        {
            removed = 0;
            string problem = null;
            List<TidyItem> nv = items.FindAll(delegate(TidyItem t) { return t.Kind == "nvidia"; });
            if (nv.Count > 0)
            {
                NvApi.BackupDb(log);
                List<TidyItem> done = new List<TidyItem>();
                int before = -1, after = -1;
                using (INvDrs d = NvApi.Factory())
                {
                    int rc = d.Open();
                    if (rc != 0) { log("  NVIDIA driver error while opening the profile database: " + NvApi.Describe(rc)); problem = Problem(rc); }
                    else
                    {
                        d.ProfileCount(out before);
                        foreach (TidyItem it in nv)
                        {
                            rc = d.DeleteProfile(it.Name);
                            if (rc == 0) done.Add(it);
                            else { log("  NVIDIA driver error while removing the profile \"" + it.Name + "\": " + NvApi.Describe(rc)); if (problem == null) problem = Problem(rc); }
                        }
                        if (done.Count > 0)
                        {
                            rc = d.Save();
                            if (rc != 0) { log("  NVIDIA driver error while saving the profile database: " + NvApi.Describe(rc)); if (problem == null) problem = Problem(rc); done.Clear(); }
                        }
                    }
                }
                if (done.Count > 0)
                {
                    List<TidyItem> really = new List<TidyItem>();
                    using (INvDrs d2 = NvApi.Factory())
                    {
                        int rc2 = d2.Open();
                        if (rc2 != 0) { log("  Could not open the profile database again to check: " + NvApi.Describe(rc2)); if (problem == null) problem = NvWorker.FailChange; }
                        else
                        {
                            d2.ProfileCount(out after);
                            foreach (TidyItem it in done)
                            {
                                IntPtr hp;
                                if (d2.FindProfile(it.Name, out hp) == NvApi.ProfileNotFound) really.Add(it);
                                else log("  The driver still has the profile \"" + it.Name + "\" after saving.");
                            }
                            if (really.Count != done.Count) { if (problem == null) problem = NvWorker.FailChange; }
                            else if (before >= 0 && after >= 0 && after != before - really.Count) { log("  The number of profiles is not what it should be (before " + before + ", after " + after + ", removed " + really.Count + ")."); if (problem == null) problem = NvWorker.FailChange; }
                        }
                    }
                    done = really;
                }
                foreach (TidyItem it in done)
                {
                    log("  Removed the NVIDIA profile \"" + it.Name + "\".");
                    foreach (string a in it.Apps) CapMemory.RemoveByFileName(FileOf(a));
                }
                removed += done.Count;
            }
            foreach (TidyItem wi in items)
            {
                if (wi.Kind != "winlist") continue;
                if (GpuPref.RemoveEntry(wi.Name, log)) removed++; else if (problem == null) problem = FailWin;
            }
            return problem;
        }
    }

    class Worker : ICapWorker
    {
        public Action<string> Log;
        public Action<string, double> Step;      // (what it is doing, how far 0..1 inside this build)
        bool backedUp = false;
        void S(string text, double sub) { if (Step != null) Step(text, sub); }

        static string Md5(byte[] b)
        {
            try { using (System.Security.Cryptography.MD5 m = System.Security.Cryptography.MD5.Create()) return BitConverter.ToString(m.ComputeHash(b)).Replace("-", ""); }
            catch (Exception) { return "?"; }
        }

        void Backup(byte[] data)
        {
            if (backedUp) return;
            Directory.CreateDirectory(Blb.BackupDir);
            string p = Path.Combine(Blb.BackupDir, "User.blb." + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            File.WriteAllBytes(p, data);
            Log("Backup saved: " + p);
            backedUp = true;
        }

        bool Matches(Build b, int cap)
        {
            Adl.ProfileValues v = Adl.Read(b.FileName, b.Dir);
            return v.Found && Val.First(v.Min) == cap && Val.First(v.Max) == cap;
        }

        bool WaitFor(Build b, Func<Adl.ProfileValues, bool> done)
        {
            S("Waiting for AMD to load the new value ...", 0.8); Log("  Waiting for Radeon Software to start and AMD to load the new value (up to about 45 seconds) ...");
            Thread.Sleep(8000);
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    Adl.ProfileValues v = Adl.Read(b.FileName, b.Dir);
                    if (v.Found && done(v)) { Log("  AMD now reports cap " + Val.First(v.Min) + " / " + Val.First(v.Max) + " for this build."); return true; }
                    Log("  AMD still reports cap " + Val.First(v.Min) + " / " + Val.First(v.Max) + " (check " + (i + 1) + " of 12)"); S("Waiting for AMD to load the new value ...", 0.8 + 0.18 * (i + 1) / 12.0);
                }
                catch (Exception ex) { Log("  AMD library not ready yet: " + ex.Message); }
                Thread.Sleep(3000);
            }
            return false;
        }

        // Returns a one line result for the build.
        public string Apply(Build b, int cap)
        {
            return Change(b, delegate(string name, string old, int len) { return Val.Make(old, cap, len); },
                delegate(Adl.ProfileValues v) { return Val.First(v.Min) == cap && Val.First(v.Max) == cap; },
                "Set to " + cap + ".", "Already set to " + cap + ".", true);
        }

        // Puts the two cap values back to what they were before the last change this tool made.
        public string Revert(Build b, string wantMin, string wantMax)
        {
            return Change(b, delegate(string name, string old, int len) { string t = name == "Chil_MinFRate" ? wantMin : wantMax; return Encoding.Unicode.GetByteCount(t) <= len ? t : null; },
                delegate(Adl.ProfileValues v) { return v.Min == wantMin && v.Max == wantMax; },
                "Cap put back to " + Val.First(wantMin) + " / " + Val.First(wantMax) + ".", "Nothing to undo: the cap is already back.", false);
        }

        // The Radeon file is changed in the same careful way for a new cap and for putting an old cap back.
        string Change(Build b, Func<string, string, int, string> make, Func<Adl.ProfileValues, bool> done, string okText, string alreadyText, bool isApply)
        {
            S("Asking AMD what is set now ...", 0.05); Log(b.FileName + ": asking AMD what is set now ...");
            Adl.ProfileValues cur = Adl.Read(b.FileName, b.Dir);
            if (!cur.Found) return "Not in Radeon Software yet. Add it there once (Gaming > Games) and turn Radeon Chill on. Then run again.";
            Log("  AMD says: Radeon Chill " + (Val.First(cur.Pf) == 1 ? "on" : "off") + ", cap now " + Val.First(cur.Min) + " / " + Val.First(cur.Max));
            if (Val.First(cur.Pf) != 1) return "Radeon Chill is off for this build. Turn it on in Radeon Software once. This tool only changes the frame cap.";
            if (Val.First(cur.Min) < 0 || Val.First(cur.Max) < 0) return "No cap values yet. Open this build in Radeon Software and move the Chill sliders once.";
            if (done(cur)) return alreadyText;

            bool wasRunning = Radeon.Running();
            if (wasRunning) Radeon.Capture();
            S("Closing Radeon Software ...", 0.2); Log("  Closing Radeon Software (it would write its old settings back otherwise) ...");
            Radeon.Stop();
            string result = null;
            try
            {
                byte[] orig = File.ReadAllBytes(Blb.Path);
                AppLog.Debug("profile file before: " + orig.Length + " bytes, md5 " + Md5(orig));
                Backup(orig);
                List<Block> blocks = Blb.Layout(orig);
                S("Reading Radeon's profile file ...", 0.35); Log("  Read Radeon's profile file: " + blocks.Count + " game profiles.");
                List<int> cands = new List<int>();
                for (int i = 0; i < blocks.Count; i++)
                    if (blocks[i].Min == cur.Min && blocks[i].Max == cur.Max && blocks[i].Pf == cur.Pf) cands.Add(i);
                AppLog.Debug("wanted values: pf=" + cur.Pf + " min=" + cur.Min + " max=" + cur.Max + " | adl index of this build: " + b.AdlIndex + " | matching profiles: " + string.Join(",", cands.ConvertAll<string>(delegate(int x) { return x.ToString(); }).ToArray()));
                foreach (int ci in cands) AppLog.Debug("  profile " + ci + ": bytes " + blocks[ci].Lo + "-" + blocks[ci].Hi + ", min slot " + blocks[ci].MinLen + " bytes, max slot " + blocks[ci].MaxLen + " bytes");
                if (cands.Count == 0) { result = "Could not find this profile in the Radeon file. Nothing was changed."; }
                else
                {
                    int guess = b.AdlIndex;
                    cands.Sort(delegate(int x, int y) { return Math.Abs(x - guess).CompareTo(Math.Abs(y - guess)); });
                    if (cands.Count > 8) cands = cands.GetRange(0, 8);
                    Log("  " + cands.Count + " profile(s) look the same as this build. Trying the most likely one first, and undoing any wrong guess.");
                    foreach (int c in cands)
                    {
                        byte[] work = (byte[])orig.Clone();
                        try { Blb.Patch(work, blocks[c], make); } catch (Exception ex) { Log("  skip profile " + c + ": " + ex.Message); continue; }
                        S("Writing the new cap ...", 0.5); Log("  Trying profile " + c + ": writing the new cap into Radeon's profile file ...");
                        File.WriteAllBytes(Blb.Path, work);
                        Blb.BumpReload();
                        S("Starting Radeon Software again ...", 0.65);
                        Log("  Starting Radeon Software again ...");
                        Radeon.Start();
                        if (WaitFor(b, done))
                        {
                            result = okText;
                            if (isApply) CapMemory.Save(b.Exe, cur.Min, cur.Max); else CapMemory.Remove(b.Exe);
                            try { byte[] after = File.ReadAllBytes(Blb.Path); AppLog.Debug("profile file after: " + after.Length + " bytes, md5 " + Md5(after)); } catch (Exception) { }
                            break;
                        }
                        S("Undoing a wrong guess ...", 0.7); Log("  AMD did not show the new cap for this build, so profile " + c + " belongs to another game. Undoing it (putting the old file back) ...");
                        Radeon.Stop();
                        File.WriteAllBytes(Blb.Path, orig);
                        Blb.BumpReload();
                    }
                    if (result == null) result = "Could not confirm which profile belongs to this build. Nothing was changed.";
                }
            }
            catch (Exception ex) { result = "Error: " + ex.Message; AppLog.Debug("exception in Apply: " + ex); }
            finally
            {
                if (!Radeon.Running()) { Log("  Starting Radeon Software again ..."); try { Radeon.Start(); } catch (Exception) { } }
            }
            return result;
        }
    }

    // ---------------------------------------------------------------- small settings file (theme, start window)
    static class Settings
    {
        static string FilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Blb.BackupDir), "settings.txt"); }
        }

        public static string Get(string key, string def)
        {
            try
            {
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath))
                        if (line.StartsWith(key + "=")) return line.Substring(key.Length + 1).Trim();
            }
            catch (Exception) { }
            return def;
        }

        public static void Set(string key, string value)
        {
            try
            {
                List<string> lines = new List<string>();
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath))
                        if (!line.StartsWith(key + "=") && line.Trim().Length > 0) lines.Add(line);
                lines.Add(key + "=" + value);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------- dark mode, light mode, follow the system
    static class Theme
    {
        public static string Mode = "system";   // system, light, dark
        public static Color Bg1, Bg2, Bg3, Bg4, Border, Text, Text2, Primary, Good, Bad, Warn, Frame, AccentText;
        public static event Action Changed;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        static readonly Dictionary<Control, bool> hooked = new Dictionary<Control, bool>();

        public static void Init()
        {
            string m = Settings.Get("theme", "system");
            if (m == "system" || m == "light" || m == "dark") Mode = m;
            Palette();
            SystemEvents.UserPreferenceChanged += delegate(object s, UserPreferenceChangedEventArgs e)
            {
                if (Mode == "system") { Palette(); Raise(); }
            };
            AppLog.Debug("theme: mode=" + Mode + ", Windows is " + (SystemIsDark() ? "dark" : "light") + ", using " + (IsDark ? "dark" : "light"));
        }

        static void Raise() { Action h = Changed; if (h != null) h(); }

        public static void Force(string m) { Mode = m; Palette(); }

        public static void SetMode(string m)
        {
            Mode = m;
            Settings.Set("theme", m);
            Palette();
            AppLog.Debug("theme changed: mode=" + Mode + ", using " + (IsDark ? "dark" : "light"));
            Raise();
        }

        public static bool SystemIsDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int && (int)v == 0;
            }
            catch (Exception) { return false; }
        }

        public static bool IsDark { get { return Mode == "dark" || (Mode == "system" && SystemIsDark()); } }

        // Same greys as the Discord bot's panel; the accent colour is the icon's blue.
        static void Palette()
        {
            if (IsDark)
            {
                Bg1 = ColorTranslator.FromHtml("#1e2024"); Bg2 = ColorTranslator.FromHtml("#25272d");
                Bg3 = ColorTranslator.FromHtml("#2a2c31"); Bg4 = ColorTranslator.FromHtml("#34373d");
                Border = ColorTranslator.FromHtml("#3a3d44"); Text = ColorTranslator.FromHtml("#e8e9eb");
                Text2 = ColorTranslator.FromHtml("#9ba0a8"); Primary = ColorTranslator.FromHtml("#1177b5");
                Good = ColorTranslator.FromHtml("#4ec77c"); Bad = ColorTranslator.FromHtml("#ed7565"); Warn = ColorTranslator.FromHtml("#f0a53a");
                Frame = ColorTranslator.FromHtml("#141619"); AccentText = ColorTranslator.FromHtml("#59c4ff");
            }
            else
            {
                Bg1 = ColorTranslator.FromHtml("#f7f7f8"); Bg2 = ColorTranslator.FromHtml("#ffffff");
                Bg3 = ColorTranslator.FromHtml("#f4f4f5"); Bg4 = ColorTranslator.FromHtml("#ececef");
                Border = ColorTranslator.FromHtml("#d6d6da"); Text = ColorTranslator.FromHtml("#1a1a1a");
                Text2 = ColorTranslator.FromHtml("#6b7280"); Primary = ColorTranslator.FromHtml("#1177b5");
                Good = ColorTranslator.FromHtml("#15803d"); Bad = ColorTranslator.FromHtml("#b91c1c"); Warn = ColorTranslator.FromHtml("#b45309");
                Frame = ColorTranslator.FromHtml("#e3e5e9"); AccentText = ColorTranslator.FromHtml("#1177b5");
            }
        }

        public static void TitleBar(Form f)
        {
            try
            {
                int v = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
            }
            catch (Exception) { }
        }

        public static void Apply(Control root)
        {
            root.SuspendLayout();
            Walk(root);
            root.ResumeLayout();
            Form f = root as Form;
            if (f != null && f.IsHandleCreated) TitleBar(f);
            root.Invalidate(true);
        }

        // Scroll bars follow the theme too (Windows paints them dark when asked).
        static void Scroll(Control c)
        {
            Action apply = delegate
            {
                try { if (c.IsHandleCreated) SetWindowTheme(c.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); }
                catch (Exception) { }
            };
            if (c.IsHandleCreated) apply();
            if (!hooked.ContainsKey(c)) { hooked[c] = true; c.HandleCreated += delegate { apply(); }; }
            if (c is DataGridView) foreach (Control k in c.Controls) Scroll(k);
        }

        static void Walk(Control c)
        {
            string tag = c.Tag as string;
            if (c is ChromeForm) { c.BackColor = Frame; c.ForeColor = Text; }
            else if (c is Form) { c.BackColor = Bg1; c.ForeColor = Text; }
            else if (c is Card) { c.BackColor = Bg2; c.ForeColor = Text; c.Invalidate(); }
            else if (c is ChromeTab || c is GlyphButton || c is CaptionButton || c is Pill || c is ToolTile || c is RedXIcon || c is ActivityCard || c is DropBox || c is NumBox) { c.Invalidate(); }
            else if (c is GroupBox) { c.BackColor = Bg1; c.ForeColor = Text; }
            else if (c is IconButton) { c.Invalidate(); }
            else if (c is LinkLabel)
            {
                LinkLabel l = (LinkLabel)c;
                l.BackColor = Color.Transparent; l.LinkColor = IsDark ? ColorTranslator.FromHtml("#59c4ff") : Primary;
                l.ActiveLinkColor = Primary; l.VisitedLinkColor = l.LinkColor;
            }
            else if (c is Label)
            {
                c.BackColor = Color.Transparent;
                c.ForeColor = tag == "muted" ? Text2 : tag == "good" ? Good : tag == "bad" ? Bad : tag == "warn" ? Warn : tag == "accent" ? AccentText : Text;
            }
            else if (c is Button)
            {
                Button b = (Button)c;
                bool main = tag == "primary" || tag == "tabon";
                b.FlatStyle = FlatStyle.Flat; b.UseVisualStyleBackColor = false;
                b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = main ? Primary : Border;
                b.BackColor = main ? Primary : Bg3; b.ForeColor = main ? Color.White : Text;
                b.FlatAppearance.MouseOverBackColor = main ? Primary : Bg4;
            }
            else if (c is RichTextBox) { RichTextBox rb = (RichTextBox)c; rb.BackColor = Bg2; rb.ForeColor = Text; rb.BorderStyle = BorderStyle.None; Scroll(c); }
            else if (c is TextBox && (c.Tag as string) == "numtext") { c.BackColor = Bg3; c.ForeColor = Text; }
            else if (c is TextBox) { TextBox t = (TextBox)c; t.BackColor = Bg2; t.ForeColor = Text; t.BorderStyle = BorderStyle.FixedSingle; Scroll(c); }
            else if (c is NumericUpDown) { c.BackColor = Bg2; c.ForeColor = Text; }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c; cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Bg2; cb.ForeColor = Text;
                if (cb.DrawMode != DrawMode.OwnerDrawFixed) { cb.DrawMode = DrawMode.OwnerDrawFixed; cb.DrawItem += ComboDraw; }
            }
            else if (c is RadioButton || c is CheckBox) { c.BackColor = Color.Transparent; c.ForeColor = Text; }
            else if (c is DataGridView) { Grid((DataGridView)c); Scroll(c); }
            else if (c is TableLayoutPanel) { c.BackColor = Bg1; c.ForeColor = Text; }
            else if (c is Panel)
            {
                c.BackColor = tag == "frame" ? Frame : tag == "comboframe" ? Border : Bg1; c.ForeColor = Text;
                if (((Panel)c).AutoScroll) Scroll(c);
            }
            foreach (Control ch in c.Controls) Walk(ch);
        }

        static void ComboDraw(object sender, DrawItemEventArgs e)
        {
            ComboBox cb = (ComboBox)sender;
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (Brush bg = new SolidBrush(sel ? Bg4 : Bg2)) e.Graphics.FillRectangle(bg, e.Bounds);
            using (Brush fg = new SolidBrush(Text)) e.Graphics.DrawString(Convert.ToString(cb.Items[e.Index]), cb.Font, fg, e.Bounds.X + 2, e.Bounds.Y + 2);
        }

        static void Grid(DataGridView g)
        {
            g.BackgroundColor = Bg2; g.GridColor = Border; g.BorderStyle = BorderStyle.None;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; g.ColumnHeadersDefaultCellStyle.Padding = new Padding(Dpi.S(7), 0, 0, 0); g.DefaultCellStyle.Padding = new Padding(Dpi.S(8), 0, 0, 0);
            g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft; g.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Bg3; g.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Bg3; g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
            g.DefaultCellStyle.BackColor = Bg2; g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = Bg4; g.DefaultCellStyle.SelectionForeColor = Text;
        }

        // The border of a "card" panel (the cache tool uses these).
        public static void CardPaint(object sender, PaintEventArgs e)
        {
            Panel p = (Panel)sender;
            bool warn = (p.Tag as string) == "cardwarn";
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(warn ? Bad : Border, warn ? 1.6f : 1f))
                e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        }
    }

    // ---------------------------------------------------------------- a button with an icon, painted by the program so it follows the theme
    class IconButton : Button
    {
        // A character from the Windows icon font (Segoe Fluent Icons / Segoe MDL2 Assets), or "gauge" for a gauge drawn by the program.
        public string Glyph = "";
        bool down;
        Fader hv;
        static Font iconFont;
        static bool iconTried;

        public static Font IconFont
        {
            get
            {
                if (!iconTried)
                {
                    iconTried = true;
                    string family = "";
                    foreach (FontFamily f in FontFamily.Families)
                    {
                        if (f.Name == "Segoe Fluent Icons") { family = f.Name; break; }
                        if (f.Name == "Segoe MDL2 Assets") family = f.Name;
                    }
                    if (family.Length > 0) iconFont = new Font(family, 12f);
                }
                return iconFont;
            }
        }

        public IconButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            hv = new Fader(this);
        }

        protected override void OnMouseEnter(EventArgs e) { hv.To(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hv.To(0f); down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        static Color Shade(Color c, float f)
        {
            if (f >= 0) return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));
            f = -f;
            return Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
        }

        static GraphicsPath Rounded(Rectangle r, int d)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static void DrawGauge(Graphics g, Rectangle r, Color c)
        {
            float s = Math.Min(r.Width, r.Height) * 0.80f;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f + s * 0.10f, rad = s / 2f;
            using (Pen p = new Pen(c, Math.Max(Dpi.F(1.6f), s * 0.14f)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawArc(p, cx - rad, cy - rad, rad * 2f, rad * 2f, 150f, 240f);
                g.DrawLine(p, cx, cy, cx + (float)Math.Cos(-0.5) * rad * 0.8f, cy + (float)Math.Sin(-0.5) * rad * 0.8f);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            string tag = Tag as string;
            bool danger = tag == "danger";
            bool main = tag == "primary" || tag == "tabon" || danger;
            Color mainBase = danger ? Theme.Bad : Theme.Primary;
            Color back, fore, border;
            if (!Enabled) { back = Theme.Bg3; fore = Theme.Text2; border = Theme.Border; }
            else if (main)
            {
                back = down ? Shade(mainBase, -0.15f) : Fx.Mix(mainBase, Shade(mainBase, 0.12f), hv.Value);
                fore = Color.White; border = back;
            }
            else
            {
                back = down ? Theme.Bg2 : Fx.Mix(Theme.Bg3, Theme.Bg4, hv.Value);
                fore = Theme.Text; border = Fx.Mix(Theme.Border, Theme.Primary, hv.Value * 0.7f);
            }
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Rounded(r, Dpi.S(8)))
            {
                using (Brush b = new SolidBrush(back)) g.FillPath(b, path);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, path);
            }

            // icon and text, centred together
            Size ts = Size.Empty;
            if (!string.IsNullOrEmpty(Text)) ts = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            bool hasIcon = Glyph.Length > 0 && (Glyph == "gauge" || IconFont != null);
            int iconW = hasIcon ? Dpi.S(22) : 0;
            int gap = (hasIcon && ts.Width > 0) ? Dpi.S(8) : 0;
            int x = Math.Max(Dpi.S(8), (Width - (iconW + gap + ts.Width)) / 2);
            if (hasIcon)
            {
                Rectangle ir = new Rectangle(x, 0, iconW, Height);
                if (Glyph == "gauge") DrawGauge(g, ir, fore);
                else TextRenderer.DrawText(g, Glyph, IconFont, ir, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (ts.Width > 0)
            {
                Rectangle tr = new Rectangle(x + iconW + gap, 0, Math.Max(1, Width - (x + iconW + gap) - Dpi.S(6)), Height);
                TextRenderer.DrawText(g, Text, Font, tr, fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            if (Focused && ShowFocusCues)
                using (Pen pen = new Pen(fore)) { pen.DashStyle = DashStyle.Dot; g.DrawRectangle(pen, Dpi.S(3), Dpi.S(3), Width - Dpi.S(7), Height - Dpi.S(7)); }
        }
    }

    // ---------------------------------------------------------------- DNS tools
    class Nic { public string Id; public string Name; public string Type; public string Desc; public bool Default; public string Now; public string Speed = ""; }
    class DnsBackup { public string Id = ""; public string Name = ""; public string V4Mode = "auto"; public string V4List = ""; public string V6Mode = "auto"; public string V6List = ""; }

    static class DnsTool
    {
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Info.Folder); }
        }
        public static string BackupFile { get { return Path.Combine(Dir, "dns-backup.txt"); } }
        public static string ResultFile { get { return Path.Combine(Dir, "dns-result.txt"); } }

        static void Servers(string provider, out string[] v4, out string[] v6)
        {
            if (provider == "google") { v4 = new string[] { "8.8.8.8", "8.8.4.4" }; v6 = new string[] { "2001:4860:4860::8888", "2001:4860:4860::8844" }; }
            else if (provider == "both") { v4 = new string[] { "1.1.1.1", "8.8.8.8" }; v6 = new string[] { "2606:4700:4700::1111", "2001:4860:4860::8888" }; }
            else { v4 = new string[] { "1.1.1.1", "1.0.0.1" }; v6 = new string[] { "2606:4700:4700::1111", "2606:4700:4700::1001" }; }
        }

        // The fixed DNS list of one connection. Empty = "automatic" (comes from the router).
        static string[] StaticList(string id, bool v6)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + (v6 ? "Tcpip6" : "Tcpip") + @"\Parameters\Interfaces\" + id))
                {
                    if (k == null) return new string[0];
                    string s = (k.GetValue("NameServer") as string) ?? "";
                    return s.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
            catch (Exception) { return new string[0]; }
        }

        static string SpeedText(NetworkInterface ni)
        {
            try
            {
                long bps = ni.Speed;
                if (bps <= 0) return "";
                if (bps >= 1000000000L) return (bps / 1e9).ToString("0.#") + " Gbps";
                return (bps / 1000000L) + " Mbps";
            }
            catch (Exception) { return ""; }
        }

        static string NowText(NetworkInterface ni)
        {
            List<string> eff4 = new List<string>(), eff6 = new List<string>();
            try
            {
                foreach (IPAddress a in ni.GetIPProperties().DnsAddresses)
                {
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) eff4.Add(a.ToString());
                    else if (!a.ToString().StartsWith("fec0:")) eff6.Add(a.ToString());
                }
            }
            catch (Exception) { }
            string[] s4 = StaticList(ni.Id, false), s6 = StaticList(ni.Id, true);
            string t = s4.Length > 0 ? Loc.T("Fixed: ") + string.Join(", ", s4) : Loc.T("Automatic") + (eff4.Count > 0 ? " (" + string.Join(", ", eff4.ToArray()) + ")" : "");
            if (s6.Length > 0) t += "  |  " + Loc.T("IPv6 fixed: ") + string.Join(", ", s6);
            return t;
        }

        public static List<Nic> Adapters()
        {
            List<Nic> list = new List<Nic>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    if (!ni.Supports(NetworkInterfaceComponent.IPv4)) continue;
                    string type = ni.NetworkInterfaceType.ToString();
                    bool physical = type.IndexOf("Ethernet", StringComparison.OrdinalIgnoreCase) >= 0 || ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    bool virt = Regex.IsMatch(ni.Description + " " + ni.Name, "virtual|vmware|hyper-v|vethernet|tap-|wintun|openvpn|wireguard|vpn|tailscale|zerotier|bluetooth|surfshark|nord|proton|loopback", RegexOptions.IgnoreCase);
                    Nic n = new Nic();
                    n.Id = ni.Id; n.Name = ni.Name; n.Type = type; n.Desc = ni.Description; n.Default = physical && !virt; n.Now = NowText(ni); n.Speed = SpeedText(ni);
                    list.Add(n);
                    AppLog.Debug("network connection: " + n.Name + " | " + n.Desc + " | " + n.Type + " | " + n.Id + " | tick by default=" + n.Default + " | DNS " + n.Now);
                }
                catch (Exception ex) { AppLog.Debug("network connection skipped: " + ex.Message); }
            }
            return list;
        }

        // ---- the saved "before" state, kept where only an administrator can change it
        static List<DnsBackup> ReadBackup()
        {
            List<DnsBackup> list = new List<DnsBackup>();
            try
            {
                if (!File.Exists(BackupFile)) return list;
                foreach (string line in File.ReadAllLines(BackupFile))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 6) continue;
                    DnsBackup b = new DnsBackup();
                    b.Id = p[0]; b.Name = p[1]; b.V4Mode = p[2]; b.V4List = p[3]; b.V6Mode = p[4]; b.V6List = p[5];
                    list.Add(b);
                }
            }
            catch (Exception) { }
            return list;
        }

        static void WriteBackup(List<DnsBackup> list)
        {
            if (list.Count == 0) { if (File.Exists(BackupFile)) File.Delete(BackupFile); return; }
            Directory.CreateDirectory(Dir);
            StringBuilder sb = new StringBuilder();
            foreach (DnsBackup b in list) sb.AppendLine(b.Id + "|" + b.Name + "|" + b.V4Mode + "|" + b.V4List + "|" + b.V6Mode + "|" + b.V6List);
            File.WriteAllText(BackupFile, sb.ToString());
        }

        public static string BackupSummary()
        {
            List<DnsBackup> l = ReadBackup();
            if (l.Count == 0) return Loc.T("none yet (nothing was changed by this program)");
            List<string> parts = new List<string>();
            foreach (DnsBackup b in l) parts.Add(b.Name + (b.V4Mode == "auto" ? Loc.T(" (was automatic)") : string.Format(Loc.T(" (was {0})"), b.V4List.Replace(",", ", "))));
            return string.Join("; ", parts.ToArray());
        }

        static DnsBackup Capture(NetworkInterface ni)
        {
            DnsBackup b = new DnsBackup();
            string[] s4 = StaticList(ni.Id, false), s6 = StaticList(ni.Id, true);
            b.Id = ni.Id; b.Name = ni.Name;
            b.V4Mode = s4.Length > 0 ? "static" : "auto"; b.V4List = string.Join(",", s4);
            b.V6Mode = s6.Length > 0 ? "static" : "auto"; b.V6List = string.Join(",", s6);
            return b;
        }

        // ---- the commands (netsh). The same list is used for the real run and for the dry run.
        static List<string> Commands(string name, bool v6, string[] list)
        {
            string fam = v6 ? "ipv6" : "ip";
            List<string> c = new List<string>();
            if (list == null || list.Length == 0) { c.Add("interface " + fam + " set dnsservers name=\"" + name + "\" source=dhcp"); return c; }
            c.Add("interface " + fam + " set dnsservers name=\"" + name + "\" source=static address=" + list[0] + " register=primary validate=no");
            for (int i = 1; i < list.Length; i++) c.Add("interface " + fam + " add dnsservers name=\"" + name + "\" address=" + list[i] + " index=" + (i + 1) + " validate=no");
            return c;
        }

        static bool ValidAddresses(string[] list)
        {
            foreach (string a in list) { IPAddress ip; if (!IPAddress.TryParse(a, out ip)) return false; }
            return true;
        }

        public static bool IsAdmin()
        {
            try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
            catch (Exception) { return false; }
        }

        static int Exec(string file, string args, StringBuilder log, bool dry)
        {
            log.AppendLine("run: " + Path.GetFileName(file) + " " + args);
            if (dry) return 0;
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                string o = (p.StandardOutput.ReadToEnd() + " " + p.StandardError.ReadToEnd()).Trim().Replace("\r\n", " ");
                p.WaitForExit(30000);
                log.AppendLine("  exit code " + p.ExitCode + (o.Length > 0 ? ": " + o : ""));
                return p.ExitCode;
            }
        }

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        static extern int DnsFlushResolverCache();

        // How many names Windows remembers right now (-1 when it cannot be counted).
        static int CacheCount()
        {
            try
            {
                using (ManagementObjectSearcher q = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT Name FROM MSFT_DNSClientCache"))
                {
                    int n = 0;
                    foreach (ManagementBaseObject o in q.Get()) { n++; o.Dispose(); }
                    return n;
                }
            }
            catch (Exception) { return -1; }
        }

        // writes the names that are still in the cache (to see what Windows keeps or adds back)
        static void ListCache(StringBuilder log, int max)
        {
            try
            {
                using (ManagementObjectSearcher q = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT Name, Type, TimeToLive FROM MSFT_DNSClientCache"))
                {
                    int n = 0;
                    foreach (ManagementBaseObject o in q.Get())
                    {
                        if (n < max) log.AppendLine("  still listed: " + o["Name"] + " (type " + o["Type"] + ", ttl " + o["TimeToLive"] + ")");
                        n++; o.Dispose();
                    }
                }
            }
            catch (Exception) { }
        }

        // the number of names in the DNS cache (only an administrator can see it; -1 for everyone else)
        public static int CountNow() { return IsAdmin() ? CacheCount() : -1; }

        static string Show(int n) { return n < 0 ? "unknown" : n.ToString(); }

        // Clears the DNS cache and checks that it worked, up to 3 times (the first try does not always empty it).
        static int Flush(StringBuilder log, bool dry, bool extra)
        {
            string sys = Environment.SystemDirectory;
            if (dry)
            {
                log.AppendLine("run: ipconfig.exe /flushdns (up to 3 times, checked each time)");
                if (extra) log.AppendLine("run: nbtstat -R, nbtstat -RR, arp -d *, netsh interface ip delete destinationcache");
                return 0;
            }
            int before = CacheCount();
            log.AppendLine("DNS cache entries before: " + Show(before));
            bool clean = false;
            int lastCode = 0;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (attempt == 2)
                {
                    int ok = 0;
                    try { ok = DnsFlushResolverCache(); } catch (Exception ex) { log.AppendLine("  Windows flush call failed: " + ex.Message); }
                    lastCode = ok != 0 ? 0 : 1;
                    log.AppendLine("attempt 2 (Windows flush call): " + (ok != 0 ? "ok" : "failed"));
                }
                else lastCode = Exec(Path.Combine(sys, "ipconfig.exe"), "/flushdns", log, false);
                System.Threading.Thread.Sleep(700);
                int now = CacheCount();
                log.AppendLine("attempt " + attempt + ": entries now " + Show(now));
                bool low = now >= 0 && (now <= 3 || (before > 0 && now * 10 <= before));
                if (attempt >= 2 && (low || (now < 0 && lastCode == 0))) { clean = true; break; }
            }
            if (extra)
            {
                Exec(Path.Combine(sys, "nbtstat.exe"), "-R", log, false);
                Exec(Path.Combine(sys, "nbtstat.exe"), "-RR", log, false);
                Exec(Path.Combine(sys, "arp.exe"), "-d *", log, false);
                Exec(Path.Combine(sys, "netsh.exe"), "interface ip delete destinationcache", log, false);
            }
            log.AppendLine("DNS cache entries after: " + Show(CacheCount()));
            ListCache(log, 15);
            if (!clean && lastCode == 0)
            {
                log.AppendLine("note: some names are still listed, but Windows said the flush worked. Windows puts some names straight back (for example the ones from the hosts file).");
                clean = true;
            }
            return clean ? 0 : 4;
        }

        static List<NetworkInterface> Pick(string idList, StringBuilder log)
        {
            List<NetworkInterface> r = new List<NetworkInterface>();
            string[] ids = idList.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                bool want = false;
                foreach (string id in ids) if (string.Equals(id, ni.Id, StringComparison.OrdinalIgnoreCase)) want = true;
                if (!want) continue;
                if (ni.Name.Contains("\"")) { log.AppendLine("skipping " + ni.Name + " (odd name)"); continue; }
                r.Add(ni);
            }
            return r;
        }

        static int DoSet(string provider, string idList, StringBuilder log, bool dry)
        {
            if (provider != "cloudflare" && provider != "google" && provider != "both") { log.AppendLine("unknown DNS choice: " + provider); return 1; }
            string[] v4, v6; Servers(provider, out v4, out v6);
            if (!ValidAddresses(v4) || !ValidAddresses(v6)) { log.AppendLine("bad address list"); return 1; }
            List<NetworkInterface> nics = Pick(idList, log);
            if (nics.Count == 0) { log.AppendLine("none of the chosen connections exist"); return 1; }
            string netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
            List<DnsBackup> backup = ReadBackup();
            foreach (NetworkInterface ni in nics)
                if (!backup.Exists(delegate(DnsBackup b) { return string.Equals(b.Id, ni.Id, StringComparison.OrdinalIgnoreCase); }))
                {
                    DnsBackup nb = Capture(ni); backup.Add(nb);
                    log.AppendLine("saved original DNS of " + ni.Name + ": IPv4 " + nb.V4Mode + " [" + nb.V4List + "], IPv6 " + nb.V6Mode + " [" + nb.V6List + "]");
                }
            if (!dry) WriteBackup(backup);
            int bad = 0;
            foreach (NetworkInterface ni in nics)
            {
                log.AppendLine("connection: " + ni.Name);
                foreach (string c in Commands(ni.Name, false, v4)) if (Exec(netsh, c, log, dry) != 0) bad++;
                if (ni.Supports(NetworkInterfaceComponent.IPv6))
                    foreach (string c in Commands(ni.Name, true, v6)) if (Exec(netsh, c, log, dry) != 0) { bad++; log.AppendLine("  (IPv6 problems are not fatal)"); }
            }
            Flush(log, dry, false);
            if (!dry) foreach (NetworkInterface ni in nics) log.AppendLine("now: " + ni.Name + " -> " + NowText(ni));
            return bad == 0 ? 0 : 4;
        }

        static int DoRestore(StringBuilder log, bool dry)
        {
            List<DnsBackup> backup = ReadBackup();
            if (backup.Count == 0) { log.AppendLine("nothing saved, so there is nothing to undo"); return 3; }
            string netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
            List<DnsBackup> left = new List<DnsBackup>();
            int bad = 0;
            foreach (DnsBackup b in backup)
            {
                NetworkInterface found = null;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) if (string.Equals(ni.Id, b.Id, StringComparison.OrdinalIgnoreCase)) found = ni;
                if (found == null || found.Name.Contains("\"")) { log.AppendLine("connection " + b.Name + " is not there now, keeping its saved setting"); left.Add(b); continue; }
                string[] l4 = b.V4Mode == "static" ? b.V4List.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
                string[] l6 = b.V6Mode == "static" ? b.V6List.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
                if (!ValidAddresses(l4) || !ValidAddresses(l6)) { log.AppendLine("saved setting of " + b.Name + " looks wrong, skipping it"); left.Add(b); bad++; continue; }
                log.AppendLine("connection: " + found.Name + " back to IPv4 " + b.V4Mode + (l4.Length > 0 ? " [" + b.V4List + "]" : ""));
                int fail = 0;
                foreach (string c in Commands(found.Name, false, l4)) if (Exec(netsh, c, log, dry) != 0) fail++;
                if (found.Supports(NetworkInterfaceComponent.IPv6))
                    foreach (string c in Commands(found.Name, true, l6)) if (Exec(netsh, c, log, dry) != 0) log.AppendLine("  (IPv6 problems are not fatal)");
                if (fail > 0) { left.Add(b); bad++; }
            }
            if (!dry) WriteBackup(left);
            Flush(log, dry, false);
            if (!dry) foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel) log.AppendLine("now: " + ni.Name + " -> " + NowText(ni));
            return bad == 0 ? 0 : 4;
        }

        // Counts the names, writes the number for the window, waits for "go" or "cancel", then clears the cache. Exit code 5 = the user said no.
        static int FlushAsk(StringBuilder log, bool extra, string runId)
        {
            int count = CacheCount();
            log.AppendLine("DNS cache entries counted before asking: " + Show(count));
            try { File.WriteAllText(CountFile, "run=" + runId + "\r\ncount=" + count + "\r\n"); }
            catch (Exception ex) { log.AppendLine("could not write the count: " + ex.Message); }
            EventWaitHandle go, cancel;
            try
            {
                go = EventWaitHandle.OpenExisting(@"Local\FiveMToolbox.Go." + runId);
                cancel = EventWaitHandle.OpenExisting(@"Local\FiveMToolbox.Cancel." + runId);
            }
            catch (Exception ex) { log.AppendLine("could not open the answer events: " + ex.Message); return 1; }
            int which = WaitHandle.WaitAny(new WaitHandle[] { go, cancel }, 120000);
            if (which != 0) { log.AppendLine(which == 1 ? "the user chose not to clear the cache" : "no answer in time, nothing was changed"); return 5; }
            return Flush(log, false, extra);
        }

        // Runs inside the elevated copy of this program (or as a dry run that only writes the planned commands to a file).
        //   --dns-set <cloudflare|google|both> <id,id,...> <runid>     --dns-restore <runid>     --dns-flush <plain|extra> <runid>
        //   --dns-plan <set|restore> <provider> <id,id,...> <outfile>
        public static int HelperMain(string[] args)
        {
            StringBuilder log = new StringBuilder();
            int code = 1;
            string runId = "";
            bool dry = args[0] == "--dns-plan";
            string outFile = ResultFile;
            try
            {
                if (dry) { outFile = args[args.Length - 1]; }
                else { runId = args[args.Length - 1]; Directory.CreateDirectory(Dir); }
                log.AppendLine("run=" + runId);
                log.AppendLine("started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ", admin=" + IsAdmin());
                if (!dry && !IsAdmin()) { log.AppendLine("this step needs administrator rights"); code = 2; }
                else if (args[0] == "--dns-flush-ask") code = FlushAsk(log, args.Length > 2 && args[1] == "extra", runId);
                else if (args[0] == "--dns-flush") code = Flush(log, false, args.Length > 2 && args[1] == "extra");
                else if (args[0] == "--dns-set") code = DoSet(args[1], args[2], log, false);
                else if (args[0] == "--dns-restore") code = DoRestore(log, false);
                else if (args[0] == "--dns-plan") code = args[1] == "restore" ? DoRestore(log, true) : DoSet(args[2], args[3], log, true);
                else log.AppendLine("unknown command " + args[0]);
            }
            catch (Exception ex) { log.AppendLine("error: " + ex); code = 1; }
            log.AppendLine("finished with code " + code);
            try { File.WriteAllText(outFile, log.ToString()); } catch (Exception) { }
            return code;
        }

        public static string Outcome = "";            // ok, failed, cancelled, timeout, noreport
        public static int LastBefore = -1, LastAfter = -1;   // DNS cache entries before and after the last flush (-1 = unknown)

        // Starts the elevated copy (Windows shows the permission window) and reads what it wrote.
        // step(text) says which stage the job is in, so the window can show it.
        public static bool RunElevated(string args, Action<string> log, Action<string> step = null)
        {
            string id = Guid.NewGuid().ToString("N");
            int code = -1;
            Outcome = "failed"; LastBefore = -1; LastAfter = -1;
            if (IsAdmin())
            {
                // this copy already runs as administrator: do the work here, no permission window
                log("Running as administrator: no permission window needed.");
                if (step != null) step("Working ...");
                int c = HelperMain((args + " " + id).Split(' '));
                return ReadResult(id, c, log, step);
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, args + " " + id);
                psi.Verb = "runas"; psi.UseShellExecute = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
                log("Windows will now ask for permission (the shield window) ...");
                if (step != null) step("Waiting for Windows permission ...");
                using (Process p = Process.Start(psi))
                {
                    if (step != null) step("Working ...");
                    if (!p.WaitForExit(180000)) { log("Timed out while waiting for the permission step."); Outcome = "timeout"; return false; }
                    code = p.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) { log("You cancelled the Windows permission window. Nothing was changed."); Outcome = "cancelled"; }
                else log("Could not start the permission step: " + ex.Message);
                return false;
            }
            return ReadResult(id, code, log, step);
        }

        // reads what the elevated copy wrote
        static bool ReadResult(string id, int code, Action<string> log, Action<string> step)
        {
            if (step != null) step("Reading the result ...");
            try
            {
                string[] lines = File.Exists(ResultFile) ? File.ReadAllLines(ResultFile) : new string[0];
                if (lines.Length == 0 || lines[0] != "run=" + id) { log("The permission step did not report back (exit code " + code + ")."); Outcome = "noreport"; return false; }
                string pb = "DNS cache entries before: ", pa = "DNS cache entries after: ";
                foreach (string l in lines)
                {
                    AppLog.Debug("[helper] " + l);
                    int n;
                    if (l.StartsWith(pb) && int.TryParse(l.Substring(pb.Length), out n)) LastBefore = n;
                    if (l.StartsWith(pa) && int.TryParse(l.Substring(pa.Length), out n)) LastAfter = n;
                }
            }
            catch (Exception ex) { log("Could not read the result: " + ex.Message); }
            Outcome = code == 0 ? "ok" : code == 5 ? "declined" : "failed";
            return code == 0;
        }

        public static string CountFile { get { return System.IO.Path.Combine(Dir, "dns-count.txt"); } }

        // Like RunElevated for "clear the DNS cache", but the elevated copy first counts the names (only an administrator can see the cache)
        // and waits for the answer of confirm(count) before it clears anything. One permission window for both.
        public static bool PreviewFlush(bool extra, Action<string> log, Action<string> step, Func<int, bool> confirm)
        {
            string id = Guid.NewGuid().ToString("N");
            Outcome = "failed"; LastBefore = -1; LastAfter = -1;
            if (IsAdmin())
            {
                // an administrator copy can see the cache itself, so there is nothing to ask: the number is already on the screen
                log("Running as administrator: no permission window needed.");
                if (step != null) step("Working ...");
                int c = HelperMain(new string[] { "--dns-flush", extra ? "extra" : "plain", id });
                return ReadResult(id, c, log, step);
            }
            EventWaitHandle go = null, cancel = null;
            try
            {
                go = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\FiveMToolbox.Go." + id);
                cancel = new EventWaitHandle(false, EventResetMode.ManualReset, @"Local\FiveMToolbox.Cancel." + id);
                ProcessStartInfo psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--dns-flush-ask " + (extra ? "extra" : "plain") + " " + id);
                psi.Verb = "runas"; psi.UseShellExecute = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
                log("Windows will now ask for permission (the shield window) ...");
                if (step != null) step("Waiting for Windows permission ...");
                int code;
                using (Process p = Process.Start(psi))
                {
                    if (step != null) step("Counting the names in the cache ...");
                    int count = -1;
                    DateTime until = DateTime.UtcNow.AddSeconds(60);
                    while (DateTime.UtcNow < until && !p.HasExited)
                    {
                        try
                        {
                            string[] l = File.Exists(CountFile) ? File.ReadAllLines(CountFile) : new string[0];
                            if (l.Length >= 2 && l[0] == "run=" + id && l[1].StartsWith("count=")) { int.TryParse(l[1].Substring(6), out count); break; }
                        }
                        catch (Exception) { }
                        Thread.Sleep(150);
                    }
                    if (p.HasExited) code = p.ExitCode;
                    else
                    {
                        AppLog.Debug("the DNS cache holds " + count + " records (counted by the elevated copy)");
                        log("The DNS cache holds " + (count < 0 ? "an unknown number of" : count.ToString()) + " names.");
                        bool yes = confirm(count);
                        if (yes) go.Set(); else cancel.Set();
                        if (yes && step != null) step("Working ...");
                        if (!p.WaitForExit(180000)) { log("Timed out while waiting for the permission step."); Outcome = "timeout"; return false; }
                        code = p.ExitCode;
                    }
                }
                return ReadResult(id, code, log, step);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) { log("You cancelled the Windows permission window. Nothing was changed."); Outcome = "cancelled"; }
                else log("Could not start the permission step: " + ex.Message);
                return false;
            }
            finally { if (go != null) go.Close(); if (cancel != null) cancel.Close(); }
        }
    }

    // ---------------------------------------------------------------- FiveM cache tool
    class CacheItem
    {
        public string Key; public string Title; public string Detail; public bool Risky;
        public List<string> Paths = new List<string>();
        public List<long> PathBytes = new List<long>();
        public long Bytes;
    }

    static class CacheTool
    {
        // Only these folder names are ever touched, and only inside a folder called FiveM.app.
        public static List<CacheItem> Items(string root)
        {
            string data = Path.Combine(root, "data");
            CacheItem t = new CacheItem();
            t.Key = "temp"; t.Title = Loc.T("Temporary files");
            t.Detail = Loc.T("Downloaded server files and game cache. FiveM builds them again, so the next start can be a bit slower.");
            t.Paths.Add(Path.Combine(data, "cache")); t.Paths.Add(Path.Combine(data, "server-cache")); t.Paths.Add(Path.Combine(data, "server-cache-priv"));
            CacheItem l = new CacheItem();
            l.Key = "logs"; l.Title = Loc.T("Logs and crash reports");
            l.Detail = Loc.T("Old log files and crash dumps. Safe to remove.");
            l.Paths.Add(Path.Combine(root, "logs")); l.Paths.Add(Path.Combine(root, "crashes"));
            CacheItem d = new CacheItem();
            d.Key = "deep"; d.Title = Loc.T("Login and saved data (deep clean)"); d.Risky = true;
            d.Detail = Loc.T("You have to log in to FiveM again. Your key binds and graphics settings stay. Use it only when the normal clean did not help.");
            d.Paths.Add(Path.Combine(data, "nui-storage")); d.Paths.Add(Path.Combine(data, "game-storage"));
            return new List<CacheItem>(new CacheItem[] { t, l, d });
        }

        public static bool IsFiveMApp(string root)
        {
            try { return !string.IsNullOrEmpty(root) && string.Equals(Path.GetFileName(root.TrimEnd('\\')), "FiveM.app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(root); }
            catch (Exception) { return false; }
        }

        public static long SizeOf(string path)
        {
            long total = 0;
            if (!Directory.Exists(path)) return 0;
            Stack<string> todo = new Stack<string>();
            todo.Push(path);
            while (todo.Count > 0)
            {
                string d = todo.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(d)) { try { total += new FileInfo(f).Length; } catch (Exception) { } }
                    foreach (string s in Directory.GetDirectories(d))
                    {
                        try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch (Exception) { continue; }
                        todo.Push(s);
                    }
                }
                catch (Exception) { }
            }
            return total;
        }

        public static string Size(long bytes)
        {
            if (bytes < 1024 * 1024) return (bytes / 1024) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024 * 1024)) + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB";
        }

        // FiveM and the processes it starts. This program itself is left alone.
        public static List<Process> Running()
        {
            List<Process> list = new List<Process>();
            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me) continue;
                    if (Regex.IsMatch(p.ProcessName, @"^FiveM(_.*)?$", RegexOptions.IgnoreCase) && !p.ProcessName.StartsWith(Info.ExeName, StringComparison.OrdinalIgnoreCase)) list.Add(p);
                }
                catch (Exception) { }
            }
            return list;
        }

        public static string FiveMExe(string root)
        {
            try { string exe = Path.Combine(Path.GetDirectoryName(root.TrimEnd('\\')), "FiveM.exe"); return File.Exists(exe) ? exe : null; }
            catch (Exception) { return null; }
        }

        public static void CloseFiveM(Action<string> log)
        {
            foreach (Process p in Running())
            {
                try { log("  Closing " + p.ProcessName + " (process " + p.Id + ") ..."); p.Kill(); p.WaitForExit(8000); }
                catch (Exception ex) { log("  Could not close " + p.ProcessName + ": " + ex.Message); }
            }
        }

        // ---- "Reset": the most extreme clean. On top of the cache, the logs and the login data it also removes the content of the
        // settings folder (key binds, graphics settings, saved server data). Only these names, only in %APPDATA%\CitizenFX, and only
        // after a backup copy. ros_id.dat and everything else in that folder stays.
        public static readonly string[] ResetNames = new string[] { "fivem.cfg", "gta5_settings.xml", "kvs", "media_access.json", "camera_save_structure.xml", "imgui.ini" };

        // Reset also removes two folders in %LOCALAPPDATA%: CEF (browser files) and DigitalEntitlements (the saved GTA V licence check).
        // ros_id.dat (your saved Rockstar services ID) and the folder "Rockstar Games" (the Rockstar launcher, Social Club and GTA V itself) are NOT touched.
        public static readonly string[] ResetLocalNames = new string[] { "CEF", "DigitalEntitlements" };
        public static string LocalOverride = null;     // for tests: work on a copy of the local folders
        public static string LocalDir { get { return LocalOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        static bool IsLocalTarget(string parent, string name)
        {
            if (!string.Equals((parent ?? "").TrimEnd('\\'), LocalDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            return Array.Exists(ResetLocalNames, delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); });
        }

        static bool IsSettingsTarget(string parent, string name)
        {
            if (!string.Equals((parent ?? "").TrimEnd('\\'), GfxTool.Dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            return Array.Exists(ResetNames, delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); });
        }

        // how many keys are bound now (a bind to NULL is an empty slot and does not count)
        public static int CountBinds()
        {
            try
            {
                string f = GfxTool.CfgFile;
                if (!File.Exists(f)) return 0;
                int n = 0;
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    string[] w = line.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (w.Length < 3) continue;
                    string first = w[0].ToLowerInvariant();
                    string key = first == "rbind" ? (w.Length > 3 ? w[3] : "NULL") : first == "bind" ? w[2] : null;
                    if (key != null && !string.Equals(key, "NULL", StringComparison.OrdinalIgnoreCase)) n++;
                }
                return n;
            }
            catch (Exception) { return 0; }
        }

        // how many files and bytes a folder tree holds (folders that cannot be read are skipped; stops after the given time)
        public static void CountTree(string dir, DateTime stop, out long files, out long bytes)
        {
            long f = 0, b = 0;
            Stack<string> todo = new Stack<string>();
            todo.Push(dir);
            while (todo.Count > 0 && DateTime.UtcNow < stop)
            {
                string d = todo.Pop();
                try { foreach (string file in Directory.GetFiles(d)) { f++; try { b += new FileInfo(file).Length; } catch (Exception) { } } } catch (Exception) { }
                try { foreach (string sub in Directory.GetDirectories(d)) todo.Push(sub); } catch (Exception) { }
            }
            files = f; bytes = b;
        }

        // what the Reset is about, before and after (so the log alone shows what changed and what stayed)
        class ResetState
        {
            public int Binds;
            public List<KeyValuePair<string, long>> Settings = new List<KeyValuePair<string, long>>();   // the six items: size, or -1 = not there
            public long RosSize = -1;                                                                    // ros_id.dat
            public List<KeyValuePair<string, long>> Local = new List<KeyValuePair<string, long>>();      // CEF and DigitalEntitlements
            public long RockFiles = -1, RockBytes = -1;                                                  // the Rockstar Games folder (never touched)
        }

        static ResetState Snapshot()
        {
            ResetState st = new ResetState();
            st.Binds = CountBinds();
            foreach (string n in ResetNames)
            {
                string p = Path.Combine(GfxTool.Dir, n);
                long size = -1;
                try { size = Directory.Exists(p) ? SizeOf(p) : File.Exists(p) ? new FileInfo(p).Length : -1; } catch (Exception) { }
                st.Settings.Add(new KeyValuePair<string, long>(n, size));
            }
            try { string ros = Path.Combine(GfxTool.Dir, "ros_id.dat"); if (File.Exists(ros)) st.RosSize = new FileInfo(ros).Length; } catch (Exception) { }
            foreach (string n in ResetLocalNames)
            {
                string p = Path.Combine(LocalDir, n);
                long size = -1;
                try { size = Directory.Exists(p) ? SizeOf(p) : -1; } catch (Exception) { }
                st.Local.Add(new KeyValuePair<string, long>(n, size));
            }
            string rock = Path.Combine(LocalDir, "Rockstar Games");
            try { if (Directory.Exists(rock)) CountTree(rock, DateTime.UtcNow.AddSeconds(40), out st.RockFiles, out st.RockBytes); } catch (Exception) { }
            return st;
        }

        static void LogState(string phase, ResetState st, Action<string> log)
        {
            string w = "  Check (" + phase + "): ";
            log(w + "key binds: " + st.Binds);
            foreach (KeyValuePair<string, long> kv in st.Settings) log(w + "settings " + kv.Key + ": " + (kv.Value < 0 ? "not there" : "there (" + Size(kv.Value) + ")"));
            log(w + "settings ros_id.dat: " + (st.RosSize < 0 ? "not there" : "there (" + st.RosSize + " bytes)"));
            foreach (KeyValuePair<string, long> kv in st.Local) log(w + "local " + kv.Key + ": " + (kv.Value < 0 ? "not there" : "there (" + Size(kv.Value) + ")"));
            log(w + "local Rockstar Games folder (never touched): " + (st.RockFiles < 0 ? "not there" : st.RockFiles + " files, " + st.RockBytes + " bytes"));
        }

        public static Action<string> AfterBackupCopy = null;     // for the test only: runs after a backup copy is made (the test damages the copy to prove that the Reset then stops)

        static void CopyDir(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
        }

        // the copy must hold the same number of files and bytes as the original, else the Reset stops before it deletes anything
        static bool SameTree(string original, string copy, string what, Action<string> log)
        {
            long f1, b1, f2, b2;
            DateTime stop = DateTime.UtcNow.AddSeconds(60);
            CountTree(original, stop, out f1, out b1); CountTree(copy, stop, out f2, out b2);
            if (f1 != f2 || b1 != b2) { log("  Backup check FAILED: the copy of the " + what + " has " + f2 + " files and " + b2 + " bytes, the original has " + f1 + " files and " + b1 + " bytes."); return false; }
            log("  Backup check: the copy of the " + what + " has the same " + f1 + " files and " + b1 + " bytes as the original.");
            return true;
        }

        // a full copy of the settings folder and of the licence check files; false when a copy could not be made (then nothing is deleted)
        static bool BackupSettings(Action<string> log)
        {
            try
            {
                string bdir = GfxTool.OverrideDir != null ? GfxTool.OverrideDir.TrimEnd('\\') + "-backups" : Blb.BackupDir;
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string src = GfxTool.Dir;
                if (!Directory.Exists(src)) log("  There is no settings folder to back up.");
                else
                {
                    string dest = Path.Combine(bdir, "CitizenFX-before-reset-" + stamp);
                    CopyDir(src, dest);
                    if (AfterBackupCopy != null) AfterBackupCopy(dest);
                    log("  Backup of the settings folder saved: " + dest);
                    if (!SameTree(src, dest, "settings folder", log)) return false;
                }
                string ent = Path.Combine(LocalDir, "DigitalEntitlements");
                if (Directory.Exists(ent))
                {
                    string dest2 = Path.Combine(bdir, "DigitalEntitlements-before-reset-" + stamp);
                    CopyDir(ent, dest2);
                    if (AfterBackupCopy != null) AfterBackupCopy(dest2);
                    log("  Backup of the licence check files saved: " + dest2);
                    if (!SameTree(ent, dest2, "licence check files", log)) return false;
                }
                return true;
            }
            catch (Exception ex) { log("  Could not back up the settings folder: " + ex.Message); AppLog.Debug("settings backup failed: " + ex); return false; }
        }

        static bool ResetSettings(Action<string> log, Action<string, double> progress)
        {
            string dir = GfxTool.Dir;
            if (!Directory.Exists(dir)) { log("  There is no settings folder, nothing to remove."); return true; }
            bool ok = true;
            for (int i = 0; i < ResetNames.Length; i++)
            {
                string name = ResetNames[i];
                string p = Path.Combine(dir, name);
                if (progress != null) progress(name, (double)i / ResetNames.Length);
                if (!IsSettingsTarget(Path.GetDirectoryName(p), Path.GetFileName(p))) { log("  Skipped (not an allowed place): " + p); ok = false; continue; }
                try
                {
                    bool isDir = Directory.Exists(p), isFile = File.Exists(p);
                    if (!isDir && !isFile) { log("  " + name + ": not there, nothing to do."); continue; }
                    if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) { log("  " + name + ": is a link, skipped for safety."); ok = false; continue; }
                    long before = isDir ? SizeOf(p) : new FileInfo(p).Length;
                    if (isDir) Directory.Delete(p, true); else File.Delete(p);
                    log("  " + name + ": removed (" + Size(before) + ").");
                }
                catch (Exception ex) { log("  " + name + ": could not be removed: " + ex.Message); AppLog.Debug("reset delete failed for " + p + ": " + ex); ok = false; }
            }
            return ok;
        }

        static bool ResetLocal(Action<string> log, Action<string, double> progress)
        {
            bool ok = true;
            for (int i = 0; i < ResetLocalNames.Length; i++)
            {
                string name = ResetLocalNames[i];
                string p = Path.Combine(LocalDir, name);
                if (progress != null) progress(name, (double)i / ResetLocalNames.Length);
                if (!IsLocalTarget(Path.GetDirectoryName(p), Path.GetFileName(p))) { log("  Skipped (not an allowed place): " + p); ok = false; continue; }
                try
                {
                    if (!Directory.Exists(p)) { log("  " + name + ": not there, nothing to do."); continue; }
                    if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) { log("  " + name + ": is a link, skipped for safety."); ok = false; continue; }
                    long before = SizeOf(p);
                    Directory.Delete(p, true);
                    log("  " + name + ": removed (" + Size(before) + ").");
                }
                catch (Exception ex) { log("  " + name + ": could not be removed: " + ex.Message); AppLog.Debug("reset delete failed for " + p + ": " + ex); ok = false; }
            }
            return ok;
        }

        // 0 = everything done, 1 = stopped before deleting anything (no backup, or not a FiveM.app folder), 2 = done, but some parts could not be removed.
        // progress(name, 0..1): an empty name means "making the backup".
        public static int Reset(string root, List<CacheItem> items, Action<string> log, Action<string, double> progress)
        {
            if (!IsFiveMApp(root)) { log("Stopped: " + root + " is not a FiveM.app folder."); return 1; }
            if (progress != null) progress("", 0.02);
            log("Check of the state before the Reset:");
            ResetState before = Snapshot();
            LogState("before", before, log);
            log("Step 1 of 4: backing up the settings folder and the licence check files ...");
            if (!BackupSettings(log)) { log("Stopped. Nothing was deleted, because the backup copy could not be made or is not complete."); return 1; }
            bool ok = true;
            log("Step 2 of 4: removing the cache, the logs and the saved login data ...");
            if (!Clear(root, items, log, delegate(string nm, double fr) { if (progress != null) progress(nm, 0.10 + 0.65 * fr); })) ok = false;
            log("Step 3 of 4: removing the key binds, the graphics settings and the saved server data ...");
            if (!ResetSettings(log, delegate(string nm, double fr) { if (progress != null) progress(nm, 0.75 + 0.15 * fr); })) ok = false;
            log("Step 4 of 4: removing the browser files and the licence check files ...");
            if (!ResetLocal(log, delegate(string nm, double fr) { if (progress != null) progress(nm, 0.90 + 0.10 * fr); })) ok = false;
            log("Check of the state after the Reset:");
            ResetState after = Snapshot();
            LogState("after", after, log);
            bool goneS = after.Settings.TrueForAll(delegate(KeyValuePair<string, long> kv) { return kv.Value < 0; });
            bool goneL = after.Local.TrueForAll(delegate(KeyValuePair<string, long> kv) { return kv.Value < 0; });
            bool rosSame = after.RosSize == before.RosSize;
            bool rockSame = after.RockFiles == before.RockFiles && after.RockBytes == before.RockBytes;
            log("Check: the six settings items are gone: " + (goneS ? "yes" : "NO"));
            log("Check: CEF and DigitalEntitlements are gone: " + (goneL ? "yes" : "NO"));
            log("Check: ros_id.dat is unchanged: " + (rosSame ? "yes" : "NO") + " (" + (before.RosSize < 0 ? "not there" : before.RosSize + " bytes") + " before, " + (after.RosSize < 0 ? "not there" : after.RosSize + " bytes") + " after)");
            log("Check: the Rockstar Games folder is unchanged: " + (rockSame ? "yes" : "NO") + " (" + before.RockFiles + " files and " + before.RockBytes + " bytes before, " + after.RockFiles + " files and " + after.RockBytes + " bytes after)"
                + (rockSame ? "" : "; the Reset never touches it, but the Rockstar Launcher can change files there by itself when it runs"));
            if (!goneS || !goneL) ok = false;
            return ok ? 0 : 2;
        }

        // Returns true when every chosen folder is gone afterwards. progress(name, 0..1) says which folder is being removed and how far the whole job is.
        public static bool Clear(string root, List<CacheItem> chosen, Action<string> log, Action<string, double> progress = null)
        {
            if (!IsFiveMApp(root)) { log("Stopped: " + root + " is not a FiveM.app folder."); return false; }
            long totalW = 0;
            foreach (CacheItem it0 in chosen)
                for (int j = 0; j < it0.Paths.Count; j++) totalW += Math.Max(1L, j < it0.PathBytes.Count ? it0.PathBytes[j] : 1L);
            if (totalW < 1) totalW = 1;
            long doneW = 0;
            bool ok = true;
            foreach (CacheItem it in chosen)
                for (int k = 0; k < it.Paths.Count; k++)
                {
                    string p = it.Paths[k];
                    long known = k < it.PathBytes.Count ? it.PathBytes[k] : -1L;
                    string name = Path.GetFileName(p);
                    if (progress != null) progress(name, (double)doneW / totalW);
                    if (!ClearOne(root, p, known, log)) ok = false;
                    doneW += Math.Max(1L, known);
                    if (progress != null) progress(name, (double)doneW / totalW);
                }
            return ok;
        }

        static bool ClearOne(string root, string p, long known, Action<string> log)
        {
            string name = Path.GetFileName(p);
            string parent = Path.GetDirectoryName(p);
            bool inRoot = string.Equals(parent, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            bool inData = string.Equals(parent, Path.Combine(root.TrimEnd('\\'), "data"), StringComparison.OrdinalIgnoreCase);
            if (!inRoot && !inData) { log("  Skipped (not an allowed place): " + p); return false; }
            if (!Directory.Exists(p)) { log("  " + name + ": not there, nothing to do."); return true; }
            try
            {
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) { log("  " + name + ": is a link, skipped for safety."); return false; }
                long before = known >= 0 ? known : SizeOf(p);
                Directory.Delete(p, true);
                log("  " + name + ": removed (" + Size(before) + ").");
                return true;
            }
            catch (Exception ex) { log("  " + name + ": could not be removed: " + ex.Message); AppLog.Debug("cache delete failed for " + p + ": " + ex); return false; }
        }
    }

    // Icon-font characters (Segoe Fluent Icons / Segoe MDL2 Assets). Written as numbers so the source file stays plain ASCII.
    static class Ico
    {
        static string C(int code) { return ((char)code).ToString(); }
        public static readonly string Search = C(0xE721);
        public static readonly string Refresh = C(0xE72C);
        public static readonly string Folder = C(0xE8B7);
        public static readonly string OpenFolder = C(0xE838);
        public static readonly string Copy = C(0xE8C8);
        public static readonly string Delete = C(0xE74D);
        public static readonly string Globe = C(0xE774);
        public static readonly string Play = C(0xE768);
        public static readonly string Cancel = C(0xE711);
        public static readonly string Accept = C(0xE8FB);
        public static readonly string Undo = C(0xE7A7);
        public static readonly string Document = C(0xE8A5);
        public static readonly string PopOut = C(0xE8A7);
        public static readonly string Warning = C(0xE7BA);
        public static readonly string Info = C(0xE946);
        public static readonly string Monitor = C(0xE7F4);
        public static readonly string Sun = C(0xE706);
        public static readonly string Moon = C(0xE708);
        public static readonly string Dot = C(0x25CF);
        public static readonly string Chevron = C(0xE70D);
        public static readonly string ChevUp = C(0xE70E);
        public static readonly string Shield = C(0xE83D);
        public static readonly string Palette = C(0xE790);
        public static readonly string Link = C(0xE71B);
        public static readonly string Report = C(0xE9D9);
        public static readonly string Bullet = C(0x2022);
        public static readonly string Copyright = C(0xA9);
    }

    // ---------------------------------------------------------------- drawing helpers
    static class Gfx
    {
        static readonly Dictionary<float, Font> iconFonts = new Dictionary<float, Font>();

        public static GraphicsPath Rounded(Rectangle r, int d)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // rounded at the top, square at the bottom (a browser-style tab)
        public static GraphicsPath RoundedTop(Rectangle r, int d)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddLine(r.Right, r.Y + d / 2, r.Right, r.Bottom);
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }

        public static Font IconFontOf(float size)
        {
            if (IconButton.IconFont == null) return null;
            Font f;
            if (!iconFonts.TryGetValue(size, out f)) { f = new Font(IconButton.IconFont.FontFamily, size); iconFonts[size] = f; }
            return f;
        }

        public static void DrawGauge(Graphics g, Rectangle r, Color c)
        {
            float s = Math.Min(r.Width, r.Height) * 0.80f;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f + s * 0.10f, rad = s / 2f;
            using (Pen p = new Pen(c, Math.Max(Dpi.F(1.6f), s * 0.14f)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawArc(p, cx - rad, cy - rad, rad * 2f, rad * 2f, 150f, 240f);
                g.DrawLine(p, cx, cy, cx + (float)Math.Cos(-0.5) * rad * 0.8f, cy + (float)Math.Sin(-0.5) * rad * 0.8f);
            }
        }

        // An icon from the icon font, or the gauge drawn by the program.
        public static void Glyph(Graphics g, string glyph, Rectangle r, Color color, float size)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            if (glyph == "gauge") { DrawGauge(g, r, color); return; }
            Font f = IconFontOf(size);
            if (f == null) { VectorGlyph(g, glyph, r, color); return; }
            TextRenderer.DrawText(g, glyph, f, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Without the Windows icon font the buttons that have ONLY an icon (theme: monitor, sun, moon, and the shield) were invisible.
        // These four are drawn by the program itself then. The other icons sit next to a text, so nothing is lost without them.
        static void VectorGlyph(Graphics g, string glyph, Rectangle r, Color c)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height) * 0.62f, cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            using (Pen pen = new Pen(c, Math.Max(Dpi.F(1.5f), s * 0.11f)))
            using (Brush br = new SolidBrush(c))
            {
                pen.LineJoin = LineJoin.Round; pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                if (glyph == Ico.Monitor)
                {
                    g.DrawRectangle(pen, cx - s / 2f, cy - s * 0.42f, s, s * 0.66f);
                    g.DrawLine(pen, cx, cy + s * 0.24f, cx, cy + s * 0.44f);
                    g.DrawLine(pen, cx - s * 0.25f, cy + s * 0.46f, cx + s * 0.25f, cy + s * 0.46f);
                }
                else if (glyph == Ico.Sun)
                {
                    g.DrawEllipse(pen, cx - s * 0.2f, cy - s * 0.2f, s * 0.4f, s * 0.4f);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        g.DrawLine(pen, cx + (float)Math.Cos(a) * s * 0.34f, cy + (float)Math.Sin(a) * s * 0.34f, cx + (float)Math.Cos(a) * s * 0.5f, cy + (float)Math.Sin(a) * s * 0.5f);
                    }
                }
                else if (glyph == Ico.Moon)
                {
                    using (GraphicsPath p = new GraphicsPath())
                    using (GraphicsPath q = new GraphicsPath())
                    {
                        p.AddEllipse(cx - s * 0.42f, cy - s * 0.42f, s * 0.84f, s * 0.84f);
                        q.AddEllipse(cx - s * 0.12f, cy - s * 0.55f, s * 0.84f, s * 0.84f);
                        using (Region rg = new Region(p)) { rg.Exclude(q); g.FillRegion(br, rg); }
                    }
                }
                else if (glyph == Ico.Shield)
                {
                    PointF[] pts = new PointF[] { new PointF(cx, cy - s * 0.5f), new PointF(cx + s * 0.42f, cy - s * 0.3f), new PointF(cx + s * 0.36f, cy + s * 0.15f), new PointF(cx, cy + s * 0.5f), new PointF(cx - s * 0.36f, cy + s * 0.15f), new PointF(cx - s * 0.42f, cy - s * 0.3f) };
                    g.DrawPolygon(pen, pts);
                }
            }
            g.SmoothingMode = old;
        }
    }

    // ---------------------------------------------------------------- a small animation engine (one shared timer for everything)
    class Tween { public DateTime Start; public int Ms; public Action<float> Step; public Action Done; }

    static class Fx
    {
        static System.Windows.Forms.Timer timer;
        static readonly List<Fader> faders = new List<Fader>();
        static readonly List<Tween> tweens = new List<Tween>();
        static DateTime last = DateTime.UtcNow;

        public static Color Mix(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static float Clamp(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        public static float EaseOut(float t) { t = Clamp(t); float u = 1f - t; return 1f - u * u * u; }

        static void Ensure()
        {
            if (timer == null) { timer = new System.Windows.Forms.Timer(); timer.Interval = 15; timer.Tick += Tick; }
            if (!timer.Enabled) { last = DateTime.UtcNow; timer.Start(); }
        }

        internal static void Add(Fader f) { if (!faders.Contains(f)) faders.Add(f); Ensure(); }

        // Runs step(0..1) for the given time, then done(). Always call it on the window thread.
        public static void Run(int ms, Action<float> step, Action done)
        {
            Tween t = new Tween(); t.Start = DateTime.UtcNow; t.Ms = Math.Max(1, ms); t.Step = step; t.Done = done;
            tweens.Add(t); Ensure();
        }

        static void Tick(object s, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            float dt = (float)(now - last).TotalMilliseconds; last = now;
            if (dt > 100f) dt = 100f;
            for (int i = faders.Count - 1; i >= 0; i--)
            {
                bool more = false;
                try { more = faders[i].Step(dt); } catch (Exception) { }
                if (!more && i < faders.Count) faders.RemoveAt(i);
            }
            for (int j = tweens.Count - 1; j >= 0; j--)
            {
                if (j >= tweens.Count) continue;
                Tween t = tweens[j];
                float p = (float)((now - t.Start).TotalMilliseconds / t.Ms);
                bool fin = p >= 1f;
                try { if (t.Step != null) t.Step(fin ? 1f : p); } catch (Exception) { }
                if (fin) { tweens.Remove(t); try { if (t.Done != null) t.Done(); } catch (Exception) { } }
            }
            if (faders.Count == 0 && tweens.Count == 0) timer.Stop();
        }
    }

    // A value that glides to its target (hover colours, tab highlights). It redraws its control while it moves.
    class Fader
    {
        readonly Control ctl;
        readonly Action changed;
        public float Value = 0f;
        public float Target = 0f;
        public float Tau = 70f;

        public Fader(Control c) { ctl = c; }
        public Fader(Action onChange) { changed = onChange; }

        public void To(float t)
        {
            Target = t;
            if (Math.Abs(Value - Target) > 0.002f) Fx.Add(this);
        }

        public void Set(float v) { Value = v; Target = v; }

        internal bool Step(float dt)
        {
            if (ctl != null && (ctl.IsDisposed || !ctl.IsHandleCreated)) { Value = Target; return false; }
            float k = 1f - (float)Math.Exp(-dt / Tau);
            Value += (Target - Value) * k;
            bool more = Math.Abs(Target - Value) > 0.004f;
            if (!more) Value = Target;
            if (ctl != null) ctl.Invalidate();
            if (changed != null) changed();
            return more;
        }
    }

    // ---------------------------------------------------------------- the activity card: what a job is doing right now, and how it ended
    class ActivityCard : Control
    {
        const int H = 66, Gap = 6;
        static int HP { get { return Dpi.S(H); } }      // the same in screen pixels
        static int GapP { get { return Dpi.S(Gap); } }
        string state = "idle", title = "", detail = "";
        float target = -1f, frac = 0f, sweep = 0f, doneT = 0f;
        DateTime started = DateTime.UtcNow;
        bool closeHover;
        readonly Fader slide;
        readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer autoHide = new System.Windows.Forms.Timer();

        public ActivityCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Visible = false; Height = 0;
            slide = new Fader(delegate { Place(); Invalidate(); });
            slide.Tau = 80f;
            clock.Interval = 16;
            clock.Tick += delegate { Advance(); };
            autoHide.Interval = 14000;
            autoHide.Tick += delegate { autoHide.Stop(); if (state == "ok" || state == "info") HideNow(); };
        }

        public bool IsRunning { get { return state == "run"; } }

        // The card is docked at the top of the log area. Its height follows the slide, so the log is pushed down while it shows.
        public void Place()
        {
            int h = (int)Math.Round((HP + GapP) * slide.Value);
            Height = h;
            if (h > 0 && !Visible) Visible = true;
            if (h <= 0 && slide.Target <= 0f) { Visible = false; clock.Stop(); state = "idle"; }
        }

        public void Start(string text)
        {
            autoHide.Stop();
            state = "run"; title = text; detail = ""; target = -1f; frac = 0f; doneT = 0f; sweep = 0f; started = DateTime.UtcNow;
            slide.To(1f);
            Visible = true; Place();
            if (!clock.Enabled) clock.Start();
            Invalidate();
        }

        public void Step(string text, double fraction)
        {
            detail = text;
            target = fraction >= 0 ? (float)Math.Min(1.0, fraction) : -1f;
            Invalidate();
        }

        // kind: ok (green tick), info (blue i), bad (red cross)
        public void Finish(string kind, string text, string more)
        {
            autoHide.Stop(); if (kind != "bad") autoHide.Start();
            state = kind; title = text; detail = more; target = 1f; doneT = 0f;
            slide.To(1f);
            Visible = true; Place();
            Fx.Run(600, delegate(float p) { doneT = p; Invalidate(); }, null);
            if (!clock.Enabled) clock.Start();
            Invalidate();
        }

        public void HideNow() { autoHide.Stop(); slide.To(0f); }

        void Advance()
        {
            float goal = target < 0f ? frac : target;
            frac += (goal - frac) * 0.10f;
            if (Math.Abs(goal - frac) < 0.003f) frac = goal;
            sweep += 0.018f; if (sweep > 1.5f) sweep = 0f;
            Invalidate();
            if (state != "run" && frac == goal && doneT >= 1f) clock.Stop();
        }

        Color StateColor() { return state == "bad" ? Theme.Bad : state == "ok" ? Theme.Good : Theme.Primary; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool over = state != "run" && e.X > Width - Dpi.S(44);
            if (over != closeHover) { closeHover = over; Invalidate(); }
            Cursor = state != "run" ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { closeHover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (state != "run") HideNow(); base.OnMouseClick(e); }

        static float Dist(PointF a, PointF b) { float dx = b.X - a.X, dy = b.Y - a.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }
        static PointF Lerp(PointF a, PointF b, float t) { return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }

        void DrawSpinner(Graphics g, float cx, float cy, Color sc)
        {
            RectangleF rc = new RectangleF(cx - 14f, cy - 14f, 28f, 28f);
            using (Pen ring = new Pen(Fx.Mix(Theme.Bg3, sc, 0.22f), 3.4f)) g.DrawEllipse(ring, rc);
            double ms = (DateTime.UtcNow - started).TotalMilliseconds;
            float ang = (float)((ms / 850.0 * 360.0) % 360.0);
            float arcLen = 90f + 70f * (float)Math.Sin(ms / 420.0);
            using (Pen arc = new Pen(sc, 3.4f)) { arc.StartCap = LineCap.Round; arc.EndCap = LineCap.Round; g.DrawArc(arc, rc, ang, arcLen); }
        }

        void DrawResult(Graphics g, float cx, float cy, Color sc)
        {
            float grow = Fx.EaseOut(doneT / 0.45f);
            float rad = 15f * (0.2f + 0.8f * grow);
            using (Brush b = new SolidBrush(sc)) g.FillEllipse(b, cx - rad, cy - rad, rad * 2f, rad * 2f);
            float q = Fx.Clamp((doneT - 0.30f) / 0.70f);
            using (Pen w = new Pen(Color.White, 3.2f))
            {
                w.StartCap = LineCap.Round; w.EndCap = LineCap.Round; w.LineJoin = LineJoin.Round;
                if (state == "ok")
                {
                    PointF a = new PointF(cx - 7f, cy + 0.5f), m = new PointF(cx - 2f, cy + 5.5f), z = new PointF(cx + 7.5f, cy - 5.5f);
                    float l1 = Dist(a, m), l2 = Dist(m, z), len = q * (l1 + l2);
                    if (len > 0.2f)
                    {
                        if (len <= l1) g.DrawLine(w, a, Lerp(a, m, len / l1));
                        else { g.DrawLine(w, a, m); g.DrawLine(w, m, Lerp(m, z, (len - l1) / l2)); }
                    }
                }
                else if (state == "bad")
                {
                    float d = 5.5f, q1 = Fx.Clamp(q * 2f), q2 = Fx.Clamp(q * 2f - 1f);
                    if (q1 > 0.02f) g.DrawLine(w, cx - d, cy - d, cx - d + 2f * d * q1, cy - d + 2f * d * q1);
                    if (q2 > 0.02f) g.DrawLine(w, cx + d, cy - d, cx + d - 2f * d * q2, cy - d + 2f * d * q2);
                }
                else
                {
                    if (q > 0.05f) g.DrawLine(w, cx, cy - 1f, cx, cy - 1f + 8f * q);
                    if (q > 0.30f) using (Brush wb = new SolidBrush(Color.White)) g.FillEllipse(wb, cx - 1.9f, cy - 8.2f, 3.8f, 3.8f);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Height < 3) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            g.TranslateTransform(0, Height - HP - GapP);
            Color sc = StateColor();
            Rectangle r = new Rectangle(0, 0, Width - 1, HP - 1);
            using (GraphicsPath p = Gfx.Rounded(r, Dpi.S(16)))
            {
                using (Brush b = new SolidBrush(Theme.Bg3)) g.FillPath(b, p);
                using (Pen pen = new Pen(Fx.Mix(Theme.Border, sc, 0.75f), Dpi.F(1.6f))) g.DrawPath(pen, p);
            }
            float cx = 34f, cy = 31f;
            {
                // the spinner and the tick are shapes drawn in logical sizes: scale the drawing
                GraphicsState gs0 = g.Save();
                float k0 = Dpi.Aware ? Dpi.Scale : 1f;
                g.ScaleTransform(k0, k0);
                if (state == "run") DrawSpinner(g, cx, cy, sc); else DrawResult(g, cx, cy, sc);
                g.Restore(gs0);
            }

            int tx = Dpi.S(64);
            int rightPad = state == "run" ? Dpi.S(74) : Dpi.S(46);
            using (Font f = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, title, f, new Rectangle(tx, Dpi.S(7), Math.Max(Dpi.S(10), Width - tx - rightPad), Dpi.S(20)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            using (Font f2 = new Font("Segoe UI", 9f))
            {
                TextRenderer.DrawText(g, detail, f2, new Rectangle(tx, Dpi.S(27), Math.Max(Dpi.S(10), Width - tx - Dpi.S(22)), Dpi.S(28)), Theme.Text2, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                if (state == "run")
                {
                    int secs = (int)(DateTime.UtcNow - started).TotalSeconds;
                    if (secs >= 2) TextRenderer.DrawText(g, secs + " s", f2, new Rectangle(Width - Dpi.S(70), Dpi.S(7), Dpi.S(54), Dpi.S(20)), Theme.Text2, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
            if (state != "run") Gfx.Glyph(g, Ico.Cancel, new Rectangle(Width - Dpi.S(38), Dpi.S(5), Dpi.S(24), Dpi.S(24)), closeHover ? Theme.Text : Theme.Text2, 9f);

            // the bar: it fills as the job moves on, or sweeps back and forth when the length is not known
            Rectangle track = new Rectangle(tx, HP - Dpi.S(11), Width - tx - Dpi.S(20), Dpi.S(4));
            using (GraphicsPath tp = Gfx.Rounded(track, Dpi.S(4)))
            {
                using (Brush tb = new SolidBrush(Theme.Border)) g.FillPath(tb, tp);
                g.SetClip(tp);
                float pos = Fx.Clamp(sweep / 1.5f);
                if (state == "run" && target < 0f)
                {
                    float segW = track.Width * 0.30f;
                    float x = track.X - segW + (track.Width + segW) * (pos * pos * (3f - 2f * pos));
                    using (Brush sb = new SolidBrush(sc)) g.FillRectangle(sb, x, track.Y, segW, track.Height);
                }
                else
                {
                    float fw = track.Width * Fx.Clamp(frac);
                    if (fw > 0.5f)
                    {
                        using (Brush fb = new SolidBrush(sc)) g.FillRectangle(fb, track.X, track.Y, fw, track.Height);
                        if (state == "run")
                        {
                            g.IntersectClip(new RectangleF(track.X, track.Y - 1, fw, track.Height + 2));
                            float shW = Dpi.F(70f), sx = track.X - shW + (fw + shW) * pos;
                            using (Brush hb = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) g.FillRectangle(hb, sx, track.Y, shW, track.Height);
                        }
                    }
                }
                g.ResetClip();
            }
        }
    }

    // ---------------------------------------------------------------- a rounded card with a small title and an icon
    class Card : Panel
    {
        public string Title = "";
        public string Glyph = "";
        public bool Warn = false;
        public Color GlyphColor = Color.Empty;

        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.Rounded(r, Dpi.S(14)))
            using (Brush b = new SolidBrush(Theme.Bg2))
                g.FillPath(b, p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.Rounded(r, Dpi.S(14)))
            using (Pen pen = new Pen(Warn ? Theme.Bad : Theme.Border, Warn ? Dpi.F(1.6f) : 1f))
                g.DrawPath(pen, p);
            int x = Dpi.S(16);
            if (Glyph.Length > 0) { Gfx.Glyph(g, Glyph, new Rectangle(x - Dpi.S(2), Dpi.S(10), Dpi.S(26), Dpi.S(26)), GlyphColor.IsEmpty ? (Warn ? Theme.Bad : Theme.AccentText) : GlyphColor, 13f); x += Dpi.S(28); }
            if (Title.Length > 0)
                using (Font f = new Font("Segoe UI", 10.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, Title, f, new Rectangle(x, Dpi.S(10), Math.Max(Dpi.S(10), Width - x - Dpi.S(12)), Dpi.S(26)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // ---------------------------------------------------------------- small custom controls
    class ChromeTab : Control
    {
        public string Glyph = "";
        bool active;
        readonly Fader ac, hv;

        public bool Active
        {
            get { return active; }
            set
            {
                if (active == value) return;
                active = value;
                float v = value ? 1f : 0f;
                if (IsHandleCreated && Visible) ac.To(v); else ac.Set(v);
                Invalidate();
            }
        }

        public ChromeTab(string glyph, string text)
        {
            Glyph = glyph; Text = Loc.T(text); Height = 38; Cursor = Cursors.Hand; Font = new Font("Segoe UI", 10f);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            Width = Dpi.TextW(Text, Font) + 62;
            ac = new Fader(this); hv = new Fader(this);
        }
        protected override void OnMouseEnter(EventArgs e) { hv.To(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hv.To(0f); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = Parent != null ? Parent.BackColor : Theme.Frame;
            g.Clear(bg);
            float a = ac.Value, h = hv.Value * (1f - a);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height);
            if (a > 0.01f)
            {
                using (GraphicsPath p = Gfx.RoundedTop(r, Dpi.S(14))) using (Brush b = new SolidBrush(Fx.Mix(bg, Theme.Bg1, a))) g.FillPath(b, p);
                int bw = (int)((Width - Dpi.S(29)) * a);
                using (Brush ab = new SolidBrush(Theme.Primary)) g.FillRectangle(ab, (Width - bw) / 2, 0, bw, Dpi.S(3));
            }
            if (h > 0.01f)
            {
                Rectangle hr = new Rectangle(Dpi.S(4), Dpi.S(4), Width - Dpi.S(9), Height - Dpi.S(8));
                using (GraphicsPath p2 = Gfx.Rounded(hr, Dpi.S(10))) using (Brush b2 = new SolidBrush(Fx.Mix(bg, Theme.Bg3, h))) g.FillPath(b2, p2);
            }
            Gfx.Glyph(g, Glyph, new Rectangle(Dpi.S(14), 0, Dpi.S(24), Height), Fx.Mix(Theme.Text2, Theme.AccentText, a), 12f);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Dpi.S(44), 0, Width - Dpi.S(48), Height), Fx.Mix(Theme.Text2, Theme.Text, a), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    // a small icon button that can be "on" (the Appearance choice)
    class GlyphButton : Control
    {
        public string Glyph = "";
        bool selected;
        readonly Fader sel, hv;

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                float v = value ? 1f : 0f;
                if (IsHandleCreated && Visible) sel.To(v); else sel.Set(v);
                Invalidate();
            }
        }

        public GlyphButton(string glyph)
        {
            Glyph = glyph; Size = new Size(36, 30); Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            sel = new Fader(this); hv = new Fader(this);
        }
        protected override void OnMouseEnter(EventArgs e) { hv.To(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hv.To(0f); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = Parent != null ? Parent.BackColor : Theme.Frame;
            g.Clear(bg);
            float s = sel.Value, h = hv.Value;
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            if (s > 0.01f || h > 0.01f)
                using (GraphicsPath p = Gfx.Rounded(r, Dpi.S(10))) using (Brush b = new SolidBrush(Fx.Mix(Fx.Mix(bg, Theme.Bg4, h), Theme.Primary, s))) g.FillPath(b, p);
            Gfx.Glyph(g, Glyph, new Rectangle(0, 0, Width, Height), Fx.Mix(Theme.Text2, Color.White, s), 11f);
        }
    }

    // minimise, maximise, restore and close
    class CaptionButton : Control
    {
        public string Kind = "close";    // min, max, restore, close
        readonly Fader hv;

        public CaptionButton(string kind)
        {
            Kind = kind; Width = 46;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            TabStop = false;
            hv = new Fader(this);
        }
        protected override void OnMouseEnter(EventArgs e) { hv.To(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hv.To(0f); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float h = hv.Value;
            bool isClose = Kind == "close";
            Color bg = Parent != null ? Parent.BackColor : Theme.Frame;
            g.Clear(Fx.Mix(bg, isClose ? Color.FromArgb(196, 43, 28) : Theme.Bg4, h));
            Color c = isClose ? Fx.Mix(Theme.Text, Color.White, h) : Theme.Text;
            int cx = Width / 2, cy = Height / 2;
            int d5 = Dpi.S(5), d2 = Dpi.S(2), d3 = Dpi.S(3), d8 = Dpi.S(8), d10 = Dpi.S(10);
            using (Pen p = new Pen(c, 1f))
            {
                if (Kind == "min") g.DrawLine(p, cx - d5, cy, cx + d5, cy);
                else if (Kind == "max") g.DrawRectangle(p, cx - d5, cy - d5, d10, d10);
                else if (Kind == "restore")
                {
                    g.DrawRectangle(p, cx - d5, cy - d2, d8, d8);
                    g.DrawLine(p, cx - d2, cy - d5, cx + d5, cy - d5);
                    g.DrawLine(p, cx + d5, cy - d5, cx + d5, cy + d3);
                }
                else
                {
                    g.DrawLine(p, cx - d5, cy - d5, cx + d5, cy + d5);
                    g.DrawLine(p, cx + d5, cy - d5, cx - d5, cy + d5);
                }
            }
        }
    }

    // a small rounded text button that can be "on" (the log filter)
    class Pill : Control
    {
        bool selected;
        readonly Fader sel, hv;

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                float v = value ? 1f : 0f;
                if (IsHandleCreated && Visible) sel.To(v); else sel.Set(v);
                Invalidate();
            }
        }

        public Pill(string text)
        {
            Text = Loc.T(text); Height = 26; Cursor = Cursors.Hand; Font = new Font("Segoe UI", 9f);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            Width = Dpi.TextW(Text, Font) + 22;
            sel = new Fader(this); hv = new Fader(this);
        }
        protected override void OnMouseEnter(EventArgs e) { hv.To(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hv.To(0f); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            float s = sel.Value, h = hv.Value;
            Color fill = Fx.Mix(Fx.Mix(Theme.Bg3, Theme.Bg4, h), Theme.Primary, s);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.Rounded(r, Height - 1))
            {
                using (Brush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (s < 0.99f) using (Pen pen = new Pen(Fx.Mix(Theme.Border, fill, s))) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width - 1, Height - 1), Fx.Mix(Theme.Text2, Color.White, s), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // a big clickable tile (the start window)
    class ToolTile : Control
    {
        public string Glyph = "";
        public string Title = "";
        public string Desc = "";
        readonly Fader hv;

        public ToolTile(string glyph, string title, string desc)
        {
            Glyph = glyph; Title = Loc.T(title); Desc = Loc.T(desc); Height = 78; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            hv = new Fader(this);
        }
        protected override void OnMouseEnter(EventArgs e) { hv.To(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hv.To(0f); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            float h = hv.Value;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.Rounded(r, Dpi.S(14)))
            {
                using (Brush b = new SolidBrush(Fx.Mix(Theme.Bg2, Theme.Bg4, h))) g.FillPath(b, p);
                using (Pen pen = new Pen(Fx.Mix(Theme.Border, Theme.Primary, h), 1f + 0.6f * h)) g.DrawPath(pen, p);
            }
            Gfx.Glyph(g, Glyph, new Rectangle(Dpi.S(14) + (int)(Dpi.F(4) * h), 0, Dpi.S(44), Height), Theme.AccentText, 20f);
            using (Font f = new Font("Segoe UI", 11.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, Title, f, new Rectangle(Dpi.S(70) + (int)(Dpi.F(2) * h), Dpi.S(12), Width - Dpi.S(84), Dpi.S(28)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            using (Font f2 = new Font("Segoe UI", 9.5f))
                TextRenderer.DrawText(g, Desc, f2, new Rectangle(Dpi.S(70) + (int)(Dpi.F(2) * h), Dpi.S(42), Width - Dpi.S(84), Dpi.S(28)), Theme.Text2, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    // a themed drop-down: it looks like the other boxes, and its list is in the theme colours too
    class DropBox : Control
    {
        public List<string> Items = new List<string>();
        int index = -1;
        bool open;
        readonly Fader hv;
        public event EventHandler SelectedIndexChanged;

        public DropBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.StandardClick, true);
            Height = 30; Cursor = Cursors.Hand; Font = new Font("Segoe UI", 9.5f);
            hv = new Fader(this);
        }

        public int SelectedIndex
        {
            get { return index; }
            set
            {
                if (value == index) return;
                index = value; Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }
        public string SelectedItem { get { return index >= 0 && index < Items.Count ? Items[index] : null; } }

        protected override void OnMouseEnter(EventArgs e) { hv.To(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hv.To(0f); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Enabled || Items.Count == 0) return;
            ContextMenuStrip m = ThemeMenu.Create();
            for (int i = 0; i < Items.Count; i++)
            {
                int pick = i;
                ToolStripMenuItem it = ThemeMenu.Item(Items[i], i == index);
                it.Click += delegate { SelectedIndex = pick; };
                m.Items.Add(it);
            }
            m.MinimumSize = new Size(Width, 0);
            m.Closed += delegate { open = false; Invalidate(); };
            open = true; Invalidate();
            m.Show(this, new Point(0, Height + Dpi.S(2)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg2);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color border = Fx.Mix(Theme.Border, Theme.Primary, open ? 1f : hv.Value * 0.8f);
            using (GraphicsPath p = Gfx.Rounded(r, Dpi.S(8)))
            {
                using (Brush b = new SolidBrush(Enabled ? Theme.Bg3 : Theme.Bg2)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
            string s = SelectedItem ?? "";
            TextRenderer.DrawText(g, s, Font, new Rectangle(Dpi.S(10), 0, Width - Dpi.S(44), Height), Enabled ? Theme.Text : Theme.Text2, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            Gfx.Glyph(g, Ico.Chevron, new Rectangle(Width - Dpi.S(32), 0, Dpi.S(24), Height), Theme.Text2, 9f);
        }
    }

    // a number box in the program's colours: a real text box inside (so you can select, type, copy and paste), with two small arrow buttons
    class NumBox : Control
    {
        public decimal Minimum = 0, Maximum = 100;
        decimal val;
        int hot = 0;                    // 1 = the up arrow is under the mouse, 2 = the down arrow
        int held = 0;                   // an arrow is held down: 1 up, 2 down
        readonly TextBox tb = new TextBox();
        readonly Fader hv, fo;
        readonly System.Windows.Forms.Timer repeat = new System.Windows.Forms.Timer();
        public event EventHandler ValueChanged;

        public NumBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 36; Width = 100; Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            hv = new Fader(this); fo = new Fader(this);
            tb.BorderStyle = BorderStyle.None; tb.MaxLength = 4; tb.Tag = "numtext"; tb.Text = "0"; tb.TabStop = true;
            tb.KeyPress += delegate(object s, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            tb.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Up) { Bump(1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { Bump(-1); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { Commit(); tb.SelectAll(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            tb.MouseWheel += delegate(object s, MouseEventArgs e) { if (Enabled) Bump(e.Delta > 0 ? 1 : -1); };
            tb.GotFocus += delegate { fo.To(1f); tb.BeginInvoke(new Action(delegate { tb.SelectAll(); })); };   // the whole number is selected when the box gets focus
            tb.LostFocus += delegate { Commit(); fo.To(0f); };
            tb.MouseEnter += delegate { hv.To(1f); };
            tb.MouseLeave += delegate { hv.To(0f); };
            Controls.Add(tb);
            repeat.Interval = 380;
            repeat.Tick += delegate { if (held != 0) { Bump(held == 1 ? 1 : -1); repeat.Interval = 60; } };
        }

        decimal Clamp(decimal v) { return v < Minimum ? Minimum : v > Maximum ? Maximum : v; }

        public decimal Value
        {
            get { return val; }
            set
            {
                decimal v = Clamp(value);
                bool changed = v != val;
                val = v;
                if (tb.Text != val.ToString()) tb.Text = val.ToString();
                Invalidate();
                if (changed) { EventHandler h = ValueChanged; if (h != null) h(this, EventArgs.Empty); }
            }
        }

        void Commit()
        {
            decimal v;
            if (decimal.TryParse(tb.Text, out v)) Value = v; else tb.Text = val.ToString();
        }

        void Bump(int d) { Commit(); Value = val + d; tb.SelectAll(); }

        bool InArrows(Point p) { return p.X >= Width - Dpi.S(28); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            tb.Font = Font;
            tb.Left = Dpi.S(12); tb.Width = Math.Max(Dpi.S(10), Width - Dpi.S(44));
            tb.Top = Math.Max(0, (Height - tb.Height) / 2);
        }

        protected override void OnEnabledChanged(EventArgs e) { tb.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hv.To(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hv.To(0f); hot = 0; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = InArrows(e.Location) ? (e.Y < Height / 2 ? 1 : 2) : 0;
            if (h != hot) { hot = h; Invalidate(); }
            Cursor = h != 0 ? Cursors.Default : Cursors.IBeam;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            if (InArrows(e.Location))
            {
                tb.Focus();
                held = e.Y < Height / 2 ? 1 : 2;
                Bump(held == 1 ? 1 : -1);
                repeat.Interval = 380; repeat.Start();
            }
            else tb.Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e) { held = 0; repeat.Stop(); base.OnMouseUp(e); }

        protected override void OnMouseWheel(MouseEventArgs e) { if (Enabled) Bump(e.Delta > 0 ? 1 : -1); base.OnMouseWheel(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Enabled ? Theme.Bg3 : Theme.Bg2;
            if (tb.BackColor != fill) tb.BackColor = fill;
            Color fore = Enabled ? Theme.Text : Theme.Text2;
            if (tb.ForeColor != fore) tb.ForeColor = fore;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg2);
            Color border = Fx.Mix(Fx.Mix(Theme.Border, Theme.Primary, hv.Value * 0.5f), Theme.Primary, fo.Value);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Gfx.Rounded(r, Dpi.S(8)))
            {
                using (Brush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (Enabled && hot != 0)
                {
                    g.SetClip(p);
                    int a28 = Dpi.S(28);
                    Rectangle half = hot == 1 ? new Rectangle(Width - a28, 0, a28, Height / 2) : new Rectangle(Width - a28, Height / 2, a28, Height - Height / 2);
                    using (Brush hb = new SolidBrush(Theme.Bg4)) g.FillRectangle(hb, half);
                    g.ResetClip();
                }
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
            using (Pen sep = new Pen(Theme.Border)) g.DrawLine(sep, Width - Dpi.S(29), Dpi.S(5), Width - Dpi.S(29), Height - Dpi.S(6));
            Color arrowUp = Enabled ? (hot == 1 ? Theme.Text : Theme.Text2) : Theme.Border;
            Color arrowDown = Enabled ? (hot == 2 ? Theme.Text : Theme.Text2) : Theme.Border;
            Gfx.Glyph(g, Ico.ChevUp, new Rectangle(Width - Dpi.S(28), 0, Dpi.S(27), Height / 2), arrowUp, 8f);
            Gfx.Glyph(g, Ico.Chevron, new Rectangle(Width - Dpi.S(28), Height / 2, Dpi.S(27), Height - Height / 2), arrowDown, 8f);
        }
    }

    // an on/off switch with a sliding knob
    class ToggleSwitch : Control
    {
        bool on;
        readonly Fader f, hv;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Size = new Size(48, 26); Cursor = Cursors.Hand; TabStop = true;
            f = new Fader(this); hv = new Fader(this);
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                float v = value ? 1f : 0f;
                if (IsHandleCreated && Visible) f.To(v); else f.Set(v);
                Invalidate();
                EventHandler h = CheckedChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { if (Enabled) { Focus(); Checked = !on; } base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (Enabled && e.KeyCode == Keys.Space) { Checked = !on; e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hv.To(1f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hv.To(0f); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            Color off = Fx.Mix(Theme.Bg4, Theme.Border, 0.5f + 0.3f * hv.Value);
            Color track = Fx.Mix(off, Theme.Primary, f.Value);
            Rectangle r = new Rectangle(0, Dpi.S(1), Width - 1, Height - Dpi.S(3));
            using (GraphicsPath p = Gfx.Rounded(r, r.Height))
            {
                using (Brush b = new SolidBrush(Enabled ? track : Fx.Mix(track, Theme.Bg1, 0.5f))) g.FillPath(b, p);
                if (Focused) using (Pen pen = new Pen(Theme.AccentText, Dpi.F(1.5f))) g.DrawPath(pen, p);
            }
            int kd = r.Height - Dpi.S(6);
            float kx = Dpi.F(3f) + (Width - 1 - kd - Dpi.S(6)) * f.Value;
            using (Brush kb = new SolidBrush(Fx.Mix(Theme.Text2, Color.White, f.Value))) g.FillEllipse(kb, kx, r.Y + Dpi.S(3), kd, kd);
        }
    }

    // pop-up menus in the theme colours
    class ThemeColors : ProfessionalColorTable
    {
        public ThemeColors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return Theme.Bg3; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Bg3; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Bg3; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Bg3; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Bg4; } }
        public override Color MenuItemSelected { get { return Theme.Bg4; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Bg4; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Bg4; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Bg4; } }
        public override Color MenuItemPressedGradientMiddle { get { return Theme.Bg4; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Bg4; } }
        public override Color CheckBackground { get { return Theme.Primary; } }
        public override Color CheckSelectedBackground { get { return Theme.Primary; } }
        public override Color CheckPressedBackground { get { return Theme.Primary; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }

    static class ThemeMenu
    {
        public static ContextMenuStrip Create()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            ToolStripProfessionalRenderer rd = new ToolStripProfessionalRenderer(new ThemeColors());
            rd.RoundedEdges = false;
            m.Renderer = rd;
            m.BackColor = Theme.Bg3; m.ForeColor = Theme.Text; m.Font = new Font("Segoe UI", 9.5f);
            m.ShowImageMargin = false; m.ShowCheckMargin = true;
            return m;
        }

        public static ToolStripMenuItem Item(string text, bool check)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Checked = check; it.ForeColor = Theme.Text; it.BackColor = Theme.Bg3;
            it.Padding = new Padding(Dpi.S(2), Dpi.S(5), Dpi.S(2), Dpi.S(5));
            return it;
        }
    }

    // the red X in the welcome window
    class RedXIcon : Control
    {
        public RedXIcon()
        {
            Size = new Size(40, 40);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            using (Brush b = new SolidBrush(Color.FromArgb(229, 72, 77))) g.FillEllipse(b, Dpi.S(2), Dpi.S(2), Width - Dpi.S(5), Height - Dpi.S(5));
            using (Pen p = new Pen(Color.White, Dpi.F(3.2f)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                float a = Width * 0.32f, z = Width * 0.68f;
                g.DrawLine(p, a, a, z, z);
                g.DrawLine(p, z, a, a, z);
            }
        }
    }

    // ---------------------------------------------------------------- small helpers for laying out text
    static class UiUtil
    {
        // A label that wraps at a given width; returns the bottom edge so the next item can follow.
        public static int Wrapped(Control parent, string text, int left, int top, int width, Font font, string tag, out Label label)
        {
            text = Loc.T(text);
            label = new Label();
            label.AutoSize = false; label.Text = text; label.Left = left; label.Top = top; label.Width = width; label.Font = font; label.Tag = tag;
            int h = Dpi.L(TextRenderer.MeasureText(text, font, new Size(Dpi.S(width), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height);
            label.Height = h + 4;
            parent.Controls.Add(label);
            return top + label.Height;
        }

        // A bullet list like markdown: the bullet stands alone and the text lines up under itself.
        public static int Bullet(Control parent, string text, int left, int top, int width, Font font)
        {
            Label b = new Label(); b.AutoSize = false; b.Text = Ico.Bullet; b.Left = left; b.Top = top; b.Width = 18; b.Height = 22; b.Font = font; b.Tag = "accent";
            parent.Controls.Add(b);
            Label t;
            int bottom = Wrapped(parent, text, left + 20, top, width - 20, font, null, out t);
            return bottom + 4;
        }
    }

    // ---------------------------------------------------------------- a window with its own title bar (the tabs live in it, like a browser)
    class ChromeForm : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

        public Panel TitleBar;
        public Panel Body;
        public Action IconClick;
        protected PictureBox IconBox { get { return iconBox; } }
        protected bool FadeIn = false;
        protected bool IgnoreShowMe = false;
        protected CaptionButton btnMin, btnMax, btnClose;
        Label titleLabel;
        PictureBox iconBox;
        bool canResize;
        int edge;
        Point startCursor;
        Rectangle startBounds;
        const int Border = 6;
        bool dpiScaled;
        // a size in screen pixels once this window is scaled; before that the window is still in logical sizes (code that runs before and after the scaling uses this)
        protected int S(int v) { return dpiScaled ? Dpi.S(v) : v; }
        public bool IsDpiScaled { get { return dpiScaled; } }

        // allow minimising from the taskbar, and a drop shadow, on a window without a normal frame
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x20000 | 0x80000;
                cp.ClassStyle |= 0x20000;
                return cp;
            }
        }

        public ChromeForm(bool resizable, bool minMax, int titleHeight, bool showTitle)
        {
            canResize = resizable;
            FadeIn = titleHeight < 46;   // the small windows fade in, the main window does not
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(Border, 0, Border, Border);

            Body = new Panel(); Body.Dock = DockStyle.Fill; Body.Tag = "body"; Controls.Add(Body);
            TitleBar = new Panel(); TitleBar.Dock = DockStyle.Top; TitleBar.Height = titleHeight; TitleBar.Tag = "frame"; Controls.Add(TitleBar);

            // caption buttons: the one added last sits at the far right
            if (minMax)
            {
                btnMin = new CaptionButton("min"); btnMin.Dock = DockStyle.Right; btnMin.Click += delegate { WindowState = FormWindowState.Minimized; }; TitleBar.Controls.Add(btnMin);
                btnMax = new CaptionButton("max"); btnMax.Dock = DockStyle.Right; btnMax.Click += delegate { ToggleMax(); }; TitleBar.Controls.Add(btnMax);
            }
            btnClose = new CaptionButton("close"); btnClose.Dock = DockStyle.Right; btnClose.Click += delegate { Close(); }; TitleBar.Controls.Add(btnClose);

            Draggable(TitleBar);
            Image img = null;
            Icon ic = Info.AppIcon();
            if (ic != null) { try { img = new Bitmap(ic.ToBitmap(), Dpi.S(18), Dpi.S(18)); } catch (Exception) { } }
            if (img != null)
            {
                iconBox = new PictureBox(); iconBox.Image = img; iconBox.SizeMode = PictureBoxSizeMode.CenterImage; iconBox.BackColor = Color.Transparent;
                iconBox.Left = 12; iconBox.Top = (titleHeight - 22) / 2; iconBox.Width = 22; iconBox.Height = 22;
                TitleBar.Controls.Add(iconBox);
                iconBox.MouseDown += delegate(object s2, MouseEventArgs e2)
                {
                    if (e2.Button != MouseButtons.Left) return;
                    if (IconClick != null) { IconClick(); return; }
                    if (e2.Clicks == 2 && btnMax != null) { ToggleMax(); return; }
                    ReleaseCapture();
                    SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
                };
            }
            if (showTitle)
            {
                titleLabel = new Label(); titleLabel.AutoSize = false; titleLabel.Left = 40; titleLabel.Top = 0; titleLabel.Width = 420; titleLabel.Height = titleHeight;
                titleLabel.TextAlign = ContentAlignment.MiddleLeft; titleLabel.Font = new Font("Segoe UI", 9.5f);
                TitleBar.Controls.Add(titleLabel); Draggable(titleLabel);
            }
        }

        // dragging the title bar moves the window; a double click maximises it
        public void Draggable(Control c)
        {
            c.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                if (e.Clicks == 2 && btnMax != null) { ToggleMax(); return; }
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // WM_NCLBUTTONDOWN on the caption
            };
        }

        void ToggleMax()
        {
            if (WindowState == FormWindowState.Maximized) { WindowState = FormWindowState.Normal; return; }
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (titleLabel != null) titleLabel.Text = Text; }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (btnMax != null) { btnMax.Kind = WindowState == FormWindowState.Maximized ? "restore" : "max"; btnMax.Invalidate(); }
            Padding = WindowState == FormWindowState.Maximized ? new Padding(0) : new Padding(S(Border), 0, S(Border), S(Border));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch (Exception) { }
        }

        protected override void OnLoad(EventArgs e)
        {
            // the window and everything in it was built in logical sizes (100 percent): scale all of it once, now that it is complete
            if (Dpi.Aware && Dpi.Scale > 1.001f && !dpiScaled)
            {
                dpiScaled = true;
                Scale(new SizeF(Dpi.Scale, Dpi.Scale));
                if (StartPosition == FormStartPosition.CenterScreen) CenterToScreen();
                else if (StartPosition == FormStartPosition.CenterParent) CenterToParent();
                FixGrids(this);
            }
            if (FadeIn) Opacity = 0;
            base.OnLoad(e);
        }

        // a grid keeps its own header and row heights when the window is scaled: set the same logical sizes as MakeGrid uses, scaled
        static void FixGrids(Control root)
        {
            foreach (Control c in root.Controls)
            {
                DataGridView g = c as DataGridView;
                if (g != null)
                {
                    g.ColumnHeadersHeight = Dpi.S(26); g.RowTemplate.Height = Dpi.S(24);
                    foreach (DataGridViewRow gr in g.Rows) gr.Height = Dpi.S(24);
                }
                FixGrids(c);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (FadeIn) Fx.Run(180, delegate(float p) { Opacity = Fx.EaseOut(p); }, null);
        }

        // The resize cursor only shows on the thin edge strips, which belong to the window itself.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == SingleInstance.Msg)
            {
                // another copy was started: come to the front (a window that waits for an answer is enabled, the window behind it is not)
                if (!IgnoreShowMe && Visible && Enabled)
                {
                    if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                    Activate(); BringToFront(); SetForegroundWindow(Handle);
                }
                return;
            }
            if (m.Msg == 0x20 && canResize)   // WM_SETCURSOR
            {
                int h = edge;
                if (h == 0) { try { h = HitEdge(PointToClient(Cursor.Position)); } catch (Exception) { h = 0; } }
                if (h != 0)
                {
                    Cursor.Current = (h == 1 || h == 2) ? Cursors.SizeWE : h == 3 ? Cursors.SizeNS : h == 4 ? Cursors.SizeNESW : Cursors.SizeNWSE;
                    m.Result = (IntPtr)1;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        // ---- resizing from the thin strips at the left, right and bottom
        int HitEdge(Point p)
        {
            if (!canResize || WindowState != FormWindowState.Normal) return 0;
            int w = ClientSize.Width, h = ClientSize.Height;
            int bd = S(Border), corner = S(18);
            bool l = p.X < bd, r = p.X >= w - bd, b = p.Y >= h - bd;
            if (b && p.X < corner) return 4;
            if (b && p.X >= w - corner) return 5;
            if (l) return 1;
            if (r) return 2;
            if (b) return 3;
            return 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (edge != 0) { ResizeTo(); return; }
            // (the resize cursor is chosen in WndProc. Setting Form.Cursor here made every control inside the window inherit it and keep it.)
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int h = HitEdge(e.Location);
            if (h != 0) { edge = h; startCursor = Cursor.Position; startBounds = Bounds; Capture = true; }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (edge != 0) { edge = 0; Capture = false; }
        }

        void ResizeTo()
        {
            Point cur = Cursor.Position;
            int dx = cur.X - startCursor.X, dy = cur.Y - startCursor.Y;
            int minW = MinimumSize.Width > 0 ? MinimumSize.Width : 300, minH = MinimumSize.Height > 0 ? MinimumSize.Height : 200;
            int left = startBounds.Left, width = startBounds.Width, height = startBounds.Height;
            if (edge == 1 || edge == 4) { width = Math.Max(minW, startBounds.Width - dx); left = startBounds.Right - width; }
            if (edge == 2 || edge == 5) width = Math.Max(minW, startBounds.Width + dx);
            if (edge == 3 || edge == 4 || edge == 5) height = Math.Max(minH, startBounds.Height + dy);
            Bounds = new Rectangle(left, startBounds.Top, width, height);
        }
    }

    // ---------------------------------------------------------------- the log (used at the bottom of the window and in the pop-out window)
    class LogPanel : Panel
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        List<Pill> pills = new List<Pill>();
        Pill debugPill;
        RichTextBox box;
        ToolTip tip = new ToolTip();
        System.Windows.Forms.Timer refreshTimer;
        volatile bool dirty = false;
        int filter = 0;
        float charPx = 7f;
        Dictionary<int, bool> expanded = new Dictionary<int, bool>();
        Dictionary<int, bool> openNow = new Dictionary<int, bool>();
        List<int[]> headerRanges = new List<int[]>();
        public event Action PopOut;
        public event Action ReportCopied;

        // A finished job is folded into one line. With StayOpenOnTab (the log in the main window) the jobs that were started while you are on a tab
        // stay open until you change to another tab; then they fold. A job you opened or folded by hand stays as you left it, and a job with a problem stays open.
        public bool StayOpenOnTab = false;
        int tabGen = 0;
        readonly Dictionary<int, int> seenGen = new Dictionary<int, int>();
        public void TabChanged() { lock (seenGen) { tabGen++; } dirty = true; }

        // for --log-fold-test
        public void TestRebuild() { Rebuild(); }
        public string TestText() { return box.Text; }
        public void TestToggle(int g) { Toggle(g); }

        static readonly string[] ToolNames = new string[] { null, "FiveM cache", "Frame cap", "DNS", "Graphics", "General" };

        static IconButton Small(string glyph, int left)
        {
            IconButton b = new IconButton();
            b.Glyph = glyph; b.Text = ""; b.Left = left; b.Top = 4; b.Width = 38; b.Height = 30;
            return b;
        }

        public LogPanel(bool debugOn, bool popOutButton)
        {
            Dock = DockStyle.Fill;
            Panel bar = new Panel(); bar.Dock = DockStyle.Top; bar.Height = 40; Controls.Add(bar);

            string[] labels = new string[] { "All", "FiveM cache", "Frame cap", "DNS", "Graphics", "General" };
            int x = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                Pill p = new Pill(labels[i]); p.Left = x; p.Top = 7; p.Selected = i == 0;
                int idx = i;
                p.Click += delegate { SetFilter(idx); };
                bar.Controls.Add(p); pills.Add(p);
                x += p.Width + 6;
            }
            debugPill = new Pill("Debug lines"); debugPill.Left = x + 10; debugPill.Top = 7; debugPill.Selected = debugOn;
            debugPill.Click += delegate { debugPill.Selected = !debugPill.Selected; debugPill.Invalidate(); Rebuild(); };
            bar.Controls.Add(debugPill);

            Panel right = new Panel(); right.Dock = DockStyle.Right; right.Width = popOutButton ? 204 : 164; bar.Controls.Add(right);
            IconButton report = Small(Ico.Report, 0); tip.SetToolTip(report, Loc.T("Copy a report for support: the whole log with all details, ready to paste"));
            report.Click += delegate
            {
                try
                {
                    string text = BuildReport();
                    Clipboard.SetText(text);
                    AppLog.Add("Report copied to the clipboard (" + (text.Length / 1024 + 1) + " KB).");
                    Action done = ReportCopied; if (done != null) done();
                }
                catch (Exception ex) { AppLog.Debug("copy report failed: " + ex.Message); }
            };
            right.Controls.Add(report);
            IconButton copy = Small(Ico.Copy, 40); tip.SetToolTip(copy, Loc.T("Copy the log to the clipboard"));
            copy.Click += delegate { try { Clipboard.SetText(box.Text); AppLog.Add("Log copied to the clipboard."); } catch (Exception ex) { AppLog.Debug("copy failed: " + ex.Message); } };
            right.Controls.Add(copy);
            IconButton open = Small(Ico.Document, 80); tip.SetToolTip(open, Loc.T("Open the log file with the program Windows uses for .txt files"));
            open.Click += delegate
            {
                try { ProcessStartInfo psi = new ProcessStartInfo(AppLog.FilePath); psi.UseShellExecute = true; Process.Start(psi); }
                catch (Exception ex) { AppLog.Debug("open log file failed: " + ex.Message); }
            };
            right.Controls.Add(open);
            IconButton folder = Small(Ico.OpenFolder, 120); tip.SetToolTip(folder, Loc.T("Show the log file in its folder"));
            folder.Click += delegate { try { Process.Start("explorer.exe", "/select,\"" + AppLog.FilePath + "\""); } catch (Exception) { } };
            right.Controls.Add(folder);
            if (popOutButton)
            {
                IconButton pop = Small(Ico.PopOut, 160); tip.SetToolTip(pop, Loc.T("Open the log in its own window"));
                pop.Click += delegate { Action h = PopOut; if (h != null) h(); };
                right.Controls.Add(pop);
            }

            box = new RichTextBox();
            box.ReadOnly = true; box.BorderStyle = BorderStyle.None; box.WordWrap = true; box.ScrollBars = RichTextBoxScrollBars.Vertical;
            box.DetectUrls = false; box.Font = new Font("Consolas", 9f); box.Dock = DockStyle.Fill;
            box.MouseDown += BoxMouseDown;
            Card holder = new Card(); holder.Dock = DockStyle.Fill; holder.Padding = new Padding(12, 8, 8, 8);
            holder.Controls.Add(box);
            Controls.Add(holder);
            holder.BringToFront();
            charPx = TextRenderer.MeasureText("0000000000", box.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width / 10f;

            refreshTimer = new System.Windows.Forms.Timer(); refreshTimer.Interval = 200;
            refreshTimer.Tick += delegate { if (dirty) { dirty = false; Rebuild(); } };
            refreshTimer.Start();
            AppLog.Added += OnAdded;
            AppLog.GroupEnded += OnGroupEnded;
            Disposed += delegate { AppLog.Added -= OnAdded; AppLog.GroupEnded -= OnGroupEnded; refreshTimer.Stop(); tip.Dispose(); };
            VisibleChanged += delegate { if (Visible) dirty = true; };
            Resize += delegate { dirty = true; };
            box.HandleCreated += delegate { dirty = true; };
            Rebuild();
        }

        void OnAdded(LogEntry e) { if (e.Group != 0) lock (seenGen) { if (!seenGen.ContainsKey(e.Group)) seenGen[e.Group] = tabGen; } dirty = true; }
        void OnGroupEnded() { dirty = true; }

        void SetFilter(int idx)
        {
            filter = idx;
            for (int i = 0; i < pills.Count; i++) { pills[i].Selected = i == idx; pills[i].Invalidate(); }
            Rebuild();
        }

        bool Matches(LogEntry e)
        {
            string t = ToolNames[filter];
            if (t != null && e.Tool != t) return false;
            if (e.Debug && !debugPill.Selected) return false;
            return true;
        }

        // 0 normal, 1 good (green), 2 light problem (yellow), 3 warning (orange), 4 error (red), 5 something really broken (purple)
        static readonly Regex RxBroken = new Regex("exception|unexpected error|unhandled", RegexOptions.IgnoreCase);
        static readonly Regex RxError = new Regex(@"\berror\b|failed|did not finish|could not be removed|could not confirm|could not find this profile|could not start|could not read|could not measure|could not close|stopped:|stop:|access is denied|timed out|finished with code [1-9]|exit code [1-9]|cannot", RegexOptions.IgnoreCase);
        static readonly Regex RxWarn = new Regex(@"warning|you cancelled|not in radeon|is off for this build|no cap values|no fivem build found|skipped|not an allowed place|nothing was changed|did not report", RegexOptions.IgnoreCase);
        static readonly Regex RxLight = new Regex(@"still reports|did not show the new cap|skip profile|not ready yet|cancel|chose not to|check \d+ of", RegexOptions.IgnoreCase);
        static readonly Regex RxGood = new Regex(@"^done\b|^finished|backup saved|set to \d+\.|already set to|removed \(|now reports cap|successfully flushed|exit code 0|finished with code 0|build\(s\) found|^found:|log copied|\bcleaned\b|^started (fivem|radeon)|cap put back|report copied", RegexOptions.IgnoreCase);

        static int Severity(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (RxBroken.IsMatch(s)) return 5;
            if (RxError.IsMatch(s)) return 4;
            if (RxWarn.IsMatch(s)) return 3;
            if (RxLight.IsMatch(s)) return 2;
            if (RxGood.IsMatch(s)) return 1;
            return 0;
        }

        // everything the program wrote in this run, with the debug lines, ready to paste into a message
        static string BuildReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("FiveM Toolbox report");
            sb.AppendLine("version: " + Info.Version + "   made: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   language: " + Loc.Lang + "   theme: " + Theme.Mode);
            sb.AppendLine("log file: " + AppLog.FilePath);
            sb.AppendLine("---- log of this run, with debug lines ----");
            foreach (LogEntry le in AppLog.Snapshot()) sb.AppendLine(le.Text);
            return sb.ToString();
        }

        static bool IsProblem(string s) { return Severity(s) >= 3; }

        static Color SevColor(int sev)
        {
            bool d = Theme.IsDark;
            if (sev == 2) return ColorTranslator.FromHtml(d ? "#f4cf3c" : "#a16207");
            if (sev == 3) return ColorTranslator.FromHtml(d ? "#ff9f43" : "#c2410c");
            return ColorTranslator.FromHtml(d ? "#e879f9" : "#a21caf");
        }

        // The background of a line with a result (green good, yellow light problem, orange warning, red error, purple broken): the colour of the result mixed into the
        // log background, so the normal text colour stays easy to read in the light and in the dark theme. (Coloured text was hard to see on white.)
        static Color Band(int sev)
        {
            Color c = sev == 1 ? Theme.Good : sev == 2 ? ColorTranslator.FromHtml(Theme.IsDark ? "#f4cf3c" : "#c99700") : sev == 3 ? SevColor(3) : sev == 4 ? Theme.Bad : SevColor(5);
            return Fx.Mix(Theme.Bg2, c, Theme.IsDark ? 0.30f : 0.24f);
        }

        // for --log-color-test
        public static int TestBand(int sev) { return Band(sev).ToArgb(); }
        public int[] TestColors(string part)
        {
            int i = box.Text.IndexOf(part, StringComparison.Ordinal);
            if (i < 0) return null;
            box.Select(i, Math.Min(4, part.Length));
            int[] r = new int[] { box.SelectionBackColor.ToArgb(), box.SelectionColor.ToArgb() };
            box.Select(box.TextLength, 0);
            return r;
        }

        static string Cw(string s) { return s.Replace('~', (char)92); }

        static string Esc(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == (char)92 || c == '{' || c == '}') sb.Append((char)92).Append(c);
                else if (c == '\t') sb.Append(Cw("~tab "));
                else if (c < ' ') { }
                else if (c > '~') sb.Append((char)92).Append('u').Append((int)(short)c).Append('?');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static string ColorEntry(Color c) { return Cw("~red") + c.R + Cw("~green") + c.G + Cw("~blue") + c.B + ";"; }
        int Tw(int chars) { return (int)Math.Round(chars * charPx * (Dpi.Aware ? 15f / Dpi.Scale : 15f)); }   // charPx is in real pixels

        static Color ToolColor(string tool)
        {
            bool d = Theme.IsDark;
            if (tool == "Frame cap") return ColorTranslator.FromHtml(d ? "#59c4ff" : "#1177b5");
            if (tool == "DNS") return ColorTranslator.FromHtml(d ? "#2dd4bf" : "#0f766e");
            if (tool == "FiveM cache") return ColorTranslator.FromHtml(d ? "#f4cf3c" : "#a16207");
            if (tool == "Graphics") return ColorTranslator.FromHtml(d ? "#f472b6" : "#be185d");
            return Theme.Text2;
        }

        void AddLine(StringBuilder rtf, StringBuilder plain, LogEntry e, string marker, string message, int headerGroup, int sev)
        {
            int msgTw = Tw(28);
            int cTool = e.Tool == "Frame cap" ? 3 : e.Tool == "DNS" ? 4 : e.Tool == "FiveM cache" ? 5 : e.Tool == "Graphics" ? 13 : 6;
            int cMsg = (e.Debug && sev < 4) ? 1 : sev <= 0 ? 2 : 7 + sev;
            // a line with a result gets the colour as its BACKGROUND and all its text in the normal text colour (colour 2); a plain line keeps its colours
            bool band = sev >= 1 && !(e.Debug && sev < 4);
            int cMark = band ? 2 : 1, cTag = band ? 2 : cTool, cText = band ? 2 : cMsg;
            string tag = "[" + e.Tool + "]";
            int start = plain.Length;
            rtf.Append(Cw("~pard~li")).Append(msgTw).Append(Cw("~fi-")).Append(msgTw);
            rtf.Append(Cw("~tx")).Append(Tw(4)).Append(Cw("~tx")).Append(Tw(14)).Append(Cw("~tx")).Append(msgTw).Append(' ');
            if (band) rtf.Append(Cw("~highlight")).Append(13 + sev).Append(' ');
            rtf.Append(Cw("~cf")).Append(cMark).Append(' ').Append(Esc(marker)).Append(Cw("~tab ")).Append(Esc(e.Time)).Append(Cw("~tab "));
            rtf.Append(Cw("~cf")).Append(cTag).Append(' ').Append(Esc(tag)).Append(Cw("~tab "));
            rtf.Append(Cw("~cf")).Append(cText).Append(' ').Append(Esc(message));
            if (band) rtf.Append(Cw("~highlight0"));
            rtf.Append(Cw("~par "));
            plain.Append(marker).Append('\t').Append(e.Time).Append('\t').Append(tag).Append('\t').Append(message).Append('\n');
            if (headerGroup != 0) headerRanges.Add(new int[] { start, plain.Length, headerGroup });
        }

        void Rebuild()
        {
            List<LogEntry> list = new List<LogEntry>();
            foreach (LogEntry e in AppLog.Snapshot()) if (Matches(e)) list.Add(e);

            StringBuilder rtf = new StringBuilder(); StringBuilder plain = new StringBuilder();
            headerRanges.Clear(); openNow.Clear();
            rtf.Append(Cw("{~rtf1~ansi~uc1~deff0{~fonttbl{~f0 Consolas;}}"));
            rtf.Append(Cw("{~colortbl ;")).Append(ColorEntry(Theme.Text2)).Append(ColorEntry(Theme.Text)).Append(ColorEntry(ToolColor("Frame cap")))
               .Append(ColorEntry(ToolColor("DNS"))).Append(ColorEntry(ToolColor("FiveM cache"))).Append(ColorEntry(Theme.Text2)).Append(ColorEntry(Theme.Bad)).Append(ColorEntry(Theme.Good)).Append(ColorEntry(SevColor(2))).Append(ColorEntry(SevColor(3))).Append(ColorEntry(Theme.Bad)).Append(ColorEntry(SevColor(5))).Append(ColorEntry(ToolColor("Graphics"))).Append(ColorEntry(Band(1))).Append(ColorEntry(Band(2))).Append(ColorEntry(Band(3))).Append(ColorEntry(Band(4))).Append(ColorEntry(Band(5))).Append('}');
            rtf.Append(Cw("~f0~fs18 "));

            int i = 0;
            while (i < list.Count)
            {
                int g = list[i].Group, j = i + 1;
                if (g != 0) while (j < list.Count && list[j].Group == g) j++;
                int n = j - i;
                if (g == 0 || n == 1)
                {
                    for (int k = i; k < j; k++) AddLine(rtf, plain, list[k], "", list[k].Message, 0, Severity(list[k].Message));
                }
                else
                {
                    bool done = AppLog.IsDone(g), problem = false;
                    int gsev = 0; for (int k = i; k < j; k++) { int sv = Severity(list[k].Message); if (list[k].Debug && sv < 4) sv = 0; if (sv > gsev) gsev = sv; if (sv >= 3) problem = true; }
                    bool stay = false;
                    if (StayOpenOnTab) lock (seenGen) { int sg; if (!seenGen.TryGetValue(g, out sg)) { sg = tabGen; seenGen[g] = sg; } stay = sg == tabGen; }
                    bool open = expanded.ContainsKey(g) ? expanded[g] : (!done || problem || stay);
                    openNow[g] = open;
                    if (open)
                    {
                        AddLine(rtf, plain, list[i], "[-]", list[i].Message, g, Severity(list[i].Message));
                        for (int k = i + 1; k < j; k++) AddLine(rtf, plain, list[k], "", list[k].Message, 0, Severity(list[k].Message));
                    }
                    else
                    {
                        string summary = list[i].Message + "   ->   " + list[j - 1].Message + "   (" + n + " lines)";
                        AddLine(rtf, plain, list[i], "[+]", summary, g, gsev);
                    }
                }
                i = j;
            }
            rtf.Append('}');

            SendMessage(box.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            try { box.Rtf = rtf.ToString(); box.SelectionStart = box.TextLength; box.SelectionLength = 0; box.ScrollToCaret(); }
            catch (Exception ex) { AppLog.Debug("log view error: " + ex.Message); }
            SendMessage(box.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
            box.Invalidate();
        }

        void BoxMouseDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int idx = box.GetCharIndexFromPosition(e.Location);
            foreach (int[] r in headerRanges)
                if (idx >= r[0] && idx < r[1]) { Toggle(r[2]); return; }
        }

        void Toggle(int g)
        {
            bool open;
            if (!openNow.TryGetValue(g, out open)) return;
            expanded[g] = !open;
            int first = box.GetCharIndexFromPosition(new Point(2, 2));
            Rebuild();
            try { box.SelectionStart = Math.Min(first, box.TextLength); box.SelectionLength = 0; box.ScrollToCaret(); } catch (Exception) { }
        }
    }

    class LogForm : ChromeForm
    {
        public LogForm() : base(true, true, 40, true)
        {
            Text = Info.AppName + " - " + Loc.T("full log");
            Icon = Info.AppIcon();
            Width = 1000; Height = 620; MinimumSize = new Size(640, 360);
            IgnoreShowMe = true;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            Panel holder = new Panel(); holder.Dock = DockStyle.Fill; holder.Padding = new Padding(14, 8, 14, 10); holder.Tag = "body"; Body.Controls.Add(holder);
            holder.Controls.Add(new LogPanel(true, false));
            Label where = new Label(); where.Text = Loc.T("Log file: ") + AppLog.FilePath; where.Tag = "muted"; where.AutoEllipsis = true; where.Dock = DockStyle.Bottom; where.Height = 26; where.TextAlign = ContentAlignment.MiddleLeft;
            holder.Controls.Add(where);
            Theme.Changed += OnTheme;
            FormClosed += delegate { Theme.Changed -= OnTheme; };
            Theme.Apply(this);
        }

        void OnTheme() { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(delegate { Theme.Apply(this); })); } catch (Exception) { } }
    }

    // ---------------------------------------------------------------- the welcome window (shown until "do not show again" is ticked)
    class DisclaimerForm : ChromeForm
    {
        CheckBox dont;
        public bool DontShowAgain { get { return dont.Checked; } }

        public DisclaimerForm() : base(false, false, 40, true)
        {
            Text = Info.AppName;
            Icon = Info.AppIcon();
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            int bodyW = 470;
            Font normal = new Font("Segoe UI", 9f);
            int y = 20;

            Label head = new Label(); head.Text = string.Format(Loc.T("Welcome to {0}"), Info.AppName); head.Font = new Font("Segoe UI", 16f, FontStyle.Bold); head.AutoSize = true; head.Left = 26; head.Top = y;
            Body.Controls.Add(head); y += 50;

            Card notice = new Card(); notice.Warn = true; notice.Left = 26; notice.Top = y; notice.Width = bodyW - 52; notice.Height = 78;
            RedXIcon x = new RedXIcon(); x.Left = 16; x.Top = 19; notice.Controls.Add(x);
            Label nl; UiUtil.Wrapped(notice, "Provided as-is and still in a testing phase. Use it at your own risk.", 68, 16, notice.Width - 84, new Font("Segoe UI", 10.5f, FontStyle.Bold), null, out nl);
            notice.Height = Math.Max(78, nl.Bottom + 16); Body.Controls.Add(notice); y += notice.Height + 10;
            Label legacy;
            y = UiUtil.Wrapped(Body, "Made for FiveM with the GTA V Legacy edition.", 26, y, bodyW - 52, new Font("Segoe UI", 9f), "muted", out legacy);
            y += 10;

            Label what = new Label(); what.Text = Loc.T("What it does"); what.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold); what.AutoSize = true; what.Left = 26; what.Top = y;
            Body.Controls.Add(what); y += 30;
            y = UiUtil.Bullet(Body, "FiveM cache: clears FiveM's temporary files. The deep clean also signs you out of FiveM and removes saved game and web data, but it does not touch your key binds or your graphics settings. The separate Reset removes those too, after a big warning.", 26, y, bodyW - 52, normal);
            y = UiUtil.Bullet(Body, "Frame cap: sets a frame cap for every FiveM build in your graphics driver (AMD and NVIDIA).", 26, y, bodyW - 52, normal);
            y = UiUtil.Bullet(Body, "DNS tools: clears the DNS cache, and can also change your DNS settings if you still have connection problems. You can always undo it.", 26, y, bodyW - 52, normal);
            y = UiUtil.Bullet(Body, "Graphics: checks two FiveM settings. Extended Texture Budget should be as high as your PC can take, or many MLOs pop in and out when a lot of them are close together. A high Post FX makes some MLOs and cars far too bright. Two switches fix the interface lag and the stutter at high frame rates.", 26, y, bodyW - 52, normal);
            y = UiUtil.Bullet(Body, "Administrator rights: the DNS tools need administrator rights to work fully. Windows asks for permission, or press the shield button at the top right to restart as administrator once and never be asked again. The frame cap, the graphics settings and the FiveM cache do not need it.", 26, y, bodyW - 52, normal);
            y += 4;
            Label p2;
            y = UiUtil.Wrapped(Body, "By continuing you accept these risks. Where it is possible, the old setting is saved or backed up first.", 26, y, bodyW - 52, normal, null, out p2);
            y += 16;

            dont = new CheckBox(); dont.Text = Loc.T("Do not show this window again"); dont.Left = 26; dont.Top = y; dont.Width = bodyW - 52; dont.Height = 26; Body.Controls.Add(dont); y += 40;

            IconButton ok = new IconButton(); ok.Glyph = Ico.Accept; ok.Text = Loc.T("I understand, continue"); ok.Tag = "primary"; ok.Left = 26; ok.Top = y; ok.Width = 262; ok.Height = 42;
            ok.Font = new Font("Segoe UI", 10f, FontStyle.Bold); ok.DialogResult = DialogResult.OK; Body.Controls.Add(ok);
            IconButton quit = new IconButton(); quit.Glyph = Ico.Cancel; quit.Text = Loc.T("Quit"); quit.Left = 298; quit.Top = y; quit.Width = 120; quit.Height = 42; quit.DialogResult = DialogResult.Cancel; Body.Controls.Add(quit);
            y += 42 + 14;

            Label copy = new Label(); copy.Text = Ico.Copyright + " " + Info.Year + " " + Info.Owner; copy.Tag = "muted"; copy.AutoSize = true; copy.Left = 26; copy.Top = y; Body.Controls.Add(copy);
            y += 30;

            AcceptButton = ok; CancelButton = quit;
            ClientSize = new Size(bodyW + 12, TitleBar.Height + y + 6);
            Theme.Apply(this);
        }
    }

    // ---------------------------------------------------------------- the small window to pick a tool
    class ChooserForm : ChromeForm
    {
        CheckBox skip;
        ToggleSwitch adminSwitch;
        public int Choice = Info.FirstPage;    // the page number: 0 frame cap, 1 DNS tools, 2 FiveM cache, 3 graphics (the tiles are shown in the order of Info.PageOrder)
        public bool SkipNextTime { get { return skip.Checked; } }
        public bool WantAdmin { get { return adminSwitch.Checked; } }

        public ChooserForm() : base(false, false, 40, true)
        {
            Text = Info.AppName;
            Icon = Info.AppIcon();
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            int bodyW = 470;
            int y = 20;

            Label head = new Label(); head.Text = Loc.T("What do you want to do?"); head.Font = new Font("Segoe UI", 16f, FontStyle.Bold); head.AutoSize = true; head.Left = 26; head.Top = y;
            Body.Controls.Add(head); y += 54;

            string[] glyphs = new string[] { "gauge", Ico.Globe, Ico.Delete, Ico.Palette };
            string[] titles = new string[] { "Frame cap", "DNS tools", "FiveM cache", "Graphics" };
            string[] descs = new string[] { "Limit the FPS of every FiveM build", "Clear or change your DNS, with an Undo", "Clean FiveM's temporary files", "Texture Budget, Post FX, and fixes for UI lag and stutter" };
            foreach (int i in Info.PageOrder)
            {
                int pick = i;
                ToolTile t = new ToolTile(glyphs[i], titles[i], descs[i]);
                t.Left = 26; t.Top = y; t.Width = bodyW - 52;
                t.Click += delegate { Choice = pick; DialogResult = DialogResult.OK; };
                Body.Controls.Add(t);
                y += 78 + 12;
            }
            y += 4;
            adminSwitch = new ToggleSwitch(); adminSwitch.Left = 26; adminSwitch.Top = y + 1; adminSwitch.Checked = DnsTool.IsAdmin() || Settings.Get("startadmin", "0") == "1"; Body.Controls.Add(adminSwitch);
            Label adminText = new Label(); adminText.Text = Loc.T("Start as administrator"); adminText.Font = new Font("Segoe UI", 10f, FontStyle.Bold); adminText.AutoSize = false; adminText.Left = 86; adminText.Top = y; adminText.Width = bodyW - 52 - 60; adminText.Height = 24; adminText.TextAlign = ContentAlignment.MiddleLeft; Body.Controls.Add(adminText);
            Label adminSub;
            y = UiUtil.Wrapped(Body, "Windows asks once. Then the DNS tools never ask for permission again.", 86, y + 26, bodyW - 52 - 60, new Font("Segoe UI", 9f), "muted", out adminSub);
            y += 14;
            skip = new CheckBox(); skip.Text = Loc.T("Skip this window next time and open the tool I pick"); skip.Left = 26; skip.Top = y; skip.Width = bodyW - 52; skip.Height = 26; Body.Controls.Add(skip); y += 32;
            Label note;
            y = UiUtil.Wrapped(Body, "You can bring this window back with the link at the bottom of the main window.", 26, y, bodyW - 52, new Font("Segoe UI", 9f), "muted", out note);
            y += 16;
            ClientSize = new Size(bodyW + 12, TitleBar.Height + y + 6);
            Theme.Apply(this);
        }
    }

    // ---------------------------------------------------------------- the big warning before "Reset"
    class ResetWarningForm : ChromeForm
    {
        CheckBox sure;
        IconButton go;

        public ResetWarningForm(int binds) : base(false, false, 40, true)
        {
            Text = Info.AppName;
            Icon = Info.AppIcon();
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            int bodyW = 580;
            int y = 22;
            RedXIcon x = new RedXIcon(); x.Left = 26; x.Top = y + 2; Body.Controls.Add(x);
            Label head;
            int hb = UiUtil.Wrapped(Body, "RESET: you lose your key binds and your graphics settings!", 80, y, bodyW - 80 - 26, new Font("Segoe UI", 16f, FontStyle.Bold), "bad", out head);
            y = Math.Max(hb, y + 48) + 16;
            Label lose = new Label(); lose.Text = Loc.T("You lose:"); lose.Font = new Font("Segoe UI", 11f, FontStyle.Bold); lose.AutoSize = true; lose.Left = 26; lose.Top = y; Body.Controls.Add(lose);
            y += 32;
            Font f = new Font("Segoe UI", 10f);
            y = UiUtil.Bullet(Body, binds > 0 ? string.Format(Loc.T("All your KEY BINDS ({0} saved keys)"), binds) : Loc.T("All your KEY BINDS"), 26, y, bodyW - 52, new Font("Segoe UI", 10f, FontStyle.Bold));
            y = UiUtil.Bullet(Body, "All your GRAPHICS SETTINGS (Extended Texture Budget, Post FX and all the others)", 26, y, bodyW - 52, new Font("Segoe UI", 10f, FontStyle.Bold));
            y = UiUtil.Bullet(Body, "Your saved server data (settings that scripts keep on your PC)", 26, y, bodyW - 52, f);
            y = UiUtil.Bullet(Body, "Your camera and microphone permissions for servers, your camera view choices and the places of FiveM's own windows", 26, y, bodyW - 52, f);
            y = UiUtil.Bullet(Body, "Your saved FiveM login and the saved game and web data (FiveM loads the game files again, so the next start is slow)", 26, y, bodyW - 52, f);
            y = UiUtil.Bullet(Body, "Your saved GTA V licence check and some browser files (FiveM does the check again at the next start, so stay signed in to the Rockstar Games Launcher or Steam)", 26, y, bodyW - 52, f);
            y = UiUtil.Bullet(Body, "All temporary files, logs and crash reports", 26, y, bodyW - 52, f);
            y += 6;
            Label note;
            y = UiUtil.Wrapped(Body, "A copy of the settings folder is saved first in the backups folder (the Backups button on the Frame cap tab opens it), so you can put things back by hand.", 26, y, bodyW - 52, new Font("Segoe UI", 9f), "muted", out note);
            y += 18;
            string sureText = Loc.T("I understand that I lose my key binds and my graphics settings");
            Font sf = new Font("Segoe UI", 10f, FontStyle.Bold);
            int sh = Dpi.L(TextRenderer.MeasureText(sureText, sf, new Size(Dpi.S(bodyW - 52 - 26), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height) + 12;
            sure = new CheckBox(); sure.Text = sureText; sure.Font = sf; sure.AutoSize = false;
            sure.Left = 26; sure.Top = y; sure.Width = bodyW - 52; sure.Height = sh; Body.Controls.Add(sure);
            y += sh + 14;
            Font bf = new Font("Segoe UI", 10f, FontStyle.Bold);
            int gw = Math.Max(250, Dpi.TextW(Loc.T("Reset everything"), bf) + 80);
            go = new IconButton(); go.Glyph = Ico.Delete; go.Text = Loc.T("Reset everything"); go.Tag = "danger"; go.Left = 26; go.Top = y; go.Width = gw; go.Height = 46; go.Enabled = false;
            go.Font = bf; go.DialogResult = DialogResult.OK; Body.Controls.Add(go);
            IconButton cancel = new IconButton(); cancel.Glyph = Ico.Cancel; cancel.Text = Loc.T("Cancel"); cancel.Left = 26 + gw + 12; cancel.Top = y; cancel.Width = 170; cancel.Height = 46;
            cancel.Font = bf; cancel.DialogResult = DialogResult.Cancel; Body.Controls.Add(cancel);
            sure.CheckedChanged += delegate { go.Enabled = sure.Checked; };
            CancelButton = cancel; ActiveControl = cancel;   // Enter and Esc both mean "Cancel"
            y += 46 + 24;
            ClientSize = new Size(bodyW + 12, TitleBar.Height + y + 6);
            Theme.Apply(this);
        }

        public bool GoEnabled { get { return go.Enabled; } }
    }

    // ---------------------------------------------------------------- "Why is 140 the highest?"
    class WhyForm : ChromeForm
    {
        public WhyForm() : base(false, false, 40, true)
        {
            Text = Loc.T("Why 140?");
            Icon = Info.AppIcon();
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            int bodyW = 500;
            Font normal = new Font("Segoe UI", 10f);
            int y = 20;

            Label head = new Label(); head.Text = Loc.T("Why is 140 the highest?"); head.Font = new Font("Segoe UI", 15f, FontStyle.Bold); head.AutoSize = true; head.Left = 26; head.Top = y;
            Body.Controls.Add(head); y += 48;
            string[] paras = new string[] {
                "FiveM runs on the GTA V engine. The game came out in 2013 and the engine was never designed for very high frame rates, because hardware back then could not reach them.",
                "The game can go above 140 FPS, but today's PCs get there easily and the engine does not cope well: players report micro-stutter (small hitches, even at high FPS) above roughly 140 FPS.",
                "A likely cause is the game's audio part falling out of step with the main part. The engine also has its own limit near 188 FPS.",
                "A cap keeps FiveM below the range where this happens. A lower cap is fine, and often feels smoother."
            };
            foreach (string p in paras)
            {
                Label l;
                y = UiUtil.Wrapped(Body, p, 26, y, bodyW - 52, normal, null, out l);
                y += 12;
            }
            y += 6;
            IconButton ok = new IconButton(); ok.Glyph = Ico.Accept; ok.Text = Loc.T("Got it"); ok.Tag = "primary"; ok.Left = 26; ok.Top = y; ok.Width = 150; ok.Height = 40; ok.DialogResult = DialogResult.OK; Body.Controls.Add(ok);
            AcceptButton = ok; CancelButton = ok;
            y += 40 + 18;
            ClientSize = new Size(bodyW + 12, TitleBar.Height + y + 6);
            Theme.Apply(this);
        }
    }

    // ---------------------------------------------------------------- the window
    class MainForm : ChromeForm
    {
        ChromeTab tabCap, tabDns, tabCache, tabGfx;
        ActivityCard activity;
        int currentPage = Info.FirstPage;
        int footerGen = 0;
        NumBox gfxBudget; DropBox gfxPost; Label gfxBudgetNow, gfxPostNow, gfxUiNow, gfxStutterNow, gfxBudgetHint, gfxMeterText; MeterBar gfxMeterBar; bool meterReading; string meterLuid; long meterTotal; VramTracker meterTracker = new VramTracker(); ToggleSwitch gfxUiFix, gfxStutterFix; Card gfxCard; IconButton gfxRecommend; int gfxRecN = -1; IconButton gfxApply, gfxClose, gfxHighest, gfxNormal, gfxOpen;
        string cacheMeasuredRoot = null;
        DateTime cacheMeasuredAt = DateTime.MinValue;
        GlyphButton themeSystem, themeLight, themeDark;
        ToolTip tip = new ToolTip();
        Panel pageCap, pageDns, pageCache, pageGfx, footer, statusBar;
        Label copyLabel;
        LinkLabel startLink;
        // frame cap page
        Label gpuLabel, gpuDrv, gpuMem, modeLabel, capHint, gpuSwitchText;
        ToggleSwitch gpuSwitch;
        Card cGpuCard, cBuildsCard, cCapCard;
        DataGridView grid;
        NumBox capBox;
        IconButton applyBtn, rescanBtn, browseBtn, backupBtn, whyBtn, undoBtn, resetBtn, tidyBtn;
        int resetRemoved = -1;
        // DNS page
        DataGridView dnsGrid;
        Label dnsState, dnsCount;
        int dnsShown = -1;
        bool dnsCounting = false;
        IconButton dnsRefresh, dnsFlush, dnsSet, dnsUndo;
        CheckBox cbExtraFlush;
        RadioButton rbCf, rbGoogle, rbBoth;
        // FiveM cache page
        DropBox cacheRoots;
        Label cacheState, cacheTotal, sizeTemp, sizeLogs, sizeDeep, descTemp, descLogs, descDeep;
        IconButton cacheRefresh, cacheClose, cacheClean, cacheStart, cacheWipe, cacheIcon;
        Card cReset; ToggleSwitch resetSwitch; Label resetOffLine, wipeBig, wipeDesc; int resetGen = 0;
        CheckBox cbTemp, cbLogs, cbDeep, cbPure;
        List<CacheItem> cacheItems = new List<CacheItem>();
        List<string> fiveRoots = new List<string>();
        bool canStart = false;
        System.Windows.Forms.Timer runTimer;
        // the log at the bottom
        LogPanel logPanel;
        LogForm logForm;

        List<Build> builds = new List<Build>();
        List<Gpu> gpus = new List<Gpu>();
        string mode = "none";   // amd, nvidia, none
        bool busy = false;
        bool inDeepChange = false;
        const int CardW = 950;

        static DataGridView MakeGrid(int left, int top, int width, int height)
        {
            DataGridView g = new DataGridView();
            g.Left = left; g.Top = top; g.Width = width; g.Height = height;
            g.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false;
            g.RowHeadersVisible = false; g.SelectionMode = DataGridViewSelectionMode.FullRowSelect; g.MultiSelect = false;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None; g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 26; g.RowTemplate.Height = 24; g.BorderStyle = BorderStyle.None; g.ReadOnly = false;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.CurrentCellDirtyStateChanged += delegate { if (g.IsCurrentCellDirty) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            return g;
        }
        static DataGridViewColumn TextCol(string header, int width, bool fill)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = Loc.T(header); c.Width = width; c.ReadOnly = true; c.SortMode = DataGridViewColumnSortMode.NotSortable;
            if (fill) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; }
            return c;
        }
        static DataGridViewColumn CheckCol(int width)
        {
            DataGridViewCheckBoxColumn c = new DataGridViewCheckBoxColumn();
            c.HeaderText = Loc.T("Use"); c.Width = Math.Max(width, Dpi.TextW(c.HeaderText, new Font("Segoe UI", 9.5f)) + 34); c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter; c.HeaderCell.Style.Padding = new Padding(0);
            c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter; c.DefaultCellStyle.Padding = new Padding(0);
            return c;
        }
        static Card NewCard(string title, string glyph, int top, int height)
        {
            Card c = new Card(); c.Title = Loc.T(title); c.Glyph = glyph; c.Left = 16; c.Top = top; c.Width = CardW; c.Height = height;
            c.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            return c;
        }
        static IconButton Btn(string glyph, string text, int left, int top, int width, int height)
        {
            IconButton b = new IconButton();
            b.Glyph = glyph; b.Text = Loc.T(text); b.Left = left; b.Top = top; b.Width = width; b.Height = height;
            return b;
        }
        static int FitW(string text, int min) { return Math.Max(min, Dpi.TextW(Loc.T(text), new Font("Segoe UI", 9.5f)) + 56); }
        static Label Small(string text, int left, int top, int width, int height, string tag)
        {
            Label l = new Label(); l.Text = Loc.T(text); l.Left = left; l.Top = top; l.Width = width; l.Height = height; l.Tag = tag; l.Font = new Font("Segoe UI", 9f);
            return l;
        }
        static Panel NewPage()
        {
            Panel p = new Panel(); p.Dock = DockStyle.Fill; p.AutoScroll = true; p.Tag = "body"; p.Visible = false;
            p.Resize += delegate
            {
                foreach (Control c in p.Controls)
                    if (c is Card || c is TableLayoutPanel) { c.Left = Dpi.S(16); c.Width = Math.Max(Dpi.S(300), p.ClientSize.Width - Dpi.S(32)); }
            };
            return p;
        }

        public MainForm() : this(Info.FirstPage) { }

        public MainForm(int startPage) : base(true, true, 46, false)
        {
            Text = Info.AppName + " " + Info.Version + (DnsTool.IsAdmin() ? " - " + Loc.T("administrator") : "") + Finder.TestModeText();
            Icon = Info.AppIcon();
            Rectangle wa = Dpi.LogicalWorkArea();
            Width = Math.Min(1000, wa.Width - 40); Height = Math.Min(985, wa.Height - 30);
            MinimumSize = new Size(960, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);

            // ---- the tabs live in the title bar, like a browser
            ChromeTab[] tabs = new ChromeTab[4];
            tabCap = new ChromeTab("gauge", "Frame cap"); tabDns = new ChromeTab(Ico.Globe, "DNS tools"); tabCache = new ChromeTab(Ico.Delete, "FiveM cache"); tabGfx = new ChromeTab(Ico.Palette, "Graphics");
            tabs[0] = tabCap; tabs[1] = tabDns; tabs[2] = tabCache; tabs[3] = tabGfx;
            int tx = 46;
            foreach (int pick in Info.PageOrder)        // left to right: FiveM cache, Frame cap, DNS tools, Graphics
            {
                int page = pick;
                tabs[page].Left = tx; tabs[page].Top = TitleBar.Height - tabs[page].Height;
                tabs[page].Click += delegate { ShowPage(page); };
                TitleBar.Controls.Add(tabs[page]);
                tx += tabs[page].Width + 4;
            }

            // Appearance: three small icons instead of a drop-down
            Panel appearPanel = new Panel(); appearPanel.Dock = DockStyle.Right; appearPanel.Width = 172; appearPanel.Tag = "frame";
            themeSystem = new GlyphButton(Ico.Monitor); themeSystem.Left = 46; themeSystem.Top = 8;
            themeLight = new GlyphButton(Ico.Sun); themeLight.Left = 86; themeLight.Top = 8;
            themeDark = new GlyphButton(Ico.Moon); themeDark.Left = 126; themeDark.Top = 8;
            themeSystem.Click += delegate { Theme.SetMode("system"); UpdateThemeButtons(); };
            themeLight.Click += delegate { Theme.SetMode("light"); UpdateThemeButtons(); };
            themeDark.Click += delegate { Theme.SetMode("dark"); UpdateThemeButtons(); };
            tip.SetToolTip(themeSystem, Loc.T("Follow the Windows setting"));
            tip.SetToolTip(themeLight, Loc.T("Light"));
            tip.SetToolTip(themeDark, Loc.T("Dark"));
            appearPanel.Controls.Add(themeSystem); appearPanel.Controls.Add(themeLight); appearPanel.Controls.Add(themeDark);
            // the shield: restart as administrator (blue = this copy already is one)
            bool runsAsAdmin = DnsTool.IsAdmin();
            GlyphButton adminBtn = new GlyphButton(Ico.Shield); adminBtn.Left = 6; adminBtn.Top = 8; adminBtn.Selected = runsAsAdmin;
            adminBtn.Click += delegate { if (!runsAsAdmin) RestartAsAdmin(); };
            tip.SetToolTip(adminBtn, runsAsAdmin ? Loc.T("Running as administrator: no permission windows for the DNS tools") : Loc.T("Restart as administrator (Windows asks once), so the DNS tools never ask again"));
            appearPanel.Controls.Add(adminBtn);
            TitleBar.Controls.Add(appearPanel);
            appearPanel.BringToFront();
            Draggable(appearPanel);
            UpdateThemeButtons();
            IconClick = ShowLanguageMenu;
            if (IconBox != null) { IconBox.Cursor = Cursors.Hand; tip.SetToolTip(IconBox, Loc.T("Change the language")); }

            // ---- pages, then the footer, so the pages fill what is left
            pageCap = NewPage(); pageDns = NewPage(); pageCache = NewPage(); pageGfx = NewPage();
            Body.Controls.Add(pageGfx); Body.Controls.Add(pageCache); Body.Controls.Add(pageDns); Body.Controls.Add(pageCap);

            footer = new Panel(); footer.Dock = DockStyle.Bottom; footer.Height = 196; footer.Padding = new Padding(16, 0, 16, 0); footer.Tag = "body";
            Body.Controls.Add(footer);
            Body.SizeChanged += delegate { LayoutFooter(false); };
            logPanel = new LogPanel(false, true);
            logPanel.StayOpenOnTab = true;
            logPanel.PopOut += ShowLogClick;
            logPanel.ReportCopied += delegate { activity.Finish("ok", Loc.T("Report copied"), Loc.T("The whole log is on your clipboard. Paste it in a message. It has file paths and your PC's name in it.")); };
            footer.Controls.Add(logPanel);
            statusBar = new Panel(); statusBar.Dock = DockStyle.Bottom; statusBar.Height = 30; statusBar.Tag = "body";
            copyLabel = new Label(); copyLabel.Text = Ico.Copyright + " " + Info.Year + " " + Info.Owner + "      " + Info.AppName + " " + Info.Version; copyLabel.Tag = "muted"; copyLabel.AutoSize = true; copyLabel.Left = 0; copyLabel.Top = 7;
            statusBar.Controls.Add(copyLabel);
            startLink = new LinkLabel(); startLink.Text = Loc.T("Show the start window next time"); startLink.Dock = DockStyle.Right; startLink.Width = 340; startLink.TextAlign = ContentAlignment.MiddleRight;
            startLink.LinkClicked += delegate { Settings.Set("startpage", "ask"); Log("The start window will show next time."); };
            statusBar.Controls.Add(startLink);
            footer.Controls.Add(statusBar);
            activity = new ActivityCard(); activity.Dock = DockStyle.Top; footer.Controls.Add(activity);

            BuildCapPage();
            BuildDnsPage();
            BuildCachePage();
            BuildGfxPage();

            runTimer = new System.Windows.Forms.Timer(); runTimer.Interval = 2000; runTimer.Tick += delegate { if (pageCache.Visible && !busy) UpdateRunning(); if (pageDns.Visible && !busy) RefreshDnsCount(); if (pageGfx.Visible && !busy) { UpdateGfxRunning(); StartVramRead(); } }; runTimer.Start();

            Theme.Changed += OnThemeChanged;
            FormClosed += delegate { Theme.Changed -= OnThemeChanged; runTimer.Stop(); tip.Dispose(); };
            ShowPage(startPage);
            Shown += delegate { AppLog.Debug("main window: bounds " + Bounds + ", client " + ClientSize + ", title bar " + TitleBar.Height + ", app icon box " + (IconBox != null ? IconBox.Bounds.ToString() : "none")); LayoutFooter(false); Rescan(null, true); RefreshDns(); };
        }

        void OnThemeChanged() { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(delegate { Theme.Apply(this); UpdateThemeButtons(); })); } catch (Exception) { } }

        void UpdateThemeButtons()
        {
            themeSystem.Selected = Theme.Mode == "system"; themeLight.Selected = Theme.Mode == "light"; themeDark.Selected = Theme.Mode == "dark";
            themeSystem.Invalidate(); themeLight.Invalidate(); themeDark.Invalidate();
        }

        // the height the tab needs: its lowest block plus the same gap as between the blocks
        int PageNeed(Panel page)
        {
            int bottom = 0;
            foreach (Control c in page.Controls) bottom = Math.Max(bottom, c.Bottom);
            return bottom + S(12);
        }

        void LayoutFooter(bool animate)
        {
            Panel page = pageCap.Visible ? pageCap : pageDns.Visible ? pageDns : pageGfx.Visible ? pageGfx : pageCache;
            int want = Math.Max(S(176), Body.ClientSize.Height - PageNeed(page));
            int gen = ++footerGen;
            if (!animate || !IsHandleCreated || footer.Height == want) { footer.Height = want; return; }
            int from = footer.Height;
            Fx.Run(180, delegate(float p)
            {
                if (gen != footerGen) return;
                footer.Height = from + (int)Math.Round((want - from) * Fx.EaseOut(p));
            }, delegate { if (gen == footerGen) footer.Height = Math.Max(S(176), Body.ClientSize.Height - PageNeed(page)); });
        }

        public Action<int> PageHook;                      // for --tab-order-test only: when set, ShowPage just reports the page number it was asked for
        public ChromeTab[] TabsByPage() { return new ChromeTab[] { tabCap, tabDns, tabCache, tabGfx }; }

        public void ShowPage(int p)
        {
            if (PageHook != null) { PageHook(p); return; }
            if (p != currentPage && logPanel != null) logPanel.TabChanged();     // the jobs of the tab you leave fold up in the log
            currentPage = p;
            if (p != 2 && resetSwitch != null && resetSwitch.Checked) resetSwitch.Checked = false;
            pageCap.Visible = p == 0; pageDns.Visible = p == 1; pageCache.Visible = p == 2; pageGfx.Visible = p == 3;
            tabCap.Active = p == 0; tabDns.Active = p == 1; tabCache.Active = p == 2; tabGfx.Active = p == 3;
            tabCap.Invalidate(); tabDns.Invalidate(); tabCache.Invalidate(); tabGfx.Invalidate();
            AppLog.Context = p == 0 ? "Frame cap" : p == 1 ? "DNS" : p == 3 ? "Graphics" : "FiveM cache";
            Theme.Apply(this);
            LayoutFooter(true);
            if (p == 3) { UpdateGfxRunning(); RefreshGfx(); StartVramRead(); }
            if (p == 2) { UpdateRunning(); if (cacheRoots.Items.Count > 0 && (cacheMeasuredRoot != CurrentRoot() || (DateTime.UtcNow - cacheMeasuredAt).TotalSeconds > 120)) RefreshCacheSizes(); }
        }

        // ---------------------------------------------------------------- frame cap page
        void BuildCapPage()
        {
            Card cGpu = NewCard("Graphics card", Ico.Monitor, 12, 126); pageCap.Controls.Add(cGpu); cGpuCard = cGpu;
            gpuLabel = new Label(); gpuLabel.Left = 16; gpuLabel.Top = 42; gpuLabel.Width = 280; gpuLabel.Height = 40; gpuLabel.Text = Loc.T("Checking ..."); cGpu.Controls.Add(gpuLabel);
            gpuDrv = new Label(); gpuDrv.Left = 310; gpuDrv.Top = 42; gpuDrv.Width = CardW - 340; gpuDrv.Height = 40; gpuDrv.Tag = "muted"; cGpu.Controls.Add(gpuDrv);
            gpuMem = new Label(); gpuMem.Left = 560; gpuMem.Top = 42; gpuMem.Width = 200; gpuMem.Height = 40; gpuMem.Tag = "muted"; cGpu.Controls.Add(gpuMem);
            modeLabel = Small("", 16, 84, CardW - 32, 22, "muted"); cGpu.Controls.Add(modeLabel);
            // only visible when the PC has an integrated chip AND a dedicated card (a laptop, or a desktop with a chip in the processor): then Apply also puts FiveM on the dedicated card
            gpuSwitch = new ToggleSwitch(); gpuSwitch.Left = CardW - 16 - 48; gpuSwitch.Top = 9; gpuSwitch.Anchor = AnchorStyles.Top | AnchorStyles.Right; gpuSwitch.Visible = false;
            gpuSwitch.Checked = Settings.Get("usedgpu", "1") != "0"; cGpu.Controls.Add(gpuSwitch);
            gpuSwitchText = new Label(); gpuSwitchText.Text = Loc.T("Use the dedicated graphics card for FiveM"); gpuSwitchText.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold); gpuSwitchText.AutoSize = false; gpuSwitchText.TextAlign = ContentAlignment.MiddleRight;
            gpuSwitchText.Width = 400; gpuSwitchText.Height = 26; gpuSwitchText.Left = CardW - 16 - 48 - 10 - 400; gpuSwitchText.Top = 9; gpuSwitchText.Anchor = AnchorStyles.Top | AnchorStyles.Right; gpuSwitchText.Visible = false; cGpu.Controls.Add(gpuSwitchText);
            gpuSwitch.CheckedChanged += delegate { Settings.Set("usedgpu", gpuSwitch.Checked ? "1" : "0"); };
            // bottom right of the Graphics card block: removes what this tool left behind (old profiles in the NVIDIA driver, old entries of the Windows graphics list), after a yes
            int tw = FitW("Clean up old entries", 190);
            tidyBtn = Btn(Ico.Delete, "Clean up old entries", CardW - 16 - tw, 82, tw, 26); tidyBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right; tidyBtn.Click += TidyClick;
            modeLabel.Width = CardW - 32 - tw - 12;
            tip.SetToolTip(tidyBtn, Loc.T("Look for old entries that this tool made for FiveM (in the NVIDIA driver and in the Windows graphics list) and remove them after you say yes"));
            cGpu.Controls.Add(tidyBtn); tidyBtn.BringToFront();

            Card cBuilds = NewCard("FiveM builds found", Ico.Folder, 150, 260); pageCap.Controls.Add(cBuilds); cBuildsCard = cBuilds;
            grid = MakeGrid(16, 44, CardW - 32, 150);
            grid.Columns.Add(CheckCol(46)); grid.Columns.Add(TextCol("Build", 380, false)); grid.Columns.Add(TextCol("Status", 290, false)); grid.Columns.Add(TextCol("Folder", 150, true));
            cBuilds.Controls.Add(grid);
            int bw1 = FitW("Search again", 132), bw2 = FitW("Pick a folder ...", 160), bw3 = FitW("Backups", 110); rescanBtn = Btn(Ico.Search, "Search again", 16, 206, bw1, 36); rescanBtn.Click += delegate { Rescan(null); }; cBuilds.Controls.Add(rescanBtn);
            browseBtn = Btn(Ico.Folder, "Pick a folder ...", 16 + bw1 + 10, 206, bw2, 36); browseBtn.Click += BrowseClick; cBuilds.Controls.Add(browseBtn);
            backupBtn = Btn(Ico.OpenFolder, "Backups", 16 + bw1 + 10 + bw2 + 10, 206, bw3, 36);
            backupBtn.Click += delegate { try { Directory.CreateDirectory(Blb.BackupDir); Process.Start("explorer.exe", Blb.BackupDir); } catch (Exception) { } };
            cBuilds.Controls.Add(backupBtn);
            int bw5 = FitW("Reset the list", 130);
            int bw4r = FitW("Undo last change", 190); resetBtn = Btn(Ico.Refresh, "Reset the list", CardW - 16 - bw4r - 10 - bw5, 206, bw5, 36); resetBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right; resetBtn.Click += ResetClick;
            tip.SetToolTip(resetBtn, Loc.T("Remove the builds that are not on disk now from the list, search again, and add what is found"));
            cBuilds.Controls.Add(resetBtn);
            int bw4 = FitW("Undo last change", 190);
            undoBtn = Btn(Ico.Undo, "Undo last change", CardW - 16 - bw4, 206, bw4, 36); undoBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right; undoBtn.Click += UndoClick;
            tip.SetToolTip(undoBtn, Loc.T("Put the cap back to what it was before the last change this tool made"));
            cBuilds.Controls.Add(undoBtn);

            Card cCap = NewCard("Frame cap", "gauge", 422, 112); pageCap.Controls.Add(cCap); cCapCard = cCap;
            capBox = new NumBox(); capBox.Minimum = Info.MinCap; capBox.Maximum = Info.MaxCap; capBox.Value = Info.DefaultCap; capBox.Left = 16; capBox.Top = 54; capBox.Width = 96;
            capBox.Font = new Font("Segoe UI", 14f, FontStyle.Bold); cCap.Controls.Add(capBox);
            capHint = Small("FPS. 140 is the highest you can pick for now. A higher cap can cause micro-stutter.", 124, 50, 384, 42, "muted"); cCap.Controls.Add(capHint);
            whyBtn = Btn(Ico.Info, "Why?", 516, 54, Math.Max(100, Dpi.TextW(Loc.T("Why?"), new Font("Segoe UI", 9.5f)) + 52), 36);
            whyBtn.Click += delegate { using (WhyForm w = new WhyForm()) w.ShowDialog(this); };
            cCap.Controls.Add(whyBtn);
            int aw = FitW("Apply cap to the ticked builds", 290); applyBtn = Btn(Ico.Accept, "Apply cap to the ticked builds", CardW - 16 - aw, 50, aw, 44); applyBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            applyBtn.Tag = "primary"; applyBtn.Font = new Font("Segoe UI", 10f, FontStyle.Bold); applyBtn.Click += ApplyClick; cCap.Controls.Add(applyBtn);
        }

        public const string IntelOnlyText = "Intel Arc (a dedicated Intel card) was found. It is not supported at the moment, because it could not be tested.";

        public static string ComputeMode(List<Gpu> g, out string text)
        {
            bool supportedDedicated = g.Exists(delegate(Gpu x) { return (x.Vendor == "AMD" || x.Vendor == "NVIDIA") && x.Kind == "dedicated"; });
            if (Finder.HasUnsupportedDedicated(g) && !supportedDedicated) { text = IntelOnlyText; return "none"; }
            bool amd = g.Exists(delegate(Gpu x) { return x.Vendor == "AMD"; });
            bool nv = g.Exists(delegate(Gpu x) { return x.Vendor == "NVIDIA"; });
            if (nv)
            {
                if (NvApi.Available()) { text = "NVIDIA mode: works with the NVIDIA driver."; return "nvidia"; }
                if (!amd) { text = "NVIDIA card found, but the NVIDIA driver could not be used. Open the full log for details."; return "none"; }
            }
            if (amd)
            {
                bool ok = Radeon.Installed() && Adl.Available();
                text = ok ? "AMD mode: works with Radeon Software." : "Radeon Software was not found.";
                return ok ? "amd" : "none";
            }
            text = "No supported card (AMD or NVIDIA).";
            return "none";
        }

        void ShowGpus(string modeText)
        {
            StringBuilder sb = new StringBuilder(), sd = new StringBuilder(), sm = new StringBuilder();
            int maxW = 0, maxDrv = 0, maxMem = 0;
            foreach (Gpu g in gpus)
            {
                string left = g.Vendor + (g.Kind.Length > 0 ? " (" + Loc.T(g.Kind) + ")" : "") + ":  " + g.Name;
                sb.AppendLine(left);
                string right = g.Driver.Length > 0 ? Loc.T("Driver") + " " + g.Driver + (g.DriverDate.Length > 0 ? "  (" + g.DriverDate + ")" : "") : "";
                string mem = g.Vram > 0 ? string.Format(Loc.T("{0} video memory"), CacheTool.Size(g.Vram)) : "";
                sd.AppendLine(right); sm.AppendLine(mem);
                maxW = Math.Max(maxW, TextRenderer.MeasureText(left, gpuLabel.Font).Width);
                maxDrv = Math.Max(maxDrv, TextRenderer.MeasureText(right, gpuDrv.Font).Width);
                maxMem = Math.Max(maxMem, TextRenderer.MeasureText(mem, gpuMem.Font).Width);
            }
            if (gpus.Count == 0) sb.AppendLine(Loc.T("No graphics card found."));
            // two lines fit in the card; every card after that makes the card, and the blocks under it, 20 px taller
            int extra = Math.Max(0, (gpus.Count - 2) * S(20));
            gpuLabel.Height = S(40) + extra; gpuDrv.Height = S(40) + extra; gpuMem.Height = S(40) + extra; modeLabel.Top = S(84) + extra;
            cGpuCard.Height = S(126) + extra; cBuildsCard.Top = S(150) + extra; cCapCard.Top = S(422) + extra;
            LayoutFooter(false);
            gpuLabel.Text = sb.ToString();
            gpuLabel.Width = Math.Max(S(280), maxW + S(8));
            gpuDrv.Left = gpuLabel.Left + gpuLabel.Width + S(20);
            gpuDrv.Width = Math.Max(S(100), maxDrv + S(10));                                   // the driver column is as wide as its widest line
            gpuDrv.Text = sd.ToString();
            gpuMem.Left = gpuDrv.Left + gpuDrv.Width + S(16);                               // the memory column starts at ONE place for every card
            gpuMem.Width = Math.Max(S(100), Math.Min(maxMem + S(10), S(CardW) - S(16) - gpuMem.Left));
            gpuMem.Text = sm.ToString();
            modeLabel.Text = Loc.T(modeText) + (Finder.HasUnsupportedDedicated(gpus) && modeText != IntelOnlyText ? "  " + Loc.T("An Intel Arc card was also found: not supported at the moment.") : "");
            tidyBtn.Top = modeLabel.Top - S(2);
            bool dgpuOption = GpuPref.Wanted(gpus);
            gpuSwitch.Visible = dgpuOption; gpuSwitchText.Visible = dgpuOption;
            UpdateBudgetAdvice();
            UpdateMeterCard();
        }

        void FillBuilds()
        {
            grid.Rows.Clear();
            foreach (Build b in builds)
            {
                int i = grid.Rows.Add(true, b.FileName + (b.Missing ? Loc.T(" (not on disk now)") : ""), Loc.TS(b.Status), b.Root);
                grid.Rows[i].Tag = b;
            }
        }

        void Rescan(string pickedFolder) { Rescan(pickedFolder, false); }

        // quiet = the search at start-up: no activity card
        void Rescan(string pickedFolder, bool quiet)
        {
            if (busy) return;
            SetBusy(true);
            if (!quiet) JobStart(Loc.T("Searching for FiveM ..."));
            Thread t = new Thread(delegate()
            {
                AppLog.Context = "General";
                AppLog.BeginGroup();
                try
                {
                    Log(pickedFolder == null ? "Searching ..." : "Looking in the folder you picked: " + pickedFolder);
                    Sysinfo.Dump();
                    if (Finder.PretendVramMb > 0) Log("TEST MODE: pretending that the dedicated card has " + Finder.PretendVramMb + " MB of video memory (--pretend-vram). Nothing on this PC is changed by it.");
                    if (Finder.PretendUseMb > 0) Log("TEST MODE: pretending that the FiveM game holds " + Finder.PretendUseMb + " MB of video memory (--pretend-vram-use). Nothing on this PC is changed by it.");
                    if (!quiet) JobStep(Loc.T("Looking at your graphics card ..."), -1);
                    List<Gpu> g = Finder.Gpus();
                    string modeText;
                    string newMode = ComputeMode(g, out modeText);
                    mode = newMode;
                    foreach (Gpu x in g) Log("Graphics card: " + x.Vendor + " - " + x.Name);
                    Log("Mode: " + newMode + " (" + modeText + ")");
                    if (!quiet) JobStep(Loc.T("Looking for FiveM on your drives ..."), -1);
                    List<Adl.AppInfo> apps = null;
                    if (newMode == "amd") { try { apps = Adl.Apps(); } catch (Exception) { } }
                    List<string> roots = pickedFolder == null ? Finder.Roots(Log, apps) : Finder.RootsFromFolder(pickedFolder);
                    if (pickedFolder != null) foreach (string r in roots) Log("Found FiveM (picked): " + r);
                    List<Build> found = Finder.Builds(roots, apps);
                    if (pickedFolder != null) { foreach (Build old in builds) { bool has = false; foreach (Build n in found) if (string.Equals(n.Exe, old.Exe, StringComparison.OrdinalIgnoreCase)) has = true; if (!has) found.Add(old); } }
                    List<Build> gone = Finder.RememberAndRecall(roots, found, apps);
                    if (gone.Count > 0)
                    {
                        found.AddRange(gone);
                        found.Sort(delegate(Build p1, Build p2) { return string.Compare(p1.Exe, p2.Exe, StringComparison.OrdinalIgnoreCase); });
                        Log(gone.Count + " build(s) remembered from before (not on disk now; FiveM downloads them again when you start it).");
                    }
                    foreach (Build b in found)
                    {
                        if (newMode == "amd" && apps != null)
                        {
                            Adl.ProfileValues v = Adl.Read(b.FileName, b.Dir);
                            if (!v.Found) b.Status = "Not in Radeon Software yet";
                            else b.Status = "Radeon Chill " + (Val.First(v.Pf) == 1 ? "on" : "off") + ", cap now " + Val.First(v.Min) + " / " + Val.First(v.Max);
                        }
                        else if (newMode == "nvidia") b.Status = NvWorker.StatusOf(b, Log);
                        else b.Status = "Found";
                    }
                    bool needRadeon = false;
                    foreach (Build xb in found)
                        if (Finder.LastNew.Contains(xb.Exe)) { Log("New FiveM build: " + xb.FileName + " (" + xb.Status + ")"); if (xb.Status.StartsWith("Not in Radeon")) needRadeon = true; }
                    if (found.Count == 0) Log("No FiveM build found. Use \"Pick a folder ...\" and choose the folder where FiveM is installed.");
                    else Log(found.Count + " FiveM build(s) found.");
                    BeginInvoke(new Action(delegate
                    {
                        gpus = g; ShowGpus(modeText);
                        builds = found; FillBuilds(); FillRoots(roots);
                        SetBusy(false);
                    }));
                    if (resetRemoved >= 0)
                    {
                        int rr = resetRemoved; resetRemoved = -1;
                        JobDone("ok", Loc.T("List reset"), string.Format(Loc.T("Removed {0} build(s) that are not on disk now. Found {1}."), rr, found.Count));
                    }
                    else if (!quiet || Finder.LastNew.Count > 0)
                    {
                        if (found.Count == 0) JobDone("info", Loc.T("No FiveM build found"), Loc.T("Use \"Pick a folder ...\" and choose the folder where FiveM is installed."));
                        else if (Finder.LastNew.Count > 0)
                        {
                            List<string> nn = new List<string>();
                            foreach (Build nb in found) if (Finder.LastNew.Contains(nb.Exe)) nn.Add(nb.FileName);
                            JobDone("info", Loc.T("New FiveM build found"), string.Join(", ", nn.ToArray()) + (needRadeon ? "   " + Loc.T("Its status says it is not in Radeon Software yet. Add it there once (Gaming > Games), then press Search again.") : ""));
                        }
                        else
                        {
                            List<string> nm = new List<string>();
                            foreach (Build fb in found) nm.Add(fb.FileName);
                            JobDone("ok", string.Format(Loc.T("Found {0} FiveM build(s)"), found.Count), string.Join(", ", nm.ToArray()) + (gone.Count > 0 ? "   " + string.Format(Loc.T("{0} remembered from before (not on disk now)."), gone.Count) : ""));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Error while searching: " + ex.Message); AppLog.Debug("exception while searching: " + ex);
                    if (!quiet) JobDone("bad", Loc.T("It did not finish"), ex.Message);
                    BeginInvoke(new Action(delegate { SetBusy(false); }));
                }
                finally { AppLog.EndGroup(); }
            });
            t.IsBackground = true; t.Start();
        }

        void BrowseClick(object s, EventArgs e)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = Loc.T("Pick the folder where FiveM is installed (the folder that contains FiveM.app).");
                if (d.ShowDialog(this) == DialogResult.OK) Rescan(d.SelectedPath);
            }
        }

        void ApplyClick(object s, EventArgs e)
        {
            if (busy) return;
            List<Build> pick = new List<Build>();
            foreach (DataGridViewRow r in grid.Rows) if (Convert.ToBoolean(r.Cells[0].Value)) pick.Add((Build)r.Tag);
            if (pick.Count == 0) { MessageBox.Show(this, Loc.T("Tick at least one FiveM build."), Text); return; }
            int cap = (int)capBox.Value;
            bool useD = gpuSwitch.Visible && gpuSwitch.Checked;   // an integrated chip and a dedicated card are there, and the switch is on
            DialogResult dr = MessageBox.Show(this,
                string.Format(mode == "nvidia" ? Loc.T("The frame limit is set in the NVIDIA driver profile of each ticked build.\r\nThe old limit is remembered, so Undo can put it back.\r\n\r\nSet the cap to {0} FPS for {1} build(s)?") : Loc.T("Radeon Software will close for a short moment and open again.\r\nYour old settings file is backed up first.\r\n\r\nSet the cap to {0} FPS for {1} build(s)?"), cap, pick.Count),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) { Log("Apply cancelled."); return; }
            SetBusy(true);
            grid.ClearSelection();
            JobStart(Loc.T("Applying the frame cap ..."));
            Thread t = new Thread(delegate()
            {
                AppLog.Context = "Frame cap";
                AppLog.BeginGroup();
                Worker w = new Worker(); w.Log = Log;
                int nSet = 0, nAlready = 0, nBad = 0, idx = 0, nGpu = 0, nSkipped = 0;
                string firstBad = null, firstBadName = null, curName = "";
                bool reported = false;
                w.Step = delegate(string text, double sub) { JobStep(string.Format("{0} ({1} of {2}): {3}", curName, idx + 1, pick.Count, Loc.T(text)), (idx + sub) / pick.Count); };
                NvWorker nw = new NvWorker(); nw.Log = Log; nw.Step = w.Step;
                ICapWorker cw = mode == "nvidia" ? (ICapWorker)nw : (ICapWorker)w;
                try
                {
                    Log("Apply: cap " + cap + " FPS for " + pick.Count + " build(s).");
                    foreach (Build pb in pick) AppLog.Debug("  selected: " + pb.Exe + " (AMD list index " + pb.AdlIndex + ")");
                    foreach (Build b in pick)
                    {
                        curName = b.FileName;
                        Log(b.FileName + ": working ...");
                        Build bb = b;
                        BeginInvoke(new Action(delegate { MarkRow(bb, Loc.T("Working ..."), 0); }));
                        string res = cw.Apply(b, cap);
                        int kind = res.StartsWith("Set to") ? 1 : res.StartsWith("Already set") ? 2 : res.StartsWith("Skipped") ? 3 : 0;
                        if (useD && !(kind == 3) && GpuPref.Set(b.Exe, 2, Log)) nGpu++;
                        if (kind == 1) nSet++;
                        else if (kind == 2) nAlready++;
                        else if (kind == 3) nSkipped++;
                        else { nBad++; if (firstBad == null) { firstBad = res; firstBadName = b.FileName; } }
                        b.Status = res; Log(b.FileName + ": " + res);
                        int rowState = kind == 0 ? -1 : kind == 3 ? 0 : 1;
                        BeginInvoke(new Action(delegate { MarkRow(bb, Loc.TS(bb.Status), rowState); }));
                        idx++;
                    }
                    Log("Finished.");
                    reported = true;
                    if (nBad > 0) JobDone("bad", Loc.T("Not everything worked"), firstBadName + ": " + Loc.TS(firstBad));
                    else if (nSet == 0 && nAlready == 0 && nSkipped > 0) JobDone("info", Loc.T("Nothing to change"), string.Format(Loc.T("{0} build(s) are not on disk now. No NVIDIA profile is made for a build that is gone."), nSkipped));
                    else if (nSet == 0) JobDone("info", Loc.T("Nothing to change"), string.Format(mode == "nvidia" ? Loc.T("{0} build(s) already had the cap set to {1}. The NVIDIA driver was left alone.") : Loc.T("{0} build(s) already had the cap set to {1}. Radeon Software was left alone."), nAlready, cap) + (nGpu > 0 ? "  " + Loc.T("The dedicated graphics card is now used for the ticked builds.") : ""));
                    else JobDone("ok", string.Format(Loc.T("Frame cap set to {0}"), cap), string.Format(mode == "nvidia" ? Loc.T("Changed {0} build(s), {1} already had it. The NVIDIA driver profile was updated.") : Loc.T("Changed {0} build(s), {1} already had it. Radeon Software was restarted."), nSet, nAlready) + (nSkipped > 0 ? "  " + string.Format(Loc.T("{0} build(s) not on disk were skipped."), nSkipped) : "") + (nGpu > 0 ? "  " + Loc.T("The dedicated graphics card is now used for the ticked builds.") : ""));
                }
                catch (Exception ex)
                {
                    Log("Error: " + ex.Message); AppLog.Debug("exception while applying: " + ex);
                    if (!reported) { reported = true; JobDone("bad", Loc.T("It did not finish"), ex.Message); }
                }
                finally { AppLog.EndGroup(); }
                BeginInvoke(new Action(delegate { SetBusy(false); }));
            });
            t.IsBackground = true; t.Start();
        }

        // "Clean up old entries": looks (read only), shows what is old, and removes it after a yes
        void TidyClick(object s, EventArgs e)
        {
            if (busy) return;
            SetBusy(true);
            JobStart(Loc.T("Looking for old entries ..."));
            List<Build> snap = new List<Build>(builds);
            bool nvd = mode == "nvidia";
            Thread t = new Thread(delegate()
            {
                AppLog.Context = "Frame cap";
                AppLog.BeginGroup();
                bool again = false;
                try
                {
                    Log("Clean up: looking for old entries ...");
                    string refuse;
                    List<TidyItem> items = Tidy.Look(snap, nvd, Log, out refuse);
                    if (refuse == "build") JobDone("info", Loc.T("Search first"), Loc.T("No FiveM build was found on disk, so the tool cannot tell which entries are old. Press Search again first."));
                    else if (refuse == "driver") JobDone("bad", Loc.T("It did not finish"), Loc.T("Could not read the NVIDIA driver profiles"));
                    else if (items.Count == 0) { Log("Clean up: nothing to clean up."); JobDone("info", Loc.T("Nothing to clean up"), Loc.T("No old entries of this tool were found.")); }
                    else
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (TidyItem it in items) sb.AppendLine("- " + Loc.T(it.Kind == "nvidia" ? "NVIDIA profile" : "Windows graphics list") + ": " + (it.Kind == "nvidia" ? it.Name : Path.GetFileName(it.Name)) + "  (" + Loc.T(it.Why) + ")");
                        bool yes = false;
                        Invoke(new Action(delegate
                        {
                            yes = MessageBox.Show(this, string.Format(Loc.T("These old entries can go:\r\n\r\n{0}\r\n\r\nThe old data is saved first (the Backups button opens the folder). When an entry is needed again, applying the cap makes it again.\r\n\r\nRemove them?"), sb.ToString().TrimEnd()),
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                        }));
                        if (!yes) { Log("Clean up cancelled."); JobDone("info", Loc.T("Cancelled"), Loc.T("Nothing was removed.")); }
                        else
                        {
                            JobStart(Loc.T("Removing the old entries ..."));
                            int removed; string problem = Tidy.Remove(items, Log, out removed);
                            Log("Clean up: removed " + removed + " of " + items.Count + " old entries.");
                            if (problem != null) JobDone("bad", Loc.T("Not everything worked"), Loc.TS(problem));
                            else JobDone("ok", Loc.T("Old entries removed"), string.Format(Loc.T("Removed {0} old entries. The old data was saved first."), removed));
                            again = true;
                        }
                    }
                }
                catch (Exception ex) { Log("Error: " + ex.Message); AppLog.Debug("exception while cleaning up: " + ex); JobDone("bad", Loc.T("It did not finish"), ex.Message); }
                finally { AppLog.EndGroup(); }
                BeginInvoke(new Action(delegate { SetBusy(false); if (again) Rescan(null, true); }));
            });
            t.IsBackground = true; t.Start();
        }

        // "Undo last change": puts the cap values back to what they were before the last change this tool made.
        void UndoClick(object s, EventArgs e)
        {
            if (busy) return;
            List<Build> pick = new List<Build>();
            foreach (DataGridViewRow r in grid.Rows) if (Convert.ToBoolean(r.Cells[0].Value)) pick.Add((Build)r.Tag);
            if (pick.Count == 0) { MessageBox.Show(this, Loc.T("Tick at least one FiveM build."), Text); return; }
            List<Build> todo = new List<Build>();
            foreach (Build pb in pick) if (CapMemory.Get(pb.Exe) != null || GpuPref.HasMemory(pb.Exe)) todo.Add(pb);
            if (todo.Count == 0)
            {
                Log("Undo: nothing was saved for the ticked build(s), because this tool has not changed them yet.");
                activity.Finish("info", Loc.T("Nothing to undo"), Loc.T("This tool has not changed the ticked builds yet, so it has nothing to put back."));
                return;
            }
            DialogResult dr = MessageBox.Show(this,
                string.Format(mode == "nvidia" ? Loc.T("Put the cap back to what it was before the last change for {0} build(s)?\r\n\r\nThe NVIDIA driver profile is changed back.") : Loc.T("Put the cap back to what it was before the last change for {0} build(s)?\r\n\r\nRadeon Software will close for a short moment and open again.\r\nYour old settings file is backed up first."), todo.Count),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) { Log("Undo cancelled."); return; }
            SetBusy(true);
            grid.ClearSelection();
            JobStart(Loc.T("Putting the cap back ..."));
            Thread t = new Thread(delegate()
            {
                AppLog.Context = "Frame cap";
                AppLog.BeginGroup();
                Worker w = new Worker(); w.Log = Log;
                int nBack = 0, nAlready = 0, nBad = 0, idx = 0;
                List<string> backTo = new List<string>();
                string firstBad = null, firstBadName = null, curName = "";
                bool reported = false;
                w.Step = delegate(string text, double sub) { JobStep(string.Format("{0} ({1} of {2}): {3}", curName, idx + 1, todo.Count, Loc.T(text)), (idx + sub) / todo.Count); };
                NvWorker nw = new NvWorker(); nw.Log = Log; nw.Step = w.Step;
                ICapWorker cw = mode == "nvidia" ? (ICapWorker)nw : (ICapWorker)w;
                try
                {
                    Log("Undo: putting the cap back for " + todo.Count + " build(s).");
                    foreach (Build b in todo)
                    {
                        curName = b.FileName;
                        string[] before = CapMemory.Get(b.Exe);
                        if (before != null) Log(b.FileName + ": putting back min=[" + before[0] + "] max=[" + before[1] + "] ...");
                        Build bb = b;
                        BeginInvoke(new Action(delegate { MarkRow(bb, Loc.T("Working ..."), 0); }));
                        string res = before != null ? cw.Revert(b, before[0], before[1]) : "Nothing to undo: the cap is already back.";
                        GpuPref.Undo(b.Exe, Log);
                        int kind = res.StartsWith("Cap put back") ? 1 : res.StartsWith("Nothing to undo") ? 2 : 0;
                        if (kind == 1)
                        {
                            nBack++;
                            string bt = Val.First(before[0]) == Val.First(before[1]) ? Val.First(before[0]).ToString() : Val.First(before[0]) + " / " + Val.First(before[1]);
                            if (!backTo.Contains(bt)) backTo.Add(bt);
                        }
                        else if (kind == 2) { nAlready++; CapMemory.Remove(b.Exe); }
                        else { nBad++; if (firstBad == null) { firstBad = res; firstBadName = b.FileName; } }
                        b.Status = res; Log(b.FileName + ": " + res);
                        int rowState = kind == 0 ? -1 : 1;
                        BeginInvoke(new Action(delegate { MarkRow(bb, Loc.TS(bb.Status), rowState); }));
                        idx++;
                    }
                    Log("Finished.");
                    reported = true;
                    if (nBad > 0) JobDone("bad", Loc.T("Not everything worked"), firstBadName + ": " + Loc.TS(firstBad));
                    else if (nBack == 0) JobDone("info", Loc.T("Nothing to undo"), string.Format(Loc.T("The cap was already back for {0} build(s)."), nAlready));
                    else JobDone("ok", Loc.T("Cap put back"), mode == "nvidia" ? string.Format(Loc.T("Put back {0} build(s). The NVIDIA driver profile was changed back."), nBack) : string.Format(Loc.T("Put back {0} build(s) to {1} FPS. Radeon Software was restarted."), nBack, string.Join("  ", backTo.ToArray())));
                }
                catch (Exception ex)
                {
                    Log("Error: " + ex.Message); AppLog.Debug("exception while undoing: " + ex);
                    if (!reported) { reported = true; JobDone("bad", Loc.T("It did not finish"), ex.Message); }
                }
                finally { AppLog.EndGroup(); }
                BeginInvoke(new Action(delegate { SetBusy(false); }));
            });
            t.IsBackground = true; t.Start();
        }

        // ---------------------------------------------------------------- DNS page
        void BuildDnsPage()
        {
            Card cNet = NewCard("Network connections", Ico.Globe, 12, 196); pageDns.Controls.Add(cNet);
            dnsGrid = MakeGrid(16, 44, CardW - 32, 100);
            dnsGrid.Columns.Add(CheckCol(46)); dnsGrid.Columns.Add(TextCol("Connection", 150, false)); dnsGrid.Columns.Add(TextCol("Network card", 330, false)); dnsGrid.Columns.Add(TextCol("Speed", 100, false)); dnsGrid.Columns.Add(TextCol("DNS now", 200, true));
            cNet.Controls.Add(dnsGrid);
            dnsState = Small("Saved original DNS: ...", 16, 154, CardW - 180, 36, "muted"); cNet.Controls.Add(dnsState);
            dnsRefresh = Btn(Ico.Refresh, "Refresh", CardW - 16 - 130, 152, 130, 34); dnsRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right; dnsRefresh.Click += delegate { RefreshDns(); }; cNet.Controls.Add(dnsRefresh);

            Card cFlush = NewCard("Clear the DNS cache", Ico.Delete, 220, 116); pageDns.Controls.Add(cFlush);
            dnsCount = new Label(); dnsCount.Left = CardW - 16 - 340; dnsCount.Top = 11; dnsCount.Width = 340; dnsCount.Height = 24; dnsCount.TextAlign = ContentAlignment.MiddleRight;
            dnsCount.Font = new Font("Segoe UI", 10f, FontStyle.Bold); dnsCount.Tag = DnsTool.IsAdmin() ? "accent" : "muted"; dnsCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            dnsCount.Text = DnsTool.IsAdmin() ? "" : Loc.T("Counting needs administrator"); cFlush.Controls.Add(dnsCount);
            dnsFlush = Btn(Ico.Delete, "Clear DNS cache", 16, 50, 220, 44);
            dnsFlush.Click += delegate { RunDns("--dns-flush " + (cbExtraFlush.Checked ? "extra" : "plain"), "Clearing the DNS cache ...", "flush", null); }; cFlush.Controls.Add(dnsFlush);
            cFlush.Controls.Add(Small("Fixes \"site not found\" after a DNS or network change. It tries up to 3 times and checks the cache is really empty. Windows asks for permission first.", 252, 46, CardW - 270, 38, "muted"));
            cbExtraFlush = new CheckBox(); cbExtraFlush.Text = Loc.T("Also clear the NetBIOS and ARP caches (a bit more thorough)"); cbExtraFlush.Left = 252; cbExtraFlush.Top = 84; cbExtraFlush.Width = 600; cbExtraFlush.Height = 24; cFlush.Controls.Add(cbExtraFlush);

            Card cChange = NewCard("Change DNS", Ico.Globe, 348, 170); pageDns.Controls.Add(cChange);
            rbCf = new RadioButton(); rbCf.Text = Loc.T("Cloudflare (default)   1.1.1.1"); rbCf.Left = 16; rbCf.Top = 46; rbCf.Width = 300; rbCf.Height = 26; rbCf.Checked = true; cChange.Controls.Add(rbCf);
            rbGoogle = new RadioButton(); rbGoogle.Text = Loc.T("Google   8.8.8.8"); rbGoogle.Left = 330; rbGoogle.Top = 46; rbGoogle.Width = 240; rbGoogle.Height = 26; cChange.Controls.Add(rbGoogle);
            rbBoth = new RadioButton(); rbBoth.Text = Loc.T("Cloudflare + Google"); rbBoth.Left = 584; rbBoth.Top = 46; rbBoth.Width = 300; rbBoth.Height = 26; cChange.Controls.Add(rbBoth);
            int dw1 = FitW("Use this DNS on the ticked connections", 340); dnsSet = Btn(Ico.Globe, "Use this DNS on the ticked connections", 16, 84, dw1, 44); dnsSet.Tag = "primary"; dnsSet.Click += DnsSetClick; cChange.Controls.Add(dnsSet);
            dnsUndo = Btn(Ico.Undo, "Undo: put DNS back how it was", 16 + dw1 + 10, 84, FitW("Undo: put DNS back how it was", 290), 44); dnsUndo.Click += DnsUndoClick; cChange.Controls.Add(dnsUndo);
            cChange.Controls.Add(Small("Windows asks for permission. The old setting is saved first, so Undo puts it back exactly.", 16, 138, CardW - 32, 22, "muted"));
        }

        // Only an administrator can see the DNS cache, so the live number is shown in administrator mode only. It counts up or down.
        void RefreshDnsCount()
        {
            if (!DnsTool.IsAdmin() || dnsCounting) return;
            dnsCounting = true;
            Thread t = new Thread(delegate()
            {
                int n = DnsTool.CountNow();
                Ui(delegate
                {
                    dnsCounting = false;
                    if (n < 0) { dnsCount.Text = Loc.T("Cache size: unknown"); dnsShown = -1; return; }
                    int from = dnsShown < 0 ? n : dnsShown;
                    dnsShown = n;
                    if (from == n) { dnsCount.Text = string.Format(Loc.T("Now in the cache: {0} names"), n); return; }
                    Fx.Run(450, delegate(float p) { dnsCount.Text = string.Format(Loc.T("Now in the cache: {0} names"), from + (int)Math.Round((n - from) * Fx.EaseOut(p))); }, null);
                });
            });
            t.IsBackground = true; t.Start();
        }

        void RefreshDns()
        {
            string oldCtx = AppLog.Context; AppLog.Context = "DNS";
            try
            {
                dnsGrid.Rows.Clear();
                foreach (Nic n in DnsTool.Adapters())
                {
                    int i = dnsGrid.Rows.Add(n.Default, n.Name, n.Desc, n.Speed, n.Now);
                    dnsGrid.Rows[i].Tag = n;
                }
                dnsState.Text = Loc.T("Saved original DNS: ") + DnsTool.BackupSummary();
                RefreshDnsCount();
                AppLog.Debug("DNS list refreshed: " + dnsGrid.Rows.Count + " connection(s)");
            }
            catch (Exception ex) { Log("Could not read the network connections: " + ex.Message); AppLog.Debug("DNS list error: " + ex); }
            finally { AppLog.Context = oldCtx; }
        }

        // job: flush, set or restore. okDetail is the sentence for the activity card when it worked (null for flush, which counts the entries).
        void RunDns(string args, string what, string job, string okDetail)
        {
            if (busy) return;
            SetBusy(true);
            string runTitle = job == "flush" ? Loc.T("Clearing the DNS cache ...") : job == "set" ? Loc.T("Changing the DNS ...") : Loc.T("Putting the DNS back ...");
            JobStart(runTitle);
            Thread t = new Thread(delegate()
            {
                AppLog.Context = "DNS";
                AppLog.BeginGroup();
                bool reported = false;
                try
                {
                    Log(what);
                    Action<string> stepFn = delegate(string st) { JobStep(Loc.T(st), -1); };
                    bool ok;
                    if (job == "flush")
                        ok = DnsTool.PreviewFlush(args.EndsWith("extra"), Log, stepFn, delegate(int n)
                        {
                            bool yes = false;
                            if (n >= 0) JobStep(string.Format(Loc.T("{0} names in the cache. Waiting for your answer ..."), n), -1);
                            Invoke(new Action(delegate
                            {
                                string q = n >= 0 ? string.Format(Loc.T("Windows holds {0} names in the DNS cache right now.\r\n\r\nClear them?"), n) : Loc.T("The DNS cache will be cleared. Continue?");
                                yes = MessageBox.Show(this, q, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                            }));
                            return yes;
                        });
                    else ok = DnsTool.RunElevated(args, Log, stepFn);
                    Log(ok ? "Done." : DnsTool.Outcome == "declined" ? "You chose not to clear the cache. Nothing was changed." : "That did not finish. Open the full log for details.");
                    reported = true;
                    string outcome = DnsTool.Outcome;
                    if (ok)
                    {
                        string okTitle = job == "flush" ? Loc.T("DNS cache cleared") : job == "set" ? Loc.T("DNS changed") : Loc.T("DNS put back");
                        string detail = okDetail;
                        if (job == "flush")
                        {
                            int b4 = DnsTool.LastBefore, af = DnsTool.LastAfter;
                            if (b4 < 0 || af < 0) detail = Loc.T("Windows said the flush worked.");
                            else if (af > 0) detail = string.Format(Loc.T("DNS cache entries: {0} before, {1} now. Windows adds a few names back by itself, like the ones from the hosts file."), b4, af);
                            else detail = string.Format(Loc.T("DNS cache entries: {0} before, {1} now."), b4, af);
                        }
                        JobDone("ok", okTitle, detail);
                    }
                    else if (outcome == "declined") JobDone("info", Loc.T("Cancelled"), Loc.T("Nothing was cleared."));
                    else if (outcome == "cancelled") JobDone("info", Loc.T("Cancelled"), Loc.T("You closed the Windows permission window. Nothing was changed."));
                    else if (outcome == "timeout") JobDone("bad", Loc.T("It did not finish"), Loc.T("It timed out waiting for the Windows permission window."));
                    else if (outcome == "noreport") JobDone("bad", Loc.T("It did not finish"), Loc.T("The permission step did not report back."));
                    else JobDone("bad", Loc.T("It did not finish"), Loc.T("Open the full log for details."));
                }
                catch (Exception ex)
                {
                    Log("Error: " + ex.Message); AppLog.Debug("DNS run error: " + ex);
                    if (!reported) { reported = true; JobDone("bad", Loc.T("It did not finish"), ex.Message); }
                }
                finally { AppLog.EndGroup(); }
                BeginInvoke(new Action(delegate { RefreshDns(); SetBusy(false); }));
            });
            t.IsBackground = true; t.Start();
        }

        void DnsSetClick(object s, EventArgs e)
        {
            if (busy) return;
            List<string> ids = new List<string>();
            foreach (DataGridViewRow r in dnsGrid.Rows) if (Convert.ToBoolean(r.Cells[0].Value)) ids.Add(((Nic)r.Tag).Id);
            if (ids.Count == 0) { MessageBox.Show(this, Loc.T("Tick at least one network connection."), Text); return; }
            string prov = rbGoogle.Checked ? "google" : rbBoth.Checked ? "both" : "cloudflare";
            string name = rbGoogle.Checked ? "Google" : rbBoth.Checked ? "Cloudflare + Google" : "Cloudflare";
            DialogResult dr = MessageBox.Show(this,
                string.Format(Loc.T("Change DNS to {0} on {1} connection(s)?\r\n\r\nWindows will ask for permission. The current setting is saved first, so \"Undo\" can put it back."), name, ids.Count),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) { Log("DNS change cancelled."); return; }
            RunDns("--dns-set " + prov + " " + string.Join(",", ids.ToArray()), "Changing DNS to " + name + " ...", "set", string.Format(Loc.T("{0} on {1} connection(s). Undo puts it back."), name, ids.Count));
        }

        void DnsUndoClick(object s, EventArgs e)
        {
            if (busy) return;
            DialogResult dr = MessageBox.Show(this,
                string.Format(Loc.T("Put DNS back the way it was before this program changed it?\r\n\r\n{0}\r\n\r\nWindows will ask for permission."), DnsTool.BackupSummary()),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) { Log("Undo cancelled."); return; }
            RunDns("--dns-restore", "Putting DNS back how it was ...", "restore", Loc.T("The saved settings are back on your connections."));
        }

        // ---------------------------------------------------------------- graphics page (Extended Texture Budget and Post FX)
        void BuildGfxPage()
        {
            Font small = new Font("Segoe UI", 9f);
            Card cB = NewCard("Extended Texture Budget", Ico.Monitor, 12, 206); pageGfx.Controls.Add(cB);
            Label bDesc;
            UiUtil.Wrapped(cB, "How much video memory FiveM may use for server textures (maps, MLOs, cars). If it is too low, many MLOs pop in and out when a lot of them are close together. Set it as high as your graphics card can take.", 16, 44, CardW - 32, small, "muted", out bDesc);
            gfxBudget = new NumBox(); gfxBudget.Minimum = 0; gfxBudget.Maximum = 100; gfxBudget.Value = 100; gfxBudget.Left = 16; gfxBudget.Top = 96; gfxBudget.Width = 100;
            cB.Controls.Add(gfxBudget);
            Label pct = Small("%", 124, 104, 24, 24, null); pct.Font = new Font("Segoe UI", 11f, FontStyle.Bold); cB.Controls.Add(pct);
            int hw = FitW("Highest (100)", 150);
            gfxHighest = Btn(Ico.Accept, "Highest (100)", 156, 96, hw, 36); gfxHighest.Click += delegate { gfxBudget.Value = 100; }; cB.Controls.Add(gfxHighest);
            // "Recommended (N %)": from the video memory of the card, so that about 20 percent of it stays free (see the class Budget). Only there when the memory of a card is known.
            int rw = Math.Max(190, TextRenderer.MeasureText(string.Format(Loc.T("Recommended ({0} %)"), 100), new Font("Segoe UI", 9.5f)).Width + 56);
            gfxRecommend = Btn(Ico.Accept, "Recommended (100 %)", 156 + hw + 10, 96, rw, 36); gfxRecommend.Visible = false;
            gfxRecommend.Click += delegate { if (gfxRecN >= 0) gfxBudget.Value = gfxRecN; };
            tip.SetToolTip(gfxRecommend, Loc.T("Leaves about 20 % of the video memory of your card free. It is an estimate: your other graphics settings change the real number."));
            cB.Controls.Add(gfxRecommend);
            int nowLeft = 156 + hw + 10 + rw + 18;
            gfxBudgetNow = Small("", nowLeft, 104, CardW - nowLeft - 16, 22, "muted"); cB.Controls.Add(gfxBudgetNow);
            gfxBudgetHint = Small("Higher uses more video memory. If FiveM stutters or crashes, go a bit lower.", 16, 142, CardW - 32, 22, "muted"); cB.Controls.Add(gfxBudgetHint);
            gfxBudget.ValueChanged += delegate { UpdateBudgetAdvice(); };
            // the live meter: the REAL video memory the FiveM game holds on the dedicated card (a Windows counter), read every 2 seconds while this tab is open
            gfxMeterBar = new MeterBar(); gfxMeterBar.Left = 16; gfxMeterBar.Top = 182; gfxMeterBar.Width = 280; gfxMeterBar.Height = 12; cB.Controls.Add(gfxMeterBar);
            gfxMeterText = Small("FiveM is not running. Start it and join a server: this line then shows how much video memory it really uses.", 312, 172, CardW - 312 - 16, 34, "muted"); cB.Controls.Add(gfxMeterText);
            tip.SetToolTip(gfxMeterBar, Loc.T("The real video memory that FiveM holds on your dedicated card, read from Windows every 2 seconds while this tab is open. The mark is at 80 %: above it less than 20 % of the card is free."));
            tip.SetToolTip(gfxMeterText, Loc.T("The real video memory that FiveM holds on your dedicated card, read from Windows every 2 seconds while this tab is open. The mark is at 80 %: above it less than 20 % of the card is free."));

            Card cP = NewCard("Post FX", Ico.Palette, 230, 176); pageGfx.Controls.Add(cP);
            Label pDesc;
            UiUtil.Wrapped(cP, "Post FX adds glow and strong light effects. At high levels some MLOs and cars look far too bright. Normal gives the plain look.", 16, 44, CardW - 32, small, "muted", out pDesc);
            gfxPost = new DropBox(); gfxPost.Left = 16; gfxPost.Top = 96; gfxPost.Width = 200;
            for (int i = 0; i < 4; i++) gfxPost.Items.Add(Loc.T(GfxTool.PostFxName(i)));
            gfxPost.SelectedIndex = 0;
            cP.Controls.Add(gfxPost);
            int nw = FitW("Set to Normal", 160);
            gfxNormal = Btn(Ico.Accept, "Set to Normal", 228, 96, nw, 36); gfxNormal.Click += delegate { gfxPost.SelectedIndex = 0; }; cP.Controls.Add(gfxNormal);
            gfxPostNow = Small("", 228 + nw + 18, 104, CardW - (228 + nw + 18) - 16, 22, "muted"); cP.Controls.Add(gfxPostNow);
            cP.Controls.Add(Small("At Normal, Motion Blur Strength and In-Game Depth Of Field Effect are greyed out in the game's graphics menu. High, Very high and Ultra unlock them.", 16, 144, CardW - 32, 22, "muted"));

            // the two options of FiveM's own Settings menu that matter before you join a server (two columns: a switch, a title, what it does, what the file says now)
            Card cN = NewCard("FiveM game options", Ico.Monitor, 418, 160); pageGfx.Controls.Add(cN);
            int colW = (CardW - 32 - 24) / 2;
            Font bold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            for (int col = 0; col < 2; col++)
            {
                int x0 = 16 + col * (colW + 24);
                ToggleSwitch sw0 = new ToggleSwitch(); sw0.Left = x0; sw0.Top = 48; cN.Controls.Add(sw0);
                Label ttl = new Label(); ttl.Text = Loc.T(col == 0 ? "Fix interface lag (NUI in-process GPU)" : "Fix stutter at high frame rates"); ttl.Font = bold; ttl.AutoSize = false; ttl.Left = x0 + 62; ttl.Top = 47; ttl.Width = colW - 62; ttl.Height = 24; ttl.TextAlign = ContentAlignment.MiddleLeft; cN.Controls.Add(ttl);
                Label dsc;
                UiUtil.Wrapped(cN, col == 0 ? "Fixes 'UI lag' when the GPU is busy. FiveM warns that it may be unstable and cause GPU crashes." : "Turns FiveM's audio frame limiter off, which stops hitches at high frame rates.", x0 + 62, 74, colW - 62, small, "muted", out dsc);
                Label now = Small("", x0 + 62, 114, colW - 62, 38, "muted"); cN.Controls.Add(now);
                if (col == 0) { gfxUiFix = sw0; gfxUiNow = now; tip.SetToolTip(sw0, Loc.T("Needs a restart of FiveM. In FiveM's own settings this is the box \"NUI in-process GPU\".")); }
                else { gfxStutterFix = sw0; gfxStutterNow = now; tip.SetToolTip(sw0, Loc.T("In FiveM's own settings this is the box \"Audio frame limiter\". It is unchecked when this switch is on.")); }
            }

            Card cA = NewCard("", Ico.Accept, 590, 150); pageGfx.Controls.Add(cA); gfxCard = cA;
            int sw = FitW("Save the settings", 240);
            gfxApply = Btn(Ico.Accept, "Save the settings", 16, 44, sw, 44); gfxApply.Tag = "primary"; gfxApply.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold); gfxApply.Click += GfxApplyClick; cA.Controls.Add(gfxApply);
            int cw = FitW("Close FiveM for me", 210);
            gfxClose = Btn(Ico.Cancel, "Close FiveM for me", 16 + sw + 10, 50, cw, 32); gfxClose.Visible = false; gfxClose.Click += GfxCloseClick; cA.Controls.Add(gfxClose);
            int ow = FitW("Open the settings folder", 220);
            gfxOpen = Btn(Ico.OpenFolder, "Open the settings folder", CardW - 16 - ow, 50, ow, 32); gfxOpen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gfxOpen.Click += delegate { try { Process.Start("explorer.exe", "\"" + GfxTool.Dir + "\""); } catch (Exception) { } };
            cA.Controls.Add(gfxOpen);
            cA.Controls.Add(Small("Each file gets a backup first. FiveM must be closed, because FiveM writes both files again when it closes.", 16, 98, CardW - 32, 22, "muted"));
            cA.Controls.Add(Small("These settings are for FiveM with the GTA V Legacy edition.", 16, 120, CardW - 32, 22, "muted"));
        }

        bool gfxInit = false;

        // the button and the line under the number: advice from the memory of the card (about 20 percent stays free), and an orange warning above it
        void UpdateBudgetAdvice()
        {
            if (gfxRecommend == null || gfxBudgetHint == null) return;
            Gpu card = Budget.Pick(gpus);
            int rec = card == null ? -1 : Budget.Recommended(card.Vram);
            gfxRecN = rec;
            if (rec < 0)
            {
                gfxRecommend.Visible = false;
                gfxBudgetHint.Text = Loc.T(Finder.HasUnsupportedDedicated(gpus) ? "Not supported at the moment for an Intel Arc card, because it could not be tested." : "Higher uses more video memory. If FiveM stutters or crashes, go a bit lower.");
                gfxBudgetHint.Tag = "muted"; gfxBudgetHint.ForeColor = Theme.Text2;
                return;
            }
            gfxRecommend.Visible = true;
            gfxRecommend.Text = string.Format(Loc.T("Recommended ({0} %)"), rec);
            gfxRecommend.Invalidate();
            int cur = (int)gfxBudget.Value;
            string level = Budget.Level(card.Vram, cur, rec);
            string estimate = CacheTool.Size((long)(Budget.EstimateMb(cur) * 1048576.0));
            if (level == "bad")
                gfxBudgetHint.Text = string.Format(Loc.T("Too high: at {1} % the game's memory bar would show about {2}, and your card has {0}. FiveM will likely stutter or crash."), CacheTool.Size(card.Vram), cur, estimate);
            else if (level == "warn")
                gfxBudgetHint.Text = string.Format(Loc.T("Above {0} % less than 20 % of your video memory stays free. FiveM can stutter or crash."), rec);
            else
                gfxBudgetHint.Text = string.Format(Loc.T("Your card has {0}. At {1} % the game's memory bar should show about {2}, which leaves about {3} % free."), CacheTool.Size(card.Vram), cur, estimate, Budget.FreePercent(card.Vram, cur));
            gfxBudgetHint.Tag = level;
            gfxBudgetHint.ForeColor = level == "bad" ? Theme.Bad : level == "warn" ? Theme.Warn : Theme.Good;
        }

        // ---- the live meter
        // the ids of the FiveM game processes (FiveM_b####_GTAProcess)
        static List<int> GamePids()
        {
            List<int> ids = new List<int>();
            if (Finder.PretendUseMb > 0) { ids.Add(-1); return ids; }
            foreach (Process p in CacheTool.Running())
            {
                try { if (Regex.IsMatch(p.ProcessName, @"^FiveM(_b\d+)?_GTAProcess$", RegexOptions.IgnoreCase)) ids.Add(p.Id); }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            return ids;
        }

        // one reading and its evaluation, without any window part (the timer and the tests use it)
        VramState ReadVram(List<int> pids)
        {
            List<GpuMemSample> samples = null;
            if (pids.Count > 0)
            {
                if (Finder.PretendUseMb > 0)
                {
                    samples = new List<GpuMemSample>();
                    GpuMemSample m = new GpuMemSample(); m.Pid = -1; m.Luid = meterLuid ?? "pretend"; m.DedicatedBytes = Finder.PretendUseMb * 1048576L; samples.Add(m);
                }
                else samples = GpuMem.Reader();
            }
            VramState st = VramMeter.Evaluate(samples, pids, meterLuid, meterTotal);
            meterTracker.Update(st);
            return st;
        }

        void StartVramRead()
        {
            if (meterReading || gfxMeterText == null) return;
            if (Finder.HasUnsupportedDedicated(gpus)) { ApplyVramUnsupported(); return; }
            if (!IsHandleCreated) return;
            meterReading = true;
            List<int> pids = GamePids();
            Thread t = new Thread(delegate()
            {
                VramState st = null;
                try { st = ReadVram(pids); } catch (Exception ex) { AppLog.Debug("the video memory meter failed: " + ex.Message); }
                Ui(delegate { meterReading = false; if (st != null) ApplyVramState(st); });
            });
            t.IsBackground = true; t.Start();
        }

        void SetMeterLook(string text, string level, float fill)
        {
            gfxMeterText.Text = text; gfxMeterText.Tag = level;
            Color c = level == "bad" ? Theme.Bad : level == "warn" ? Theme.Warn : level == "good" ? Theme.Good : Theme.Text2;
            gfxMeterText.ForeColor = c; gfxMeterBar.Set(fill, c);
        }

        void ApplyVramUnsupported() { SetMeterLook(Loc.T("Not supported at the moment for an Intel Arc card, because it could not be tested."), "muted", 0f); }

        void ApplyVramState(VramState st)
        {
            string text, level = "muted"; float fill = 0f;
            if (st.State == "off")
            {
                bool had = st.PeakBytes > 0 && st.TotalBytes > 0;
                text = had ? string.Format(Loc.T("FiveM is not running. Last time: highest {0} of {1} ({2} %)."), CacheTool.Size(st.PeakBytes), CacheTool.Size(st.TotalBytes), st.PeakPercent) : Loc.T("FiveM is not running. Start it and join a server: this line then shows how much video memory it really uses.");
                if (had) { level = st.PeakLevel; fill = (float)st.PeakBytes / st.TotalBytes; }
            }
            else if (st.State == "nodata") { text = Loc.T("FiveM is running, but Windows gives no video memory numbers for it (it needs Windows 10 version 1709 or newer)."); level = "warn"; }
            else if (st.OnOtherChip) { text = string.Format(Loc.T("FiveM runs mostly on another graphics chip, not on your dedicated card. Its memory use there: {0}."), CacheTool.Size(st.OtherBytes)); level = "warn"; }
            else if (st.TotalBytes > 0)
            {
                text = string.Format(Loc.T("FiveM uses {0} of your {1} video memory ({2} %). Highest this session: {3}."), CacheTool.Size(st.UsedBytes), CacheTool.Size(st.TotalBytes), st.UsedPercent, CacheTool.Size(st.PeakBytes));
                level = st.Level; fill = (float)st.UsedBytes / st.TotalBytes;
            }
            else text = string.Format(Loc.T("FiveM uses {0} of video memory."), CacheTool.Size(st.UsedBytes));
            SetMeterLook(text, level, fill);
        }

        // after every search: which card the meter watches (its LUID and its memory)
        void UpdateMeterCard()
        {
            Gpu card = Budget.Pick(gpus);
            meterTotal = card != null ? card.Vram : 0;
            meterLuid = null;
            if (card != null)
                foreach (WinGpu.Adapter a in WinGpu.Query())
                    if (a.Luid.Length > 0 && string.Equals(a.Name, (card.Name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) meterLuid = a.Luid;
            meterTracker = new VramTracker();
            if (Finder.HasUnsupportedDedicated(gpus)) ApplyVramUnsupported();
        }

        // for --gfx-ui-test only
        public void TestSetMeterCard(string luid, long total) { meterLuid = luid; meterTotal = total; meterTracker = new VramTracker(); }
        public void TestMeterPoll(List<int> pids) { ApplyVramState(ReadVram(pids)); }
        public string[] TestMeter() { return new string[] { gfxMeterText.Text, (string)gfxMeterText.Tag }; }
        public float TestMeterFill() { return gfxMeterBar.Value; }
        public Color TestMeterColor() { return gfxMeterBar.Fill; }
        public void TestSetGpusReal(List<Gpu> list) { gpus = list; string mt; mode = ComputeMode(list, out mt); ShowGpus(mt); SetBusy(false); }
        public bool[] TestCapButtons() { return new bool[] { applyBtn.Enabled, undoBtn.Enabled }; }
        public void TestStartVramRead() { StartVramRead(); }
        public string TestModeLabel() { return modeLabel.Text; }
        public void TestSetGpus(List<Gpu> list) { gpus = list; ShowGpus("NVIDIA mode: works with the NVIDIA driver."); }
        // for --dpi-test: the window and every tab
        public void DpiCheckAll(Action<bool, string> ck)
        {
            ck(!(Dpi.Aware && Dpi.Scale > 1.001f) || IsDpiScaled, "main window: was scaled by " + Dpi.Scale);
            ck(Math.Abs(TitleBar.Height - Dpi.S(46)) <= 1, "main window: title bar " + TitleBar.Height + " px (expected " + Dpi.S(46) + ")");
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            ck(Width <= wa.Width && Height <= wa.Height, "main window: " + Width + "x" + Height + " fits the work area " + wa.Width + "x" + wa.Height);
            // the tabs in the title bar end before the buttons on the right (a tab that is too wide covers them)
            int lastRight = 0;
            foreach (ChromeTab ct in TabsByPage()) lastRight = Math.Max(lastRight, ct.Right);
            int buttonsLeft = themeSystem.Parent != null ? themeSystem.Parent.Left : Width;
            ck(lastRight <= buttonsLeft, "title bar: the last tab ends at " + lastRight + " px, the buttons on the right start at " + buttonsLeft + " px");
            // each tab is as wide as its text plus the room for the icon, scaled once (a size measured in real pixels and then scaled again would be too wide)
            foreach (ChromeTab ct in TabsByPage())
            {
                int want = TextRenderer.MeasureText(ct.Text, ct.Font).Width + Dpi.S(62);
                ck(Math.Abs(ct.Width - want) <= 3, "title bar: the tab \"" + ct.Text + "\" is " + ct.Width + " px wide, expected about " + want + " px");
            }
            // the log filter pills end before the buttons on the right of the log
            foreach (Control lc in logPanel.Controls) if (lc is Panel && !(lc is Card) && lc.Dock == DockStyle.Top) DpiCheck.Container(lc, "log bar", ck);
            Panel[] pages = new Panel[] { pageCap, pageDns, pageCache, pageGfx };
            string[] names = new string[] { "Frame cap tab", "DNS tab", "FiveM cache tab", "Graphics tab" };
            for (int p = 0; p < 4; p++)
            {
                ShowPage(p);
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(40); }
                DpiCheck.Tree(pages[p], names[p], ck);
            }
            ShowPage(2); DemoResetMode(true);
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(40); }
            DpiCheck.Tree(pageCache, "FiveM cache tab with Reset mode on", ck);
            DemoResetMode(false);
        }

        // for --log-fold-test: the text of the log as it is shown now
        public string TestLogText() { logPanel.TestRebuild(); return logPanel.TestText(); }

        // for --pure-icon-test: the button of the desktop icon
        public string[] TestPureIconButton() { return new string[] { cacheIcon.Text, cacheIcon.Enabled.ToString(), cacheIcon.Parent != null ? cacheIcon.Parent.GetType().Name : "none" }; }

        public string[] TestBudgetButton()
        {
            // the form is not shown in the test, so Visible would say false for every control: ask for the visible flag of the button itself
            object own = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null).Invoke(gfxRecommend, new object[] { 2 });
            return new string[] { own.ToString(), gfxRecommend.Text };
        }
        public string[] TestBudgetHint() { return new string[] { gfxBudgetHint.Text, (string)gfxBudgetHint.Tag }; }
        public Color TestBudgetHintColor() { return gfxBudgetHint.ForeColor; }
        public void TestBudgetClick() { typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(gfxRecommend, new object[] { EventArgs.Empty }); }
        public void TestSetBudget(int v) { gfxBudget.Value = v; }
        public int TestBudgetValue() { return (int)gfxBudget.Value; }

        // reads the two files and shows what is set now (fivem.cfg: vid_budgetScale, gta5_settings.xml: PostFX)
        void RefreshGfx()
        {
            int b = GfxTool.ReadBudget(), px = GfxTool.ReadPostFx();
            gfxBudgetNow.Text = b == -2 ? string.Format(Loc.T("Settings file not found: {0}"), GfxTool.CfgFile)
                : b < 0 ? Loc.T("Not set in the file (FiveM uses its own default)")
                : string.Format(Loc.T("Now in the file: {0} %"), b) + "    (fivem.cfg: vid_budgetScale)";
            gfxPostNow.Text = px == -2 ? string.Format(Loc.T("Settings file not found: {0}"), GfxTool.XmlFile)
                : px < 0 ? Loc.T("Not set in the file (FiveM uses its own default)")
                : string.Format(Loc.T("Now in the file: {0}"), Loc.T(GfxTool.PostFxName(px))) + "    (gta5_settings.xml: PostFX = " + px + ")";
            int ui = GfxTool.ReadFlag(GfxTool.UiFixName), lim = GfxTool.ReadFlag(GfxTool.LimiterName);
            gfxUiNow.Text = ui == -2 ? string.Format(Loc.T("Settings file not found: {0}"), GfxTool.CfgFile)
                : ui < 0 ? Loc.T("Not set in the file (FiveM uses its own default)")
                : string.Format(Loc.T("Now in the file: {0}"), Loc.T(ui == 1 ? "On" : "Off")) + "\r\n(" + GfxTool.UiFixName + " = " + (ui == 1 ? "true" : "false") + ")";
            gfxStutterNow.Text = lim == -2 ? string.Format(Loc.T("Settings file not found: {0}"), GfxTool.CfgFile)
                : lim < 0 ? Loc.T("Not set in the file (FiveM uses its own default)")
                : string.Format(Loc.T("Now in the file: {0}"), Loc.T(lim == 0 ? "On" : "Off")) + "\r\n(" + GfxTool.LimiterName + " = " + (lim == 1 ? "true" : "false") + ")";
            if (!gfxInit)
            {
                gfxInit = true;
                if (b >= 0) gfxBudget.Value = b;
                if (px >= 0 && px < 4) gfxPost.SelectedIndex = px;
                gfxUiFix.Checked = ui == 1;
                gfxStutterFix.Checked = lim == 0;
            }
            UpdateBudgetAdvice();
            AppLog.Debug("graphics settings now: vid_budgetScale=" + b + " PostFX=" + px + " " + GfxTool.UiFixName + "=" + ui + " " + GfxTool.LimiterName + "=" + lim + " (" + GfxTool.Dir + ")");
        }

        void UpdateGfxRunning()
        {
            List<Process> r = CacheTool.Running();
            bool running = r.Count > 0;
            gfxCard.Title = running ? string.Format(Loc.T("FiveM is running ({0} process(es)). Close it before saving."), r.Count) : Loc.T("FiveM is closed. Good.");
            gfxCard.Glyph = running ? Ico.Warning : Ico.Accept;
            gfxCard.GlyphColor = running ? Theme.Bad : Theme.Good;
            gfxCard.Warn = running;
            gfxCard.Invalidate();
            gfxClose.Visible = running;
            foreach (Process p in r) p.Dispose();
        }

        void GfxCloseClick(object s, EventArgs e)
        {
            if (busy) return;
            DialogResult dr = MessageBox.Show(this, Loc.T("Close FiveM now? Anything you have open in the game is lost."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;
            Log("Closing FiveM ...");
            CacheTool.CloseFiveM(Log);
            UpdateGfxRunning();
        }

        void GfxApplyClick(object s, EventArgs e)
        {
            if (busy) return;
            if (CacheTool.Running().Count > 0) { MessageBox.Show(this, Loc.T("FiveM is running. Close it first (the button \"Close FiveM for me\" does that)."), Text); UpdateGfxRunning(); return; }
            int wantB = (int)gfxBudget.Value, wantP = gfxPost.SelectedIndex;
            bool wantUi = gfxUiFix.Checked, wantFix = gfxStutterFix.Checked;
            SetBusy(true);
            JobStart(Loc.T("Saving the settings ..."));
            Thread t = new Thread(delegate()
            {
                AppLog.Context = "Graphics";
                AppLog.BeginGroup();
                bool reported = false;
                try
                {
                    List<string> parts = GfxSaveCore(wantB, wantP, wantUi, wantFix, delegate(string text, double fr) { JobStep(text, fr); });
                    reported = true;
                    if (parts.Count == 0)
                    {
                        Log("Nothing to change: the settings already have these values.");
                        JobDone("info", Loc.T("Nothing to change"), Loc.T("The settings already have these values."));
                    }
                    else
                    {
                        Log("Finished.");
                        JobDone("ok", Loc.T("Settings saved"), string.Join(" ", parts.ToArray()) + " " + Loc.T("FiveM reads it the next time it starts."));
                    }
                }
                catch (Exception ex)
                {
                    Log("Error: " + ex.Message); AppLog.Debug("graphics save error: " + ex);
                    if (!reported) { reported = true; JobDone("bad", Loc.T("Could not save a setting"), ex.Message); }
                }
                finally { AppLog.EndGroup(); }
                BeginInvoke(new Action(delegate { SetBusy(false); gfxInit = false; RefreshGfx(); UpdateGfxRunning(); }));
            });
            t.IsBackground = true; t.Start();
        }

        // The work of "Save the settings", without the window: only what differs from the files now is changed. Returns what was changed (in the window language).
        List<string> GfxSaveCore(int wantB, int wantP, bool wantUi, bool wantFix, Action<string, double> step)
        {
            int curB = GfxTool.ReadBudget(), curP = GfxTool.ReadPostFx();
            bool curUi = GfxTool.UiFixOn(), curFix = GfxTool.StutterFixOn();
            List<string> parts = new List<string>();
            if (curB != wantB)
            {
                string msg = string.Format(Loc.T("Extended Texture Budget is now {0} %."), wantB);
                if (step != null) step(msg, 0.3);
                Log("Extended Texture Budget: " + curB + " -> " + wantB + " (fivem.cfg, vid_budgetScale)");
                GfxTool.SetBudget(wantB, Log);
                parts.Add(msg);
            }
            if (curP != wantP)
            {
                string msg = string.Format(Loc.T("Post FX is now {0}."), Loc.T(GfxTool.PostFxName(wantP)));
                if (step != null) step(msg, 0.7);
                Log("Post FX: " + curP + " -> " + wantP + " (gta5_settings.xml, PostFX)");
                GfxTool.SetPostFx(wantP, Log);
                parts.Add(msg);
            }
            if (curUi != wantUi)
            {
                string msg = Loc.T(wantUi ? "The interface lag fix is now on." : "The interface lag fix is now off.");
                if (step != null) step(msg, 0.8);
                Log("Interface lag fix (NUI in-process GPU): " + (curUi ? "on" : "off") + " -> " + (wantUi ? "on" : "off") + " (fivem.cfg, " + GfxTool.UiFixName + " = " + (wantUi ? "true" : "false") + ")");
                GfxTool.SetUiFix(wantUi, Log);
                parts.Add(msg);
            }
            if (curFix != wantFix)
            {
                string msg = Loc.T(wantFix ? "The stutter fix is now on." : "The stutter fix is now off.");
                if (step != null) step(msg, 0.9);
                Log("Stutter fix (audio frame limiter): " + (curFix ? "on" : "off") + " -> " + (wantFix ? "on" : "off") + " (fivem.cfg, " + GfxTool.LimiterName + " = " + (wantFix ? "false" : "true") + ")");
                GfxTool.SetStutterFix(wantFix, Log);
                parts.Add(msg);
            }
            return parts;
        }

        // for --gfx-ui-test only
        public List<string> TestGfxSave(int b, int p, bool ui, bool fix) { return GfxSaveCore(b, p, ui, fix, null); }
        public bool[] TestGfxSwitches() { return new bool[] { gfxUiFix.Checked, gfxStutterFix.Checked }; }
        public void TestGfxRefresh() { gfxInit = false; RefreshGfx(); }

        // ---------------------------------------------------------------- FiveM cache page
        // One card: an icon, a title, the size, a short text, and a tick box to include it.
        static Card MakeCacheCard(string glyph, string cardTitle, bool warn, out Label size, out Label desc, out CheckBox include)
        {
            Card p = new Card(); p.Title = Loc.T(cardTitle); p.Glyph = glyph; p.Warn = warn; p.Dock = DockStyle.Fill; p.Margin = new Padding(6);
            size = new Label(); size.Text = "..."; size.AutoSize = false; size.Left = 14; size.Top = 46; size.Width = 250; size.Height = 42; size.Font = new Font("Segoe UI", 22f, FontStyle.Bold);
            size.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            p.Controls.Add(size);
            desc = new Label(); desc.Text = ""; desc.AutoSize = false; desc.Left = 16; desc.Top = 94; desc.Width = 250; desc.Height = 70; desc.Tag = "muted"; desc.Font = new Font("Segoe UI", 9f);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            p.Controls.Add(desc);
            include = new CheckBox(); include.Text = Loc.T("Include in the clean"); include.AutoSize = false; include.Left = 14; include.Top = 172; include.Width = 250; include.Height = 28;
            include.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            p.Controls.Add(include); p.Resize += delegate { int cw = Math.Max(Dpi.S(60), p.Width - Dpi.S(32)); foreach (Control cc in p.Controls) if (cc is Label || cc is CheckBox) cc.Width = cw; };
            return p;
        }

        void BuildCachePage()
        {
            Card cWhich = NewCard("Which FiveM", Ico.Folder, 12, 108); pageCache.Controls.Add(cWhich);
            int rw = FitW("Search again", 150); int instW = Math.Max(100, Dpi.TextW(Loc.T("FiveM install:"), new Font("Segoe UI", 9f)) + 14); cWhich.Controls.Add(Small("FiveM install:", 16, 52, instW, 22, null));
            cacheRoots = new DropBox(); cacheRoots.Left = 16 + instW + 4; cacheRoots.Top = 48; cacheRoots.Width = CardW - (16 + instW + 4) - 16 - rw - 10;
            cacheRoots.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; cacheRoots.SelectedIndexChanged += delegate { RefreshCacheSizes(); }; cWhich.Controls.Add(cacheRoots);
            cacheRefresh = Btn(Ico.Search, "Search again", CardW - 16 - rw, 44, rw, 34); cacheRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right; cacheRefresh.Click += delegate { Rescan(null); }; cWhich.Controls.Add(cacheRefresh);
            cacheState = Small("", 16, 80, 560, 22, "good"); cWhich.Controls.Add(cacheState);
            int cw2 = FitW("Close FiveM for me", 210); cacheClose = Btn(Ico.Cancel, "Close FiveM for me", CardW - 16 - cw2, 74, cw2, 30); cacheClose.Anchor = AnchorStyles.Top | AnchorStyles.Right; cacheClose.Visible = false; cacheClose.Click += CacheCloseClick; cWhich.Controls.Add(cacheClose);

            TableLayoutPanel t = new TableLayoutPanel(); t.Left = 16; t.Top = 130; t.Width = CardW; t.Height = 216; t.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            t.ColumnCount = 3; t.RowCount = 1;
            for (int i = 0; i < 3; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            t.Controls.Add(MakeCacheCard(Ico.Folder, "Temporary files", false, out sizeTemp, out descTemp, out cbTemp), 0, 0);
            t.Controls.Add(MakeCacheCard(Ico.Document, "Logs and crash reports", false, out sizeLogs, out descLogs, out cbLogs), 1, 0);
            t.Controls.Add(MakeCacheCard(Ico.Warning, "Login and settings (deep clean)", true, out sizeDeep, out descDeep, out cbDeep), 2, 0);
            cbTemp.Checked = true; cbLogs.Checked = true; cbDeep.Checked = false;
            cbTemp.CheckedChanged += delegate { UpdateCacheLabels(); };
            cbLogs.CheckedChanged += delegate { UpdateCacheLabels(); };
            cbDeep.CheckedChanged += DeepChanged;
            pageCache.Controls.Add(t);

            Card cGo = NewCard("", "", 358, 162); pageCache.Controls.Add(cGo);
            cacheTotal = new Label(); cacheTotal.Left = 16; cacheTotal.Top = 12; cacheTotal.Width = CardW - 32; cacheTotal.Height = 28; cacheTotal.Font = new Font("Segoe UI", 11f, FontStyle.Bold); cacheTotal.Text = "..."; cacheTotal.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; cGo.Controls.Add(cacheTotal);
            cacheClean = Btn(Ico.Delete, "Clean cache", 16, 54, 230, 48); cacheClean.Tag = "primary"; cacheClean.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold); cacheClean.Click += CacheCleanClick; cGo.Controls.Add(cacheClean);
            cacheStart = Btn(Ico.Play, "Start FiveM", 256, 54, 200, 48); cacheStart.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold); cacheStart.Enabled = false; cacheStart.Click += StartFiveMClick; cGo.Controls.Add(cacheStart);
            cbPure = new CheckBox(); cbPure.Text = Loc.T("Pure mode (-pure_1)"); cbPure.Left = 476; cbPure.Top = 54; cbPure.Width = 300; cbPure.Height = 26; cGo.Controls.Add(cbPure);
            cGo.Controls.Add(Small("ignores local file changes; for troubleshooting mods", 498, 80, 420, 22, "muted"));
            // a second row: an icon on the desktop that starts FiveM in pure mode
            int iw = FitW("Desktop icon: pure mode", 260);
            cacheIcon = Btn(Ico.Link, "Desktop icon: pure mode", 16, 112, iw, 36); cacheIcon.Enabled = false; cacheIcon.Click += PureIconClick;
            tip.SetToolTip(cacheIcon, Loc.T("Makes an icon on your desktop that starts FiveM with -pure_1 (FiveM then ignores local file changes; for troubleshooting mods)."));
            cGo.Controls.Add(cacheIcon);
            cGo.Controls.Add(Small("Makes an icon on your desktop that starts FiveM in pure mode.", 16 + iw + 14, 118, 560, 22, "muted"));

            // the most extreme clean: a block of its own, and the button asks again in a big warning window
            cReset = NewCard("Reset (the most extreme clean)", Ico.Warning, 532, 88); pageCache.Controls.Add(cReset);
            int wipeW = FitW("Reset FiveM ...", 230);
            cacheWipe = Btn(Ico.Delete, "Reset FiveM ...", CardW - 16 - wipeW, 54, wipeW, 50); cacheWipe.Tag = "danger"; cacheWipe.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            cacheWipe.Anchor = AnchorStyles.Top | AnchorStyles.Right; cacheWipe.Click += CacheResetClick; cReset.Controls.Add(cacheWipe);
            wipeBig = Small("You lose your key binds and your graphics settings!", 16, 44, CardW - 32 - wipeW - 20, 28, "bad"); wipeBig.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold); cReset.Controls.Add(wipeBig);
            cReset.Controls.Add(wipeDesc = Small("Cleans everything: temporary files, logs, login and saved data, and also your settings folder (key binds, graphics, saved server data). FiveM starts like a new install. A backup of the settings is saved first.", 16, 74, CardW - 32 - wipeW - 20, 52, "muted"));

            // the switch: off = a quiet line, on = the red block with the button. It is never saved: every start, and every visit to another tab, puts it off again.
            resetSwitch = new ToggleSwitch(); resetSwitch.Left = CardW - 16 - 48; resetSwitch.Top = 9; resetSwitch.Anchor = AnchorStyles.Top | AnchorStyles.Right; cReset.Controls.Add(resetSwitch);
            Label resetModeText = new Label(); resetModeText.Text = Loc.T("Reset mode"); resetModeText.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold); resetModeText.AutoSize = false; resetModeText.TextAlign = ContentAlignment.MiddleRight;
            resetModeText.Width = 200; resetModeText.Height = 26; resetModeText.Left = CardW - 16 - 48 - 10 - 200; resetModeText.Top = 9; resetModeText.Anchor = AnchorStyles.Top | AnchorStyles.Right; cReset.Controls.Add(resetModeText);
            resetOffLine = Small("Off. The normal clean and the deep clean never touch your key binds or your graphics settings.", 16, 44, CardW - 32, 34, "muted"); cReset.Controls.Add(resetOffLine);
            wipeBig.Visible = false; wipeDesc.Visible = false; cacheWipe.Visible = false;
            resetSwitch.CheckedChanged += ResetModeChanged;
        }

        // the switch of the Reset block: off = small and calm, on = the red block with the Reset button. The switch itself deletes nothing.
        void ResetModeChanged(object s, EventArgs e)
        {
            bool on = resetSwitch.Checked;
            cReset.Warn = on; cReset.Invalidate();
            resetOffLine.Visible = !on;
            wipeBig.Visible = on; wipeDesc.Visible = on; cacheWipe.Visible = on;
            int from = cReset.Height, to = on ? S(132) : S(88);
            int gen = ++resetGen;
            if (!IsHandleCreated || !pageCache.Visible) { cReset.Height = to; LayoutFooter(false); return; }
            Fx.Run(200, delegate(float p)
            {
                if (gen != resetGen) return;
                cReset.Height = from + (int)Math.Round((to - from) * Fx.EaseOut(p));
                LayoutFooter(false);
            }, delegate { if (gen == resetGen) { cReset.Height = to; LayoutFooter(false); } });
        }

        public void DemoResetMode(bool on) { resetSwitch.Checked = on; }   // for the test pictures

        string CurrentRoot() { return cacheRoots.SelectedItem as string; }

        void FillRoots(List<string> roots)
        {
            fiveRoots = new List<string>(roots);
            string keep = CurrentRoot();
            cacheRoots.Items.Clear(); cacheRoots.SelectedIndex = -1;
            foreach (string r in fiveRoots) cacheRoots.Items.Add(r);
            if (fiveRoots.Count > 0) { int i = keep != null ? fiveRoots.IndexOf(keep) : -1; cacheRoots.SelectedIndex = i >= 0 ? i : 0; }
            else RefreshCacheSizes();
        }

        void UpdateRunning()
        {
            List<Process> r = CacheTool.Running();
            bool running = r.Count > 0;
            cacheState.Text = Ico.Dot + "  " + (running ? string.Format(Loc.T("FiveM is running ({0} process(es)). Close it before cleaning."), r.Count) : Loc.T("FiveM is closed. Good."));
            cacheState.Tag = running ? "bad" : "good";
            cacheState.ForeColor = running ? Theme.Bad : Theme.Good;
            cacheClose.Visible = running;
            string root = CurrentRoot();
            canStart = !running && root != null && CacheTool.FiveMExe(root) != null;
            cacheStart.Enabled = !busy && canStart;
            cacheIcon.Enabled = !busy && root != null && CacheTool.FiveMExe(root) != null;
            foreach (Process p in r) p.Dispose();
        }

        // Measures the folders on a background thread and says in the log what was found.
        void RefreshCacheSizes()
        {
            string root = CurrentRoot();
            if (root == null || !CacheTool.IsFiveMApp(root))
            {
                cacheItems = new List<CacheItem>();
                cacheTotal.Text = Loc.T("No FiveM folder found. Use \"Search again\", or pick a folder on the frame cap tab.");
                sizeTemp.Text = "-"; sizeLogs.Text = "-"; sizeDeep.Text = "-";
                descTemp.Text = ""; descLogs.Text = ""; descDeep.Text = "";
                canStart = false; cacheStart.Enabled = false; cacheIcon.Enabled = false;
                return;
            }
            // not on this tab: do not touch the disk and do not write in the log. The tab measures when it is opened.
            if (currentPage != 2) { cacheMeasuredRoot = null; return; }
            cacheItems = CacheTool.Items(root);
            cacheTotal.Text = Loc.T("Measuring the folders ...");
            sizeTemp.Text = "..."; sizeLogs.Text = "..."; sizeDeep.Text = "...";
            List<CacheItem> items = cacheItems;
            Thread t = new Thread(delegate()
            {
                AppLog.Context = "FiveM cache";
                AppLog.BeginGroup();
                try
                {
                    Log("Looking at the FiveM cache in " + root + " ...");
                    foreach (CacheItem it in items)
                    {
                        long sum = 0;
                        it.PathBytes.Clear();
                        foreach (string p in it.Paths)
                        {
                            long b = CacheTool.SizeOf(p);
                            it.PathBytes.Add(b); sum += b;
                            Log("  " + Path.GetFileName(p) + ": " + (Directory.Exists(p) ? CacheTool.Size(b) : "not there"));
                        }
                        it.Bytes = sum;
                    }
                    Log("Found: temporary files " + CacheTool.Size(items[0].Bytes) + ", logs and crash reports " + CacheTool.Size(items[1].Bytes) + ", login and settings " + CacheTool.Size(items[2].Bytes) + " (only cleared by the deep clean).");
                }
                catch (Exception ex) { Log("Could not measure the cache: " + ex.Message); AppLog.Debug("cache measure error: " + ex); }
                finally { AppLog.EndGroup(); }
                cacheMeasuredRoot = root; cacheMeasuredAt = DateTime.UtcNow;
                try { BeginInvoke(new Action(UpdateCacheLabels)); } catch (Exception) { }
            });
            t.IsBackground = true; t.Start();
        }

        CacheItem Item(string key) { return cacheItems.Find(delegate(CacheItem x) { return x.Key == key; }); }

        void UpdateCacheLabels()
        {
            CacheItem t = Item("temp"), l = Item("logs"), d = Item("deep");
            if (t == null || l == null || d == null) return;
            sizeTemp.Text = CacheTool.Size(t.Bytes); descTemp.Text = t.Detail;
            sizeLogs.Text = CacheTool.Size(l.Bytes); descLogs.Text = l.Detail;
            sizeDeep.Text = CacheTool.Size(d.Bytes); descDeep.Text = d.Detail;
            long total = (cbTemp.Checked ? t.Bytes : 0) + (cbLogs.Checked ? l.Bytes : 0) + (cbDeep.Checked ? d.Bytes : 0);
            cacheTotal.Text = string.Format(Loc.T("Will clean about {0} (the cards you included)."), CacheTool.Size(total));
        }

        void DeepChanged(object s, EventArgs e)
        {
            if (inDeepChange) return;
            if (cbDeep.Checked)
            {
                DialogResult dr = MessageBox.Show(this,
                    Loc.T("Deep clean also removes your saved FiveM login and other saved data, as well as the cache.\r\n\r\nYou will have to log in to FiveM again. Your key binds and graphics settings are not touched.\r\n\r\nUse it only when the normal clean did not help. Include it anyway?"),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) { inDeepChange = true; cbDeep.Checked = false; inDeepChange = false; }
            }
            UpdateCacheLabels();
        }

        void CacheCloseClick(object s, EventArgs e)
        {
            if (busy) return;
            DialogResult dr = MessageBox.Show(this, Loc.T("Close FiveM now? Anything you have open in the game is lost."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;
            Log("Closing FiveM ...");
            CacheTool.CloseFiveM(Log);
            UpdateRunning();
        }

        void CacheCleanClick(object s, EventArgs e)
        {
            if (busy) return;
            string root = CurrentRoot();
            if (root == null || !CacheTool.IsFiveMApp(root)) { MessageBox.Show(this, Loc.T("No FiveM folder is selected."), Text); return; }
            List<CacheItem> chosen = new List<CacheItem>();
            foreach (CacheItem it in cacheItems)
                if ((it.Key == "temp" && cbTemp.Checked) || (it.Key == "logs" && cbLogs.Checked) || (it.Key == "deep" && cbDeep.Checked)) chosen.Add(it);
            if (chosen.Count == 0) { MessageBox.Show(this, Loc.T("Include at least one card."), Text); return; }
            if (CacheTool.Running().Count > 0) { MessageBox.Show(this, Loc.T("FiveM is running. Close it first (the button \"Close FiveM for me\" does that)."), Text); UpdateRunning(); return; }
            long total = 0; List<string> names = new List<string>();
            foreach (CacheItem it in chosen) { total += it.Bytes; names.Add(it.Title); }
            DialogResult dr = MessageBox.Show(this,
                string.Format(Loc.T("Clean these (about {0})?\r\n\r\n - {1}\r\n\r\nFolder: {2}"), CacheTool.Size(total), string.Join("\r\n - ", names.ToArray()), root),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) { Log("Cleaning cancelled."); return; }
            SetBusy(true);
            Thread t = new Thread(delegate()
            {
                AppLog.Context = "FiveM cache";
                AppLog.BeginGroup();
                bool reported = false;
                JobStart(Loc.T("Cleaning the FiveM cache ..."));
                try
                {
                    Log("Cleaning the FiveM cache in " + root + " ...");
                    bool ok = CacheTool.Clear(root, chosen, Log, delegate(string nm, double fr) { JobStep(string.Format(Loc.T("Removing {0} ..."), nm), fr); });
                    Log(ok ? "Done. About " + CacheTool.Size(total) + " cleaned." : "Some folders could not be cleaned. Open the full log for details.");
                    reported = true;
                    if (ok) JobDone("ok", Loc.T("Cache cleaned"), string.Format(Loc.T("About {0} was removed."), CacheTool.Size(total)));
                    else JobDone("bad", Loc.T("Some folders were not cleaned"), Loc.T("Open the full log for details."));
                }
                catch (Exception ex)
                {
                    Log("Error: " + ex.Message); AppLog.Debug("cache clean error: " + ex);
                    if (!reported) { reported = true; JobDone("bad", Loc.T("It did not finish"), ex.Message); }
                }
                finally { AppLog.EndGroup(); }
                BeginInvoke(new Action(delegate { SetBusy(false); RefreshCacheSizes(); UpdateRunning(); }));
            });
            t.IsBackground = true; t.Start();
        }

        void CacheResetClick(object s, EventArgs e)
        {
            if (busy) return;
            string root = CurrentRoot();
            if (root == null || !CacheTool.IsFiveMApp(root)) { MessageBox.Show(this, Loc.T("No FiveM folder is selected."), Text); return; }
            if (CacheTool.Running().Count > 0) { MessageBox.Show(this, Loc.T("FiveM is running. Close it first (the button \"Close FiveM for me\" does that)."), Text); UpdateRunning(); return; }
            int binds = CacheTool.CountBinds();
            using (ResetWarningForm w = new ResetWarningForm(binds))
                if (w.ShowDialog(this) != DialogResult.OK) { Log("Reset cancelled."); return; }
            if (CacheTool.Running().Count > 0) { MessageBox.Show(this, Loc.T("FiveM is running. Close it first (the button \"Close FiveM for me\" does that)."), Text); UpdateRunning(); return; }
            RunReset(root);
        }

        public bool IsBusy { get { return busy; } }
        public void TestReset(string root) { RunReset(root); }   // for the test switch: the same work, without the question

        // the work of the Reset, on a background thread
        void RunReset(string root)
        {
            List<CacheItem> items = CacheTool.Items(root);
            if (root == cacheMeasuredRoot)   // the sizes measured earlier make the progress bar move with the work
                foreach (CacheItem it in items)
                {
                    CacheItem known = Item(it.Key);
                    if (known != null) { it.PathBytes.AddRange(known.PathBytes); it.Bytes = known.Bytes; }
                }
            SetBusy(true);
            Thread t = new Thread(delegate()
            {
                AppLog.Context = "FiveM cache";
                AppLog.BeginGroup();
                bool reported = false;
                JobStart(Loc.T("Resetting FiveM ..."));
                try
                {
                    Log("Resetting FiveM (the most extreme clean) in " + root + " ...");
                    int rc = CacheTool.Reset(root, items, Log, delegate(string nm, double fr)
                    {
                        JobStep(nm.Length == 0 ? Loc.T("Backing up the settings ...") : string.Format(Loc.T("Removing {0} ..."), nm), fr);
                    });
                    reported = true;
                    if (rc == 0) { Log("Done. FiveM was reset. It starts like a new install."); JobDone("ok", Loc.T("FiveM was reset"), Loc.T("It starts like a new install. A backup of your settings is in the backups folder.")); }
                    else if (rc == 1) { Log("Reset stopped. Nothing was deleted."); JobDone("bad", Loc.T("Reset stopped"), Loc.T("The backup copy could not be made, so nothing was deleted.")); }
                    else { Log("Some parts could not be removed. Open the full log for details."); JobDone("bad", Loc.T("Some folders were not cleaned"), Loc.T("Open the full log for details.")); }
                }
                catch (Exception ex)
                {
                    Log("Error: " + ex.Message); AppLog.Debug("cache reset error: " + ex);
                    if (!reported) { reported = true; JobDone("bad", Loc.T("It did not finish"), ex.Message); }
                }
                finally { AppLog.EndGroup(); }
                BeginInvoke(new Action(delegate { SetBusy(false); RefreshCacheSizes(); UpdateRunning(); gfxInit = false; RefreshGfx(); resetSwitch.Checked = false; }));
            });
            t.IsBackground = true; t.Start();
        }

        void StartFiveMClick(object s, EventArgs e)
        {
            string root = CurrentRoot();
            string exe = root == null ? null : CacheTool.FiveMExe(root);
            if (exe == null) { MessageBox.Show(this, Loc.T("FiveM.exe was not found next to the FiveM.app folder."), Text); return; }
            if (CacheTool.Running().Count > 0) { MessageBox.Show(this, Loc.T("FiveM is already running."), Text); UpdateRunning(); return; }
            try
            {
                Launch.Normal(exe, cbPure.Checked ? "-pure_1" : "", Path.GetDirectoryName(exe));
                Log("Started FiveM" + (cbPure.Checked ? " in pure mode (-pure_1)" : "") + ": " + exe);
            }
            catch (Exception ex) { Log("Could not start FiveM: " + ex.Message); AppLog.Debug("start FiveM error: " + ex); }
            UpdateRunning();
        }

        // the icon on the desktop that starts FiveM in pure mode
        void PureIconClick(object s, EventArgs e)
        {
            string root = CurrentRoot();
            string exe = root == null ? null : CacheTool.FiveMExe(root);
            if (exe == null) { MessageBox.Show(this, Loc.T("FiveM.exe was not found next to the FiveM.app folder."), Text); return; }
            string what;
            string problem = PureIcon.Make(exe, out what);
            if (problem != null)
            {
                Log("Could not make the desktop icon: " + problem); AppLog.Debug("desktop icon error: " + problem);
                JobDone("bad", Loc.T("The desktop icon was not made"), problem);
                return;
            }
            string lnk = PureIcon.PathOf();
            Log((what == "already" ? "The desktop icon was already there: " : what == "updated" ? "Desktop icon updated: " : "Desktop icon made: ") + lnk + "   (starts " + exe + " with " + PureIcon.Arguments + ")");
            JobDone(what == "already" ? "info" : "ok", Loc.T(what == "already" ? "The desktop icon is already there" : what == "updated" ? "The desktop icon was updated" : "Desktop icon made"), lnk);
        }

        // ---------------------------------------------------------------- shared
        void Log(string s) { AppLog.Add(s); }

        // ---------------------------------------------------------------- the activity card, row colours, the language menu
        void Ui(Action a) { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch (Exception) { } }

        // These three can be called from any thread. The text is already in the window language.
        void JobStart(string title) { Ui(delegate { activity.Start(title); }); }
        void JobStep(string text, double frac) { Ui(delegate { activity.Step(text, frac); }); }
        void JobDone(string kind, string title, string detail) { Ui(delegate { activity.Finish(kind, title, detail); }); }

        // state: 0 working, 1 good, -1 not good
        void MarkRow(Build b, string text, int state)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag != b) continue;
                row.Cells[2].Value = text;
                row.Cells[2].Style.ForeColor = state == 0 ? Theme.AccentText : state > 0 ? Theme.Good : Theme.Bad;
                if (state != 0) Flash(row, state > 0);
                break;
            }
        }

        // The row glows green or red for a moment, then fades back.
        void Flash(DataGridViewRow row, bool good)
        {
            Color from = Fx.Mix(Theme.Bg2, good ? Theme.Good : Theme.Bad, 0.30f);
            Fx.Run(1600, delegate(float p)
            {
                if (row.Index < 0) return;
                row.DefaultCellStyle.BackColor = Fx.Mix(from, Theme.Bg2, Fx.EaseOut(p));
            }, delegate { if (row.Index >= 0) row.DefaultCellStyle.BackColor = Color.Empty; });
        }

        // for the test pictures: three cards, as on a laptop with two graphics chips and one more
        public void DemoGpus()
        {
            gpus = new List<Gpu>();
            Gpu a = new Gpu(); a.Vendor = "AMD"; a.Name = "AMD Radeon(TM) 680M"; a.Driver = "32.0.12033.1030"; a.DriverDate = "2-5-2025"; a.Kind = "integrated"; a.Vram = 512L * 1024 * 1024; gpus.Add(a);
            Gpu b = new Gpu(); b.Vendor = "NVIDIA"; b.Name = "NVIDIA GeForce RTX 4060 Laptop GPU"; b.Driver = "32.0.15.8115"; b.DriverDate = "14-10-2025"; b.Kind = "dedicated"; b.Vram = 8L * 1024 * 1024 * 1024; gpus.Add(b);
            Gpu c = new Gpu(); c.Vendor = "Intel"; c.Name = "Intel(R) UHD Graphics"; c.Driver = "31.0.101.2135"; c.DriverDate = "1-3-2024"; c.Kind = "integrated"; c.Vram = 128L * 1024 * 1024; gpus.Add(c);
            ShowGpus("NVIDIA mode: works with the NVIDIA driver.");
        }

        // for the test pictures
        public void DemoActivity(string kind)
        {
            if (kind == "run") { activity.Start(Loc.T("Applying the frame cap ...")); activity.Step("FiveM_b3258_GTAProcess.exe (1 of 2): " + Loc.T("Closing Radeon Software ..."), 0.42); }
            else if (kind == "ok") activity.Finish("ok", string.Format(Loc.T("Frame cap set to {0}"), 140), string.Format(Loc.T("Changed {0} build(s), {1} already had it. Radeon Software was restarted."), 1, 1));
            else if (kind == "info") activity.Finish("info", Loc.T("Nothing to change"), string.Format(Loc.T("{0} build(s) already had the cap set to {1}. Radeon Software was left alone."), 2, 140));
            else activity.Finish("bad", Loc.T("Not everything worked"), "FiveM_b3570_GTAProcess.exe: " + Loc.TS("Could not confirm which profile belongs to this build. Nothing was changed."));
        }

        // "Reset the list": the builds that are not on disk now leave the list, the program searches again and adds what it finds
        void ResetClick(object s, EventArgs e)
        {
            if (busy) return;
            int n = 0;
            foreach (Build b in builds) if (b.Missing) n++;
            Finder.ForgetMissing(builds);
            resetRemoved = n;
            Log("Reset the list: " + n + " build(s) that are not on disk now were removed. Searching again ...");
            Rescan(null);
        }

        // Click the program icon (top left) to pick the language. The program restarts in that language, on the same tab.
        void ShowLanguageMenu() { BuildLanguageMenu().Show(IconBox, new Point(0, IconBox.Height + 6)); }

        // for the test pictures: draws the menu into a file
        public void SnapLanguageMenu(string path)
        {
            ContextMenuStrip m = BuildLanguageMenu();
            m.Show(IconBox, new Point(0, IconBox.Height + 6));
            for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(50); }
            using (Bitmap b = new Bitmap(m.Width, m.Height))
            {
                m.DrawToBitmap(b, new Rectangle(0, 0, m.Width, m.Height));
                b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            m.Close();
        }

        ContextMenuStrip BuildLanguageMenu()
        {
            ContextMenuStrip m = ThemeMenu.Create();
            string cur = Settings.Get("language", "auto").ToLowerInvariant();
            string[] codes = new string[] { "auto", "en", "nl", "de", "fr", "es" };
            string[] names = new string[] { Loc.T("Follow the Windows setting"), "English", "Nederlands", "Deutsch", "Français", "Español" };
            for (int i = 0; i < codes.Length; i++)
            {
                string code = codes[i];
                ToolStripMenuItem it = ThemeMenu.Item(names[i], cur == code || (cur.Length == 0 && code == "auto"));
                it.Enabled = !busy;
                it.Click += delegate { ChangeLanguage(code); };
                m.Items.Add(it);
                if (i == 0) m.Items.Add(new ToolStripSeparator());
            }
            return m;
        }

        // Restarts this program with administrator rights (Windows asks once). It opens on the same tab.
        void RestartAsAdmin()
        {
            if (busy) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--page " + currentPage);
                psi.Verb = "runas"; psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) { Log("You cancelled the Windows permission window. Nothing was changed."); activity.Finish("info", Loc.T("Cancelled"), Loc.T("You closed the Windows permission window. Nothing was changed.")); }
                else Log("Could not restart as administrator: " + ex.Message);
                return;
            }
            Log("Restarting as administrator ...");
            Close();
        }

        void ChangeLanguage(string code)
        {
            if (busy) return;
            if (code == Settings.Get("language", "auto").ToLowerInvariant()) return;
            Settings.Set("language", code);
            Log("Language set to " + code + ". Restarting the program.");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--page " + currentPage);
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Exception ex) { Log("Could not restart the program: " + ex.Message); return; }
            Close();
        }


        void ShowLogClick()
        {
            if (logForm == null || logForm.IsDisposed) logForm = new LogForm();
            logForm.Show(this);
            logForm.BringToFront();
        }

        void SetBusy(bool b)
        {
            busy = b;
            resetBtn.Enabled = !b; gfxApply.Enabled = !b; gfxClose.Enabled = !b; gfxHighest.Enabled = !b; gfxNormal.Enabled = !b; applyBtn.Enabled = !b && (mode == "amd" || mode == "nvidia"); undoBtn.Enabled = !b && (mode == "amd" || mode == "nvidia"); rescanBtn.Enabled = !b; tidyBtn.Enabled = !b; browseBtn.Enabled = !b; capBox.Enabled = !b; whyBtn.Enabled = true;
            dnsFlush.Enabled = !b; dnsSet.Enabled = !b; dnsUndo.Enabled = !b; dnsRefresh.Enabled = !b; cbExtraFlush.Enabled = !b;
            cacheClean.Enabled = !b; cacheWipe.Enabled = !b; resetSwitch.Enabled = !b; gpuSwitch.Enabled = !b; cacheRefresh.Enabled = !b; cacheClose.Enabled = !b; cacheRoots.Enabled = !b; cacheStart.Enabled = !b && canStart; cacheIcon.Enabled = !b && CurrentRoot() != null && CacheTool.FiveMExe(CurrentRoot()) != null;
        }
    }

    // A pretend NVIDIA driver for the self test: no NVIDIA card needed, nothing real is touched.
    class NvFakeDrs : INvDrs
    {
        public class Prof { public string Name; public List<string> Apps = new List<string>(); public int Cap = -1; public int Other = 0; }   // Other = how many settings it holds besides the frame limit
        public static List<Prof> Committed = new List<Prof>();
        public static int GlobalCap = -1;
        public static int FailOn = 0;   // 1 = open fails, 2 = writing fails, 3 = saving fails, 4 = saving says OK but keeps nothing, 5 = saving says access denied
        List<Prof> work;

        static List<Prof> Clone(List<Prof> src)
        {
            List<Prof> r = new List<Prof>();
            foreach (Prof p in src) { Prof q = new Prof(); q.Name = p.Name; q.Cap = p.Cap; q.Other = p.Other; q.Apps.AddRange(p.Apps); r.Add(q); }
            return r;
        }

        public static string Dump()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Prof p in Committed) sb.Append(p.Name + "|" + string.Join(",", p.Apps.ToArray()) + "|" + p.Cap + ";");
            return sb.ToString();
        }

        Prof At(IntPtr h) { return work[(int)h - 1]; }
        public int Open() { if (FailOn == 1) return NvApi.Error; work = Clone(Committed); return 0; }
        // like the real driver: given a FULL path, a program stored with that same full path wins (slashes and capitals do not matter); then the bare file name is tried.
        // Given only a file name, only programs stored as a bare file name match.
        public int FindApp(string exe, out IntPtr profile)
        {
            string full = exe.Replace('\\', '/').ToLowerInvariant();
            bool hasPath = full.IndexOf('/') >= 0;
            string bare = full.Substring(full.LastIndexOf('/') + 1);
            if (hasPath)
                for (int i = 0; i < work.Count; i++)
                    if (work[i].Apps.Exists(delegate(string a) { return a.Replace('\\', '/').ToLowerInvariant() == full; })) { profile = (IntPtr)(i + 1); return 0; }
            for (int i = 0; i < work.Count; i++)
                if (work[i].Apps.Exists(delegate(string a) { return a.IndexOf('/') < 0 && a.IndexOf('\\') < 0 && a.ToLowerInvariant() == bare; })) { profile = (IntPtr)(i + 1); return 0; }
            profile = IntPtr.Zero; return NvApi.ExeNotFound;
        }
        public string NameOf(IntPtr profile) { return At(profile).Name; }
        public int FindProfile(string name, out IntPtr profile)
        {
            for (int i = 0; i < work.Count; i++) if (work[i].Name == name) { profile = (IntPtr)(i + 1); return 0; }
            profile = IntPtr.Zero; return NvApi.ProfileNotFound;
        }
        public int CreateProfile(string name, out IntPtr profile) { Prof p = new Prof(); p.Name = name; work.Add(p); profile = (IntPtr)work.Count; return 0; }
        public int AddApp(IntPtr profile, string exe) { At(profile).Apps.Add(exe); return 0; }
        public int GetCap(IntPtr profile, out int fps, out bool own) { Prof p = At(profile); own = p.Cap > 0; fps = p.Cap > 0 ? p.Cap : GlobalCap; return 0; }
        public int SetCap(IntPtr profile, int fps) { if (FailOn == 2) return NvApi.Error; At(profile).Cap = fps; return 0; }
        public int DeleteCap(IntPtr profile) { if (FailOn == 2) return NvApi.Error; At(profile).Cap = -1; return 0; }
        public int Save()
        {
            if (FailOn == 3) return NvApi.Error;
            if (FailOn == 5) return NvApi.AccessDenied;
            if (FailOn == 4) return 0;
            Committed = Clone(work); return 0;
        }
        public int ProfileCount(out int count) { count = work.Count; return 0; }
        public int OurProfiles(string prefix, List<NvProfInfo> found)
        {
            foreach (Prof p in work)
                if (p.Name != null && p.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    NvProfInfo n = new NvProfInfo(); n.Name = p.Name; n.Settings = p.Other + (p.Cap > 0 ? 1 : 0); n.AppCount = p.Apps.Count; n.Apps.AddRange(p.Apps);
                    found.Add(n);
                }
            return 0;
        }
        public int DeleteProfile(string name)
        {
            if (FailOn == 2) return NvApi.Error;
            int i = work.FindIndex(delegate(Prof p) { return p.Name == name; });
            if (i < 0) return NvApi.ProfileNotFound;
            work.RemoveAt(i);
            return 0;
        }

        public int DumpSettings(string exeName, Action<string> log)
        {
            IntPtr p;
            if (FindApp(exeName, out p) != 0) { log(exeName + ": is in no driver profile."); return 0; }
            Prof q = At(p);
            log(exeName + " => profile \"" + q.Name + "\": " + (q.Other + (q.Cap > 0 ? 1 : 0)) + " setting(s)");
            if (q.Cap > 0) log("    id 0x10835002 \"Frame Rate Limiter\" | DWORD 0x" + q.Cap.ToString("X8") + " (" + q.Cap + ") | location this profile");
            return 0;
        }

        public int List(Action<string> log, bool all)
        {
            log("profiles in the driver database: " + work.Count);
            foreach (Prof p in work) log("[user made]  \"" + p.Name + "\" | programs " + p.Apps.Count + ": " + string.Join(", ", p.Apps.ToArray()) + " | frame limit " + (p.Cap > 0 ? p.Cap.ToString() : "none of its own"));
            return 0;
        }
        public void Dispose() { }
    }

    // ---------------------------------------------------------------- the language of the window text
    // English is the default. When Windows is set to Dutch, German, French or Spanish the window text follows it.
    // To force a language, put  language=nl  (or de, fr, es, en)  in settings.txt. The log and the log file stay in English on purpose.
    static class Loc
    {
        static string lang = "en";
        static Dictionary<string, string[]> map;

        public static string Lang { get { return lang; } }

        public static void Init()
        {
            string s = Settings.Get("language", "auto");
            if (s == "auto" || s.Length == 0) s = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            Force(s);
            AppLog.Debug("language: " + lang + " (Windows says " + System.Globalization.CultureInfo.CurrentUICulture.Name + ")");
        }

        public static void Force(string s)
        {
            s = (s == null ? "en" : s.ToLowerInvariant());
            lang = (s == "nl" || s == "de" || s == "fr" || s == "es") ? s : "en";
        }

        static int Col { get { return lang == "nl" ? 1 : lang == "de" ? 2 : lang == "fr" ? 3 : lang == "es" ? 4 : 0; } }

        // The English text is the key. A text without a translation stays English.
        public static string T(string en)
        {
            if (en == null || lang == "en") return en;
            if (map == null)
            {
                map = new Dictionary<string, string[]>();
                foreach (string[] r in Rows) map[r[0]] = r;
            }
            string[] row;
            if (map.TryGetValue(en, out row) && row.Length > Col && !string.IsNullOrEmpty(row[Col])) return row[Col];
            return en;
        }

        // Status texts that carry numbers.
        public static string TS(string s)
        {
            if (lang == "en" || string.IsNullOrEmpty(s)) return s;
            Match m = Regex.Match(s, @"^Set to (\d+)\.$");
            if (m.Success) return string.Format(T("Set to {0}."), m.Groups[1].Value);
            m = Regex.Match(s, @"^Already set to (\d+)\.$");
            if (m.Success) return string.Format(T("Already set to {0}."), m.Groups[1].Value);
            m = Regex.Match(s, @"^Radeon Chill (on|off), cap now (-?\d+) / (-?\d+)$");
            if (m.Success) return string.Format(T("Radeon Chill " + m.Groups[1].Value + ", cap now {0} / {1}"), m.Groups[2].Value, m.Groups[3].Value);
            m = Regex.Match(s, @"^Cap put back to (-?\d+) / (-?\d+)\.$");
            if (m.Success) return string.Format(T("Cap put back to {0} / {1}."), m.Groups[1].Value, m.Groups[2].Value);
            m = Regex.Match(s, @"^NVIDIA: frame limit (\d+) FPS$");
            if (m.Success) return string.Format(T("NVIDIA: frame limit {0} FPS"), m.Groups[1].Value);
            return T(s);
        }

        // English, Dutch, German, French, Spanish
        static readonly string[][] Rows = new string[][] {
            new string[] { "Frame cap", "FPS-limiet", "FPS-Limit", "Limite de FPS", "Límite FPS" },
            new string[] { "DNS tools", "DNS-tools", "DNS-Werkzeuge", "Outils DNS", "DNS" },
            new string[] { "FiveM cache", "FiveM-cache", "FiveM-Cache", "Cache FiveM", "Caché FiveM" },
            new string[] { "Follow the Windows setting", "Windows-instelling volgen", "Windows-Einstellung folgen", "Suivre le réglage de Windows", "Seguir el ajuste de Windows" },
            new string[] { "Light", "Licht", "Hell", "Clair", "Claro" },
            new string[] { "Dark", "Donker", "Dunkel", "Sombre", "Oscuro" },
            new string[] { "Show the start window next time", "Startvenster de volgende keer tonen", "Startfenster beim nächsten Mal anzeigen", "Afficher la fenêtre de démarrage la prochaine fois", "Mostrar la ventana de inicio la próxima vez" },

            new string[] { "Skipped: this build is not on disk now.",
                           "Overgeslagen: deze build staat nu niet op de schijf.",
                           "Übersprungen: Diese Build ist gerade nicht auf der Festplatte.",
                           "Ignorée : cette build n'est pas sur le disque pour le moment.",
                           "Omitida: esta build no está en el disco ahora." },
            new string[] { "{0} build(s) are not on disk now. No NVIDIA profile is made for a build that is gone.",
                           "{0} build(s) staan nu niet op de schijf. Voor een build die weg is, wordt geen NVIDIA-profiel gemaakt.",
                           "{0} Build(s) sind gerade nicht auf der Festplatte. Für eine verschwundene Build wird kein NVIDIA-Profil angelegt.",
                           "{0} build(s) ne sont pas sur le disque pour le moment. Aucun profil NVIDIA n'est créé pour une build disparue.",
                           "{0} build(s) no están en el disco ahora. No se crea ningún perfil NVIDIA para una build que ya no está." },
            new string[] { "{0} build(s) not on disk were skipped.",
                           "{0} build(s) die niet op de schijf staan, zijn overgeslagen.",
                           "{0} Build(s), die nicht auf der Festplatte sind, wurden übersprungen.",
                           "{0} build(s) absentes du disque ont été ignorées.",
                           "Se omitieron {0} build(s) que no están en el disco." },

            new string[] { "Clean up old entries", "Oude items opruimen", "Alte Einträge aufräumen", "Nettoyer les anciennes entrées", "Limpiar entradas antiguas" },
            new string[] { "Look for old entries that this tool made for FiveM (in the NVIDIA driver and in the Windows graphics list) and remove them after you say yes",
                           "Zoek naar oude items die deze tool voor FiveM heeft gemaakt (in het NVIDIA-stuurprogramma en in de grafische lijst van Windows) en verwijder ze nadat je ja zegt",
                           "Sucht alte Einträge, die dieses Werkzeug für FiveM angelegt hat (im NVIDIA-Treiber und in der Grafikliste von Windows), und entfernt sie, nachdem Sie Ja sagen",
                           "Cherche les anciennes entrées que cet outil a créées pour FiveM (dans le pilote NVIDIA et dans la liste graphique de Windows) et les supprime après votre accord",
                           "Busca las entradas antiguas que esta herramienta creó para FiveM (en el controlador NVIDIA y en la lista de gráficos de Windows) y las quita cuando digas que sí" },
            new string[] { "Looking for old entries ...", "Oude items zoeken ...", "Alte Einträge werden gesucht ...", "Recherche des anciennes entrées ...", "Buscando entradas antiguas ..." },
            new string[] { "Nothing to clean up", "Niets om op te ruimen", "Nichts aufzuräumen", "Rien à nettoyer", "Nada que limpiar" },
            new string[] { "No old entries of this tool were found.", "Er zijn geen oude items van deze tool gevonden.", "Es wurden keine alten Einträge dieses Werkzeugs gefunden.", "Aucune ancienne entrée de cet outil n'a été trouvée.", "No se encontraron entradas antiguas de esta herramienta." },
            new string[] { "Search first", "Zoek eerst", "Zuerst suchen", "Cherchez d'abord", "Busca primero" },
            new string[] { "No FiveM build was found on disk, so the tool cannot tell which entries are old. Press Search again first.",
                           "Er is geen FiveM-build op de schijf gevonden, dus de tool kan niet zien welke items oud zijn. Druk eerst op Opnieuw zoeken.",
                           "Es wurde keine FiveM-Build auf der Festplatte gefunden, daher kann das Werkzeug nicht erkennen, welche Einträge alt sind. Klicken Sie zuerst auf Erneut suchen.",
                           "Aucune build FiveM n'a été trouvée sur le disque, l'outil ne peut donc pas savoir quelles entrées sont anciennes. Appuyez d'abord sur Rechercher à nouveau.",
                           "No se encontró ninguna build de FiveM en el disco, así que la herramienta no puede saber qué entradas son antiguas. Pulsa primero Buscar de nuevo." },
            new string[] { "These old entries can go:\r\n\r\n{0}\r\n\r\nThe old data is saved first (the Backups button opens the folder). When an entry is needed again, applying the cap makes it again.\r\n\r\nRemove them?",
                           "Deze oude items kunnen weg:\r\n\r\n{0}\r\n\r\nDe oude gegevens worden eerst bewaard (de knop Back-ups opent de map). Is een item later weer nodig, dan maakt het toepassen van de limiet het opnieuw aan.\r\n\r\nVerwijderen?",
                           "Diese alten Einträge können weg:\r\n\r\n{0}\r\n\r\nDie alten Daten werden zuerst gesichert (die Schaltfläche Sicherungen öffnet den Ordner). Wird ein Eintrag wieder gebraucht, legt das Anwenden des Limits ihn neu an.\r\n\r\nEntfernen?",
                           "Ces anciennes entrées peuvent être supprimées :\r\n\r\n{0}\r\n\r\nLes anciennes données sont d'abord sauvegardées (le bouton Sauvegardes ouvre le dossier). Si une entrée est de nouveau nécessaire, l'application de la limite la recrée.\r\n\r\nLes supprimer ?",
                           "Estas entradas antiguas se pueden quitar:\r\n\r\n{0}\r\n\r\nLos datos antiguos se guardan primero (el botón Copias de seguridad abre la carpeta). Si una entrada vuelve a hacer falta, al aplicar el límite se crea de nuevo.\r\n\r\n¿Quitarlas?" },
            new string[] { "NVIDIA profile", "NVIDIA-profiel", "NVIDIA-Profil", "Profil NVIDIA", "Perfil NVIDIA" },
            new string[] { "Windows graphics list", "Grafische lijst van Windows", "Grafikliste von Windows", "Liste graphique de Windows", "Lista de gráficos de Windows" },
            new string[] { "the build is not on disk", "de build staat niet op de schijf", "die Build ist nicht auf der Festplatte", "la build n'est pas sur le disque", "la build no está en el disco" },
            new string[] { "it holds no settings", "het bevat geen instellingen", "es enthält keine Einstellungen", "il ne contient aucun réglage", "no contiene ajustes" },
            new string[] { "the driver uses another profile for this build", "het stuurprogramma gebruikt een ander profiel voor deze build", "der Treiber verwendet für diese Build ein anderes Profil", "le pilote utilise un autre profil pour cette build", "el controlador usa otro perfil para esta build" },
            new string[] { "the file is not on disk", "het bestand staat niet op de schijf", "die Datei ist nicht auf der Festplatte", "le fichier n'est pas sur le disque", "el archivo no está en el disco" },
            new string[] { "Removing the old entries ...", "Oude items verwijderen ...", "Alte Einträge werden entfernt ...", "Suppression des anciennes entrées ...", "Quitando las entradas antiguas ..." },
            new string[] { "Old entries removed", "Oude items verwijderd", "Alte Einträge entfernt", "Anciennes entrées supprimées", "Entradas antiguas quitadas" },
            new string[] { "Removed {0} old entries. The old data was saved first.", "{0} oude items verwijderd. De oude gegevens zijn eerst bewaard.", "{0} alte Einträge entfernt. Die alten Daten wurden zuerst gesichert.", "{0} anciennes entrées supprimées. Les anciennes données ont d'abord été sauvegardées.", "Se quitaron {0} entradas antiguas. Los datos antiguos se guardaron primero." },
            new string[] { "Nothing was removed.", "Er is niets verwijderd.", "Es wurde nichts entfernt.", "Rien n'a été supprimé.", "No se quitó nada." },
            new string[] { "Could not remove an entry of the Windows graphics list. Open the full log for details.",
                           "Een item van de grafische lijst van Windows kon niet worden verwijderd. Open het volledige logboek voor details.",
                           "Ein Eintrag der Grafikliste von Windows konnte nicht entfernt werden. Öffnen Sie das vollständige Protokoll für Details.",
                           "Une entrée de la liste graphique de Windows n'a pas pu être supprimée. Ouvrez le journal complet pour les détails.",
                           "No se pudo quitar una entrada de la lista de gráficos de Windows. Abre el registro completo para ver los detalles." },

            // frame cap page
            new string[] { "Graphics card", "Videokaart", "Grafikkarte", "Carte graphique", "Tarjeta gráfica" },
            new string[] { "Checking ...", "Controleren ...", "Wird geprüft ...", "Vérification ...", "Comprobando ..." },
            new string[] { "FiveM builds found", "Gevonden FiveM-builds", "Gefundene FiveM-Builds", "Builds FiveM trouvées", "Builds de FiveM encontradas" },
            new string[] { "Use", "Gebruik", "Nutzen", "Utiliser", "Usar" },
            new string[] { "Build", "Build", "Build", "Build", "Build" },
            new string[] { "Status", "Status", "Status", "État", "Estado" },
            new string[] { "Folder", "Map", "Ordner", "Dossier", "Carpeta" },
            new string[] { "Search again", "Opnieuw zoeken", "Erneut suchen", "Rechercher à nouveau", "Buscar de nuevo" },
            new string[] { "Pick a folder ...", "Kies een map ...", "Ordner wählen ...", "Choisir un dossier ...", "Elegir una carpeta ..." },
            new string[] { "Pick the folder where FiveM is installed (the folder that contains FiveM.app).", "Kies de map waar FiveM is geïnstalleerd (de map die FiveM.app bevat).", "Wählen Sie den Ordner, in dem FiveM installiert ist (der Ordner, der FiveM.app enthält).", "Choisissez le dossier où FiveM est installé (le dossier qui contient FiveM.app).", "Elige la carpeta donde está instalado FiveM (la carpeta que contiene FiveM.app)." },
            new string[] { "Backups", "Back-ups", "Sicherungen", "Sauvegardes", "Copias de seguridad" },
            new string[] { "FPS. 140 is the highest you can pick for now. A higher cap can cause micro-stutter.",
                           "FPS. 140 is voorlopig het hoogste dat je kunt kiezen. Een hogere limiet kan microstotters veroorzaken.",
                           "FPS. 140 ist vorerst das Maximum. Ein höheres Limit kann Mikroruckler verursachen.",
                           "FPS. 140 est le maximum pour l'instant. Une limite plus haute peut causer des micro-saccades.",
                           "FPS. 140 es el máximo por ahora. Un límite más alto puede causar microtirones." },
            new string[] { "Why?", "Waarom?", "Warum?", "Pourquoi ?", "¿Por qué?" },
            new string[] { "Apply cap to the ticked builds", "Limiet toepassen op selectie", "Limit auf Auswahl anwenden", "Appliquer aux builds cochées", "Aplicar a las builds marcadas" },
            new string[] { "NVIDIA card found. The frame cap cannot be applied on NVIDIA cards.", "NVIDIA-kaart gevonden. De FPS-limiet kan niet op NVIDIA-kaarten worden toegepast.", "NVIDIA-Karte gefunden. Das FPS-Limit lässt sich auf NVIDIA-Karten nicht anwenden.", "Carte NVIDIA détectée. La limite de FPS ne peut pas être appliquée sur les cartes NVIDIA.", "Tarjeta NVIDIA encontrada. El límite de FPS no se puede aplicar en tarjetas NVIDIA." },
            new string[] { "AMD mode: works with Radeon Software.", "AMD-modus: werkt met Radeon Software.", "AMD-Modus: funktioniert mit Radeon Software.", "Mode AMD : fonctionne avec Radeon Software.", "Modo AMD: funciona con Radeon Software." },
            new string[] { "Radeon Software was not found.", "Radeon Software is niet gevonden.", "Radeon Software wurde nicht gefunden.", "Radeon Software est introuvable.", "No se encontró Radeon Software." },
            new string[] { "No supported card (AMD or NVIDIA).", "Geen ondersteunde kaart (AMD of NVIDIA).", "Keine unterstützte Karte (AMD oder NVIDIA).", "Aucune carte prise en charge (AMD ou NVIDIA).", "No hay ninguna tarjeta compatible (AMD o NVIDIA)." },
            new string[] { "No graphics card found.", "Geen videokaart gevonden.", "Keine Grafikkarte gefunden.", "Aucune carte graphique trouvée.", "No se encontró ninguna tarjeta gráfica." },

            // status texts
            new string[] { "Not in Radeon Software yet", "Nog niet in Radeon Software", "Noch nicht in Radeon Software", "Pas encore dans Radeon Software", "Aún no está en Radeon Software" },
            new string[] { "Radeon Chill on, cap now {0} / {1}", "Radeon Chill aan, limiet nu {0} / {1}", "Radeon Chill an, Limit jetzt {0} / {1}", "Radeon Chill activé, limite actuelle {0} / {1}", "Radeon Chill activado, límite actual {0} / {1}" },
            new string[] { "Radeon Chill off, cap now {0} / {1}", "Radeon Chill uit, limiet nu {0} / {1}", "Radeon Chill aus, Limit jetzt {0} / {1}", "Radeon Chill désactivé, limite actuelle {0} / {1}", "Radeon Chill desactivado, límite actual {0} / {1}" },
            new string[] { "Found", "Gevonden", "Gefunden", "Trouvé", "Encontrado" },
            new string[] { "Set to {0}.", "Ingesteld op {0}.", "Auf {0} gesetzt.", "Réglé sur {0}.", "Establecido en {0}." },
            new string[] { "Already set to {0}.", "Al ingesteld op {0}.", "Bereits auf {0} gesetzt.", "Déjà réglé sur {0}.", "Ya está establecido en {0}." },
            new string[] { "Not in Radeon Software yet. Add it there once (Gaming > Games) and turn Radeon Chill on. Then run again.",
                           "Nog niet in Radeon Software. Voeg het daar één keer toe (Gaming > Games) en zet Radeon Chill aan. Voer daarna opnieuw uit.",
                           "Noch nicht in Radeon Software. Fügen Sie es dort einmal hinzu (Gaming > Games) und schalten Sie Radeon Chill ein. Dann erneut ausführen.",
                           "Pas encore dans Radeon Software. Ajoutez-la une fois là-bas (Gaming > Games) et activez Radeon Chill. Puis relancez.",
                           "Aún no está en Radeon Software. Añádela allí una vez (Gaming > Games) y activa Radeon Chill. Luego vuelve a ejecutar." },
            new string[] { "Radeon Chill is off for this build. Turn it on in Radeon Software once. This tool only changes the frame cap.",
                           "Radeon Chill staat uit voor deze build. Zet het één keer aan in Radeon Software. Deze tool verandert alleen de FPS-limiet.",
                           "Radeon Chill ist für diese Build aus. Schalten Sie es einmal in Radeon Software ein. Dieses Werkzeug ändert nur das FPS-Limit.",
                           "Radeon Chill est désactivé pour cette build. Activez-le une fois dans Radeon Software. Cet outil ne change que la limite de FPS.",
                           "Radeon Chill está desactivado para esta build. Actívalo una vez en Radeon Software. Esta herramienta solo cambia el límite de FPS." },
            new string[] { "No cap values yet. Open this build in Radeon Software and move the Chill sliders once.",
                           "Nog geen limietwaarden. Open deze build in Radeon Software en verschuif de Chill-schuiven één keer.",
                           "Noch keine Limit-Werte. Öffnen Sie diese Build in Radeon Software und bewegen Sie die Chill-Regler einmal.",
                           "Pas encore de valeurs de limite. Ouvrez cette build dans Radeon Software et déplacez une fois les curseurs Chill.",
                           "Aún no hay valores de límite. Abre esta build en Radeon Software y mueve los controles de Chill una vez." },
            new string[] { "Could not find this profile in the Radeon file. Nothing was changed.",
                           "Kon dit profiel niet vinden in het Radeon-bestand. Er is niets veranderd.",
                           "Dieses Profil wurde in der Radeon-Datei nicht gefunden. Es wurde nichts geändert.",
                           "Ce profil est introuvable dans le fichier Radeon. Rien n'a été modifié.",
                           "No se encontró este perfil en el archivo de Radeon. No se cambió nada." },
            new string[] { "Could not confirm which profile belongs to this build. Nothing was changed.",
                           "Kon niet bevestigen welk profiel bij deze build hoort. Er is niets veranderd.",
                           "Es konnte nicht bestätigt werden, welches Profil zu dieser Build gehört. Es wurde nichts geändert.",
                           "Impossible de confirmer quel profil correspond à cette build. Rien n'a été modifié.",
                           "No se pudo confirmar qué perfil pertenece a esta build. No se cambió nada." },

            // messages on the frame cap page
            new string[] { "Tick at least one FiveM build.", "Vink minstens één FiveM-build aan.", "Markieren Sie mindestens eine FiveM-Build.", "Cochez au moins une build FiveM.", "Marca al menos una build de FiveM." },
            new string[] { "Radeon Software will close for a short moment and open again.\r\nYour old settings file is backed up first.\r\n\r\nSet the cap to {0} FPS for {1} build(s)?",
                           "Radeon Software wordt even gesloten en daarna weer geopend.\r\nJe oude instellingenbestand wordt eerst opgeslagen als back-up.\r\n\r\nDe limiet op {0} FPS zetten voor {1} build(s)?",
                           "Radeon Software wird kurz geschlossen und danach wieder geöffnet.\r\nDie alte Einstellungsdatei wird zuvor gesichert.\r\n\r\nDas Limit für {1} Build(s) auf {0} FPS setzen?",
                           "Radeon Software va se fermer un instant puis se rouvrir.\r\nVotre ancien fichier de réglages est d'abord sauvegardé.\r\n\r\nRégler la limite sur {0} FPS pour {1} build(s) ?",
                           "Radeon Software se cerrará un momento y se volverá a abrir.\r\nTu archivo de ajustes anterior se guarda antes como copia.\r\n\r\n¿Establecer el límite en {0} FPS para {1} build(s)?" },

            // DNS page
            new string[] { "Network connections", "Netwerkverbindingen", "Netzwerkverbindungen", "Connexions réseau", "Conexiones de red" },
            new string[] { "Clear the DNS cache", "De DNS-cache wissen", "Den DNS-Cache leeren", "Vider le cache DNS", "Borrar la caché DNS" },
            new string[] { "Change DNS", "DNS wijzigen", "DNS ändern", "Changer le DNS", "Cambiar el DNS" },
            new string[] { "Connection", "Verbinding", "Verbindung", "Connexion", "Conexión" },
            new string[] { "Type", "Type", "Typ", "Type", "Tipo" },
            new string[] { "DNS now", "DNS nu", "DNS jetzt", "DNS actuel", "DNS actual" },
            new string[] { "Saved original DNS: ...", "Opgeslagen oorspronkelijke DNS: ...", "Gespeichertes ursprüngliches DNS: ...", "DNS d'origine enregistré : ...", "DNS original guardado: ..." },
            new string[] { "Saved original DNS: ", "Opgeslagen oorspronkelijke DNS: ", "Gespeichertes ursprüngliches DNS: ", "DNS d'origine enregistré : ", "DNS original guardado: " },
            new string[] { "none yet (nothing was changed by this program)", "nog geen (dit programma heeft niets veranderd)", "noch keines (dieses Programm hat nichts geändert)", "aucun pour l'instant (ce programme n'a rien modifié)", "ninguno aún (este programa no ha cambiado nada)" },
            new string[] { " (was automatic)", " (was automatisch)", " (war automatisch)", " (était automatique)", " (era automático)" },
            new string[] { " (was {0})", " (was {0})", " (war {0})", " (était {0})", " (era {0})" },
            new string[] { "Fixed: ", "Vast: ", "Fest: ", "Fixe : ", "Fijo: " },
            new string[] { "Automatic", "Automatisch", "Automatisch", "Automatique", "Automático" },
            new string[] { "IPv6 fixed: ", "IPv6 vast: ", "IPv6 fest: ", "IPv6 fixe : ", "IPv6 fijo: " },
            new string[] { "Refresh", "Vernieuwen", "Aktualisieren", "Actualiser", "Actualizar" },
            new string[] { "Clear DNS cache", "DNS-cache wissen", "DNS-Cache leeren", "Vider le cache DNS", "Borrar caché DNS" },
            new string[] { "Fixes \"site not found\" after a DNS or network change. It tries up to 3 times and checks the cache is really empty. Windows asks for permission first.",
                           "Lost \"site niet gevonden\" op na een DNS- of netwerkwijziging. Het probeert tot 3 keer en controleert of de cache echt leeg is. Windows vraagt eerst om toestemming.",
                           "Behebt \"Seite nicht gefunden\" nach einer DNS- oder Netzwerkänderung. Es versucht bis zu 3-mal und prüft, ob der Cache wirklich leer ist. Windows fragt zuerst nach Erlaubnis.",
                           "Corrige \"site introuvable\" après un changement de DNS ou de réseau. Essaie jusqu'à 3 fois et vérifie que le cache est bien vide. Windows demande d'abord l'autorisation.",
                           "Soluciona \"no se encuentra el sitio\" tras un cambio de DNS o de red. Lo intenta hasta 3 veces y comprueba que la caché esté realmente vacía. Windows pide permiso primero." },
            new string[] { "Also clear the NetBIOS and ARP caches (a bit more thorough)", "Wis ook de NetBIOS- en ARP-cache (iets grondiger)", "NetBIOS- und ARP-Cache ebenfalls leeren (etwas gründlicher)", "Vider aussi les caches NetBIOS et ARP (un peu plus complet)", "Borrar también las cachés NetBIOS y ARP (algo más a fondo)" },
            new string[] { "Cloudflare (default)   1.1.1.1", "Cloudflare (standaard)   1.1.1.1", "Cloudflare (Standard)   1.1.1.1", "Cloudflare (par défaut)   1.1.1.1", "Cloudflare (predeterminado)   1.1.1.1" },
            new string[] { "Google   8.8.8.8", "Google   8.8.8.8", "Google   8.8.8.8", "Google   8.8.8.8", "Google   8.8.8.8" },
            new string[] { "Cloudflare + Google", "Cloudflare + Google", "Cloudflare + Google", "Cloudflare + Google", "Cloudflare + Google" },
            new string[] { "Use this DNS on the ticked connections", "Gebruik deze DNS op de aangevinkte verbindingen", "Dieses DNS auf den markierten Verbindungen verwenden", "Utiliser ce DNS sur les connexions cochées", "Usar este DNS en las conexiones marcadas" },
            new string[] { "Undo: put DNS back how it was", "Ongedaan maken: DNS terugzetten zoals het was", "Rückgängig: DNS wie vorher einstellen", "Annuler : remettre le DNS comme avant", "Deshacer: dejar el DNS como estaba" },
            new string[] { "Windows asks for permission. The old setting is saved first, so Undo puts it back exactly.",
                           "Windows vraagt om toestemming. De oude instelling wordt eerst opgeslagen, zodat Ongedaan maken alles precies terugzet.",
                           "Windows fragt nach Erlaubnis. Die alte Einstellung wird zuerst gespeichert, sodass Rückgängig alles genau wiederherstellt.",
                           "Windows demande l'autorisation. L'ancien réglage est d'abord enregistré, donc Annuler le remet exactement.",
                           "Windows pide permiso. El ajuste anterior se guarda primero, así que Deshacer lo deja exactamente igual." },
            new string[] { "Tick at least one network connection.", "Vink minstens één netwerkverbinding aan.", "Markieren Sie mindestens eine Netzwerkverbindung.", "Cochez au moins une connexion réseau.", "Marca al menos una conexión de red." },
            new string[] { "Change DNS to {0} on {1} connection(s)?\r\n\r\nWindows will ask for permission. The current setting is saved first, so \"Undo\" can put it back.",
                           "DNS wijzigen naar {0} op {1} verbinding(en)?\r\n\r\nWindows vraagt om toestemming. De huidige instelling wordt eerst opgeslagen, zodat \"Ongedaan maken\" het kan terugzetten.",
                           "DNS auf {0} für {1} Verbindung(en) ändern?\r\n\r\nWindows fragt nach Erlaubnis. Die aktuelle Einstellung wird zuerst gespeichert, sodass \"Rückgängig\" sie wiederherstellen kann.",
                           "Changer le DNS en {0} sur {1} connexion(s) ?\r\n\r\nWindows demandera l'autorisation. Le réglage actuel est d'abord enregistré, pour que \"Annuler\" puisse le remettre.",
                           "¿Cambiar el DNS a {0} en {1} conexión(es)?\r\n\r\nWindows pedirá permiso. El ajuste actual se guarda primero, para que \"Deshacer\" pueda restaurarlo." },
            new string[] { "Put DNS back the way it was before this program changed it?\r\n\r\n{0}\r\n\r\nWindows will ask for permission.",
                           "DNS terugzetten zoals het was voordat dit programma het veranderde?\r\n\r\n{0}\r\n\r\nWindows vraagt om toestemming.",
                           "DNS so zurücksetzen, wie es war, bevor dieses Programm es geändert hat?\r\n\r\n{0}\r\n\r\nWindows fragt nach Erlaubnis.",
                           "Remettre le DNS comme il était avant que ce programme le change ?\r\n\r\n{0}\r\n\r\nWindows demandera l'autorisation.",
                           "¿Dejar el DNS como estaba antes de que este programa lo cambiara?\r\n\r\n{0}\r\n\r\nWindows pedirá permiso." },

            // FiveM cache page
            new string[] { "Which FiveM", "Welke FiveM", "Welches FiveM", "Quel FiveM", "Qué FiveM" },
            new string[] { "FiveM install:", "FiveM-installatie:", "FiveM-Installation:", "Installation FiveM :", "Instalación de FiveM:" },
            new string[] { "Close FiveM for me", "Sluit FiveM voor mij", "FiveM für mich schließen", "Fermer FiveM pour moi", "Cerrar FiveM por mí" },
            new string[] { "Temporary files", "Tijdelijke bestanden", "Temporäre Dateien", "Fichiers temporaires", "Archivos temporales" },
            new string[] { "Logs and crash reports", "Logs en crashrapporten", "Protokolle und Absturzberichte", "Journaux et rapports de plantage", "Registros e informes de fallos" },
            new string[] { "Login and settings (deep clean)", "Inlog en instellingen (diep)", "Anmeldung/Einstellungen (tief)", "Connexion et réglages (profond)", "Sesión y ajustes (profunda)" },
            new string[] { "Include in the clean", "Meenemen in het opschonen", "In die Bereinigung einbeziehen", "Inclure dans le nettoyage", "Incluir en la limpieza" },
            new string[] { "Downloaded server files and game cache. FiveM builds them again, so the next start can be a bit slower.",
                           "Gedownloade serverbestanden en game-cache. FiveM bouwt ze opnieuw op, dus de volgende start kan iets trager zijn.",
                           "Heruntergeladene Serverdateien und Spiel-Cache. FiveM baut sie neu auf, der nächste Start kann daher etwas langsamer sein.",
                           "Fichiers serveur téléchargés et cache du jeu. FiveM les recrée, donc le prochain démarrage peut être un peu plus lent.",
                           "Archivos del servidor descargados y caché del juego. FiveM los vuelve a crear, así que el próximo inicio puede ser algo más lento." },
            new string[] { "Old log files and crash dumps. Safe to remove.", "Oude logbestanden en crashdumps. Veilig te verwijderen.", "Alte Protokolldateien und Absturzabbilder. Kann bedenkenlos entfernt werden.", "Anciens fichiers journaux et vidages de plantage. Peut être supprimé sans risque.", "Archivos de registro antiguos y volcados de fallos. Se pueden borrar sin riesgo." },
            new string[] { "You have to log in to FiveM again and may lose local settings. Use it only when the normal clean did not help.",
                           "Je moet opnieuw inloggen bij FiveM en kunt lokale instellingen kwijtraken. Gebruik het alleen als het gewone opschonen niet hielp.",
                           "Sie müssen sich bei FiveM neu anmelden und verlieren möglicherweise lokale Einstellungen. Nur verwenden, wenn die normale Bereinigung nicht geholfen hat.",
                           "Vous devrez vous reconnecter à FiveM et pourriez perdre des réglages locaux. À utiliser seulement si le nettoyage normal n'a pas aidé.",
                           "Tendrás que volver a iniciar sesión en FiveM y podrías perder ajustes locales. Úsalo solo si la limpieza normal no ayudó." },
            new string[] { "Pure mode (-pure_1)", "Pure-modus (-pure_1)", "Pure-Modus (-pure_1)", "Mode pure (-pure_1)", "Modo puro (-pure_1)" },
            new string[] { "ignores local file changes; for troubleshooting mods", "negeert lokale bestandswijzigingen; voor het opsporen van mod-problemen", "ignoriert lokale Dateiänderungen; zur Fehlersuche bei Mods", "ignore les modifications locales de fichiers ; pour dépanner les mods", "ignora los cambios locales de archivos; para solucionar problemas con mods" },
            new string[] { "Desktop icon: pure mode", "Bureaubladpictogram: pure-modus", "Desktop-Symbol: Pure-Modus", "Icône du bureau : mode pure", "Icono del escritorio: modo puro" },
            new string[] { "Makes an icon on your desktop that starts FiveM in pure mode.", "Maakt een pictogram op je bureaublad dat FiveM in pure-modus start.", "Legt ein Symbol auf den Desktop, das FiveM im Pure-Modus startet.", "Crée sur le bureau une icône qui lance FiveM en mode pure.", "Crea en el escritorio un icono que inicia FiveM en modo puro." },
            new string[] { "Makes an icon on your desktop that starts FiveM with -pure_1 (FiveM then ignores local file changes; for troubleshooting mods).", "Maakt een pictogram op je bureaublad dat FiveM met -pure_1 start (FiveM negeert dan lokale bestandswijzigingen; voor het opsporen van mod-problemen).", "Legt ein Symbol auf den Desktop, das FiveM mit -pure_1 startet (FiveM ignoriert dann lokale Dateiänderungen; zur Fehlersuche bei Mods).", "Crée sur le bureau une icône qui lance FiveM avec -pure_1 (FiveM ignore alors les modifications locales de fichiers ; pour dépanner les mods).", "Crea en el escritorio un icono que inicia FiveM con -pure_1 (FiveM ignora entonces los cambios locales de archivos; para solucionar problemas con mods)." },
            new string[] { "Desktop icon made", "Bureaubladpictogram gemaakt", "Desktop-Symbol erstellt", "Icône du bureau créée", "Icono del escritorio creado" },
            new string[] { "The desktop icon was updated", "Het bureaubladpictogram is bijgewerkt", "Das Desktop-Symbol wurde aktualisiert", "L'icône du bureau a été mise à jour", "El icono del escritorio se actualizó" },
            new string[] { "The desktop icon is already there", "Het bureaubladpictogram staat er al", "Das Desktop-Symbol ist schon da", "L'icône du bureau est déjà là", "El icono del escritorio ya existe" },
            new string[] { "The desktop icon was not made", "Het bureaubladpictogram is niet gemaakt", "Das Desktop-Symbol wurde nicht erstellt", "L'icône du bureau n'a pas été créée", "El icono del escritorio no se creó" },
            new string[] { "Clean cache", "Cache opschonen", "Cache bereinigen", "Nettoyer le cache", "Limpiar caché" },
            new string[] { "Start FiveM", "FiveM starten", "FiveM starten", "Lancer FiveM", "Iniciar FiveM" },
            new string[] { "FiveM is running ({0} process(es)). Close it before cleaning.", "FiveM draait ({0} proces(sen)). Sluit het vóór het opschonen.", "FiveM läuft ({0} Prozess(e)). Schließen Sie es vor der Bereinigung.", "FiveM est en cours d'exécution ({0} processus). Fermez-le avant de nettoyer.", "FiveM se está ejecutando ({0} proceso(s)). Ciérralo antes de limpiar." },
            new string[] { "FiveM is closed. Good.", "FiveM is gesloten. Goed.", "FiveM ist geschlossen. Gut.", "FiveM est fermé. Parfait.", "FiveM está cerrado. Bien." },
            new string[] { "No FiveM folder found. Use \"Search again\", or pick a folder on the frame cap tab.",
                           "Geen FiveM-map gevonden. Gebruik \"Opnieuw zoeken\" of kies een map op het tabblad FPS-limiet.",
                           "Kein FiveM-Ordner gefunden. Verwenden Sie \"Erneut suchen\" oder wählen Sie einen Ordner auf der Registerkarte FPS-Limit.",
                           "Aucun dossier FiveM trouvé. Utilisez \"Rechercher à nouveau\" ou choisissez un dossier dans l'onglet Limite de FPS.",
                           "No se encontró ninguna carpeta de FiveM. Usa \"Buscar de nuevo\" o elige una carpeta en la pestaña Límite de FPS." },
            new string[] { "Measuring the folders ...", "De mappen worden gemeten ...", "Ordner werden vermessen ...", "Mesure des dossiers ...", "Midiendo las carpetas ..." },
            new string[] { "Will clean about {0} (the cards you included).", "Gaat ongeveer {0} opschonen (de kaarten die je hebt meegenomen).", "Bereinigt etwa {0} (die einbezogenen Karten).", "Va nettoyer environ {0} (les cartes incluses).", "Limpiará unos {0} (las tarjetas incluidas)." },
            new string[] { "Deep clean removes your saved FiveM login details and local settings, as well as the cache.\r\n\r\nYou will have to log in to FiveM again and may lose local settings.\r\n\r\nUse it only when the normal clean did not help. Include it anyway?",
                           "De diepe schoonmaak verwijdert je opgeslagen FiveM-inloggegevens en lokale instellingen, naast de cache.\r\n\r\nJe moet opnieuw inloggen bij FiveM en kunt lokale instellingen kwijtraken.\r\n\r\nGebruik het alleen als het gewone opschonen niet hielp. Toch meenemen?",
                           "Die Tiefenreinigung entfernt Ihre gespeicherten FiveM-Anmeldedaten und lokalen Einstellungen sowie den Cache.\r\n\r\nSie müssen sich bei FiveM neu anmelden und verlieren möglicherweise lokale Einstellungen.\r\n\r\nNur verwenden, wenn die normale Bereinigung nicht geholfen hat. Trotzdem einbeziehen?",
                           "Le nettoyage en profondeur supprime vos identifiants FiveM enregistrés et vos réglages locaux, en plus du cache.\r\n\r\nVous devrez vous reconnecter à FiveM et pourriez perdre des réglages locaux.\r\n\r\nN'utilisez-le que si le nettoyage normal n'a pas aidé. L'inclure quand même ?",
                           "La limpieza profunda elimina tus datos de inicio de sesión de FiveM guardados y los ajustes locales, además de la caché.\r\n\r\nTendrás que volver a iniciar sesión en FiveM y podrías perder ajustes locales.\r\n\r\nÚsala solo si la limpieza normal no ayudó. ¿Incluirla de todos modos?" },
            new string[] { "Close FiveM now? Anything you have open in the game is lost.", "FiveM nu sluiten? Alles wat je in het spel open hebt staan gaat verloren.", "FiveM jetzt schließen? Alles, was im Spiel offen ist, geht verloren.", "Fermer FiveM maintenant ? Tout ce qui est ouvert dans le jeu sera perdu.", "¿Cerrar FiveM ahora? Se perderá todo lo que tengas abierto en el juego." },
            new string[] { "No FiveM folder is selected.", "Er is geen FiveM-map gekozen.", "Es ist kein FiveM-Ordner ausgewählt.", "Aucun dossier FiveM n'est sélectionné.", "No hay ninguna carpeta de FiveM seleccionada." },
            new string[] { "Include at least one card.", "Neem minstens één kaart mee.", "Beziehen Sie mindestens eine Karte ein.", "Incluez au moins une carte.", "Incluye al menos una tarjeta." },
            new string[] { "FiveM is running. Close it first (the button \"Close FiveM for me\" does that).", "FiveM draait. Sluit het eerst (de knop \"Sluit FiveM voor mij\" doet dat).", "FiveM läuft. Schließen Sie es zuerst (die Schaltfläche \"FiveM für mich schließen\" erledigt das).", "FiveM est en cours d'exécution. Fermez-le d'abord (le bouton \"Fermer FiveM pour moi\" le fait).", "FiveM se está ejecutando. Ciérralo primero (el botón \"Cerrar FiveM por mí\" lo hace)." },
            new string[] { "Clean these (about {0})?\r\n\r\n - {1}\r\n\r\nFolder: {2}", "Dit opschonen (ongeveer {0})?\r\n\r\n - {1}\r\n\r\nMap: {2}", "Dies bereinigen (etwa {0})?\r\n\r\n - {1}\r\n\r\nOrdner: {2}", "Nettoyer ceci (environ {0}) ?\r\n\r\n - {1}\r\n\r\nDossier : {2}", "¿Limpiar esto (unos {0})?\r\n\r\n - {1}\r\n\r\nCarpeta: {2}" },
            new string[] { "FiveM.exe was not found next to the FiveM.app folder.", "FiveM.exe is niet gevonden naast de map FiveM.app.", "FiveM.exe wurde neben dem Ordner FiveM.app nicht gefunden.", "FiveM.exe est introuvable à côté du dossier FiveM.app.", "No se encontró FiveM.exe junto a la carpeta FiveM.app." },
            new string[] { "FiveM is already running.", "FiveM draait al.", "FiveM läuft bereits.", "FiveM est déjà en cours d'exécution.", "FiveM ya se está ejecutando." },

            // the log
            new string[] { "All", "Alles", "Alle", "Tout", "Todo" },
            new string[] { "DNS", "DNS", "DNS", "DNS", "DNS" },
            new string[] { "General", "Algemeen", "Allgemein", "Général", "General" },
            new string[] { "Debug lines", "Debugregels", "Debug-Zeilen", "Lignes de débogage", "Líneas de depuración" },
            new string[] { "Copy the log to the clipboard", "Kopieer het logboek naar het klembord", "Das Protokoll in die Zwischenablage kopieren", "Copier le journal dans le presse-papiers", "Copiar el registro al portapapeles" },
            new string[] { "Open the log file with the program Windows uses for .txt files", "Open het logbestand met het programma dat Windows voor .txt-bestanden gebruikt", "Die Protokolldatei mit dem Programm öffnen, das Windows für .txt-Dateien verwendet", "Ouvrir le fichier journal avec le programme que Windows utilise pour les .txt", "Abrir el archivo de registro con el programa que Windows usa para .txt" },
            new string[] { "Show the log file in its folder", "Toon het logbestand in zijn map", "Die Protokolldatei in ihrem Ordner anzeigen", "Afficher le fichier journal dans son dossier", "Mostrar el archivo de registro en su carpeta" },
            new string[] { "Open the log in its own window", "Open het logboek in een eigen venster", "Das Protokoll in einem eigenen Fenster öffnen", "Ouvrir le journal dans sa propre fenêtre", "Abrir el registro en su propia ventana" },
            new string[] { "full log", "volledig logboek", "vollständiges Protokoll", "journal complet", "registro completo" },
            new string[] { "Log file: ", "Logbestand: ", "Protokolldatei: ", "Fichier journal : ", "Archivo de registro: " },

            // welcome window
            new string[] { "Welcome to {0}", "Welkom bij {0}", "Willkommen bei {0}", "Bienvenue dans {0}", "Bienvenido a {0}" },
            new string[] { "Provided as-is and still in a testing phase. Use it at your own risk.", "Geleverd zoals het is en nog in een testfase. Gebruik op eigen risico.", "Wird ohne Gewähr bereitgestellt und befindet sich noch in der Testphase. Nutzung auf eigenes Risiko.", "Fourni tel quel et encore en phase de test. À utiliser à vos propres risques.", "Se ofrece tal cual y aún está en fase de pruebas. Úsalo bajo tu propio riesgo." },
            new string[] { "What it does", "Wat het doet", "Was es tut", "Ce qu'il fait", "Qué hace" },
            new string[] { "DNS tools: clears the DNS cache, and changes DNS with an Undo.", "DNS-tools: wist de DNS-cache en wijzigt DNS met een Ongedaan maken.", "DNS-Werkzeuge: leeren den DNS-Cache und ändern DNS mit Rückgängig-Funktion.", "Outils DNS : vident le cache DNS et changent le DNS avec une fonction Annuler.", "Herramientas DNS: borran la caché DNS y cambian el DNS con opción de deshacer." },
            new string[] { "FiveM cache: clears FiveM's temporary files. The deep clean also removes your FiveM login and local settings.", "FiveM-cache: wist de tijdelijke bestanden van FiveM. De diepe schoonmaak verwijdert ook je FiveM-inlog en lokale instellingen.", "FiveM-Cache: löscht die temporären Dateien von FiveM. Die Tiefenreinigung entfernt auch Ihre FiveM-Anmeldung und lokalen Einstellungen.", "Cache FiveM : supprime les fichiers temporaires de FiveM. Le nettoyage en profondeur supprime aussi votre connexion FiveM et vos réglages locaux.", "Caché de FiveM: borra los archivos temporales de FiveM. La limpieza profunda también elimina tu inicio de sesión de FiveM y los ajustes locales." },
            new string[] { "Windows may ask for permission for some actions. Where it is possible, the old setting is saved or backed up first.", "Windows kan bij sommige acties om toestemming vragen. Waar mogelijk wordt de oude instelling eerst opgeslagen of er wordt een back-up gemaakt.", "Windows fragt bei manchen Aktionen möglicherweise nach Erlaubnis. Wo es möglich ist, wird die alte Einstellung zuvor gespeichert oder gesichert.", "Windows peut demander l'autorisation pour certaines actions. Quand c'est possible, l'ancien réglage est d'abord enregistré ou sauvegardé.", "Windows puede pedir permiso para algunas acciones. Cuando es posible, el ajuste anterior se guarda o se copia antes." },
            new string[] { "By continuing you accept these risks.", "Door verder te gaan accepteer je deze risico's.", "Wenn Sie fortfahren, akzeptieren Sie diese Risiken.", "En continuant, vous acceptez ces risques.", "Al continuar aceptas estos riesgos." },
            new string[] { "Do not show this window again", "Dit venster niet meer tonen", "Dieses Fenster nicht mehr anzeigen", "Ne plus afficher cette fenêtre", "No volver a mostrar esta ventana" },
            new string[] { "I understand, continue", "Ik begrijp het, doorgaan", "Ich verstehe, weiter", "J'ai compris, continuer", "Entendido, continuar" },
            new string[] { "Quit", "Afsluiten", "Beenden", "Quitter", "Salir" },

            // start window
            new string[] { "What do you want to do?", "Wat wil je doen?", "Was möchten Sie tun?", "Que voulez-vous faire ?", "¿Qué quieres hacer?" },
            new string[] { "Limit the FPS of every FiveM build", "Beperk de FPS van elke FiveM-build", "Die FPS jeder FiveM-Build begrenzen", "Limiter les FPS de chaque build FiveM", "Limitar los FPS de cada build de FiveM" },
            new string[] { "Clear or change your DNS, with an Undo", "Wis of wijzig je DNS, met een Ongedaan maken", "DNS leeren oder ändern, mit Rückgängig-Funktion", "Vider ou changer votre DNS, avec Annuler", "Borrar o cambiar tu DNS, con opción de deshacer" },
            new string[] { "Clean FiveM's temporary files", "Ruim de tijdelijke bestanden van FiveM op", "Temporäre Dateien von FiveM bereinigen", "Nettoyer les fichiers temporaires de FiveM", "Limpiar los archivos temporales de FiveM" },
            new string[] { "Skip this window next time and open the tool I pick", "Dit venster de volgende keer overslaan en het gekozen hulpmiddel openen", "Dieses Fenster beim nächsten Mal überspringen und das gewählte Werkzeug öffnen", "Passer cette fenêtre la prochaine fois et ouvrir l'outil choisi", "Omitir esta ventana la próxima vez y abrir la herramienta elegida" },
            new string[] { "You can bring this window back with the link at the bottom of the main window.", "Je kunt dit venster terugbrengen met de link onderaan het hoofdvenster.", "Mit dem Link unten im Hauptfenster können Sie dieses Fenster wieder aktivieren.", "Vous pouvez réafficher cette fenêtre avec le lien en bas de la fenêtre principale.", "Puedes recuperar esta ventana con el enlace de la parte inferior de la ventana principal." },

            // "Why 140?" window
            new string[] { "Why 140?", "Waarom 140?", "Warum 140?", "Pourquoi 140 ?", "¿Por qué 140?" },
            new string[] { "Why is 140 the highest?", "Waarom is 140 het hoogste?", "Warum ist 140 das Maximum?", "Pourquoi 140 est-il le maximum ?", "¿Por qué 140 es el máximo?" },
            new string[] { "FiveM runs on the GTA V engine. The game came out in 2013 and the engine was never designed for very high frame rates, because hardware back then could not reach them.",
                           "FiveM draait op de GTA V-engine. De game kwam uit in 2013 en de engine is nooit ontworpen voor heel hoge framerates, omdat hardware die toen niet kon halen.",
                           "FiveM läuft auf der GTA-V-Engine. Das Spiel erschien 2013, und die Engine wurde nie für sehr hohe Bildraten entworfen, weil die Hardware sie damals nicht erreichen konnte.",
                           "FiveM fonctionne sur le moteur de GTA V. Le jeu est sorti en 2013 et le moteur n'a jamais été conçu pour de très hautes fréquences d'images, car le matériel de l'époque ne pouvait pas les atteindre.",
                           "FiveM funciona con el motor de GTA V. El juego salió en 2013 y el motor nunca se diseñó para tasas de fotogramas muy altas, porque el hardware de entonces no podía alcanzarlas." },
            new string[] { "The game can go above 140 FPS, but today's PCs get there easily and the engine does not cope well: players report micro-stutter (small hitches, even at high FPS) above roughly 140 FPS.",
                           "De game kan boven de 140 FPS komen, maar pc's van nu halen dat makkelijk en de engine kan er niet goed mee overweg: spelers melden microstotters (kleine haperingen, zelfs bij hoge FPS) boven ongeveer 140 FPS.",
                           "Das Spiel kann über 140 FPS laufen, aber heutige PCs erreichen das leicht und die Engine kommt damit nicht gut zurecht: Spieler berichten von Mikrorucklern (kleine Aussetzer, auch bei hohen FPS) ab etwa 140 FPS.",
                           "Le jeu peut dépasser 140 FPS, mais les PC actuels y arrivent facilement et le moteur ne le gère pas bien : des joueurs signalent des micro-saccades (petits ralentissements, même avec beaucoup de FPS) au-delà d'environ 140 FPS.",
                           "El juego puede superar los 140 FPS, pero los PC de hoy llegan fácilmente y el motor no lo maneja bien: los jugadores informan de microtirones (pequeños tirones, incluso con FPS altos) por encima de unos 140 FPS." },
            new string[] { "A likely cause is the game's audio part falling out of step with the main part. The engine also has its own limit near 188 FPS.",
                           "Een waarschijnlijke oorzaak is dat het geluidsdeel van de game uit de pas loopt met het hoofddeel. De engine heeft ook een eigen limiet rond 188 FPS.",
                           "Eine wahrscheinliche Ursache ist, dass der Audioteil des Spiels nicht mehr im Takt mit dem Hauptteil ist. Die Engine hat außerdem ein eigenes Limit bei etwa 188 FPS.",
                           "Une cause probable est que la partie audio du jeu se désynchronise de la partie principale. Le moteur a aussi sa propre limite autour de 188 FPS.",
                           "Una causa probable es que la parte de audio del juego se desincroniza de la parte principal. El motor también tiene su propio límite cerca de 188 FPS." },
            new string[] { "A cap keeps FiveM below the range where this happens. A lower cap is fine, and often feels smoother.",
                           "Een limiet houdt FiveM onder het bereik waarin dit gebeurt. Een lagere limiet is prima en voelt vaak soepeler.",
                           "Ein Limit hält FiveM unter dem Bereich, in dem das passiert. Ein niedrigeres Limit ist in Ordnung und fühlt sich oft flüssiger an.",
                           "Une limite garde FiveM sous la plage où cela se produit. Une limite plus basse convient et paraît souvent plus fluide.",
                           "Un límite mantiene FiveM por debajo del rango donde ocurre esto. Un límite más bajo está bien y a menudo se siente más fluido." },
            new string[] { "Got it", "Begrepen", "Verstanden", "Compris", "Entendido" },
            // activity card: what is going on and how it ended
            new string[] { "Change the language", "Taal wijzigen", "Sprache ändern", "Changer la langue", "Cambiar el idioma" },
            new string[] { "Working ...", "Bezig ...", "Wird bearbeitet ...", "En cours ...", "Trabajando ..." },
            new string[] { "Applying the frame cap ...", "De FPS-limiet wordt ingesteld ...", "FPS-Limit wird angewendet ...", "Application de la limite de FPS ...", "Aplicando el límite de FPS ..." },
            new string[] { "Asking AMD what is set now ...", "AMD wordt gevraagd wat er nu is ingesteld ...", "AMD wird gefragt, was jetzt eingestellt ist ...", "Demande à AMD ce qui est réglé actuellement ...", "Preguntando a AMD qué hay configurado ahora ..." },
            new string[] { "Closing Radeon Software ...", "Radeon Software wordt gesloten ...", "Radeon Software wird geschlossen ...", "Fermeture de Radeon Software ...", "Cerrando Radeon Software ..." },
            new string[] { "Reading Radeon's profile file ...", "Het profielbestand van Radeon wordt gelezen ...", "Die Profildatei von Radeon wird gelesen ...", "Lecture du fichier de profils de Radeon ...", "Leyendo el archivo de perfiles de Radeon ..." },
            new string[] { "Writing the new cap ...", "De nieuwe limiet wordt geschreven ...", "Das neue Limit wird geschrieben ...", "Écriture de la nouvelle limite ...", "Escribiendo el nuevo límite ..." },
            new string[] { "Starting Radeon Software again ...", "Radeon Software wordt opnieuw gestart ...", "Radeon Software wird neu gestartet ...", "Redémarrage de Radeon Software ...", "Iniciando Radeon Software de nuevo ..." },
            new string[] { "Waiting for AMD to load the new value ...", "Wachten tot AMD de nieuwe waarde laadt ...", "Warten, bis AMD den neuen Wert lädt ...", "Attente du chargement de la nouvelle valeur par AMD ...", "Esperando a que AMD cargue el nuevo valor ..." },
            new string[] { "Undoing a wrong guess ...", "Een verkeerde gok wordt teruggedraaid ...", "Eine falsche Vermutung wird rückgängig gemacht ...", "Annulation d'une mauvaise hypothèse ...", "Deshaciendo una suposición equivocada ..." },
            new string[] { "Nothing to change", "Niets te veranderen", "Nichts zu ändern", "Rien à changer", "Nada que cambiar" },
            new string[] { "{0} build(s) already had the cap set to {1}. Radeon Software was left alone.", "{0} build(s) hadden de limiet al op {1} staan. Radeon Software is met rust gelaten.", "{0} Build(s) hatten das Limit schon auf {1}. Radeon Software wurde nicht angefasst.", "{0} build(s) avaient déjà la limite réglée sur {1}. Radeon Software n'a pas été touché.", "{0} build(s) ya tenían el límite en {1}. No se tocó Radeon Software." },
            new string[] { "Frame cap set to {0}", "FPS-limiet ingesteld op {0}", "FPS-Limit auf {0} gesetzt", "Limite de FPS réglée sur {0}", "Límite de FPS establecido en {0}" },
            new string[] { "Changed {0} build(s), {1} already had it. Radeon Software was restarted.", "{0} build(s) aangepast, {1} hadden het al. Radeon Software is opnieuw gestart.", "{0} Build(s) geändert, {1} hatten es schon. Radeon Software wurde neu gestartet.", "{0} build(s) modifiée(s), {1} l'avaient déjà. Radeon Software a été redémarré.", "{0} build(s) cambiada(s), {1} ya lo tenían. Se reinició Radeon Software." },
            new string[] { "Not everything worked", "Niet alles is gelukt", "Nicht alles hat funktioniert", "Tout n'a pas fonctionné", "No todo funcionó" },
            new string[] { "It did not finish", "Het is niet afgerond", "Es wurde nicht abgeschlossen", "Cela ne s'est pas terminé", "No terminó" },
            new string[] { "Open the full log for details.", "Open het volledige logboek voor details.", "Öffnen Sie das vollständige Protokoll für Details.", "Ouvrez le journal complet pour les détails.", "Abre el registro completo para ver los detalles." },
            new string[] { "Searching for FiveM ...", "Zoeken naar FiveM ...", "Suche nach FiveM ...", "Recherche de FiveM ...", "Buscando FiveM ..." },
            new string[] { "Looking at your graphics card ...", "Je videokaart wordt bekeken ...", "Ihre Grafikkarte wird geprüft ...", "Examen de votre carte graphique ...", "Comprobando tu tarjeta gráfica ..." },
            new string[] { "Looking for FiveM on your drives ...", "Zoeken naar FiveM op je schijven ...", "Suche nach FiveM auf Ihren Laufwerken ...", "Recherche de FiveM sur vos disques ...", "Buscando FiveM en tus unidades ..." },
            new string[] { "Found {0} FiveM build(s)", "{0} FiveM-build(s) gevonden", "{0} FiveM-Build(s) gefunden", "{0} build(s) FiveM trouvée(s)", "{0} build(s) de FiveM encontrada(s)" },
            new string[] { "No FiveM build found", "Geen FiveM-build gevonden", "Keine FiveM-Build gefunden", "Aucune build FiveM trouvée", "No se encontró ninguna build de FiveM" },
            new string[] { "Use \"Pick a folder ...\" and choose the folder where FiveM is installed.", "Gebruik \"Kies een map ...\" en kies de map waar FiveM is geïnstalleerd.", "Verwenden Sie \"Ordner wählen ...\" und wählen Sie den Ordner, in dem FiveM installiert ist.", "Utilisez \"Choisir un dossier ...\" et choisissez le dossier où FiveM est installé.", "Usa \"Elegir una carpeta ...\" y elige la carpeta donde está instalado FiveM." },
            new string[] { "Clearing the DNS cache ...", "De DNS-cache wordt gewist ...", "Der DNS-Cache wird geleert ...", "Vidage du cache DNS ...", "Borrando la caché DNS ..." },
            new string[] { "Changing the DNS ...", "De DNS wordt gewijzigd ...", "Das DNS wird geändert ...", "Changement du DNS ...", "Cambiando el DNS ..." },
            new string[] { "Putting the DNS back ...", "De DNS wordt teruggezet ...", "Das DNS wird zurückgesetzt ...", "Rétablissement du DNS ...", "Restaurando el DNS ..." },
            new string[] { "Waiting for Windows permission ...", "Wachten op toestemming van Windows ...", "Warten auf die Erlaubnis von Windows ...", "Attente de l'autorisation de Windows ...", "Esperando el permiso de Windows ..." },
            new string[] { "Reading the result ...", "Het resultaat wordt gelezen ...", "Das Ergebnis wird gelesen ...", "Lecture du résultat ...", "Leyendo el resultado ..." },
            new string[] { "DNS cache cleared", "DNS-cache gewist", "DNS-Cache geleert", "Cache DNS vidé", "Caché DNS borrada" },
            new string[] { "Windows said the flush worked.", "Windows meldt dat het wissen gelukt is.", "Windows meldet, dass das Leeren funktioniert hat.", "Windows indique que le vidage a réussi.", "Windows indica que el borrado funcionó." },
            new string[] { "DNS cache entries: {0} before, {1} now.", "DNS-cache-items: {0} ervoor, nu {1}.", "DNS-Cache-Einträge: {0} vorher, jetzt {1}.", "Entrées du cache DNS : {0} avant, {1} maintenant.", "Entradas de la caché DNS: {0} antes, {1} ahora." },
            new string[] { "DNS cache entries: {0} before, {1} now. Windows adds a few names back by itself, like the ones from the hosts file.", "DNS-cache-items: {0} ervoor, nu {1}. Windows zet zelf een paar namen terug, zoals die uit het hosts-bestand.", "DNS-Cache-Einträge: {0} vorher, jetzt {1}. Windows fügt von selbst ein paar Namen wieder hinzu, etwa die aus der Hosts-Datei.", "Entrées du cache DNS : {0} avant, {1} maintenant. Windows en remet quelques-unes tout seul, comme celles du fichier hosts.", "Entradas de la caché DNS: {0} antes, {1} ahora. Windows vuelve a añadir algunos nombres por sí mismo, como los del archivo hosts." },
            new string[] { "DNS changed", "DNS gewijzigd", "DNS geändert", "DNS modifié", "DNS cambiado" },
            new string[] { "{0} on {1} connection(s). Undo puts it back.", "{0} op {1} verbinding(en). Ongedaan maken zet het terug.", "{0} auf {1} Verbindung(en). Rückgängig stellt es wieder her.", "{0} sur {1} connexion(s). Annuler le remet.", "{0} en {1} conexión(es). Deshacer lo restaura." },
            new string[] { "DNS put back", "DNS teruggezet", "DNS zurückgesetzt", "DNS rétabli", "DNS restaurado" },
            new string[] { "The saved settings are back on your connections.", "De opgeslagen instellingen staan weer op je verbindingen.", "Die gespeicherten Einstellungen sind wieder auf Ihren Verbindungen.", "Les réglages enregistrés sont de retour sur vos connexions.", "Los ajustes guardados vuelven a estar en tus conexiones." },
            new string[] { "Cancelled", "Geannuleerd", "Abgebrochen", "Annulé", "Cancelado" },
            new string[] { "You closed the Windows permission window. Nothing was changed.", "Je hebt het toestemmingsvenster van Windows gesloten. Er is niets veranderd.", "Sie haben das Berechtigungsfenster von Windows geschlossen. Es wurde nichts geändert.", "Vous avez fermé la fenêtre d'autorisation de Windows. Rien n'a été modifié.", "Cerraste la ventana de permiso de Windows. No se cambió nada." },
            new string[] { "It timed out waiting for the Windows permission window.", "Het wachten op het toestemmingsvenster van Windows duurde te lang.", "Das Warten auf das Berechtigungsfenster von Windows hat zu lange gedauert.", "L'attente de la fenêtre d'autorisation de Windows a expiré.", "Se agotó el tiempo de espera de la ventana de permiso de Windows." },
            new string[] { "The permission step did not report back.", "De toestemmingsstap meldde niets terug.", "Der Berechtigungsschritt hat nichts zurückgemeldet.", "L'étape d'autorisation n'a rien renvoyé.", "El paso de permiso no devolvió ningún resultado." },
            new string[] { "Cleaning the FiveM cache ...", "De FiveM-cache wordt opgeschoond ...", "Der FiveM-Cache wird bereinigt ...", "Nettoyage du cache FiveM ...", "Limpiando la caché de FiveM ..." },
            new string[] { "Removing {0} ...", "{0} wordt verwijderd ...", "{0} wird entfernt ...", "Suppression de {0} ...", "Eliminando {0} ..." },
            new string[] { "Cache cleaned", "Cache opgeschoond", "Cache bereinigt", "Cache nettoyé", "Caché limpiada" },
            new string[] { "About {0} was removed.", "Ongeveer {0} is verwijderd.", "Etwa {0} wurden entfernt.", "Environ {0} ont été supprimés.", "Se eliminaron unos {0}." },
            new string[] { "Some folders were not cleaned", "Sommige mappen zijn niet opgeschoond", "Einige Ordner wurden nicht bereinigt", "Certains dossiers n'ont pas été nettoyés", "Algunas carpetas no se limpiaron" },
            // undo, remembered builds, the report for support
            new string[] { "Undo last change", "Laatste wijziging ongedaan maken", "Letzte Änderung rückgängig", "Annuler la modification", "Deshacer el último cambio" },
            new string[] { "Put the cap back to what it was before the last change this tool made", "Zet de limiet terug naar wat het was voor de laatste wijziging van deze tool", "Das Limit auf den Wert vor der letzten Änderung dieses Werkzeugs zurücksetzen", "Remettre la limite comme avant la dernière modification de cet outil", "Dejar el límite como estaba antes del último cambio de esta herramienta" },
            new string[] { "Nothing to undo", "Niets om ongedaan te maken", "Nichts rückgängig zu machen", "Rien à annuler", "Nada que deshacer" },
            new string[] { "This tool has not changed the ticked builds yet, so it has nothing to put back.", "Deze tool heeft de aangevinkte builds nog niet veranderd, dus er is niets terug te zetten.", "Dieses Werkzeug hat die markierten Builds noch nicht geändert, es gibt also nichts zurückzusetzen.", "Cet outil n'a pas encore modifié les builds cochées, il n'y a donc rien à remettre.", "Esta herramienta aún no ha cambiado las builds marcadas, así que no hay nada que restaurar." },
            new string[] { "Put the cap back to what it was before the last change for {0} build(s)?\r\n\r\nRadeon Software will close for a short moment and open again.\r\nYour old settings file is backed up first.", "De limiet terugzetten naar wat het was voor de laatste wijziging, voor {0} build(s)?\r\n\r\nRadeon Software wordt even gesloten en daarna weer geopend.\r\nJe oude instellingenbestand wordt eerst opgeslagen als back-up.", "Das Limit für {0} Build(s) auf den Wert vor der letzten Änderung zurücksetzen?\r\n\r\nRadeon Software wird kurz geschlossen und danach wieder geöffnet.\r\nDie alte Einstellungsdatei wird zuvor gesichert.", "Remettre la limite comme avant la dernière modification pour {0} build(s) ?\r\n\r\nRadeon Software va se fermer un instant puis se rouvrir.\r\nVotre ancien fichier de réglages est d'abord sauvegardé.", "¿Dejar el límite como estaba antes del último cambio para {0} build(s)?\r\n\r\nRadeon Software se cerrará un momento y se volverá a abrir.\r\nTu archivo de ajustes anterior se guarda antes como copia." },
            new string[] { "Putting the cap back ...", "De limiet wordt teruggezet ...", "Das Limit wird zurückgesetzt ...", "Rétablissement de la limite ...", "Restaurando el límite ..." },
            new string[] { "Cap put back", "Limiet teruggezet", "Limit zurückgesetzt", "Limite rétablie", "Límite restaurado" },
            new string[] { "Cap put back.", "Limiet teruggezet.", "Limit zurückgesetzt.", "Limite rétablie.", "Límite restaurado." },
            new string[] { "Nothing to undo: the cap is already back.", "Niets om ongedaan te maken: de limiet is al terug.", "Nichts rückgängig zu machen: Das Limit ist schon zurück.", "Rien à annuler : la limite est déjà rétablie.", "Nada que deshacer: el límite ya está restaurado." },
            new string[] { "The cap was already back for {0} build(s).", "De limiet was al terug voor {0} build(s).", "Das Limit war für {0} Build(s) schon zurück.", "La limite était déjà rétablie pour {0} build(s).", "El límite ya estaba restaurado para {0} build(s)." },
            new string[] { "Put back {0} build(s). Radeon Software was restarted.", "{0} build(s) teruggezet. Radeon Software is opnieuw gestart.", "{0} Build(s) zurückgesetzt. Radeon Software wurde neu gestartet.", "{0} build(s) rétablie(s). Radeon Software a été redémarré.", "{0} build(s) restaurada(s). Se reinició Radeon Software." },
            new string[] { " (not on disk now)", " (nu niet op schijf)", " (nicht auf dem Datenträger)", " (pas sur le disque)", " (no está en disco)" },
            new string[] { "{0} remembered from before (not on disk now).", "{0} onthouden van eerder (nu niet op schijf).", "{0} von früher gemerkt (derzeit nicht auf dem Datenträger).", "{0} mémorisée(s) d'avant (absentes du disque pour l'instant).", "{0} recordada(s) de antes (ahora no están en el disco)." },
            new string[] { "Copy a report for support: the whole log with all details, ready to paste", "Kopieer een rapport voor hulp: het hele logboek met alle details, klaar om te plakken", "Einen Bericht für den Support kopieren: das ganze Protokoll mit allen Details, bereit zum Einfügen", "Copier un rapport pour l'assistance : tout le journal avec tous les détails, prêt à coller", "Copiar un informe para soporte: todo el registro con todos los detalles, listo para pegar" },
            new string[] { "Report copied", "Rapport gekopieerd", "Bericht kopiert", "Rapport copié", "Informe copiado" },
            new string[] { "The whole log is on your clipboard. Paste it in a message. It has file paths and your PC's name in it.", "Het hele logboek staat op je klembord. Plak het in een bericht. Er staan bestandspaden en de naam van je pc in.", "Das ganze Protokoll liegt in der Zwischenablage. Fügen Sie es in eine Nachricht ein. Es enthält Dateipfade und den Namen Ihres PCs.", "Tout le journal est dans le presse-papiers. Collez-le dans un message. Il contient des chemins de fichiers et le nom de votre PC.", "Todo el registro está en el portapapeles. Pégalo en un mensaje. Contiene rutas de archivos y el nombre de tu PC." },
            new string[] { "Driver", "Stuurprogramma", "Treiber", "Pilote", "Controlador" },
            new string[] { "Cap put back to {0} / {1}.", "Limiet teruggezet naar {0} / {1}.", "Limit auf {0} / {1} zurückgesetzt.", "Limite rétablie à {0} / {1}.", "Límite restaurado a {0} / {1}." },
            new string[] { "Put back {0} build(s) to {1} FPS. Radeon Software was restarted.", "{0} build(s) teruggezet naar {1} FPS. Radeon Software is opnieuw gestart.", "{0} Build(s) auf {1} FPS zurückgesetzt. Radeon Software wurde neu gestartet.", "{0} build(s) rétablie(s) à {1} FPS. Radeon Software a été redémarré.", "{0} build(s) restaurada(s) a {1} FPS. Se reinició Radeon Software." },
            new string[] { "New FiveM build found", "Nieuwe FiveM-build gevonden", "Neue FiveM-Build gefunden", "Nouvelle build FiveM trouvée", "Nueva build de FiveM encontrada" },
            new string[] { "Its status says it is not in Radeon Software yet. Add it there once (Gaming > Games), then press Search again.", "De status zegt dat hij nog niet in Radeon Software staat. Voeg hem daar één keer toe (Gaming > Games) en druk daarna op Opnieuw zoeken.", "Der Status sagt, dass sie noch nicht in Radeon Software ist. Fügen Sie sie dort einmal hinzu (Gaming > Games) und drücken Sie dann Erneut suchen.", "Son état indique qu'elle n'est pas encore dans Radeon Software. Ajoutez-la une fois là-bas (Gaming > Games), puis appuyez sur Rechercher à nouveau.", "Su estado indica que aún no está en Radeon Software. Añádela allí una vez (Gaming > Games) y luego pulsa Buscar de nuevo." },
            new string[] { "Network card", "Netwerkkaart", "Netzwerkkarte", "Carte réseau", "Tarjeta de red" },
            new string[] { "Speed", "Snelheid", "Geschwindigkeit", "Vitesse", "Velocidad" },
            new string[] { "Windows holds {0} names in the DNS cache right now.\r\n\r\nClear them?", "Windows heeft nu {0} namen in de DNS-cache.\r\n\r\nWissen?", "Windows hat gerade {0} Namen im DNS-Cache.\r\n\r\nLöschen?", "Windows garde actuellement {0} noms dans le cache DNS.\r\n\r\nLes vider ?", "Windows tiene ahora {0} nombres en la caché DNS.\r\n\r\n¿Borrarlos?" },
            new string[] { "The DNS cache will be cleared. Continue?", "De DNS-cache wordt gewist. Doorgaan?", "Der DNS-Cache wird geleert. Fortfahren?", "Le cache DNS va être vidé. Continuer ?", "Se borrará la caché DNS. ¿Continuar?" },
            new string[] { "{0} names in the cache. Waiting for your answer ...", "{0} namen in de cache. Wachten op je antwoord ...", "{0} Namen im Cache. Warte auf Ihre Antwort ...", "{0} noms dans le cache. En attente de votre réponse ...", "{0} nombres en la caché. Esperando tu respuesta ..." },
            new string[] { "Counting the names in the cache ...", "De namen in de cache worden geteld ...", "Die Namen im Cache werden gezählt ...", "Comptage des noms dans le cache ...", "Contando los nombres de la caché ..." },
            new string[] { "Nothing was cleared.", "Er is niets gewist.", "Es wurde nichts geleert.", "Rien n'a été vidé.", "No se borró nada." },
            new string[] { "administrator", "beheerder", "Administrator", "administrateur", "administrador" },
            new string[] { "Running as administrator: no permission windows for the DNS tools", "Draait als beheerder: geen toestemmingsvensters meer voor de DNS-tools", "Läuft als Administrator: keine Berechtigungsfenster mehr für die DNS-Werkzeuge", "Exécuté en administrateur : plus de fenêtres d'autorisation pour les outils DNS", "Se ejecuta como administrador: sin ventanas de permiso para las herramientas DNS" },
            new string[] { "Restart as administrator (Windows asks once), so the DNS tools never ask again", "Opnieuw starten als beheerder (Windows vraagt één keer), zodat de DNS-tools niet meer vragen", "Als Administrator neu starten (Windows fragt einmal), damit die DNS-Werkzeuge nicht mehr fragen", "Redémarrer en administrateur (Windows demande une fois), pour que les outils DNS ne demandent plus", "Reiniciar como administrador (Windows pregunta una vez), para que las herramientas DNS no vuelvan a preguntar" },
            new string[] { "Counting needs administrator", "Tellen kan alleen als beheerder", "Zählen geht nur als Administrator", "Le comptage demande l'administrateur", "Contar requiere administrador" },
            new string[] { "Cache size: unknown", "Cachegrootte: onbekend", "Cache-Größe: unbekannt", "Taille du cache : inconnue", "Tamaño de la caché: desconocido" },
            new string[] { "Now in the cache: {0} names", "Nu in de cache: {0} namen", "Jetzt im Cache: {0} Namen", "Actuellement dans le cache : {0} noms", "Ahora en la caché: {0} nombres" },
            new string[] { "Administrator rights: the DNS tools need them (clearing the DNS cache and changing the DNS). Windows then asks for permission. To stop that, press the shield button at the top right: the program restarts as administrator and does not ask again. The frame cap and the FiveM cache do not need them.", "Beheerdersrechten: de DNS-tools hebben ze nodig (de DNS-cache wissen en de DNS wijzigen). Windows vraagt dan om toestemming. Wil je dat niet? Druk op de schildknop rechtsboven: het programma start opnieuw als beheerder en vraagt het niet meer. De FPS-limiet en de FiveM-cache hebben het niet nodig.", "Administratorrechte: Die DNS-Werkzeuge brauchen sie (den DNS-Cache leeren und das DNS ändern). Windows fragt dann nach Erlaubnis. Um das zu vermeiden, drücken Sie die Schild-Schaltfläche oben rechts: Das Programm startet als Administrator neu und fragt nicht mehr. Das FPS-Limit und der FiveM-Cache brauchen sie nicht.", "Droits d'administrateur : les outils DNS en ont besoin (vider le cache DNS et changer le DNS). Windows demande alors l'autorisation. Pour éviter cela, appuyez sur le bouton bouclier en haut à droite : le programme redémarre en administrateur et ne demande plus. La limite de FPS et le cache FiveM n'en ont pas besoin.", "Permisos de administrador: las herramientas DNS los necesitan (borrar la caché DNS y cambiar el DNS). Windows pide permiso entonces. Para evitarlo, pulsa el botón del escudo arriba a la derecha: el programa se reinicia como administrador y no vuelve a preguntar. El límite de FPS y la caché de FiveM no los necesitan." },
            // welcome window (new wording)
            new string[] { "Frame cap: sets a frame cap for every FiveM build in your graphics driver (AMD and NVIDIA).", "FPS-limiet: stelt in je videokaartstuurprogramma een FPS-limiet in voor elke FiveM-build (AMD en NVIDIA).", "FPS-Limit: legt in Ihrem Grafiktreiber ein FPS-Limit für jede FiveM-Build fest (AMD und NVIDIA).", "Limite de FPS : règle une limite de FPS pour chaque build FiveM dans votre pilote graphique (AMD et NVIDIA).", "Límite de FPS: establece un límite de FPS para cada build de FiveM en tu controlador gráfico (AMD y NVIDIA)." },
            new string[] { "DNS tools: clears the DNS cache, and can also change your DNS settings if you still have connection problems. You can always undo it.", "DNS-tools: wist de DNS-cache en kan ook je DNS-instellingen wijzigen als je nog steeds verbindingsproblemen hebt. Je kunt het altijd ongedaan maken.", "DNS-Werkzeuge: leeren den DNS-Cache und können auch Ihre DNS-Einstellungen ändern, wenn Sie weiterhin Verbindungsprobleme haben. Sie können es jederzeit rückgängig machen.", "Outils DNS : vident le cache DNS et peuvent aussi changer vos réglages DNS si vous avez encore des problèmes de connexion. Vous pouvez toujours annuler.", "Herramientas DNS: borran la caché DNS y también pueden cambiar tu configuración DNS si aún tienes problemas de conexión. Siempre puedes deshacerlo." },
            new string[] { "FiveM cache: clears FiveM's temporary files. The deep clean also signs you out of FiveM and removes saved game and web data, but it does not touch your key binds or your graphics settings. The separate Reset removes those too, after a big warning.", "FiveM-cache: wist de tijdelijke bestanden van FiveM. De diepe schoonmaak logt je ook uit bij FiveM en verwijdert opgeslagen game- en webgegevens, maar raakt je keybinds en grafische instellingen niet aan. De aparte Reset verwijdert die ook, na een grote waarschuwing.", "FiveM-Cache: löscht die temporären Dateien von FiveM. Die Tiefenreinigung meldet Sie auch bei FiveM ab und entfernt gespeicherte Spiel- und Webdaten, berührt aber weder Ihre Tastenbelegung noch Ihre Grafikeinstellungen. Das separate Zurücksetzen entfernt auch diese, nach einer deutlichen Warnung.", "Cache FiveM : supprime les fichiers temporaires de FiveM. Le nettoyage en profondeur vous déconnecte aussi de FiveM et supprime les données de jeu et web enregistrées, mais ne touche ni à vos touches ni à vos réglages graphiques. La réinitialisation séparée les supprime aussi, après un grand avertissement.", "Caché de FiveM: borra los archivos temporales de FiveM. La limpieza profunda también cierra tu sesión de FiveM y elimina datos guardados del juego y de la web, pero no toca tus teclas asignadas ni tus ajustes gráficos. El restablecimiento aparte también los elimina, tras una gran advertencia." },
            new string[] { "Graphics: checks two FiveM settings. Extended Texture Budget should be as high as your PC can take, or many MLOs pop in and out when a lot of them are close together. A high Post FX makes some MLOs and cars far too bright. Two switches fix the interface lag and the stutter at high frame rates.", "Graphics: controleert twee FiveM-instellingen. Extended Texture Budget hoort zo hoog te staan als je pc aankan, anders poppen veel MLO's in en uit beeld als er veel dicht bij elkaar staan. Een hoge Post FX maakt sommige MLO's en auto's veel te fel. Twee schakelaars verhelpen de interface-lag en het haperen bij hoge framerates.", "Grafik: prüft zwei FiveM-Einstellungen. Extended Texture Budget sollte so hoch sein, wie Ihr PC verträgt, sonst poppen viele MLOs ein und aus, wenn viele nah beieinander stehen. Ein hohes Post FX lässt manche MLOs und Autos viel zu hell wirken. Zwei Schalter beheben den Oberflächen-Lag und das Ruckeln bei hohen FPS.", "Graphismes : vérifie deux réglages de FiveM. Extended Texture Budget doit être aussi haut que votre PC le permet, sinon beaucoup de MLO apparaissent et disparaissent quand ils sont nombreux et proches. Un Post FX élevé rend certains MLO et certaines voitures bien trop lumineux. Deux interrupteurs corrigent le lag de l'interface et les saccades à haute fréquence d'images.", "Gráficos: revisa dos ajustes de FiveM. Extended Texture Budget debe estar tan alto como aguante tu PC, o muchos MLO aparecen y desaparecen cuando hay muchos juntos. Un Post FX alto hace que algunos MLO y coches se vean demasiado brillantes. Dos interruptores corrigen el lag de la interfaz y los tirones con FPS altos." },
            new string[] { "Administrator rights: the DNS tools need administrator rights to work fully. Windows asks for permission, or press the shield button at the top right to restart as administrator once and never be asked again. The frame cap, the graphics settings and the FiveM cache do not need it.", "Beheerdersrechten: de DNS-tools hebben beheerdersrechten nodig om volledig te functioneren. Windows vraagt om toestemming, of druk op de schildknop rechtsboven om één keer als beheerder opnieuw te starten en nooit meer iets te hoeven bevestigen. De FPS-limiet, de graphics-instellingen en de FiveM-cache hebben dat niet nodig.", "Administratorrechte: Die DNS-Werkzeuge brauchen Administratorrechte, um voll zu funktionieren. Windows fragt nach Erlaubnis, oder drücken Sie die Schild-Schaltfläche oben rechts, um einmal als Administrator neu zu starten und nie mehr gefragt zu werden. Das FPS-Limit, die Grafikeinstellungen und der FiveM-Cache brauchen das nicht.", "Droits d'administrateur : les outils DNS ont besoin des droits d'administrateur pour fonctionner pleinement. Windows demande l'autorisation, ou appuyez sur le bouton bouclier en haut à droite pour redémarrer une fois en administrateur et ne plus jamais être sollicité. La limite de FPS, les réglages graphiques et le cache FiveM n'en ont pas besoin.", "Permisos de administrador: las herramientas DNS necesitan permisos de administrador para funcionar del todo. Windows pide permiso, o pulsa el botón del escudo arriba a la derecha para reiniciar una vez como administrador y que no vuelva a preguntar. El límite de FPS, los ajustes gráficos y la caché de FiveM no los necesitan." },
            new string[] { "Where it is possible, the old setting is saved or backed up first.", "Waar het kan, wordt de oude instelling eerst opgeslagen of er wordt een back-up gemaakt.", "Wo es möglich ist, wird die alte Einstellung zuerst gespeichert oder gesichert.", "Quand c'est possible, l'ancien réglage est d'abord enregistré ou sauvegardé.", "Cuando es posible, el ajuste anterior se guarda o se copia antes." },

            // the deep clean says what it really does
            new string[] { "Login and saved data (deep clean)", "Inlog en opgeslagen data (diep)", "Anmeldung und Daten (tief)", "Connexion et données (profond)", "Sesión y datos (profunda)" },
            new string[] { "You have to log in to FiveM again. Your key binds and graphics settings stay. Use it only when the normal clean did not help.", "Je moet opnieuw inloggen bij FiveM. Je keybinds en grafische instellingen blijven staan. Gebruik het alleen als het gewone opschonen niet hielp.", "Sie müssen sich bei FiveM neu anmelden. Ihre Tastenbelegung und Grafikeinstellungen bleiben erhalten. Nur verwenden, wenn die normale Bereinigung nicht geholfen hat.", "Vous devrez vous reconnecter à FiveM. Vos touches et vos réglages graphiques restent. À utiliser seulement si le nettoyage normal n'a pas aidé.", "Tendrás que volver a iniciar sesión en FiveM. Tus teclas asignadas y tus ajustes gráficos se quedan. Úsalo solo si la limpieza normal no ayudó." },
            new string[] { "Deep clean also removes your saved FiveM login and other saved data, as well as the cache.\r\n\r\nYou will have to log in to FiveM again. Your key binds and graphics settings are not touched.\r\n\r\nUse it only when the normal clean did not help. Include it anyway?", "De diepe schoonmaak verwijdert ook je opgeslagen FiveM-inlog en andere opgeslagen gegevens, naast de cache.\r\n\r\nJe moet opnieuw inloggen bij FiveM. Je keybinds en grafische instellingen blijven onaangeroerd.\r\n\r\nGebruik het alleen als het gewone opschonen niet hielp. Toch meenemen?", "Die Tiefenreinigung entfernt neben dem Cache auch Ihre gespeicherte FiveM-Anmeldung und andere gespeicherte Daten.\r\n\r\nSie müssen sich bei FiveM neu anmelden. Ihre Tastenbelegung und Grafikeinstellungen bleiben unberührt.\r\n\r\nNur verwenden, wenn die normale Bereinigung nicht geholfen hat. Trotzdem einbeziehen?", "Le nettoyage en profondeur supprime aussi votre connexion FiveM enregistrée et d'autres données enregistrées, en plus du cache.\r\n\r\nVous devrez vous reconnecter à FiveM. Vos touches et vos réglages graphiques ne sont pas touchés.\r\n\r\nN'utilisez-le que si le nettoyage normal n'a pas aidé. L'inclure quand même ?", "La limpieza profunda también elimina tu inicio de sesión de FiveM guardado y otros datos guardados, además de la caché.\r\n\r\nTendrás que volver a iniciar sesión en FiveM. Tus teclas asignadas y tus ajustes gráficos no se tocan.\r\n\r\nÚsala solo si la limpieza normal no ayudó. ¿Incluirla de todos modos?" },

            // start window
            new string[] { "Graphics", "Graphics", "Grafik", "Graphismes", "Gráficos" },
            new string[] { "Texture Budget, Post FX, and fixes for UI lag and stutter", "Texture Budget, Post FX en oplossingen voor lag en haperen", "Texture Budget, Post FX und Korrekturen für UI-Lag und Ruckeln", "Texture Budget, Post FX et corrections du lag et des saccades", "Texture Budget, Post FX y arreglos del lag y los tirones" },
            new string[] { "Start as administrator", "Starten als beheerder", "Als Administrator starten", "Démarrer en administrateur", "Iniciar como administrador" },
            new string[] { "Windows asks once. Then the DNS tools never ask for permission again.", "Windows vraagt één keer. Daarna vragen de DNS-tools nooit meer om toestemming.", "Windows fragt einmal. Danach fragen die DNS-Werkzeuge nie wieder nach Erlaubnis.", "Windows demande une fois. Ensuite les outils DNS ne demandent plus jamais d'autorisation.", "Windows pregunta una vez. Después las herramientas DNS nunca vuelven a pedir permiso." },

            // the live meter and the Intel Arc rule
            new string[] { "FiveM uses {0} of your {1} video memory ({2} %). Highest this session: {3}.",
                           "FiveM gebruikt {0} van je {1} videogeheugen ({2} %). Hoogste in deze sessie: {3}.",
                           "FiveM nutzt {0} Ihres Videospeichers von {1} ({2} %). Höchster Wert in dieser Sitzung: {3}.",
                           "FiveM utilise {0} de vos {1} de mémoire vidéo ({2} %). Maximum de cette session : {3}.",
                           "FiveM usa {0} de tus {1} de memoria de vídeo ({2} %). Máximo de esta sesión: {3}." },
            new string[] { "FiveM uses {0} of video memory.", "FiveM gebruikt {0} videogeheugen.", "FiveM nutzt {0} Videospeicher.", "FiveM utilise {0} de mémoire vidéo.", "FiveM usa {0} de memoria de vídeo." },
            new string[] { "FiveM is not running. Start it and join a server: this line then shows how much video memory it really uses.",
                           "FiveM draait niet. Start het en ga een server in: deze regel toont dan hoeveel videogeheugen het echt gebruikt.",
                           "FiveM läuft nicht. Starten Sie es und betreten Sie einen Server: Diese Zeile zeigt dann, wie viel Videospeicher es wirklich nutzt.",
                           "FiveM n'est pas lancé. Démarrez-le et rejoignez un serveur : cette ligne montrera alors la mémoire vidéo réellement utilisée.",
                           "FiveM no está abierto. Ábrelo y entra en un servidor: esta línea mostrará entonces cuánta memoria de vídeo usa de verdad." },
            new string[] { "FiveM is not running. Last time: highest {0} of {1} ({2} %).",
                           "FiveM draait niet. De vorige keer: hoogste {0} van {1} ({2} %).",
                           "FiveM läuft nicht. Letztes Mal: höchster Wert {0} von {1} ({2} %).",
                           "FiveM n'est pas lancé. La dernière fois : maximum {0} sur {1} ({2} %).",
                           "FiveM no está abierto. La última vez: máximo {0} de {1} ({2} %)." },
            new string[] { "FiveM is running, but Windows gives no video memory numbers for it (it needs Windows 10 version 1709 or newer).",
                           "FiveM draait, maar Windows geeft er geen videogeheugengetallen voor (daarvoor is Windows 10 versie 1709 of nieuwer nodig).",
                           "FiveM läuft, aber Windows liefert dafür keine Videospeicher-Zahlen (dafür braucht es Windows 10 Version 1709 oder neuer).",
                           "FiveM est lancé, mais Windows ne donne pas de chiffres de mémoire vidéo pour lui (il faut Windows 10 version 1709 ou plus récent).",
                           "FiveM está abierto, pero Windows no da cifras de memoria de vídeo para él (hace falta Windows 10 versión 1709 o más reciente)." },
            new string[] { "FiveM runs mostly on another graphics chip, not on your dedicated card. Its memory use there: {0}.",
                           "FiveM draait vooral op een andere grafische chip, niet op je dedicated kaart. Gebruik daar: {0}.",
                           "FiveM läuft hauptsächlich auf einem anderen Grafikchip, nicht auf Ihrer dedizierten Karte. Nutzung dort: {0}.",
                           "FiveM tourne surtout sur une autre puce graphique, pas sur votre carte dédiée. Utilisation là-bas : {0}.",
                           "FiveM funciona sobre todo en otro chip gráfico, no en tu tarjeta dedicada. Uso allí: {0}." },
            new string[] { "The real video memory that FiveM holds on your dedicated card, read from Windows every 2 seconds while this tab is open. The mark is at 80 %: above it less than 20 % of the card is free.",
                           "Het echte videogeheugen dat FiveM op je dedicated kaart vasthoudt, elke 2 seconden uit Windows gelezen zolang dit tabblad open is. De streep staat op 80 %: erboven is minder dan 20 % van de kaart vrij.",
                           "Der echte Videospeicher, den FiveM auf Ihrer dedizierten Karte belegt, alle 2 Sekunden aus Windows gelesen, solange dieser Tab offen ist. Die Marke liegt bei 80 %: darüber sind weniger als 20 % der Karte frei.",
                           "La vraie mémoire vidéo que FiveM occupe sur votre carte dédiée, lue dans Windows toutes les 2 secondes tant que cet onglet est ouvert. Le repère est à 80 % : au-delà, moins de 20 % de la carte est libre.",
                           "La memoria de vídeo real que FiveM ocupa en tu tarjeta dedicada, leída de Windows cada 2 segundos mientras esta pestaña está abierta. La marca está en el 80 %: por encima queda libre menos del 20 % de la tarjeta." },
            new string[] { "Not supported at the moment for an Intel Arc card, because it could not be tested.",
                           "Op dit moment niet ondersteund voor een Intel Arc-kaart, omdat het niet getest kon worden.",
                           "Zurzeit nicht unterstützt für eine Intel-Arc-Karte, weil es nicht getestet werden konnte.",
                           "Non pris en charge pour le moment avec une carte Intel Arc, car cela n'a pas pu être testé.",
                           "No compatible por ahora con una tarjeta Intel Arc, porque no se pudo probar." },
            new string[] { "Intel Arc (a dedicated Intel card) was found. It is not supported at the moment, because it could not be tested.",
                           "Intel Arc (een dedicated Intel-kaart) gevonden. Het wordt op dit moment niet ondersteund, omdat het niet getest kon worden.",
                           "Intel Arc (eine dedizierte Intel-Karte) gefunden. Sie wird zurzeit nicht unterstützt, weil sie nicht getestet werden konnte.",
                           "Intel Arc (une carte Intel dédiée) trouvée. Elle n'est pas prise en charge pour le moment, car elle n'a pas pu être testée.",
                           "Se encontró Intel Arc (una tarjeta Intel dedicada). No es compatible por ahora, porque no se pudo probar." },
            new string[] { "An Intel Arc card was also found: not supported at the moment.",
                           "Er is ook een Intel Arc-kaart gevonden: op dit moment niet ondersteund.",
                           "Es wurde auch eine Intel-Arc-Karte gefunden: zurzeit nicht unterstützt.",
                           "Une carte Intel Arc a aussi été trouvée : non prise en charge pour le moment.",
                           "También se encontró una tarjeta Intel Arc: no compatible por ahora." },

            // the advice for the Extended Texture Budget
            new string[] { "Recommended ({0} %)", "Aanbevolen ({0} %)", "Empfohlen ({0} %)", "Recommandé ({0} %)", "Recomendado ({0} %)" },
            new string[] { "Leaves about 20 % of the video memory of your card free. It is an estimate: your other graphics settings change the real number.",
                           "Laat ongeveer 20 % van het videogeheugen van je kaart vrij. Het is een schatting: je andere grafische instellingen veranderen het echte getal.",
                           "Lässt etwa 20 % des Videospeichers Ihrer Karte frei. Es ist eine Schätzung: Ihre anderen Grafikeinstellungen ändern den echten Wert.",
                           "Laisse environ 20 % de la mémoire vidéo de votre carte libre. C'est une estimation : vos autres réglages graphiques changent le chiffre réel.",
                           "Deja libre alrededor del 20 % de la memoria de vídeo de tu tarjeta. Es una estimación: tus otros ajustes gráficos cambian la cifra real." },
            new string[] { "Your card has {0}. At {1} % the game's memory bar should show about {2}, which leaves about {3} % free.",
                           "Je kaart heeft {0}. Bij {1} % hoort de geheugenbalk van de game ongeveer {2} te tonen, dus ongeveer {3} % blijft vrij.",
                           "Ihre Karte hat {0}. Bei {1} % sollte die Speicheranzeige des Spiels etwa {2} zeigen, etwa {3} % bleiben frei.",
                           "Votre carte a {0}. À {1} %, la jauge mémoire du jeu devrait afficher environ {2}, soit environ {3} % de libre.",
                           "Tu tarjeta tiene {0}. Con {1} %, la barra de memoria del juego debería mostrar unos {2}, y quedaría libre un {3} % aproximadamente." },
            new string[] { "Too high: at {1} % the game's memory bar would show about {2}, and your card has {0}. FiveM will likely stutter or crash.",
                           "Te hoog: bij {1} % zou de geheugenbalk van de game ongeveer {2} tonen, en je kaart heeft {0}. FiveM gaat waarschijnlijk haperen of crashen.",
                           "Zu hoch: Bei {1} % würde die Speicheranzeige des Spiels etwa {2} zeigen, und Ihre Karte hat {0}. FiveM wird wahrscheinlich ruckeln oder abstürzen.",
                           "Trop haut : à {1} %, la jauge mémoire du jeu afficherait environ {2}, et votre carte a {0}. FiveM va probablement saccader ou planter.",
                           "Demasiado alto: con {1} %, la barra de memoria del juego mostraría unos {2}, y tu tarjeta tiene {0}. FiveM probablemente irá a tirones o se cerrará." },
            new string[] { "Above {0} % less than 20 % of your video memory stays free. FiveM can stutter or crash.",
                           "Boven {0} % blijft minder dan 20 % van je videogeheugen vrij. FiveM kan haperen of crashen.",
                           "Über {0} % bleiben weniger als 20 % Ihres Videospeichers frei. FiveM kann ruckeln oder abstürzen.",
                           "Au-delà de {0} %, moins de 20 % de votre mémoire vidéo reste libre. FiveM peut saccader ou planter.",
                           "Por encima de {0} % queda libre menos del 20 % de tu memoria de vídeo. FiveM puede ir a tirones o cerrarse." },

            // the two game options of the graphics tab
            new string[] { "FiveM game options", "FiveM-spelopties", "FiveM-Spieloptionen", "Options de jeu FiveM", "Opciones de juego de FiveM" },
            new string[] { "Fix interface lag (NUI in-process GPU)", "Interface-lag verhelpen (NUI-GPU in het spelproces)", "Oberflächen-Lag beheben (NUI-GPU im Spielprozess)", "Corriger le lag de l'interface (GPU NUI dans le jeu)", "Arreglar el lag de la interfaz (GPU de NUI en el juego)" },
            new string[] { "Fixes 'UI lag' when the GPU is busy. FiveM warns that it may be unstable and cause GPU crashes.",
                           "Verhelpt 'lag' in de interface als de GPU druk bezig is. FiveM waarschuwt dat het instabiel kan zijn en GPU-crashes kan geven.",
                           "Behebt 'UI-Lag', wenn die GPU stark ausgelastet ist. FiveM warnt, dass es instabil sein und GPU-Abstürze verursachen kann.",
                           "Corrige le « lag d'interface » quand le GPU est très sollicité. FiveM prévient que cela peut être instable et provoquer des plantages du GPU.",
                           "Corrige el 'lag' de la interfaz cuando la GPU está muy ocupada. FiveM avisa de que puede ser inestable y causar cierres de la GPU." },
            new string[] { "Fix stutter at high frame rates", "Haperen bij hoge framerates verhelpen", "Ruckeln bei hohen FPS beheben", "Corriger les saccades en haute fréquence d'images", "Arreglar los tirones con FPS altos" },
            new string[] { "Turns FiveM's audio frame limiter off, which stops hitches at high frame rates.",
                           "Zet de audio-framebegrenzer van FiveM uit, wat haperingen bij hoge framerates voorkomt.",
                           "Schaltet den Audio-Frame-Limiter von FiveM aus, was Ruckler bei hohen FPS verhindert.",
                           "Désactive le limiteur de trame audio de FiveM, ce qui évite les saccades à haute fréquence d'images.",
                           "Desactiva el limitador de fotogramas de audio de FiveM, lo que evita los tirones con FPS altos." },
            new string[] { "Needs a restart of FiveM. In FiveM's own settings this is the box \"NUI in-process GPU\".",
                           "FiveM moet opnieuw worden gestart. In de eigen instellingen van FiveM is dit het vakje \"NUI-GPU in het spelproces\".",
                           "FiveM muss neu gestartet werden. In den FiveM-Einstellungen ist das die Option \"NUI-Beschleunigung\".",
                           "FiveM doit être redémarré. Dans les réglages de FiveM, c'est la case \"GPU NUI dans le processus du jeu\".",
                           "Hay que reiniciar FiveM. En los ajustes de FiveM es la casilla \"GPU de NUI en el proceso del juego\"." },
            new string[] { "In FiveM's own settings this is the box \"Audio frame limiter\". It is unchecked when this switch is on.",
                           "In de eigen instellingen van FiveM is dit het vakje \"Audio-framebegrenzer\". Het is uitgevinkt als deze schakelaar aan staat.",
                           "In den FiveM-Einstellungen ist das die Option \"Audioframe-Limitierung\". Sie ist abgewählt, wenn dieser Schalter an ist.",
                           "Dans les réglages de FiveM, c'est la case \"Limiteur de trame audio\". Elle est décochée quand cet interrupteur est activé.",
                           "En los ajustes de FiveM es la casilla \"Limitador de fotogramas de audio\". Está desmarcada cuando este interruptor está activado." },
            new string[] { "On", "Aan", "An", "Activé", "Activado" },
            new string[] { "Off", "Uit", "Aus", "Désactivé", "Desactivado" },
            new string[] { "The interface lag fix is now on.", "De interface-lag-oplossing staat nu aan.", "Die Oberflächen-Lag-Korrektur ist jetzt an.", "La correction du lag de l'interface est maintenant activée.", "La corrección del lag de la interfaz está ahora activada." },
            new string[] { "The interface lag fix is now off.", "De interface-lag-oplossing staat nu uit.", "Die Oberflächen-Lag-Korrektur ist jetzt aus.", "La correction du lag de l'interface est maintenant désactivée.", "La corrección del lag de la interfaz está ahora desactivada." },
            new string[] { "The stutter fix is now on.", "De haperingsoplossing staat nu aan.", "Die Ruckel-Korrektur ist jetzt an.", "La correction des saccades est maintenant activée.", "La corrección de los tirones está ahora activada." },
            new string[] { "The stutter fix is now off.", "De haperingsoplossing staat nu uit.", "Die Ruckel-Korrektur ist jetzt aus.", "La correction des saccades est maintenant désactivée.", "La corrección de los tirones está ahora desactivada." },

            // graphics tab
            new string[] { "Extended Texture Budget", "Extended Texture Budget", "Extended Texture Budget", "Extended Texture Budget", "Extended Texture Budget" },
            new string[] { "Post FX", "Post FX", "Post FX", "Post FX", "Post FX" },
            new string[] { "How much video memory FiveM may use for server textures (maps, MLOs, cars). If it is too low, many MLOs pop in and out when a lot of them are close together. Set it as high as your graphics card can take.", "Hoeveel videogeheugen FiveM mag gebruiken voor server-textures (maps, MLO's, auto's). Staat het te laag, dan poppen veel MLO's in en uit beeld als er veel dicht bij elkaar staan. Zet het zo hoog als je videokaart aankan.", "Wie viel Videospeicher FiveM für Server-Texturen (Maps, MLOs, Autos) nutzen darf. Ist der Wert zu niedrig, poppen viele MLOs ein und aus, wenn viele nah beieinander stehen. Stellen Sie ihn so hoch ein, wie Ihre Grafikkarte verträgt.", "La quantité de mémoire vidéo que FiveM peut utiliser pour les textures du serveur (cartes, MLO, voitures). Si elle est trop basse, beaucoup de MLO apparaissent et disparaissent quand ils sont nombreux et proches. Réglez-la aussi haut que votre carte graphique le permet.", "Cuánta memoria de vídeo puede usar FiveM para las texturas del servidor (mapas, MLO, coches). Si es demasiado baja, muchos MLO aparecen y desaparecen cuando hay muchos juntos. Ponla tan alta como aguante tu tarjeta gráfica." },
            new string[] { "Higher uses more video memory. If FiveM stutters or crashes, go a bit lower.", "Hoger gebruikt meer videogeheugen. Haperen of crashen gaat FiveM, zet het dan iets lager.", "Höher verbraucht mehr Videospeicher. Wenn FiveM ruckelt oder abstürzt, gehen Sie etwas niedriger.", "Plus haut utilise plus de mémoire vidéo. Si FiveM saccade ou plante, baissez un peu.", "Más alto usa más memoria de vídeo. Si FiveM va a tirones o se cierra, bájalo un poco." },
            new string[] { "Highest (100)", "Hoogste (100)", "Maximum (100)", "Maximum (100)", "Máximo (100)" },
            new string[] { "Now in the file: {0} %", "Nu in het bestand: {0} %", "Jetzt in der Datei: {0} %", "Actuellement dans le fichier : {0} %", "Ahora en el archivo: {0} %" },
            new string[] { "Now in the file: {0}", "Nu in het bestand: {0}", "Jetzt in der Datei: {0}", "Actuellement dans le fichier : {0}", "Ahora en el archivo: {0}" },
            new string[] { "Not set in the file (FiveM uses its own default)", "Niet ingesteld in het bestand (FiveM gebruikt zijn eigen standaard)", "In der Datei nicht gesetzt (FiveM nutzt seinen Standard)", "Non défini dans le fichier (FiveM utilise sa valeur par défaut)", "No está en el archivo (FiveM usa su valor por defecto)" },
            new string[] { "Settings file not found: {0}", "Instellingenbestand niet gevonden: {0}", "Einstellungsdatei nicht gefunden: {0}", "Fichier de réglages introuvable : {0}", "No se encontró el archivo de ajustes: {0}" },
            new string[] { "Post FX adds glow and strong light effects. At high levels some MLOs and cars look far too bright. Normal gives the plain look.", "Post FX voegt gloed en sterke lichteffecten toe. Op hoge niveaus zien sommige MLO's en auto's er veel te fel uit. Normaal geeft het gewone beeld.", "Post FX fügt Leuchten und starke Lichteffekte hinzu. Auf hohen Stufen wirken manche MLOs und Autos viel zu hell. Normal ergibt das schlichte Bild.", "Post FX ajoute de la lueur et de forts effets de lumière. À haut niveau, certains MLO et certaines voitures paraissent bien trop lumineux. Normal donne l'aspect simple.", "Post FX añade brillo y efectos de luz fuertes. En niveles altos algunos MLO y coches se ven demasiado brillantes. Normal da el aspecto sencillo." },
            new string[] { "Normal", "Normaal", "Normal", "Normal", "Normal" },
            new string[] { "High", "Hoog", "Hoch", "Élevé", "Alto" },
            new string[] { "Very high", "Zeer hoog", "Sehr hoch", "Très élevé", "Muy alto" },
            new string[] { "Ultra", "Ultra", "Ultra", "Ultra", "Ultra" },
            new string[] { "Set to Normal", "Op Normaal zetten", "Auf Normal setzen", "Mettre sur Normal", "Poner en Normal" },
            new string[] { "Save the settings", "Instellingen opslaan", "Einstellungen speichern", "Enregistrer les réglages", "Guardar los ajustes" },
            new string[] { "Open the settings folder", "Open de instellingenmap", "Einstellungsordner öffnen", "Ouvrir le dossier des réglages", "Abrir la carpeta de ajustes" },
            new string[] { "Each file gets a backup first. FiveM must be closed, because FiveM writes both files again when it closes.", "Elk bestand krijgt eerst een back-up. FiveM moet dicht zijn, want FiveM schrijft beide bestanden opnieuw als het afsluit.", "Jede Datei wird zuerst gesichert. FiveM muss geschlossen sein, weil FiveM beim Beenden beide Dateien neu schreibt.", "Chaque fichier est d'abord sauvegardé. FiveM doit être fermé, car FiveM réécrit les deux fichiers en se fermant.", "Cada archivo se copia antes. FiveM debe estar cerrado, porque FiveM vuelve a escribir los dos archivos al cerrarse." },
            new string[] { "FiveM is running ({0} process(es)). Close it before saving.", "FiveM draait ({0} proces(sen)). Sluit het voor het opslaan.", "FiveM läuft ({0} Prozess(e)). Schließen Sie es vor dem Speichern.", "FiveM est en cours d'exécution ({0} processus). Fermez-le avant d'enregistrer.", "FiveM se está ejecutando ({0} proceso(s)). Ciérralo antes de guardar." },
            new string[] { "Saving the settings ...", "Instellingen worden opgeslagen ...", "Einstellungen werden gespeichert ...", "Enregistrement des réglages ...", "Guardando los ajustes ..." },
            new string[] { "Settings saved", "Instellingen opgeslagen", "Einstellungen gespeichert", "Réglages enregistrés", "Ajustes guardados" },
            new string[] { "Extended Texture Budget is now {0} %.", "Extended Texture Budget staat nu op {0} %.", "Extended Texture Budget steht jetzt auf {0} %.", "Extended Texture Budget est maintenant à {0} %.", "Extended Texture Budget está ahora en {0} %." },
            new string[] { "Post FX is now {0}.", "Post FX staat nu op {0}.", "Post FX steht jetzt auf {0}.", "Post FX est maintenant sur {0}.", "Post FX está ahora en {0}." },
            new string[] { "FiveM reads it the next time it starts.", "FiveM leest het bij de volgende start.", "FiveM liest es beim nächsten Start.", "FiveM le lit au prochain démarrage.", "FiveM lo lee en el próximo inicio." },
            new string[] { "The settings already have these values.", "De instellingen hebben deze waarden al.", "Die Einstellungen haben diese Werte schon.", "Les réglages ont déjà ces valeurs.", "Los ajustes ya tienen estos valores." },
            new string[] { "Could not save a setting", "Een instelling opslaan is niet gelukt", "Eine Einstellung konnte nicht gespeichert werden", "Impossible d'enregistrer un réglage", "No se pudo guardar un ajuste" },
            new string[] { "Made for FiveM with the GTA V Legacy edition.", "Gemaakt voor FiveM met de GTA V Legacy-editie.", "Gemacht für FiveM mit der GTA-V-Legacy-Edition.", "Conçu pour FiveM avec l'édition Legacy de GTA V.", "Hecho para FiveM con la edición Legacy de GTA V." },
            new string[] { "These settings are for FiveM with the GTA V Legacy edition.", "Deze instellingen zijn voor FiveM met de GTA V Legacy-editie.", "Diese Einstellungen gelten für FiveM mit der GTA-V-Legacy-Edition.", "Ces réglages sont pour FiveM avec l'édition Legacy de GTA V.", "Estos ajustes son para FiveM con la edición Legacy de GTA V." },
            new string[] { "At Normal, Motion Blur Strength and In-Game Depth Of Field Effect are greyed out in the game's graphics menu. High, Very high and Ultra unlock them.", "Op Normaal zijn Motion Blur Strength en In-Game Depth Of Field Effect grijs in het grafische menu van de game. Hoog, Zeer hoog en Ultra ontgrendelen ze.", "Bei Normal sind Motion Blur Strength und In-Game Depth Of Field Effect im Grafikmenü des Spiels ausgegraut. Hoch, Sehr hoch und Ultra schalten sie frei.", "Avec Normal, Motion Blur Strength et In-Game Depth Of Field Effect sont grisés dans le menu graphique du jeu. Élevé, Très élevé et Ultra les débloquent.", "Con Normal, Motion Blur Strength e In-Game Depth Of Field Effect aparecen en gris en el menú gráfico del juego. Alto, Muy alto y Ultra los desbloquean." },
            new string[] { "Reset the list", "Lijst resetten", "Liste zurücksetzen", "Réinitialiser", "Restablecer" },
            new string[] { "Remove the builds that are not on disk now from the list, search again, and add what is found", "Haal de builds die nu niet op schijf staan uit de lijst, zoek opnieuw en voeg toe wat gevonden wordt", "Builds, die jetzt nicht auf dem Datenträger sind, aus der Liste entfernen, erneut suchen und das Gefundene hinzufügen", "Retirer de la liste les builds absentes du disque, rechercher à nouveau et ajouter ce qui est trouvé", "Quitar de la lista las builds que ahora no están en el disco, buscar de nuevo y añadir lo encontrado" },
            new string[] { "List reset", "Lijst gereset", "Liste zurückgesetzt", "Liste réinitialisée", "Lista restablecida" },
            new string[] { "Removed {0} build(s) that are not on disk now. Found {1}.", "{0} build(s) verwijderd die nu niet op schijf staan. {1} gevonden.", "{0} Build(s) entfernt, die jetzt nicht auf dem Datenträger sind. {1} gefunden.", "{0} build(s) retirée(s) qui ne sont plus sur le disque. {1} trouvée(s).", "Se quitaron {0} build(s) que ahora no están en el disco. Se encontraron {1}." },
            new string[] { "By continuing you accept these risks. Where it is possible, the old setting is saved or backed up first.", "Door verder te gaan accepteer je deze risico's. Waar het kan, wordt de oude instelling eerst opgeslagen of er wordt een back-up gemaakt.", "Wenn Sie fortfahren, akzeptieren Sie diese Risiken. Wo es möglich ist, wird die alte Einstellung zuerst gespeichert oder gesichert.", "En continuant, vous acceptez ces risques. Quand c'est possible, l'ancien réglage est d'abord enregistré ou sauvegardé.", "Al continuar aceptas estos riesgos. Cuando es posible, el ajuste anterior se guarda o se copia antes." },
            new string[] { "High, Very high and Ultra unlock Motion Blur Strength and In-Game Depth Of Field Effect in the game's graphics menu.", "Hoog, Zeer hoog en Ultra ontgrendelen Motion Blur Strength en In-Game Depth Of Field Effect in het grafische menu van de game.", "Hoch, Sehr hoch und Ultra schalten Motion Blur Strength und In-Game Depth Of Field Effect im Grafikmenü des Spiels frei.", "Élevé, Très élevé et Ultra débloquent Motion Blur Strength et In-Game Depth Of Field Effect dans le menu graphique du jeu.", "Alto, Muy alto y Ultra desbloquean Motion Blur Strength e In-Game Depth Of Field Effect en el menú gráfico del juego." },
            // reset (the most extreme clean)
            new string[] { "Reset (the most extreme clean)", "Reset (de meest extreme schoonmaak)", "Zurücksetzen (die extremste Reinigung)", "Réinitialisation (le nettoyage le plus extrême)", "Restablecer (la limpieza más extrema)" },
            new string[] { "You lose your key binds and your graphics settings!", "Je verliest je keybinds en je grafische instellingen!", "Sie verlieren Ihre Tastenbelegung und Ihre Grafikeinstellungen!", "Vous perdez vos touches et vos réglages graphiques !", "¡Pierdes tus teclas asignadas y tus ajustes gráficos!" },
            new string[] { "Cleans everything: temporary files, logs, login and saved data, and also your settings folder (key binds, graphics, saved server data). FiveM starts like a new install. A backup of the settings is saved first.", "Ruimt alles op: tijdelijke bestanden, logs, login en opgeslagen gegevens, en ook je instellingenmap (keybinds, grafische instellingen, opgeslagen servergegevens). FiveM start als een nieuwe installatie. Eerst wordt een back-up van de instellingen bewaard.", "Räumt alles auf: temporäre Dateien, Protokolle, Anmeldung und gespeicherte Daten, außerdem Ihren Einstellungsordner (Tastenbelegung, Grafik, gespeicherte Serverdaten). FiveM startet wie neu installiert. Zuerst wird eine Sicherung der Einstellungen angelegt.", "Nettoie tout : fichiers temporaires, journaux, connexion et données enregistrées, ainsi que votre dossier de réglages (touches, graphismes, données de serveur enregistrées). FiveM démarre comme une nouvelle installation. Une sauvegarde des réglages est faite d'abord.", "Limpia todo: archivos temporales, registros, inicio de sesión y datos guardados, y también tu carpeta de ajustes (teclas, gráficos, datos de servidor guardados). FiveM arranca como recién instalado. Antes se guarda una copia de seguridad de los ajustes." },
            new string[] { "Reset FiveM ...", "FiveM resetten ...", "FiveM zurücksetzen ...", "Réinitialiser FiveM ...", "Restablecer FiveM ..." },
            new string[] { "RESET: you lose your key binds and your graphics settings!", "RESET: je verliest je keybinds en je grafische instellingen!", "ZURÜCKSETZEN: Sie verlieren Ihre Tastenbelegung und Ihre Grafikeinstellungen!", "RÉINITIALISATION : vous perdez vos touches et vos réglages graphiques !", "RESTABLECER: ¡pierdes tus teclas asignadas y tus ajustes gráficos!" },
            new string[] { "You lose:", "Je verliest:", "Sie verlieren:", "Vous perdez :", "Pierdes:" },
            new string[] { "All your KEY BINDS ({0} saved keys)", "Al je KEYBINDS ({0} opgeslagen toetsen)", "Ihre gesamte TASTENBELEGUNG ({0} gespeicherte Tasten)", "Toutes vos TOUCHES ({0} touches enregistrées)", "Todas tus TECLAS ASIGNADAS ({0} teclas guardadas)" },
            new string[] { "All your KEY BINDS", "Al je KEYBINDS", "Ihre gesamte TASTENBELEGUNG", "Toutes vos TOUCHES", "Todas tus TECLAS ASIGNADAS" },
            new string[] { "All your GRAPHICS SETTINGS (Extended Texture Budget, Post FX and all the others)", "Al je GRAFISCHE INSTELLINGEN (Extended Texture Budget, Post FX en alle andere)", "Alle Ihre GRAFIKEINSTELLUNGEN (Extended Texture Budget, Post FX und alle anderen)", "Tous vos RÉGLAGES GRAPHIQUES (Extended Texture Budget, Post FX et tous les autres)", "Todos tus AJUSTES GRÁFICOS (Extended Texture Budget, Post FX y todos los demás)" },
            new string[] { "Your saved server data (settings that scripts keep on your PC)", "Je opgeslagen servergegevens (instellingen die scripts op je pc bewaren)", "Ihre gespeicherten Serverdaten (Einstellungen, die Skripte auf Ihrem PC ablegen)", "Vos données de serveur enregistrées (réglages que les scripts gardent sur votre PC)", "Tus datos de servidor guardados (ajustes que los scripts guardan en tu PC)" },
            new string[] { "Your camera and microphone permissions for servers, your camera view choices and the places of FiveM's own windows", "Je camera- en microfoontoestemmingen voor servers, je camerastandpunten en de plek van de eigen vensters van FiveM", "Ihre Kamera- und Mikrofonfreigaben für Server, Ihre Kameraansichten und die Position der FiveM-eigenen Fenster", "Vos autorisations caméra et micro pour les serveurs, vos choix de vue caméra et la position des fenêtres propres à FiveM", "Tus permisos de cámara y micrófono para servidores, tus vistas de cámara y la posición de las ventanas propias de FiveM" },
            new string[] { "Your saved FiveM login and the saved game and web data (FiveM loads the game files again, so the next start is slow)", "Je opgeslagen FiveM-login en de opgeslagen game- en webgegevens (FiveM laadt de spelbestanden opnieuw, dus de volgende start is traag)", "Ihre gespeicherte FiveM-Anmeldung und die gespeicherten Spiel- und Webdaten (FiveM lädt die Spieldateien neu, der nächste Start dauert daher lange)", "Votre connexion FiveM enregistrée et les données de jeu et web enregistrées (FiveM recharge les fichiers de jeu, le prochain démarrage est donc lent)", "Tu inicio de sesión de FiveM guardado y los datos guardados del juego y de la web (FiveM vuelve a cargar los archivos del juego, así que el próximo arranque es lento)" },
            new string[] { "All temporary files, logs and crash reports", "Alle tijdelijke bestanden, logs en crashrapporten", "Alle temporären Dateien, Protokolle und Absturzberichte", "Tous les fichiers temporaires, journaux et rapports de plantage", "Todos los archivos temporales, registros e informes de fallos" },
            new string[] { "A copy of the settings folder is saved first in the backups folder (the Backups button on the Frame cap tab opens it), so you can put things back by hand.", "Eerst wordt een kopie van de instellingenmap bewaard in de back-upmap (de knop Back-ups op het tabblad FPS-limiet opent hem), zodat je alles met de hand kunt terugzetten.", "Zuerst wird eine Kopie des Einstellungsordners im Sicherungsordner abgelegt (die Schaltfläche Sicherungen auf dem Reiter FPS-Limit öffnet ihn), damit Sie alles von Hand zurückkopieren können.", "Une copie du dossier de réglages est d'abord enregistrée dans le dossier des sauvegardes (le bouton Sauvegardes de l'onglet Limite de FPS l'ouvre), pour que vous puissiez tout remettre à la main.", "Primero se guarda una copia de la carpeta de ajustes en la carpeta de copias de seguridad (el botón Copias de seguridad de la pestaña Límite FPS la abre), para que puedas volver a poner todo a mano." },
            new string[] { "I understand that I lose my key binds and my graphics settings", "Ik begrijp dat ik mijn keybinds en mijn grafische instellingen verlies", "Ich verstehe, dass ich meine Tastenbelegung und meine Grafikeinstellungen verliere", "Je comprends que je perds mes touches et mes réglages graphiques", "Entiendo que pierdo mis teclas asignadas y mis ajustes gráficos" },
            new string[] { "Reset everything", "Alles resetten", "Alles zurücksetzen", "Tout réinitialiser", "Restablecerlo todo" },
            new string[] { "Cancel", "Annuleren", "Abbrechen", "Annuler", "Cancelar" },
            new string[] { "Resetting FiveM ...", "FiveM resetten ...", "FiveM wird zurückgesetzt ...", "Réinitialisation de FiveM ...", "Restableciendo FiveM ..." },
            new string[] { "Backing up the settings ...", "Back-up van de instellingen maken ...", "Einstellungen werden gesichert ...", "Sauvegarde des réglages ...", "Guardando copia de los ajustes ..." },
            new string[] { "FiveM was reset", "FiveM is gereset", "FiveM wurde zurückgesetzt", "FiveM a été réinitialisé", "FiveM se ha restablecido" },
            new string[] { "It starts like a new install. A backup of your settings is in the backups folder.", "Het start als een nieuwe installatie. Een back-up van je instellingen staat in de back-upmap.", "Es startet wie neu installiert. Eine Sicherung Ihrer Einstellungen liegt im Sicherungsordner.", "Il démarre comme une nouvelle installation. Une sauvegarde de vos réglages se trouve dans le dossier des sauvegardes.", "Arranca como recién instalado. Hay una copia de seguridad de tus ajustes en la carpeta de copias de seguridad." },
            new string[] { "Reset stopped", "Reset gestopt", "Zurücksetzen gestoppt", "Réinitialisation arrêtée", "Restablecimiento detenido" },
            new string[] { "The backup copy could not be made, so nothing was deleted.", "De back-up kon niet worden gemaakt, dus er is niets verwijderd.", "Die Sicherung konnte nicht erstellt werden, daher wurde nichts gelöscht.", "La sauvegarde n'a pas pu être faite, rien n'a donc été supprimé.", "No se pudo hacer la copia de seguridad, así que no se borró nada." },
            new string[] { "Your saved GTA V licence check and some browser files (FiveM does the check again at the next start, so stay signed in to the Rockstar Games Launcher or Steam)", "Je opgeslagen GTA V-licentiecontrole en wat browserbestanden (FiveM doet de controle bij de volgende start opnieuw, dus blijf ingelogd bij de Rockstar Games Launcher of Steam)", "Ihre gespeicherte GTA-V-Lizenzprüfung und einige Browserdateien (FiveM prüft beim nächsten Start erneut, bleiben Sie also im Rockstar Games Launcher oder bei Steam angemeldet)", "Votre vérification de licence GTA V enregistrée et quelques fichiers de navigateur (FiveM refait la vérification au prochain démarrage, restez donc connecté au Rockstar Games Launcher ou à Steam)", "Tu comprobación de licencia de GTA V guardada y algunos archivos del navegador (FiveM la repite en el próximo arranque, así que mantén la sesión iniciada en el Rockstar Games Launcher o en Steam)" },
            // graphics cards: which one is which
            new string[] { "integrated", "geïntegreerd", "integriert", "intégrée", "integrada" },
            new string[] { "dedicated", "dedicated", "dediziert", "dédiée", "dedicada" },
            new string[] { "{0} video memory", "{0} videogeheugen", "{0} Grafikspeicher", "{0} de mémoire vidéo", "{0} de memoria de vídeo" },
            // the Reset switch
            new string[] { "Reset mode", "Reset-modus", "Zurücksetzen-Modus", "Mode réinitialisation", "Modo restablecer" },
            new string[] { "Off. The normal clean and the deep clean never touch your key binds or your graphics settings.", "Uit. De gewone en de diepe schoonmaak raken je keybinds en je grafische instellingen nooit aan.", "Aus. Die normale und die Tiefenreinigung berühren Ihre Tastenbelegung und Ihre Grafikeinstellungen nie.", "Désactivé. Le nettoyage normal et le nettoyage en profondeur ne touchent jamais à vos touches ni à vos réglages graphiques.", "Desactivado. La limpieza normal y la profunda nunca tocan tus teclas asignadas ni tus ajustes gráficos." },
            // NVIDIA frame cap
            new string[] { "NVIDIA mode: works with the NVIDIA driver.", "NVIDIA-modus: werkt met het NVIDIA-stuurprogramma.", "NVIDIA-Modus: funktioniert mit dem NVIDIA-Treiber.", "Mode NVIDIA : fonctionne avec le pilote NVIDIA.", "Modo NVIDIA: funciona con el controlador de NVIDIA." },
            new string[] { "NVIDIA card found, but the NVIDIA driver could not be used. Open the full log for details.", "NVIDIA-kaart gevonden, maar het NVIDIA-stuurprogramma kon niet worden gebruikt. Open het volledige logboek voor details.", "NVIDIA-Karte gefunden, aber der NVIDIA-Treiber konnte nicht verwendet werden. Öffnen Sie das vollständige Protokoll für Details.", "Carte NVIDIA détectée, mais le pilote NVIDIA n'a pas pu être utilisé. Ouvrez le journal complet pour les détails.", "Tarjeta NVIDIA encontrada, pero no se pudo usar el controlador de NVIDIA. Abre el registro completo para ver los detalles." },
            new string[] { "NVIDIA: no frame limit set", "NVIDIA: geen FPS-limiet ingesteld", "NVIDIA: kein FPS-Limit gesetzt", "NVIDIA : aucune limite de FPS définie", "NVIDIA: ningún límite de FPS establecido" },
            new string[] { "NVIDIA: frame limit {0} FPS", "NVIDIA: FPS-limiet {0}", "NVIDIA: FPS-Limit {0}", "NVIDIA : limite de FPS {0}", "NVIDIA: límite de FPS {0}" },
            new string[] { "Could not read the NVIDIA driver profiles", "Kon de NVIDIA-stuurprogrammaprofielen niet lezen", "Die NVIDIA-Treiberprofile konnten nicht gelesen werden", "Impossible de lire les profils du pilote NVIDIA", "No se pudieron leer los perfiles del controlador de NVIDIA" },
            new string[] { "Could not change the NVIDIA driver profile. Open the full log for details.", "Kon het NVIDIA-stuurprogrammaprofiel niet wijzigen. Open het volledige logboek voor details.", "Das NVIDIA-Treiberprofil konnte nicht geändert werden. Öffnen Sie das vollständige Protokoll für Details.", "Impossible de modifier le profil du pilote NVIDIA. Ouvrez le journal complet pour les détails.", "No se pudo cambiar el perfil del controlador de NVIDIA. Abre el registro completo para ver los detalles." },
            new string[] { "Could not change the NVIDIA driver profile. Try again as administrator (the shield button at the top).", "Kon het NVIDIA-stuurprogrammaprofiel niet wijzigen. Probeer het opnieuw als beheerder (de schildknop bovenaan).", "Das NVIDIA-Treiberprofil konnte nicht geändert werden. Versuchen Sie es als Administrator (die Schild-Schaltfläche oben).", "Impossible de modifier le profil du pilote NVIDIA. Réessayez en tant qu'administrateur (le bouton bouclier en haut).", "No se pudo cambiar el perfil del controlador de NVIDIA. Inténtalo de nuevo como administrador (el botón del escudo, arriba)." },
            new string[] { "Cap put back: no limit set.", "Limiet teruggezet: geen limiet ingesteld.", "Limit zurückgesetzt: kein Limit gesetzt.", "Limite rétablie : aucune limite définie.", "Límite restaurado: ningún límite establecido." },
            new string[] { "The frame limit is set in the NVIDIA driver profile of each ticked build.\r\nThe old limit is remembered, so Undo can put it back.\r\n\r\nSet the cap to {0} FPS for {1} build(s)?", "De limiet wordt ingesteld in het NVIDIA-stuurprogrammaprofiel van elke aangevinkte build.\r\nDe oude limiet wordt onthouden, zodat Ongedaan maken hem kan terugzetten.\r\n\r\nLimiet instellen op {0} FPS voor {1} build(s)?", "Das Limit wird im NVIDIA-Treiberprofil jeder angekreuzten Build gesetzt.\r\nDas alte Limit wird gemerkt, damit die Rückgängig-Schaltfläche es zurücksetzen kann.\r\n\r\nLimit auf {0} FPS für {1} Build(s) setzen?", "La limite est définie dans le profil du pilote NVIDIA de chaque build cochée.\r\nL'ancienne limite est mémorisée, pour que l'annulation puisse la rétablir.\r\n\r\nDéfinir la limite à {0} FPS pour {1} build(s) ?", "El límite se establece en el perfil del controlador de NVIDIA de cada build marcada.\r\nEl límite anterior se recuerda, para que Deshacer pueda restaurarlo.\r\n\r\n¿Establecer el límite en {0} FPS para {1} build(s)?" },
            new string[] { "Put the cap back to what it was before the last change for {0} build(s)?\r\n\r\nThe NVIDIA driver profile is changed back.", "De limiet terugzetten naar wat het was voor de laatste wijziging, voor {0} build(s)?\r\n\r\nHet NVIDIA-stuurprogrammaprofiel wordt teruggezet.", "Das Limit für {0} Build(s) auf den Wert vor der letzten Änderung zurücksetzen?\r\n\r\nDas NVIDIA-Treiberprofil wird zurückgesetzt.", "Remettre la limite comme avant la dernière modification pour {0} build(s) ?\r\n\r\nLe profil du pilote NVIDIA est remis comme avant.", "¿Restaurar el límite a como estaba antes del último cambio para {0} build(s)?\r\n\r\nEl perfil del controlador de NVIDIA se restaura." },
            new string[] { "Changed {0} build(s), {1} already had it. The NVIDIA driver profile was updated.", "{0} build(s) aangepast, {1} hadden het al. Het NVIDIA-stuurprogrammaprofiel is bijgewerkt.", "{0} Build(s) geändert, {1} hatten es schon. Das NVIDIA-Treiberprofil wurde aktualisiert.", "{0} build(s) modifiée(s), {1} l'avaient déjà. Le profil du pilote NVIDIA a été mis à jour.", "{0} build(s) cambiada(s), {1} ya lo tenían. Se actualizó el perfil del controlador de NVIDIA." },
            new string[] { "{0} build(s) already had the cap set to {1}. The NVIDIA driver was left alone.", "{0} build(s) hadden de limiet al op {1} staan. Het NVIDIA-stuurprogramma is met rust gelaten.", "{0} Build(s) hatten das Limit schon auf {1}. Der NVIDIA-Treiber wurde nicht angefasst.", "{0} build(s) avaient déjà la limite réglée sur {1}. Le pilote NVIDIA n'a pas été touché.", "{0} build(s) ya tenían el límite en {1}. No se tocó el controlador de NVIDIA." },
            new string[] { "Put back {0} build(s). The NVIDIA driver profile was changed back.", "{0} build(s) teruggezet. Het NVIDIA-stuurprogrammaprofiel is teruggezet.", "{0} Build(s) zurückgesetzt. Das NVIDIA-Treiberprofil wurde zurückgesetzt.", "{0} build(s) rétablie(s). Le profil du pilote NVIDIA a été remis comme avant.", "{0} build(s) restaurada(s). Se restauró el perfil del controlador de NVIDIA." },
            new string[] { "Talking to the NVIDIA driver ...", "Praten met het NVIDIA-stuurprogramma ...", "Verbindung zum NVIDIA-Treiber ...", "Dialogue avec le pilote NVIDIA ...", "Hablando con el controlador de NVIDIA ..." },
            new string[] { "Writing the new frame limit ...", "De nieuwe FPS-limiet wordt geschreven ...", "Neues FPS-Limit wird geschrieben ...", "Écriture de la nouvelle limite de FPS ...", "Escribiendo el nuevo límite de FPS ..." },
            new string[] { "Checking what the driver kept ...", "Controleren wat het stuurprogramma bewaard heeft ...", "Prüfen, was der Treiber behalten hat ...", "Vérification de ce que le pilote a gardé ...", "Comprobando lo que guardó el controlador ..." },
            // the dedicated graphics card for FiveM
            new string[] { "Use the dedicated graphics card for FiveM", "Gebruik de dedicated videokaart voor FiveM", "Dedizierte Grafikkarte für FiveM verwenden", "Utiliser la carte graphique dédiée pour FiveM", "Usar la tarjeta gráfica dedicada para FiveM" },
            new string[] { "The dedicated graphics card is now used for the ticked builds.", "De dedicated videokaart wordt nu gebruikt voor de aangevinkte builds.", "Für die angekreuzten Builds wird jetzt die dedizierte Grafikkarte verwendet.", "La carte graphique dédiée est maintenant utilisée pour les builds cochées.", "Ahora se usa la tarjeta gráfica dedicada para las builds marcadas." },
        };
    }

    // ---------------------------------------------------------------- the real video memory use of a program (Windows counter "GPU Process Memory")
    class GpuMemSample { public int Pid; public string Luid = ""; public long DedicatedBytes; }

    static class GpuMem
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQueryW(string dataSource, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);
        [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
        const uint FmtLarge = 0x400, MoreData = 0x800007D2;
        static readonly Regex InstRx = new Regex(@"^pid_(\d+)_luid_(0x[0-9a-f]+)_(0x[0-9a-f]+)_phys_\d+", RegexOptions.IgnoreCase);

        // The reader the window uses. A test replaces it. null = Windows gives no numbers (an old Windows, or the counter is missing), an empty list = no program holds video memory now.
        public static Func<List<GpuMemSample>> Reader = ReadReal;

        // READ ONLY. One reading of "Dedicated Usage" for every process and every card.
        public static List<GpuMemSample> ReadReal()
        {
            IntPtr q = IntPtr.Zero;
            try
            {
                if (PdhOpenQueryW(null, IntPtr.Zero, out q) != 0) return null;
                IntPtr c;
                if (PdhAddEnglishCounterW(q, @"\GPU Process Memory(*)\Dedicated Usage", IntPtr.Zero, out c) != 0) return null;
                if (PdhCollectQueryData(q) != 0) return null;
                uint size = 0, count = 0;
                uint rc = PdhGetFormattedCounterArrayW(c, FmtLarge, ref size, ref count, IntPtr.Zero);
                List<GpuMemSample> list = new List<GpuMemSample>();
                if (rc != MoreData) return rc == 0 ? list : null;
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    rc = PdhGetFormattedCounterArrayW(c, FmtLarge, ref size, ref count, buf);
                    if (rc != 0) return null;
                    // PDH_FMT_COUNTERVALUE_ITEM: a pointer to the name, then the status (4 bytes), then the 8 byte value (aligned to 8)
                    int itemSize = IntPtr.Size == 8 ? 24 : 16, valueAt = IntPtr.Size == 8 ? 16 : 8;
                    for (int i = 0; i < (int)count; i++)
                    {
                        IntPtr item = IntPtr.Add(buf, i * itemSize);
                        string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                        if (name == null) continue;
                        Match m = InstRx.Match(name);
                        if (!m.Success) continue;
                        GpuMemSample sm = new GpuMemSample();
                        sm.Pid = int.Parse(m.Groups[1].Value);
                        sm.Luid = (m.Groups[2].Value + "_" + m.Groups[3].Value).ToLowerInvariant();
                        sm.DedicatedBytes = Marshal.ReadInt64(item, valueAt);
                        list.Add(sm);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
                return list;
            }
            catch (Exception ex) { AppLog.Debug("reading the GPU Process Memory counter failed: " + ex.Message); return null; }
            finally { if (q != IntPtr.Zero) { try { PdhCloseQuery(q); } catch (Exception) { } } }
        }
    }

    // What the meter shows, worked out from one reading (pure, tested).
    class VramState
    {
        public string State = "off";      // off = the game is not running, nodata = it runs but Windows gives no numbers, ok = numbers
        public string GameKey = "";       // the process ids of the game: a new game (other ids) starts a new peak
        public long UsedBytes, OtherBytes, TotalBytes, PeakBytes;
        public int UsedPercent, PeakPercent;
        public string Level = "good", PeakLevel = "good";
        public bool OnOtherChip;          // the game holds more on another chip than on the dedicated card
    }

    static class VramMeter
    {
        // up to 80 percent used = at least 20 percent free = good; up to 90 percent = orange; above that = red (the same limits as the advice for the budget)
        public static string LevelFor(double usedPercent) { return usedPercent <= 80 ? "good" : usedPercent <= 90 ? "warn" : "bad"; }

        // samples: one reading of the counter (null = Windows gives no numbers). pids: the ids of the FiveM game. dedicatedLuid: the LUID of the dedicated card (null = not known: then the card with the most is taken). total: its video memory in bytes (0 = not known).
        public static VramState Evaluate(List<GpuMemSample> samples, List<int> pids, string dedicatedLuid, long total)
        {
            VramState st = new VramState(); st.TotalBytes = total;
            List<int> sorted = new List<int>(pids); sorted.Sort();
            st.GameKey = string.Join(",", sorted.ConvertAll(delegate(int i) { return i.ToString(); }).ToArray());
            if (pids.Count == 0) { st.State = "off"; return st; }
            Dictionary<string, long> per = new Dictionary<string, long>();
            if (samples != null)
                foreach (GpuMemSample m in samples)
                    if (pids.Contains(m.Pid)) { long v; per.TryGetValue(m.Luid, out v); per[m.Luid] = v + m.DedicatedBytes; }
            if (samples == null || per.Count == 0) { st.State = "nodata"; return st; }
            long ded = 0, other = 0;
            if (dedicatedLuid != null)
            {
                foreach (KeyValuePair<string, long> kv in per) { if (kv.Key == dedicatedLuid) ded += kv.Value; else other += kv.Value; }
            }
            else
            {
                long max = 0, all = 0;
                foreach (long v in per.Values) { max = Math.Max(max, v); all += v; }
                ded = max; other = all - max;
            }
            st.State = "ok"; st.UsedBytes = ded; st.OtherBytes = other;
            st.OnOtherChip = other > ded && other >= 256L * 1048576L;
            if (total > 0) { double pct = 100.0 * ded / total; st.UsedPercent = (int)Math.Round(pct); st.Level = LevelFor(pct); }
            return st;
        }
    }

    // keeps the highest value of the game that is running now, and of the last game after it closed
    class VramTracker
    {
        bool running; long peak, lastPeak; string key = "";

        public void Update(VramState st)
        {
            if (st.State == "ok")
            {
                if (!running || key != st.GameKey) { running = true; peak = 0; key = st.GameKey; }
                if (st.UsedBytes > peak) peak = st.UsedBytes;
                lastPeak = peak; st.PeakBytes = peak;
            }
            else if (st.State == "off") { running = false; st.PeakBytes = lastPeak; }
            else st.PeakBytes = peak;
            if (st.TotalBytes > 0 && st.PeakBytes > 0) { double pct = 100.0 * st.PeakBytes / st.TotalBytes; st.PeakPercent = (int)Math.Round(pct); st.PeakLevel = VramMeter.LevelFor(pct); }
        }
    }

    // a thin bar: the track, the filled part in the colour of the level, and a mark at 80 percent (above it less than 20 percent of the card is free)
    class MeterBar : Control
    {
        public float Value = 0f, Mark = 0.8f;
        public Color Fill = Color.Gray;

        public MeterBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 12;
        }

        public void Set(float v, Color c) { Value = Math.Max(0f, Math.Min(1f, v)); Fill = c; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg2);
            Rectangle r = new Rectangle(0, 1, Width - 1, Height - 3);
            int d = Math.Max(2, r.Height);
            using (GraphicsPath track = Gfx.Rounded(r, d)) using (SolidBrush b = new SolidBrush(Theme.Bg4)) g.FillPath(b, track);
            int w = (int)Math.Round(r.Width * Value);
            if (w >= d)
            {
                Rectangle f = new Rectangle(r.X, r.Y, w, r.Height);
                using (GraphicsPath fill = Gfx.Rounded(f, d)) using (SolidBrush b = new SolidBrush(Fill)) g.FillPath(b, fill);
            }
            else if (w > 0) using (SolidBrush b = new SolidBrush(Fill)) g.FillEllipse(b, new Rectangle(r.X, r.Y, d, r.Height));
            int mx = r.X + (int)Math.Round(r.Width * Mark);
            using (Pen p = new Pen(Theme.Text2, 1f)) g.DrawLine(p, mx, 0, mx, Height - 1);
        }
    }

    // ---------------------------------------------------------------- how high the Extended Texture Budget can go for a card
    // Measured by the user in FiveM's own memory bar (with his graphics settings): 3056 MB at the lowest budget and 8056 MB at the highest, so about 50 MB for every percent.
    // That bar is an ESTIMATE of the game, and the base of 3056 MB depends on the other graphics settings (lower settings leave more room). The advice leaves about 20 percent of the video memory of the card free.
    static class Budget
    {
        public const double BaseMb = 3056, PerPercentMb = 50, Headroom = 0.20;

        public static double EstimateMb(int pct) { return BaseMb + PerPercentMb * pct; }

        // how many percent of the video memory stay free at this budget (an estimate)
        public static int FreePercent(long vramBytes, int pct)
        {
            double v = vramBytes / 1048576.0;
            return v <= 0 ? 0 : (int)Math.Round((1 - EstimateMb(pct) / v) * 100);
        }

        // The card the game will use: the biggest dedicated card, else the biggest card of unknown kind. An integrated chip has a small memory of its own (it shares the PC memory), so there is no advice for it.
        public static Gpu Pick(List<Gpu> gpus)
        {
            Gpu best = null;
            if (gpus == null) return null;
            foreach (Gpu g in gpus) if (g.Kind == "dedicated" && g.Vram > 0 && !Finder.IsUnsupportedDedicated(g) && (best == null || g.Vram > best.Vram)) best = g;
            if (best != null) return best;
            foreach (Gpu g in gpus) if (g.Kind.Length == 0 && g.Vram > 0 && (best == null || g.Vram > best.Vram)) best = g;
            return best;
        }

        // "good" up to the advice, "warn" above it while 10 percent or more of the card stays free, "bad" when less than 10 percent stays free (or the estimate is more than the card has)
        public static string Level(long vramBytes, int pct, int recommended)
        {
            if (pct <= recommended) return "good";
            return FreePercent(vramBytes, pct) >= 10 ? "warn" : "bad";
        }

        // -1 = no advice (the memory of the card is not known); else 0 to 100 in steps of 5, leaving about 20 percent of the card free
        public static int Recommended(long vramBytes)
        {
            if (vramBytes <= 0) return -1;
            double v = vramBytes / 1048576.0;
            double pct = ((1 - Headroom) * v - BaseMb) / PerPercentMb;
            if (pct <= 0) return 0;
            int r = (int)(Math.Floor((pct + 1) / 5.0) * 5);
            return Math.Max(0, Math.Min(100, r));
        }
    }

    // ---------------------------------------------------------------- FiveM's own graphics settings: Extended Texture Budget and Post FX
    // fivem.cfg (in %APPDATA%\CitizenFX) holds the console variables. The Extended Texture Budget slider is the variable vid_budgetScale (a percentage).
    // gta5_settings.xml (same folder) holds the game graphics. PostFX there is 0 normal, 1 high, 2 very high, 3 ultra.
    // FiveM writes both files again when it closes, so they are only changed while FiveM is closed. Key binds live in fivem.cfg too and are never touched.
    static class GfxTool
    {
        public static string OverrideDir = null;     // for tests: work on a copy of the files
        public static string Dir { get { return OverrideDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX"); } }
        public static string CfgFile { get { return Path.Combine(Dir, "fivem.cfg"); } }
        public static string XmlFile { get { return Path.Combine(Dir, "gta5_settings.xml"); } }

        public static string PostFxName(int v) { return v == 0 ? "Normal" : v == 1 ? "High" : v == 2 ? "Very high" : "Ultra"; }

        static readonly Regex BudgetRx = new Regex(@"^[ \t]*seta[ \t]+""vid_budgetScale""[ \t]+""(\d+)""", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex PostFxRx = new Regex(@"<PostFX\s+value=""(\d+)""\s*/>", RegexOptions.IgnoreCase);

        static string Read(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; }
            catch (Exception) { return null; }
        }

        static int Number(Match m) { int v; return m.Success && int.TryParse(m.Groups[1].Value, out v) ? v : -1; }

        // -1 = the setting is not in the file, -2 = there is no file
        public static int ReadBudget() { string t = Read(CfgFile); return t == null ? -2 : Number(BudgetRx.Match(t)); }
        public static int ReadPostFx() { string t = Read(XmlFile); return t == null ? -2 : Number(PostFxRx.Match(t)); }

        public const string UiFixName = "nui_useInProcessGpu", LimiterName = "game_useAudioFrameLimiter";

        static Regex FlagRx(string name) { return new Regex(@"^[ \t]*seta[ \t]+""" + Regex.Escape(name) + @"""[ \t]+""([^""]*)""", RegexOptions.Multiline | RegexOptions.IgnoreCase); }

        // 1 = true, 0 = false, -1 = the line is not in the file (or has another value), -2 = there is no file
        public static int ReadFlag(string name)
        {
            string t = Read(CfgFile);
            if (t == null) return -2;
            Match m = FlagRx(name).Match(t);
            if (!m.Success) return -1;
            string v = m.Groups[1].Value.Trim().ToLowerInvariant();
            return v == "true" || v == "1" ? 1 : v == "false" || v == "0" ? 0 : -1;
        }

        // Only this one line changes (the others, the key binds included, stay as they are). A line that is not there yet goes after the last "seta" line. FiveM writes true and false as words.
        public static void SetFlag(string name, bool v, Action<string> log)
        {
            string path = CfgFile;
            string t = Read(path);
            if (t == null) throw new Exception("fivem.cfg was not found.");
            Regex rx = FlagRx(name);
            string line = "seta \"" + name + "\" \"" + (v ? "true" : "false") + "\"";
            string nt;
            if (rx.IsMatch(t)) nt = rx.Replace(t, line, 1);
            else
            {
                string nl = t.Contains("\r\n") ? "\r\n" : "\n";
                int i = t.LastIndexOf("\nseta ", StringComparison.Ordinal);
                int e = i >= 0 ? t.IndexOf('\n', i + 1) : -1;
                if (e >= 0) nt = t.Substring(0, e + 1) + line + nl + t.Substring(e + 1);
                else nt = t + (t.EndsWith("\n") ? "" : nl) + line + nl;
            }
            Backup(path, log);
            Write(path, nt);
            if (ReadFlag(name) != (v ? 1 : 0)) throw new Exception("The new value of " + name + " was not found in fivem.cfg after saving.");
        }

        // The two options as the Toolbox shows them. Interface lag fix: ON = nui_useInProcessGpu true (a missing line means off). Stutter fix: ON = the audio frame limiter is OFF, game_useAudioFrameLimiter false (a missing line means the limiter is on).
        public static bool UiFixOn() { return ReadFlag(UiFixName) == 1; }
        public static bool StutterFixOn() { return ReadFlag(LimiterName) == 0; }
        public static void SetUiFix(bool on, Action<string> log) { SetFlag(UiFixName, on, log); }
        public static void SetStutterFix(bool on, Action<string> log) { SetFlag(LimiterName, !on, log); }

        static void Backup(string path, Action<string> log)
        {
            string bdir = OverrideDir != null ? Path.Combine(OverrideDir, "backup") : Blb.BackupDir;
            Directory.CreateDirectory(bdir);
            string dest = Path.Combine(bdir, Path.GetFileName(path) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            if (File.Exists(dest)) { log("Backup of this second is already there (it holds the file as it was before the first change): " + dest); return; }
            File.Copy(path, dest, true);
            log("Backup saved: " + dest);
        }

        // build the bytes first, then open the file (an error while building must not leave an empty file)
        static void Write(string path, string text)
        {
            byte[] data = new UTF8Encoding(false).GetBytes(text);
            File.WriteAllBytes(path, data);
        }

        public static void SetBudget(int v, Action<string> log)
        {
            string path = CfgFile;
            string t = Read(path);
            if (t == null) throw new Exception("fivem.cfg was not found.");
            string line = "seta \"vid_budgetScale\" \"" + v + "\"";
            string nt;
            if (BudgetRx.IsMatch(t)) nt = BudgetRx.Replace(t, line, 1);
            else
            {
                string nl = t.Contains("\r\n") ? "\r\n" : "\n";
                int i = t.LastIndexOf("\nseta ", StringComparison.Ordinal);
                int e = i >= 0 ? t.IndexOf('\n', i + 1) : -1;
                if (e >= 0) nt = t.Substring(0, e + 1) + line + nl + t.Substring(e + 1);
                else nt = t + (t.EndsWith("\n") ? "" : nl) + line + nl;
            }
            Backup(path, log);
            Write(path, nt);
            if (ReadBudget() != v) throw new Exception("The new Extended Texture Budget was not found in fivem.cfg after saving.");
        }

        public static void SetPostFx(int v, Action<string> log)
        {
            string path = XmlFile;
            string t = Read(path);
            if (t == null) throw new Exception("gta5_settings.xml was not found.");
            string tag = "<PostFX value=\"" + v + "\" />";
            string nt;
            if (PostFxRx.IsMatch(t)) nt = PostFxRx.Replace(t, tag, 1);
            else
            {
                int i = t.IndexOf("</graphics>", StringComparison.OrdinalIgnoreCase);
                if (i < 0) throw new Exception("gta5_settings.xml has no graphics part.");
                string nl = t.Contains("\r\n") ? "\r\n" : "\n";
                nt = t.Substring(0, i) + "  " + tag + nl + "  " + t.Substring(i);
            }
            Backup(path, log);
            Write(path, nt);
            if (ReadPostFx() != v) throw new Exception("The new Post FX value was not found in gta5_settings.xml after saving.");
        }
    }

    // ---------------------------------------------------------------- start a program with normal rights
    // FiveM and Radeon Software must not run as administrator, also when this copy does.
    // ---------------------------------------------------------------- a desktop icon that starts FiveM in pure mode
    static class PureIcon
    {
        public const string FileName = "FiveM (pure mode).lnk";
        public const string Arguments = "-pure_1";
        public static string FolderOverride = null;      // the tests make the icon in a scratch folder, never on the real desktop

        public static string Folder() { return FolderOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        public static string PathOf() { return Path.Combine(Folder(), FileName); }

        static object Get(object o, string name) { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null); }
        static void Put(object o, string name, object value) { o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new object[] { value }); }

        // what an icon says: target, arguments, folder to start in, icon, description (null when there is no icon file or it cannot be read)
        public static string[] ReadAll(string lnk)
        {
            if (!File.Exists(lnk)) return null;
            object sh = null, sc = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                sh = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                return new string[] { Get(sc, "TargetPath") as string, Get(sc, "Arguments") as string, Get(sc, "WorkingDirectory") as string, Get(sc, "IconLocation") as string, Get(sc, "Description") as string };
            }
            catch (Exception) { return null; }
            finally { if (sc != null) Marshal.ReleaseComObject(sc); if (sh != null) Marshal.ReleaseComObject(sh); }
        }

        static bool Same(string[] now, string fiveMExe)
        {
            return now != null && string.Equals(now[0], fiveMExe, StringComparison.OrdinalIgnoreCase) && string.Equals((now[1] ?? "").Trim(), Arguments, StringComparison.OrdinalIgnoreCase);
        }

        // Makes the icon on the desktop, or updates it. Returns null when it worked (what = made, updated or already), else the reason it did not.
        public static string Make(string fiveMExe, out string what)
        {
            what = "";
            try
            {
                if (string.IsNullOrEmpty(fiveMExe) || !File.Exists(fiveMExe)) return "FiveM.exe was not found.";
                string folder = Folder();
                if (!Directory.Exists(folder)) return "The desktop folder was not found: " + folder;
                string lnk = PathOf();
                bool had = File.Exists(lnk);
                if (had && Same(ReadAll(lnk), fiveMExe)) { what = "already"; return null; }
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return "Windows could not make a shortcut (WScript.Shell is missing).";
                object sh = Activator.CreateInstance(t);
                object sc = null;
                try
                {
                    sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                    Put(sc, "TargetPath", fiveMExe);
                    Put(sc, "Arguments", Arguments);
                    Put(sc, "WorkingDirectory", Path.GetDirectoryName(fiveMExe));
                    Put(sc, "IconLocation", fiveMExe + ",0");
                    Put(sc, "Description", "FiveM in pure mode (made by FiveM Toolbox)");
                    sc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                }
                finally { if (sc != null) Marshal.ReleaseComObject(sc); Marshal.ReleaseComObject(sh); }
                if (!Same(ReadAll(lnk), fiveMExe)) return "The icon was saved, but reading it back did not give the same target.";
                what = had ? "updated" : "made";
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    static class Launch
    {
        public static void Normal(string exe, string args, string dir)
        {
            if (args == null) args = "";
            if (DnsTool.IsAdmin())
            {
                try
                {
                    if (args.Length == 0)
                    {
                        // Explorer starts it with the rights of the signed-in user
                        ProcessStartInfo ex = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + exe + "\"");
                        ex.UseShellExecute = false;
                        Process.Start(ex);
                        return;
                    }
                    ProcessStartInfo ru = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "runas.exe"), "/trustlevel:0x20000 \"\\\"" + exe + "\\\" " + args + "\"");
                    ru.UseShellExecute = false; ru.CreateNoWindow = true; ru.WorkingDirectory = dir;
                    Process.Start(ru);
                    return;
                }
                catch (Exception e) { AppLog.Debug("could not start " + exe + " with normal rights, starting it the plain way: " + e.Message); }
            }
            ProcessStartInfo plain = new ProcessStartInfo(exe, args);
            plain.WorkingDirectory = dir; plain.UseShellExecute = false;
            Process.Start(plain);
        }
    }

    // ---------------------------------------------------------------- only one copy of the program at a time
    // Two copies could change Radeon's settings file or the DNS at the same moment. The helper step for DNS (--dns-...) and the test switches do not use this.
    static class SingleInstance
    {
        [DllImport("user32.dll")] static extern int RegisterWindowMessage(string s);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern bool ChangeWindowMessageFilter(int msg, int flag);

        public static readonly int Msg = RegisterWindowMessage("FiveMToolbox.ShowYourself");
        static Mutex mutex;

        // true when this is the only copy. A copy that the program started itself (after a language change) waits a little for the old copy to close.
        public static bool Acquire(bool waitForOld)
        {
            try
            {
                bool created;
                // the user's own account may open it, also from a copy with other rights (administrator or not)
                System.Security.AccessControl.MutexSecurity sec = new System.Security.AccessControl.MutexSecurity();
                sec.AddAccessRule(new System.Security.AccessControl.MutexAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User, System.Security.AccessControl.MutexRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
                mutex = new Mutex(true, @"Local\FiveMToolbox.OneWindow", out created, sec);
                if (created) { Allow(); return true; }
                if (waitForOld)
                {
                    try { if (mutex.WaitOne(10000)) { Allow(); return true; } }
                    catch (AbandonedMutexException) { Allow(); return true; }
                }
                return false;
            }
            catch (UnauthorizedAccessException) { return false; }   // the other copy runs as administrator
        }

        // an administrator copy accepts the "come to the front" message from a normal copy
        static void Allow() { try { ChangeWindowMessageFilter(Msg, 1); } catch (Exception) { } }

        // asks the open copy to come to the front
        public static void ShowOther()
        {
            try { AllowSetForegroundWindow(-1); PostMessage((IntPtr)0xFFFF, Msg, IntPtr.Zero, IntPtr.Zero); } catch (Exception) { }
        }
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        // For the test pictures: ask Windows to paint the whole window into the bitmap.
        // starts this program again with administrator rights (Windows asks once); false when the permission window was closed
        static bool RestartElevated(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, arguments);
                psi.Verb = "runas"; psi.UseShellExecute = true;
                Process.Start(psi);
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        static bool snapScreen = false;
        static void Snap(Form f, Bitmap bmp)
        {
            if (snapScreen)
            {
                f.TopMost = true; f.BringToFront();
                for (int w = 0; w < 8; w++) { Application.DoEvents(); Thread.Sleep(60); }
                using (Graphics sg = Graphics.FromImage(bmp)) sg.CopyFromScreen(f.Left, f.Top, 0, 0, new Size(f.Width, f.Height));
                f.TopMost = false;
                return;
            }
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                try { PrintWindow(f.Handle, hdc, 2); } finally { g.ReleaseHdc(hdc); }
            }
        }

        // Command line (for testing): FiveMToolbox.exe --scan <outfile>   |   --apply <cap> <outfile>
        [STAThread]
        static int Main(string[] args)
        {
            // test mode: --pretend-vram <MB> anywhere in the arguments (for looking at the advice for a small card on a PC with a big one); it is taken out of the arguments here
            List<string> pvArgs = new List<string>();
            for (int pvI = 0; pvI < args.Length; pvI++)
            {
                int pvMb;
                if (args[pvI] == "--pretend-vram" && pvI + 1 < args.Length && int.TryParse(args[pvI + 1], out pvMb) && pvMb >= 512 && pvMb <= 262144) { Finder.PretendVramMb = pvMb; pvI++; continue; }
                if (args[pvI] == "--pretend-vram-use" && pvI + 1 < args.Length && int.TryParse(args[pvI + 1], out pvMb) && pvMb >= 1 && pvMb <= 262144) { Finder.PretendUseMb = pvMb; pvI++; continue; }
                pvArgs.Add(args[pvI]);
            }
            args = Dpi.Init(pvArgs.ToArray());
            if (args.Length >= 1 && args[0].StartsWith("--dns-")) return DnsTool.HelperMain(args);
            if (args.Length >= 1 && (args[0] == "--nv-apply" || args[0] == "--nv-undo"))
            {
                // --nv-apply <cap> <outfile>   and   --nv-undo <outfile>: the same work as the Apply and Undo buttons, without the window.
                // This CHANGES the REAL NVIDIA driver profiles of the FiveM builds (like the buttons). Undo puts back what Apply replaced.
                bool nvApply = args[0] == "--nv-apply";
                int nvCap = 0;
                string nvOut = nvApply ? (args.Length >= 3 ? args[2] : null) : (args.Length >= 2 ? args[1] : null);
                if (nvOut == null || (nvApply && (!int.TryParse(args[1], out nvCap) || nvCap < Info.MinCap || nvCap > Info.MaxCap))) return 3;
                StringBuilder nvs = new StringBuilder();
                Action<string> nvlog = delegate(string t) { nvs.AppendLine(t); };
                int nvBad = 1;
                try
                {
                    bool nvOk = NvApi.Available();
                    nvlog("NVIDIA driver profiles: " + (nvOk ? "available" : "NOT available"));
                    if (nvOk)
                    {
                        List<string> nvRoots = Finder.Roots(nvlog, null);
                        List<Build> nvBuilds = Finder.Builds(nvRoots, null);
                        nvlog("builds found: " + nvBuilds.Count);
                        if (nvBuilds.Count > 0)
                        {
                            nvBad = nvApply ? NvCli.Apply(nvCap, nvBuilds, nvlog) : NvCli.Undo(nvBuilds, nvlog);
                            foreach (Build nb in nvBuilds) nvlog("now: " + nb.FileName + " => " + NvWorker.StatusOf(nb, nvlog));
                        }
                    }
                }
                catch (Exception nvex) { nvs.AppendLine("EXCEPTION " + nvex); }
                File.WriteAllText(nvOut, nvs.ToString());
                return nvBad == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && (args[0] == "--tidy-look" || args[0] == "--tidy-run"))
            {
                // --tidy-look <outfile>: READ ONLY. What "Clean up old entries" would remove: old profiles that this tool made in the NVIDIA driver, and old entries of the Windows graphics list.
                // --tidy-run <outfile>: the same, and then REMOVES them (a copy of the driver database is saved first), like the button after a yes. It changes the REAL driver profiles and the REAL Windows list.
                StringBuilder tds = new StringBuilder();
                Action<string> tdlog = delegate(string tl) { tds.AppendLine(tl); };
                int tdBad = 0;
                try
                {
                    bool tdNvd = NvApi.Available();
                    tdlog("NVIDIA driver profiles: " + (tdNvd ? "available" : "not available"));
                    List<Build> tdBuilds = Finder.Builds(Finder.Roots(tdlog, null), null);
                    tdlog("builds on disk: " + tdBuilds.Count);
                    string tdRefuse; List<TidyItem> tdItems = Tidy.Look(tdBuilds, tdNvd, tdlog, out tdRefuse);
                    if (tdRefuse != null) { tdlog("cannot judge: " + tdRefuse); tdBad++; }
                    else
                    {
                        tdlog("old entries: " + tdItems.Count);
                        foreach (TidyItem ti in tdItems) tdlog("  " + ti.Kind + " | " + ti.Name + " | " + ti.Why);
                        if (args[0] == "--tidy-run" && tdItems.Count > 0)
                        {
                            int tdRemoved; string tdProblem = Tidy.Remove(tdItems, tdlog, out tdRemoved);
                            tdlog("removed " + tdRemoved + " of " + tdItems.Count + (tdProblem != null ? "; PROBLEM: " + tdProblem : ""));
                            if (tdProblem != null) tdBad++;
                        }
                    }
                }
                catch (Exception tdex) { tds.AppendLine("EXCEPTION " + tdex); tdBad++; }
                File.WriteAllText(args[1], tds.ToString());
                return tdBad == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--gpu-windows")
            {
                // --gpu-windows <outfile>: READ ONLY. What Windows itself says about each graphics adapter (the adapter type bits) and what the Toolbox decides from it (Windows first, the name as the fallback).
                StringBuilder wgs = new StringBuilder();
                try
                {
                    List<WinGpu.Adapter> wl = WinGpu.Query();
                    wgs.AppendLine("Windows lists " + wl.Count + " graphics adapter(s):");
                    foreach (WinGpu.Adapter a in wl) wgs.AppendLine("  " + a.Name + " | type bits 0x" + a.Flags.ToString("X") + " = " + WinGpu.Describe(a.Flags));
                    foreach (Gpu g in Finder.Gpus()) wgs.AppendLine("card: " + g.Vendor + " | " + g.Name + " => " + (g.Kind.Length > 0 ? g.Kind : "not sure") + " (decided by " + g.KindFrom + ")");
                    wgs.AppendLine("integrated chip AND dedicated card present (the switch would show): " + GpuPref.Wanted(Finder.Gpus()));
                }
                catch (Exception wex) { wgs.AppendLine("EXCEPTION " + wex); }
                File.WriteAllText(args[1], wgs.ToString());
                return 0;
            }
            if (args.Length >= 3 && args[0] == "--gpu-pref")
            {
                // --gpu-pref show|set|undo <outfile>: the Windows graphics preference of the FiveM builds. "set" puts them on the dedicated card (high performance), "undo" puts back what was there.
                // show only reads. set and undo change the Windows per-program graphics list of THIS user (the same list as Settings > System > Display > Graphics).
                StringBuilder gps = new StringBuilder();
                Action<string> gplog = delegate(string t) { gps.AppendLine(t); };
                int gpBad = 0;
                try
                {
                    List<Gpu> gpg = Finder.Gpus();
                    foreach (Gpu x in gpg) gplog("card: " + x.Vendor + " (" + (x.Kind.Length > 0 ? x.Kind : "?") + ") " + x.Name);
                    gplog("integrated chip AND dedicated card present (the switch would show): " + GpuPref.Wanted(gpg));
                    List<Build> gpb = Finder.Builds(Finder.Roots(gplog, null), null);
                    foreach (Build b in gpb)
                    {
                        if (args[1] == "set") { if (!GpuPref.Set(b.Exe, 2, gplog)) gpBad++; }
                        else if (args[1] == "undo") { if (!GpuPref.Undo(b.Exe, gplog)) gplog("  " + b.FileName + ": nothing to put back."); }
                        string raw = GpuPref.Raw(b.Exe);
                        gplog("now: " + b.Exe + " => " + (raw == null ? "(no entry: Windows decides)" : raw) + "   preference " + GpuPref.PrefOf(raw));
                    }
                    if (gpb.Count == 0) { gplog("no FiveM build found"); gpBad++; }
                }
                catch (Exception gpex) { gps.AppendLine("EXCEPTION " + gpex); gpBad++; }
                File.WriteAllText(args[2], gps.ToString());
                return gpBad == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--gpupref-selftest")
            {
                // --gpupref-selftest <outfile>: the graphics preference code on a SCRATCH registry key and a scratch file (the real list is never touched)
                StringBuilder gts = new StringBuilder();
                int gbad = 0;
                Action<bool, string> gck = delegate(bool good, string what) { if (!good) gbad++; gts.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                string gtmp = Path.Combine(Path.GetTempPath(), "fivemtoolbox-gputest-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(gtmp);
                    GpuPref.KeyPath = @"Software\FiveMToolbox-selftest\UserGpuPreferences";
                    GpuPref.MemoryOverride = Path.Combine(gtmp, "gpupref-before.txt");
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\FiveMToolbox-selftest", false);
                    Action<string> glog = delegate(string t) { gts.AppendLine("        " + t); };
                    string e1 = @"C:\fake\FiveM_b0001_GTAProcess.exe", e2 = @"C:\fake\FiveM_b0002_GTAProcess.exe", e3 = @"C:\fake\FiveM.exe";

                    gck(GpuPref.Raw(e1) == null, "a program with no entry has no data");
                    gck(GpuPref.Set(e1, 2, glog), "set to high performance works");
                    gck(GpuPref.Raw(e1) == "GpuPreference=2;", "the data is exactly GpuPreference=2;");
                    gck(GpuPref.HasMemory(e1), "the old state (no entry) is remembered");
                    gck(GpuPref.Set(e1, 2, glog), "set again says: already");
                    gck(GpuPref.Raw(e1) == "GpuPreference=2;", "set again changed nothing");
                    gck(GpuPref.Undo(e1, glog), "undo works");
                    gck(GpuPref.Raw(e1) == null && !GpuPref.HasMemory(e1), "undo removed the entry again (there was none before) and forgot it");
                    gck(!GpuPref.Undo(e1, glog), "undo again: nothing to put back");

                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(GpuPref.KeyPath)) { k.SetValue(e2, "SwapEffectUpgradeEnable=1;GpuPreference=0;", RegistryValueKind.String); k.SetValue(e3, "SwapEffectUpgradeEnable=1;", RegistryValueKind.String); }
                    gck(GpuPref.Set(e2, 2, glog) && GpuPref.Raw(e2) == "SwapEffectUpgradeEnable=1;GpuPreference=2;", "an entry with other settings and GpuPreference=0: only the preference changes (" + GpuPref.Raw(e2) + ")");
                    gck(GpuPref.Undo(e2, glog) && GpuPref.Raw(e2) == "SwapEffectUpgradeEnable=1;GpuPreference=0;", "undo gives back the exact old data");
                    gck(GpuPref.Set(e3, 2, glog) && GpuPref.Raw(e3) == "SwapEffectUpgradeEnable=1;GpuPreference=2;", "an entry with other settings and no preference: the preference is added, the rest is kept (" + GpuPref.Raw(e3) + ")");
                    gck(GpuPref.Undo(e3, glog) && GpuPref.Raw(e3) == "SwapEffectUpgradeEnable=1;", "undo gives back the exact old data");

                    Func<string, Gpu> mkg = delegate(string kind) { Gpu g0 = new Gpu(); g0.Kind = kind; g0.Name = "x"; g0.Vendor = "AMD"; return g0; };
                    gck(GpuPref.Wanted(new List<Gpu>(new Gpu[] { mkg("integrated"), mkg("dedicated") })), "integrated + dedicated: the switch is wanted (laptop, or desktop with a chip in the processor)");
                    gck(!GpuPref.Wanted(new List<Gpu>(new Gpu[] { mkg("dedicated") })), "only a dedicated card: no switch");
                    gck(!GpuPref.Wanted(new List<Gpu>(new Gpu[] { mkg("integrated") })), "only an integrated chip: no switch");
                    gck(!GpuPref.Wanted(new List<Gpu>(new Gpu[] { mkg("dedicated"), mkg("dedicated") })), "two dedicated cards: no switch");
                    gck(!GpuPref.Wanted(new List<Gpu>(new Gpu[] { mkg("integrated"), mkg("") })), "an integrated chip and a card of unknown kind: no switch");
                    gck(!GpuPref.Wanted(new List<Gpu>()), "no card: no switch");
                    gck(GpuPref.PrefOf("GpuPreference=1;") == 1 && GpuPref.PrefOf(null) == -1 && GpuPref.PrefOf("x") == -1, "the preference is read right");

                    // ---- "Clean up old entries": old entries of the Windows graphics list (scratch key; nothing real is touched)
                    string wGone = Path.Combine(gtmp, @"nowhere\FiveM_b0001_GTAProcess.exe"), wLive = Path.Combine(gtmp, "FiveM_b0002_GTAProcess.exe"), wLauncher = Path.Combine(gtmp, @"nowhere\FiveM.exe");
                    string wWinOnly = Path.Combine(gtmp, @"nowhere\FiveM_b0003_GTAProcess.exe"), wOther = Path.Combine(gtmp, @"nowhere\game.exe");
                    File.WriteAllText(wLive, "x");
                    using (RegistryKey k3 = Registry.CurrentUser.CreateSubKey(GpuPref.KeyPath)) { foreach (string old in k3.GetValueNames()) k3.DeleteValue(old, false); }
                    char offLetter = 'Z';
                    foreach (char ch in "ZYXWVUTSRQPONM") { if (!Directory.Exists(ch + @":\")) { offLetter = ch; break; } }
                    string wOffline = offLetter + @":\FiveM\FiveM_b0004_GTAProcess.exe";
                    using (RegistryKey k2 = Registry.CurrentUser.CreateSubKey(GpuPref.KeyPath))
                    {
                        k2.SetValue(wGone, "AppStatus=0;", RegistryValueKind.String); k2.SetValue(wLive, "GpuPreference=2;", RegistryValueKind.String); k2.SetValue(wLauncher, "GpuPreference=2;", RegistryValueKind.String);
                        k2.SetValue(wWinOnly, "AppStatus=0;", RegistryValueKind.String); k2.SetValue(wOther, "GpuPreference=2;", RegistryValueKind.String); k2.SetValue(wOffline, "GpuPreference=2;", RegistryValueKind.String);
                    }
                    gck(GpuPref.Set(wGone, 2, glog) && GpuPref.HasMemory(wGone) && GpuPref.Raw(wGone) == "AppStatus=0;GpuPreference=2;", "an entry made by this tool for a game file that will be gone (its old data is remembered)");
                    string wRefuse; List<TidyItem> wItems = Tidy.Look(new List<Build>(), false, glog, out wRefuse);
                    gck(wRefuse == null && wItems.Count == 1 && wItems[0].Kind == "winlist" && wItems[0].Name == wGone && wItems[0].Why == Tidy.WhyFile, "clean up finds only the entry of the gone game file (found " + wItems.Count + ")");
                    int wRemoved; string wProblem = Tidy.Remove(wItems, glog, out wRemoved);
                    gck(wProblem == null && wRemoved == 1 && GpuPref.Raw(wGone) == null && !GpuPref.HasMemory(wGone), "it is removed and forgotten");
                    gck(GpuPref.Raw(wLive) == "GpuPreference=2;" && GpuPref.Raw(wLauncher) == "GpuPreference=2;" && GpuPref.Raw(wWinOnly) == "AppStatus=0;" && GpuPref.Raw(wOther) == "GpuPreference=2;" && GpuPref.Raw(wOffline) == "GpuPreference=2;",
                        "a file that exists, the launcher, an entry made by Windows alone, another program and a drive that is not there all stay");
                    string wLog = Path.Combine(gtmp, "gpupref-removed.txt");
                    gck(File.Exists(wLog) && File.ReadAllText(wLog).Contains(wGone) && File.ReadAllText(wLog).Contains("AppStatus=0;GpuPreference=2;"), "the old data is written down in gpupref-removed.txt");
                    gck(Tidy.Look(new List<Build>(), false, glog, out wRefuse).Count == 0, "a second look finds nothing");
                }
                catch (Exception gex) { gts.AppendLine("EXCEPTION " + gex); gbad++; }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\FiveMToolbox-selftest", false); } catch (Exception) { }
                try { Directory.Delete(gtmp, true); } catch (Exception) { }
                gts.AppendLine(gbad == 0 ? "ALL PASSED" : "FAILED: " + gbad);
                File.WriteAllText(args[1], gts.ToString());
                return gbad == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--nv-settings")
            {
                // --nv-settings <outfile> [exe names ...]: READ ONLY. Every setting (number, name, value) of the driver profile of each program.
                // Without names it takes the FiveM builds found on this PC. With names (for example gta5_enhanced.exe) it shows the profiles of those programs.
                StringBuilder nss = new StringBuilder();
                Action<string> nslog = delegate(string t) { nss.AppendLine(t); };
                try
                {
                    List<string> names = new List<string>();
                    for (int ai = 2; ai < args.Length; ai++) names.Add(args[ai]);
                    if (names.Count == 0) foreach (Build b in Finder.Builds(Finder.Roots(nslog, null), null)) names.Add(b.FileName);
                    using (INvDrs d = NvApi.Factory())
                    {
                        int rc = d.Open();
                        nslog("open the driver profile database: " + NvApi.Describe(rc));
                        if (rc == 0) foreach (string n in names) { int r2 = d.DumpSettings(n, nslog); if (r2 != 0) nslog(n + ": could not list the settings: " + NvApi.Describe(r2)); }
                    }
                }
                catch (Exception nsex) { nss.AppendLine("EXCEPTION " + nsex); }
                File.WriteAllText(args[1], nss.ToString());
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--nv-list")
            {
                // --nv-list <outfile> [all]: READ ONLY. Every profile of the NVIDIA driver with its programs and its frame limit (without "all": the user made ones and anything with FiveM or GTA in it).
                StringBuilder nls = new StringBuilder();
                Action<string> nllog = delegate(string t) { nls.AppendLine(t); };
                try
                {
                    using (INvDrs d = NvApi.Factory())
                    {
                        int rc = d.Open();
                        nllog("open the driver profile database: " + NvApi.Describe(rc));
                        if (rc == 0) nllog("list: " + NvApi.Describe(d.List(nllog, args.Length >= 3 && args[2] == "all")));
                    }
                }
                catch (Exception nlex) { nls.AppendLine("EXCEPTION " + nlex); }
                File.WriteAllText(args[1], nls.ToString());
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--nv-probe")
            {
                // --nv-probe <outfile>: READ ONLY look at the NVIDIA driver profiles of the FiveM builds (nothing is changed)
                StringBuilder ps = new StringBuilder();
                Action<string> plog = delegate(string t) { ps.AppendLine(t); };
                try
                {
                    plog("nvapi64.dll in System32: " + File.Exists(Path.Combine(Environment.SystemDirectory, "nvapi64.dll")));
                    foreach (Gpu x in Finder.Gpus()) plog("card: " + x.Vendor + " (" + (x.Kind.Length > 0 ? x.Kind : "?") + ") " + x.Name + " | driver " + x.Driver + " | own memory " + (x.Vram > 0 ? CacheTool.Size(x.Vram) : "?"));
                    using (INvDrs d = NvApi.Factory())
                    {
                        int rc = d.Open();
                        plog("open the driver profile database: " + NvApi.Describe(rc));
                        plog("a new build gets its own profile, named like: \"" + NvWorker.ProfileNameFor("FiveM_b0000_GTAProcess.exe") + "\"");
                    }
                    List<string> proots = Finder.Roots(plog, null);
                    foreach (Build pb in Finder.Builds(proots, null)) plog("build " + pb.FileName + " => " + NvWorker.StatusOf(pb, plog));
                }
                catch (Exception pex) { ps.AppendLine("EXCEPTION " + pex); }
                File.WriteAllText(args[1], ps.ToString());
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--nv-selftest")
            {
                // --nv-selftest <outfile>: the NVIDIA logic against a pretend driver (no NVIDIA card needed, nothing real is touched)
                StringBuilder ns = new StringBuilder();
                int nbad = 0;
                Action<bool, string> ck = delegate(bool good, string what) { if (!good) nbad++; ns.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                string tmp = Path.Combine(Path.GetTempPath(), "fivemtoolbox-nvtest-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(tmp);
                    CapMemory.PathOverride = Path.Combine(tmp, "caps-before.txt");
                    NvApi.BackupTo = Path.Combine(tmp, "backup"); NvApi.DbDir = Path.Combine(tmp, "drs");
                    NvApi.Factory = delegate { return new NvFakeDrs(); };
                    GpuPref.KeyPath = @"Software\FiveMToolbox-selftest-nv\UserGpuPreferences"; GpuPref.MemoryOverride = Path.Combine(tmp, "gpupref-before.txt");
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\FiveMToolbox-selftest-nv", false);
                    Action<string> nlog = delegate(string t) { ns.AppendLine("        " + t); };
                    NvWorker nw = new NvWorker(); nw.Log = nlog;
                    Func<string, Build> mk = delegate(string fn) { Build bb = new Build(); bb.FileName = fn; bb.Exe = @"C:\fake\" + fn; bb.Dir = @"C:\fake"; bb.Root = @"C:\fake\FiveM.app"; return bb; };

                    Build b1 = mk("FiveM_b0001_GTAProcess.exe");
                    ck(NvWorker.StatusOf(b1, nlog) == "NVIDIA: no frame limit set", "a build in no profile says: no frame limit set");
                    string r = nw.Apply(b1, 140);
                    ck(r == "Set to 140.", "apply 140 -> " + r);
                    ck(NvFakeDrs.Committed.Count == 1 && NvFakeDrs.Committed[0].Name == NvWorker.ProfileNameFor(b1.FileName) && NvFakeDrs.Committed[0].Cap == 140, "a profile of its own was made for the build and holds 140");
                    ck(NvFakeDrs.Committed.Count == 1 && NvFakeDrs.Committed[0].Apps.Contains(b1.FileName), "the build was added to our profile");
                    ck(NvWorker.StatusOf(b1, nlog) == "NVIDIA: frame limit 140 FPS", "the status says 140 now");
                    string[] mem = CapMemory.Get(b1.Exe);
                    ck(mem != null && mem[0] == "nv::-1;;", "the old value (none) is remembered for Undo");
                    r = nw.Apply(b1, 140); ck(r == "Already set to 140.", "apply 140 again -> " + r);
                    r = nw.Apply(b1, 120); ck(r == "Set to 120.", "apply 120 -> " + r);
                    mem = CapMemory.Get(b1.Exe);
                    ck(mem != null && mem[0] == "nv::140;;", "the value before the last change (140) is remembered");
                    r = nw.Revert(b1, mem != null ? mem[0] : "", mem != null ? mem[1] : ""); ck(r == "Cap put back to 140 / 140.", "undo -> " + r);
                    ck(NvFakeDrs.Committed[0].Cap == 140, "the driver holds 140 again");
                    r = nw.Revert(b1, "nv::-1;;", "nv::-1;;"); ck(r == "Cap put back: no limit set.", "undo to no limit -> " + r);
                    ck(NvFakeDrs.Committed[0].Cap == -1, "the limit is gone from the driver");
                    r = nw.Revert(b1, "nv::-1;;", "nv::-1;;"); ck(r == "Nothing to undo: the cap is already back.", "undo again -> " + r);

                    NvFakeDrs.Prof other = new NvFakeDrs.Prof(); other.Name = "Some Game"; other.Apps.Add("FiveM_b0002_GTAProcess.exe"); other.Cap = 60; NvFakeDrs.Committed.Add(other);
                    int count = NvFakeDrs.Committed.Count;
                    Build b2 = mk("FiveM_b0002_GTAProcess.exe");
                    ck(NvWorker.StatusOf(b2, nlog) == "NVIDIA: frame limit 60 FPS", "a build in an existing profile shows that profile's limit (60)");
                    r = nw.Apply(b2, 140); ck(r == "Set to 140.", "apply 140 to the build in the existing profile -> " + r);
                    ck(NvFakeDrs.Committed.Count == count && NvFakeDrs.Committed[count - 1].Cap == 140, "the EXISTING profile was changed and no new profile was made");
                    mem = CapMemory.Get(b2.Exe);
                    ck(mem != null && mem[0] == "nv::60;;", "its old limit (60) is remembered");
                    r = nw.Revert(b2, mem != null ? mem[0] : "", mem != null ? mem[1] : ""); ck(r == "Cap put back to 60 / 60.", "undo -> " + r);
                    ck(NvFakeDrs.Committed[count - 1].Cap == 60, "the existing profile holds 60 again");

                    r = nw.Revert(mk("FiveM_b0003_GTAProcess.exe"), "nv::-1;;", "nv::-1;;"); ck(r == "Nothing to undo: the cap is already back.", "undo for a build in no profile -> " + r);

                    string[] what = new string[] { "the driver does not open", "writing fails", "saving fails", "saving says OK but keeps nothing", "saving says access denied" };
                    for (int fo = 1; fo <= 5; fo++)
                    {
                        Build bf = mk("FiveM_b00" + (10 + fo) + "_GTAProcess.exe");
                        string snap = NvFakeDrs.Dump();
                        NvFakeDrs.FailOn = fo;
                        r = nw.Apply(bf, 100);
                        NvFakeDrs.FailOn = 0;
                        ck(r.StartsWith("Could not change the NVIDIA driver profile"), what[fo - 1] + ": apply says it could not -> " + r);
                        if (fo == 5) ck(r.Contains("administrator"), "access denied: the message tells to try as administrator");
                        ck(NvFakeDrs.Dump() == snap, what[fo - 1] + ": the driver data did not change");
                        ck(CapMemory.Get(bf.Exe) == null, what[fo - 1] + ": nothing was remembered for Undo");
                    }
                    // the driver says "saved" but keeps the OLD value: the check after saving must notice (this time the build IS in a profile, so the check really reads a value)
                    NvFakeDrs.Prof keep = new NvFakeDrs.Prof(); keep.Name = "Another Game"; keep.Apps.Add("FiveM_b0020_GTAProcess.exe"); keep.Cap = 60; NvFakeDrs.Committed.Add(keep);
                    Build b20 = mk("FiveM_b0020_GTAProcess.exe");
                    string snap2 = NvFakeDrs.Dump();
                    NvFakeDrs.FailOn = 4;
                    r = nw.Apply(b20, 140);
                    NvFakeDrs.FailOn = 0;
                    ck(r.StartsWith("Could not change the NVIDIA driver profile"), "saving keeps the OLD value (the build is in a profile): apply says it could not -> " + r);
                    ck(NvFakeDrs.Dump() == snap2, "saving keeps the OLD value: the driver data did not change");
                    ck(CapMemory.Get(b20.Exe) == null, "saving keeps the OLD value: nothing was remembered for Undo");

                    NvFakeDrs.FailOn = 1;
                    ck(NvWorker.StatusOf(b1, nlog) == "Could not read the NVIDIA driver profiles", "the status says it could not read when the driver does not open");
                    NvFakeDrs.FailOn = 0;

                    // the command line path (--nv-apply and --nv-undo use it) against the pretend driver
                    NvFakeDrs.Committed = new List<NvFakeDrs.Prof>();
                    List<Build> two = new List<Build>(); two.Add(mk("FiveM_b0100_GTAProcess.exe")); two.Add(mk("FiveM.exe"));
                    ck(NvCli.Apply(140, two, nlog) == 0, "command line apply 140 for two builds: both set");
                    ck(NvWorker.StatusOf(two[0], nlog) == "NVIDIA: frame limit 140 FPS" && NvWorker.StatusOf(two[1], nlog) == "NVIDIA: frame limit 140 FPS", "command line apply: both show 140");
                    ck(NvCli.Undo(two, nlog) == 0, "command line undo: both put back");
                    ck(NvWorker.StatusOf(two[0], nlog) == "NVIDIA: no frame limit set" && NvWorker.StatusOf(two[1], nlog) == "NVIDIA: no frame limit set", "command line undo: both show no limit");
                    ck(NvCli.Undo(two, nlog) == 0, "command line undo again: nothing to undo is not an error");
                    NvFakeDrs.FailOn = 3; int badCount = NvCli.Apply(120, two, nlog); NvFakeDrs.FailOn = 0;
                    ck(badCount == 2, "command line apply when saving fails: both reported as failed (" + badCount + ")");

                    // the profile of a build exists with the right limit but the build is not in it: the build must be added AND saved (a missed Save would lose it)
                    NvFakeDrs.Prof half = new NvFakeDrs.Prof(); half.Name = NvWorker.ProfileNameFor("FiveM_b0200_GTAProcess.exe"); half.Cap = 140; NvFakeDrs.Committed.Add(half);
                    Build b200 = mk("FiveM_b0200_GTAProcess.exe");
                    r = nw.Apply(b200, 140);
                    ck(r == "Set to 140.", "the profile has the limit but not the build: apply -> " + r);
                    ck(NvFakeDrs.Committed.Exists(delegate(NvFakeDrs.Prof q) { return q.Name == half.Name && q.Apps.Contains(b200.FileName); }), "the build was added AND saved");
                    mem = CapMemory.Get(b200.Exe);
                    ck(mem != null && mem[0] == "nv::-1;;", "its old value is remembered as: no limit");

                    // the situation seen on the real laptop: FiveM made its OWN profile for the new build (found by the FULL path, 4 settings of its own, no frame limit),
                    // and a profile of ours that holds only the bare file name (with 140) is ignored by the driver. The cap must go into the profile the driver really uses.
                    Build b3 = new Build(); b3.FileName = "FiveM_b3889_GTAProcess.exe"; b3.Dir = @"C:\Users\Test\AppData\Local\FiveM\FiveM.app\data\cache\subprocess"; b3.Root = @"C:\Users\Test\AppData\Local\FiveM\FiveM.app"; b3.Exe = b3.Dir + @"\" + b3.FileName;
                    NvFakeDrs.Prof oursOld = new NvFakeDrs.Prof(); oursOld.Name = NvWorker.ProfileNameFor(b3.FileName); oursOld.Apps.Add(b3.FileName); oursOld.Cap = 140;
                    NvFakeDrs.Prof cfx = new NvFakeDrs.Prof(); cfx.Name = "Cfx.re: C:/Users/Test/AppData/Local/FiveM/FiveM.app/data/cache/subprocess/FiveM_b3889_GTAProcess.exe";
                    cfx.Apps.Add("c:/users/test/appdata/local/fivem/fivem.app/data/cache/subprocess/fivem_b3889_gtaprocess.exe"); cfx.Other = 4;
                    NvFakeDrs.Committed.Add(oursOld); NvFakeDrs.Committed.Add(cfx);
                    int profilesBefore = NvFakeDrs.Committed.Count;
                    Func<string, NvFakeDrs.Prof> byName = delegate(string pn) { return NvFakeDrs.Committed.Find(delegate(NvFakeDrs.Prof q) { return q.Name == pn; }); };
                    ck(NvWorker.StatusOf(b3, nlog) == "NVIDIA: no frame limit set", "FiveM's own profile (found by the full path) has no limit, so the status does not say 140 (our bare-name profile is ignored by the driver)");
                    r = nw.Apply(b3, 140); ck(r == "Set to 140.", "apply 140 when the driver uses FiveM's own profile -> " + r);
                    ck(byName(cfx.Name) != null && byName(cfx.Name).Cap == 140, "the limit went into the profile the driver really uses");
                    ck(byName(cfx.Name) != null && byName(cfx.Name).Other == 4, "the 4 settings FiveM put in its own profile are still there");
                    ck(NvFakeDrs.Committed.Count == profilesBefore && byName(oursOld.Name).Cap == 140 && byName(oursOld.Name).Apps.Count == 1, "no new profile was made and the old profile of ours was not touched");
                    ck(NvWorker.StatusOf(b3, nlog) == "NVIDIA: frame limit 140 FPS", "the status says 140 now");
                    mem = CapMemory.Get(b3.Exe);
                    ck(mem != null && mem[0] == "nv::-1;;", "the old value (none) is remembered for Undo");
                    r = nw.Revert(b3, mem != null ? mem[0] : "", mem != null ? mem[1] : ""); ck(r == "Cap put back: no limit set.", "undo -> " + r);
                    ck(byName(cfx.Name) != null && byName(cfx.Name).Cap == -1 && byName(cfx.Name).Other == 4, "undo took the limit out of FiveM's profile and left its 4 settings alone");
                    ck(byName(oursOld.Name).Cap == 140, "undo did not touch the profile of ours");
                    string snap3 = NvFakeDrs.Dump();
                    NvFakeDrs.FailOn = 4; r = nw.Apply(b3, 140); NvFakeDrs.FailOn = 0;
                    ck(r.StartsWith("Could not change the NVIDIA driver profile") && NvFakeDrs.Dump() == snap3, "the driver says saved but keeps nothing (FiveM's profile): the check reads the same profile, sees it, and apply says it could not -> " + r);
                    List<string> dumped2 = new List<string>();
                    using (INvDrs dd2 = NvApi.Factory()) { dd2.Open(); dd2.DumpSettings(b3.Exe, delegate(string t) { dumped2.Add(t); }); }
                    ck(dumped2.Exists(delegate(string t) { return t.Contains("Cfx.re:") && t.Contains("4 setting(s)"); }), "the settings dump with the full path shows FiveM's own profile");

                    // a build that is gone from disk: no profile is made for it (a profile that already exists is still updated)
                    int profilesNow = NvFakeDrs.Committed.Count;
                    Build gone1 = mk("FiveM_b0300_GTAProcess.exe"); gone1.Missing = true;
                    r = nw.Apply(gone1, 140); ck(r.StartsWith("Skipped"), "a build that is gone from disk and in no profile: apply skips it -> " + r);
                    ck(NvFakeDrs.Committed.Count == profilesNow, "no profile was made for it");
                    ck(CapMemory.Get(gone1.Exe) == null, "nothing was remembered for Undo");
                    ck(NvCli.Apply(140, new List<Build>(new Build[] { gone1 }), nlog) == 0, "command line apply: a skipped build is not an error");
                    NvFakeDrs.Prof goneProf = new NvFakeDrs.Prof(); goneProf.Name = NvWorker.ProfileNameFor("FiveM_b0301_GTAProcess.exe"); goneProf.Apps.Add("FiveM_b0301_GTAProcess.exe"); goneProf.Cap = 60; NvFakeDrs.Committed.Add(goneProf);
                    Build gone2 = mk("FiveM_b0301_GTAProcess.exe"); gone2.Missing = true;
                    r = nw.Apply(gone2, 140); ck(r == "Set to 140.", "a build that is gone from disk but already has a profile: that profile is still updated -> " + r);
                    r = nw.Revert(gone1, "nv::-1;;", "nv::-1;;"); ck(r == "Nothing to undo: the cap is already back.", "undo for the skipped build says nothing to undo -> " + r);

                    // the list (read only) shows the profiles that exist
                    List<string> listed = new List<string>();
                    using (INvDrs ld = NvApi.Factory()) { ld.Open(); ld.List(delegate(string t) { listed.Add(t); }, false); }
                    ck(listed.Exists(delegate(string t) { return t.Contains(NvWorker.ProfileNameFor("FiveM_b0200_GTAProcess.exe")) && t.Contains("FiveM_b0200_GTAProcess.exe") && t.Contains("frame limit 140"); }), "the list shows the profile of a build with its program and its limit");
                    ck(listed.Count >= 2 && listed[0].StartsWith("profiles in the driver database:"), "the list starts with the number of profiles");
                    List<string> dumped = new List<string>();
                    using (INvDrs dd = NvApi.Factory()) { dd.Open(); dd.DumpSettings("FiveM_b0200_GTAProcess.exe", delegate(string t) { dumped.Add(t); }); }
                    ck(dumped.Exists(delegate(string t) { return t.Contains("0x10835002") && t.Contains("Frame Rate Limiter") && t.Contains("(140)"); }), "the settings dump shows the frame limit of a profile with its number, name and value");

                    // ---- "Clean up old entries": old profiles of this tool in the driver
                    NvFakeDrs.FailOn = 0;
                    NvFakeDrs.Committed = new List<NvFakeDrs.Prof>();
                    Func<string, string, int, int, NvFakeDrs.Prof> tdMk = delegate(string pn, string app, int cap, int oth) { NvFakeDrs.Prof q = new NvFakeDrs.Prof(); q.Name = pn; if (app != null) q.Apps.Add(app); q.Cap = cap; q.Other = oth; return q; };
                    string cfxName = "Cfx.re: C:/Users/Test/AppData/Local/FiveM/FiveM.app/data/cache/subprocess/FiveM_b3889_GTAProcess.exe";
                    Build tdFive = mk("FiveM.exe"), td4000 = mk("FiveM_b4000_GTAProcess.exe"), td3570 = mk("FiveM_b3570_GTAProcess.exe"); td3570.Missing = true;
                    List<Build> tdBuilds = new List<Build>(new Build[] { b3, tdFive, td4000, td3570 });
                    NvFakeDrs.Committed.Add(tdMk(NvWorker.ProfileNameFor("FiveM_b3570_GTAProcess.exe"), "FiveM_b3570_GTAProcess.exe", 140, 4));      // old: the build is not on disk
                    NvFakeDrs.Committed.Add(tdMk(NvWorker.ProfileNameFor("FiveM.exe"), "FiveM.exe", -1, 0));                                           // old: empty
                    NvFakeDrs.Committed.Add(tdMk(NvWorker.ProfileNameFor("FiveM_b3889_GTAProcess.exe"), "FiveM_b3889_GTAProcess.exe", 140, 0));         // old: the driver uses FiveM's own profile
                    NvFakeDrs.Committed.Add(tdMk(cfxName, "c:/users/test/appdata/local/fivem/fivem.app/data/cache/subprocess/fivem_b3889_gtaprocess.exe", 140, 4));   // FiveM's own: stays
                    NvFakeDrs.Committed.Add(tdMk(NvWorker.ProfileNameFor("FiveM_b4000_GTAProcess.exe"), "FiveM_b4000_GTAProcess.exe", 100, 0));       // in use: stays
                    NvFakeDrs.Committed.Add(tdMk("Some Game", "somegame.exe", 60, 3));                                                                 // another program: stays
                    NvFakeDrs.Committed.Add(tdMk("FiveM_b3889_GTAProcess.exe", null, 80, 1));                                                          // made by the NVIDIA app: stays
                    NvFakeDrs.Committed.Add(tdMk(NvWorker.ProfileNameFor("FiveM_b0500_GTAProcess.exe"), null, 140, 0));                                 // old: no program at all
                    string tdRefuse;
                    List<TidyItem> tdItems = Tidy.Look(tdBuilds, true, nlog, out tdRefuse);
                    Func<string, string, bool> tdHas = delegate(string pn, string why) { return tdItems.Exists(delegate(TidyItem q) { return q.Kind == "nvidia" && q.Name == pn && q.Why == why; }); };
                    ck(tdRefuse == null && tdItems.Count == 4, "clean up finds 4 old profiles (found " + tdItems.Count + ")");
                    ck(tdHas(NvWorker.ProfileNameFor("FiveM_b3570_GTAProcess.exe"), Tidy.WhyGone), "the profile of a build that is not on disk is old");
                    ck(tdHas(NvWorker.ProfileNameFor("FiveM.exe"), Tidy.WhyEmpty), "an empty profile is old");
                    ck(tdHas(NvWorker.ProfileNameFor("FiveM_b3889_GTAProcess.exe"), Tidy.WhyIgnored), "our profile that the driver ignores (FiveM's own profile wins) is old");
                    ck(tdHas(NvWorker.ProfileNameFor("FiveM_b0500_GTAProcess.exe"), Tidy.WhyGone), "our profile with no program at all is old");
                    ck(!tdItems.Exists(delegate(TidyItem q) { return q.Name == cfxName || q.Name == "Some Game" || q.Name == "FiveM_b3889_GTAProcess.exe" || q.Name == NvWorker.ProfileNameFor("FiveM_b4000_GTAProcess.exe"); }),
                        "FiveM's own profile, the profile of a build in use, another program's profile and the NVIDIA app's profile are NOT touched");
                    string tdSnapAll = NvFakeDrs.Dump();
                    ck(Tidy.Look(new List<Build>(new Build[] { td3570 }), true, nlog, out tdRefuse).Count == 0 && tdRefuse == "build" && NvFakeDrs.Dump() == tdSnapAll, "no build on disk: nothing is judged and nothing changes");
                    NvFakeDrs.FailOn = 1; Tidy.Look(tdBuilds, true, nlog, out tdRefuse); NvFakeDrs.FailOn = 0;
                    ck(tdRefuse == "driver", "the driver does not open: clean up says it could not read it");
                    CapMemory.Save(td3570.Exe, "nv::-1;;", "nv::-1;;");
                    int tdRemoved; string tdProblem;
                    // failures first: nothing may be lost
                    string[] tdFailText = new string[] { "the driver does not open", "removing fails", "saving fails", "saving says OK but keeps nothing", "saving says access denied" };
                    for (int fo = 1; fo <= 5; fo++)
                    {
                        NvFakeDrs.FailOn = fo; tdProblem = Tidy.Remove(tdItems, nlog, out tdRemoved); NvFakeDrs.FailOn = 0;
                        ck(tdProblem != null && tdRemoved == 0, tdFailText[fo - 1] + ": clean up says it could not, and counts nothing as removed -> " + tdProblem);
                        ck(NvFakeDrs.Dump() == tdSnapAll, tdFailText[fo - 1] + ": the driver data did not change");
                        if (fo == 5) ck(tdProblem != null && tdProblem.Contains("administrator"), "access denied: the message tells to try as administrator");
                    }
                    ck(CapMemory.Get(td3570.Exe) != null, "after the failures the Undo memory is still there");
                    int tdBefore = NvFakeDrs.Committed.Count;
                    tdProblem = Tidy.Remove(tdItems, nlog, out tdRemoved);
                    ck(tdProblem == null && tdRemoved == 4, "clean up removes the 4 old profiles (removed " + tdRemoved + ", " + tdProblem + ")");
                    ck(NvFakeDrs.Committed.Count == tdBefore - 4, "the driver holds 4 profiles less");
                    ck(byName(cfxName) != null && byName(cfxName).Cap == 140 && byName(cfxName).Other == 4, "FiveM's own profile is the same as before");
                    ck(byName("Some Game") != null && byName("Some Game").Cap == 60 && byName("FiveM_b3889_GTAProcess.exe") != null && byName(NvWorker.ProfileNameFor("FiveM_b4000_GTAProcess.exe")) != null && byName(NvWorker.ProfileNameFor("FiveM_b4000_GTAProcess.exe")).Cap == 100, "the other profiles are the same as before");
                    ck(CapMemory.Get(td3570.Exe) == null, "the Undo memory of the removed build is gone");
                    ck(Tidy.Look(tdBuilds, true, nlog, out tdRefuse).Count == 0 && tdRefuse == null, "a second look finds nothing old");

                    ck(NvDrsNative.SettingVer == 0x13020 && NvDrsNative.ProfileVer == 0x11014 && NvDrsNative.AppVer[0] == 0x4500C && NvDrsNative.AppVer[1] == 0x3400C && NvDrsNative.AppVer[2] == 0x24008, "the struct versions equal the numbers in NVIDIA's header");
                    ck(NvDrsNative.SetCur + 4100 == NvDrsNative.SettingSize && NvDrsNative.SetId == 4 + NvDrsNative.Str, "the setting struct offsets add up to its size (12320)");
                    ck(NvApi.FrlFpsId == 0x10835002u, "the frame limit setting number is 0x10835002");
                }
                catch (Exception nex) { ns.AppendLine("EXCEPTION " + nex); nbad++; }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\FiveMToolbox-selftest-nv", false); } catch (Exception) { }
                try { Directory.Delete(tmp, true); } catch (Exception) { }
                ns.AppendLine(nbad == 0 ? "ALL PASSED" : "FAILED: " + nbad);
                File.WriteAllText(args[1], ns.ToString());
                return nbad == 0 ? 0 : 1;
            }
            if (args.Length >= 3 && args[0] == "--icon-test")
            {
                // --icon-test <exe path> <outfile>: can the icon be read from this exe? (read only; works for a network path too)
                string ihow; Icon ico = Info.IconOfExe(args[1], out ihow);
                string res = "exe: " + args[1] + "\r\nresult: " + (ico != null ? "icon found " + ico.Width + "x" + ico.Height : "NO icon") + "\r\nhow: " + ihow + "\r\n";
                try { if (ico != null) using (Bitmap ib = ico.ToBitmap()) res += "bitmap ok: " + ib.Width + "x" + ib.Height + ", centre pixel " + ib.GetPixel(ib.Width / 2, ib.Height / 2) + "\r\n"; }
                catch (Exception iex) { res += "bitmap FAILED: " + iex.Message + "\r\n"; }
                File.WriteAllText(args[2], res);
                return ico != null ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--gpu-kind-test")
            {
                // --gpu-kind-test <outfile>: is a card called integrated or dedicated the right way round? (names only, no hardware needed)
                string[][] cases = new string[][] {
                    new string[] { "AMD", "AMD Radeon(TM) 680M", "integrated" },
                    new string[] { "AMD", "AMD Radeon(TM) 780M Graphics", "integrated" },
                    new string[] { "AMD", "AMD Radeon(TM) Graphics", "integrated" },
                    new string[] { "AMD", "AMD Radeon(TM) Vega 8 Graphics", "integrated" },
                    new string[] { "AMD", "AMD Radeon RX 9070 XT", "dedicated" },
                    new string[] { "AMD", "AMD Radeon RX 6800M", "dedicated" },
                    new string[] { "AMD", "AMD Radeon RX 7600M XT", "dedicated" },
                    new string[] { "AMD", "AMD Radeon RX Vega 56", "dedicated" },
                    new string[] { "AMD", "AMD Radeon(TM) Pro WX 3200", "dedicated" },
                    new string[] { "AMD", "AMD Radeon HD 7970", "dedicated" },
                    new string[] { "NVIDIA", "NVIDIA GeForce RTX 4060 Laptop GPU", "dedicated" },
                    new string[] { "NVIDIA", "NVIDIA GeForce GTX 1650", "dedicated" },
                    new string[] { "Intel", "Intel(R) UHD Graphics 770", "integrated" },
                    new string[] { "Intel", "Intel(R) Iris(R) Xe Graphics", "integrated" },
                    new string[] { "Intel", "Intel(R) Arc(TM) A770 Graphics", "dedicated" },
                    new string[] { "Intel", "Intel(R) Arc(TM) A370M Graphics", "dedicated" },
                    new string[] { "Intel", "Intel(R) Arc(TM) Pro A60 Graphics", "dedicated" },
                    new string[] { "Intel", "Intel(R) Arc(TM) B580 Graphics", "dedicated" },
                    new string[] { "Intel", "Intel(R) Arc(TM) 140V GPU (16GB)", "integrated" },
                    new string[] { "Intel", "Intel(R) Arc(TM) Graphics", "integrated" },
                    new string[] { "Other", "Some Other Card", "" },
                };
                StringBuilder ks = new StringBuilder();
                int bad = 0;
                foreach (string[] c in cases)
                {
                    string got = Finder.KindOf(c[0], c[1]);
                    bool good = got == c[2];
                    if (!good) bad++;
                    ks.AppendLine((good ? "PASS  " : "FAIL  ") + c[1] + " -> " + (got.Length > 0 ? got : "(not sure)") + (good ? "" : "   expected " + (c[2].Length > 0 ? c[2] : "(not sure)")));
                }
                // Windows first (the type bits), the name only as the fallback. Bits: 1 render, 2 display, 4 software, 16 hybrid discrete, 32 hybrid integrated.
                object[][] wcases = new object[][] {
                    new object[] { "AMD", "AMD Radeon(TM) 680M", 0x23u, "integrated", "Windows" },
                    new object[] { "NVIDIA", "NVIDIA GeForce RTX 4060 Laptop GPU", 0x13u, "dedicated", "Windows" },
                    new object[] { "AMD", "AMD Radeon(TM) Graphics", 0x13u, "dedicated", "Windows" },        // the name says integrated, Windows says dedicated: Windows wins
                    new object[] { "AMD", "AMD Radeon RX 9070 XT", 0x23u, "integrated", "Windows" },          // the name says dedicated, Windows says integrated: Windows wins
                    new object[] { "Other", "Some Other Card", 0x13u, "dedicated", "Windows" },               // a name nobody knows, Windows knows
                    new object[] { "Intel", "Intel(R) Arc(TM) A770 Graphics", 0x23u, "integrated", "Windows" },
                    new object[] { "NVIDIA", "NVIDIA GeForce GTX 1650", 0x3u, "dedicated", "name" },          // no hybrid bits: the name decides
                    new object[] { "AMD", "AMD Radeon RX 9070 XT", 0x3u, "dedicated", "name" },
                    new object[] { "AMD", "AMD Radeon(TM) Graphics", 0x3u, "integrated", "name" },
                    new object[] { "Other", "Some Other Card", 0x3u, "", "name" },
                    new object[] { "AMD", "AMD Radeon(TM) Graphics", 0x33u, "integrated", "name" },          // both bits at once makes no sense: the name decides
                    new object[] { "AMD", "AMD Radeon RX 9070 XT", null, "dedicated", "name" },              // Windows gave no flags: the name decides
                };
                foreach (object[] c in wcases)
                {
                    string wsrc;
                    string wgot = Finder.KindOf((string)c[0], (string)c[1], c[2] == null ? (uint?)null : (uint?)(uint)c[2], out wsrc);
                    bool good = wgot == (string)c[3] && wsrc == (string)c[4];
                    if (!good) bad++;
                    ks.AppendLine((good ? "PASS  " : "FAIL  ") + c[1] + " with Windows bits " + (c[2] == null ? "(none)" : "0x" + ((uint)c[2]).ToString("X")) + " -> " + (wgot.Length > 0 ? wgot : "(not sure)") + " by " + wsrc + (good ? "" : "   expected " + (((string)c[3]).Length > 0 ? (string)c[3] : "(not sure)") + " by " + c[4]));
                }
                ks.AppendLine(bad == 0 ? "ALL PASSED" : "FAILED: " + bad);
                File.WriteAllText(args[1], ks.ToString());
                return bad == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--tab-order-test")
            {
                // --tab-order-test <outfile>: the tabs, the tiles of the start window, the bullets of the welcome window and the log filter buttons are in the agreed order
                // (FiveM cache, Frame cap, DNS tools, Graphics), and each one still leads to the same page number as before (0 frame cap, 1 DNS tools, 2 FiveM cache, 3 graphics).
                // The windows are built but never shown, and no real folder is touched.
                Loc.Force("en"); Theme.Force("light");
                StringBuilder tos = new StringBuilder();
                int tbad = 0;
                Action<bool, string> tck = delegate(bool ok, string what) { if (!ok) tbad++; tos.AppendLine((ok ? "PASS  " : "FAIL  ") + what); };
                try
                {
                    string[] wantTitles = new string[] { "FiveM cache", "Frame cap", "DNS tools", "Graphics" };
                    int[] wantPages = new int[] { 2, 0, 1, 3 };
                    MethodInfo clickIt = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
                    tck(Info.FirstPage == 2, "the first page is the FiveM cache (page number 2)");
                    using (MainForm mf = new MainForm())
                    {
                        List<ChromeTab> tabList = new List<ChromeTab>(mf.TabsByPage());
                        tabList.Sort(delegate(ChromeTab a, ChromeTab b) { return a.Left.CompareTo(b.Left); });
                        int got = -1;
                        mf.PageHook = delegate(int p) { got = p; };
                        for (int i = 0; i < 4; i++)
                        {
                            tck(tabList[i].Text == wantTitles[i], "tab " + (i + 1) + " from the left is \"" + wantTitles[i] + "\" (found \"" + tabList[i].Text + "\")");
                            got = -1; clickIt.Invoke(tabList[i], new object[] { EventArgs.Empty });
                            tck(got == wantPages[i], "a click on the tab \"" + wantTitles[i] + "\" opens page " + wantPages[i] + " (opened " + got + ")");
                        }
                    }
                    using (ChooserForm cf = new ChooserForm())
                    {
                        List<ToolTile> tiles = new List<ToolTile>();
                        foreach (Control c in cf.Body.Controls) { ToolTile tt = c as ToolTile; if (tt != null) tiles.Add(tt); }
                        tiles.Sort(delegate(ToolTile a, ToolTile b) { return a.Top.CompareTo(b.Top); });
                        tck(tiles.Count == 4, "the start window has four tiles (found " + tiles.Count + ")");
                        for (int i = 0; i < tiles.Count && i < 4; i++)
                        {
                            tck(tiles[i].Title == wantTitles[i], "tile " + (i + 1) + " from the top is \"" + wantTitles[i] + "\" (found \"" + tiles[i].Title + "\")");
                            cf.Choice = -1; clickIt.Invoke(tiles[i], new object[] { EventArgs.Empty });
                            tck(cf.Choice == wantPages[i], "a click on the tile \"" + wantTitles[i] + "\" gives page " + wantPages[i] + " (gave " + cf.Choice + ")");
                        }
                    }
                    using (DisclaimerForm df = new DisclaimerForm())
                    {
                        string[] prefixes = new string[] { "FiveM cache:", "Frame cap:", "DNS tools:", "Graphics:", "Administrator rights:" };
                        int[] tops = new int[] { -1, -1, -1, -1, -1 };
                        foreach (Control c in df.Body.Controls)
                        {
                            Label lb = c as Label; if (lb == null) continue;
                            for (int k = 0; k < prefixes.Length; k++) if (lb.Text.StartsWith(prefixes[k])) tops[k] = lb.Top;
                        }
                        bool inOrder = tops[0] >= 0;
                        for (int k = 1; k < tops.Length; k++) if (tops[k] <= tops[k - 1]) inOrder = false;
                        tck(inOrder, "the welcome bullets run FiveM cache, Frame cap, DNS tools, Graphics, Administrator rights (tops " + string.Join(", ", Array.ConvertAll(tops, delegate(int t) { return t.ToString(); })) + ")");
                    }
                    using (LogPanel lp = new LogPanel(false, false))
                    {
                        List<Pill> pl = (List<Pill>)typeof(LogPanel).GetField("pills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lp);
                        string[] tn = (string[])typeof(LogPanel).GetField("ToolNames", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                        string[] wantPills = new string[] { "All", "FiveM cache", "Frame cap", "DNS", "Graphics", "General" };
                        tck(pl.Count == wantPills.Length && tn.Length == wantPills.Length, "the log has " + wantPills.Length + " filter buttons (found " + pl.Count + " buttons, " + tn.Length + " names)");
                        for (int i = 0; i < pl.Count && i < wantPills.Length && i < tn.Length; i++)
                        {
                            tck(pl[i].Text == wantPills[i], "log button " + (i + 1) + " from the left is \"" + wantPills[i] + "\" (found \"" + pl[i].Text + "\")");
                            tck(i == 0 ? tn[i] == null : tn[i] == wantPills[i], "log button " + (i + 1) + " filters on \"" + wantPills[i] + "\" (filters on \"" + (tn[i] == null ? "everything" : tn[i]) + "\")");
                        }
                    }
                }
                catch (Exception tex) { tbad++; tos.AppendLine("FAIL  EXCEPTION " + tex); }
                tos.AppendLine(tbad == 0 ? "ALL PASSED" : "FAILED: " + tbad);
                File.WriteAllText(args[1], tos.ToString());
                return tbad == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--vram-probe")
            {
                // --vram-probe <outfile> [part of a program name]: READ ONLY. The real video memory use per program (Windows counter "GPU Process Memory", Dedicated Usage) and the LUID of every card.
                // Without a name it shows the 15 programs that hold the most; with a name (for example FiveM) only the programs whose name contains it.
                StringBuilder vps = new StringBuilder();
                try
                {
                    List<WinGpu.Adapter> vad = WinGpu.Query();
                    vps.AppendLine("cards (LUID | name | Windows type bits):");
                    foreach (WinGpu.Adapter a in vad) vps.AppendLine("  " + a.Luid + " | " + a.Name + " | 0x" + a.Flags.ToString("X") + " = " + WinGpu.Describe(a.Flags));
                    List<GpuMemSample> vs = GpuMem.ReadReal();
                    if (vs == null) vps.AppendLine("Windows gives no numbers for the counter GPU Process Memory (Dedicated Usage).");
                    else
                    {
                        string want = args.Length >= 3 ? args[2] : null;
                        vs.Sort(delegate(GpuMemSample x, GpuMemSample y) { return y.DedicatedBytes.CompareTo(x.DedicatedBytes); });
                        vps.AppendLine("instances read: " + vs.Count + (want == null ? " (the 15 biggest)" : " (only programs with \"" + want + "\" in the name)"));
                        int shown = 0;
                        foreach (GpuMemSample x in vs)
                        {
                            string pn = "?";
                            try { using (Process pp = Process.GetProcessById(x.Pid)) pn = pp.ProcessName; } catch (Exception) { }
                            if (want != null && pn.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            string cardName = "?";
                            foreach (WinGpu.Adapter a in vad) if (a.Luid == x.Luid) cardName = a.Name;
                            vps.AppendLine("  " + pn + " (pid " + x.Pid + ") on " + cardName + " [" + x.Luid + "]: " + CacheTool.Size(x.DedicatedBytes) + " (" + x.DedicatedBytes + " bytes)");
                            if (++shown >= (want == null ? 15 : 60)) break;
                        }
                        if (shown == 0) vps.AppendLine("  no program found" + (want != null ? " with \"" + want + "\" in the name" : ""));
                    }
                }
                catch (Exception vex) { vps.AppendLine("EXCEPTION " + vex); }
                File.WriteAllText(args[1], vps.ToString());
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--vram-test")
            {
                // --vram-test <outfile>: the live meter (pure: made-up readings), the peak, and the rule for a dedicated Intel card
                StringBuilder vts = new StringBuilder();
                int vbad = 0;
                Action<bool, string> vck = delegate(bool good, string what) { if (!good) vbad++; vts.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                long mb = 1048576L;
                const string ded = "0x00000000_0x0000aaaa", igp = "0x00000000_0x0000bbbb";
                Func<int, string, long, GpuMemSample> smp = delegate(int pid, string luid, long mbs) { GpuMemSample m = new GpuMemSample(); m.Pid = pid; m.Luid = luid; m.DedicatedBytes = mbs * mb; return m; };
                Func<int[], List<int>> ids = delegate(int[] arr0) { return new List<int>(arr0); };

                vck(VramMeter.Evaluate(new List<GpuMemSample>(), ids(new int[0]), ded, 8192 * mb).State == "off", "the game is not running: off");
                vck(VramMeter.Evaluate(null, ids(new int[] { 10 }), ded, 8192 * mb).State == "nodata", "the game runs but Windows gives no numbers (null): nodata");
                vck(VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(99, ded, 3000) }), ids(new int[] { 10 }), ded, 8192 * mb).State == "nodata", "the game runs but only other programs have a reading: nodata");
                VramState a = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 5200), smp(99, ded, 3000) }), ids(new int[] { 10 }), ded, 8192 * mb);
                vck(a.State == "ok" && a.UsedBytes == 5200 * mb && a.UsedPercent == 63 && a.Level == "good", "5200 MB of 8192 MB: ok, 63 %, green; another program's memory is not counted");
                vck(VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 6700) }), ids(new int[] { 10 }), ded, 8192 * mb).Level == "warn", "6700 MB of 8192 MB (82 %): orange");
                vck(VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 7600) }), ids(new int[] { 10 }), ded, 8192 * mb).Level == "bad", "7600 MB of 8192 MB (93 %): red");
                vck(VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 6500) }), ids(new int[] { 10 }), ded, 8192 * mb).Level == "good", "6500 MB of 8192 MB (79 %): still green");
                vck(VramMeter.LevelFor(80) == "good" && VramMeter.LevelFor(80.5) == "warn" && VramMeter.LevelFor(90) == "warn" && VramMeter.LevelFor(90.5) == "bad", "the limits: 80 green, above 80 orange, 90 orange, above 90 red");
                VramState two = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 2000), smp(11, ded, 1500) }), ids(new int[] { 10, 11 }), ded, 8192 * mb);
                vck(two.UsedBytes == 3500 * mb, "two game processes: their memory is added up (3500 MB)");
                VramState both = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 4000), smp(10, igp, 300) }), ids(new int[] { 10 }), ded, 8192 * mb);
                vck(both.UsedBytes == 4000 * mb && both.OtherBytes == 300 * mb && !both.OnOtherChip, "memory on the integrated chip is not counted for the dedicated card, and a little there is no warning");
                VramState wrong = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 100), smp(10, igp, 3000) }), ids(new int[] { 10 }), ded, 8192 * mb);
                vck(wrong.OnOtherChip && wrong.OtherBytes == 3000 * mb, "the game holds most on the integrated chip: it is said to run on another chip");
                VramState none = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, igp, 3000) }), ids(new int[] { 10 }), ded, 8192 * mb);
                vck(none.State == "ok" && none.UsedBytes == 0 && none.OnOtherChip, "nothing on the dedicated card, 3000 MB on the other chip: on another chip");
                VramState unknown = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 4000), smp(10, igp, 300) }), ids(new int[] { 10 }), null, 8192 * mb);
                vck(unknown.UsedBytes == 4000 * mb, "the card is not known (no LUID): the chip with the most is taken");
                VramState nototal = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 4000) }), ids(new int[] { 10 }), ded, 0);
                vck(nototal.State == "ok" && nototal.UsedBytes == 4000 * mb && nototal.UsedPercent == 0, "the size of the card is not known: only the memory in use");

                VramTracker tr = new VramTracker();
                VramState t1 = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 3000) }), ids(new int[] { 10 }), ded, 8192 * mb); tr.Update(t1);
                VramState t2 = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 5000) }), ids(new int[] { 10 }), ded, 8192 * mb); tr.Update(t2);
                VramState t3 = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(10, ded, 4000) }), ids(new int[] { 10 }), ded, 8192 * mb); tr.Update(t3);
                vck(t1.PeakBytes == 3000 * mb && t2.PeakBytes == 5000 * mb && t3.PeakBytes == 5000 * mb, "the highest value goes up and stays when the use goes down (3000, 5000, 4000 gives the peak 5000)");
                VramState toff = VramMeter.Evaluate(new List<GpuMemSample>(), ids(new int[0]), ded, 8192 * mb); tr.Update(toff);
                vck(toff.State == "off" && toff.PeakBytes == 5000 * mb && toff.PeakPercent == 61 && toff.PeakLevel == "good", "after the game closes the last peak is still known (5000 MB, 61 %)");
                VramState t4 = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(20, ded, 2000) }), ids(new int[] { 20 }), ded, 8192 * mb); tr.Update(t4);
                vck(t4.PeakBytes == 2000 * mb, "a new game starts a new peak (2000 MB, not 5000)");
                VramState t5 = VramMeter.Evaluate(new List<GpuMemSample>(new GpuMemSample[] { smp(20, ded, 1500), smp(30, ded, 1000) }), ids(new int[] { 30 }), ded, 8192 * mb); tr.Update(t5);
                vck(t5.PeakBytes == 1000 * mb, "a new game with other process ids (1000 MB) that starts without the game closing in between, also starts a new peak, not the old 2000 MB (" + t5.PeakBytes / mb + " MB)");

                // a dedicated Intel card
                Gpu arc = new Gpu(); arc.Vendor = "Intel"; arc.Kind = "dedicated"; arc.Name = "Intel(R) Arc(TM) A770 Graphics"; arc.Vram = 16384 * mb;
                Gpu uhd = new Gpu(); uhd.Vendor = "Intel"; uhd.Kind = "integrated"; uhd.Name = "Intel(R) UHD Graphics 770"; uhd.Vram = 128 * mb;
                Gpu rtx = new Gpu(); rtx.Vendor = "NVIDIA"; rtx.Kind = "dedicated"; rtx.Name = "NVIDIA GeForce RTX 4060"; rtx.Vram = 8192 * mb;
                vck(Finder.IsUnsupportedDedicated(arc) && !Finder.IsUnsupportedDedicated(uhd) && !Finder.IsUnsupportedDedicated(rtx), "only a dedicated Intel card is the unsupported kind (an Intel chip in the processor and an NVIDIA card are not)");
                vck(Finder.HasUnsupportedDedicated(new List<Gpu>(new Gpu[] { uhd, arc })) && !Finder.HasUnsupportedDedicated(new List<Gpu>(new Gpu[] { uhd, rtx })) && !Finder.HasUnsupportedDedicated(null), "a PC with a dedicated Intel card is found, a PC without one is not");
                string mt;
                vck(MainForm.ComputeMode(new List<Gpu>(new Gpu[] { arc }), out mt) == "none" && mt == MainForm.IntelOnlyText && mt.Contains("not supported at the moment"), "a dedicated Intel card alone: no mode, and the text says it is not supported at the moment");
                vck(MainForm.ComputeMode(new List<Gpu>(new Gpu[] { uhd, arc }), out mt) == "none" && mt == MainForm.IntelOnlyText, "an Intel chip in the processor plus an Intel Arc card: not supported at the moment");
                vck(MainForm.ComputeMode(new List<Gpu>(new Gpu[] { uhd }), out mt) == "none" && mt == "No supported card (AMD or NVIDIA).", "an Intel chip in the processor alone: the old text, no Arc message");
                vck(Budget.Pick(new List<Gpu>(new Gpu[] { arc })) == null && Budget.Pick(new List<Gpu>(new Gpu[] { arc, rtx })) == rtx, "the advice leaves an Intel Arc card out (and still takes another card)");

                // the real chain on THIS PC (read only), with dwm.exe in the place of the game
                List<GpuMemSample> realRead = GpuMem.ReadReal();
                List<int> dwm = new List<int>();
                foreach (Process dp in Process.GetProcessesByName("dwm")) { dwm.Add(dp.Id); dp.Dispose(); }
                List<Gpu> realCards = Finder.Gpus();
                Gpu realCard = Budget.Pick(realCards);
                if (realRead == null || dwm.Count == 0 || realCard == null) vck(true, "(skipped: this PC gives no counter numbers, has no dwm.exe, or has no card with a known memory)");
                else
                {
                    string realLuid = null;
                    foreach (WinGpu.Adapter ad in WinGpu.Query()) if (ad.Luid.Length > 0 && string.Equals(ad.Name, (realCard.Name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) realLuid = ad.Luid;
                    vck(realLuid != null, "the dedicated card of this PC (" + realCard.Name + ") is found by its LUID (" + realLuid + ")");
                    VramState rs = VramMeter.Evaluate(realRead, dwm, realLuid, realCard.Vram);
                    vck(rs.State == "ok" && rs.UsedBytes > 0 && rs.TotalBytes > 0 && rs.UsedBytes < rs.TotalBytes, "the real counter: dwm.exe holds " + CacheTool.Size(rs.UsedBytes) + " of the " + CacheTool.Size(rs.TotalBytes) + " of the dedicated card (" + rs.UsedPercent + " %)");
                }
                vts.AppendLine(vbad == 0 ? "ALL PASSED" : "FAILED: " + vbad);
                File.WriteAllText(args[1], vts.ToString());
                return vbad == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--budget-test")
            {
                // --budget-test <outfile>: the advice for the Extended Texture Budget (pure numbers, no files, no hardware)
                StringBuilder bgs = new StringBuilder();
                int bgbad = 0;
                Action<bool, string> bck = delegate(bool good, string what) { if (!good) bgbad++; bgs.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                long mb = 1048576L;
                int[][] cases = new int[][] { new int[] { 24576, 100 }, new int[] { 16384, 100 }, new int[] { 12288, 100 }, new int[] { 10240, 100 }, new int[] { 8192, 70 }, new int[] { 7956, 65 }, new int[] { 6144, 35 }, new int[] { 4096, 5 }, new int[] { 3072, 0 }, new int[] { 2048, 0 } };
                foreach (int[] c in cases) bck(Budget.Recommended(c[0] * mb) == c[1], c[0] + " MB of video memory -> recommended " + c[1] + " % (got " + Budget.Recommended(c[0] * mb) + ")");
                bck(Budget.Recommended(0) == -1, "no known memory: no advice");
                bck(Budget.EstimateMb(0) == 3056 && Budget.EstimateMb(100) == 8056, "the estimate gives the two numbers the user measured: 3056 MB at 0 % and 8056 MB at 100 %");
                bck(Budget.FreePercent(8192 * mb, 70) == 20, "an 8 GB card at 70 % keeps about 20 % free (" + Budget.FreePercent(8192 * mb, 70) + ")");
                bck(Budget.FreePercent(16384 * mb, 100) == 51, "a 16 GB card at 100 % keeps about 51 % free (" + Budget.FreePercent(16384 * mb, 100) + ")");
                bool rising = true, headroom = true; int prev = -1;
                for (int m = 1024; m <= 32768; m += 256)
                {
                    int r = Budget.Recommended(m * mb);
                    if (r < prev) rising = false;
                    prev = r;
                    if (r > 0 && r < 100 && Budget.FreePercent(m * mb, r) < 19) headroom = false;
                }
                bck(rising, "a bigger card never gets a lower advice (1 GB to 32 GB)");
                bck(headroom, "at the advice (below 100 %) at least 19 % of the card stays free, for every size from 1 GB to 32 GB");
                Func<string, string, long, Gpu> mkc = delegate(string kind, string name, long mbs) { Gpu g = new Gpu(); g.Kind = kind; g.Name = name; g.Vram = mbs * mb; return g; };
                Gpu pick = Budget.Pick(new List<Gpu>(new Gpu[] { mkc("integrated", "680M", 512), mkc("dedicated", "RTX 4060", 8192) }));
                bck(pick != null && pick.Name == "RTX 4060", "laptop: the dedicated card is the one the advice is for");
                pick = Budget.Pick(new List<Gpu>(new Gpu[] { mkc("integrated", "iGPU", 2048), mkc("dedicated", "RX 9070 XT", 16384) }));
                bck(pick != null && pick.Name == "RX 9070 XT", "desktop with a chip in the processor: the card");
                bck(Budget.Pick(new List<Gpu>(new Gpu[] { mkc("integrated", "iGPU", 2048) })) == null, "an integrated chip alone: no advice (it shares the PC memory)");
                pick = Budget.Pick(new List<Gpu>(new Gpu[] { mkc("dedicated", "small", 6144), mkc("dedicated", "big", 12288) }));
                bck(pick != null && pick.Name == "big", "two dedicated cards: the bigger one");
                pick = Budget.Pick(new List<Gpu>(new Gpu[] { mkc("", "unknown kind", 8192) }));
                bck(pick != null && pick.Name == "unknown kind", "a card of unknown kind with a known memory still gets advice");
                bck(Budget.Pick(new List<Gpu>(new Gpu[] { mkc("dedicated", "no memory known", 0) })) == null && Budget.Pick(new List<Gpu>()) == null && Budget.Pick(null) == null, "a card without a known memory, no card at all, and a missing list: no advice");
                bck(Budget.Level(8192 * mb, 70, 70) == "good" && Budget.Level(8192 * mb, 20, 70) == "good", "8 GB card: up to the advice (70 %) the line is green");
                bck(Budget.Level(8192 * mb, 71, 70) == "warn" && Budget.Level(8192 * mb, 85, 70) == "warn", "8 GB card: above the advice, while 10 % or more stays free (71 % to 85 %), the line is orange");
                bck(Budget.Level(8192 * mb, 90, 70) == "bad" && Budget.Level(8192 * mb, 100, 70) == "bad", "8 GB card: when less than 10 % stays free (90 % and 100 %), the line is red");
                bck(Budget.Level(16384 * mb, 100, 100) == "good", "16 GB card at 100 %: green");
                bck(Budget.Level(4096 * mb, 100, 5) == "bad" && Budget.Level(4096 * mb, 5, 5) == "good", "4 GB card: 100 % is red, 5 % is green");
                List<Gpu> pl = new List<Gpu>(new Gpu[] { mkc("integrated", "iGPU", 2048), mkc("dedicated", "RX 9070 XT", 16384) });
                Finder.ApplyPretend(pl, 8192);
                bck(pl[0].Vram == 2048 * mb && pl[1].Vram == 8192 * mb && Budget.Recommended(Budget.Pick(pl).Vram) == 70, "test mode, pretending 8 GB: only the dedicated card changes, and the advice becomes 70");
                Finder.ApplyPretend(pl, 0);
                bck(pl[0].Vram == 2048 * mb && pl[1].Vram == 8192 * mb, "test mode off (0): nothing is changed");
                Finder.PretendVramMb = 8192;
                List<Gpu> realCards = Finder.Gpus();
                Finder.PretendVramMb = 0;
                int dedicatedHere = 0; bool allPretended = true;
                foreach (Gpu g in realCards) if (g.Kind == "dedicated") { dedicatedHere++; if (g.Vram != 8192 * mb) allPretended = false; }
                bck(allPretended, "a real search with the test mode on gives every dedicated card of this PC 8192 MB (" + dedicatedHere + " dedicated card(s) found)");
                bgs.AppendLine(bgbad == 0 ? "ALL PASSED" : "FAILED: " + bgbad);
                File.WriteAllText(args[1], bgs.ToString());
                return bgbad == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--gfx-ui-test")
            {
                // --gfx-ui-test <outfile>: the whole "Save the settings" flow of the Graphics tab (the switches start from the file, the save changes only what differs) on a made-up folder. Nothing real is touched.
                Loc.Force("en"); Theme.Force("light");
                StringBuilder us = new StringBuilder();
                int ubad = 0;
                Action<bool, string> uck = delegate(bool good, string what) { if (!good) ubad++; us.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                string udir = Path.Combine(Path.GetTempPath(), "fivemtoolbox-uitest-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(udir);
                    GfxTool.OverrideDir = udir;
                    string cfgPath = Path.Combine(udir, "fivem.cfg");
                    File.WriteAllText(cfgPath, "bind keyboard F1 +vehicleHorn\r\nseta \"game_useAudioFrameLimiter\" \"false\"\r\nseta \"nui_useInProcessGpu\" \"true\"\r\nseta \"vid_budgetScale\" \"20\"\r\n", new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(udir, "gta5_settings.xml"), "<?xml version=\"1.0\"?>\r\n<Settings>\r\n  <graphics>\r\n    <PostFX value=\"0\" />\r\n  </graphics>\r\n</Settings>\r\n", new UTF8Encoding(false));
                    Func<string> cfgText = delegate { return File.ReadAllText(cfgPath); };
                    using (MainForm uf = new MainForm(3))
                    {
                        bool[] sw = uf.TestGfxSwitches();
                        uck(sw[0] && sw[1], "the two switches start ON when the file has nui_useInProcessGpu true and game_useAudioFrameLimiter false");
                        List<string> parts = uf.TestGfxSave(20, 0, false, true);
                        uck(parts.Count == 1 && parts[0] == "The interface lag fix is now off." && GfxTool.ReadFlag(GfxTool.UiFixName) == 0 && GfxTool.ReadFlag(GfxTool.LimiterName) == 0, "interface lag fix off: only nui_useInProcessGpu changed (false), the limiter stays off (" + string.Join("|", parts.ToArray()) + ")");
                        parts = uf.TestGfxSave(20, 0, false, false);
                        uck(parts.Count == 1 && parts[0] == "The stutter fix is now off." && GfxTool.ReadFlag(GfxTool.UiFixName) == 0 && GfxTool.ReadFlag(GfxTool.LimiterName) == 1, "stutter fix off: game_useAudioFrameLimiter is TRUE again (the limiter is on), nui stays false (" + string.Join("|", parts.ToArray()) + ")");
                        uf.TestGfxRefresh(); sw = uf.TestGfxSwitches();
                        uck(!sw[0] && !sw[1], "after a refresh the two switches follow the file (both off)");
                        parts = uf.TestGfxSave(20, 0, true, true);
                        uck(parts.Count == 2 && parts[0] == "The interface lag fix is now on." && parts[1] == "The stutter fix is now on." && GfxTool.ReadFlag(GfxTool.UiFixName) == 1 && GfxTool.ReadFlag(GfxTool.LimiterName) == 0, "both on: nui true and limiter false (" + string.Join("|", parts.ToArray()) + ")");
                        parts = uf.TestGfxSave(20, 0, true, true);
                        uck(parts.Count == 0, "nothing to change when the file already has these values");
                        parts = uf.TestGfxSave(100, 2, true, true);
                        uck(parts.Count == 2 && GfxTool.ReadBudget() == 100 && GfxTool.ReadPostFx() == 2 && GfxTool.ReadFlag(GfxTool.UiFixName) == 1 && GfxTool.ReadFlag(GfxTool.LimiterName) == 0, "the budget and Post FX still save, and the two options stay as they were (" + string.Join("|", parts.ToArray()) + ")");
                        uck(cfgText().Contains("bind keyboard F1 +vehicleHorn"), "the key bind is still there");

                        // the advice for the Extended Texture Budget: the button, the line under the number, the orange warning
                        Func<string, string, long, Gpu> mkc = delegate(string kind, string name, long mbs) { Gpu g = new Gpu(); g.Kind = kind; g.Name = name; g.Vendor = "NVIDIA"; g.Vram = mbs * 1048576L; return g; };
                        uf.TestSetGpus(new List<Gpu>(new Gpu[] { mkc("integrated", "AMD Radeon(TM) 680M", 512), mkc("dedicated", "NVIDIA GeForce RTX 4060 Laptop GPU", 8192) }));
                        string[] bb = uf.TestBudgetButton();
                        uck(bb[0] == "True" && bb[1] == "Recommended (70 %)", "8 GB laptop card: the button says Recommended (70 %) (" + bb[0] + ", " + bb[1] + ")");
                        uf.TestSetBudget(20);
                        string[] hh = uf.TestBudgetHint();
                        uck(hh[1] == "good" && hh[0].Contains("At 20 %") && hh[0].Contains("about 50 % free"), "at 20 % the line is green and shows the numbers (" + hh[0] + ")");
                        uf.TestBudgetClick();
                        uck(uf.TestBudgetValue() == 70, "a click on the button sets the number to 70");
                        hh = uf.TestBudgetHint();
                        uck(hh[1] == "good" && hh[0].Contains("At 70 %") && hh[0].Contains("about 20 % free"), "at 70 % the line is green and says about 20 % stays free (" + hh[0] + ")");
                        uck(uf.TestBudgetHintColor() == Theme.Good && Theme.Good != Theme.Warn && Theme.Warn != Theme.Bad && Theme.Good != Theme.Bad, "at 70 % the line has the green colour of the theme (and green, orange and red are three different colours)");
                        uf.TestSetBudget(85);
                        uck(uf.TestBudgetHintColor() == Theme.Warn, "at 85 % the line has the orange colour of the theme");
                        hh = uf.TestBudgetHint();
                        uck(hh[1] == "warn" && hh[0].StartsWith("Above 70 %"), "at 85 % the line is the orange warning (" + hh[0] + ")");
                        uf.TestSetBudget(71);
                        uck(uf.TestBudgetHint()[1] == "warn", "one step above the advice is already orange");
                        uf.TestSetBudget(100);
                        uck(uf.TestBudgetHintColor() == Theme.Bad, "at 100 % on an 8 GB card the line has the red colour of the theme");
                        hh = uf.TestBudgetHint();
                        uck(hh[1] == "bad" && hh[0].StartsWith("Too high:") && hh[0].Contains("about 7,9 GB") || hh[1] == "bad" && hh[0].StartsWith("Too high:") && hh[0].Contains("about 7.9 GB"), "at 100 % on an 8 GB card the line is red and says Too high (" + hh[0] + ")");
                        uf.TestSetBudget(90);
                        uck(uf.TestBudgetHint()[1] == "bad", "at 90 % (less than 10 % free) the line is red");
                        uf.TestSetGpus(new List<Gpu>(new Gpu[] { mkc("dedicated", "AMD Radeon RX 9070 XT", 16384) }));
                        bb = uf.TestBudgetButton(); uf.TestSetBudget(100); hh = uf.TestBudgetHint();
                        uck(bb[1] == "Recommended (100 %)" && hh[1] == "good", "16 GB card: the advice is 100 % and 100 % is green (" + bb[1] + ")");
                        uf.TestSetGpus(new List<Gpu>(new Gpu[] { mkc("integrated", "AMD Radeon(TM) Graphics", 2048) }));
                        bb = uf.TestBudgetButton(); hh = uf.TestBudgetHint();
                        uck(bb[0] == "False" && hh[0].StartsWith("Higher uses more video memory"), "an integrated chip alone: no button and the plain hint");
                        uf.TestSetGpus(new List<Gpu>());
                        uck(uf.TestBudgetButton()[0] == "False", "no card at all: no button");

                        // a dedicated Intel card is not supported at the moment: the window says so in three places
                        Gpu arc = new Gpu(); arc.Vendor = "Intel"; arc.Kind = "dedicated"; arc.Name = "Intel(R) Arc(TM) A770 Graphics"; arc.Vram = 16384L * 1048576L;
                        uf.TestSetGpusReal(new List<Gpu>(new Gpu[] { arc }));
                        uck(uf.TestModeLabel().StartsWith("Intel Arc (a dedicated Intel card) was found. It is not supported at the moment"), "Intel Arc alone: the mode line says it is not supported at the moment (" + uf.TestModeLabel() + ")");
                        uck(uf.TestBudgetButton()[0] == "False" && uf.TestBudgetHint()[0].StartsWith("Not supported at the moment for an Intel Arc card"), "Intel Arc alone: no Recommended button, and the line under the number says not supported");
                        bool[] capb = uf.TestCapButtons();
                        uck(!capb[0] && !capb[1], "Intel Arc alone: the buttons \"Apply cap\" and \"Undo last change\" are off (nothing can be changed for a card that could not be tested)");
                        uf.TestMeterPoll(new List<int>()); uf.TestStartVramRead();
                        uck(uf.TestMeter()[0].StartsWith("Not supported at the moment for an Intel Arc card"), "Intel Arc alone: a poll of the meter does not start, the line keeps saying not supported");
                        uck(uf.TestMeter()[0].StartsWith("Not supported at the moment for an Intel Arc card"), "Intel Arc alone: the meter says not supported (" + uf.TestMeter()[0] + ")");
                        uf.TestSetGpus(new List<Gpu>(new Gpu[] { arc, mkc("dedicated", "NVIDIA GeForce RTX 4060 Laptop GPU", 8192) }));
                        uck(uf.TestModeLabel().Contains("An Intel Arc card was also found: not supported at the moment.") && uf.TestBudgetButton()[0] == "True", "Intel Arc plus an NVIDIA card: a note on the mode line, and the NVIDIA card still gets its advice");

                        // the live meter: made-up readings of the counter, the same path as the timer
                        const string dLuid = "0x00000000_0x0000aaaa", oLuid = "0x00000000_0x0000bbbb";
                        Func<int, string, long, GpuMemSample> msm = delegate(int pid, string luid, long mbs) { GpuMemSample m = new GpuMemSample(); m.Pid = pid; m.Luid = luid; m.DedicatedBytes = mbs * 1048576L; return m; };
                        uf.TestSetGpus(new List<Gpu>(new Gpu[] { mkc("dedicated", "NVIDIA GeForce RTX 4060 Laptop GPU", 8192) }));
                        uf.TestSetMeterCard(dLuid, 8192L * 1048576L);
                        Func<List<GpuMemSample>> realReader = GpuMem.Reader;
                        try
                        {
                            GpuMem.Reader = delegate { return new List<GpuMemSample>(new GpuMemSample[] { msm(111, dLuid, 5200), msm(222, dLuid, 1000) }); };
                            uf.TestMeterPoll(new List<int>(new int[] { 111 }));
                            string[] mm = uf.TestMeter();
                            uck(mm[1] == "good" && mm[0].Contains("(63 %)") && mm[0].Contains("Highest this session") && uf.TestMeterColor() == Theme.Good && Math.Abs(uf.TestMeterFill() - 0.635f) < 0.01f, "5200 MB of 8192 MB: green text, green bar, the bar is 63 % full (" + mm[0] + ")");
                            GpuMem.Reader = delegate { return new List<GpuMemSample>(new GpuMemSample[] { msm(111, dLuid, 6700) }); };
                            uf.TestMeterPoll(new List<int>(new int[] { 111 }));
                            mm = uf.TestMeter();
                            uck(mm[1] == "warn" && mm[0].Contains("(82 %)") && uf.TestMeterColor() == Theme.Warn, "6700 MB: orange text and orange bar");
                            GpuMem.Reader = delegate { return new List<GpuMemSample>(new GpuMemSample[] { msm(111, dLuid, 7600) }); };
                            uf.TestMeterPoll(new List<int>(new int[] { 111 }));
                            mm = uf.TestMeter();
                            uck(mm[1] == "bad" && mm[0].Contains("(93 %)") && uf.TestMeterColor() == Theme.Bad, "7600 MB: red text and red bar");
                            GpuMem.Reader = delegate { return new List<GpuMemSample>(new GpuMemSample[] { msm(111, dLuid, 4000) }); };
                            uf.TestMeterPoll(new List<int>(new int[] { 111 }));
                            uck(uf.TestMeter()[0].Contains("(49 %)") && uf.TestMeter()[0].Contains("(") && uf.TestMeter()[1] == "good", "back to 4000 MB: green again");
                            uf.TestMeterPoll(new List<int>());
                            mm = uf.TestMeter();
                            uck(mm[0].StartsWith("FiveM is not running. Last time: highest") && mm[0].Contains("(93 %)") && mm[1] == "bad", "FiveM closed: the line says the highest value of the last time (93 %, red) (" + mm[0] + ")");
                            GpuMem.Reader = delegate { return null; };
                            uf.TestMeterPoll(new List<int>(new int[] { 111 }));
                            mm = uf.TestMeter();
                            uck(mm[0].StartsWith("FiveM is running, but Windows gives no video memory numbers") && mm[1] == "warn", "Windows gives no numbers: the line says so");
                            GpuMem.Reader = delegate { return new List<GpuMemSample>(new GpuMemSample[] { msm(111, oLuid, 3000), msm(111, dLuid, 100) }); };
                            uf.TestMeterPoll(new List<int>(new int[] { 111 }));
                            mm = uf.TestMeter();
                            uck(mm[0].StartsWith("FiveM runs mostly on another graphics chip") && mm[1] == "warn", "the game holds most on the other chip: the line says so");
                            uf.TestSetMeterCard(dLuid, 0);
                            GpuMem.Reader = delegate { return new List<GpuMemSample>(new GpuMemSample[] { msm(111, dLuid, 4000) }); };
                            uf.TestMeterPoll(new List<int>(new int[] { 111 }));
                            uck(uf.TestMeter()[0].StartsWith("FiveM uses ") && !uf.TestMeter()[0].Contains("%"), "the size of the card is not known: only the memory in use, no percent");
                        }
                        finally { GpuMem.Reader = realReader; }
                    }
                }
                catch (Exception uex) { us.AppendLine("EXCEPTION " + uex); ubad++; }
                GfxTool.OverrideDir = null;
                try { Directory.Delete(udir, true); } catch (Exception) { }
                us.AppendLine(ubad == 0 ? "ALL PASSED" : "FAILED: " + ubad);
                File.WriteAllText(args[1], us.ToString());
                return ubad == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--gfx-flags-test")
            {
                // --gfx-flags-test <outfile>: the two game options (interface lag fix, stutter fix) on a made-up fivem.cfg in a scratch folder. The real files are never touched.
                StringBuilder fs = new StringBuilder();
                int fbad = 0;
                Action<bool, string> fck = delegate(bool good, string what) { if (!good) fbad++; fs.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                string fdir = Path.Combine(Path.GetTempPath(), "fivemtoolbox-flagtest-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(fdir);
                    GfxTool.OverrideDir = fdir;
                    Action<string> flog = delegate(string t) { fs.AppendLine("        " + t); };
                    string cfg = Path.Combine(fdir, "fivem.cfg");
                    string[] baseLines = new string[] { "bind keyboard F1 +vehicleHorn", "rbind keyboard F2 NULL", "seta \"game_enableScaleformDebugLog\" \"false\"", "seta \"game_useAudioFrameLimiter\" \"true\"", "seta \"nui_useInProcessGpu2\" \"true\"", "seta \"nui_useInProcessGpu\" \"false\"", "seta \"vid_budgetScale\" \"20\"" };
                    File.WriteAllText(cfg, string.Join("\r\n", baseLines) + "\r\n", new UTF8Encoding(false));
                    string before = File.ReadAllText(cfg);

                    fck(GfxTool.ReadFlag(GfxTool.UiFixName) == 0 && GfxTool.ReadFlag(GfxTool.LimiterName) == 1, "false and true are read right (not mixed up with nui_useInProcessGpu2)");
                    fck(!GfxTool.UiFixOn() && !GfxTool.StutterFixOn(), "interface lag fix is off (the setting is false) and the stutter fix is off (the limiter is on)");
                    GfxTool.SetUiFix(true, flog);
                    string after1 = File.ReadAllText(cfg);
                    fck(GfxTool.ReadFlag(GfxTool.UiFixName) == 1 && GfxTool.UiFixOn(), "the interface lag fix is switched on: nui_useInProcessGpu is true");
                    fck(after1 == before.Replace("seta \"nui_useInProcessGpu\" \"false\"", "seta \"nui_useInProcessGpu\" \"true\""), "only that one line changed (the decoy nui_useInProcessGpu2, the key binds and the other lines are the same, CRLF kept)");
                    GfxTool.SetStutterFix(true, flog);
                    string after2 = File.ReadAllText(cfg);
                    fck(GfxTool.ReadFlag(GfxTool.LimiterName) == 0 && GfxTool.StutterFixOn(), "the stutter fix is switched on: game_useAudioFrameLimiter is FALSE (the limiter is off)");
                    fck(after2 == after1.Replace("seta \"game_useAudioFrameLimiter\" \"true\"", "seta \"game_useAudioFrameLimiter\" \"false\""), "only that one line changed");
                    GfxTool.SetStutterFix(false, flog);
                    fck(GfxTool.ReadFlag(GfxTool.LimiterName) == 1 && !GfxTool.StutterFixOn(), "the stutter fix is switched off again: the limiter is on (true)");
                    GfxTool.SetUiFix(false, flog); GfxTool.SetStutterFix(false, flog);
                    fck(File.ReadAllText(cfg) == before, "everything switched back gives the file exactly as it was");
                    string[] backups = Directory.Exists(Path.Combine(fdir, "backup")) ? Directory.GetFiles(Path.Combine(fdir, "backup"), "fivem.cfg.*") : new string[0];
                    fck(backups.Length >= 1, "a backup of fivem.cfg was made before the changes");
                    Array.Sort(backups);
                    fck(backups.Length >= 1 && File.ReadAllText(backups[0]) == before, "the oldest backup holds the file as it was before the FIRST change (changes in the same second must not overwrite it)");
                    int bindsBefore = 0, bindsAfter = 0;
                    foreach (string ln in before.Split('\n')) if (ln.StartsWith("bind") || ln.StartsWith("rbind")) bindsBefore++;
                    foreach (string ln in File.ReadAllText(cfg).Split('\n')) if (ln.StartsWith("bind") || ln.StartsWith("rbind")) bindsAfter++;
                    fck(bindsBefore == 2 && bindsAfter == 2, "the key binds are all still there");
                    File.WriteAllText(cfg, before, new UTF8Encoding(false));
                    string bdirT = Path.Combine(fdir, "backup"); if (Directory.Exists(bdirT)) Directory.Delete(bdirT, true);
                    GfxTool.SetUiFix(true, flog); GfxTool.SetStutterFix(true, flog);
                    string[] bk2 = Directory.GetFiles(bdirT, "fivem.cfg.*"); Array.Sort(bk2);
                    fck(bk2.Length >= 1 && File.ReadAllText(bk2[0]) == before, "two changes in a row: the oldest backup is the ORIGINAL file, not the file after the first change");

                    // lines that are not in the file yet (a fresh install, or after a Reset): FiveM's own defaults are interface lag fix OFF and limiter ON
                    File.WriteAllText(cfg, "bind keyboard F1 +vehicleHorn\nseta \"vid_budgetScale\" \"20\"\n", new UTF8Encoding(false));
                    fck(GfxTool.ReadFlag(GfxTool.UiFixName) == -1 && !GfxTool.UiFixOn() && !GfxTool.StutterFixOn(), "lines that are not in the file: both fixes show as off (FiveM's defaults)");
                    GfxTool.SetStutterFix(true, flog); GfxTool.SetUiFix(true, flog);
                    string fresh = File.ReadAllText(cfg);
                    fck(GfxTool.StutterFixOn() && GfxTool.UiFixOn() && !fresh.Contains("\r"), "both lines are added, and a file with LF line ends stays LF");
                    fck(fresh.Contains("seta \"game_useAudioFrameLimiter\" \"false\"") && fresh.Contains("seta \"nui_useInProcessGpu\" \"true\"") && fresh.Contains("bind keyboard F1 +vehicleHorn") && fresh.Contains("seta \"vid_budgetScale\" \"20\""), "the added lines have FiveM's own format and the old lines are untouched");

                    // other spellings FiveM could write
                    File.WriteAllText(cfg, "seta \"nui_useInProcessGpu\" \"1\"\nseta \"game_useAudioFrameLimiter\" \"0\"\n", new UTF8Encoding(false));
                    fck(GfxTool.UiFixOn() && GfxTool.StutterFixOn(), "1 and 0 are understood as true and false");

                    File.Delete(cfg);
                    fck(GfxTool.ReadFlag(GfxTool.UiFixName) == -2, "no file: reading says so");
                    bool threw = false; try { GfxTool.SetUiFix(true, flog); } catch (Exception) { threw = true; }
                    fck(threw && !File.Exists(cfg), "no file: saving refuses and does not invent a file");
                }
                catch (Exception fex) { fs.AppendLine("EXCEPTION " + fex); fbad++; }
                GfxTool.OverrideDir = null;
                try { Directory.Delete(fdir, true); } catch (Exception) { }
                fs.AppendLine(fbad == 0 ? "ALL PASSED" : "FAILED: " + fbad);
                File.WriteAllText(args[1], fs.ToString());
                return fbad == 0 ? 0 : 1;
            }
            if (args.Length >= 3 && args[0] == "--gfx-test")
            {
                // --gfx-test <folder with copies of fivem.cfg and gta5_settings.xml> <outfile>
                StringBuilder gs = new StringBuilder();
                Action<string> glog = delegate(string t) { gs.AppendLine(t); };
                try
                {
                    GfxTool.OverrideDir = args[1];
                    gs.AppendLine("before: budget=" + GfxTool.ReadBudget() + " postfx=" + GfxTool.ReadPostFx());
                    GfxTool.SetBudget(100, glog); gs.AppendLine("after SetBudget(100): budget=" + GfxTool.ReadBudget());
                    GfxTool.SetPostFx(2, glog); gs.AppendLine("after SetPostFx(2): postfx=" + GfxTool.ReadPostFx());
                    GfxTool.SetPostFx(0, glog); gs.AppendLine("after SetPostFx(0): postfx=" + GfxTool.ReadPostFx());
                }
                catch (Exception gex) { gs.AppendLine("EXCEPTION " + gex); }
                File.WriteAllText(args[2], gs.ToString());
                return 0;
            }
            if (args.Length >= 3 && args[0] == "--cache-test")
            {
                StringBuilder cs = new StringBuilder();
                Action<string> clog = delegate(string t) { cs.AppendLine(t); };
                List<CacheItem> items = CacheTool.Items(args[1]);
                foreach (CacheItem it in items) { long b = 0; foreach (string p in it.Paths) b += CacheTool.SizeOf(p); it.Bytes = b; clog("before: " + it.Key + " = " + b + " bytes"); }
                clog("is FiveM.app folder: " + CacheTool.IsFiveMApp(args[1]));
                clog("clear result: " + CacheTool.Clear(args[1], items, clog));
                foreach (CacheItem it in items) { long b = 0; foreach (string p in it.Paths) b += CacheTool.SizeOf(p); clog("after: " + it.Key + " = " + b + " bytes"); }
                File.WriteAllText(args[2], cs.ToString());
                return 0;
            }
            if (args.Length >= 1 && args[0] == "--reset-test")
            {
                // --reset-test <FiveM.app folder (a copy)> <settings folder (a copy)> <local folder (a copy)> <outfile>
                // all four are needed: a test can never fall back to the real folders
                if (args.Length < 5) return 3;
                StringBuilder rs = new StringBuilder();
                Action<string> rlog = delegate(string t) { rs.AppendLine(t); };
                try
                {
                    GfxTool.OverrideDir = args[2];
                    CacheTool.LocalOverride = args[3];
                    if (args.Length >= 6 && args[5] == "damage-backup")
                        CacheTool.AfterBackupCopy = delegate(string copyDir) { string[] fs = Directory.GetFiles(copyDir, "*", SearchOption.AllDirectories); if (fs.Length > 0) File.Delete(fs[0]); rlog("TEST: the backup copy was damaged on purpose (deleted " + fs.Length + " -> " + (fs.Length - 1) + " files)"); };
                    List<CacheItem> ritems = CacheTool.Items(args[1]);
                    foreach (CacheItem it in ritems)
                    {
                        long b = 0;
                        foreach (string p in it.Paths) { long pb = CacheTool.SizeOf(p); it.PathBytes.Add(pb); b += pb; }
                        it.Bytes = b; rlog("before: " + it.Key + " = " + b + " bytes");
                    }
                    rlog("binds counted: " + CacheTool.CountBinds());
                    int rc = CacheTool.Reset(args[1], ritems, rlog, delegate(string nm, double fr) { rlog("  progress " + fr.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + nm); });
                    rlog("reset result: " + rc);
                    foreach (CacheItem it in ritems) { long b = 0; foreach (string p in it.Paths) b += CacheTool.SizeOf(p); rlog("after: " + it.Key + " = " + b + " bytes"); }
                    foreach (string n in CacheTool.ResetNames) rlog("settings " + n + " exists: " + (File.Exists(Path.Combine(args[2], n)) || Directory.Exists(Path.Combine(args[2], n))));
                    foreach (string n in CacheTool.ResetLocalNames) rlog("local " + n + " exists: " + Directory.Exists(Path.Combine(args[3], n)));
                }
                catch (Exception rex) { rs.AppendLine("EXCEPTION " + rex); }
                File.WriteAllText(args[4], rs.ToString());
                return 0;
            }
            if (args.Length >= 2 && (args[0] == "--scan" || args[0] == "--apply"))
            {
                StringBuilder sb = new StringBuilder();
                Action<string> log = delegate(string s) { sb.AppendLine(s); };
                try
                {
                    List<Gpu> g = Finder.Gpus();
                    foreach (Gpu x in g) log("GPU: " + x.Vendor + " - " + x.Name + " | kind " + (x.Kind.Length > 0 ? x.Kind : "unknown") + " | own memory " + (x.Vram > 0 ? CacheTool.Size(x.Vram) : "unknown"));
                    List<Adl.AppInfo> apps = Adl.Apps();
                    List<string> roots = Finder.Roots(log, apps);
                    List<Build> builds = Finder.Builds(roots, apps);
                    foreach (Build b in builds)
                    {
                        Adl.ProfileValues v = Adl.Read(b.FileName, b.Dir);
                        log("BUILD " + b.Exe + " adlIndex=" + b.AdlIndex + " found=" + v.Found + " rc=" + v.Rc + " pf=" + v.Pf + " min=" + v.Min + " max=" + v.Max);
                    }
                    if (args[0] == "--apply")
                    {
                        int cap = int.Parse(args[1]);
                        if (cap < Info.MinCap || cap > Info.MaxCap) throw new Exception("cap out of range");
                        Worker w = new Worker(); w.Log = log;
                        foreach (Build b in builds) log("RESULT " + b.FileName + ": " + w.Apply(b, cap));
                    }
                }
                catch (Exception ex) { log("EXCEPTION " + ex); }
                File.WriteAllText(args[args.Length - 1], sb.ToString());
                return 0;
            }
            try
            {
                // the folder used before the program got its new name
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string oldDir = Path.Combine(local, "FiveMFrameCap"), newDir = Path.Combine(local, Info.Folder);
                if (Directory.Exists(oldDir) && !Directory.Exists(newDir)) Directory.Move(oldDir, newDir);
            }
            catch (Exception) { }
            bool isShot = args.Length >= 2 && (args[0] == "--shot" || args[0] == "--log-color-test" || args[0] == "--reset-ui-test" || args[0] == "--reset-form-test" || args[0] == "--dpi-test" || args[0] == "--pure-icon-test" || args[0] == "--log-fold-test");
            bool relaunched = false;
            foreach (string a0 in args) if (a0 == "--page" || a0 == "--relaunch") relaunched = true;
            if (!isShot)
            {
                if (!SingleInstance.Acquire(relaunched))
                {
                    SingleInstance.ShowOther();
                    return 0;
                }
                // the switch "Start as administrator" in the start window is on: restart with administrator rights before anything is shown
                if (!relaunched && !DnsTool.IsAdmin() && Settings.Get("startadmin", "0") == "1" && RestartElevated("--relaunch")) return 0;
            }
            Loc.Init();
            Theme.Init();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Anything that goes wrong without a handler still ends up in the log file.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { AppLog.Add("Unexpected error: " + e.Exception.Message); AppLog.Debug("unhandled UI exception: " + e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { AppLog.Add("Unexpected error: " + e.ExceptionObject); };
            if (args.Length >= 1 && args[0] == "--reset-form-test")
            {
                // --reset-form-test <outfile>: the rules of the big warning window
                if (args.Length < 2) return 3;
                StringBuilder fs = new StringBuilder();
                try
                {
                    ResetWarningForm wf = new ResetWarningForm(661);
                    CheckBox tick = null; IconButton danger = null, cancelB = null;
                    foreach (Control c in wf.Body.Controls)
                    {
                        if (c is CheckBox) tick = (CheckBox)c;
                        if (c is IconButton) { IconButton ib = (IconButton)c; if ((ib.Tag as string) == "danger") danger = ib; else cancelB = ib; }
                    }
                    fs.AppendLine("danger button found: " + (danger != null) + ", tick box found: " + (tick != null) + ", cancel button found: " + (cancelB != null));
                    fs.AppendLine("button enabled before ticking: " + danger.Enabled);
                    tick.Checked = true;
                    fs.AppendLine("button enabled after ticking: " + danger.Enabled);
                    tick.Checked = false;
                    fs.AppendLine("button enabled after un-ticking: " + danger.Enabled);
                    fs.AppendLine("Esc button is Cancel: " + (wf.CancelButton == cancelB) + ", Enter button (AcceptButton) is none: " + (wf.AcceptButton == null));
                    fs.AppendLine("danger button result is OK: " + (danger.DialogResult == DialogResult.OK) + ", cancel button result is Cancel: " + (cancelB.DialogResult == DialogResult.Cancel));
                    wf.Dispose();
                }
                catch (Exception fex) { fs.AppendLine("EXCEPTION " + fex); }
                File.WriteAllText(args[1], fs.ToString());
                return 0;
            }
            if (args.Length >= 1 && args[0] == "--reset-ui-test")
            {
                // --reset-ui-test <FiveM.app folder (a copy)> <settings folder (a copy)> <local folder (a copy)> <picture.png>: the real window runs the Reset work on copies
                if (args.Length < 5) return 3;
                StringBuilder us = new StringBuilder();
                try
                {
                    GfxTool.OverrideDir = args[2];
                    CacheTool.LocalOverride = args[3];
                    MainForm rf = new MainForm(2);
                    rf.Show();
                    DateTime t0 = DateTime.UtcNow.AddSeconds(6);
                    while (DateTime.UtcNow < t0) { Application.DoEvents(); Thread.Sleep(50); }
                    rf.TestReset(args[1]);
                    us.AppendLine("busy right after start: " + rf.IsBusy);
                    DateTime t1 = DateTime.UtcNow.AddSeconds(60);
                    while (rf.IsBusy && DateTime.UtcNow < t1) { Application.DoEvents(); Thread.Sleep(50); }
                    us.AppendLine("busy at the end: " + rf.IsBusy);
                    DateTime t2 = DateTime.UtcNow.AddSeconds(1.5);
                    while (DateTime.UtcNow < t2) { Application.DoEvents(); Thread.Sleep(50); }
                    using (Bitmap bmpU = new Bitmap(rf.Width, rf.Height)) { Snap(rf, bmpU); bmpU.Save(args[4], System.Drawing.Imaging.ImageFormat.Png); }
                    rf.Close();
                }
                catch (Exception uex) { us.AppendLine("EXCEPTION " + uex); }
                File.WriteAllText(args[4].Replace(".png", ".txt"), us.ToString());
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--log-color-test")
            {
                // --log-color-test <outfile>: a line with a result has the colour as its BACKGROUND, in the light and in the dark theme. The text stays the normal text colour and is easy to read.
                StringBuilder cs = new StringBuilder();
                int cfail = 0;
                Action<bool, string> cck = delegate(bool good, string what) { if (!good) cfail++; cs.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                Func<Color, double> lum = delegate(Color c)
                {
                    Func<int, double> ch = delegate(int v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); };
                    return 0.2126 * ch(c.R) + 0.7152 * ch(c.G) + 0.0722 * ch(c.B);
                };
                Func<Color, Color, double> contrast = delegate(Color a, Color b) { double la = lum(a), lb = lum(b); return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05); };
                try
                {
                    Loc.Force("en");
                    foreach (string mode in new string[] { "dark", "light" })
                    {
                        Theme.Force(mode);
                        List<int> bands = new List<int>();
                        for (int sv = 1; sv <= 5; sv++) bands.Add(LogPanel.TestBand(sv));
                        string[] msgs = new string[] { "colortest-" + mode + " plain line", "colortest-" + mode + " Backup saved ok", "colortest-" + mode + " still reports it", "colortest-" + mode + " warning here", "colortest-" + mode + " something failed", "colortest-" + mode + " unhandled exception" };
                        Form host = new Form(); host.Width = 900; host.Height = 500;
                        LogPanel lp = new LogPanel(false, true); host.Controls.Add(lp); Theme.Apply(host); host.Show();
                        for (int i = 0; i < msgs.Length; i++) { AppLog.Add(msgs[i]); AppLog.Add("colortest-" + mode + " plain after " + i); }
                        lp.TestRebuild();
                        Application.DoEvents(); using (Bitmap sb = new Bitmap(host.Width, host.Height)) { Snap(host, sb); sb.Save(args[1] + "." + mode + ".png", System.Drawing.Imaging.ImageFormat.Png); }   // a picture to look at
                        for (int sev = 1; sev <= 5; sev++)
                        {
                            int[] cc = lp.TestColors(msgs[sev]);
                            Color band = Color.FromArgb(LogPanel.TestBand(sev));
                            cck(cc != null && cc[0] == band.ToArgb(), mode + ": severity " + sev + " line has its band as the background (" + (cc != null ? cc[0].ToString("X8") : "none") + ", wanted " + band.ToArgb().ToString("X8") + ")");
                            cck(cc != null && cc[1] == Theme.Text.ToArgb(), mode + ": severity " + sev + " line has the normal text colour (" + (cc != null ? cc[1].ToString("X8") : "none") + ")");
                            double cr = contrast(Theme.Text, band);
                            cck(cr >= 4.5, mode + ": severity " + sev + ": the text is easy to read on its band (contrast " + cr.ToString("0.0") + ", at least 4.5)");
                            double cv = contrast(Theme.Bg2, band);
                            cck(cv >= 1.2, mode + ": severity " + sev + ": the band is easy to see against the log background (contrast " + cv.ToString("0.00") + ", at least 1.2)");
                            int[] after = lp.TestColors("colortest-" + mode + " plain after " + sev);
                            cck(after != null && !bands.Contains(after[0]), mode + ": the plain line right after it has no band");
                        }
                        int[] pl = lp.TestColors(msgs[0]);
                        cck(pl != null && !bands.Contains(pl[0]), mode + ": a plain line has no band");
                        bool distinct = true;
                        for (int i = 0; i < bands.Count; i++) for (int j = i + 1; j < bands.Count; j++) if (bands[i] == bands[j]) distinct = false;
                        cck(distinct, mode + ": the five bands are five different colours");
                        host.Close();
                    }
                }
                catch (Exception cex) { cfail++; cs.AppendLine("FAIL  EXCEPTION " + cex); }
                cs.AppendLine(cfail == 0 ? "ALL PASSED" : "FAILED: " + cfail);
                File.WriteAllText(args[1], cs.ToString());
                return cfail == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--log-fold-test")
            {
                // --log-fold-test <outfile>: a finished job in the log stays open while you stay on the tab, and folds up when you change tabs
                StringBuilder ls = new StringBuilder();
                int lfail = 0;
                Action<bool, string> lck = delegate(bool good, string what) { if (!good) lfail++; ls.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                Func<string, string, bool> has = delegate(string text, string part) { return text.IndexOf(part, StringComparison.Ordinal) >= 0; };
                try
                {
                    Theme.Force("dark"); Loc.Force("en");
                    Action pump = delegate { for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(30); } };
                    Func<string, int> job = delegate(string tag)
                    {
                        AppLog.BeginGroup(); AppLog.Add(tag + " one"); AppLog.Add(tag + " two"); AppLog.Add(tag + " three");
                        List<LogEntry> snap = AppLog.Snapshot();
                        int g = snap[snap.Count - 1].Group;
                        AppLog.EndGroup();
                        return g;
                    };
                    // the log on its own
                    Form host = new Form(); host.Width = 900; host.Height = 500;
                    LogPanel lp = new LogPanel(false, true); lp.StayOpenOnTab = true; host.Controls.Add(lp); host.Show(); pump();
                    int ga = job("alpha");
                    lp.TestRebuild(); string tx = lp.TestText();
                    lck(has(tx, "[-]") && has(tx, "alpha two"), "a finished job stays open while you stay on the tab");
                    lp.TabChanged(); lp.TestRebuild(); tx = lp.TestText();
                    lck(has(tx, "alpha one   ->   alpha three   (3 lines)") && !has(tx, "alpha two"), "it folds into one line when you change tabs");
                    int gb = job("beta"); lp.TestRebuild(); tx = lp.TestText();
                    lck(has(tx, "beta two") && !has(tx, "alpha two"), "a job started on the new tab is open, the old one stays folded");
                    lp.TestToggle(ga); tx = lp.TestText();
                    lck(has(tx, "alpha two"), "a job you open by hand opens");
                    lp.TabChanged(); lp.TestRebuild(); tx = lp.TestText();
                    lck(has(tx, "alpha two") && !has(tx, "beta two"), "after another tab change: your own choice stays open, the other job folds");
                    int gc = job("gamma"); lp.TestRebuild(); tx = lp.TestText();
                    lck(has(tx, "gamma two"), "a new job is open");
                    lp.TestToggle(gc); tx = lp.TestText();
                    lck(has(tx, "gamma one   ->   gamma three   (3 lines)") && !has(tx, "gamma two"), "a job you fold by hand stays folded while you stay on the tab");
                    AppLog.BeginGroup(); AppLog.Add("delta one"); AppLog.Add("delta failed to start"); AppLog.Add("delta three"); AppLog.EndGroup();
                    lp.TabChanged(); lp.TestRebuild(); tx = lp.TestText();
                    lck(has(tx, "delta failed to start"), "a job with a problem stays open after a tab change");
                    LogPanel pop = new LogPanel(true, false); Form host2 = new Form(); host2.Width = 900; host2.Height = 500; host2.Controls.Add(pop); host2.Show(); pump();
                    job("epsilon"); pop.TestRebuild(); tx = pop.TestText();
                    lck(has(tx, "epsilon one   ->   epsilon three   (3 lines)") && !has(tx, "epsilon two"), "the pop-out log (no tabs) folds a finished job at once, as before");
                    host2.Close(); host.Close();
                    // the main window
                    MainForm mf = new MainForm(0);
                    mf.Show();
                    DateTime ldend = DateTime.UtcNow.AddSeconds(8);
                    while (DateTime.UtcNow < ldend) { Application.DoEvents(); Thread.Sleep(40); }
                    job("zeta"); tx = mf.TestLogText();
                    lck(has(tx, "zeta two"), "main window: a finished job is open on the tab where it happened");
                    mf.ShowPage(1); pump(); tx = mf.TestLogText();
                    lck(!has(tx, "zeta two") && has(tx, "zeta one   ->   zeta three   (3 lines)"), "main window: it folds when you change to another tab");
                    job("eta"); tx = mf.TestLogText();
                    lck(has(tx, "eta two"), "main window: a job on the new tab is open");
                    mf.ShowPage(1); pump(); tx = mf.TestLogText();
                    lck(has(tx, "eta two"), "main window: pressing the tab you are on changes nothing");
                    mf.ShowPage(2); pump(); tx = mf.TestLogText();
                    lck(!has(tx, "eta two") && !has(tx, "zeta two"), "main window: the next tab change folds it too");
                    mf.Close();
                }
                catch (Exception lex) { lfail++; ls.AppendLine("FAIL  EXCEPTION " + lex); }
                ls.AppendLine(lfail == 0 ? "ALL PASSED" : "FAILED: " + lfail);
                File.WriteAllText(args[1], ls.ToString());
                return lfail == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--pure-icon-test")
            {
                // --pure-icon-test <outfile>: the desktop icon for pure mode. It is made in a scratch folder, never on the real desktop.
                StringBuilder pi = new StringBuilder();
                int pfail = 0;
                Action<bool, string> pck = delegate(bool good, string what) { if (!good) pfail++; pi.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                string ptmp = Path.Combine(Path.GetTempPath(), "fivemtoolbox-icontest-" + Guid.NewGuid().ToString("N"));
                string realLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), PureIcon.FileName);
                bool realBefore = File.Exists(realLnk);
                DateTime realTime = realBefore ? File.GetLastWriteTimeUtc(realLnk) : DateTime.MinValue;
                try
                {
                    Theme.Force("dark"); Loc.Force("en");
                    string desk = Path.Combine(ptmp, "desk"); Directory.CreateDirectory(desk);
                    string exeA = Path.Combine(ptmp, "FiveM A", "FiveM.exe"), exeB = Path.Combine(ptmp, "F\u00eeve B", "FiveM.exe");
                    Directory.CreateDirectory(Path.GetDirectoryName(exeA)); Directory.CreateDirectory(Path.GetDirectoryName(exeB));
                    File.WriteAllBytes(exeA, new byte[] { 1 }); File.WriteAllBytes(exeB, new byte[] { 2 });
                    PureIcon.FolderOverride = desk;
                    string lnk = Path.Combine(desk, "FiveM (pure mode).lnk");
                    string what, problem;
                    pck(PureIcon.FileName == "FiveM (pure mode).lnk", "the icon is called FiveM (pure mode).lnk");
                    problem = PureIcon.Make(exeA, out what);
                    pck(problem == null && what == "made" && File.Exists(lnk), "the first run makes the icon (result '" + what + "', problem '" + problem + "')");
                    string[] r1 = PureIcon.ReadAll(lnk);
                    pck(r1 != null && string.Equals(r1[0], exeA, StringComparison.OrdinalIgnoreCase), "it starts the FiveM.exe of that install (" + (r1 != null ? r1[0] : "none") + ")");
                    pck(r1 != null && r1[1] == "-pure_1", "with exactly -pure_1 (" + (r1 != null ? r1[1] : "none") + ")");
                    pck(r1 != null && string.Equals(r1[2], Path.GetDirectoryName(exeA), StringComparison.OrdinalIgnoreCase), "it starts in the folder of FiveM.exe (" + (r1 != null ? r1[2] : "none") + ")");
                    pck(r1 != null && r1[3] != null && r1[3].StartsWith(exeA, StringComparison.OrdinalIgnoreCase), "it has the icon of FiveM.exe (" + (r1 != null ? r1[3] : "none") + ")");
                    DateTime t1 = File.GetLastWriteTimeUtc(lnk);
                    Thread.Sleep(1300);
                    problem = PureIcon.Make(exeA, out what);
                    pck(problem == null && what == "already", "the same again says 'already' (result '" + what + "', problem '" + problem + "')");
                    pck(File.GetLastWriteTimeUtc(lnk) == t1, "and does not write the icon file again");
                    problem = PureIcon.Make(exeB, out what);
                    string[] r2 = PureIcon.ReadAll(lnk);
                    pck(problem == null && what == "updated" && r2 != null && string.Equals(r2[0], exeB, StringComparison.OrdinalIgnoreCase), "another install updates the icon, also a path with a space and an accent (result '" + what + "', target " + (r2 != null ? r2[0] : "none") + ")");
                    problem = PureIcon.Make(Path.Combine(ptmp, "nowhere", "FiveM.exe"), out what);
                    string[] r3 = PureIcon.ReadAll(lnk);
                    pck(problem != null && problem.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 && r3 != null && string.Equals(r3[0], exeB, StringComparison.OrdinalIgnoreCase), "a FiveM.exe that is not there is refused and the icon stays as it was (problem '" + problem + "')");
                    problem = PureIcon.Make(null, out what);
                    pck(problem != null, "no path at all is refused");
                    File.WriteAllBytes(lnk, new byte[] { 1, 2, 3, 4, 5 });
                    problem = PureIcon.Make(exeA, out what);
                    string[] r4 = PureIcon.ReadAll(lnk);
                    pck(problem == null && r4 != null && string.Equals(r4[0], exeA, StringComparison.OrdinalIgnoreCase), "a damaged icon file is replaced by a good one (result '" + what + "', problem '" + problem + "')");
                    string nodesk = Path.Combine(ptmp, "no desktop here");
                    PureIcon.FolderOverride = nodesk;
                    problem = PureIcon.Make(exeA, out what);
                    pck(problem != null && !Directory.Exists(nodesk), "a desktop folder that is not there is refused and not invented (problem '" + problem + "')");
                    PureIcon.FolderOverride = desk;
                    // the button in the window
                    MainForm pf = new MainForm(2);
                    string[] pb = pf.TestPureIconButton();
                    pck(pb[0] == "Desktop icon: pure mode" && pb[2] == "Card", "the cache tab has the button, in a block (" + pb[0] + ", in " + pb[2] + ")");
                    pck(pb[1] == "False", "the button is off while no FiveM install is known (" + pb[1] + ")");
                    pf.Dispose();
                }
                catch (Exception pex) { pfail++; pi.AppendLine("FAIL  EXCEPTION " + pex); }
                finally { PureIcon.FolderOverride = null; try { Directory.Delete(ptmp, true); } catch (Exception) { } }
                bool realAfter = File.Exists(realLnk);
                pck(realAfter == realBefore && (!realAfter || File.GetLastWriteTimeUtc(realLnk) == realTime), "the REAL desktop was not touched by this test");
                pi.AppendLine(pfail == 0 ? "ALL PASSED" : "FAILED: " + pfail);
                File.WriteAllText(args[1], pi.ToString());
                return pfail == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--dpi-test")
            {
                // --dpi-test <outfile>: looks at the main window, every tab and every small window at the screen scaling of THIS PC (this switch is DPI aware, like a normal start).
                // Add --no-dpi to see the old way (the picture stretched by Windows) for comparison. It shows the windows for a few seconds.
                StringBuilder ds = new StringBuilder();
                int dpass = 0, dfail = 0;
                Action<bool, string> dck = delegate(bool good, string what) { if (good) dpass++; else dfail++; ds.AppendLine((good ? "PASS  " : "FAIL  ") + what); };
                try
                {
                    Theme.Force("dark"); Loc.Force("en");
                    ds.AppendLine("screen scaling: aware=" + Dpi.Aware + ", scale=" + Dpi.Scale);
                    MainForm df = new MainForm(0);
                    df.Show();
                    DateTime dend = DateTime.UtcNow.AddSeconds(10);
                    while (DateTime.UtcNow < dend) { Application.DoEvents(); Thread.Sleep(50); }
                    df.DpiCheckAll(dck);
                    df.Close();
                    ChromeForm[] dsm = new ChromeForm[] { new DisclaimerForm(), new ChooserForm(), new WhyForm(), new ResetWarningForm(661), new LogForm() };
                    string[] dsn = new string[] { "welcome window", "start window", "why window", "reset warning window", "log window" };
                    for (int k = 0; k < dsm.Length; k++)
                    {
                        dsm[k].Show();
                        for (int i = 0; i < 14; i++) { Application.DoEvents(); Thread.Sleep(40); }
                        DpiCheck.Dialog(dsm[k], dsn[k], dck);
                        dsm[k].Close();
                    }
                }
                catch (Exception dex) { dfail++; ds.AppendLine("FAIL  EXCEPTION " + dex); }
                ds.AppendLine(dfail == 0 ? "ALL PASSED (" + dpass + " checks)" : "FAILED: " + dfail + " of " + (dpass + dfail));
                File.WriteAllText(args[1], ds.ToString());
                return dfail == 0 ? 0 : 1;
            }
            if (args.Length >= 2 && args[0] == "--shot")
            {
                // for testing: draw each page into a PNG:  --shot <name.png> [light|dark]
                if (args.Length >= 3) Theme.Force(args[2]);
                if (args.Length >= 4) Loc.Force(args[3]);
                snapScreen = args.Length >= 5 && args[4] == "screen";
                MainForm f = new MainForm();
                f.Show();
                DateTime end = DateTime.UtcNow.AddSeconds(14);
                while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(50); }
                for (int page = 0; page < 4; page++)
                {
                    f.ShowPage(page);
                    for (int i = 0; i < 12; i++) { Application.DoEvents(); Thread.Sleep(50); }
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        Snap(f, bmp);
                        bmp.Save(args[1].Replace(".png", "-page" + page + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                f.ShowPage(0);
                string[] kinds = new string[] { "run", "ok", "info", "bad" };
                foreach (string kd in kinds)
                {
                    f.DemoActivity(kd);
                    for (int i = 0; i < 26; i++) { Application.DoEvents(); Thread.Sleep(50); }
                    using (Bitmap bmpT = new Bitmap(f.Width, f.Height))
                    {
                        Snap(f, bmpT);
                        bmpT.Save(args[1].Replace(".png", "-toast-" + kd + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                f.ShowPage(0);
                f.DemoGpus();
                for (int i = 0; i < 12; i++) { Application.DoEvents(); Thread.Sleep(50); }
                using (Bitmap bmpG = new Bitmap(f.Width, f.Height)) { Snap(f, bmpG); bmpG.Save(args[1].Replace(".png", "-gpus3.png"), System.Drawing.Imaging.ImageFormat.Png); }
                f.ShowPage(3);
                List<Gpu> demo8 = new List<Gpu>(); Gpu d8 = new Gpu(); d8.Vendor = "NVIDIA"; d8.Name = "NVIDIA GeForce RTX 4060 Laptop GPU"; d8.Kind = "dedicated"; d8.Vram = 8192L * 1048576L; demo8.Add(d8);
                int[] demoPct = new int[] { 70, 85, 100 }; string[] demoName = new string[] { "green", "orange", "red" };
                for (int dpi = 0; dpi < 3; dpi++)
                {
                    f.TestSetGpus(demo8); f.TestSetBudget(demoPct[dpi]);
                    for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(50); }
                    using (Bitmap bmpB = new Bitmap(f.Width, f.Height)) { Snap(f, bmpB); bmpB.Save(args[1].Replace(".png", "-budget-" + demoName[dpi] + ".png"), System.Drawing.Imaging.ImageFormat.Png); }
                }
                Gpu arcDemo = new Gpu(); arcDemo.Vendor = "Intel"; arcDemo.Name = "Intel(R) Arc(TM) A770 Graphics"; arcDemo.Kind = "dedicated"; arcDemo.Vram = 16384L * 1048576L; arcDemo.Driver = "32.0.101.6319";
                f.TestSetGpusReal(new List<Gpu>(new Gpu[] { arcDemo }));
                f.TestMeterPoll(new List<int>());
                f.ShowPage(0);
                for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(50); }
                using (Bitmap bmpA = new Bitmap(f.Width, f.Height)) { Snap(f, bmpA); bmpA.Save(args[1].Replace(".png", "-intel-arc-page0.png"), System.Drawing.Imaging.ImageFormat.Png); }
                f.ShowPage(3);
                for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(50); }
                using (Bitmap bmpA3 = new Bitmap(f.Width, f.Height)) { Snap(f, bmpA3); bmpA3.Save(args[1].Replace(".png", "-intel-arc-page3.png"), System.Drawing.Imaging.ImageFormat.Png); }
                f.ShowPage(2);
                f.DemoResetMode(true);
                for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(50); }
                using (Bitmap bmpR = new Bitmap(f.Width, f.Height)) { Snap(f, bmpR); bmpR.Save(args[1].Replace(".png", "-reset-on.png"), System.Drawing.Imaging.ImageFormat.Png); }
                f.DemoResetMode(false);
                f.ShowPage(0);
                f.SnapLanguageMenu(args[1].Replace(".png", "-langmenu.png"));
                LogForm lf = new LogForm();
                lf.Show();
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(50); }
                using (Bitmap bmp2 = new Bitmap(lf.Width, lf.Height))
                {
                    Snap(lf, bmp2);
                    bmp2.Save(args[1].Replace(".png", "-popout.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                lf.Close();
                Form[] smalls = new Form[] { new DisclaimerForm(), new ChooserForm(), new WhyForm(), new ResetWarningForm(661) };
                string[] smallNames = new string[] { "welcome", "chooser", "why", "reset" };
                for (int k = 0; k < smalls.Length; k++)
                {
                    smalls[k].Show();
                    for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(50); }
                    using (Bitmap bmp3 = new Bitmap(smalls[k].Width, smalls[k].Height))
                    {
                        Snap(smalls[k], bmp3);
                        bmp3.Save(args[1].Replace(".png", "-" + smallNames[k] + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    smalls[k].Close();
                }
                f.Close();
                return 0;
            }
            // restart after a language change: open straight on the same tab
            for (int ai = 0; ai + 1 < args.Length; ai++)
            {
                int relaunchPage;
                if (args[ai] == "--page" && int.TryParse(args[ai + 1], out relaunchPage) && relaunchPage >= 0 && relaunchPage <= 3)
                {
                    Application.Run(new MainForm(relaunchPage));
                    return 0;
                }
            }
            // a welcome window (until "do not show again" is ticked), then a small window to pick the tool
            if (Settings.Get("disclaimer", "") != "accepted")
            {
                using (DisclaimerForm d = new DisclaimerForm())
                {
                    if (d.ShowDialog() != DialogResult.OK) return 0;
                    if (d.DontShowAgain) Settings.Set("disclaimer", "accepted");
                }
            }
            int firstPage = Info.FirstPage;
            string startPref = Settings.Get("startpage", "ask");
            int fixedPage;
            if (startPref != "ask" && int.TryParse(startPref, out fixedPage) && fixedPage >= 0 && fixedPage <= 3) firstPage = fixedPage;
            else
            {
                using (ChooserForm c = new ChooserForm())
                {
                    if (c.ShowDialog() != DialogResult.OK) return 0;
                    firstPage = c.Choice;
                    if (c.SkipNextTime) Settings.Set("startpage", firstPage.ToString());
                    Settings.Set("startadmin", c.WantAdmin ? "1" : "0");
                    if (c.WantAdmin && !DnsTool.IsAdmin() && RestartElevated("--page " + firstPage)) return 0;
                }
            }
            Application.Run(new MainForm(firstPage));
            return 0;
        }
    }
}
