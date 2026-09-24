namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The one animatable property an <see cref="InfoDemoStep"/> drives on its element. Colour is
    /// deliberately split into <see cref="Paint"/> (which palette entry, switched instantly) and
    /// <see cref="Flash"/> (a 0..1 blend of that entry towards white), rather than tweening raw
    /// <c>Color</c> values, so a timeline never bakes a theme's colours in: the stage resolves a paint
    /// against whatever theme is current, and a theme switch mid-loop repaints correctly.
    /// </summary>
    internal enum InfoDemoProperty
    {
        /// <summary>Opacity, 0..1, in <c>From.x</c>/<c>To.x</c>.</summary>
        Alpha,

        /// <summary>Uniform scale, in <c>From.x</c>/<c>To.x</c>.</summary>
        Scale,

        /// <summary>Position in board units (x = column, y = row, row 0 at the top), in
        /// <c>From.xy</c>/<c>To.xy</c> — see <see cref="InfoDemoLayout"/>.</summary>
        Position,

        /// <summary>Z rotation in degrees, in <c>From.x</c>/<c>To.x</c>.</summary>
        Rotation,

        /// <summary>Blend of the element's paint towards white, 0..1, in <c>From.x</c>/<c>To.x</c> —
        /// the line-clear flash.</summary>
        Flash,

        /// <summary>Palette entry (<see cref="InfoDemoPaint"/>), in <c>To.x</c>. Discrete: switched the
        /// moment the step starts, whatever its duration.</summary>
        Paint,
    }
}
