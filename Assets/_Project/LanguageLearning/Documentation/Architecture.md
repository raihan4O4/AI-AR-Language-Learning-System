# AI-AR Language Learning MVP Architecture

This Unity project is organized as a small production-style architecture for the project synopsis:
AI-powered AR vocabulary learning with object recognition, multilingual translations, TTS, progress tracking, quizzes, retention analysis, and adaptive revision.

## MVP Goal

The first working MVP should prove the full learning loop with a controlled vocabulary:

1. Recognize or simulate a common object label.
2. Match that label to the local vocabulary catalog.
3. Display the selected language word in AR.
4. Play pronunciation through TTS.
5. Record views, pronunciation attempts, and quiz outcomes.
6. Recommend weak or unseen words for revision.

The vocabulary catalog covers all 80 COCO detector classes across English, Hindi, Malayalam, and
French, while retaining the original supplemental learning words for backward compatibility.

## Folder Layout

`Assets/_Project/LanguageLearning/Domain`
: Pure C# entities and value objects. No Unity dependency.

`Assets/_Project/LanguageLearning/Application`
: Use cases, ports, quiz generation, object label matching, progress recording, retention summaries, and adaptive revision.

`Assets/_Project/LanguageLearning/Unity`
: Unity adapters for Resources JSON loading, PlayerPrefs persistence, asynchronous Android TTS, Unity Inference Engine recognition, bootstrap, AR placement, and the learner-facing UI.

`Assets/_Project/LanguageLearning/ObjectDetection`
: Reproducibly exported YOLO11n ONNX, exact preprocessing/postprocessing metadata, COCO labels, and the golden validation image.

`Assets/_Project/LanguageLearning/Content/Resources/Vocabulary/vocabulary_catalog.json`
: Runtime word catalog. Every COCO label has a distinct canonical entry and four translations; avoid
hardcoding vocabulary in scenes.

`Assets/_Project/LanguageLearning/Tests/EditMode`
: EditMode tests for matching, quiz generation, progress, and adaptive recommendations.

## Assemblies

`AIAR.LanguageLearning.Domain`
: Portable learning model. Can later move into a package or backend shared library.

`AIAR.LanguageLearning.Application`
: Testable business logic. Depends only on Domain.

`AIAR.LanguageLearning.Unity`
: Unity-specific implementation. Depends on Domain, Application, and TextMeshPro.

This keeps AR Foundation, Android, PlayerPrefs, and UI details outside the learning logic.

## Runtime Flow

```mermaid
flowchart LR
    Camera["AR camera background"] --> Visible["GPU copy of the visible<br/>orientation and crop"]
    Visible --> Letterbox["Bilinear resize + 114 letterbox<br/>RGB float NCHW 320×320"]
    Letterbox --> Sentis["Inference Engine YOLO11n worker<br/>layer-budgeted scheduling"]
    Sentis --> Readback["Async GPU readback<br/>1×84×2100"]
    Readback --> Decode["Verified channel-first decode<br/>class-aware NMS"]
    Decode --> Recognition["IObjectRecognitionService"]
    Recognition --> Stabilizer["Temporal stabilization<br/>box smoothing + hold"]
    Stabilizer --> Session["LearningSessionController"]
    Session --> Learning["VocabularyLearningService"]
    Learning --> Catalog["IVocabularyRepository"]
    Learning --> Progress["ILearningProgressRepository"]
    Session --> Overlay["Screen-space label + boxes"]
    Session --> World["ARRaycast world label"]
    Session --> Speech["ITextToSpeechService"]
    Progress --> Adaptive["AdaptiveRevisionService"]
    Catalog --> Quiz["QuizService"]
    Quiz --> Shell["Home / Scan / Quiz / Progress UI"]
    Adaptive --> Shell
```

## Scene Wiring

`Assets/Scenes/Main.unity` contains the AR Session, XR Origin, camera manager, runtime composition root,
YOLO11n detector, vocabulary repository, TTS adapter, learning session, and pooled vocabulary overlay.
`LearningSessionController` also repairs/creates `LearningMvpController` and
`ARWorldVocabularyPresenter` at startup so stale scene serialization cannot remove the learner flow.

