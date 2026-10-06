# Static risk scan

Scanned 130 files under `src`: **63** hits, **36** unguarded. Triage tool, not a verdict - read each unguarded hit in context.

| Rule | Severity | Title | Hits | Unguarded |
|---|---|---|---:|---:|
| CR05 | High | Managed COM interop / raw interface pointers | 6 | 6 |
| CR10 | High | Subscription to a long-lived event with no matching unsubscribe | 2 | 0 |
| PF02 | High | HttpClient created per use | 1 | 0 |
| CR01 | Medium | async void | 21 | 3 |
| CR03 | Medium | Fire-and-forget Task | 1 | 1 |
| CR04 | Medium | Thread without IsBackground | 1 | 1 |
| CR08 | Medium | Unbounded whole-file read | 5 | 1 |
| CR09 | Medium | XML parsing without a DTD policy | 2 | 0 |
| PF01 | Medium | Thread.Sleep | 1 | 1 |
| PF03 | Medium | Timer firing faster than 100 ms | 1 | 1 |
| PF07 | Medium | Synchronous existence check that can touch the network | 20 | 20 |
| PF04 | Low | BitmapImage without a decode size | 1 | 1 |
| PF08 | Low | Synchronous file I/O in a UI-layer file | 1 | 1 |

## CR05 Managed COM interop / raw interface pointers (High)

_Managed [ComImport] interfaces marshalled through WinRT.Runtime have produced 0xc000027b stowed-exception fail-fasts here before (IImageList). Fail-fasts skip every managed handler._

- `src/SwiftX/Interop/NativeMethods.cs:559` `[ComImport]`
- `src/SwiftX/Interop/NativeMethods.cs:574` `[ComImport]`
- `src/SwiftX/Interop/NativeMethods.cs:606` `[ComImport]`
- `src/SwiftX/Services/ShortcutResolver.cs:96` `[ComImport]`
- `src/SwiftX/Services/ShortcutResolver.cs:105` `[ComImport]`
- `src/SwiftX/Services/ShortcutResolver.cs:117` `[ComImport]`

## CR01 async void (Medium)

_An exception escaping an async void method cannot be caught by the caller; it is raised on the synchronization context and takes the process down (or lands in UnhandledException)._

- `src/SwiftX/ShellWindow.xaml.cs:6077` `private async void ShareFeedback_Click(object sender, RoutedEventArgs e) =>`
- `src/SwiftX/ShellWindow.xaml.cs:6080` `private async void RateOnStore_Click(object sender, RoutedEventArgs e) =>`
- `src/SwiftX/ShellWindow.xaml.cs:6194` `private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)`

## CR03 Fire-and-forget Task (Medium)

_A task nobody awaits swallows its exception until the finalizer raises UnobservedTaskException; work may be silently lost._

- `src/SwiftX/Services/IconService.cs:119` `Task.Run(() => Probe(item, fileExists ?? File.Exists, directoryExists ?? Directory.Exists));`

## CR04 Thread without IsBackground (Medium)

_A foreground thread keeps the process alive after the last window closes - the app appears to quit but stays in Task Manager._

- `src/SwiftX/Services/Launcher.cs:28` `var thread = new Thread(() =>`

## CR08 Unbounded whole-file read (Medium)

_A mis-picked multi-GB file becomes an OutOfMemoryException from a UI command._

- `src/SwiftX/Services/SecureValueStore.cs:75` `var json = File.ReadAllText(FilePath);`

## PF01 Thread.Sleep (Medium)

_On the UI thread this is a freeze of exactly that length._

- `src/SwiftX/Services/AtomicWrite.cs:129` `Thread.Sleep(delay);`

## PF03 Timer firing faster than 100 ms (Medium)

_A tray app is idle 99% of its life; a sub-100 ms timer keeps the CPU out of deep idle states and costs battery all day._

- `src/SwiftX/OrbitWindow.xaml.cs:136` `Interval = TimeSpan.FromMilliseconds(40),`

## PF07 Synchronous existence check that can touch the network (Medium)

_On a UNC path or a disconnected mapped drive this blocks for the SMB timeout (seconds to tens of seconds). Called on the UI thread it freezes the app._

- `src/SwiftX/Models/ElementSections.cs:112` `return !File.Exists(path) && !Directory.Exists(path);`
- `src/SwiftX/Services/AtomicWrite.cs:139` `if (File.Exists(path))`
- `src/SwiftX/Services/DesktopShortcutService.cs:66` `return File.Exists(ShortcutPath);`
- `src/SwiftX/Services/DesktopShortcutService.cs:87` `if (File.Exists(ShortcutPath) && !IsOurs(ShortcutPath))`
- `src/SwiftX/Services/DesktopShortcutService.cs:113` `if (!File.Exists(ShortcutPath))`
- `src/SwiftX/Services/DesktopShortcutService.cs:128` `return File.Exists(ShortcutPath);`
- `src/SwiftX/Services/DesktopShortcutService.cs:246` `if (!File.Exists(source))`
- `src/SwiftX/Services/DesktopShortcutService.cs:251` `if (!File.Exists(destination) ||`
- `src/SwiftX/Services/IconService.cs:487` `if (File.Exists(cacheFile))`
- `src/SwiftX/Services/Launcher.cs:60` `if (item.Kind == OrbitItemKind.File && File.Exists(item.Target) &&`
- `src/SwiftX/Services/Launcher.cs:177` `return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;`
- `src/SwiftX/Services/OnboardingCatalog.cs:61` `if (!File.Exists(path)) return;`
- `src/SwiftX/Services/OnboardingCatalog.cs:76` `if (!Directory.Exists(path)) return;`
- `src/SwiftX/Services/OrbitItemFactory.cs:12` `if (Directory.Exists(target))`
- `src/SwiftX/Services/OrbitItemFactory.cs:123` `if (File.Exists(trimmed) || Directory.Exists(trimmed))`
- `src/SwiftX/Services/OrbitStore.cs:57` `public static bool Exists => File.Exists(FilePath) || File.Exists(LegacyFilePath);`
- `src/SwiftX/Services/OrbitStore.cs:65` `var path = File.Exists(FilePath) ? FilePath`
- `src/SwiftX/Services/OrbitStore.cs:66` `: File.Exists(LegacyFilePath) ? LegacyFilePath`
- `src/SwiftX/Services/SecureValueStore.cs:72` `if (!File.Exists(FilePath))`
- `src/SwiftX/Services/ShortcutResolver.cs:36` `!File.Exists(path))`

## PF04 BitmapImage without a decode size (Low)

_Without DecodePixelWidth/Height the full image is decoded at native size however small it is drawn._

- `src/SwiftX/Services/IconService.cs:627` `bmp = new BitmapImage();`

## PF08 Synchronous file I/O in a UI-layer file (Low)

_Disk and antivirus latency lands on the UI thread when this runs from an event handler._

- `src/SwiftX/ToolPages/FormatterPage.cs:568` `File.WriteAllText(file.Path, _editor.Text);`

