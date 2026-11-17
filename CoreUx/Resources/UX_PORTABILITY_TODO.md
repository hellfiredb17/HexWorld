# CoreUx UX Portability TODO

## Current Priority

The reusable Desktop server/client flow is complete enough for current LAN development. CoreUx is ClanIron-free, game tools are optional integrations, and both server and client setup screens expose validated TCP/UDP configuration.

Near-term work should stay focused on:

- Completing the ClanIron World Builder in `ClanIronSim.CoreUx`.
- Tuning the world viewer from real playtesting.
- Running expanded integration and sustained LAN soak testing.
- Keeping the Avalonia UI usable and easy to iterate on.

Do not build the Unity portability layer until the Desktop UX has proven the core flow.

## Milestone 1 World Viewer TODO

- [x] Run a real Desktop server + client repro and verify:
  - lobby countdown reaches zero
  - server transitions from chat lobby to match
  - client enters match
  - player token appears
  - movement is smooth instead of hopping
  - logs explain any remaining failure path
- [x] Preserve the desktop client runtime after a user-requested disconnect so pressing Connect again can use the stored reconnect token/player id instead of creating a new player.
- [x] Show the lobby ready countdown on the server lobby view.
- [ ] Tune world viewer config after playtesting:
  - `TargetUpdateFramesPerSecond`
  - `IgnoreErrorDistance`
  - `BlendErrorDistance`
  - `BlendMilliseconds`
- [x] Confirm player spawn path in reusable tests/debug contract:
  - no fallback UI tokens
  - spawned navigator comes from the installed game integration state
  - display name maps from match player payload
- [x] Improve server match inspector with movement details:
  - server position
  - render position where available
  - destination
  - max speed
  - max rotation speed
  - movement status
  - path index
- [x] Add a movement debug overlay:
  - [x] toggleable server position marker
  - [x] toggleable render position marker
  - [x] path line
  - [x] current target waypoint
  - [x] final destination marker
  - [x] toggleable correction mode: ignore, blend, or snap
- [x] Test reconnect during match:
  - client disconnects during match
  - [x] client reconnects to the same player id
  - [x] movement commands still route to the player's existing game navigator after reconnect
  - [x] current world state refreshes
  - [x] player token reappears
  - [x] connected/disconnected inspector state is correct
- [ ] Later: evaluate client-side movement prediction after server-approved movement is stable.

## Future Goal

Make HawkNet UX logic portable across Avalonia and Unity UI Toolkit without forcing both renderers to share the same control implementation.

Preferred shape:

```text
HawkNet.Ux
  Shared UX state, plain models, command contracts, asset keys

CoreUx
  Avalonia views, styles, resources, asset resolution

Unity-facing project
  UI Toolkit UXML/USS, Unity asset resolution, Unity-specific binding
```

The shared layer should describe the UI state and available actions. Avalonia and Unity should each render that state using native controls.

## Recommended Naming

Use `HawkNet.Ux` for the future shared UX model/contracts layer.

Good names:

- `HawkNet.Ux`
- `IUiAssetProvider`
- `UiOption<T>`
- `UiAssetKey`
- `UiCommand` or command/action contracts if needed

Avoid `UIRender` as the primary name. It is too vague and implies a full rendering engine rather than shared UX state.

## Suggested Shared Types

Future shared UX models should use portable concepts:

- Asset keys instead of renderer image objects.
- Plain state models for loading, server settings, chat lobby, members, and chat messages.
- `UiOption<T>` for dropdown-like choices.
- Command/action contracts where both Avalonia and Unity need to trigger the same behavior.

Example direction:

```csharp
public sealed record UiOption<T>(T Value, string Label);

public sealed record LoadingScreenState(
    string HeaderText,
    string ContentText,
    string? ImageKey);
```

Avalonia can map `ImageKey` to a bitmap resource. Unity can map the same key to a `Sprite` or `Texture2D`.

## Migration Notes

Current known Avalonia-specific leak:

- `LoadingScreenViewModel` uses Avalonia `IImage`.

Future migration:

- Replace `LoadingScreenViewModel.IImage` with an asset key such as `ImageKey`.
- Add an Avalonia asset resolver in `CoreUx`.
- Add a Unity asset resolver in the Unity-facing project.
- Keep Avalonia controls in `.axaml`.
- Let Unity render equivalent UI Toolkit UXML/USS from the same portable state models.

Keep these renderer-specific:

- Avalonia `.axaml` views.
- Avalonia style/resource dictionaries.
- Unity `.uxml` and `.uss`.
- Unity `Sprite`, `Texture2D`, and UI Toolkit controls.

## Complexity Warning

Asset keys, state models, options, and command contracts are low-to-medium complexity and likely worth doing later.

A full generic control renderer is high complexity. Wrapping buttons, dropdowns, input fields, scrolling lists, focus, validation, styles, accessibility, and layout behavior would effectively become a custom UI toolkit. Do not start there unless the Desktop and Unity implementations prove they need it.

Recommended future order:

1. Finish the simple Desktop server UX app.
2. Identify which viewmodels need to be shared with Unity.
3. Extract plain state models into `HawkNet.Ux`.
4. Replace renderer-specific asset types with asset keys.
5. Build a small Unity UI Toolkit renderer around those shared models.
6. Only consider generic control descriptors if duplicated Avalonia/Unity work becomes painful.
