// Compiled at run time by measure-app.ps1 (Add-Type). Everything here is observation or a window
// message posted straight to the target process's own windows - it never injects keyboard or mouse
// input into the shared desktop, so it cannot type into, click on, or steal input from whatever the
// user is working in.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class Win32Probe
{
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr hProcess, uint flags);

    public sealed class WindowInfo
    {
        public long Handle;
        public string Class;
        public string Title;
        public bool Visible;
    }

    public static List<WindowInfo> Windows(int pid)
    {
        var list = new List<WindowInfo>();
        EnumWindows((h, _) =>
        {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner == (uint)pid)
            {
                var cls = new StringBuilder(256);
                var title = new StringBuilder(256);
                GetClassName(h, cls, 256);
                GetWindowText(h, title, 256);
                list.Add(new WindowInfo { Handle = h.ToInt64(), Class = cls.ToString(), Title = title.ToString(), Visible = IsWindowVisible(h) });
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>The hidden message window SwiftX registers as "SwiftX.MessageWindow.&lt;pid&gt;"; 0 if absent.</summary>
    public static long FindMessageWindow(int pid)
    {
        foreach (var w in Windows(pid))
            if (w.Class.StartsWith("SwiftX.MessageWindow.", StringComparison.Ordinal))
                return w.Handle;
        return 0;
    }

    public static int CountVisible(int pid)
    {
        int n = 0;
        foreach (var w in Windows(pid))
            if (w.Visible && !w.Class.StartsWith("SwiftX.MessageWindow.", StringComparison.Ordinal))
                n++;
        return n;
    }

    public static long FindVisibleByTitle(int pid, string titleContains)
    {
        foreach (var w in Windows(pid))
            if (w.Visible && w.Title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                return w.Handle;
        return 0;
    }

    /// <summary>Waits until more (or no more) than <paramref name="baseline"/> windows are visible.
    /// Returns elapsed milliseconds, or -1 on timeout.</summary>
    public static double WaitVisibleCount(int pid, int baseline, bool wantMore, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            int n = CountVisible(pid);
            if (wantMore ? n > baseline : n <= baseline)
                return sw.Elapsed.TotalMilliseconds;
            Thread.Sleep(1);
        }
        return -1;
    }

    public static double WaitTitle(int pid, string titleContains, bool visible, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            bool found = FindVisibleByTitle(pid, titleContains) != 0;
            if (found == visible)
                return sw.Elapsed.TotalMilliseconds;
            Thread.Sleep(1);
        }
        return -1;
    }

    public static double WaitMessageWindow(int pid, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (FindMessageWindow(pid) != 0)
                return sw.Elapsed.TotalMilliseconds;
            Thread.Sleep(1);
        }
        return -1;
    }

    public static bool Post(long hwnd, uint msg, long wParam, long lParam) =>
        PostMessage(new IntPtr(hwnd), msg, new IntPtr(wParam), new IntPtr(lParam));

    public static uint Register(string name) => RegisterWindowMessage(name);

    public static int Gdi(IntPtr process) => (int)GetGuiResources(process, 0);

    public static int UserObjects(IntPtr process) => (int)GetGuiResources(process, 1);

    /// <summary>
    /// How long the window's own thread takes to answer a WM_NULL: the direct measure of whether
    /// the UI thread is free. -1 means it did not answer within <paramref name="timeoutMs"/>.
    /// Deliberately not SMTO_ABORTIFHUNG, which would return early on a hung window and hide how
    /// long the stall really was.
    /// </summary>
    public static double Ping(long hwnd, uint timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        IntPtr result;
        IntPtr ok = SendMessageTimeout(new IntPtr(hwnd), 0, IntPtr.Zero, IntPtr.Zero, 0, timeoutMs, out result);
        sw.Stop();
        return ok == IntPtr.Zero ? -1 : sw.Elapsed.TotalMilliseconds;
    }
}

/// <summary>Pings a window's UI thread on a background thread and keeps the latency distribution.</summary>
public sealed class UiPinger
{
    private Thread _thread;
    private volatile bool _stop;
    private readonly object _gate = new object();
    private readonly List<double> _latencies = new List<double>();
    private readonly List<double[]> _stalls = new List<double[]>(); // [seconds since start, duration ms]
    private int _timeouts;
    private Stopwatch _clock;
    private double _stallThresholdMs;

