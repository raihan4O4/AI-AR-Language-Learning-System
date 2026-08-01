# UI Implementation Guide

## Technology

The learner UI uses **Unity UGUI**, not UI Toolkit.

- Rendering: `Canvas` in `ScreenSpaceOverlay` mode
- Controls and layout: `Image`, `Button`, `ScrollRect`, `VerticalLayoutGroup`, `HorizontalLayoutGroup`, `GridLayoutGroup`, `LayoutElement`, and `ContentSizeFitter`
- Text: TextMesh Pro (`TextMeshProUGUI` / `TMP_FontAsset`) for Latin text and a native UniText UGUI
  `MaskableGraphic` for HarfBuzz-shaped Devanagari and Malayalam
- Responsive scaling: `CanvasScaler` with a `1080 x 1440` reference resolution and height matching
- Device cutouts: `LearningSafeAreaFitter` applies `Screen.safeArea`
- Rounded progress bars: custom `LearningPillGraphic`
- Icons: procedural UGUI graphics, so no icon texture atlas is required

There are no UXML or USS files. Most screens are generated from C# at runtime rather than being manually laid out in the Unity scene.

## UI Architecture

```text
LearningMvpController
  -> creates/configures the Canvas and safe-area root
  -> builds Home, Scan, Quiz, and Progress screens
  -> switches screens with CanvasGroup visibility
  -> reads learning data from LearningSessionController/services

LearningUiTheme
  -> shared colors, corner radii, shadows, and button styles

LearningUiMotion
  -> safe-area handling, press feedback, responsive grids,
     and the custom pill progress graphic

ARVocabularyOverlayPresenter
  -> Scan-mode object/translation information card
  -> multilingual font selection

LanguageLearningSceneSetup
  -> deterministic scene wiring plus TMP and UniText font-asset generation
```

The main scene contains the AR/runtime services, but the learner-facing screen hierarchy is rebuilt by `LearningMvpController.EnsureUi()`. Avoid relying on manual scene edits for Home, Scan, Quiz, or Progress because those edits will not be the source of truth.

## Screen Map

All screen methods are in:

`Assets/_Project/LanguageLearning/Unity/Presentation/LearningMvpController.cs`

| Screen | Main method | Change here for |
|---|---|---|
| Home | `CreateNavigation()` | Greeting, summary card, menu cards, outer spacing |
| Scan | `CreateScanControls()` | Back button, language selector, bottom action bar |
| Quiz | `CreateQuizPanel()` | Header, progress bar, language buttons, question/options |
| Progress | `CreateProgressPanel()` | Header, level card, stats, weekly activity, achievements |
| Shared controls | `CreateButton()`, `CreatePanel()`, `CreateText()` | Reusable button, card, and text defaults |
| Scan object card | `ARVocabularyOverlayPresenter.ApplyModernLayout()` | Object-card position, size, padding, and text sizes |

## Common Changes

### Padding and spacing

UGUI layout padding uses this order:

```csharp
layout.padding = new RectOffset(left, right, top, bottom);
layout.spacing = 16f;
```

Examples:

- Home outer padding: edit the `VerticalLayoutGroup` near the start of `CreateNavigation()`.
- Quiz outer padding: edit the layout near the start of `CreateQuizPanel()`.
- Progress outer padding: edit the layout near the start of `CreateProgressPanel()`.
- Card internal padding: find the card's local `HorizontalLayoutGroup` or `VerticalLayoutGroup` and change its `RectOffset`.
- Scan object-card padding: edit `layout.padding` in `ARVocabularyOverlayPresenter.ApplyModernLayout()`.

Use multiples of 8 where possible: `8, 16, 24, 32, 40, 48, 64`.

### Font size and weight

Text sizes are passed directly to `CreateText()`:

```csharp
var greeting = CreateText(parent, "Greeting", "Hi, Explorer!", 48f, font, LearningUiTheme.Ink);
greeting.fontStyle = FontStyles.Bold;
```

Change the numeric value (`48f`) for the font size. Use `FontStyles.Bold`, `FontStyles.Italic`, or `FontStyles.Normal` for emphasis.

The primary Latin font is Google Sans. Hindi and Malayalam retain Noto Sans source fonts and TMP
fallbacks. At runtime, `NativeIndicText` detects those two Unicode ranges and switches the affected label
to a UniText child graphic backed by HarfBuzz and FreeType. That child uses the same `RectTransform` tree,
so buttons, scrolling, masks, fading, scaling, and world-space placement work exactly as they do for Latin
labels. Font files live under:

`Assets/_Project/LanguageLearning/Content/Resources/Fonts`

To replace the primary font, update both:

- Font paths in `LanguageLearningSceneSetup.cs`
- Resource paths in `ARVocabularyOverlayPresenter.MultilingualFontProvider`
- Native shaping resource paths in `NativeIndicText`

Keep script-specific fallback fonts unless the replacement font supports all required Devanagari and Malayalam glyphs.

The UniText dependency is pinned in `Packages/manifest.json`. Do not replace the in-hierarchy graphic
with a `UIDocument` or coordinate-mirrored overlay; Unity's Advanced Text Generator is UI Toolkit-only,
and a separate visual tree will not naturally inherit UGUI layout or `ScrollRect` clipping.

### Colors, corners, and shadows

Edit shared design tokens in:

`Assets/_Project/LanguageLearning/Unity/Presentation/LearningUiTheme.cs`

- Colors: `Primary`, `Secondary`, `Accent`, `Background`, `Ink`, `Muted`, and state colors
- Corner radii: `RadiusSmall`, `RadiusMedium`, `RadiusLarge`, and `RadiusPill`
- Shadow color: `Shadow`
- Button states: `StyleButton()`
- Rounded cards: `StyleSurface()`

Prefer changing these tokens instead of repeating new colors or radii inside individual screens.

### Heights and widths

- Card height: the final height argument in `CreatePanel(..., height)`
- Button height: the height argument in `CreateButton()` or `CreateIconButton()`
- Text/layout height: `SetPreferredHeight()` or a `LayoutElement`
- Responsive stat cells: `LearningResponsiveGrid.Configure(columns, spacing, cellHeight)`
- Anchored overlays: `RectTransform.anchoredPosition` and `sizeDelta`

Do not use `childForceExpandHeight = true` for cards or buttons unless they are intentionally meant to fill remaining space.

### Responsive scaling

Canvas scaling is configured in `LearningMvpController.ConfigureCanvas()`.

```csharp
scaler.referenceResolution = new Vector2(1080f, 1440f);
scaler.matchWidthOrHeight = 1f;
```

`1f` means the scale follows screen height, which is appropriate for this portrait-only app. Screen content is placed under the safe-area root created by `CreateSafeAreaRoot()`.

When changing layout, test at least:

- `1080 x 1920` (16:9)
- `1080 x 2160` (18:9)
- `1080 x 2400` (20:9)
- A device profile with a notch or camera cutout

## Change Workflow

1. Edit the relevant screen-building method or shared theme component.
2. Stop and restart Play mode so the runtime hierarchy is rebuilt.
3. Check the screen at several portrait Game View resolutions.
4. Run `AI-AR Language Learning > Setup Main Scene` when changing scene wiring or generated TMP font assets.
5. Keep reusable visual changes in `LearningUiTheme` or shared creation helpers rather than duplicating them per screen.

See also:

- `PremiumARLanguageLearning-DesignSystem.md` for visual tokens and screen behavior
- `../Architecture.md` for application and AR architecture
