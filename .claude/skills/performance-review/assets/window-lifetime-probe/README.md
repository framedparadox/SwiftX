# Window-lifetime probe

A throwaway WinUI 3 app that answers two questions in about 30 seconds per variant: **is a closed
window collected, and what does one window cost in GDI objects, handles and private bytes?** It
isolates the framework from the product, so a cause can be named (or ruled out) without guessing.

It exists because "the Settings window leaks" was chased for an hour through the product code
(icons, animations, backdrop, chrome, manager events: every fix measured, none moved the numbers)
and was settled in minutes once the product's own noise was taken out of the picture.

## Use

Copy this folder **outside the repository** (the skill's scratch folder), then:

```powershell
dotnet build LeakProbe.csproj -p:Platform=x64 -c Release
$exe = ".\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\LeakProbe.exe"
foreach ($v in 'none','closed-lambda-this','shell-like-this','shell-like-unsub','ctl-titlebar') {
    $json = ".\$v.json"
    Start-Process $exe -ArgumentList $v, 30, $json -Wait
    $r = Get-Content $json | ConvertFrom-Json; $n = $r.cycles - 1
    '{0,-22} alive {1,2}/{2}  GDI/window {3:N1}  handles/window {4:N1}' -f $r.variant, $r.aliveAfterFullGc, $r.cycles,
        (($r.gdiLast - $r.gdiFirst) / $n), (($r.handlesLast - $r.handlesFirst) / $n)
}
```

Windows flash on screen briefly (30 x 250 ms per variant). `aliveAfterFullGc` out of `cycles` says
whether windows are collected: 0 means all were, `cycles` means none. GDI and handles per window say
what the window costs natively. To test a hypothesis, add a `case` to `ConfigurableWindow` that
reproduces just that pattern; change one thing per variant and always run `none` as the control.
`ctl-<a>+<b>` builds a window from control kinds (`colorpicker`, `listview`, `titlebar`, ...). To ask
"would a newer SDK fix it?", change the `Microsoft.WindowsAppSDK` version in the csproj and rerun.

## What it found for SwiftX (Windows App SDK 2.4.0, .NET 10, 30 windows each; 2.5.1 identical)

**1. Handlers on a window's own events root the window.**

| Variant | Alive after full GC |
|---|---:|
| `none` | 0 / 30 |
| XAML `Click=""` handlers on child elements | 0 / 30 |
| `content-loaded-this` (`Loaded` on a child) | 0 / 30 |
| `appwindow-closing-this` (`AppWindow.Closing`) | 0 / 30 |
| **`closed-lambda-this`**, `closed-method-group` | **30 / 30** |
| **`activated-lambda-this`** | **30 / 30** |
| **`shell-like-this`** (Activated + Closed, as ShellWindow did) | **30 / 30** |
| `activated-kept-closed-unsub` (Closed unsubscribes itself, Activated stays) | 30 / 30 |
| `closed-unsub-self` | 0 / 30 |
| `closed-weak`, `activated-weak`, `shell-like-weak` (weak-reference delegate target) | 0 / 30 |
| **`shell-like-unsub`** (named handlers, all removed inside `Closed`, itself included) | **0 / 30** |

`Window` is not a `DependencyObject`, so the XAML reference tracking that lets the GC see through a
managed-to-native cycle does not cover it: a handler of the window's own on the window's own event
is a strong reference the native window holds, and the native window lives as long as that
reference does. Handlers on child elements, and on `AppWindow`, do not matter. The fix is
mechanical: named handlers, and `-=` every one of them, `Closed` included, inside `Closed`.
(Related upstream reports: microsoft/microsoft-ui-xaml #9960, #7282, #9063.)

**2. A custom title bar leaks native resources per window, collected or not.**

| Variant (30 windows) | GDI / window | handles / window |
|---|---:|---:|
| `ctl-none`, `mica`, `acrylic`, every common control | 0 | 4-10 |
| `ctl-tbextend` (`Window.ExtendsContentIntoTitleBar = true`) | **49** | **57** |
| `ctl-titlebar` (+ `SetTitleBar`) | 49 | 56 |
| `ctl-tbapp` (`AppWindow.TitleBar.ExtendsContentIntoTitleBar`) | 32 | 39 |
| `ctl-tbnotitle` (`SetBorderAndTitleBar(true, false)` only) | 26 | 45 |
| `ctl-tbset` (`SetTitleBar` alone, no extend) | 0 | 4 |
| revert the title bar in `Closing` / `Closed`, hide first | 48-57 | 55-65 |

None of the workarounds helps; the cost is paid at creation. At the 10 000-object GDI quota that is
about 200 windows per process. Application of the seven `AppWindowTitleBar` colour setters in a tight
loop adds about 6 GDI per application (0 / 1 / 2 / 5 / 20 applications: 51 / 59 / 77 / 119 / 166).

The probe also showed the limit of `private bytes` as a leak metric: with simple content, windows
that were collected and windows that were not cost the same private bytes (native heaps do not
return pages promptly). Use object counts after a forced GC, and GDI / handle counts, as the verdict.
