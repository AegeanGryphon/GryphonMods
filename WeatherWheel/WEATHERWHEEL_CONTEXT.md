# WeatherWheel Mod — Context Document

## Overview
WeatherWheel is a BepInEx IL2CPP mod for LumenTale: Memories of Trey that adds a radial weather selection wheel to the overworld. Press C (or right stick click on controller) to open the wheel, pick a weather type, and it pins that weather to the current map. Selecting Clear sets Clear weather and removes the pin.

---

## Files

| File | Purpose |
|---|---|
| `WeatherWheel\WeatherWheel.csproj` | Project file — references BepInEx, IL2CPP interop, Unity UI, InputSystem, HarmonyX |
| `WeatherWheel\MyPluginInfo.cs` | GUID: `com.corym.lumentale.weatherwheel` |
| `WeatherWheel\Plugin.cs` | Loads the mod, registers the MonoBehaviour, applies Harmony patches |
| `WeatherWheel\WeatherWheelUI.cs` | All UI construction and input logic |

---

## Architecture

### MonoBehaviour injection
`WeatherWheelUI` is registered with `ClassInjector.RegisterTypeInIl2Cpp<WeatherWheelUI>()` and added to a `DontDestroyOnLoad` GameObject. **Unity does not reliably call `Update()` on injected types in this BepInEx version**, so the update loop is driven by a Harmony postfix on `WeatherController.Update()` that calls `WeatherWheelUI.Instance.Tick()`.

### Harmony patches (Plugin.cs)
| Patch target | Type | Purpose |
|---|---|---|
| `WeatherController.Update` | Postfix | Drives `WeatherWheelUI.Tick()` every frame |
| `TreyGameplayController.StartPreparing` | Prefix | Blocks holoken throw (left-click) while wheel is open |
| `GameMaster.OpenMenu` | Prefix | Blocks pause menu from opening while wheel is open |
| `GameMaster.OpenMapMenu` | Prefix | Blocks map menu from opening while wheel is open |

### InputAction suppression (on Open/Close)
`TreyGameplayController.MoveHUDLeft` and `MoveHUDRight` (the Q/E holoken-mode-switch actions) are `.Disable()`d on wheel open and `.Enable()`d on wheel close.

---

## Input Scheme

### Keyboard / Mouse
- **C** — open wheel (if `WeatherController.Instance != null && !IsInsideOrAnispace`)
- **Mouse proximity** — highlight: checks if mouse is within 44 canvas-units of each icon's screen position; if not over any icon, defaults to Clear
- **Left click** — select highlighted weather
- **Escape / Right click** — close wheel

### Controller
- **Right stick click (RS)** — open wheel
- **Right stick direction** — highlight: angle-based via `AngleToSlice()`, dead zone 0.30f magnitude
- **A / Cross** — select highlighted weather
- **B / Circle** — close wheel

### Default highlight
When mouse is not over any icon OR stick is in dead zone → Clear is highlighted (index 0).

---

## Weather Wheel Logic

### Slice layout
- 10 slices, one per `Weathers` enum value (0–9), in order: Clear, HarshSun, Rain, HeavyRain, SandStorm, Hail, Rainbow, Fog, Snow, AshesRain
- `StartDeg = -90f` (Clear at top), advancing clockwise at 36° per slice
- `LayoutRadius = 168f` canvas units

### AngleToSlice
```csharp
float shifted = (angleDeg - StartDeg + 360f + SliceDeg * 0.5f) % 360f;
return Mathf.FloorToInt(shifted / SliceDeg) % SliceCount;
```

### Selecting Clear
Calls `ApplyAndUnpin()`: sets `WeatherController.ChangeWeather(Weathers.Clear)` AND removes the map entry via `MapsWeather.RemoveWeather(mapGuid)`, so the game's natural weather cycle can resume.

### Selecting any other weather
Calls `ApplyWeather(weather)`: sets weather via `WeatherController.ChangeWeather()` AND pins it via `Player.PlayerState.MapsWeather.AddWeather(mapGuid, weather, 999_999)`. Map GUID is added to `_modPinnedMaps` (static `HashSet<string>`).

### Story weather detection
A map is considered story-locked if:
1. `MapsWeather.HasWeather(mapGuid)` is true
2. `MapsWeather.GetWeather(mapGuid).Duration == -1` (infinite = story-set)
3. `_modPinnedMaps` does NOT contain the mapGuid (i.e. we didn't set it)

When locked: all pill backgrounds go grey, inputs are ignored, status text shown.

---

## UI Structure

```
WeatherWheelCanvas (Canvas, ScreenSpaceOverlay, sortOrder 9999, ScaleWithScreenSize 1920x1080)
└── WheelRoot (transparent fullscreen RectTransform — no background image)
    ├── Wheel (transparent 460×460 container, centred)
    │   └── Slice_<Weather> × 10 (transparent 72×72 container at layout position)
    │       ├── Icon (Image, weather sprite from MiniMap.Instance.WeatherSprites[i], 56×56)
    │       └── Pill (Image, procedural rounded rect, 92×22, at y=-50)
    │           └── Text (weather name)
    ├── Status (Text, amber italic, at y=-228 — story weather warning)
    └── HintBar (Image, procedural rounded rect, 560×34, centred at bottom)
        └── Hint (Text, keybind summary)
```

### Procedural rounded rect sprite
Generated at runtime via `MakeRoundedRectSprite(w, h, radius)`:
- Draws a pixel-perfect rounded rect into a `Texture2D` using `Color32[]`
- Creates a `Sprite` with 9-slice border `new Vector4(r, r, r, r)` so it scales to any size
- Used for both name pills (64×20, r=10) and hint bar (256×40, r=12)

### Visual state
Pill backgrounds change color based on state:
- Default: `(0.04, 0.02, 0.14, 0.78)` dark navy
- Hovered: `(0.40, 0.24, 0.80, 0.95)` bright purple
- Active (current weather): `(0.20, 0.10, 0.46, 0.90)` mid purple
- Locked (story weather): `(0.18, 0.18, 0.18, 0.58)` grey

---

## Key Game APIs Used

| API | Usage |
|---|---|
| `WeatherController.Instance` | Null-check for CanOpen; `IsInsideOrAnispace`; `CurrentWeather` |
| `WeatherController.ChangeWeather(Weathers)` | Static — changes weather immediately |
| `Player.PlayerState.MapsWeather` | `WeatherDurationByMapGuidData` — `AddWeather`, `RemoveWeather`, `HasWeather`, `GetWeather` |
| `WeatherController.WeatherMapInfo.Duration` | `-1` = infinite (story weather) |
| `MiniMap.Instance.WeatherSprites` | `Sprite[]` indexed by `(int)Weathers` — weather icons |
| `PlayerController.Instance.CurrentMap.MapGUID` | Current map identifier for pinning |
| `TreyGameplayController.MoveHUDLeft/Right` | `InputAction` — disabled while wheel open |
| `GameMaster.OpenMenu/OpenMapMenu` | Patched to skip while wheel open |

---

## Known Issues / Notes
- `MiniMap.Instance` may be null when wheel is first built (if opened very early). Icons simply won't show for that session — non-critical.
- The `_modPinnedMaps` HashSet is static and resets when the game closes. On next session, existing pins set by this mod will appear as "natural" entries (duration 999,999 ≠ -1), so they won't be mistakenly treated as story weather.
- `WeatherController.Update` is the tick driver — if WeatherController is destroyed/inactive the wheel stops responding. Has not been observed in practice.
