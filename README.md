<p align="center">
  <img src="img/SwiftX.png" alt="SwiftX logo" width="140" height="140">
</p>

<h1 align="center">SwiftX</h1>

<p align="center"><b>A quick-access radial orbit for Windows 11.</b></p>

Summon it with a keyboard shortcut, a mouse button, a screen corner, or the tray icon; launch apps,
files, folders and links from it; pop groups open into an outer orbit; and reach a set of built-in
developer tools (JSON/XML formatter, local secure values, Base64, certificate inspector) without
leaving it.

Everything runs on your own machine. There is no account, no telemetry, and nothing is uploaded.

<p align="center">
  <img src="img/SwiftX2_OrbitDock.png" alt="The orbit open on the desktop" width="70%">
  <br><sub><em>The orbit, summoned over the desktop — items on eight slots around a floating hub.</em></sub>
</p>

More screenshots are spread through the sections below, next to what they show.

---

## Contents

- [Requirements](#requirements)
- [Build, test, run](#build-test-run)
- [The orbit at a glance](#the-orbit-at-a-glance)
- [Summoning the orbit](#summoning-the-orbit)
  - [Keyboard](#keyboard)
  - [Mouse button](#mouse-button)
  - [Tray icon](#tray-icon)
  - [Hot corner](#hot-corner)
- [Using the orbit](#using-the-orbit)
- [The corner quadrant](#the-corner-quadrant)
- [The SwiftX app](#the-swiftx-app)
  - [Home](#home)
  - [First run](#first-run)
  - [Orbit screen](#orbit-screen)
  - [Elements](#elements)
  - [Tools](#tools)
  - [Settings → General](#settings--general)
  - [Settings → Feedback & Support](#settings--feedback--support)
  - [Settings → About](#settings--about)
  - [Settings → Privacy & Data](#settings--privacy--data)
- [Built-in tools](#built-in-tools)
- [Configuration file](#configuration-file)
- [Theme](#theme)
- [Language](#language)
- [Troubleshooting](#troubleshooting)
- [Roadmap](#roadmap)

---

## Requirements

- Windows 10 1809+ / Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- x64 or ARM64

## Build, test, run

```bash
dotnet build src/SwiftX/SwiftX.csproj -p:Platform=x64
dotnet test  tests/SwiftX.Tests/SwiftX.Tests.csproj -p:Platform=x64
dotnet run   --project src/SwiftX/SwiftX.csproj -p:Platform=x64
```

Substitute `-p:Platform=ARM64` on ARM hardware. Or launch the built `SwiftX.exe` from
`bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\`.

Only one SwiftX runs at a time per data directory: a second launch exits rather than starting a
rival tray icon and hotkey. The lock is keyed to the data directory, so a test instance pointed at
a different `SWIFTX_DATA_DIR` can run alongside your real one.

> For source structure, test coverage, theming internals, brand regeneration, and Store packaging,
> see [docs/developer-reference.md](docs/developer-reference.md).

---

## The orbit at a glance

The orbit is a **transparent, always-on-top overlay** the size of the monitor's work area. Only the
items themselves take input; every other pixel is click-through.

| Piece | What it is |
|-------|------------|
| **Hub** | The app mark at the center, floating on a drop shadow. Clicking it closes an open group, or dismisses the orbit. It animates in and out with each summon. |
| **Slots** | Eight positions around the orbit, 45° apart, slot 0 straight up and clockwise. |
| **User slots** | The first six. Filled from your items in order; empty ones are not drawn. |
| **Settings** | Always slot 7. Hovering pops out **Add new**; clicking opens the SwiftX app window. |
| **Close** | Always slot 8. Shrinks rather than grows under the pointer. |
| **Tools** | One of the six user slots by default — a group holding every built-in tool. |
| **Flyout** | A group opens into a concentric outer orbit, up to eight children. |

Item kinds: **Application**, **File**, **Folder**, **Web link**, **Shortcut** (.lnk), **Group**,
and **Tool**. Icons come from the shell, from a web link's site icon, or from a Segoe Fluent glyph
you pick.

---

## Summoning the orbit

### Keyboard

`Ctrl+Shift+X` by default; press again to dismiss. Remap it in **Settings → General → Invoke** by
clicking the capture button and pressing the combination you want.

- A shortcut must include **Ctrl, Alt, or Win** — Shift alone is not enough for a system-wide key.

- Windows refuses combinations another application already owns. The Invoke card says so instead of
  failing silently, and the old shortcut stays live until a working one is set.

- The switch beside the capture button turns the shortcut off entirely without forgetting it.

### Mouse button

A mouse button can summon the orbit instead of — or alongside — the keyboard shortcut.
Configure it in **Settings → General → Invoke**:

| Setting | Detail |
|---------|--------|
| **Button** | Left, Right, **Middle** (default), X1, or X2. |
| **Gesture** | **Long press** (hold the button for a moment — default), **Double-click**, or **None** (any press). |
| **On/off switch** | Turns the mouse trigger off without clearing the button choice. |

Long press is the default so the middle button's ordinary scroll-wheel click still works normally.
The mouse trigger obeys the same "Hide orbit" tray toggle as every other summon method.

### Tray icon

- **Left-click** — summon or dismiss.

- **Right-click** — Show/Hide orbit · Add item… · Settings · Quit.

"Hide orbit" is a deliberate off switch: while hidden, neither the hotkey, the mouse trigger, nor
the hot corner brings the orbit back until you show it again.

### Hot corner

macOS-style, on by default at the **top-left**. Push the pointer into the corner and hold it for a
moment; the orbit appears without a click.

| Behavior | Detail |
|----------|--------|
| **Trigger patch** | A 6-pixel square at the very corner of the display. |
| **Dwell** | The pointer must stay in the patch for **250 ms**. A pass-through does not count. |
| **Re-arm** | Will not fire again until the pointer leaves — parking in the corner summons once. |
| **Drag guard** | While any mouse button is held, the dwell does not start. |
| **Per monitor** | Measured against the full display bounds of whichever monitor the pointer is on. |
| **A toggle** | Reaching the corner while the orbit is open dismisses it. |
| **Hidden** | "Hide orbit" from the tray is honored. |
| **Detection** | Polled every 50 ms via `WM_TIMER`. No system-wide mouse hook is installed. |

Pick the corner and shape in **Settings → Home → Hot corner**:

- **Orbit** (default) — the whole orbit, drawn at the corner.

- **Quadrant** — four items only, on a quarter circle centered on the corner (see
  [The corner quadrant](#the-corner-quadrant)).

<p align="center">
  <img src="img/SwiftX2_HotCorner.png" alt="The hot corner, opened in its Orbit shape" width="60%">
  <br><sub><em>The top-left corner fired, drawing the whole orbit anchored to that corner.</em></sub>
</p>

---

## Using the orbit

| Action | Result |
|--------|--------|
| **Hover** an item | Its full name appears in a pill beside it, on the same radial as the item. |
| **Hover** a group | After a short dwell, opens into the outer orbit. |
| **Click** a leaf | Launches it and dismisses the orbit. |
| **Click** a group | Opens it. |
| **Click** the gaps | Activates the angularly nearest item; a click on dead space dismisses. |
| **Click** the hub | Closes an open group, or dismisses the orbit. |
| **Esc** | Closes an open group first, then the orbit. |
| **Click anywhere else** | Dismisses. The click reaches whatever is under it. |

A group wears a short arc on its outward edge to distinguish it from a leaf item.

---

## The corner quadrant

With **Quadrant** selected, the hot corner puts four items on a quarter circle at the corner of the
screen. The pointer is already there, so every item is a short flick away.

The four items by default are:

1. **QuickAccess** — every web link and shortcut on your orbit, gathered into one group. Drops out
   if you have none.
2. **Notepad** — your orbit's own Notepad if it has one, or a default stand-in.
3. **Tools**
4. **Settings**

Edit the corner on the Home page's canvas. Your first edit writes the arrangement into the config;
from then on the corner is your own. Groups work inside a quadrant: expanding one fans its children
out into an outer arc, slid back inside the quarter if it would throw items off screen.

---

## The SwiftX app

The companion window — where you customize what the orbit holds, tweak its look, manage the
built-in tools, and change app-wide settings. Reached from the orbit's Settings item, the tray
menu, or by launching SwiftX again.

### Home

A greeting header over three portrait boxes:

| Box | Opens |
|-----|-------|
| **Orbit** | The live orbit customizer |
| **Tools** | The built-in developer tools |
| **Elements** | The apps, files, folders, links and groups on the orbit |

<p align="center">
  <img src="img/SwiftX2_Home.png" alt="Home" width="70%">
  <br><sub><em>Home, with its three boxes.</em></sub>
</p>

A slim tab on Home's right edge opens a **How to** guide — a drawer with four topics (setting up
the orbit, invoking it, using the hot corner, changing items), written from your actual
configuration. The gear icon at the top-right opens **Settings**.

### First run

The first launch opens a three-step set-up guide: a welcome, a picker for what goes on the orbit,
and the keyboard shortcut and hot corner that summon it — along with *Start with Windows* and
*Desktop shortcut*. Nothing is written until **Finish**. Re-run it any time from **Settings →
General → Set-up guide**.

<p align="center">
  <img src="img/SwiftX2_OnBoarding.png" alt="Step 1 of the first-run set-up guide" width="60%">
  <br><sub><em>Step 1 of 3 — the welcome step.</em></sub>
</p>

### Orbit screen

A live preview of your orbit with two permanent sidebars:

**Left sidebar — ADD TO ORBIT** — an **Apps / Shortcuts / Folders / Links / Groups / Tools**
accordion. Click a row to place it in the first free slot (ticked rows are already on the orbit).
**Add to Orbit…** at the bottom opens the full add dialog. Drag from this panel onto a slot, a
group, or a name pill.

**Right sidebar — CONFIGURE ORBIT** — Item size, Item shape, Orbit radius, Hot corner, and Hover
style, all live against the preview.

<p align="center">
  <img src="img/SwiftX2_CustomizeOrbit_Settings.png" alt="The Orbit screen" width="70%">
  <br><sub><em>ADD TO ORBIT on the left, the canvas in the center, CONFIGURE ORBIT on the right.</em></sub>
</p>

**On the canvas:**

- **Click an empty slot** (or its `+`) to add an item.

- **Click an item** for a details card: kind, name, target, and icon — with a glyph picker,
  **Use a picture…**, and **Reset**.

- **Click a group** to open it onto its children; the group's mark at the center closes it.

- **Drag an item** to another slot to rearrange.

- **Right-click an item** for **Replace…** and **Delete**.

- **Reset defaults** in the toolbar restores the orbit to its defaults. Hidden while inside a group.

- **Settings gear** (⚙) in the screen header opens the Settings screen.

- **Corner button** — appears in the canvas corner when the hot corner is on; switches the preview
  to show what that corner opens and lets you edit it.

**CONFIGURE ORBIT settings:**

**Size** — Small / Medium / Large.

**Shape** — Circle, Squircle or Square.

**Radius** — 80–220 px from the hub to each item's center.

**Hover style** — how an item reacts to the pointer:

- **Invert** — swaps fill to the opposite theme color and grows slightly (default).

- **Zoom** — keeps the fill; right pill toggles **In** (grows) or **Out** (shrinks).

- **Tint** — fills with a color you pick; right pill opens the color picker. The glyph adapts for
  contrast. Under High Contrast, Tint behaves as Invert.

- **Animate** — plays a motion: **Pulse** (default), **Bounce**, **Wobble**, **Shake**, **Spin**,
  or **Float**. Falls back to Zoom if Windows animations are switched off.

Close goes red under the pointer whichever style is set.

**Hot corner** — switch, corner picker, and **Orbit** or **Quadrant** choice.

### Elements

A card per orbit item — show/hide, edit, or delete. Groups are listed with their children. **Edit**
reopens the add dialog preserving the item's id and any custom icon. Built-in tools are not listed
here (the Tools screen owns them).

### Tools

One card per built-in tool, with a switch that puts it in the orbit's Tools item or removes it, and
an open button.

<p align="center">
  <img src="img/SwiftX2_DevTools.png" alt="The Dev Tools screen" width="70%">
  <br><sub><em>Every built-in tool with its own open button.</em></sub>
</p>

### Settings → General

<p align="center">
  <img src="img/SwiftX2_Settings.png" alt="Settings — General" width="70%">
  <br><sub><em>Settings → General.</em></sub>
</p>

| Setting | What it does |
|---------|--------------|
| **Theme** | Light, Dark, **Midnight**, **Frosted glass**, or System. See [Theme](#theme). |
| **Accent colour** | A swatch and colour picker. Defaults to violet; **Reset to default** in the flyout. |
| **Language** | 29 languages plus System. See [Language](#language). |
| **Invoke** | The keyboard shortcut and mouse trigger, with their switches. |
| **Start with Windows** | Per-user autostart. |
| **Backup & restore** | Export or import the whole configuration as JSON. |
| **Reset** | Restores theme, accent, hover style, size, radius, shortcut, hot corner, tools, and orbit items to defaults. |
| **Set-up guide** | Re-runs the first-run guide. |

### Settings → Feedback & Support

Three cards with links for general feedback, a star rating, and issue reporting. The stars are
stored locally and never sent anywhere.

### Settings → About

App mark, version, and links to Terms and Privacy; plus a spec card — Windows build, UI framework,
managed runtime, and build type (MSIX or portable).

### Settings → Privacy & Data

What happens to your data: everything stays on the device, no tracking, secure values encrypted for
your account, tools running offline, and how to export or reset.

---

## Built-in tools

All four run entirely on this machine. Nothing you paste or open is uploaded. By default tool
windows are **reused** — opening a tool a second time brings the existing window forward
(`ReuseToolWindows` in the config).

### Data Formatter

Auto-detects and works with **JSON and XML**.

- Paste into the editor to format and detect the type immediately.
- Basic bar: **Format**, **Validate**, **Copy**, **Clear**, **Save**.
- Advanced sidebar: **Minify**, **Sort keys**, **Escape/Unescape**, **Convert to JSON/XML**, indent width.

### Secure values

A local key/secret store. Values are masked in the list, copy to the clipboard on demand, and are
protected with Windows' own per-user encryption (DPAPI) — readable only while signed in to that
account on that device.

### Base64

Encode and decode text, with a URL-safe alphabet option, and encode a file's bytes directly.

### Certificate

Inspects `.cer` / `.crt` / `.der` / `.pem`, PKCS#7 bundles, password-protected PKCS#12/PFX, and
Java KeyStore (`.jks`) files, showing full field detail for every certificate.

---

## Configuration file

Stored at `%AppData%\SwiftX\orbit.json`. Set `SWIFTX_DATA_DIR` to put it (and the icon cache)
somewhere else — useful for a portable install or running a test instance alongside your real one.

Writes are atomic; a config that fails to parse falls back to defaults.

**Key settings:**

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `Theme` | `Light`/`Dark`/`Black`/`FrostedGlass`/`System` | `Dark` | Window theme. `Black` is **Midnight** in the UI. |
| `Language` | see [Language](#language) | `System` | UI language. |
| `Hotkey` | string | `"Ctrl+Shift+X"` | Summon shortcut. |
| `HotkeyEnabled` | bool | `true` | Whether the shortcut is registered. |
| `MouseTriggerEnabled` | bool | `true` | Whether a mouse-button gesture summons the orbit. |
| `MouseTriggerButton` | `Left`/`Right`/`Middle`/`X1`/`X2` | `Middle` | Which mouse button. |
| `MouseTriggerAction` | `LongPress`/`DoubleClick`/`None` | `LongPress` | The gesture. |
| `HotCornerEnabled` | bool | `true` | Whether a screen corner summons the orbit. |
| `HotCorner` | `TopLeft`/`TopRight`/`BottomLeft`/`BottomRight` | `TopLeft` | Which corner. |
| `HotCornerLayout` | `Orbit`/`Quadrant` | `Orbit` | What the corner opens. |
| `HoverStyle` | `Invert`/`Zoom`/`Tint`/`Animate` | `Invert` | Item hover effect. |
| `ItemSize` | `Small`/`Medium`/`Large` | `Medium` | Orbit item size. |
| `ItemShape` | `Circle`/`Squircle`/`Square` | `Circle` | Orbit item shape. |
| `OrbitRadius` | number or null | `null` (100) | Hub-to-item distance, 80–220 px. |
| `ReuseToolWindows` | bool | `true` | Reopening a tool reuses its window. |

For the full schema — all keys, orbit items format, HotCornerOrbit resolution, and startup
migrations — see [docs/developer-reference.md](docs/developer-reference.md).

---

## Theme

Five choices in **Settings → General → Theme**:

| Choice | What it looks like |
|--------|-------------------|
| **Dark** (default) | Layered dark plates; the Mica backdrop shows through them. |
| **Light** | Opaque light surfaces. |
| **Midnight** | One flat near-black surface. Card strokes carry the structure. |
| **Frosted glass** | Transparent window with frosted plates — the desktop reads through it. |
| **System** | Follows Windows; resolves to Light or Dark only. |

Orbit item bubbles are live acrylic in every theme.

**Frosted glass frost level** — **Light** (default), **Medium**, or **Dark** — shown under the
theme cards while Frosted glass is selected.

---

## Language

**Settings → General → Language** — 29 languages plus System:

- **Latin script:** English (US), English (UK), French, German, Spanish, Italian, Dutch, Polish,
  Portuguese (Brazil), Turkish, Czech, Danish, Indonesian, Norwegian, Romanian, Swedish
- **Cyrillic:** Russian, Ukrainian
- **Right-to-left:** Arabic, Hebrew
- **Devanagari:** Hindi
- **Southeast Asian / other:** Thai, Vietnamese
- **Greek:** Greek
- **CJK:** Korean, Japanese, Chinese (Simplified)

Switching language takes effect immediately for the Data Formatter, Secure values, Base64,
Certificate tool windows, and the tray menu. Other UI (Settings, Add item, orbit tooltips) applies
on restart.

---

## Troubleshooting

**The shortcut does nothing.** Another application owns that combination. The Invoke card says so
— pick a different one.

**The hot corner does nothing.**
- Check it is on, and which corner is selected, in Settings → Home.
- Hold the pointer *in* the corner for a moment — a pass-through does not count.
- Let go of the mouse button: a held button suppresses it on purpose.
- It is the corner of the display the pointer is on, not of the desktop as a whole.
- "Hide orbit" from the tray suppresses it until you show the orbit again.

**A tool is missing from the orbit.** Tools live inside the Tools item. If that item was deleted,
re-add a group named `Tools` (or restore defaults) and the next launch will fill it.

**Something looks wrong on screen.** SwiftX writes a diagnostic log to `%TEMP%\swiftx.log`,
recreated on each launch.

**Start fresh.** Quit SwiftX and delete `%AppData%\SwiftX` (or `SWIFTX_DATA_DIR`). The next
launch reseeds the defaults.

---

## Roadmap

Not started; noted here as candidates.

**Next**

- Customizable orbit items
- Customizable orbit design: chemical-formula layout
- Orbit item shape — hexagon

**Application**

- Redesign the application layout as a formal application

**Orbit**

- A second orbit when items outgrow the first
- Multi-orbit support — more than one outer orbit
- Rotating the orbit
- Alternative layouts: pyramid, timer scroll
- Configurable hot-corner dwell, and more than four places in a quadrant

**Drag and drop**

- Auto-add a dropped item to the nearest empty slot
- Drag a dropped item straight onto the outer orbit
- Drag a dropped item into a shortcuts folder/group

**System actions**

One-tap orbit items that toggle or open a Windows setting rather than launching an app or file:

- **Mute / Unmute** — toggle the default audio output's mute state; glyph updates to reflect state.
- **Volume up / Volume down** — nudge master volume by a fixed step.
- **Open Bluetooth devices** — open `ms-settings:bluetooth` directly.
- **Brightness up / down** — adjust display brightness via WMI (laptop panels).
- **Night light toggle** — enable or disable Windows Night light.
- **Focus assist / Do Not Disturb toggle** — suppress notifications without opening Action Center.
- **Open Wi-Fi networks** — show the available wireless networks flyout.
- **Screenshot / Snip & Sketch** — trigger the Windows snipping tool.
- **Lock workstation** — call `LockWorkStation` (Win+L equivalent).
- **Open Action Center** — open the Windows 11 notification panel directly.
- **Toggle dark / light mode** — switch Windows app mode between dark and light.
- **Open Task Manager** — launch Task Manager directly.
- **Empty Recycle Bin** — call `SHEmptyRecycleBin` from the orbit.

**Developer tools**

- An add-on/extension model for third-party developer tools alongside the built-in four

**Data Formatter**

- **YAML conversion** — fits the existing "Convert to JSON/XML" pattern.
- **JSONPath query** — a query box that filters/highlights matches.
- **Tree view** — a collapsible node view alongside the text editor.
- **Diff/compare** — a two-document comparison; likely its own tool page.
