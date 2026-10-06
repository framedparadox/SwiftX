# Crash forensics (SwiftX, last 90 days)

Crash events: **25** in **7** distinct bucket(s); hang events: 0; .NET unhandled (1026): 3; WER dumps on disk: 9.

| Faulting module | Code | Offset | Count | First seen | Last seen | App versions |
|---|---|---|---:|---|---|---|
| Windows.ApplicationModel.Store.dll | 0xc0000005 | 0x00000000000b6902 | 2 | 2026-09-15 23:31 | 2026-09-22 21:35 | 1.0.1.0, 2.0.0.0 |
| Microsoft.UI.Xaml.dll | 0xc000027b | 0x00000000003a9c5d | 11 | 2026-09-13 18:34 | 2026-09-19 18:28 | 1.0.1.0 |
| CoreMessagingXP.dll | 0xc000027b | 0x0000000000097de6 | 1 | 2026-09-17 23:46 | 2026-09-17 23:46 | 1.0.1.0 |
| coreclr.dll | 0xc0000005 | 0x0000000000356fef | 1 | 2026-08-30 18:11 | 2026-08-30 18:11 | 1.0.0.0 |
| Microsoft.UI.Xaml.dll | 0xc000027b | 0x00000000003a6d95 | 5 | 2026-08-08 18:52 | 2026-08-12 23:48 | 1.0.0.0 |
| KERNELBASE.dll | 0xc000041d | 0x00000000000c187a | 3 | 2026-08-08 17:05 | 2026-08-08 17:52 | 1.0.0.0 |
| KERNELBASE.dll | 0xe0434352 | 0x00000000000c187a | 2 | 2026-08-08 17:05 | 2026-08-08 17:52 | 1.0.0.0 |

## Dump SwiftX.exe.40872.dmp (2026-09-22 21:35) - code identifiers recovered
- `SwiftX.dll` x4
- `SwiftX.exe_40872.txt` x3
- `Windows.ApplicationModel.Store.dll` x2
- `Windows.System.Launcher.pdb` x1
- `Windows.ApplicationModel.Store.pdb` x1
- `SwiftX.ShellWindow.RateOnStore_Click()` x1
- `SwiftX.Services.StoreFeedback.RequestRatingAsync()` x1
- `SwiftX.Services.StoreFeedback+<RequestRatingAsync>d__1.MoveNext()` x1
- `SwiftX.Program.Main()` x1
- `Windows.System.Launcher.dll` x1

## Dump SwiftX.exe.28620.dmp (2026-09-19 18:24) - code identifiers recovered
- `SwiftX.dll` x6
- `SwiftX.exe_28620.txt` x3
- `SwiftX.ShellWindow.BuildItemIcon()` x1
- `SwiftX.ShellWindow.BuildElementSection()` x1
- `SwiftX.ShellWindow.<BuildElementSection>b__319_0()` x1
- `SwiftX.Services.IconService.LoadIconAsync()` x1
- `SwiftX.Services.IconService+<ShellIconAsync>d__4.MoveNext()` x1
- `SwiftX.Services.IconService+<LoadIconAsync>d__3.MoveNext()` x1
- `InterfaceMarshaler.ConvertToManaged` x1
- `SwiftX.ShellWindow.<.ctor>b__49_6()` x1
- `SwiftX.ShellWindow.RebuildItemsList()` x1
- `SwiftX.Interop.NativeMethods.SHGetImageList()` x1
- `SwiftX.ShellWindow.BuildItemRow()` x1
- `SwiftX.Services.IconService.ShellIconAsync()` x1
- `SwiftX.Program.Main()` x1
- `SwiftX.Services.IconService.TryGetJumboIcon()` x1
- `SwiftX.ShellWindow.LoadRowIcon()` x1

## App log
`%USERPROFILE%\AppData\Local\Temp\swiftx.log`: exists = True, 31466 bytes, last write 2026-10-01 00:11:12, 19 fault-like lines