    // Stopwatch timestamp at which the ping that is being waited on right now was sent; 0 when none.
    // A ping is only recorded when it RETURNS, so a stall still in progress would be invisible to
    // anyone reading the statistics mid-stall - exactly the freeze that matters most. Tracking the
    // in-flight ping lets the statistics say "stalled for at least this long, and still going".
    private long _inFlightSince;

    public void Start(long hwnd, int intervalMs, uint timeoutMs, double stallThresholdMs)
    {
        _stop = false;
        _stallThresholdMs = stallThresholdMs;
        _clock = Stopwatch.StartNew();
        _thread = new Thread(() =>
        {
            while (!_stop)
            {
                double at = _clock.Elapsed.TotalSeconds;
                lock (_gate) { _inFlightSince = Stopwatch.GetTimestamp(); }
                double ms = Win32Probe.Ping(hwnd, timeoutMs);
                lock (_gate)
                {
                    _inFlightSince = 0;
                    if (ms < 0) { _timeouts++; ms = timeoutMs; }
                    _latencies.Add(ms);
                    if (ms >= stallThresholdMs) _stalls.Add(new[] { at, ms });
                }
                Thread.Sleep(intervalMs);
            }
        }) { IsBackground = true, Name = "UiPinger" };
        _thread.Start();
    }

    /// <summary>How long the ping currently being waited on has been outstanding, in milliseconds;
    /// 0 when the UI thread answered the last one.</summary>
    public double OngoingMs()
    {
        lock (_gate)
        {
            if (_inFlightSince == 0) return 0;
            return (Stopwatch.GetTimestamp() - _inFlightSince) * 1000.0 / Stopwatch.Frequency;
        }
    }

    /// <summary>Blocks until the UI thread is answering again (no ping outstanding for 100 ms) or the
    /// timeout passes. Returns true when it is responsive.</summary>
    public bool WaitUntilResponsive(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (OngoingMs() < 100) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    public void Stop()
    {
        _stop = true;
        if (_thread != null) _thread.Join(15000);
    }

    public void Reset()
    {
        lock (_gate) { _latencies.Clear(); _stalls.Clear(); _timeouts = 0; }
        _clock.Restart();
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int idx = (int)Math.Min(sorted.Count - 1, Math.Ceiling(p * sorted.Count) - 1);
        return sorted[Math.Max(0, idx)];
    }

    /// <summary>count, p50, p95, p99, max, timeouts, stallCount, longestStallMs. A ping still
    /// outstanding past the threshold counts as a stall (and as the longest/max) even though it has
    /// not returned yet.</summary>
    public double[] Stats()
    {
        lock (_gate)
        {
            var s = new List<double>(_latencies);
            s.Sort();
            double longest = 0;
            foreach (var st in _stalls) longest = Math.Max(longest, st[1]);

            double ongoing = _inFlightSince == 0 ? 0 : (Stopwatch.GetTimestamp() - _inFlightSince) * 1000.0 / Stopwatch.Frequency;
            int stalls = _stalls.Count;
            double max = s.Count == 0 ? 0 : s[s.Count - 1];
            if (ongoing >= _stallThresholdMs)
            {
                stalls++;
                longest = Math.Max(longest, ongoing);
                max = Math.Max(max, ongoing);
            }

            return new double[]
            {
                s.Count, Percentile(s, 0.50), Percentile(s, 0.95), Percentile(s, 0.99),
                max, _timeouts, stalls, longest,
            };
        }
    }

    public double[][] Stalls()
    {
        lock (_gate)
        {
            var all = new List<double[]>(_stalls);
            double ongoing = _inFlightSince == 0 ? 0 : (Stopwatch.GetTimestamp() - _inFlightSince) * 1000.0 / Stopwatch.Frequency;
            if (ongoing >= _stallThresholdMs)
                all.Add(new[] { _clock.Elapsed.TotalSeconds - ongoing / 1000.0, ongoing });
            return all.ToArray();
        }
    }
}
