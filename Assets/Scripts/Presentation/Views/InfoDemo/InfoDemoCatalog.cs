using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Which info-popup subjects have an animated demo, and the demo for each. A subject with none
    /// returns null and its card keeps today's static hero icon (issue #445 AC9). Each demo is built
    /// once, the first time it is asked for, and cached — a timeline is immutable, so replaying it
    /// every time the card opens costs nothing.
    /// <para>
    /// Adding a demo (issues #447–#454) is: a new script class with a static <c>Build()</c>, one case
    /// in <see cref="Find"/> (info-popup subjects) or <see cref="FindObjective"/> (objective cards),
    /// and one cache slot. Objectives are looked up by their own definition rather than through
    /// <see cref="InfoPopupSubjectKind"/>, so an objective type can never collide with a special
    /// cell/power-up kind value, and a demo can match the objective's own parameters.
    /// </para>
    /// </summary>
    internal sealed class InfoDemoCatalog
    {
        private InfoDemoTimeline _vortex;

        /// <summary>Simultaneous Line Clear demos, one per required line count, indexed by that count.</summary>
        private readonly InfoDemoTimeline[] _simultaneousLineClear =
            new InfoDemoTimeline[SimultaneousLineClearInfoDemo.MAX_LINE_COUNT + 1];

        /// <summary>The demo for (<paramref name="subjectKind"/>, <paramref name="kindValue"/>), or null
        /// when that subject has none.</summary>
        internal InfoDemoTimeline Find(InfoPopupSubjectKind subjectKind, int kindValue)
        {
            if (subjectKind == InfoPopupSubjectKind.SpecialCell && kindValue == (int)SpecialCellKind.Vortex)
            {
                if (_vortex == null)
                {
                    _vortex = VortexInfoDemo.Build();
                }

                return _vortex;
            }

            return null;
        }

        /// <summary>The demo for the objective card of <paramref name="definition"/>, or null when that
        /// objective has none (its card keeps its static glyph). A Simultaneous Line Clear demo shows the
        /// objective's own required line count; a count the demo cannot draw gets null.</summary>
        internal InfoDemoTimeline FindObjective(ObjectiveDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            switch (definition.Type)
            {
                case ObjectiveType.SimultaneousLineClear:
                {
                    int lineCount = definition.RequiredLineCount;
                    if (!SimultaneousLineClearInfoDemo.Supports(lineCount))
                    {
                        return null;
                    }

                    if (_simultaneousLineClear[lineCount] == null)
                    {
                        _simultaneousLineClear[lineCount] = SimultaneousLineClearInfoDemo.Build(lineCount);
                    }

                    return _simultaneousLineClear[lineCount];
                }

                default:
                    return null;
            }
        }
    }
}
