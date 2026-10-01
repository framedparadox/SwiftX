# Crash and hang hunting for a .NET / WinUI 3 desktop app

## 1. Read the signature first

The Application event log (id 1000) names the **faulting module**, the **exception code** and the
**fault offset**. Same triple = same bug. Bucket by it before reading anything else; the count and
the first/last-seen dates tell you how often it happens and whether it still does.
`scripts/crash-forensics.ps1` does exactly this.

| Signature | What it is | Managed `catch` helps? | How to confirm |
|---|---|---|---|
| `0xc000027b` (WER `8000ffff`), module `Microsoft.UI.Xaml.dll` / `CoreMessagingXP.dll` | **Stowed exception**: a WinRT/XAML call failed and the runtime fail-fasts. Skips *every* managed handler, writes nothing to the app log. | No | WER dump; managed frames via identifier scan |
| `0xc0000005` in a **system** DLL | Access violation inside a native API called wrongly (wrong thread, no package identity, bad pointer) | No (not catchable in .NET) | dump identifiers show the managed caller |
| `0xc0000005` in `coreclr.dll` | Runtime-level corruption: a bad `unsafe`/P/Invoke signature, a use-after-free of a handle | No | review every `DllImport` and `unsafe` near the frames |
| `0xe0434352` | CLR unhandled managed exception | Log id 1026 carries the managed stack | read event 1026 |
| `0xc00000fd` | **Stack overflow** - uncatchable; usually recursion over user-controlled depth | No | look for recursive parsers/renderers without a depth cap |
| `0xc0000017` / OOM | Unbounded allocation from an unbounded input | Sometimes (`OutOfMemoryException`) | size limits missing on a read |
| Event 1002 "stopped interacting" | **Hang**: UI thread blocked > 5 s | n/a | sync I/O or a lock on the UI thread; see section 4 |

## 2. Dumps without a debugger

Windows already keeps a dump per crash in `%LOCALAPPDATA%\CrashDumps\<exe>.<pid>.dmp`. No debugger
is needed: a string scan of the file recovers the managed frames that were live, because their
names are still in memory. `crash-forensics.ps1` scans for identifier-shaped strings only
(`SwiftX.Namespace.Type.Method()`, `InterfaceMarshaler`, `WinRT.*`, `Windows.*.*`).

**Privacy: a dump is a copy of the app's memory.** SwiftX has a Secure Values tool, so a dump can
hold decrypted secrets. Extract identifiers only; never print, paste, or store other strings, and
never copy a dump off the machine.

To get richer dumps for a crash that is still reproducible (needs admin, once):
`HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\SwiftX.exe` with
`DumpType = 2` (full) and `DumpFolder`. Then `dotnet-dump analyze` or WinDbg `!clrstack`.

## 3. A crash you cannot reproduce is not a crash that is fixed

The icon crash (`0xc000027b`, 9 dumps in a week) reproduced only under the user's debugger; ~15
scripted launches never hit it. Treat the **dump evidence** as ground truth: identify the frame,
explain the mechanism, fix it, add a test that pins the mechanism, and ask the user to confirm on
their own setup. Never write "could not reproduce, so it is fine".

`SWIFTX_TRACE_EXCEPTIONS=1` logs first-chance `ArgumentException` / `COMException` with the live
call stack. It is off by default because start-up throws and catches one on purpose.

A scripted launch with `--startup` never builds `ShellWindow` (nearly all the UI). Launch with no
arguments to exercise it.

## 4. Hangs: where the UI thread stops answering

The UI thread must never wait on something it does not control. Hazards, roughly by how often they
bite on a laptop:

1. **Existence checks on network paths.** `File.Exists` / `Directory.Exists` on a UNC path to an
   unreachable host blocks for the SMB connect timeout - **measured here: 21.04 s** for
   `\\192.0.2.1\share\folder`, then fast for a while (negative cache). The orbit item pointing at
   `\\nas\...` while the VPN is down is the realistic trigger. Any such call before the first real
   `await` of an async method still runs on the UI thread.
2. `Thread.Sleep`, `.Result`, `.Wait()`, `lock` held across I/O.
3. Synchronous whole-file reads/writes of user-sized files from an event handler.
4. `Process.Start` with `UseShellExecute` on a slow association handler.
5. COM/WinRT calls into out-of-process servers (Store, shell extensions) that can stall.

Measure it, do not argue it: `measure-app.ps1` pings the UI thread with `WM_NULL` every 25 ms and
reports every answer slower than the budget. The `unreachable` phase builds an orbit with
unroutable RFC 5737 addresses and reports the stall.

## 5. Native APIs that need a capability the build may not have

A call that is fine in one packaging mode can fault natively in another. `StoreContext`
(`Windows.Services.Store`) needs package identity; calling it from the unpackaged portable build is
an access violation in `Windows.ApplicationModel.Store.dll` that no `try/catch` can stop.
Pattern: **ask the capability question first** (`PackagedRuntime.IsPackaged`), and make the
unsupported branch take a safe alternative (a URI launch) rather than an exception. Grep for every
WinRT namespace that documents an identity requirement: `Windows.Services.Store`,
`Windows.ApplicationModel.Store`, `Windows.ApplicationModel.Background`, `Windows.Storage.ApplicationData`,
`Windows.ApplicationModel.Package`, notification APIs.

## 6. Prevention patterns that are proven here

- `async void` handlers wrap their whole body in `try/catch` and log (an exception escaping one is
  raised on the sync context and ends the process).
- Everything fed by the user is bounded before it is read: `InputLimits.ReadText`, depth caps
  (`XmlFormat.MaxDepth`), `CodeEditor.MaxHighlightLength`.
- Raw interface pointers and vtable calls instead of managed `[ComImport]` interfaces through
  WinRT.Runtime (`IconService.TryGetJumboIcon`: `GetIcon` by vtable slot 10).
- Handlers on long-lived publishers are unsubscribed in `Closed` (`OrbitMetrics.Changed` is; check
  every new subscription to the manager or a static event).
- Whole-file stores publish through `AtomicWrite` with a bounded retry.
- A timer is stopped in the owning window's `Closed`.
- A deserialized model is repaired to the shape its types promise before anything walks it
  (`AppConfig.RepairShape`): JSON is not bound by C# nullability, so `"Items": [null]` parses and
  then throws on the launch path. "Malformed JSON" tests catch the file that does not parse; seed
  the file that parses and is wrong.

## 7. Fault-injection checklist (what to try on every review)

Unreachable / slow network target on the orbit; a locked or read-only config; a 0-byte, huge,
malformed **or well-formed-but-null-riddled** `orbit.json` (`null` for a list, `[null]` for its
entries, a null string); a tool pointed at a multi-GB file; icon that is not an image; a favicon host
that never answers; DPI or monitor change while the orbit is open; theme flip while a tool window
is open; summon during Settings open; Settings opened twice quickly; Quit during an animation; two
instances with the same data directory; disk full on the data directory.
