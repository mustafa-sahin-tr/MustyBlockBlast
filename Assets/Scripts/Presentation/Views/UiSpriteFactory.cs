using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Generates the handful of placeholder sprites the prototype needs (rounded square, vertical
    /// gradient) so no art assets are required. Each sprite is created once and shared by every
    /// Image, so all cells keep batching into a single draw call.
    /// </summary>
    internal static class UiSpriteFactory
    {
        private const int ROUNDED_SIZE = 64;
        private const int ROUNDED_RADIUS = 16;

        private static Sprite _roundedSquare;

        /// <summary>9-sliced rounded square, white. Tint via <see cref="UnityEngine.UI.Image.color"/>.</summary>
        internal static Sprite RoundedSquare
        {
            get
            {
                if (_roundedSquare == null)
                {
                    _roundedSquare = CreateRoundedSquare(ROUNDED_SIZE, ROUNDED_RADIUS);
                }

                return _roundedSquare;
            }
        }

        internal static Sprite CreateVerticalGradient(Color bottom, Color top)
        {
            const int HEIGHT = 128;
            var texture = new Texture2D(1, HEIGHT, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_Gradient",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[HEIGHT];
            for (int y = 0; y < HEIGHT; y++)
            {
                pixels[y] = Color.Lerp(bottom, top, y / (float)(HEIGHT - 1));
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, 1f, HEIGHT), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MustyBlockBlast_GradientSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateRoundedSquare(int size, int radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_RoundedSquare",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = CornerCoverage(x, y, size, radius);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            sprite.name = "MustyBlockBlast_RoundedSquareSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>Anti-aliased coverage of a rounded-rect corner for one pixel.</summary>
        private static float CornerCoverage(int x, int y, int size, int radius)
        {
            float pixelX = x + 0.5f;
            float pixelY = y + 0.5f;

            float centreX = pixelX < radius ? radius : (pixelX > size - radius ? size - radius : pixelX);
            float centreY = pixelY < radius ? radius : (pixelY > size - radius ? size - radius : pixelY);

            float dx = pixelX - centreX;
            float dy = pixelY - centreY;
            float distance = Mathf.Sqrt((dx * dx) + (dy * dy));

            return Mathf.Clamp01(radius + 0.5f - distance);
        }
    }
}