The setup command `AI-AR Language Learning > Setup Main Scene` rebuilds the wiring deterministically.
It requires the exact `yolo11n_320.onnx` asset and fails loudly if the verified model or its 80 COCO
labels are absent. The aligned `coco80.en.txt`, `coco80.hi.txt`, `coco80.ml.txt`, and `coco80.fr.txt`
assets provide a reviewable localization list in detector class order. There is no mock or silent
detector fallback in the production scene.

The Home screen is deliberately non-AR. Selecting Scan requests Android camera permission, verifies
AR Foundation availability, enables the AR session/camera/background, waits for initialization or
tracking, and only then starts recognition. Back, Quiz, and Progress stop recognition and disable
the camera path. This avoids launching XR subsystems before the learner requests a camera feature.

## Runtime Stability Rules

- Detection never reads the full camera image back to the CPU. AR Foundation's background material
  renders a reusable, display-oriented GPU frame, keeping preview rendering independent from inference.
- Only one preprocessing, inference, or readback operation may be in flight; newer camera frames are intentionally dropped.
- The YOLO graph is scheduled incrementally across rendered frames so inference does not monopolize one frame.
- Output is read back asynchronously; no blocking GPU download occurs in the camera callback.
- Scan selects a camera configuration near 1280x720 at 30 FPS when the device exposes one.
- The model consumes the exact visible camera crop. Reversing the deterministic letterbox therefore
  produces normalized screen coordinates without a fragile device-orientation approximation.
- The first valid prediction is shown immediately; brief detector gaps are held for 2 seconds and box/confidence values are smoothed.
- Bounding-box GameObjects are pooled and reused instead of instantiated for every inference result.
- A vocabulary item is recorded once per Scan session, preventing repeated detector frames from inflating learning progress.
- Detector state is surfaced as initializing, running, awaiting camera, degraded, faulted, or stopped.
- Verbose per-frame recognition logging is disabled in production builds.
- Android TTS queues the first utterance until the platform engine has initialized and verifies locale support.

## Scaling Path

Object recognition:
`RealtimeCocoObjectDetector` implements `IObjectRecognitionService`. Future model replacements must
provide a new explicit contract and decoder; output layouts are never guessed at runtime. Keep model
labels in English and use `recognitionLabels` in the JSON catalog for aliases.

Vocabulary:
Continue using the JSON schema for the MVP. When the word list becomes large, replace `ResourcesVocabularyRepository` with Addressables, SQLite, or a remote synced repository while keeping the same `IVocabularyRepository` port.

Progress:
`PlayerPrefsProgressRepository` is acceptable for MVP. For multi-user, sync, or analytics dashboards, replace it with SQLite or a cloud-backed repository through `ILearningProgressRepository`.

TTS:
`AndroidTextToSpeechService` is the mobile adapter. For pronunciation scoring later, add a separate speech-recognition port rather than mixing assessment into TTS.

Adaptive learning:
`AdaptiveRevisionService` separates discovery from revision. The dashboard queue contains only learned
words that are unassessed, below the retention threshold, or overdue. Quiz generation uses the same
risk signal as a sampling weight, so weak learned words recur more often without allowing adjacent
duplicate questions. Unseen catalog words remain eligible at a lower baseline weight.

Retention analysis:
`RetentionAnalysisService` converts persisted word evidence into total and explored words, mastered
words, words needing revision, vocabulary mastery, average retention, and quiz accuracy.

### Analytics definitions and integrity

The MVP deliberately uses explainable metrics rather than generated or estimated dashboard values:

