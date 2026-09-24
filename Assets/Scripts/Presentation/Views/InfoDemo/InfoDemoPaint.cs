namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The palette an info demo paints with — ids, not colours, so the stage resolves each against the
    /// current theme (see <see cref="InfoDemoProperty.Paint"/>). 1..5 are the theme's own block colours
    /// (<c>ThemeDefinition.GetFill</c> colour ids), the rest are demo-specific accents.
    /// </summary>
    internal static class InfoDemoPaint
    {
        /// <summary>No paint: a board block is hidden (the empty cell under it shows).</summary>
        internal const int NONE = 0;

        /// <summary>Theme block colours, matching <c>ThemeDefinition.GetFill</c>'s 1-based colour ids.</summary>
        internal const int BLOCK_1 = 1;
        internal const int BLOCK_2 = 2;
        internal const int BLOCK_3 = 3;
        internal const int BLOCK_4 = 4;
        internal const int BLOCK_5 = 5;

        /// <summary>The deep indigo block a Vortex fill lands as.</summary>
        internal const int VORTEX_BLOCK = 20;

        /// <summary>The Vortex's own violet — its icon glow, burst and island outlines.</summary>
        internal const int VORTEX_GLOW = 21;

        /// <summary>Plain white (a label outline, a flash).</summary>
        internal const int WHITE = 30;

        /// <summary>The theme's accent (the progress bar's gold).</summary>
        internal const int ACCENT = 31;

        /// <summary>The theme's ink.</summary>
        internal const int INK = 32;

        /// <summary>The theme's would-clear highlight — a line about to clear.</summary>
        internal const int LINE_HIGHLIGHT = 33;

        /// <summary>Maps a board-pattern character to a paint: '.' empty, then the first letter of each
        /// Ilkbahar block colour — 'p' pink (1), 'g' green (2), 'u' purple (3), 'm' magenta (4),
        /// 'b' blue (5), 'v' vortex indigo. Anything else is <see cref="NONE"/>.</summary>
        internal static int FromPatternChar(char character)
        {
            switch (character)
            {
                case 'p':
                    return BLOCK_1;
                case 'g':
                    return BLOCK_2;
                case 'u':
                    return BLOCK_3;
                case 'm':
                    return BLOCK_4;
                case 'b':
                    return BLOCK_5;
                case 'v':
                    return VORTEX_BLOCK;
                default:
                    return NONE;
            }
        }
    }
}
