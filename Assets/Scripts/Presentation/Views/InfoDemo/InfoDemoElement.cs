using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The static definition of one thing a demo draws: its kind, its size, what it looks like, and
    /// its state at the start of every loop (<see cref="Initial"/>). Created once by
    /// <see cref="InfoDemoTimelineBuilder"/> and never mutated after <see cref="InfoDemoTimelineBuilder.Build"/>.
    /// </summary>
    internal sealed class InfoDemoElement
    {
        internal InfoDemoElement(
            InfoDemoElementKind kind,
            Vector2 size,
            InfoDemoSprite sprite,
            int spriteParameter,
            Vector2Int[] shape,
            string labelKey,
            string labelArgument,
            float cornerRadius,
            float strokeWidth,
            InfoDemoElementState initial,
            int variant = 0,
            int[] cellValues = null)
        {
            Kind = kind;
            Size = size;
            Sprite = sprite;
            SpriteParameter = spriteParameter;
            Shape = shape;
            LabelKey = labelKey;
            LabelArgument = labelArgument;
            CornerRadius = cornerRadius;
            StrokeWidth = strokeWidth;
            Initial = initial;
            Variant = variant;
            CellValues = cellValues;
        }

        internal InfoDemoElementKind Kind { get; }

        /// <summary>Footprint in board units (1 = one cell pitch; see <see cref="InfoDemoLayout"/>).
        /// For a <see cref="InfoDemoElementKind.Label"/>, x is the wrap width and y the font height.</summary>
        internal Vector2 Size { get; }

        internal InfoDemoSprite Sprite { get; }

        /// <summary>An <see cref="InfoDemoElementKind.Icon"/>'s sprite variant (see <see cref="InfoDemoSprite"/>);
        /// for a <see cref="InfoDemoElementKind.Piece"/>, the <c>SpecialPieceKind</c> it carries (issue #451 —
        /// 0, an ordinary piece, unless added with <see cref="InfoDemoTimelineBuilder.AddSpecialPiece"/>).</summary>
        internal int SpriteParameter { get; }

        /// <summary>A <see cref="InfoDemoElementKind.Piece"/>'s cells as (column, row) offsets from its
        /// top-left cell; null for every other kind.</summary>
        internal Vector2Int[] Shape { get; }

        /// <summary>A <see cref="InfoDemoElementKind.Label"/>'s localization key; null for a literal
        /// label (whose text is then <see cref="LabelArgument"/>) and for every other kind.</summary>
        internal string LabelKey { get; }

        /// <summary>A <see cref="InfoDemoElementKind.Label"/>'s format argument (the <c>{0}</c> of
        /// <see cref="LabelKey"/>), or its whole literal text when <see cref="LabelKey"/> is null
        /// (a counter like "0/1" that needs no translation). Null for a plain translated label.</summary>
        internal string LabelArgument { get; }

        /// <summary>A <see cref="InfoDemoElementKind.Panel"/>'s or <see cref="InfoDemoElementKind.Ring"/>'s
        /// corner radius in board units; 0 for every other kind.</summary>
        internal float CornerRadius { get; }

        /// <summary>A <see cref="InfoDemoElementKind.Ring"/>'s wall thickness in board units; 0 for every
        /// other kind.</summary>
        internal float StrokeWidth { get; }

        /// <summary>A <see cref="InfoDemoElementKind.CellLayer"/>'s fixed look variant (issue #453) — the
        /// armour skin of an <see cref="InfoDemoCellLayer.Armour"/> layer; 0 for every other kind.</summary>
        internal int Variant { get; }

        /// <summary>A <see cref="InfoDemoElementKind.Piece"/>'s per-cell diamond gem colour ids, parallel to
        /// <see cref="Shape"/> (issue #453 — the dock's diamond decoration, drawn on those cells through
        /// <c>DiamondVisuals</c>); 0 marks an undecorated cell. Null for an undecorated piece and every other
        /// kind.</summary>
        internal int[] CellValues { get; }

        /// <summary>The element's state at loop time 0. Mutable only while the builder owns it.</summary>
        internal InfoDemoElementState Initial;

        /// <summary>Drawn in the stage's overlay layer — over every piece, under the labels — instead of
        /// its kind's own layer (issue #449): a finger touching a tray piece, a badge hung on one. Set only
        /// while the builder owns the element (<see cref="InfoDemoTimelineBuilder.BringToFront"/>).</summary>
        internal bool OnTop { get; private set; }

        internal void MarkOnTop() => OnTop = true;
    }
}
