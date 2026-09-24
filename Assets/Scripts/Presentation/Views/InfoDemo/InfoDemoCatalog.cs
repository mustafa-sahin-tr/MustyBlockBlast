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
    /// in <see cref="Find"/>, and one cache slot.
    /// </para>
    /// </summary>
    internal sealed class InfoDemoCatalog
    {
        private InfoDemoTimeline _vortex;

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
    }
}
