using System.Collections.Generic;
using MustyBlockBlast.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Builds the glyph that stands for one <see cref="ObjectiveType"/> on the objective icon row.
    /// <para>
    /// Every glyph is assembled from the shared primitives in <see cref="UiSpriteFactory"/> — rounded
    /// square, circle, triangle facet, starburst, refresh ring — rotated, squashed and stacked, exactly
    /// as <c>PowerUpInventoryView.BuildGlyph</c> does for the power-up strip. That is this project's
    /// icon convention: no art assets, no atlas, and every Image in the HUD still batching into one
    /// draw call because they all share the same handful of textures. Seventeen objective types are
    /// more silhouettes than two sprites can carry alone, so the later ones compose two or three
    /// primitives rather than reaching for new art.
    /// </para>
    /// <para>
    /// Some glyphs punch a hole — a "core" drawn in the backing plate's colour over a larger shape, the
    /// same trick <see cref="HubPanelView"/>'s tab glyphs use. Those Images go into a second bucket so
    /// the caller repaints them with the plate colour instead of the ink colour.
    /// </para>
    /// </summary>
    internal static class ObjectiveIconFactory
    {
        /// <summary>Rotation that turns the shared rounded square into a diamond.</summary>
        private const float DIAMOND_ROTATION_DEGREES = 45f;

        /// <summary>
        /// Builds <paramref name="type"/>'s glyph as a child of <paramref name="parent"/>, sized to fit
        /// a <paramref name="size"/>-square icon. Every Image it creates is appended either to
        /// <paramref name="inkImages"/> (painted with the ink colour) or to <paramref name="coreImages"/>
        /// (painted with the plate colour, to punch a hole); both start out transparent, because the
        /// theme is not known until the first repaint.
        /// </summary>
        internal static void Build(
            RectTransform parent,
            ObjectiveType type,
            float size,
            List<Image> inkImages,
            List<Image> coreImages)
        {
            float barLength = size * 0.72f;
            float barThickness = size * 0.15f;

            switch (type)
            {
                case ObjectiveType.SimultaneousLineClear:
                    // Two full lines, stacked: "clear N lines with one placement".
                    AddBar(parent, inkImages, barLength, barThickness, new Vector2(0f, size * 0.13f));
                    AddBar(parent, inkImages, barLength, barThickness, new Vector2(0f, -size * 0.13f));
                    break;

                case ObjectiveType.AtLeastLineClear:
                    // One line and a trailing dot — the "or more" the exact-match type above has not.
                    AddBar(parent, inkImages, barLength * 0.78f, barThickness, new Vector2(-size * 0.09f, 0f));
                    AddDisc(parent, inkImages, barThickness, new Vector2(size * 0.32f, 0f));
                    break;

                case ObjectiveType.RowAndColumnCrossClear:
                    // A row and a column crossing, which is literally what the objective asks for.
                    AddBar(parent, inkImages, barLength, barThickness, Vector2.zero);
                    AddBar(parent, inkImages, barThickness, barLength, Vector2.zero);
                    break;

                case ObjectiveType.BombInducedLineClear:
                    // The bomb's own starburst with the line it emptied laid across it.
                    AddStarburst(parent, inkImages, size * 0.66f, Vector2.zero);
                    AddBar(parent, inkImages, barLength, barThickness * 0.8f, Vector2.zero);
                    break;

                case ObjectiveType.ScoreInRun:
                    // The starburst alone: points, the way every score flourish in this game is drawn.
                    AddStarburst(parent, inkImages, size * 0.72f, Vector2.zero);
                    break;

                case ObjectiveType.PieceFamilyCount:
                    // A plain upright block — one piece of a shape family.
                    AddPlate(parent, inkImages, new Vector2(size * 0.5f, size * 0.5f), Vector2.zero, 0f);
                    break;

                case ObjectiveType.PieceIdCount:
                    // The same block on its point: one *named* piece rather than a whole family.
                    AddPlate(
                        parent, inkImages, new Vector2(size * 0.44f, size * 0.44f), Vector2.zero,
                        DIAMOND_ROTATION_DEGREES);
                    break;

                case ObjectiveType.PieceIdLineClear:
                    // That named piece, over the line it cleared.
                    AddPlate(
                        parent, inkImages, new Vector2(size * 0.38f, size * 0.38f),
                        new Vector2(0f, size * 0.12f), DIAMOND_ROTATION_DEGREES);
                    AddBar(parent, inkImages, barLength, barThickness * 0.8f, new Vector2(0f, -size * 0.27f));
                    break;

                case ObjectiveType.BoardWipeCount:
                    // An emptied board: the board's square outline with nothing left inside it.
                    AddPlate(parent, inkImages, new Vector2(size * 0.66f, size * 0.66f), Vector2.zero, 0f);
                    AddPlate(parent, coreImages, new Vector2(size * 0.42f, size * 0.42f), Vector2.zero, 0f);
                    break;

                case ObjectiveType.CenterCoreEvacuated:
                    // A ring: the middle of the board gone, the edges still standing.
                    AddDisc(parent, inkImages, size * 0.62f, Vector2.zero);
                    AddDisc(parent, coreImages, size * 0.34f, Vector2.zero);
                    break;

                case ObjectiveType.NoIsolatedHolesStreak:
                    // A solid block with one hole punched off-centre — the thing this objective is
                    // about not leaving behind. Offset so it is never mistaken for the centred ring.
                    AddPlate(parent, inkImages, new Vector2(size * 0.6f, size * 0.6f), Vector2.zero, 0f);
                    AddDisc(parent, coreImages, size * 0.2f, new Vector2(size * 0.13f, -size * 0.13f));
                    break;

                case ObjectiveType.FourCornersCleared:
                    // The four corners themselves, and nothing in the middle.
                    AddCornerBlocks(parent, inkImages, size);
                    break;

                case ObjectiveType.StreakThreshold:
                    // A rising bar chart: a streak is a run that gets longer.
                    AddStepBars(parent, inkImages, size, barThickness);
                    break;

                case ObjectiveType.ClutchRecoveryClear:
                    // A wedge pointing down — the board bearing down on the player before the save.
                    AddFacet(parent, inkImages, size * 0.66f, upright: false);
                    break;

                case ObjectiveType.EarlyScoreRush:
                    // The same wedge pointing up: score, and quickly.
                    AddFacet(parent, inkImages, size * 0.66f, upright: true);
                    break;

                case ObjectiveType.RollingLineClearWindow:
                    // The refresh ring — a window that keeps moving — with a cleared line across it.
                    AddRefreshRing(parent, inkImages, size * 0.74f);
                    AddBar(parent, inkImages, barLength * 0.9f, barThickness * 0.7f, Vector2.zero);
                    break;

                case ObjectiveType.RerollSave:
                    // The reroll's own ring, unadorned.
                    AddRefreshRing(parent, inkImages, size * 0.74f);
                    break;

                case ObjectiveType.DiamondsCleared:
                    // A cut gem in silhouette: the flat table across the top with the pavilion tapering
                    // to a point below it (issue #395). The procedural fallback only — the catalog
                    // carries the authored diamond glyph the board's cells wear.
                    AddBar(parent, inkImages, barLength * 0.9f, barThickness * 1.2f, new Vector2(0f, size * 0.22f));
                    AddFacet(parent, inkImages, size * 0.62f, upright: false);
                    break;

                default:
                    // An objective type added without a glyph still draws something legible rather than
                    // an empty slot, which would read as a bug.
                    AddDisc(parent, inkImages, size * 0.5f, Vector2.zero);
                    break;
            }
        }

        /// <summary>Four small blocks in the corners of the icon, middle left empty.</summary>
        private static void AddCornerBlocks(RectTransform parent, List<Image> inkImages, float size)
        {
            float blockSide = size * 0.26f;
            float offset = size * 0.24f;

            for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                float x = (cornerIndex % 2 == 0 ? -1f : 1f) * offset;
                float y = (cornerIndex < 2 ? 1f : -1f) * offset;
                AddPlate(parent, inkImages, new Vector2(blockSide, blockSide), new Vector2(x, y), 0f);
            }
        }

        /// <summary>Three bars of increasing height, sitting on a common baseline.</summary>
        private static void AddStepBars(
            RectTransform parent, List<Image> inkImages, float size, float barThickness)
        {
            const int BAR_COUNT = 3;
            float baseline = -size * 0.3f;
            float spacing = size * 0.24f;

            for (int barIndex = 0; barIndex < BAR_COUNT; barIndex++)
            {
                float height = size * (0.24f + (barIndex * 0.18f));
                float x = (barIndex - ((BAR_COUNT - 1) * 0.5f)) * spacing;
                AddPlate(
                    parent,
                    inkImages,
                    new Vector2(barThickness, height),
                    new Vector2(x, baseline + (height * 0.5f)),
                    0f);
            }
        }

        private static void AddBar(
            RectTransform parent, List<Image> inkImages, float width, float height, Vector2 anchoredPosition)
            => AddPlate(parent, inkImages, new Vector2(width, height), anchoredPosition, 0f);

        private static void AddPlate(
            RectTransform parent,
            List<Image> bucket,
            Vector2 size,
            Vector2 anchoredPosition,
            float rotationDegrees)
        {
            Image image = CreateImage(parent, "GlyphPlate", size, anchoredPosition, rotationDegrees);
            image.sprite = UiSpriteFactory.RoundedSquare;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 3f;
            bucket.Add(image);
        }

        private static void AddDisc(
            RectTransform parent, List<Image> bucket, float diameter, Vector2 anchoredPosition)
        {
            Image image = CreateImage(
                parent, "GlyphDisc", new Vector2(diameter, diameter), anchoredPosition, 0f);

            // The circle sprite has no border, so it must never be sliced.
            image.sprite = UiSpriteFactory.Circle;
            image.type = Image.Type.Simple;
            bucket.Add(image);
        }

        private static void AddStarburst(
            RectTransform parent, List<Image> bucket, float diameter, Vector2 anchoredPosition)
        {
            Image image = CreateImage(
                parent, "GlyphStar", new Vector2(diameter, diameter), anchoredPosition, 0f);
            image.sprite = UiSpriteFactory.Starburst;
            image.type = Image.Type.Simple;
            bucket.Add(image);
        }

        private static void AddRefreshRing(RectTransform parent, List<Image> bucket, float diameter)
        {
            Image image = CreateImage(parent, "GlyphRing", new Vector2(diameter, diameter), Vector2.zero, 0f);
            image.sprite = UiSpriteFactory.RefreshIcon;
            image.type = Image.Type.Simple;
            bucket.Add(image);
        }

        /// <summary>The shared bevel triangle, pointed up or down. Its own texture has the base along
        /// the top edge, so "upright" is the rotated one.</summary>
        private static void AddFacet(RectTransform parent, List<Image> bucket, float size, bool upright)
        {
            Image image = CreateImage(
                parent, "GlyphFacet", new Vector2(size, size), Vector2.zero, upright ? 180f : 0f);
            image.sprite = UiSpriteFactory.TriangleFacet;
            image.type = Image.Type.Simple;
            bucket.Add(image);
        }

        private static Image CreateImage(
            RectTransform parent, string objectName, Vector2 size, Vector2 anchoredPosition, float rotationDegrees)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)imageObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            if (rotationDegrees != 0f)
            {
                // Rotating the rect, not the sprite, so the shared texture stays shared and the glyph
                // keeps batching with the rest of the UI.
                rect.localRotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            }

            var image = imageObject.GetComponent<Image>();
            image.color = Color.clear;

            // Raycasts stay off: taps arrive through BoardInputView's pointer action, not through an
            // EventSystem, and this scene has none.
            image.raycastTarget = false;
            return image;
        }
    }
}
