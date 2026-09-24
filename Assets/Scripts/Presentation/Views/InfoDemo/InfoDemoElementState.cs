using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Every animatable value of one demo element at one instant — what
    /// <see cref="InfoDemoTimeline.Evaluate"/> writes and <see cref="InfoDemoStage"/> renders. A plain
    /// mutable struct held in a preallocated array, so sampling a frame allocates nothing.
    /// </summary>
    internal struct InfoDemoElementState
    {
        /// <summary>Board units: x = column, y = row (row 0 at the top) — see <see cref="InfoDemoLayout"/>.</summary>
        internal Vector2 Position;
        internal float Scale;
        internal float Alpha;
        internal float Rotation;
        internal float Flash;
        internal int Paint;

        internal static InfoDemoElementState At(Vector2 position, int paint, float alpha)
        {
            return new InfoDemoElementState
            {
                Position = position,
                Scale = 1f,
                Alpha = alpha,
                Rotation = 0f,
                Flash = 0f,
                Paint = paint,
            };
        }

        /// <summary>Field-wise equality without boxing — the stage skips re-applying an unchanged
        /// element so a still element never dirties its canvas.</summary>
        internal bool SameAs(in InfoDemoElementState other)
        {
            return Position == other.Position
                && Mathf.Approximately(Scale, other.Scale)
                && Mathf.Approximately(Alpha, other.Alpha)
                && Mathf.Approximately(Rotation, other.Rotation)
                && Mathf.Approximately(Flash, other.Flash)
                && Paint == other.Paint;
        }
    }
}
