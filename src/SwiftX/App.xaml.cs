using System.Threading;
using SwiftX.Services;
using Microsoft.UI.Xaml;

namespace SwiftX;

public partial class App : Application
{
    public static OrbitManager? Manager { get; private set; }

    private static Mutex? _instanceMutex;

    [ThreadStatic]
    private static bool _tracingFirstChance;

    public App()
    {
        // Exceptions that never reach the XAML handler below used to leave nothing in the log at all,
        // which is how a crash on a background thread went unexplained.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Diag.Log("FATAL (AppDomain): " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Diag.Log("UNOBSERVED TASK EXCEPTION: " + e.Exception);

        // A stowed WinRT error (0xc000027b) is worse: it is a fail-fast, so not even the two handlers
        // above see it. What can be logged is the exception thrown and caught just before it, with the
        // live call stack (an exception's own trace ends at the throw, before it has unwound through
        // any caller). Off unless asked for: ArgumentException and COMException are thrown and
        // caught routinely (the window frame code catches one at every start-up), so on by default
        // this would bury the log. Set SWIFTX_TRACE_EXCEPTIONS=1 to turn it on.
        if (Environment.GetEnvironmentVariable("SWIFTX_TRACE_EXCEPTIONS") == "1")
        {
            AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
            {
                if (_tracingFirstChance || e.Exception is not (ArgumentException or System.Runtime.InteropServices.COMException))
                    return;
                // Logging can throw and catch too, which would re-enter this handler.
                _tracingFirstChance = true;
                try { Diag.Log("FIRST-CHANCE " + e.Exception.GetType().Name + ": " + e.Exception + "\n  caller stack:\n" + Environment.StackTrace); }
                finally { _tracingFirstChance = false; }
            };
        }

        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Diag.Log($"UNHANDLED: {e.Message}\n{e.Exception}");
            // Leaving Handled false here does not just skip a log line — WinUI terminates the
            // process once this handler returns unless it is set. That is right for a genuine
            // application bug, but wrong for the composition/backdrop-layer exceptions this app
            // has already hit (see TransparentBackdrop's own doc comment): framework-internal
            // noise unrelated to any corrupted app state, where crashing the whole orbit over a
            // theme-change repaint glitch is a worse outcome than logging it and continuing.
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Windows starting SwiftX at sign-in is not the same event as a person clicking its icon,
        // and the two want opposite things: a sign-in launch should slip into the tray unnoticed,
        // while a launch the user performed should visibly do something. Everything below turns on
        // that one distinction — see StartupService.StartupArgument for how it is told.
        bool autostart = StartupService.IsAutostartLaunch;

        if (!IsFirstInstance())
        {
            // The old behavior was to log this and exit, which from the user's side was
            // indistinguishable from the icon being broken: SwiftX has no taskbar button, so a
            // second launch produced no window, no flash, nothing at all. Now the already-running
            // instance is asked to show Settings and this process steps aside.
            if (autostart)
            {
                // Belt and braces: Windows should not be starting a second copy at sign-in, and if
                // it somehow does, throwing Settings in the user's face is the last thing to do.
                Diag.Log("Autostart launch, but SwiftX is already running — exiting quietly.");
            }
            else
            {
                Diag.Log("Another SwiftX instance is already running — handing it the launch.");
                SingleInstanceSignal.RequestOpenSettings();
            }
            Exit();
            return;
        }

        var config = OrbitStore.Load();
        Loc.Initialize(config.Language);
        Manager = new OrbitManager(config);
        Manager.Start(openSettings: !autostart);
    }

    /// <summary>
    /// Claims the single-instance mutex, whose name is shared with
    /// <see cref="SingleInstanceSignal"/> so that the instance a launch collides with is by
    /// definition the instance the launch gets handed to.
    /// </summary>
    private static bool IsFirstInstance()
    {
        _instanceMutex = new Mutex(
            initiallyOwned: false, SingleInstanceSignal.MutexName, out bool createdNew);
        return createdNew;
    }
}
