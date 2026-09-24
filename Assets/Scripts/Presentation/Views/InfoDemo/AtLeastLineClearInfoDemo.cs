using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.AtLeastLineClear"/> objective card's demo (issue #452, mockup artboard
    /// "En az N"), authored from the real rule (<see cref="ObjectiveProgress"/>): one placement must clear
    /// <b>at least</b> the objective's required line count. The demo is built for that count N, so the
    /// card never shows a different N than its own text.
    /// <para>
    /// What one qualifying placement looks like is exactly the Simultaneous Line Clear demo's (#447): a
    /// vertical 1xN drops into the one gap of the bottom N rows and all N clear together. So this is that
    /// choreography (<see cref="SimultaneousLineClearInfoDemo.Build(int, string)"/>) with an "AT LEAST N"
    /// chip — 5.0 s loop: 0.45 s the 1xN lifts from tray slot 1 · ~1.15 s it lands in column 4 · 1.3 s the
    /// bottom N rows clear at once · 1.7 s "N lines!" · 1.75 s the chip ticks 0/1 → 1/1 with the check.
    /// </para>
    /// <para>
    /// N is 2..5: 2 is the authoring default (<c>LevelObjectiveConfig._requiredLineCount</c>) and the
    /// debug cheat's value; no level in the catalog uses this type yet. 5 is the most a vertical catalog
    /// line (1x5) can clear on the demo board. Any other N gets no demo and the card keeps its glyph.
    /// </para>
    /// </summary>
    internal static class AtLeastLineClearInfoDemo
    {
        internal const float LOOP_DURATION = SimultaneousLineClearInfoDemo.LOOP_DURATION;

        internal const int MIN_LINE_COUNT = SimultaneousLineClearInfoDemo.MIN_LINE_COUNT;
        internal const int MAX_LINE_COUNT = SimultaneousLineClearInfoDemo.MAX_BUILDABLE_LINE_COUNT;

        /// <summary>Whether a demo exists for an objective requiring at least <paramref name="lineCount"/> lines.</summary>
        internal static bool Supports(int lineCount) => lineCount >= MIN_LINE_COUNT && lineCount <= MAX_LINE_COUNT;

        internal static InfoDemoTimeline Build(int lineCount)
            => SimultaneousLineClearInfoDemo.Build(lineCount, LocalizationKeys.INFO_POPUP_DEMO_CHIP_AT_LEAST);
    }
}
