# Premium AR Language Learning UI Design System

## Design intent

A friendly, playful, mobile-first learning interface inspired by modern language-learning products without reproducing any one product. The camera remains visually dominant in Scan mode, while Home, Quiz, and Progress use a soft off-white canvas with elevated white cards.

## High-fidelity mockups

- Source board: `PremiumARLanguageLearning-Mockups.html`
- Rendered board: `PremiumARLanguageLearning-Mockups.png`
- Reference viewport: 390 × 844 logical pixels per phone
- Unity reference resolution: 1080 × 1920, match width/height 0.55

## Color palette

| Token | Hex | Unity use |
|---|---:|---|
| Background | `#F7F9F4` | Home, Quiz, Progress canvas |
| Card | `#FFFFFF` | Elevated surfaces and action dock |
| Primary | `#4FCB72` | Main actions, progress, selected state |
| Primary Dark | `#2FAE58` | Pressed state and green text |
| Primary Soft | `#E5F8EA` | Icon wells, tabs, supportive cards |
| Secondary | `#4DB7F5` | Quiz and translation accents |
| Secondary Soft | `#E5F5FE` | Secondary icon wells |
| Accent | `#FFC857` | XP, achievements, streak highlights |
| Accent Soft | `#FFF4D5` | Achievement and XP cards |
| Lavender | `#8C7CF0` | Progress and pronunciation accents |
| Lavender Soft | `#EFEDFF` | Lavender icon wells |
| Ink | `#24344D` | Primary text and icons |
| Muted | `#718096` | Body copy, captions, metadata |
| Line | `#E5EADF` | Progress tracks and separators |
| Success | `#38B96A` | Correct answer feedback |
| Warning | `#F5A044` | Streak and attention states |
| Error | `#EE6B73` | Incorrect answer feedback |

Avoid dark full-screen surfaces. Dark ink is reserved for text, line icons, and the translucent Scan status pill.

## Typography scale

Primary runtime family is the included Noto Sans TMP stack with Devanagari and Malayalam fallbacks.

| Role | Unity reference size | Weight | Usage |
|---|---:|---|---|
| Display | 48 | Bold | Home greeting |
| Screen title | 38–40 | Bold | Quiz and Progress headers |
| Card title | 31–36 | Bold | Feature cards and stats |
| Question / translation | 40–54 | Bold | Quiz prompt and detected translation |
| Subheading | 27–29 | Bold/Regular | Feature titles and stat values |
| Body | 22–23 | Regular | Descriptions and feedback |
| Caption | 17–21 | Bold/Regular | Metadata, labels, tabs |

- Default text color: Ink
- Secondary text color: Muted
- Maximum body line length: approximately 32–38 characters on a 390 px phone
- Use auto-sizing only for translated words and question prompts

## Spacing and geometry

The UI uses an 8 pt spacing system.

- Base unit: 8
- Common gaps: 8, 16, 24, 32, 40, 48
- Screen horizontal padding: 40–48 Unity reference pixels
- Card internal padding: 22–34
- Home feature card height: 184
- Quiz option height: 92
- Bottom action dock height: 156
- Minimum touch target: 48 logical px / approximately 82 Unity reference px
- Small radius: 16
- Standard radius: 20
- Large card radius: 24
- Pill/circle radius: 32
- Elevated shadow: Ink at 14% opacity, 5–8 px vertical offset

## Shared components

### Buttons

- Primary button: green fill, white bold label, 24 px radius
- Secondary button: blue fill, white label
- Surface button: white fill, ink label, optional soft shadow
- Soft button: pale green fill, green label
- Icon button: circular or pill surface with a 48% size line icon
- Action dock button: icon well + compact caption, equal-width layout

Every button includes a press-scale response to 96% and an 80 ms color transition.

### Cards

- Information card: translucent white, 24 px radius, soft shadow
- Feature card: white, leading colored icon well, title/subtitle, trailing chevron
- Stat card: white, centered icon/value/caption
- Achievement card: yellow-soft background with trophy badge
- Level card: primary green surface, white progress bar and metadata

### Icons

The implementation uses lightweight procedural UGUI line icons to avoid texture atlases and inconsistent emoji rendering.

