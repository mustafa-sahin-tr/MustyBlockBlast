using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Special-cell beats for the info demos (issue #450), on top of <see cref="InfoDemoChoreography"/>:
    /// a special cell drawn the way the real board draws it — its own icon (<c>BoardView.IconSprite</c>)
    /// over a soft halo in its identity hue (<see cref="InfoDemoPaint.SpecialCellGlow"/>) — and that cell
    /// going with the block it rides on when a clear takes it. Every helper only queues steps on an
    /// <see cref="InfoDemoTimelineBuilder"/>; nothing here runs at play time.
    /// </summary>
    internal static class InfoDemoSpecialCellChoreography
    {
        /// <summary>A special cell's icon, in board-cell widths.</summary>
        internal const float ICON_SIZE = 0.8f;

        /// <summary>The halo behind the icon: its size (board-cell widths) and resting opacity.</summary>
        internal const float HALO_SIZE = 1.35f;
        internal const float HALO_ALPHA = 0.55f;

        /// <summary>How long the icon takes to swell and fade as its cell clears.</summary>
        internal const float EXIT_DURATION = 0.3f;

        private const float EXIT_SCALE = 1.6f;
        private const float HALO_EXIT_DURATION = 0.2f;

        /// <summary>
        /// A <paramref name="kind"/> special cell on board cell (<paramref name="row"/>,
        /// <paramref name="column"/>), present from loop time 0: its halo and its icon. The block under
        /// it is the board pattern's own. Returns the icon's element id; the halo's comes back in
        /// <paramref name="haloId"/>.
        /// </summary>
        internal static int SpecialCell(
            InfoDemoTimelineBuilder builder, SpecialCellKind kind, int row, int column, out int haloId)
        {
            Vector2 cell = InfoDemoLayout.Cell(row, column);
            haloId = builder.AddGlow(cell, HALO_SIZE, InfoDemoPaint.SpecialCellGlow(kind), HALO_ALPHA);
            return builder.AddIcon(InfoDemoSprite.SpecialCellIcon, (int)kind, cell, ICON_SIZE);
        }

        /// <summary>The special cell (<paramref name="iconId"/>, <paramref name="haloId"/>) goes with its
        /// block at <paramref name="time"/> — pass <see cref="InfoDemoChoreography.ClearCellShrinkStart"/>
        /// for a line clear: the icon swells and fades, the halo fades. Returns the time it is gone.</summary>
        internal static float GoWithCell(InfoDemoTimelineBuilder builder, int iconId, int haloId, float time)
        {
            builder.Scale(iconId, time, EXIT_DURATION, 1f, EXIT_SCALE, InfoDemoEasing.EaseOutCubic);
            builder.Fade(iconId, time, EXIT_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);
            builder.Fade(haloId, time, HALO_EXIT_DURATION, HALO_ALPHA, 0f);
            return time + EXIT_DURATION;
        }

        /// <summary>How long a cell layer's value change pops (issue #453): a crack of the armour, a melt of
        /// the ice, a tick of the countdown.</summary>
        internal const float LAYER_STEP_DURATION = 0.3f;

        private const float LAYER_STEP_SCALE = 1.18f;
        private const float LAYER_APPEAR_DURATION = 0.18f;

        /// <summary>
        /// Cell layer <paramref name="layerId"/> (<see cref="InfoDemoTimelineBuilder.AddCellLayer"/>) changes to
        /// <paramref name="value"/> at <paramref name="time"/> with a small swell and a white flash — the ice
        /// losing a level, the armour losing a stage, the countdown ticking (issue #453). Returns the time the
        /// pop settles.
        /// </summary>
        internal static float StepLayer(InfoDemoTimelineBuilder builder, int layerId, int value, float time)
        {
            builder.Paint(layerId, time, value);
            builder.Scale(layerId, time, LAYER_STEP_DURATION, 1f, LAYER_STEP_SCALE, InfoDemoEasing.Pulse);
            return time + LAYER_STEP_DURATION;
        }

        /// <summary>Cell layer <paramref name="layerId"/> goes at <paramref name="time"/> the way
        /// <see cref="GoWithCell"/> takes an icon — it swells and fades — and then holds no value, so nothing is
        /// left of it (issue #453: a timer or diamond cell going with its cleared block, the last level of an ice
        /// socket melting). Returns the time it is gone.</summary>
        internal static float LayerGoes(InfoDemoTimelineBuilder builder, int layerId, float time)
        {
            builder.Scale(layerId, time, EXIT_DURATION, 1f, EXIT_SCALE, InfoDemoEasing.EaseOutCubic);
            builder.Fade(layerId, time, EXIT_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);

            float gone = time + EXIT_DURATION;
            builder.Paint(layerId, gone, 0);
            return gone;
        }

        /// <summary>Cell layer <paramref name="layerId"/> — added hidden (alpha 0) — appears at
        /// <paramref name="time"/> with the landing cell's pop (issue #453: a diamond arriving on the board
        /// with the piece that carried it).</summary>
        internal static void LayerAppears(InfoDemoTimelineBuilder builder, int layerId, float time)
        {
            builder.Fade(layerId, time, 0f, 0f, 1f);
            builder.Scale(
                layerId, time, LAYER_APPEAR_DURATION, InfoDemoChoreography.LAND_POP_START_SCALE, 1f, InfoDemoEasing.EaseOutBack);
        }
    }
}
