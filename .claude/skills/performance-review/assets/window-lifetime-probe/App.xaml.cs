using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LeakProbe;

/// <summary>
/// Opens and closes <c>cycles</c> windows of one variant, then forces collections and counts how many
/// are still alive. Usage: <c>LeakProbe.exe &lt;variant&gt; &lt;cycles&gt; &lt;result.json&gt;</c>.
/// A window that is still alive after a full GC is being rooted by something; the variant that
/// makes it collectable names the cause. Variants live in <see cref="ConfigurableWindow"/>.
/// </summary>
public partial class App : Application
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr hProcess, uint flags);

    private Window? _driver;

    public App() { InitializeComponent(); }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        string variant = argv.Length > 1 ? argv[1] : "none";
        int cycles = argv.Length > 2 ? int.Parse(argv[2]) : 30;
        string outPath = argv.Length > 3 ? argv[3] : Path.Combine(Path.GetTempPath(), "leakprobe.json");

        // Never activated: it only keeps the app alive while windows come and go.
        _driver = new Window();

        var queue = DispatcherQueue.GetForCurrentThread();
        var refs = new List<WeakReference>();
        var samples = new List<int[]>();
        var privMb = new List<double>();
        Window? current = null;
        int opened = 0;
        bool finishing = false;

        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            if (finishing) return;

            if (current is not null)
            {
                current.Close();
                current = null;
                return;
            }

            if (opened >= cycles)
            {
                finishing = true;
                timer.Stop();
                Finish(queue, variant, cycles, outPath, refs, samples, privMb);
                return;
            }

            current = new ConfigurableWindow(variant);
            refs.Add(new WeakReference(current));
            current.Activate();
            opened++;

            var p = Process.GetCurrentProcess();
            p.Refresh();
            samples.Add([(int)GetGuiResources(p.Handle, 0), p.HandleCount]);
            privMb.Add(Math.Round(p.PrivateMemorySize64 / 1048576.0, 1));
        };
        timer.Start();
    }

    private void Finish(DispatcherQueue queue, string variant, int cycles, string outPath,
        List<WeakReference> refs, List<int[]> samples, List<double> privMb)
    {
        // Give the last close time to complete, then force collections: a window still alive after
        // that is being rooted by something.
        var settle = queue.CreateTimer();
        settle.Interval = TimeSpan.FromSeconds(2);
        settle.IsRepeating = false;
        settle.Tick += (_, _) =>
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            var p = Process.GetCurrentProcess();
            p.Refresh();
            var result = new
            {
                variant,
                cycles,
                aliveAfterFullGc = refs.Count(r => r.IsAlive),
                gdiFirst = samples[0][0],
                gdiLast = samples[^1][0],
                handlesFirst = samples[0][1],
                handlesLast = samples[^1][1],
                privFirstMb = privMb[0],
                privLastMb = privMb[^1],
                gdiAfterGc = (int)GetGuiResources(p.Handle, 0),
                handlesAfterGc = p.HandleCount,
                privAfterGcMb = Math.Round(p.PrivateMemorySize64 / 1048576.0, 1),
            };
            File.WriteAllText(outPath, JsonSerializer.Serialize(result));
            Environment.Exit(0);
        };
        settle.Start();
    }
}
