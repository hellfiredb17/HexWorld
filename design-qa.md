# Layer Selection Design QA

- Source visual truth: `C:\Users\dunca\Dev\HawkNet\output\mockups\layer-selection-ui-mockup.png`
- Implementation screenshot: `C:\Users\dunca\Dev\HawkNet\output\layer-selection-capture.png`
- Combined comparison: `C:\Users\dunca\Dev\HawkNet\output\layer-selection-comparison.png`
- Material-dialog evidence: `C:\Users\dunca\Dev\HawkNet\output\layer-selection-material-dialog.png`
- Empty-default evidence: `C:\Users\dunca\Dev\HawkNet\output\layer-selection-empty-default.png`
- Source pixels: 1672 × 941
- Implementation pixels: 2240 × 1260 for a 1280 × 720 logical Avalonia window at 1.75 display density
- CSS/logical viewport: 1280 × 720; device scale factor: 1.75
- Normalization: implementation capture downsampled with Lanczos to 1672 × 941; both sides use the same 16:9 crop
- State: Layer 0, forest-floor-tile.png selected, three attributes, default footer guidance visible

## Findings

No actionable P0, P1, or P2 differences remain.

- Fonts and typography: the implementation uses the existing HawkNet Inter configuration. Header, panel titles, labels, values, helper copy, and footer copy retain the source hierarchy without wrapping or clipping.
- Spacing and layout rhythm: the header/body/footer frame, responsive 40/60 split, gutters, panel radii, card spacing, and persistent actions match the source composition. The native controls are slightly denser than the generated mock but remain within P3-level platform rendering variation.
- Colors and visual tokens: existing HawkNet charcoal, border, off-white, muted copy, cyan accent, and subdued danger treatment match the approved palette. ComboBox chevrons were explicitly corrected to maintain visible contrast.
- Image quality and asset fidelity: the existing high-resolution forest texture is sharp inside the caller-supplied hex preview. Folder, close, add, delete, and reorder visuals use licensed Microsoft Fluent System Icons; no placeholder imagery or handcrafted icon art remains.
- Copy and content: Layer 0, Material Preview, Attributes, Add Attribute, Type, Value, Movement, Energy, HitPoints, helper copy, filename, attribute count, and Update Layer all match the approved state.
- Accessibility and affordances: icon-only actions have automation names and tooltips. The clickable preview, reorder handle, remove actions, and primary update action have clear interactive states.

## Full-view Comparison Evidence

The combined comparison shows matching information architecture and visual balance: preview on the left, three ordered cards on the right, filename/close header, and the full-width status/action footer. No persistent control is cropped or outside the viewport.

## Focused Region Comparison Evidence

Focused inspection used the 2240 × 1260 native capture and the material-dialog capture. Type chevrons, string values, Fluent reorder/delete icons, cyan hex edge, helper copy, texture filename, directory content, selected material outline, and footer text are sharp and readable. Separate crops were unnecessary at this density.

## Interaction and Runtime Checks

- Clicking the material preview opened the existing `TextureSelectorWindow`.
- Selecting `rough-ground-tile.png` updated both the layer preview and displayed filename immediately; closing the dialog returned to the layer view.
- Native pointer drag reordered Movement from first to last, producing Energy → HitPoints → Movement and the expected status message.
- Native Add Attribute increased the row/remove-control count from three to four; removing the added row returned it to three.
- Native Update Layer produced the `Layer updated.` state.
- Unit tests verify normalized initial state, add/remove/reorder behavior, texture application and rollback, immutable ordered snapshots, Update/Close reasons, and context validation.

## Comparison History

1. Initial native comparison found a P1 blank preview caused by an empty overlay button controlling layout. Fixed by placing the renderer beneath a stretched transparent interaction layer; the revised capture shows the textured cyan-outlined hex and grid.
2. The first normalized comparison found P2 low-contrast ComboBox chevrons and a more vivid delete treatment than the mock. Fixed with local Fluent glyph resources and subdued danger styling. The hex scale was also increased to match the approved composition.
3. Final normalized comparison found no remaining actionable P0/P1/P2 differences.

## Open Questions

- None. The caller still controls the concrete model renderer through the preview-adapter contract, as intended.

## Implementation Checklist

- [x] Reusable embedded view and standalone window
- [x] Caller-provided preview and Texture Selector integration
- [x] String-valued typed attributes
- [x] Add, remove, and pointer drag reorder
- [x] Update and Close immutable state propagation
- [x] HawkNet theme and licensed icons
- [x] Native interaction, build, test, and visual verification

## Follow-up Polish

- P3: native Avalonia input typography is slightly smaller than the illustrative generated mock at the normalized comparison size; this is consistent with existing HawkNet controls.

## Post-QA Adjustments

- Default state now contains zero attributes and applies no texture; the untextured caller-provided model remains visible with `None` shown as its material.
- Reordering now calculates a stable insertion slot from the midpoints of all non-dragged rows. A native check using distinct values confirmed `value-1`, `value-2`, `value-3` becomes `value-2`, `value-3`, `value-1` without oscillation.

final result: passed
