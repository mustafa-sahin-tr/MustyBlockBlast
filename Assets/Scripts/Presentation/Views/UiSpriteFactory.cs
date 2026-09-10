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
        private const int GLOW_SIZE = 128;

        // The rounded square is drawn 9-sliced with a pixelsPerUnitMultiplier of 3, so its corner
        // radius stays ~5 screen pixels whatever the cell size. The facet triangle cannot be sliced
        // (it is rotated and has diagonal edges), so it is stretched over the whole cell instead:
        // these numbers reproduce roughly the same corner radius on a board-sized cell.
        private const int TRIANGLE_SIZE = 128;
        private const int TRIANGLE_RADIUS = 6;

        private static Sprite _roundedSquare;
        private static Sprite _radialGlow;
        private static Sprite _triangleFacet;

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

        /// <summary>Soft radial falloff, white. One shared instance so every glow batches together.</summary>
        internal static Sprite RadialGlow
        {
            get
            {
                if (_radialGlow == null)
                {
                    _radialGlow = CreateRadialGlow(GLOW_SIZE);
                }

                return _radialGlow;
            }
        }

        /// <summary>
        /// Right triangle whose base is the top edge of the texture and whose apex is the exact
        /// centre, with the two base corners rounded to match <see cref="RoundedSquare"/>. Stretch
        /// it over a square cell and rotate by 0/90/180/270 to get the four bevel facets; the four
        /// rotations together tile the full rounded square. White — tint via Image.color.
        /// </summary>
        internal static Sprite TriangleFacet
        {
            get
            {
                if (_triangleFacet == null)
                {
                    _triangleFacet = CreateTriangleFacet(TRIANGLE_SIZE, TRIANGLE_RADIUS);
                }

                return _triangleFacet;
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

        private static Sprite CreateTriangleFacet(int size, int radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_TriangleFacet",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[size * size];

            // Vertices: base (0, size)-(size, size) along the top edge, apex at the centre.
            // Interior is x + y >= size (left diagonal) and y >= x (right diagonal).
            const float INV_SQRT2 = 0.70710678f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float pixelX = x + 0.5f;
                    float pixelY = y + 0.5f;

                    float leftCoverage = Mathf.Clamp01((((pixelX + pixelY) - size) * INV_SQRT2) + 0.5f);
                    float rightCoverage = Mathf.Clamp01(((pixelY - pixelX) * INV_SQRT2) + 0.5f);

                    // Round only the two base corners so the four rotations line up with the
                    // rounded-square silhouette used by the rest of the cell.
                    float centreX = pixelX < radius ? radius : (pixelX > size - radius ? size - radius : pixelX);
                    float centreY = pixelY > size - radius ? size - radius : pixelY;
                    float cornerCoverage = CircleCoverage(pixelX, pixelY, centreX, centreY, radius);

                    float alpha = Mathf.Min(Mathf.Min(leftCoverage, rightCoverage), cornerCoverage);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MustyBlockBlast_TriangleFacetSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite CreateRadialGlow(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_RadialGlow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[size * size];
            float centre = (size - 1) * 0.5f;
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - centre) / radius;
                    float dy = (y - centre) / radius;
                    float distance = Mathf.Sqrt((dx * dx) + (dy * dy));

                    // Smooth quadratic falloff: opaque core, fully transparent at the edge.
                    float alpha = Mathf.Clamp01(1f - distance);
                    alpha *= alpha;
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MustyBlockBlast_RadialGlowSprite";
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

            return CircleCoverage(pixelX, pixelY, centreX, centreY, radius);
        }

        /// <summary>Anti-aliased coverage of one pixel against a circle of <paramref name="radius"/>.</summary>
        private static float CircleCoverage(float pixelX, float pixelY, float centreX, float centreY, float radius)
        {
            float dx = pixelX - centreX;
            float dy = pixelY - centreY;
            float distance = Mathf.Sqrt((dx * dx) + (dy * dy));

            return Mathf.Clamp01(radius + 0.5f - distance);
        }
    }
}
