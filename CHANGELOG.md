# Changelog

All notable changes to SwiftX will be documented in this file.

## [3.0.1] - 2026-10-26

### Added
- **More default system operations** — the "Add to orbit" sidebar's Controls section now also
  offers Lock, Sleep, Mute/Unmute, Screenshot (to the clipboard), Empty Recycle Bin, and Task
  Manager, alongside Home/Settings/Close.
- **Map keyboard action** — a new kind of orbit item that plays back a recorded key combination
  (reusing the same capture control as the summon shortcut) instead of launching anything. Unlike
  the other Controls items, more than one can exist, each with its own combination and name; add
  one from the sidebar's "Map keyboard action…" row, and edit it later from its card's Edit button.
  The combination is replayed against whatever window had focus before the orbit was summoned, not
  the orbit itself.

### Fixed
- **Midnight theme — invisible orbit discs and shadow** — the Orbit Layout preview's discs, pill
  and back button relied entirely on their drop shadow to read against the canvas, and Midnight's
  shared system shadow is dark-on-dark there. They now cast a plain white composition shadow under
  Midnight instead, so every shape is visible again.
- **Hot corner preview's back button** — swapped its back-arrow glyph for the same chevron-left
  icon (and matching hover) the group and tool back buttons already use, so every "go back"
  affordance in the app reads as one icon.
- **Dropping something on the "Add to Orbit" sidebar** — a drop now opens the Add to Orbit dialog
  prefilled from what was dropped (name, target, inferred type) instead of adding it straight to
  the orbit with no chance to review or rename it first.

### Changed
- Bumped the project, assembly, file, and package identity versions to 3.0.1.0.

## [3.0.0] - 2026-10-24

### Added
- **Mouse trigger** — a second way to summon the orbit without a keyboard. Any mouse button
  (Left, Right, Middle, X1, or X2) can now summon the orbit on a long press, a double-click,
  or a plain single press. Middle button with long press is the default. Enable and configure it
  in **Settings → General → Invoke**; it runs alongside the keyboard shortcut and the hot corner
  rather than replacing either. `MouseTriggerEnabled`, `MouseTriggerButton`, and
  `MouseTriggerAction` are the new config keys.
- **10 new languages** — Czech, Danish, Greek, Indonesian, Norwegian, Romanian, Swedish, Thai,
  Ukrainian, and Vietnamese join the existing 19, bringing the total to 29 languages plus System.
  All new tables are fully translated; any key a table doesn't cover falls back to English (US)
  as existing languages do.
- **Store update checker** — SwiftX now polls the Microsoft Store in the background for available
  updates and surfaces a notice in **Settings → About** when one is found, with a button to
  install it. Nothing is downloaded automatically; the check is silent until there is something to
  show.
- **Tool window icons** — the Data Formatter, Secure values, Base64, and Certificate windows now
  carry the SwiftX mark as their taskbar and Alt+Tab icon, so they are distinguishable from a
  plain window at a glance.
- **Hand cursor** — interactive controls in the Orbit Settings sidebar (size, shape, radius, hover
  style) now show the pointer cursor when hovered, making them feel clickable rather than flat.

### Changed
- **Window cloaking on activation** — the orbit window is cloaked (invisible to the compositor)
  between the moment it is activated and the moment the first frame is ready to draw. This
  eliminates the brief blank/white flash that could appear on first summon or after a theme change.
  The same guard covers the ShellWindow on first open.
- The language list in **Settings → General → Language** now shows 29 languages. Language
  switching and the per-language font-size compensation both cover the new tables.
- Localization strings for several existing languages were audited and tightened; a handful of
  keys that previously fell back to English now have proper translations.
- `LanguageCatalog` carries the culture tag, flow direction, and font-size compensation for every
  language in one place, replacing scattered per-language constants and making the new tables
  easier to add.
- Bumped the project, assembly, file, and package identity versions to 3.0.0.0.

### Fixed
- `OrbitProfile` now initialises its computed geometry lazily so that a deserialized profile
  never triggers a metric calculation before its items are fully loaded. Previously, opening a
  config that had been written by a build with different metric defaults could yield stale slot
  positions until the first explicit refresh.
- `OrbitItemGeometry` position calculations are now recomputed whenever the item list changes
  length, not only when the radius setting changes. Items added to or removed from a profile
  while the orbit is open now land in the correct positions without requiring a re-summon.
- `SystemOrbitItems` synthesizes Add-new on the quadrant canvas correctly when the corner holds
  fewer than four items; previously a synthesized slot could appear one position too far clockwise
  in edge cases involving a single user item.

## [2.0.4] - 2026-10-20

### Added
- Dropping an item on a group in the Orbit screen's **Quadrant** preview now adds it to that group,
  as it already did on the ring. QuickAccess (gathered from the orbit) and the Tools group do not
  take drops, and a group that is full or already holds the item refuses it, as it does on the ring.
