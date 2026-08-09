# SwiftX

A quick-access **radial ring** for Windows 11 — summon with a hotkey, launch apps/files/folders/links, pop groups open into an outer ring, and open built-in JSON/XML formatters.

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

- **Summon / dismiss:** `Ctrl+Shift+S` (default; remappable in Settings → Shortcuts), or left-click the tray icon
- **Hover** a bubble to pop its full name out in a pill beside it
- **Click** a leaf to launch; **Group** pops its children out into a concentric outer ring — select it again to collapse
- **Center hub** shows the app logo; click it to collapse an open group
- **Settings** and **Close** are built in to the last two slots — never hidden, reordered, or removed, leaving 6 user slots
- **Esc** collapses an open group, or dismisses the ring when none is open; click empty space / dead zone to dismiss
- **Tray → Settings** opens the home ring editor and other pages
- **Tools:** JSON and XML formatters (Format / Minify / Validate / indent / Copy / Save / Clear)

## Settings

Persisted at `%AppData%\SwiftX\ring.json` (override with `SWIFTX_DATA_DIR`).

| Page | What it does |
|------|----------------|
| Home | Live ring customizer — add, edit, remove, open groups |
| General | Theme (Light / Dark / System), ring item size (Small / Medium / Large), ring radius, start with Windows, import/export, reset |
| Shortcuts | Summon hotkey capture |
| Apps & links | Flat list with show/hide and remove — Settings and Close aren't listed |
| Tools | Toggle JSON / XML on the ring; open formatters |
| About | Version and privacy note |

## Theme

Solid **black** (dark) / **white** (light) surfaces only — no acrylic, Mica, or glass.

## Phase 1 scope

Hotkey ring + settings home customizer + JSON/XML tools. Full item kinds including Group → outer-ring flyout. English only.


## TODO

- If more items are added add another ring
- Animation - to pop out the items, to close the dock. 
- allow dock to rotate 

- Change layout from circular to 
 - pyramid
 - timer scroll
 