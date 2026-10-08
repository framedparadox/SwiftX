# Changelog

All notable changes to SwiftX will be documented in this file.

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
