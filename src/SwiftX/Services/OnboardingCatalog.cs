using SwiftX.Models;

namespace SwiftX.Services;

/// <summary>
/// One thing the first-run guide offers to put on the orbit.
/// </summary>
/// <param name="Key">Stable identity for this suggestion, so the guide can remember which tiles
/// are ticked across a step change and tell the tools tile apart from the rest. Not persisted —
/// the orbit stores <see cref="Item"/>, not the suggestion it came from.</param>
/// <param name="Subtitle">One line under the name saying what it is, so a tile is legible without
/// the user already knowing the target.</param>
/// <param name="Item">The orbit item this tile adds, built ready to hand over. Its
/// <see cref="OrbitItem.DisplayName"/> is the tile's own title and its
/// <see cref="OrbitItem.Glyph"/> the tile's icon, so a tile can never advertise something
/// different from what it adds.</param>
/// <param name="DefaultOn">Whether the tile starts ticked.</param>
public sealed record OnboardingSuggestion(
    string Key,
    string Subtitle,
    OrbitItem Item,
    bool DefaultOn = false);

/// <summary>
/// What the first-run guide offers: stock Windows apps, the user's own folders, a couple of links,
/// and the built-in tools group.
/// <para>
/// Every entry is existence-checked before it is offered (see <see cref="Available"/>), because the
/// alternative is a first-run screen that proposes an app the machine does not have and produces a
/// dead orbit item the moment it is ticked. That check is also why this is a method rather than a
/// static list: the answers depend on the machine, and a static initialiser would run them once, at
/// type load, on whatever thread got there first. Each call also builds fresh
/// <see cref="OrbitItem"/>s, so a guide opened twice never hands the same mutable instance to two
/// orbits.
/// </para>
/// <para>
/// The six entries marked <see cref="OnboardingSuggestion.DefaultOn"/> are exactly what
/// <see cref="OrbitManager.SeedDefaults"/> plants, in the same order, so a user who opens the guide
/// and presses Finish without touching anything gets precisely the orbit SwiftX shipped with before
/// the guide existed. Everything else here is an addition the user has to ask for.
/// </para>
/// </summary>
public static class OnboardingCatalog
{
    /// <summary>
    /// Identity of the tools tile, so the guide can treat it specially — it is a group of its own
    /// rather than a single target, and <see cref="ToolCatalog.EnsureToolsGroup"/> has opinions
    /// about where it lives.
    /// </summary>
    public const string ToolsKey = "tools";

    /// <summary>
    /// The suggestions that make sense on this machine, in the order the guide shows them.
    /// </summary>
    public static IReadOnlyList<OnboardingSuggestion> Available()
    {
        var list = new List<OnboardingSuggestion>();

        void App(string key, string name, string subtitle, string path, bool on = false)
        {
            if (!File.Exists(path)) return;
            list.Add(new OnboardingSuggestion(key, subtitle, new OrbitItem
            {
                Kind = OrbitItemKind.Application,
                DisplayName = name,
                Target = path,
            }, on));
        }

        void Folder(string key, string name, string subtitle, Environment.SpecialFolder folder,
                    string? child = null, bool on = false)
        {
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root)) return;
            var path = child is null ? root : Path.Combine(root, child);
            if (!Directory.Exists(path)) return;
            list.Add(new OnboardingSuggestion(key, subtitle, new OrbitItem
            {
                Kind = OrbitItemKind.Folder,
                DisplayName = name,
                Target = path,
            }, on));
        }

        void Link(string key, string name, string subtitle, string url, string? glyph = null,
                  bool on = false)
        {
            list.Add(new OnboardingSuggestion(key, subtitle, new OrbitItem
            {
                Kind = OrbitItemKind.WebLink,
                DisplayName = name,
                Target = url,
                CustomGlyph = glyph,
            }, on));
        }

        string system = Environment.SystemDirectory;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Apps. Notepad, Calculator and File Explorer are three of the six SeedDefaults plants.
        App("notepad", "Notepad", "Plain-text editor",
            Path.Combine(system, "notepad.exe"), on: true);
        App("calc", "Calculator", "Windows Calculator",
            Path.Combine(system, "calc.exe"), on: true);
        App("explorer", "File Explorer", "Browse this PC",
            Path.Combine(windows, "explorer.exe"), on: true);
        App("terminal", "Terminal", "Windows Terminal",
            Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe"));
        App("powershell", "PowerShell", "Windows PowerShell",
            Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"));
        App("cmd", "Command Prompt", "cmd.exe",
            Path.Combine(system, "cmd.exe"));
        App("vscode", "VS Code", "Visual Studio Code",
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"));
        App("paint", "Paint", "Microsoft Paint",
            Path.Combine(system, "mspaint.exe"));
        App("taskmgr", "Task Manager", "What is running right now",
            Path.Combine(system, "Taskmgr.exe"));

        // Folders. Downloads is a SeedDefaults plant; the rest are offers.
        Folder("downloads", "Downloads", "Your Downloads folder",
            Environment.SpecialFolder.UserProfile, "Downloads", on: true);
        Folder("documents", "Documents", "Your Documents folder",
            Environment.SpecialFolder.MyDocuments);
        Folder("desktop", "Desktop", "Your Desktop folder",
            Environment.SpecialFolder.DesktopDirectory);
        Folder("pictures", "Pictures", "Your Pictures folder",
            Environment.SpecialFolder.MyPictures);

        // Links. Nothing to existence-check — whether a site answers is not something to decide on
        // the user's behalf at first run. Neither carries a glyph, which is also how SeedDefaults
        // plants GitHub, so the default orbit is identical either way: GitHub shows the logo
        // SwiftX ships (see BrandIcons) and Google its favicon once IconService has fetched it.
        Link("github", "GitHub", "github.com", "https://github.com", on: true);
        Link("google", "Google", "google.com", "https://www.google.com");

        // The built-in tools, as the one group that owns all of them — see
        // ToolCatalog.EnsureToolsGroup, which keeps an existing orbit in that shape too.
        list.Add(new OnboardingSuggestion(
            ToolsKey,
            ToolSubtitle(),
            ToolCatalog.CreateToolsGroup(),
            DefaultOn: true));

        return list;
    }

    /// <summary>
    /// The tools tile's subtitle, listed from the catalog rather than written out, so a tool added
    /// to <see cref="ToolCatalog.All"/> shows up here without anyone remembering to come back.
    /// </summary>
    private static string ToolSubtitle() =>
        string.Join(", ", ToolCatalog.All.Select(t => t.DisplayName));
}
