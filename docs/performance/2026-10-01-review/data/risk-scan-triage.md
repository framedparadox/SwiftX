# Static scan: every unguarded hit, read in context

`scan-risk-patterns.ps1` over `src/`: 130 files, 63 hits, **36 unguarded** (the other 27 match the rule
but carry the mitigation it looks for). A hit is a place to read, not a verdict; each of the 36 was read.
Result: **5 false positives, 30 accepted with a reason, 1 hazard recorded (PR-018), 0 new defects.**
The defects the scan pointed at in the first place (the network existence checks, the unremoved handlers)
are PR-004 and PR-002 and are already fixed - which is why they are guarded or absent here.

| Rule | Unguarded | Verdict | Reason |
|---|---:|---|---|
| CR01 `async void` | 3 | False positive | `ShareFeedback_Click`, `RateOnStore_Click` and `StartupToggle_Toggled` are one-line forwards; the `try/catch` is in the awaited method (`StoreFeedback.OpenFeedbackHubAsync`, `RequestRatingAsync` via `TryInAppRatingAsync` / `LaunchStoreReviewAsync`, `StartupService.SetEnabledAsync`), which the rule does not follow. |
| CR03 fire-and-forget `Task` | 1 | False positive | `IconService.ProbeAsync` returns the `Task.Run` task; its callers await it. |
| CR04 `Thread` without `IsBackground` | 1 | False positive | `Launcher` sets `IsBackground = true` in the object initialiser (the rule looks for a separate statement). |
| CR05 `[ComImport]` | 6 | Accepted | Classic shell-link COM (`IShellLinkW`, `IPersistFile`, `ShellLink`) used from the UI's STA to write the desktop shortcut, with the vtable declared in full and every call inside a `try`. `ShortcutResolver`'s read-only copy is not referenced by product code. Not the WinRT projection path the earlier native crashes came through. |
| CR08 unbounded `ReadAllText` | 1 | Accepted | `SecureValueStore` reads its own file in the user's private data directory; a malformed file is covered by `SecureValues_WithAMalformedFile_LoadEmptyRatherThanThrowing`. |
| PF01 `Thread.Sleep` | 1 | Accepted | `AtomicWrite` retries a locked `File.Move` with a bounded doubling delay (`MoveAttempts`); the alternative is a lost save. |
| PF03 timer < 100 ms | 1 | Accepted | The orbit's outside-click poll (a 40 ms `DispatcherTimer`) is started on summon and stopped on dismiss (`OrbitWindow` lines 284, 346, 400), so it runs only while the orbit is on screen; the tray-idle cost with it stopped is 0.37% of a core, measured. |
| PF04 `BitmapImage` without decode size | 1 | **Hazard, PR-018** | `IconService.DecodeAsync` bounds the bytes (2 MB) but not the pixels. |
| PF07 synchronous `File.Exists` / `Directory.Exists` | 20 | Accepted (see below) | |
| PF08 synchronous file I/O in a UI-layer file | 1 | Accepted | `FormatterPage` "Save" writes the path the user picked in the save dialog, on the UI thread: an explicit action on a path the person chose. |

### The 20 existence checks

| Where | Count | Why it is acceptable |
|---|---:|---|
| `DesktopShortcutService` (the desktop shortcut) | 6 | The user's own Desktop folder. |
| `OrbitStore`, `SecureValueStore`, `AtomicWrite` (the data directory) | 5 | `%AppData%` / `%LocalAppData%`, or the redirected test directory. |
| `IconService` favicon cache file | 1 | Under `%LocalAppData%`. |
| `OnboardingCatalog` (Start Menu / known folders) | 2 | Shell-known local folders. |
| `ElementSections.IsMissing` | 1 | Returns before touching the disk unless the drive is `DriveType.Fixed`; the caller runs it on the pool. |
| `Launcher` (target, working directory) | 2 | Network targets now run on a dedicated thread (`ShouldLaunchOffTheCallerThread`) - PR-004. |
| `OrbitItemFactory` (a path typed or dropped in the Add dialog) | 2 | A person typing or dropping a path is waiting on the answer; a dropped network folder is reachable by construction. Residual of PR-004, accepted. |
| `ShortcutResolver` | 1 | Not referenced by product code. |
