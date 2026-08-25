# SwiftX

A quick-access **radial ring** for Windows 11 — summon with a hotkey, launch apps/files/folders/links, pop groups open into an outer ring, and open a built-in Data Formatter (JSON/XML/CSV).

## Requirements

- Windows 10 1809+ / Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- x64 or ARM64

## Build

```bash
dotnet build src/SwiftX/SwiftX.csproj -p:Platform=x64
```

## Test

```bash
dotnet test tests/SwiftX.Tests/SwiftX.Tests.csproj -p:Platform=x64
```

## Run

```bash
dotnet run --project src/SwiftX/SwiftX.csproj -p:Platform=x64
```

Or launch the built `SwiftX.exe` from the output folder under `bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\`.

## Usage

- **Summon / dismiss:** `Ctrl+Shift+S` (default; remappable in Settings → General), or left-click the tray icon
- **Hot corner:** push the pointer into a corner of the screen and hold it there for a moment — macOS-style,
  on by default at the top-left, and any of the four corners can be picked in Settings → General.
  It fires per monitor, ignores a corner clipped in passing, and stays quiet while a mouse button is held
  so dragging a window into a corner still snaps it the way Windows expects
- **Hover** a bubble to pop its full name out in a pill beside it
- **Click** a leaf to launch; **Group** pops its children out into a concentric outer ring — select it again to collapse
- **Center hub** shows the app logo; click it to collapse an open group
- **Settings** and **Close** are built in to the last two slots — never hidden, reordered, or removed, leaving 6 user slots
- **Tools** is one of those slots by default: a single parent item holding every built-in tool, so they open one
  hop in rather than competing for slots of their own (Settings → Tools picks which of them it holds)
- **Esc** collapses an open group, or dismisses the ring when none is open; click empty space / dead zone to dismiss
- **Tray → Settings** opens the home ring editor and other pages
- **Tools:** Data Formatter (auto-detect JSON/XML/CSV), local Secure values,
  Base64 encode/decode, and a Certificate inspector
  - Basic bar: Format / Validate (clean + pretty-print) / Copy / Clear / Save
  - Pasting into the editor auto-formats and auto-detects the type immediately, not just the
    debounced keystroke-by-keystroke detection typing gets
  - Advanced sidebar: Minify, Sort keys, Escape/Unescape, Convert, indent
  - Secure values: DPAPI-protected key/secret store with masked display + copy
  - Base64: Encode / Decode, URL-safe alphabet, encode straight from a file
  - Certificate: inspects .cer/.crt/.der/.pem, PKCS#7 bundles, password-protected PKCS#12/PFX,
    and Java KeyStore (.jks) files — full field detail per certificate, nothing uploaded anywhere

## Settings

Persisted at `%AppData%\SwiftX\ring.json` (override with `SWIFTX_DATA_DIR`).

| Page | What it does |
|------|----------------|
| Home | Live ring customizer — add, edit, remove, open groups, item size (Small / Medium / Large), ring radius |
| General | Theme (Light / Dark / System), summon hotkey, hot corner, start with Windows, import/export, reset |
| Apps & links | Flat list with show/hide and remove — Settings and Close aren't listed |
| Tools | Toggle Data / Secure values / Base64 / Certificate inside the ring's Tools item; open tools |
| Privacy | What stays on the device, what is encrypted, and the one thing that touches the network |
| About | What SwiftX is, and its version |

## Theme

Solid **black** (dark) / **white** (light) surfaces only — no acrylic, Mica, or glass.

## Phase 1 scope

Hotkey ring + settings home customizer + JSON/XML/CSV data tools + local secure values. Full item kinds including Group → outer-ring flyout. English only.


## TODO

- If more items are added add another ring
- Animation - to pop out the items, to close the dock. 
- allow dock to rotate 

- Change layout from circular to 
 - pyramid
 - timer scroll

### Data Formatter enhancement ideas

Popular JSON tools (JSONFormatter.org, JSONLint, RJL.io) commonly offer these on top of
format/minify/validate. Not started; noted here as candidates for a future pass, roughly in
order of how well they fit the current sidebar-button architecture:

- YAML conversion - fits the existing "Convert to JSON/XML/CSV" pattern directly (add a YAML
  IDataFormat to FormatRegistry).
- JSONPath query - a query box that filters/highlights matches; a new interaction, not just a
  button.
- Tree view - a collapsible node view of the parsed document, alongside or instead of the text
  editor; a bigger UI addition.
- Diff/compare - a two-document comparison; likely its own tool page rather than a Data
  Formatter sidebar addition, given the two-pane layout it needs.

## Add new 

- Add + button to group or folder and auto add to the ring child section
- Add second circle
- Ring - Settings  : add Rearrange quick action