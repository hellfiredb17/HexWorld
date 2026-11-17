# Tile Creator validation

- Added `CoreUx/Common/TileCreation`: reusable view, thin window, context, document, events, and view models.
- Uses HawkNet theme, Inter, existing licensed Fluent icons, and caller-supplied model adapters.
- Approved source: `output/mockups/tile-creator-ui-mockup.png` (revised option 2).
- Layer list scrolls inside fixed header/add/footer controls. Rows grow with attribute count.
- A layer zero always exists; last-layer removal is blocked. Layer numbers follow reordered positions.
- Preview clicks open the existing Layer Selection editor. Closing it returns the layer details and refreshes row previews/attributes.
- Layer Selection context now supports signed layer numbers.
- Load / Save As persist versioned `.tile` JSON with ordered attributes and relative texture paths.
- Preview preparation and document validation precede state replacement on load.
- Save uses a complete temporary file before replacing the destination.

Validation:

- Focused Texture Selector, Layer Selection, and Tile Creator suite: 27 tests passed.
- CoreUx build: no warnings or errors.
- Avalonia render harness: `output/tile-creator-qa/TileCreatorQa.csproj`.
- Populated render: 22 layers, nine attributes on layer zero; row height grows from about 214 to 442 logical pixels.
- Scroll extent about 5169 logical pixels exceeds the bounded viewport of about 706 logical pixels.
- Rendered default, populated, and scrolled states inspected for clipping, alignment, and spacing.
- Hosted model controls can be reordered without a visual-parent conflict.
- Programmatic preview click opens Layer Selection; Add Attribute + Close returns details to the tile row.
- Native window launched successfully. Native pointer/picker verification could not be performed because the Computer Use helper failed capture twice with `SetIsBorderRequired failed: No such interface supported (0x80004002)`. Renders and programmatic routed-event checks are not native pointer verification.

Captures:

- `output/tile-creator-default.png`
- `output/tile-creator-populated.png`
- `output/tile-creator-scrolled.png`

Run demo: `dotnet run --project Desktop -- tile-creator`.

Editor refinements:

- Texture Selector no longer displays Load, Save, or Save As; it returns selection through the existing path/event interface. Its result remains available after standalone close.
- Layer Editor no longer displays Update Layer; closing returns the ordered layer snapshot.
- Preview overlay presenter stays transparent in normal, hover, and pressed states; hover adds a cyan border only.
- Render harness checked actual hover/pressed presenter brushes and inspected normal/hover captures.
- Captures: `output/layer-editor-normal.png`, `output/layer-editor-hover.png`, `output/texture-selector-path-only.png`.

Model dropdown validation:

- Choices: Square, Hex Flat, Hex Point; stored as named Model values in tile JSON.
- Missing Model loads as Hex Flat for backward compatibility.
- Model-aware factories supply matching previews to rows, Layer Editor, and Texture Selector.
- Rendered and inspected all three shapes (`output/tile-model-Square.png`, `output/tile-model-HexFlat.png`, `output/tile-model-HexPoint.png`).
- UI harness exercised each dropdown choice and verified geometry in both nested editors.
- Focused suite: 32 tests, including persistence for all models, attribute/texture preservation, invalid model rejection, and failure rollback.

Dropdown freeze fix:

- Captured the hung Desktop process stack in `output/tile-dropdown-freeze-stack.txt`.
- UI thread was waiting inside `WinUiCompositedWindow.Dispose`, reached from ComboBox popup close after pointer release. The WinUI compositor thread was also waiting.
- Desktop now selects DirectComposition, with RedirectionSurface fallback, avoiding WinUIComposition teardown.
- Updated the menu harness to use the actual Desktop AppBuilder and open/close native dropdown popups for all three model selections. It completed nested editor checks, close, and reopen without hanging.
- Desktop build passed without warnings. The native input helper still could not capture the window on this machine; pointer clicks were not reverified through that helper.
