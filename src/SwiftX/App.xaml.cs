using System.Threading;
using SwiftX.Services;
using Microsoft.UI.Xaml;

namespace SwiftX;

public partial class App : Application
{
    public static RingManager? Manager { get; private set; }

    private static Mutex? _instanceMutex;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Diag.Log($"UNHANDLED: {e.Message}\n{e.Exception}");
            // Leaving Handled false here does not just skip a log line — WinUI terminates the
            // process once this handler returns unless it is set. That is right for a genuine
            // application bug, but wrong for the composition/backdrop-layer exceptions this app
            // has already hit (see TransparentBackdrop's own doc comment): framework-internal
            // noise unrelated to any corrupted app state, where crashing the whole ring over a
            // theme-change repaint glitch is a worse outcome than logging it and continuing.
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!IsFirstInstance())
        {
            Diag.Log("Another SwiftX instance is already running — exiting this one.");
            Exit();
            return;
        }

        var config = RingStore.Load();
        Loc.Initialize(config.Language);
        Manager = new RingManager(config);
        Manager.Start();
    }

    private static bool IsFirstInstance()
    {
        string name = @"Local\SwiftX.SingleInstance.v1." + DataDirectoryKey();
        _instanceMutex = new Mutex(initiallyOwned: false, name, out bool createdNew);
        return createdNew;
    }

    private static string DataDirectoryKey()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            RingStore.DataDirectory.ToLowerInvariant());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes), 0, 8);
    }
}
