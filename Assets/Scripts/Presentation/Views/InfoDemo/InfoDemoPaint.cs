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

        /// <summary>The theme's soft ink — secondary text (a progress chip's caption).</summary>
        internal const int SOFT_INK = 34;

        /// <summary>The "goal done" green of a progress chip's check badge.</summary>
        internal const int SUCCESS = 35;

        /// <summary>Power-up button plates in the demo strip (issue #448), one per power-up family:
        /// Bomb red (#E2533D), Row/Column Clear blue (#3F8FD6), Joker violet (#BA68C8), Color Cleanser
        /// green (#66BB6A), Paint Cross pink (#F06292).</summary>
        internal const int PLATE_BOMB = 40;
        internal const int PLATE_LINE_CLEAR = 41;
        internal const int PLATE_JOKER = 42;
        internal const int PLATE_CLEANSER = 43;
        internal const int PLATE_PAINT = 44;

        /// <summary>The yellow (#FFD54F) of a power-up's "armed" ring and glow.</summary>
        internal const int ARMED = 45;

        /// <summary>A bomb's orange blast (#FF8A3D).</summary>
        internal const int BLAST = 46;

        /// <summary>The soft coral (#FFB4A2) a bomb's 3x3 target area is previewed in.</summary>
        internal const int BLAST_PREVIEW = 47;

        /// <summary>The red (#E53935) of a power-up button's count badge.</summary>
        internal const int BADGE_RED = 48;

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
