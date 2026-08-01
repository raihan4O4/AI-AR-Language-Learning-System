using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    public enum LearningButtonStyle
    {
        Primary,
        Secondary,
        Surface,
        Soft,
        Success,
        Error
    }

    public static class LearningUiTheme
    {
        const int SpriteTextureSize = 64;

        static readonly Dictionary<int, Sprite> s_RoundedSprites = new Dictionary<int, Sprite>();

        public static readonly Color Background = Hex("F7F9F4");
        public static readonly Color Card = Color.white;
        public static readonly Color Primary = Hex("4FCB72");
        public static readonly Color PrimaryDark = Hex("2FAE58");
        public static readonly Color PrimarySoft = Hex("E5F8EA");
        public static readonly Color Secondary = Hex("4DB7F5");
        public static readonly Color SecondaryDark = Hex("278FCE");
        public static readonly Color SecondarySoft = Hex("E5F5FE");
        public static readonly Color Accent = Hex("FFC857");
        public static readonly Color AccentSoft = Hex("FFF4D5");
        public static readonly Color Ink = Hex("24344D");
        public static readonly Color Muted = Hex("718096");
        public static readonly Color Line = Hex("E5EADF");
        public static readonly Color Success = Hex("38B96A");
        public static readonly Color Warning = Hex("F5A044");
        public static readonly Color Error = Hex("EE6B73");
        public static readonly Color ErrorSoft = Hex("FDE9EB");
        public static readonly Color Lavender = Hex("8C7CF0");
        public static readonly Color LavenderSoft = Hex("EFEDFF");
        public static readonly Color Glass = new Color(1f, 1f, 1f, 0.93f);
        public static readonly Color Shadow = new Color(0.11f, 0.2f, 0.3f, 0.14f);

        public const float RadiusSmall = 16f;
        public const float RadiusMedium = 20f;
        public const float RadiusLarge = 24f;
        public const float RadiusPill = 32f;

        public static void StyleSurface(
            Image image,
            Color color,
            float radius = RadiusLarge,
            bool addShadow = false,
            Vector2? shadowDistance = null)
        {
            if (image == null)
                return;

            image.sprite = GetRoundedSprite(radius);
            image.type = Image.Type.Sliced;
            image.color = color;

            if (!addShadow)
                return;

            var shadow = image.GetComponent<Shadow>() ?? image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Shadow;
            shadow.effectDistance = shadowDistance ?? new Vector2(0f, -8f);
            shadow.useGraphicAlpha = true;
        }

        public static void StyleButton(Button button, LearningButtonStyle style, float radius = RadiusMedium)
        {
            if (button == null)
                return;

            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            var normal = Card;
            var pressed = Hex("EDF1EA");
            var disabled = Hex("E7EBE4");

            switch (style)
            {
                case LearningButtonStyle.Primary:
                    normal = Primary;
                    pressed = PrimaryDark;
                    break;
                case LearningButtonStyle.Secondary:
                    normal = Secondary;
                    pressed = SecondaryDark;
                    break;
                case LearningButtonStyle.Soft:
                    normal = PrimarySoft;
                    pressed = Hex("D4F1DC");
                    break;
                case LearningButtonStyle.Success:
                    normal = Success;
                    pressed = Hex("279955");
                    break;
                case LearningButtonStyle.Error:
                    normal = Error;
                    pressed = Hex("D9535D");
                    break;
            }

            StyleSurface(image, normal, radius);
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = image;
            button.colors = new ColorBlock
            {
                normalColor = normal,
                highlightedColor = Color.Lerp(normal, Color.white, 0.08f),
                pressedColor = pressed,
                selectedColor = normal,
                disabledColor = disabled,
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            if (button.GetComponent<LearningUiPressFeedback>() == null)
                button.gameObject.AddComponent<LearningUiPressFeedback>();
        }

        public static void SetButtonColor(Button button, Color color, Color pressedColor)
        {
            if (button == null)
                return;

            var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
            if (image != null)
                image.color = color;

            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.08f);
            colors.pressedColor = pressedColor;
            colors.selectedColor = color;
            button.colors = colors;
        }

        public static Sprite GetRoundedSprite(float radius)
        {
            var radiusKey = Mathf.Clamp(Mathf.RoundToInt(radius), 4, SpriteTextureSize / 2);
            if (s_RoundedSprites.TryGetValue(radiusKey, out var cachedSprite) && cachedSprite != null)
                return cachedSprite;

            var texture = new Texture2D(SpriteTextureSize, SpriteTextureSize, TextureFormat.RGBA32, false)
            {
                name = $"UI Rounded {radiusKey}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[SpriteTextureSize * SpriteTextureSize];
            var radiusValue = radiusKey;
            var minimum = radiusValue - 0.5f;
            var maximum = SpriteTextureSize - radiusValue - 0.5f;

            for (var verticalIndex = 0; verticalIndex < SpriteTextureSize; verticalIndex++)
            {
                for (var horizontalIndex = 0; horizontalIndex < SpriteTextureSize; horizontalIndex++)
                {
                    var horizontal = Mathf.Clamp(horizontalIndex, minimum, maximum);
                    var vertical = Mathf.Clamp(verticalIndex, minimum, maximum);
                    var distance = Vector2.Distance(
                        new Vector2(horizontalIndex, verticalIndex),
                        new Vector2(horizontal, vertical));
                    var alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radiusValue + 0.5f - distance) * 255f);
                    pixels[(verticalIndex * SpriteTextureSize) + horizontalIndex] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var border = new Vector4(radiusKey, radiusKey, radiusKey, radiusKey);
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, SpriteTextureSize, SpriteTextureSize),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            s_RoundedSprites[radiusKey] = sprite;
            return sprite;
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        public static Color Hex(string hexValue)
        {
            return ColorUtility.TryParseHtmlString($"#{hexValue}", out var color)
                ? color
                : Color.magenta;
        }
    }
}
