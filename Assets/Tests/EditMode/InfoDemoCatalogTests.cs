using System;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Which info-popup subjects get an animated demo (issue #446): only the Vortex special cell so
    /// far. Every other subject must fall back to the static hero icon (issue #445 AC9), which the View
    /// does whenever the catalog returns null.
    /// </summary>
    public class InfoDemoCatalogTests
    {
        [Test]
        public void Vortex_HasADemo_AndItIsCached()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            InfoDemoTimeline first = catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)SpecialCellKind.Vortex);
            InfoDemoTimeline second = catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)SpecialCellKind.Vortex);

            Assert.IsNotNull(first);
            Assert.AreSame(first, second);
        }

        [Test]
        public void EveryOtherSpecialCell_HasNoDemo()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();
            Array kinds = Enum.GetValues(typeof(SpecialCellKind));

            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                SpecialCellKind kind = (SpecialCellKind)kinds.GetValue(kindIndex);
                if (kind == SpecialCellKind.Vortex)
                {
                    continue;
                }

                Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialCell, (int)kind), kind.ToString());
            }
        }

        [Test]
        public void PowerUpsSpecialPiecesAndHold_HaveNoDemo_EvenWithTheVortexKindValue()
        {
            InfoDemoCatalog catalog = new InfoDemoCatalog();

            AssertNoneFor(catalog, InfoPopupSubjectKind.PowerUp, typeof(PowerUpKind));
            AssertNoneFor(catalog, InfoPopupSubjectKind.SpecialPiece, typeof(SpecialPieceKind));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.Hold, 0));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.PowerUp, (int)SpecialCellKind.Vortex));
            Assert.IsNull(catalog.Find(InfoPopupSubjectKind.SpecialPiece, (int)SpecialCellKind.Vortex));
        }

        [Test]
        public void VortexDemo_MatchesTheAuthoredLayout()
        {
            InfoDemoTimeline timeline = new InfoDemoCatalog().Find(
                InfoPopupSubjectKind.SpecialCell, (int)SpecialCellKind.Vortex);
            InfoDemoElementState[] states = new InfoDemoElementState[timeline.ElementCount];

            Assert.AreEqual(VortexInfoDemo.LOOP_DURATION, timeline.Duration, 0.0001f);

            // Loop start: row 7 has its single gap at column 4; the three islands are empty.
            timeline.Evaluate(0f, states);
            Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(7, 4)].Paint);
            Assert.AreNotEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(7, 1)].Paint);
            Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(3, 3)].Paint);
            Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(5, 6)].Paint);

            // After the fills (and before row 5's cascade clear) every island cell is Vortex indigo.
            timeline.Evaluate(3.1f, states);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(3, 3)].Paint);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(5, 2)].Paint);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(5, 4)].Paint);
            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(5, 6)].Paint);
            Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(7, 1)].Paint, "row 7 cleared");

            // Near the end: row 5 has cleared, the filled (3,3) survives.
            timeline.Evaluate(4.5f, states);
            for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
            {
                Assert.AreEqual(InfoDemoPaint.NONE, states[InfoDemoLayout.BoardBlockId(5, column)].Paint);
            }

            Assert.AreEqual(InfoDemoPaint.VORTEX_BLOCK, states[InfoDemoLayout.BoardBlockId(3, 3)].Paint);
        }

        private static void AssertNoneFor(InfoDemoCatalog catalog, InfoPopupSubjectKind subjectKind, Type enumType)
        {
            Array kinds = Enum.GetValues(enumType);
            for (int kindIndex = 0; kindIndex < kinds.Length; kindIndex++)
            {
                int kindValue = Convert.ToInt32(kinds.GetValue(kindIndex));
                Assert.IsNull(catalog.Find(subjectKind, kindValue), $"{subjectKind} {kinds.GetValue(kindIndex)}");
            }
        }
    }
}