Recommended production replacement, if a licensed icon package is added later:

- Lucide Rounded
- Material Symbols Rounded
- Phosphor Icons Regular/Rounded

Keep one family, 2–2.5 px optical stroke, rounded line caps, and 24/32/48 px masters. Avoid mixing filled and outline icons except achievement badges.

## Screen specifications

### Home

Hierarchy:

- Safe Area
  - App Background
  - Main Menu
    - Home Content
      - Home Header
      - Today Card
      - Scan Button
      - Quiz Button
      - Progress Button
      - Encouragement caption

Behavior:

- Refresh streak, today XP, and level whenever Home appears
- Slide/fade the screen upward over 280 ms
- Feature cards scale to 96% while pressed

### Scan

Hierarchy:

- Full canvas: Recognition Bounding Box layer
- Safe Area
  - Vocabulary Panel
    - Language caption
    - Object name
    - Translation
    - Phonetic hint
    - Confidence
    - Card Speak Button
  - Scan Controls
    - Scan Actions
      - Back Button
      - Scan Language Selector
      - Scan Action Bar
        - Capture Button
        - Translate Button
        - Speak Button
        - Save Button
        - Settings Button
  - Learning Status

Behavior:

- Camera background remains unobstructed except for the top information card and bottom dock
- Information card fades and slides down over 260 ms on detection
- Confidence updates from the strongest matched recognition
- Language selector expands above the bottom dock
- Speak remains disabled until a catalog object is detected

### Quiz

Hierarchy:

- Quiz Panel
  - Quiz Header
  - Quiz Progress
  - Quiz Language Selector
  - Question Card
  - Quiz Option 1–4 Buttons
  - Quiz Feedback
  - Next Question Button
  - XP Burst overlay

Behavior:

- Correct: success green + 8% bounce
- Incorrect: soft red + horizontal shake
- XP burst rises and fades over 800 ms
- Progress bars animate over 340 ms
- Next remains disabled until the learner answers

### Progress

Hierarchy:

- Progress Panel / ScrollRect
  - Progress Content
    - Progress Header
    - Level Card
    - Progress Stats grid
    - Weekly Activity Card
    - Achievement Card
    - Practice Card

Metrics:

- Level and XP progress
- Words learned
- Objects scanned
- Quiz accuracy
- Current streak
- Learning time
- Seven-day activity
- Recent achievement
- Adaptive practice recommendations

## Motion guidelines

| Motion | Duration | Easing |
|---|---:|---|
| Screen enter | 280 ms | SmoothStep |
| Detection card | 260 ms | Fade + 24 px slide |
| Button press | Responsive | Exponential scale interpolation |
| Progress fill | 340 ms | SmoothStep |
| Correct bounce | 340 ms | Sine bounce |
| Wrong shake | 340 ms | Damped 8-cycle shake |
| XP burst | 800 ms | Rise, pop, fade |

Respect motion sensitivity in a future accessibility pass by adding a global reduced-motion toggle.

## Unity implementation notes

- UGUI runtime construction remains deterministic and test-friendly
- `LearningSafeAreaFitter` responds to notch and Dynamic Island safe-area changes
- `CanvasScaler` uses 1080 × 1920 with match value 0.55
- Rounded surfaces use one cached, generated 9-sliced sprite per radius
- Procedural icons avoid additional texture memory and draw calls from mixed atlases
- AR camera feed has no full-screen translucent overlay in Scan mode
- Shadows are limited to major cards and docks to control overdraw
- Progress content uses a masked vertical ScrollRect for smaller phones
- Existing test-visible object and button names are preserved

## Runtime files

- `LearningUiTheme.cs`: palette, radii, rounded sprites, button styling
- `LearningUiIcon.cs`: reusable procedural icon graphic
- `LearningUiMotion.cs`: press feedback and safe-area fitter
- `LearningActivityTracker.cs`: daily XP, streak, and seven-day activity
- `LearningMvpController.cs`: Home, Scan, Quiz, Progress composition and motion
- `ARVocabularyOverlayPresenter.cs`: detected-object card and pronunciation action
