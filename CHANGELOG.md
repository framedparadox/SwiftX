# Changelog

All notable changes to SwiftX will be documented in this file.

## [Unreleased]

### Added
- Icon colour: the "Choose an icon" picker has an Icon colour section (swatches, any other colour,
  and a way back to the default), and the right-click menu of a group — in the Add to Orbit list and
  on the ring — has Edit icon. The colour tints the item's glyph on the orbit, the Orbit screen
  preview, the sidebar and the Elements cards.
- Clicking a group in the Add to Orbit list opens it on the Orbit screen, showing what is inside it.
- A GitHub logo that reads on both light and dark, used for GitHub links.

### Changed
- "New group…" in Add to Orbit opens the "Add to orbit" window on its Group tile. On a full orbit it
  now says so on the row instead of doing nothing visible.
- Items added to a group from the Add to Orbit list show up in the group at once.
- "Add something else…" is now "Add to Orbit…".
- The first-run guide offers Google instead of Stack Overflow.
- The default GitHub link no longer wears the Data tool's glyph; existing orbits that still have it
  are updated.
- Renaming a group no longer resets an icon you gave it.

## [2.0.2] - 2026-10-05

### Added
- Updated the Windows app metadata to the next release version.
- Prepared the MSIX package bundle for the 2.0.2 release.

### Changed
- Bumped the project, assembly, file, and package identity versions to 2.0.2.0.
- Kept the package manifest version aligned with the project assembly version.

### Verification
- Built and validated the x64 and ARM64 MSIX packaging flows for the release.
