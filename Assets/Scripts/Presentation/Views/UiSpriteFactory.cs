using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Generates the handful of placeholder sprites the prototype needs (rounded square, circle,
    /// bevel facet, glow, starburst, dock icons, check mark, vertical gradient) so no art assets are
    /// required. Each
    /// sprite is created once and shared by every
    /// Image, so all cells keep batching into a single draw call.
    /// </summary>
    internal static class UiSpriteFactory
    {
        private const int ROUNDED_SIZE = 64;

        /// <summary>
        /// Baked corner radius of <see cref="RoundedSquare"/>, in sprite pixels — which, at the sprite's
        /// 100 pixels per unit against the canvas's own 100, is also the radius in reference pixels a
        /// sliced Image renders it at with a <c>pixelsPerUnitMultiplier</c> of 1. Exposed so a caller can
        /// pick a rendered radius (<c>ROUNDED_RADIUS / wanted</c>) instead of a magic multiplier.
        /// </summary>
        internal const int ROUNDED_RADIUS = 16;
        private const int GLOW_SIZE = 128;
        private const int CIRCLE_SIZE = 128;

        // The rounded square is drawn 9-sliced with a pixelsPerUnitMultiplier of 3, so its corner
        // radius stays ~5 screen pixels whatever the cell size. The facet triangle cannot be sliced
        // (it is rotated and has diagonal edges), so it is stretched over the whole cell instead:
        // these numbers reproduce roughly the same corner radius on a board-sized cell.
        private const int TRIANGLE_SIZE = 128;
        private const int TRIANGLE_RADIUS = 6;

        private const int STARBURST_SIZE = 128;

        private const int REFRESH_ICON_SIZE = 128;

        private const int CHECK_MARK_SIZE = 128;

        /// <summary>Angle range (degrees, measured from the +x axis) left unringed on
        /// <see cref="RefreshIcon"/> for its arrowhead.</summary>
        private const float REFRESH_ICON_GAP_START_DEG = -35f;
        private const float REFRESH_ICON_GAP_END_DEG = 35f;

        /// <summary>Side of one glyph cell on the dock-icon sheet (see <see cref="RocketIcon"/>).</summary>
        private const int DOCK_ICON_SIZE = 128;

        /// <summary>Glyphs on the dock-icon sheet: rocket, then hammer.</summary>
        private const int DOCK_ICON_COUNT = 2;

        /// <summary>Spikes on <see cref="Starburst"/>. Six reads as a spark rather than as a snowflake
        /// (eight) or an arrow cluster (four) at the size a board cell draws it.</summary>
        private const int STARBURST_POINTS = 6;

        /// <summary>Radius of the starburst between its spikes, as a fraction of the spike radius. A
        /// fat enough waist that the shape keeps a solid core instead of reading as loose rays.</summary>
        private const float STARBURST_INNER_RADIUS = 0.42f;

        /// <summary>Points and waist of <see cref="FivePointStar"/>: five, with a waist wide enough
        /// that the badge-sized glyph keeps a solid body between its points.</summary>
        private const int STAR_POINTS = 5;
        private const float STAR_INNER_RADIUS = 0.55f;

        /// <summary>Width of one awning tile in sprite pixels — one stripe plus one gap, or one
        /// scallop. Rendered at 100 pixels per unit it is also the tile width in reference pixels
        /// before an Image's <c>pixelsPerUnitMultiplier</c> rescales it.</summary>
        private const int AWNING_TILE_WIDTH = 64;
        private const int AWNING_STRIPE_TILE_HEIGHT = 8;
        private const int AWNING_SCALLOP_TILE_HEIGHT = 32;

        /// <summary>Alpha of the awning's light stripes over the accent plate (the mockup's 0.28).</summary>
        private const byte AWNING_STRIPE_ALPHA = 72;

        private static Sprite _roundedSquare;
        private static Sprite _fivePointStar;
        private static Sprite _awningStripes;
        private static Sprite _awningScallops;
        private static readonly Sprite[] _roundedOutlines = new Sprite[ROUNDED_RADIUS + 1];
        private static Sprite _radialGlow;
        private static Sprite _triangleFacet;
        private static Sprite _circle;
        private static Sprite _starburst;
        private static Sprite _rocketIcon;
        private static Sprite _hammerIcon;
        private static Sprite _refreshIcon;
        private static Sprite _checkMark;

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

        /// <summary>
        /// 9-sliced hollow rounded frame, white: the outline of <see cref="RoundedSquare"/> with a
        /// <paramref name="thickness"/>-pixel wall (1..<see cref="ROUNDED_RADIUS"/>). Sliced at a
        /// radius, the wall renders at <c>thickness * radius / ROUNDED_RADIUS</c>, so callers pick the
        /// thickness from the radius they slice at (see <c>HudChrome.BuildOutline</c>). One instance per
        /// thickness, cached, so every ring of one weight batches together.
        /// </summary>
        internal static Sprite RoundedOutline(int thickness)
        {
            int clamped = Mathf.Clamp(thickness, 1, ROUNDED_RADIUS);
            if (_roundedOutlines[clamped] == null)
            {
                _roundedOutlines[clamped] = CreateRoundedOutline(ROUNDED_SIZE, ROUNDED_RADIUS, clamped);
            }

            return _roundedOutlines[clamped];
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

        /// <summary>
        /// Hard-edged disc, white, filling the whole texture. Unlike <see cref="RadialGlow"/> it is
        /// opaque right up to an anti-aliased rim, so it reads as a solid circle: draw a smaller one
        /// on top in the backing plate's colour to fake a ring or a cut-out. Tint via Image.color and
        /// use <c>Image.Type.Simple</c> — it must not be sliced.
        /// </summary>
        internal static Sprite Circle
        {
            get
            {
                if (_circle == null)
                {
                    _circle = CreateCircle(CIRCLE_SIZE);
                }

                return _circle;
            }
        }

        /// <summary>
        /// Six-pointed starburst, white, centred in the texture — the "explosive core" icon a special
        /// board cell wears. One shared instance like every other sprite here, so an icon on any number
        /// of cells still batches with the rest of the board. Tint via Image.color and use
        /// <c>Image.Type.Simple</c> — it must not be sliced.
        /// </summary>
        internal static Sprite Starburst
        {
            get
            {
                if (_starburst == null)
                {
                    _starburst = CreateStarburst(STARBURST_SIZE, STARBURST_POINTS, STARBURST_INNER_RADIUS);
                }

                return _starburst;
            }
        }

        /// <summary>
        /// Upward arrow, white — the <see cref="MustyBlockBlast.Core.SpecialPieceKind.PiercingRocket"/>
        /// mark a dock plate wears. Tint via Image.color and use <c>Image.Type.Simple</c> — it must not
        /// be sliced.
        /// <para>
        /// Shares one texture with <see cref="HammerIcon"/> (see <see cref="BuildDockIconSheet"/>), so
        /// the two dock marks batch with each other rather than costing a draw call apiece.
        /// </para>
        /// </summary>
        internal static Sprite RocketIcon
        {
            get
            {
                EnsureDockIcons();
                return _rocketIcon;
            }
        }

        /// <summary>
        /// Upright hammer (wide head over a narrow handle), white — the
        /// <see cref="MustyBlockBlast.Core.SpecialPieceKind.DemolitionHammer"/> mark a dock plate wears.
        /// Deliberately nothing like <see cref="RocketIcon"/>'s arrow: the hammer is the one dock piece
        /// that is tapped rather than dragged, so it has to be told apart at a glance. Tint via
        /// Image.color and use <c>Image.Type.Simple</c>.
        /// </summary>
        internal static Sprite HammerIcon
        {
            get
            {
                EnsureDockIcons();
                return _hammerIcon;
            }
        }

        /// <summary>
        /// An open ring with an arrowhead at one end, chasing its own tail — the "restart" mark the
        /// Path-mode game-over card wears on its "Play Again" button. Tint via Image.color and use
        /// <c>Image.Type.Simple</c> — it must not be sliced.
        /// </summary>
        internal static Sprite RefreshIcon
        {
            get
            {
                if (_refreshIcon == null)
                {
                    _refreshIcon = CreateRefreshIcon(REFRESH_ICON_SIZE);
                }

                return _refreshIcon;
            }
        }

        /// <summary>
        /// A tick — two thick strokes meeting at a point, white — the "this one is done" mark an
        /// objective icon wears. The one silhouette the existing primitives genuinely cannot fake: a
        /// rotated bar reads as a slash and a pair of them as a cross, both of which already mean
        /// something else in this UI ("close"). Tint via Image.color and use <c>Image.Type.Simple</c> —
        /// it must not be sliced.
        /// </summary>
        internal static Sprite CheckMark
        {
            get
            {
                if (_checkMark == null)
                {
                    _checkMark = CreateCheckMark(CHECK_MARK_SIZE);
                }

                return _checkMark;
            }
        }

        /// <summary>
        /// A five-pointed star, white — the badge glyph the level-path pill wears in Path mode. The
        /// same sweep as <see cref="Starburst"/> with five points and a fatter waist, so it reads as a
        /// star rather than a spark. Tint via Image.color and use <c>Image.Type.Simple</c>.
        /// </summary>
        internal static Sprite FivePointStar
        {
            get
            {
                if (_fivePointStar == null)
                {
                    _fivePointStar = CreateStarburst(STARBURST_SIZE, STAR_POINTS, STAR_INNER_RADIUS);
                }

                return _fivePointStar;
            }
        }

        /// <summary>
        /// One tile of the awning's stripes: the left half a translucent white bar, the right half
        /// clear. Drawn <c>Image.Type.Tiled</c> over an accent-tinted plate it lightens every other
        /// stripe of it, which is how the storefront awning gets its two-tone canvas from one tint.
        /// Left untinted (white) so the stripes stay lighter than whatever accent sits under them.
        /// </summary>
        internal static Sprite AwningStripes
        {
            get
            {
                if (_awningStripes == null)
                {
                    _awningStripes = CreateAwningStripes(AWNING_TILE_WIDTH, AWNING_STRIPE_TILE_HEIGHT);
                }

                return _awningStripes;
            }
        }

        /// <summary>
        /// One tile of the awning's scalloped hem: a disc centred on the tile's top edge, so only its
        /// lower half shows. Tiled along the bottom of the awning plate in the plate's own colour it
        /// hangs a row of half-discs off the hem. Tint via Image.color and use <c>Image.Type.Tiled</c>.
        /// </summary>
        internal static Sprite AwningScallops
        {
            get
            {
                if (_awningScallops == null)
                {
                    _awningScallops = CreateAwningScallops(AWNING_TILE_WIDTH, AWNING_SCALLOP_TILE_HEIGHT);
                }

                return _awningScallops;
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

        private static Sprite CreateCircle(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_Circle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[size * size];
            float centre = size * 0.5f;

            // Half a pixel of inset keeps the anti-aliased rim inside the texture, so scaling the
            // sprite up never clips the edge against the texture border.
            float radius = centre - 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = CircleCoverage(x + 0.5f, y + 0.5f, centre, centre, radius);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MustyBlockBlast_CircleSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>
        /// Draws a star whose radius sweeps between <paramref name="innerRadius"/> and the full radius
        /// <paramref name="points"/> times around the circle, so the spikes and the waist between them
        /// come from one continuous formula rather than from polygon edges — which keeps every spike
        /// identical and the anti-aliasing uniform all the way round.
        /// </summary>
        private static Sprite CreateStarburst(int size, int points, float innerRadius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_Starburst",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[size * size];
            float centre = size * 0.5f;

            // Half a pixel of inset, as in CreateCircle: the anti-aliased spike tips stay inside the
            // texture, so scaling the sprite up never clips them against the border.
            float radius = centre - 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - centre;
                    float dy = (y + 0.5f) - centre;
                    float distance = Mathf.Sqrt((dx * dx) + (dy * dy));

                    // Cos sweeps -1..1 `points` times around the circle; remapped to 0..1 it is how far
                    // this angle is from the waist towards a spike tip.
                    float spike = (Mathf.Cos(Mathf.Atan2(dy, dx) * points) + 1f) * 0.5f;
                    float edge = Mathf.Lerp(innerRadius, 1f, spike) * radius;

                    float alpha = Mathf.Clamp01(edge + 0.5f - distance);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MustyBlockBlast_StarburstSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>
        /// Draws the ring first (a radial in/out coverage test with a wedge of degrees left empty for
        /// the arrowhead), then the arrowhead as a triangle rooted where the ring resumes and pointing
        /// tangentially back into the gap — the same "chasing its tail" shape every refresh glyph uses.
        /// </summary>
        private static Sprite CreateRefreshIcon(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_Refresh",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[size * size];
            float centre = size * 0.5f;
            float outerRadius = centre * 0.72f;
            float thickness = size * 0.16f;
            float innerRadius = outerRadius - thickness;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float pixelX = x + 0.5f;
                    float pixelY = y + 0.5f;
                    float dx = pixelX - centre;
                    float dy = pixelY - centre;
                    float distance = Mathf.Sqrt((dx * dx) + (dy * dy));

                    float radialCoverage =
                        Mathf.Clamp01(Mathf.Min(distance - innerRadius, outerRadius - distance) + 0.5f);

                    float angleDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    bool inGap = angleDeg > REFRESH_ICON_GAP_START_DEG && angleDeg < REFRESH_ICON_GAP_END_DEG;

                    WritePixel(pixels, size, x, y, inGap ? 0f : radialCoverage);
                }
            }

            // The arrowhead sits at the ring's edge where the gap ends, its base along the radial
            // direction there and its tip extending tangentially back into the gap.
            float midRadius = (innerRadius + outerRadius) * 0.5f;
            float edgeAngleRad = REFRESH_ICON_GAP_END_DEG * Mathf.Deg2Rad;
            var edgePoint = new Vector2(
                centre + (midRadius * Mathf.Cos(edgeAngleRad)), centre + (midRadius * Mathf.Sin(edgeAngleRad)));
            var radialDirection = new Vector2(Mathf.Cos(edgeAngleRad), Mathf.Sin(edgeAngleRad));
            var tangentDirection = new Vector2(-Mathf.Sin(edgeAngleRad), Mathf.Cos(edgeAngleRad));

            float arrowLength = thickness * 2f;
            float arrowHalfWidth = thickness * 1.15f;
            Vector2 tip = edgePoint - (tangentDirection * arrowLength);
            Vector2 baseA = edgePoint + (radialDirection * arrowHalfWidth);
            Vector2 baseB = edgePoint - (radialDirection * arrowHalfWidth);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float pixelX = x + 0.5f;
                    float pixelY = y + 0.5f;
                    float alpha = TriangleCoverage(
                        pixelX, pixelY, baseA.x, baseA.y, baseB.x, baseB.y, tip.x, tip.y);
                    WritePixel(pixels, size, x, y, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MustyBlockBlast_RefreshIconSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>
        /// Draws the tick as two round-capped strokes — a short one down to the elbow and a long one up
        /// to the tip — using a distance-to-segment coverage test, so both strokes anti-alias uniformly
        /// and their join is seamless without any polygon work.
        /// </summary>
        private static Sprite CreateCheckMark(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_CheckMark",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[size * size];

            float halfThickness = size * 0.085f;

            // Texture space is y-up, so the elbow is the lowest of the three points.
            var strokeStart = new Vector2(size * 0.20f, size * 0.54f);
            var elbow = new Vector2(size * 0.42f, size * 0.28f);
            var tip = new Vector2(size * 0.82f, size * 0.76f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float pixelX = x + 0.5f;
                    float pixelY = y + 0.5f;

                    float shortStroke = SegmentCoverage(
                        pixelX, pixelY, strokeStart, elbow, halfThickness);
                    float longStroke = SegmentCoverage(pixelX, pixelY, elbow, tip, halfThickness);

                    WritePixel(pixels, size, x, y, Mathf.Max(shortStroke, longStroke));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MustyBlockBlast_CheckMarkSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>Anti-aliased coverage of one pixel by a round-capped stroke of
        /// <paramref name="halfThickness"/> running from <paramref name="from"/> to <paramref name="to"/>.</summary>
        private static float SegmentCoverage(
            float pixelX, float pixelY, Vector2 from, Vector2 to, float halfThickness)
        {
            float edgeX = to.x - from.x;
            float edgeY = to.y - from.y;
            float lengthSquared = (edgeX * edgeX) + (edgeY * edgeY);
            if (lengthSquared <= 0f)
            {
                return 0f;
            }

            float travel = Mathf.Clamp01(
                (((pixelX - from.x) * edgeX) + ((pixelY - from.y) * edgeY)) / lengthSquared);

            float dx = pixelX - (from.x + (travel * edgeX));
            float dy = pixelY - (from.y + (travel * edgeY));
            float distance = Mathf.Sqrt((dx * dx) + (dy * dy));

            return Mathf.Clamp01(halfThickness + 0.5f - distance);
        }

        private static void EnsureDockIcons()
        {
            // Both sprites come off one texture, so either one being gone means the sheet has to be
            // rebuilt for both — they are never created apart.
            if (_rocketIcon != null && _hammerIcon != null)
            {
                return;
            }

            BuildDockIconSheet();
        }

        /// <summary>
        /// Draws both dock glyphs side by side into a single texture and cuts one sprite out of each
        /// half. One texture rather than two for the reason every other sprite here is shared: a second
        /// texture would break the dock's batch the moment a special piece appeared in it.
        /// </summary>
        private static void BuildDockIconSheet()
        {
            int width = DOCK_ICON_SIZE * DOCK_ICON_COUNT;
            const int HEIGHT = DOCK_ICON_SIZE;

            var texture = new Texture2D(width, HEIGHT, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_DockIcons",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            // Color32's default is a fully transparent black, so only the glyphs themselves are written.
            var pixels = new Color32[width * HEIGHT];
            DrawRocket(pixels, width, 0);
            DrawHammer(pixels, width, DOCK_ICON_SIZE);

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            _rocketIcon = CreateDockIconSprite(texture, 0, "Rocket");
            _hammerIcon = CreateDockIconSprite(texture, DOCK_ICON_SIZE, "Hammer");
        }

        private static Sprite CreateDockIconSprite(Texture2D sheet, int originX, string glyphName)
        {
            Sprite sprite = Sprite.Create(
                sheet,
                new Rect(originX, 0f, DOCK_ICON_SIZE, DOCK_ICON_SIZE),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = $"MustyBlockBlast_{glyphName}IconSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>An arrow pointing up: a triangular head over a straight shaft. "Piercing" reads as
        /// direction, so the glyph is a direction rather than a picture of a rocket.</summary>
        private static void DrawRocket(Color32[] pixels, int stride, int originX)
        {
            const float SIZE = DOCK_ICON_SIZE;

            float apexX = 0.5f * SIZE;
            float apexY = 0.97f * SIZE;
            float baseLeftX = 0.16f * SIZE;
            float baseRightX = 0.84f * SIZE;
            float baseY = 0.52f * SIZE;

            float shaftMinX = 0.40f * SIZE;
            float shaftMaxX = 0.60f * SIZE;
            float shaftMinY = 0.06f * SIZE;
            float shaftMaxY = baseY;

            for (int y = 0; y < DOCK_ICON_SIZE; y++)
            {
                for (int x = 0; x < DOCK_ICON_SIZE; x++)
                {
                    float pixelX = x + 0.5f;
                    float pixelY = y + 0.5f;

                    float head = TriangleCoverage(
                        pixelX, pixelY, baseLeftX, baseY, baseRightX, baseY, apexX, apexY);
                    float shaft = RectCoverage(
                        pixelX, pixelY, shaftMinX, shaftMinY, shaftMaxX, shaftMaxY);

                    WritePixel(pixels, stride, originX + x, y, Mathf.Max(head, shaft));
                }
            }
        }

        /// <summary>A hammer seen head-on: a wide head sitting on a narrow handle. Two rectangles, so
        /// the silhouette stays legible at the size a dock plate draws it.</summary>
        private static void DrawHammer(Color32[] pixels, int stride, int originX)
        {
            const float SIZE = DOCK_ICON_SIZE;

            float headMinX = 0.12f * SIZE;
            float headMaxX = 0.88f * SIZE;
            float headMinY = 0.62f * SIZE;
            float headMaxY = 0.92f * SIZE;

            float handleMinX = 0.43f * SIZE;
            float handleMaxX = 0.57f * SIZE;
            float handleMinY = 0.08f * SIZE;
            float handleMaxY = headMinY;

            for (int y = 0; y < DOCK_ICON_SIZE; y++)
            {
                for (int x = 0; x < DOCK_ICON_SIZE; x++)
                {
                    float pixelX = x + 0.5f;
                    float pixelY = y + 0.5f;

                    float head = RectCoverage(pixelX, pixelY, headMinX, headMinY, headMaxX, headMaxY);
                    float handle = RectCoverage(
                        pixelX, pixelY, handleMinX, handleMinY, handleMaxX, handleMaxY);

                    WritePixel(pixels, stride, originX + x, y, Mathf.Max(head, handle));
                }
            }
        }

        private static void WritePixel(Color32[] pixels, int stride, int x, int y, float alpha)
        {
            if (alpha <= 0f)
            {
                return;
            }

            pixels[(y * stride) + x] =
                new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
        }

        /// <summary>Anti-aliased coverage of one pixel by an axis-aligned rectangle.</summary>
        private static float RectCoverage(
            float pixelX, float pixelY, float minX, float minY, float maxX, float maxY)
        {
            float horizontal = Mathf.Clamp01(Mathf.Min(pixelX - minX, maxX - pixelX) + 0.5f);
            float vertical = Mathf.Clamp01(Mathf.Min(pixelY - minY, maxY - pixelY) + 0.5f);
            return horizontal * vertical;
        }

        /// <summary>
        /// Anti-aliased coverage of one pixel by the triangle (a, b, c), which must be wound
        /// counter-clockwise. Each edge contributes a signed distance clamped to a one-pixel ramp and
        /// the smallest wins, which is the same half-plane trick <see cref="CreateTriangleFacet"/> uses
        /// for its two diagonals.
        /// </summary>
        private static float TriangleCoverage(
            float pixelX, float pixelY,
            float ax, float ay, float bx, float by, float cx, float cy)
        {
            float ab = EdgeCoverage(pixelX, pixelY, ax, ay, bx, by);
            float bc = EdgeCoverage(pixelX, pixelY, bx, by, cx, cy);
            float ca = EdgeCoverage(pixelX, pixelY, cx, cy, ax, ay);
            return Mathf.Min(Mathf.Min(ab, bc), ca);
        }

        /// <summary>How far inside the half-plane left of the directed edge the pixel sits, as a 0..1
        /// ramp one pixel wide.</summary>
        private static float EdgeCoverage(
            float pixelX, float pixelY, float fromX, float fromY, float toX, float toY)
        {
            float edgeX = toX - fromX;
            float edgeY = toY - fromY;
            float length = Mathf.Sqrt((edgeX * edgeX) + (edgeY * edgeY));
            if (length <= 0f)
            {
                return 0f;
            }

            float cross = (edgeX * (pixelY - fromY)) - (edgeY * (pixelX - fromX));
            return Mathf.Clamp01((cross / length) + 0.5f);
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

        /// <summary>The rounded square minus a copy of itself inset by <paramref name="thickness"/>
        /// on every side, leaving an anti-aliased frame whose corners follow the outer radius.</summary>
        private static Sprite CreateRoundedOutline(int size, int radius, int thickness)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_RoundedOutline_" + thickness,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            int innerSize = size - (thickness * 2);
            int innerRadius = Mathf.Max(0, radius - thickness);

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float outer = CornerCoverage(x, y, size, radius);
                    int innerX = x - thickness;
                    int innerY = y - thickness;
                    float inner = innerX >= 0 && innerY >= 0 && innerX < innerSize && innerY < innerSize
                        ? CornerCoverage(innerX, innerY, innerSize, innerRadius)
                        : 0f;
                    float alpha = Mathf.Clamp01(outer - inner);
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
            sprite.name = "MustyBlockBlast_RoundedOutlineSprite_" + thickness;
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

        /// <summary>The left half of the tile is a translucent white bar, the right half clear — see
        /// <see cref="AwningStripes"/>. Repeat wrap, so a tiled Image's seams are invisible.</summary>
        private static Sprite CreateAwningStripes(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_AwningStripes",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[width * height];
            int stripeWidth = width / 2;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte alpha = x < stripeWidth ? AWNING_STRIPE_ALPHA : (byte)0;
                    pixels[(y * width) + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MustyBlockBlast_AwningStripesSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>A white disc centred on the tile's top edge, so only its lower half is inside the
        /// tile — see <see cref="AwningScallops"/>. Slightly narrower than the tile so neighbouring
        /// scallops read as separate lobes rather than one wavy line.</summary>
        private static Sprite CreateAwningScallops(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "MustyBlockBlast_AwningScallops",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[width * height];
            float centreX = width * 0.5f;
            float radius = (width * 0.5f) - 2f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Texture rows count up from the bottom, so the disc's centre sits on the top row.
                    float alpha = CircleCoverage(x + 0.5f, y + 0.5f, centreX, height, radius);
                    pixels[(y * width) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = "MustyBlockBlast_AwningScallopsSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
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
