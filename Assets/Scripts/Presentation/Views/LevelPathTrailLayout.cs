using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Where each level node sits on the winding trail <see cref="LevelPathPanelView"/> scrolls.
    /// <para>
    /// Pure math and deliberately not a spline: the trail only has to read as "a path through a
    /// season" rather than as a grid, and a sine sweep does that with one multiply per node — which
    /// matters because the layout is evaluated once for every authored level when the panel is built.
    /// A spline would buy curvature nobody can see at this node spacing and would put a control-point
    /// table between the designer and the shape.
    /// </para>
    /// <para>
    /// Coordinates are content-local with y growing downward from the first node's row: level 1 is at
    /// the top and the walk descends, which is the reading order of every other list in this game and
    /// keeps <see cref="ContentHeight"/> a straight multiplication rather than an inversion.
    /// </para>
    /// </summary>
    internal static class LevelPathTrailLayout
    {
        /// <summary>Vertical distance between consecutive nodes. Also the trail's only length scale:
        /// <see cref="ContentHeight"/> is derived from it, so the two can never disagree.</summary>
        internal const float ROW_SPACING = 150f;

        /// <summary>
        /// Nodes per full left-right-left sweep. Six is the smallest count that still reads as a curve
        /// rather than as a zigzag once the segments between nodes are drawn.
        /// </summary>
        private const float NODES_PER_SWEEP = 6f;

        /// <summary>
        /// Horizontal position of a node, as a fraction of the sweep amplitude. Split out from
        /// <see cref="WaypointOf"/> so the accent scatter can sample the same curve between two nodes
        /// without re-deriving the phase.
        /// </summary>
        private static float SweepAt(float levelIndex)
            => Mathf.Sin(levelIndex * (2f * Mathf.PI / NODES_PER_SWEEP));

        /// <summary>
        /// Content-local position of the node for <paramref name="levelIndex"/> (0 for level 1), given
        /// the sweep amplitude in canvas pixels. y is negative: the trail descends from the top.
        /// </summary>
        internal static Vector2 WaypointOf(int levelIndex, float amplitude)
            => new Vector2(SweepAt(levelIndex) * amplitude, -levelIndex * ROW_SPACING);

        /// <summary>
        /// A point on the trail between two nodes, used to scatter the seasonal accent along the path
        /// instead of only at the nodes. <paramref name="travel"/> is 0..1 across the gap, and the x is
        /// sampled from the same sweep rather than interpolated, so an accent placed mid-gap sits on the
        /// curve rather than on the chord the drawn segment approximates it with.
        /// </summary>
        internal static Vector2 PointBetween(int levelIndex, float travel, float amplitude)
            => new Vector2(
                SweepAt(levelIndex + travel) * amplitude,
                -(levelIndex + travel) * ROW_SPACING);

        /// <summary>
        /// Height the scroll content needs to hold <paramref name="levelCount"/> nodes with
        /// <paramref name="verticalPadding"/> of breathing room above the first and below the last.
        /// Sized to the node extents exactly, so scrolling to either end never reveals empty trail.
        /// </summary>
        internal static float ContentHeight(int levelCount, float verticalPadding)
            => (Mathf.Max(1, levelCount) - 1) * ROW_SPACING + (verticalPadding * 2f);
    }
}
