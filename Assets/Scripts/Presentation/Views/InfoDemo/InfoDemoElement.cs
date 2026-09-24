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
            InfoDemoElementState initial)
        {
            Kind = kind;
            Size = size;
            Sprite = sprite;
            SpriteParameter = spriteParameter;
            Shape = shape;
            LabelKey = labelKey;
            Initial = initial;
        }

        internal InfoDemoElementKind Kind { get; }

        /// <summary>Footprint in board units (1 = one cell pitch; see <see cref="InfoDemoLayout"/>).
        /// For a <see cref="InfoDemoElementKind.Label"/>, x is the wrap width and y the font height.</summary>
        internal Vector2 Size { get; }

        internal InfoDemoSprite Sprite { get; }
        internal int SpriteParameter { get; }

        /// <summary>A <see cref="InfoDemoElementKind.Piece"/>'s cells as (column, row) offsets from its
        /// top-left cell; null for every other kind.</summary>
        internal Vector2Int[] Shape { get; }

        /// <summary>A <see cref="InfoDemoElementKind.Label"/>'s localization key; null otherwise.</summary>
        internal string LabelKey { get; }

        /// <summary>The element's state at loop time 0. Mutable only while the builder owns it.</summary>
        internal InfoDemoElementState Initial;
    }
}
