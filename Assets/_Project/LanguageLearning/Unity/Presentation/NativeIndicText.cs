using System;
using LightSide;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NativeHorizontalAlignment = LightSide.HorizontalAlignment;
using NativeVerticalAlignment = LightSide.VerticalAlignment;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    /// <summary>
    /// Adds HarfBuzz-backed Indic rendering to a TMP label without moving it out of UGUI.
    /// The native graphic is a child of the original RectTransform, so layout, scrolling,
    /// masks, CanvasGroups, animation, and world-space canvases are inherited normally.
    /// </summary>
    public static class NativeIndicText
    {
        public static Type NativeGraphicType => typeof(UniText);

        public static bool RequiresShaping(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (var character in value)
            {
                if ((character >= '\u0900' && character <= '\u097F') ||
                    (character >= '\u1CD0' && character <= '\u1CFF') ||
                    (character >= '\uA8E0' && character <= '\uA8FF') ||
                    (character >= '\u0D00' && character <= '\u0D7F'))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool UsesFaceOnlyAppearance()
        {
            var fontStack = Resources.Load<UniTextFontStack>(
                NativeIndicTextAdapter.FontStackResourcePath);
            var appearance = Resources.Load<UniTextAppearance>(
                NativeIndicTextAdapter.AppearanceResourcePath);
            if (fontStack == null || fontStack.fonts.Count == 0 || appearance == null)
                return false;

            var materials = appearance.GetMaterials(fontStack.fonts[0]);
            return materials is { Length: 1 } &&
                   materials[0] != null &&
                   !materials[0].IsKeywordEnabled("OUTLINE_ON") &&
                   !materials[0].IsKeywordEnabled("UNDERLAY_ON");
        }

        public static void Track(TMP_Text text)
        {
            if (!UnityEngine.Application.isPlaying || text == null)
                return;

            var adapter = text.GetComponent<NativeIndicTextAdapter>() ??
                          text.gameObject.AddComponent<NativeIndicTextAdapter>();
            adapter.Configure(text);
        }

        /// <summary>
        /// Applies the correct renderer immediately after a label's value or language changes.
        /// This prevents the TMP fallback from being visible for a frame before LateUpdate.
        /// </summary>
        public static void Refresh(TMP_Text text)
        {
            Track(text);
        }

        /// <summary>
        /// Replaces a label's content and immediately selects the correct renderer. Keeping this
        /// operation in one place prevents a previous TMP or UniText mesh from surviving a
        /// language change while the new shaped mesh is being prepared.
        /// </summary>
        public static void SetText(TMP_Text text, string value)
        {
            if (text == null)
                return;

            text.SetText(value ?? string.Empty);
            Refresh(text);
        }
    }

    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class NativeIndicTextAdapter : MonoBehaviour
    {
        internal const string FontStackResourcePath = "Fonts/UniText/IndicFontStack";
        internal const string AppearanceResourcePath = "Fonts/UniText/IndicAppearance";
        const string NativeChildName = "Native Indic Text";

        static UniTextFontStack s_FontStack;
        static UniTextAppearance s_Appearance;
        static bool s_ResourcesLoaded;
        static bool s_MissingResourcesLogged;

        TMP_Text m_Source;
        UniText m_NativeText;
        ModRegister m_BoldRegister;
        ModRegister m_ItalicRegister;
        bool m_SourceSuppressed;
        bool m_SourceWasEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_FontStack = null;
            s_Appearance = null;
            s_ResourcesLoaded = false;
            s_MissingResourcesLogged = false;
        }

        public void Configure(TMP_Text source)
        {
            if (source != null)
                m_Source = source;
            Refresh();
        }

        void Awake()
        {
            m_Source ??= GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            m_Source ??= GetComponent<TMP_Text>();
            Refresh();
        }

        void LateUpdate()
        {
            Refresh();
        }

        void OnDisable()
        {
            HideNativeText();
        }

        void OnDestroy()
        {
            RestoreSource();
            if (m_NativeText == null)
                return;

            if (UnityEngine.Application.isPlaying)
                Destroy(m_NativeText.gameObject);
            else
                DestroyImmediate(m_NativeText.gameObject);
        }

        void Refresh()
        {
            if (m_Source == null)
            {
                HideNativeText();
                return;
            }

            // UniText initializes its font provider from OnEnable. Configuring Appearance while
            // this label is under an inactive button would access that provider too early and
            // abort the caller's option-population loop. The next OnEnable performs the refresh.
            if (!isActiveAndEnabled)
                return;

            if (!NativeIndicText.RequiresShaping(m_Source.text))
            {
                HideNativeText();
                return;
            }

            if (!EnsureNativeText())
            {
                RestoreSource();
                return;
            }

            SyncNativeText();
            if (!m_NativeText.gameObject.activeSelf)
                m_NativeText.gameObject.SetActive(true);

            if (!m_SourceSuppressed)
            {
                m_SourceWasEnabled = m_Source.enabled;
                m_SourceSuppressed = true;
            }

            // Callers such as Scan may enable TMP while assigning a new font. Enforce exclusive
            // ownership every refresh so the old, unshaped glyphs can never show underneath.
            if (m_Source.enabled)
                m_Source.enabled = false;
            m_Source.canvasRenderer.Clear();
        }

        bool EnsureNativeText()
        {
            EnsureResourcesLoaded();
            if (s_FontStack == null || s_Appearance == null)
            {
                if (!s_MissingResourcesLogged)
                {
                    Debug.LogError(
                        "Native Indic text assets are missing. Run AI-AR Language Learning > Setup Main Scene " +
                        "or reimport the project to regenerate them.");
                    s_MissingResourcesLogged = true;
                }

                return false;
            }

            if (m_NativeText != null)
                return true;

            var existing = transform.Find(NativeChildName);
            if (existing != null)
                m_NativeText = existing.GetComponent<UniText>();

            if (m_NativeText == null)
            {
                var nativeObject = new GameObject(
                    NativeChildName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(CanvasGroup),
                    typeof(UniText));
                nativeObject.layer = gameObject.layer;
                nativeObject.transform.SetParent(transform, false);
                m_NativeText = nativeObject.GetComponent<UniText>();
            }

            var nativeRect = m_NativeText.rectTransform;
            nativeRect.anchorMin = Vector2.zero;
            nativeRect.anchorMax = Vector2.one;
            nativeRect.pivot = m_Source.rectTransform.pivot;
            nativeRect.offsetMin = Vector2.zero;
            nativeRect.offsetMax = Vector2.zero;
            nativeRect.localScale = Vector3.one;
            nativeRect.localRotation = Quaternion.identity;

            ConfigureNativeAssets();
            m_NativeText.raycastTarget = false;
            m_NativeText.maskable = true;
            DisableNativeRaycasts();

            return true;
        }

        void ConfigureNativeAssets()
        {
            if (m_NativeText.FontStack != s_FontStack)
                m_NativeText.FontStack = s_FontStack;

            if (m_NativeText.Appearance == s_Appearance)
                return;

            try
            {
                m_NativeText.Appearance = s_Appearance;
            }
            catch (NullReferenceException) when (m_NativeText.Appearance == s_Appearance)
            {
                // UniText 1.0.0 assigns its serialized appearance, then immediately updates a
                // font provider that is not created until the first canvas rebuild. The asset is
                // already configured correctly; suppress only that known first-use exception.
            }
        }

        void DisableNativeRaycasts()
        {
            var canvasGroup = m_NativeText.GetComponent<CanvasGroup>() ??
                              m_NativeText.gameObject.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            // UniText may create supporting graphics as its layout changes. None of them should
            // participate in hit testing; the Button/Image behind the label owns interaction.
            foreach (var graphic in m_NativeText.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        void SyncNativeText()
        {
            if (!string.Equals(m_NativeText.Text, m_Source.text, StringComparison.Ordinal))
                ClearNativeRenderers();

            m_NativeText.Text = m_Source.text;
            m_NativeText.FontSize = m_Source.fontSize;
            m_NativeText.color = m_Source.color;
            m_NativeText.WordWrap = m_Source.textWrappingMode != TextWrappingModes.NoWrap;
            m_NativeText.AutoSize = m_Source.enableAutoSizing;
            m_NativeText.MinFontSize = m_Source.fontSizeMin;
            m_NativeText.MaxFontSize = m_Source.fontSizeMax;
            m_NativeText.HorizontalAlignment = ResolveHorizontalAlignment(m_Source);
            m_NativeText.VerticalAlignment = ResolveVerticalAlignment(m_Source);
            SyncFontStyle();
            DisableNativeRaycasts();
        }

        void ClearNativeRenderers()
        {
            m_NativeText.canvasRenderer.Clear();
            foreach (var renderer in m_NativeText.CanvasRenderers)
                renderer?.Clear();
        }

        void SyncFontStyle()
        {
            var style = m_Source.fontStyle;
            var usesBoldWeight = (int)m_Source.fontWeight >= (int)FontWeight.SemiBold;
            SyncModifier(
                (style & FontStyles.Bold) != 0 || usesBoldWeight,
                ref m_BoldRegister,
                static () => new BoldModifier());
            SyncModifier(
                (style & FontStyles.Italic) != 0,
                ref m_ItalicRegister,
                static () => new ItalicModifier());
        }

        void SyncModifier(
            bool enabled,
            ref ModRegister registration,
            Func<BaseModifier> createModifier)
        {
            if (enabled && registration == null)
            {
                var rangeRule = new RangeRule();
                rangeRule.data.Add(new RangeRule.Data { range = ".." });
                registration = new ModRegister
                {
                    Rule = rangeRule,
                    Modifier = createModifier()
                };
                m_NativeText.RegisterModifier(registration);
                return;
            }

            if (!enabled && registration != null)
            {
                m_NativeText.UnregisterModifier(registration);
                registration = null;
            }
        }

        void HideNativeText()
        {
            if (m_NativeText != null && m_NativeText.gameObject.activeSelf)
                m_NativeText.gameObject.SetActive(false);
            RestoreSource();
        }

        void RestoreSource()
        {
            if (!m_SourceSuppressed)
                return;

            if (m_Source != null)
            {
                m_Source.enabled = m_SourceWasEnabled;
                if (m_Source.enabled && m_Source.gameObject.activeInHierarchy)
                {
                    m_Source.SetAllDirty();
                    m_Source.ForceMeshUpdate(true, true);
                }
            }
            m_SourceSuppressed = false;
        }

        static void EnsureResourcesLoaded()
        {
            if (s_ResourcesLoaded)
                return;

            s_ResourcesLoaded = true;
            s_FontStack = Resources.Load<UniTextFontStack>(FontStackResourcePath);
            s_Appearance = Resources.Load<UniTextAppearance>(AppearanceResourcePath);
        }

        static NativeHorizontalAlignment ResolveHorizontalAlignment(TMP_Text source)
        {
            var horizontal = source.horizontalAlignment;
            if ((horizontal & HorizontalAlignmentOptions.Right) != 0)
                return NativeHorizontalAlignment.Right;
            if ((horizontal & HorizontalAlignmentOptions.Center) != 0 ||
                (horizontal & HorizontalAlignmentOptions.Geometry) != 0)
            {
                return NativeHorizontalAlignment.Center;
            }

            return NativeHorizontalAlignment.Left;
        }

        static NativeVerticalAlignment ResolveVerticalAlignment(TMP_Text source)
        {
            var vertical = source.verticalAlignment;
            if ((vertical & VerticalAlignmentOptions.Bottom) != 0 ||
                (vertical & VerticalAlignmentOptions.Baseline) != 0 ||
                (vertical & VerticalAlignmentOptions.Capline) != 0)
            {
                return NativeVerticalAlignment.Bottom;
            }

            if ((vertical & VerticalAlignmentOptions.Middle) != 0 ||
                (vertical & VerticalAlignmentOptions.Geometry) != 0)
            {
                return NativeVerticalAlignment.Middle;
            }

            return NativeVerticalAlignment.Top;
        }
    }
}
