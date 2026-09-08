using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Creates legacy UI Text using Unity's built-in runtime font. TextMeshPro is deliberately not
    /// used here: TMP Essentials are not imported in this project, so a TMP label would render
    /// nothing without a manual editor step.
    /// </summary>
    internal static class UiTextFactory
    {
        private static Font _builtinFont;

        internal static Text Create(
            RectTransform parent, string objectName, int fontSize, FontStyle fontStyle, Color colour)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var text = textObject.GetComponent<Text>();
            text.font = GetBuiltinFont();
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = colour;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Font GetBuiltinFont()
        {
            if (_builtinFont == null)
            {
                _builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (_builtinFont == null)
            {
                _builtinFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return _builtinFont;
        }
    }
}