- A group that only the hot corner has (made on the corner's own canvas) is now listed under Groups
  in **Add to orbit**, and offered in an item's **Add to group** menu, beside the orbit's groups.
  Before, nothing added from the sidebar could reach it. Its row says in its tooltip that it is on the
  corner only, and its Remove takes it off the corner.

### Changed
- A group dragged from the **Add to orbit** Groups list onto the hot corner's Quadrant is now that
  same group, not a copy frozen at the moment it was dragged. Items added to it from the sidebar, or
  dropped on it in the preview, show on the corner and on the dock together. Dropped on the ring, a
  group is still copied.
- Bumped the project, assembly, file, and package identity versions to 2.0.4.0, and the About box
  fallback version to match.

### Fixed
- Groups an earlier version copied onto the Quadrant are linked back to the group they were copied
  from when SwiftX starts (and when a configuration is imported), so they show what has been added to
  it since. A copy is matched by name and by holding only things that group holds; one that matches
  no group, holds nothing, or fits two groups is left exactly as it was.
- Adding an item to a group on the Quadrant preview now brings the dock up to date. The drop only
  saved the change, so a dock left open kept showing the group as it was, and the Elements list did
  not move until something else refreshed it. It now tells both, the same way a drop on the ring
  does.
- Placing, moving or removing an item in the Quadrant preview reaches a dock that is open at once,
  instead of the next time it is summoned.

## [2.0.3] - 2026-10-07

### Added
- Group editing in the Add to Orbit flow: groups can now be opened directly from the orbit sidebar,
  items inside a group can be reordered with drag-and-drop or Move up / Move down actions, and each
  item can be removed individually or as a group member without leaving the list in a stale state.
- Icon colour support for groups and orbit items, including a dedicated swatch picker, custom colour
  selection, a reset-to-default action, and the group context-menu Edit icon command.
- A dark/light-safe GitHub logo and GitHub link treatment so external links remain legible across
  themes without the old glyph mismatch.
- Additional onboarding polish, including the first-run guide now pointing to Google and the Add to
  Orbit action label being updated from "Add something else…" to "Add to Orbit…".

### Changed
- "New group…" in the Add to Orbit list now opens the add-to-orbit window on the group tile and shows
  a clear status message when the orbit is already full.
- Group membership updates are now applied immediately after adding an item from the Add to Orbit list,
  and the orbit preview reflects the group contents as soon as they are added.
- Existing orbits with the default GitHub link still carrying the Data tool glyph are now migrated to
  the new GitHub branding automatically.
- Renaming a group no longer clears the icon that had been assigned to that group.
- Any tool can now be added to a group of your own, from a Tools row's right-click **Add to group**
  menu or by dragging it onto a group in the sidebar or on the ring. The copy stays in the group
  across launches, the Tools group keeps its own, and a group's Elements card counts and lists it.
- On the Orbit screen's **Quadrant** preview, clicking a group (Tools, QuickAccess, or one of yours)
  now opens it onto its children, as it does on the ring; the centre mark returns to the quadrant.
  QuickAccess is gathered from the orbit rather than kept, so its arc has no add slots and its
  items cannot be reordered there.
- The icon picker is as wide as its row of icons: the Default colour, Use a picture, Select an app
  or DLL and Reset buttons no longer stretch past the grid. It is a fixed height with no scrollbar:
  the full colour picker now opens in a flyout of its own from the palette tile instead of unfolding
  inside it.
- Right-clicking a group on the Quadrant preview now offers **Edit icon…**, as it does on the ring.
  QuickAccess keeps its icon and colour as part of the corner's arrangement.

### Fixed
- The Add to Orbit workflow now behaves consistently when groups are full, while the list editing UX
  correctly supports both drag reordering and direct remove actions.
- Colour and branding updates are applied consistently across the orbit, preview pane, sidebar, and
  Elements cards instead of being left partially configured.
- Dragging a bubble on the Orbit screen's preview and dropping it where it started no longer leaves
  the name pills hidden until the next redraw.

### Backtracked release history
- This release pulls together the latest group-management and onboarding feature work that followed the
  2.0.2 packaging milestone and builds on the reliability pass from the 2.0.1 line.

## [2.0.2] - 2026-10-05

### Added
- Updated the Windows app metadata to the next release version.
- Prepared the MSIX package bundle for the 2.0.2 release.

### Changed
- Bumped the project, assembly, file, and package identity versions to 2.0.2.0.
- Kept the package manifest version aligned with the project assembly version.
- Documented the release in the project changelog so release history is preserved with the project.

### Verification
- Built and validated the x64 and ARM64 MSIX packaging flows for the release.

## [2.0.1] - 2026-10-01

### Changed
- Bumped the app manifest and project file version to 2.0.1.0.
- Refined orbit animation timing and icon-loading logic to smooth motion and improve reliability during
  item rendering and updates.
- Added focused performance and resilience coverage for orbit timing, icon decode paths, network hangs,
  configuration-shape validation, native capability checks, and allocator budget tracking.
- Updated the app shell about box fallback version string to stay aligned with the current release
  metadata when the assembly value is unavailable.

### Verification
- Ran the project’s resilience and performance-focused test pass to confirm the build remains stable after
  the timing and loading changes.