| Metric | Definition | Persisted evidence |
|---|---|---|
| Vocabulary mastery | Mastered learned words / all learned words. A word is mastered at a retention score of at least 75%. | Per-word quiz and recall history |
| Quiz accuracy | Correct answers / all quiz answers. Empty history displays 0%, not a fabricated baseline. | Correct and incorrect answer counts |
| Retention score | 50% confidence-weighted quiz accuracy + 40% spaced successful recalls + 10% recall recency. Attempt confidence reaches full weight after three answers; spaced recall credit requires 20 hours between successful recalls; recency decreases over 30 days. | Attempts, correct answers, spaced-recall count, last-correct time |
| Needs revision | A learned word has no quiz evidence, retention below 60%, no successful recall, or its last successful recall is at least seven days old. | Per-word retention evidence |
| Recent/weekly activity | Exact scans, quiz answers, completed quizzes, pronunciation plays, and focused learning seconds aggregated by local calendar day. Daily bars show scans + completed quizzes on a proportional scale. | Typed daily activity records |
| Learning streak | Consecutive days containing at least one scan, quiz answer, or pronunciation action. An unfinished current day does not break yesterday's streak. | Typed daily activity records |
| Focused learning time | Time while the learner is on Scan or Quiz only; Home and Progress browsing are excluded. | Periodic focused-time checkpoints |

Object scans are recorded only after the detector produces a catalog match, and each vocabulary item can
contribute at most one scan per Scan session. Quiz completions are recorded only after the final answer.
Pronunciation playback is counted once at the service boundary. These rules prevent rendering frames,
repeated UI callbacks, and partially opened quizzes from inflating the analytics. Legacy generic activity
remains valid for streak continuity but is excluded from typed scan/quiz totals and daily chart bars.

Progress schema v2 stores recall streaks and timestamps while reading schema v1 saves. Because the old
schema did not retain answer timing, migration grants at most one historical recall session rather than
inventing spaced repetitions. Focused-time tracking starts on its own v2 key because the earlier timer
included Home and Progress screen time and therefore cannot be migrated without misrepresenting it.

These analytics demonstrate the observed AR-to-quiz learning loop, but they do not by themselves prove
that AR caused a learning improvement. A causal effectiveness claim would require a pre-test/post-test
or non-AR comparison design. Pronunciation playback is engagement evidence only; it is not pronunciation
accuracy because the MVP has no speech-assessment model.

## Font And Shaping Note

The UI remains one UGUI hierarchy. Google Sans and the script-specific Noto fonts are bundled under
`Content/Resources/Fonts`, with persisted TMP fallbacks under its `TMP` subfolder. Glyph coverage alone
is not sufficient for Indic scripts: Devanagari and Malayalam require contextual substitutions and mark
reordering. `NativeIndicText` therefore switches labels containing those scripts to the pinned UniText
UGUI `MaskableGraphic`, which shapes with HarfBuzz and FreeType. The native graphic is a stretched child
of the original TMP `RectTransform`, so it inherits the same button, layout group, `ScrollRect`, masks,
`CanvasGroup`, animation, and world-space canvas. It is not a screen-coordinate overlay. Latin and French
labels stay on the original TMP rendering path.

## MVP Acceptance Checklist

- Catalog loads all MVP words without scene-specific code.
- The official YOLO11n COCO export imports with exact input `[1,3,320,320]` and output `[1,84,2100]`.
- The detector uses GPU display-oriented preprocessing, layer-budgeted inference, non-blocking readback, explicit decoding, class-aware NMS, and temporal stabilization.
- AR overlay shows translated word, English source word, phonetic hint, and language name.
- Entering Scan clears the transient object card and boxes without resetting progress or language.
- A world-space label is placed through AR raycasting when a plane/feature point is available.
- Scan always exposes English, Hindi, Malayalam, and French selectors plus Back and Speak controls.
- Speak calls TTS for the current detected word in the selected language and records an attempt.
- The learner can return Home from Scan, Quiz, and Progress.
- Quiz exposes all four languages, advances only after an answer, and does not repeat adjacent prompts.
- Quiz answers are persisted and immediately update mastery/accuracy.
- Progress shows mastery, accuracy, retention, revision need, real weekly activity, focused time, and an adaptive revision queue.
- Weak learned words are sampled more often in later quizzes, with no adjacent duplicate prompts.
- EditMode tests cover business rules, scene wiring, letterbox inversion, channel-first decoding, NMS,
  catalog completeness, multilingual font atlases, the imported model contract, and a CPU inference smoke test.
