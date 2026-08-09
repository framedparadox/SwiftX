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

        Loc.Initialize();
        var config = RingStore.Load();
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
