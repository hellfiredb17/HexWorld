# Tile Creator

Reusable tile editing UI in `HawkNet.CoreUx.Common.TileCreation`.
Uses the shared HawkNet theme and the existing Layer Selection / Texture Selector editors.

```csharp
var context = new TileCreatorContext(() => new MyTexturePreviewAdapter())
{
    InitialTextureDirectory = textureDirectory,
    TexturePreviewInteractionHint = "Drag to rotate • Scroll to zoom"
};
var window = new TileCreatorWindow(context);
window.Changed += (_, e) => ApplyTileSnapshot(e.Document);
window.Saved += (_, e) => OnTileSaved(e.FilePath, e.Document);
window.Show();
```

`TileCreatorView` provides the same embeddable content and emits `CloseRequested` for its host.
Both surfaces expose `CurrentDocument`, `FilePath`, `Initialization`, `LoadAsync` and `SaveAsAsync`.
Initial `FilePath` supplies a save location/title; use `LoadAsync` to read a file.
Initial layers must be contiguous, descending, and contain zero. The default is one untextured
layer zero with no attributes. The final remaining layer cannot be removed. Adding above/below
extends the range; dragging moves contents into renumbered positions. Removing a positive or
negative layer closes the gap toward zero. Removing zero renumbers an adjacent layer to zero.

Factories must return fresh preview controls for each row, layer editor, and texture selector.
Controls cannot share visual parents. Created adapters implementing `IDisposable` are owned by
the component. Dispose embedded views when the host is finished with them. The standalone
window disposes its view on close. Model rendering remains entirely caller supplied.

Layer rows grow with their ordered attributes; only the outer layer list scrolls. Drag grips
capture the stable scroll container, support multiple-slot moves and automatically scroll near
the viewport edges. Header, add buttons and footer remain visible.

Save As writes indented versioned JSON with a `.tile` extension. Layers store their number,
texture path (null for None), and ordered string attributes including duplicates. Texture paths
are relative to the tile document where possible. Runtime snapshots contain absolute paths.
Loads validate state and prepare all previews before replacing the current tile; malformed
documents, missing textures and renderer errors preserve the previous tile. Saves replace the
destination only after a complete temporary file has been written. Cancelled pickers preserve
state; errors appear above the footer controls.

Desktop demo: `dotnet run --project Desktop -- tile-creator`.
Approved visual: `output/mockups/tile-creator-ui-mockup.png`.

Layer Selection returns edited details on Close. Texture Selector only chooses a texture path; it has no Load/Save controls. Tile document persistence remains in Tile Creator.

Set InitialDirectory to the host project's preferred tile folder. Load and Save As use the current tile's containing folder first, then InitialDirectory, then the working/app folder. The Desktop demo starts at the HawkNet workspace root. Choose Directory in Texture Selector starts at the current texture folder.

Model selection: the Model dropdown offers Square, Hex Flat and Hex Point. The tile JSON stores
`Model` as `Square`, `HexFlat` or `HexPoint`. Older files without the field default to Hex Flat.
Use the model-aware constructor `new TileCreatorContext(model => new MyPreviewAdapter(model))`
so every row and nested Layer/Texture editor receives the selected model. `InitialModel` defaults
to Hex Flat. Changing the model preserves ordered layers, textures and attributes; preview setup
must succeed before the new model replaces the current state. The legacy parameterless factory
supports its initial model only and reports an error for other models.
