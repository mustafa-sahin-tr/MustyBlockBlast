using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #147: the per-colour destroyed-cell tally the resolvers report, the ColourCleared
    /// objective that consumes it (by placement and by power-up), the 5-colour palette, and the
    /// authoring validation — plus the two negative guarantees: no progress without the wanted
    /// colour, and progress that is a pure function of colour id, never of a theme.
    /// </summary>
    public class ColourClearedObjectiveTests
    {
        private const int WANTED = 2;
        private const int OTHER = 1;

        // --- Resolver tally ---

        [Test]
        public void ResolveClears_OneFullRow_TalliesEveryCellByItsColour()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 3), x < 5 ? WANTED : OTHER);
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(ColourTally.LENGTH, result.DestroyedCellCountByColour.Count);
            Assert.AreEqual(5, result.DestroyedCellCountByColour[WANTED]);
            Assert.AreEqual(3, result.DestroyedCellCountByColour[OTHER]);
            Assert.AreEqual(0, result.DestroyedCellCountByColour[0]);
        }

        [Test]
        public void ResolveClears_IntersectingRowAndColumn_CountsTheSharedCellOnce()
        {
            var board = new Board();
            for (int i = 0; i < Board.SIZE; i++)
            {
                board.Occupy(new GridPosition(i, 2), WANTED);
                board.Occupy(new GridPosition(4, i), WANTED);
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            // 8 + 8 - 1 shared: the same rule ClearedCellCount already follows.
            Assert.AreEqual(15, result.ClearedCellCount);
            Assert.AreEqual(15, result.DestroyedCellCountByColour[WANTED]);
        }

        [Test]
        public void ResolveClears_NoFullLine_TalliesNothing()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), WANTED);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            for (int colourId = 0; colourId < ColourTally.LENGTH; colourId++)
            {
                Assert.AreEqual(0, result.DestroyedCellCountByColour[colourId]);
            }
        }

        [Test]
        public void ResolveClears_AReinforcedCellThatOnlyTookAHit_IsNotTallied()
        {
            var board = new Board();
            for (int x = 1; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 1), WANTED);
            }

            board.OccupyReinforced(new GridPosition(0, 1), WANTED, hitCount: 2, skin: 0);

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(Board.SIZE - 1, result.DestroyedCellCountByColour[WANTED]);
        }

        [Test]
        public void ResolveColorCleanser_TalliesOnlyTheTargetsColour()
        {
            var board = new Board();
            board.Occupy(new GridPosition(0, 0), WANTED);
            board.Occupy(new GridPosition(7, 7), WANTED);
            board.Occupy(new GridPosition(3, 3), WANTED);
            board.Occupy(new GridPosition(1, 1), OTHER);

            PowerUpClearResult result = PowerUpClearResolver.ResolveColorCleanser(board, new GridPosition(0, 0));

            Assert.AreEqual(3, result.DestroyedCellCountByColour[WANTED]);
            Assert.AreEqual(0, result.DestroyedCellCountByColour[OTHER]);
        }

        [Test]
        public void ResolveBombClear_TalliesTheMixedColoursItDestroyed()
        {
            var board = new Board();
            board.Occupy(new GridPosition(3, 3), WANTED);
            board.Occupy(new GridPosition(4, 4), WANTED);
            board.Occupy(new GridPosition(2, 2), OTHER);
            board.Occupy(new GridPosition(7, 7), WANTED); // outside the 3x3

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, new GridPosition(3, 3));

            Assert.AreEqual(2, result.DestroyedCellCountByColour[WANTED]);
            Assert.AreEqual(1, result.DestroyedCellCountByColour[OTHER]);
        }

        [Test]
        public void ResolveCascade_SumsTheTallyAcrossPhases()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, 5), WANTED);
            }

            CascadeClearResult cascade = CascadeClearResolver.ResolveCascade(board);

            Assert.AreEqual(Board.SIZE, cascade.TotalDestroyedCellCountByColour[WANTED]);
            Assert.AreEqual(0, cascade.TotalDestroyedCellCountByColour[OTHER]);
        }

        // --- Objective progress ---

        [Test]
        public void ApplyPlacement_AdvancesByTheNumberOfWantedCellsDestroyed()
        {
            ObjectiveProgress objective = ColourObjective(target: 20);

            bool changed = objective.ApplyPlacement(Placement(Tally((WANTED, 6), (OTHER, 2))));

            Assert.IsTrue(changed);
            Assert.AreEqual(6, objective.CurrentValue);
            Assert.IsFalse(objective.IsComplete);
        }

        [Test]
        public void ApplyPlacement_ClampsAtTheTargetAndCompletes()
        {
            ObjectiveProgress objective = ColourObjective(target: 5);

            objective.ApplyPlacement(Placement(Tally((WANTED, 8))));

            Assert.AreEqual(5, objective.CurrentValue);
            Assert.IsTrue(objective.IsComplete);
        }

        [Test]
        public void ApplyPlacement_DestroyingOnlyOtherColours_DoesNotAdvance()
        {
            ObjectiveProgress objective = ColourObjective(target: 8);

            // Two full lines of the other colour: 16 cells, none of them wanted.
            bool changed = objective.ApplyPlacement(Placement(Tally((OTHER, 16)), linesCleared: 2));

            Assert.IsFalse(changed);
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void ApplyPlacement_WithNoTally_DoesNotAdvance()
        {
            ObjectiveProgress objective = ColourObjective(target: 8);

            Assert.IsFalse(objective.ApplyPlacement(Placement(null)));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void ApplyPowerUpColourCleared_AdvancesByTheWantedCount()
        {
            ObjectiveProgress objective = ColourObjective(target: 10);

            Assert.IsTrue(objective.ApplyPowerUpColourCleared(Tally((WANTED, 3), (OTHER, 5))));
            Assert.AreEqual(3, objective.CurrentValue);
        }

        [Test]
        public void ApplyPowerUpColourCleared_WithNoneOfTheWantedColour_ChangesNothing()
        {
            ObjectiveProgress objective = ColourObjective(target: 10);

            Assert.IsFalse(objective.ApplyPowerUpColourCleared(Tally((OTHER, 5))));
            Assert.IsFalse(objective.ApplyPowerUpColourCleared(null));
            Assert.AreEqual(0, objective.CurrentValue);
        }

        [Test]
        public void ApplyPowerUpColourCleared_OnAnotherObjectiveType_ChangesNothing()
        {
            var objective = new ObjectiveProgress(new ObjectiveDefinition(
                "lines", ObjectiveType.AtLeastLineClear, ObjectiveScope.PerRun, 3, requiredLineCount: 1));

            Assert.IsFalse(objective.ApplyPowerUpColourCleared(Tally((WANTED, 3))));
        }

        /// <summary>
        /// AC7. Progress is keyed on the colour id alone: the same tally credits the same amount
        /// whichever theme is "active", and a theme switch between two placements neither resets nor
        /// rescales what was earned. The objective never sees a ThemeDefinition at all — the two
        /// themes here exist only to prove nothing about them can reach it.
        /// </summary>
        [Test]
        public void Progress_IsAPureFunctionOfColourId_IndependentOfTheme()
        {
            ThemeDefinition themeA = ScriptableObject.CreateInstance<ThemeDefinition>();
            ThemeDefinition themeB = ScriptableObject.CreateInstance<ThemeDefinition>();
            try
            {
                ObjectiveProgress underA = ColourObjective(target: 10);
                ObjectiveProgress underB = ColourObjective(target: 10);
                ObjectiveProgress switched = ColourObjective(target: 10);

                underA.ApplyPlacement(Placement(Tally((WANTED, 4))));
                underB.ApplyPlacement(Placement(Tally((WANTED, 4))));

                switched.ApplyPlacement(Placement(Tally((WANTED, 4))));
                // "Theme switch" happens here: the objective holds no reference to either theme, and the
                // fills the two would render colour 2 with are irrelevant to the next placement.
                Assert.AreNotSame(themeA, themeB);
                switched.ApplyPlacement(Placement(Tally((WANTED, 3))));

                Assert.AreEqual(4, underA.CurrentValue);
                Assert.AreEqual(4, underB.CurrentValue);
                Assert.AreEqual(7, switched.CurrentValue);
            }
            finally
            {
                Object.DestroyImmediate(themeA);
                Object.DestroyImmediate(themeB);
            }
        }

        // --- Palette ---

        [Test]
        public void Palette_IsFiveColours_AndTheThreeConstantsAgree()
        {
            Assert.AreEqual(5, Board.COLOUR_COUNT);
            Assert.AreEqual(Board.COLOUR_COUNT, WeightedPieceDraw.COLOUR_COUNT);
            Assert.AreEqual(Board.COLOUR_COUNT, ThemeDefinition.KIND_COUNT);
        }

        [Test]
        public void DrawColourId_OverManyDraws_StaysInsideOneToFive()
        {
            var draw = new WeightedPieceDraw(seed: 7);
            var seen = new bool[Board.COLOUR_COUNT + 1];

            for (int drawIndex = 0; drawIndex < 2000; drawIndex++)
            {
                int colourId = draw.DrawColourId();
                Assert.GreaterOrEqual(colourId, 1);
                Assert.LessOrEqual(colourId, Board.COLOUR_COUNT);
                seen[colourId] = true;
            }

            // Every colour of the widened palette is actually reachable, not just the first three.
            for (int colourId = 1; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                Assert.IsTrue(seen[colourId], $"Colour {colourId} was never drawn.");
            }
        }

        [Test]
        public void ThemeDefinition_Default_HasAFillForEveryColourIdWithoutFallingBackToEmpty()
        {
            ThemeDefinition theme = ScriptableObject.CreateInstance<ThemeDefinition>();
            try
            {
                for (int colourId = 1; colourId <= Board.COLOUR_COUNT; colourId++)
                {
                    Assert.AreNotEqual(theme.EmptyCellFill, theme.GetFill(colourId), $"Colour {colourId} fell back.");
                    Assert.AreNotEqual(theme.EmptyCellFill, theme.GetHighlight(colourId));
                    Assert.AreNotEqual(theme.EmptyCellOutline, theme.GetShade(colourId));
                }
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        // --- Authoring ---

        [Test]
        public void LevelObjectiveConfig_ColourCleared_BuildsADefinitionCarryingTheColourId()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_objectiveType\":18,\"_targetValue\":8,\"_requiredColourId\":3}");

            Assert.IsTrue(config.IsValid(out string error), error);
            ObjectiveDefinition definition = config.ToObjectiveDefinition();
            Assert.AreEqual(ObjectiveType.ColourCleared, definition.Type);
            Assert.AreEqual(3, definition.RequiredColourId);
        }

        [TestCase(0)]
        [TestCase(6)]
        [TestCase(-2)]
        public void LevelObjectiveConfig_ColourCleared_WithAColourOutsideThePalette_IsInvalid(int colourId)
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_objectiveType\":18,\"_targetValue\":8,\"_requiredColourId\":{colourId}}}");

            Assert.IsFalse(config.IsValid(out string error));
            StringAssert.Contains("colour id", error);
        }

        [Test]
        public void LevelObjectiveConfig_ValidateInEditor_ClampsTheColourIdIntoThePalette()
        {
            LevelObjectiveConfig low = ARow("{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredColourId\":0}");
            LevelObjectiveConfig high = ARow("{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredColourId\":9}");

            low.ValidateInEditor();
            high.ValidateInEditor();

            Assert.AreEqual(1, low.RequiredColourId);
            Assert.AreEqual(Board.COLOUR_COUNT, high.RequiredColourId);
        }

        [Test]
        public void LevelObjectiveConfig_AnotherType_IgnoresTheColourIdEntirely()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_objectiveType\":3,\"_targetValue\":1,\"_requiredColourId\":9}");

            Assert.IsTrue(config.IsValid(out _));
            Assert.AreEqual(0, config.ToObjectiveDefinition().RequiredColourId);
        }

        [Test]
        public void ObjectiveDefinition_ColourCleared_RejectsAColourOutsideThePalette()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ObjectiveDefinition(
                "bad", ObjectiveType.ColourCleared, ObjectiveScope.PerRun, 1, requiredColourId: Board.COLOUR_COUNT + 1));
        }

        // --- Helpers ---

        private static ObjectiveProgress ColourObjective(int target)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                "colour", ObjectiveType.ColourCleared, ObjectiveScope.PerRun, target, requiredColourId: WANTED));
        }

        private static int[] Tally(params (int ColourId, int Count)[] entries)
        {
            var tally = new int[ColourTally.LENGTH];
            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                tally[entries[entryIndex].ColourId] = entries[entryIndex].Count;
            }

            return tally;
        }

        private static ObjectivePlacementContext Placement(int[] tally, int linesCleared = 1)
        {
            return new ObjectivePlacementContext(
                linesCleared, linesCleared, 0, PieceFamily.Single, null, 0, false, 0, 0,
                false, false, false, 0f, 0, tally);
        }

        private static LevelObjectiveConfig ARow(string json)
        {
            var config = new LevelObjectiveConfig();
            JsonUtility.FromJsonOverwrite(json, config);
            return config;
        }
    }
}
