using System.Collections.Generic;
using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Cover for the diamond mechanic's level-authoring slice (issue #396): the per-level spawn chance
    /// and decorated-count range on <see cref="LevelObjectiveConfig"/>, <see cref="DiamondPieceDecorator"/>
    /// honouring them (and falling back to the #390 defaults without them), a level composing several
    /// colour-scoped <see cref="ObjectiveType.DiamondsCleared"/> rows, and the one shipped level that
    /// uses all of it.
    /// <para>
    /// Every decorator here runs on a fixed seed, so each "over N draws" assertion is a replay.
    /// </para>
    /// </summary>
    public class DiamondLevelAuthoringTests
    {
        private const int RED = 1;
        private const int GREEN = 2;
        private const int BLUE = 5;
        private const int SEED = 11;
        private const int MANY_DRAWS = 400;
        private const int LEVEL = 13;

        private const string SHIPPED_CATALOG_PATH = "Assets/Content/Levels/LevelCatalog.asset";

        private static readonly Piece Domino = new Piece(
            "test_domino", new[] { new GridPosition(0, 0), new GridPosition(1, 0) });

        private static readonly Piece Square = new Piece(
            "test_square",
            new[] { new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(0, 1), new GridPosition(1, 1) });

        private static readonly Piece Line5 = new Piece(
            "test_line5",
            new[]
            {
                new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(2, 0),
                new GridPosition(3, 0), new GridPosition(4, 0),
            });

        // --- The config (AC1) ---

        [Test]
        public void LevelObjectiveConfig_Defaults_AreTheRateAndRangeOfEveryLevelAuthoredBeforeTheFields()
        {
            LevelObjectiveConfig row = ACatalogOf(DiamondRow(LEVEL, RED)).Find(LEVEL);

            Assert.AreEqual(LevelObjectiveConfig.DEFAULT_DIAMOND_DECORATION_CHANCE, row.DiamondDecorationChance);
            Assert.AreEqual(0.2f, row.DiamondDecorationChance, 0.0001f, "The ~20% of #390.");
            Assert.AreEqual(1, row.DiamondMinDecoratedCells);
            Assert.AreEqual(LevelObjectiveConfig.DIAMOND_MAX_DECORATED_CELLS_UNCAPPED, row.DiamondMaxDecoratedCells);
            Assert.IsTrue(row.IsValid(out string error), error);
        }

        [Test]
        public void LevelObjectiveConfig_ReadsTheAuthoredDiamondFields()
        {
            LevelObjectiveConfig row = ACatalogOf(DiamondRow(LEVEL, RED, chance: 0.35f, min: 2, max: 3)).Find(LEVEL);

            Assert.AreEqual(0.35f, row.DiamondDecorationChance, 0.0001f);
            Assert.AreEqual(2, row.DiamondMinDecoratedCells);
            Assert.AreEqual(3, row.DiamondMaxDecoratedCells);
            Assert.IsTrue(row.IsValid(out string error), error);
        }

        [TestCase(-0.1f, 1, 0)]
        [TestCase(1.1f, 1, 0)]
        [TestCase(0.2f, 0, 0)]
        [TestCase(0.2f, 3, 2)]
        public void LevelObjectiveConfig_RefusesAnImpossibleDiamondRange(float chance, int min, int max)
        {
            LevelObjectiveConfig row = ACatalogOf(DiamondRow(LEVEL, RED, chance, min, max)).Find(LEVEL);

            Assert.IsFalse(row.IsValid(out string error));
            StringAssert.Contains("Diamond", error);
        }

        [Test]
        public void LevelObjectiveConfig_AMaxOfZero_MeansUncappedAndIsValidWithAnyMin()
        {
            LevelObjectiveConfig row = ACatalogOf(DiamondRow(LEVEL, RED, chance: 0.5f, min: 4, max: 0)).Find(LEVEL);

            Assert.IsTrue(row.IsValid(out string error), error);
        }

        // --- The decorator reading it (AC4 of the slice) ---

        [Test]
        public void TryDecorate_WithAnAuthoredChanceOfOne_DecoratesEveryMultiCellDraw()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(DiamondRow(LEVEL, RED, chance: 1f));

            Assert.AreEqual(MANY_DRAWS, CountDecoratedDraws(decorator, Square, MANY_DRAWS));
            Assert.AreEqual(0, CountDecoratedDraws(decorator, PieceCatalog.SingleCell, MANY_DRAWS),
                "A 1x1 is never decorated, however high the chance.");
        }

        [Test]
        public void TryDecorate_WithAnAuthoredChanceOfZero_NeverDecorates()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(DiamondRow(LEVEL, RED, chance: 0f));

            Assert.IsTrue(decorator.IsActive, "The gate is open; only the roll says no.");
            Assert.AreEqual(0, CountDecoratedDraws(decorator, Square, MANY_DRAWS));
        }

        [Test]
        public void TryDecorate_WithAnAuthoredChance_DecoratesRoughlyThatShare()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(DiamondRow(LEVEL, RED, chance: 0.5f));

            const int draws = 2000;
            double share = CountDecoratedDraws(decorator, Square, draws) / (double)draws;

            Assert.That(share, Is.InRange(0.42, 0.58), $"Decorated share was {share:P1}.");
        }

        [Test]
        public void TryDecorate_WithAnAuthoredMinAndMax_KeepsEveryCountInsideThem()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(DiamondRow(LEVEL, RED, chance: 1f, min: 2, max: 3));

            var buffer = new int[Line5.CellCount];
            bool sawTwo = false;
            bool sawThree = false;
            for (int draw = 0; draw < MANY_DRAWS; draw++)
            {
                Assert.IsTrue(decorator.TryDecorate(Line5, buffer));
                int count = CountGems(buffer, Line5.CellCount);
                Assert.That(count, Is.InRange(2, 3), $"Draw {draw} carried {count} gems.");
                sawTwo |= count == 2;
                sawThree |= count == 3;
            }

            Assert.IsTrue(sawTwo && sawThree, "Both ends of the authored range should come up.");
        }

        [Test]
        public void TryDecorate_WithMinEqualToMax_DealsExactlyThatMany()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(DiamondRow(LEVEL, RED, chance: 1f, min: 2, max: 2));

            var buffer = new int[Square.CellCount];
            for (int draw = 0; draw < MANY_DRAWS; draw++)
            {
                Assert.IsTrue(decorator.TryDecorate(Square, buffer));
                Assert.AreEqual(2, CountGems(buffer, Square.CellCount));
            }
        }

        /// <summary>The <c>1..CellCount-1</c> band is a rule of the mechanic, not a default: a range
        /// authored wider than the piece is clamped into it rather than honoured.</summary>
        [Test]
        public void TryDecorate_ClampsAnAuthoredRangeToThePiece_SoNoPieceIsEverFullyDecorated()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(DiamondRow(LEVEL, RED, chance: 1f, min: 3, max: 6));

            var buffer = new int[Square.CellCount];
            for (int draw = 0; draw < MANY_DRAWS; draw++)
            {
                Assert.IsTrue(decorator.TryDecorate(Domino, buffer));
                Assert.AreEqual(1, CountGems(buffer, Domino.CellCount), "A domino can only ever carry one.");

                Assert.IsTrue(decorator.TryDecorate(Square, buffer));
                Assert.AreEqual(3, CountGems(buffer, Square.CellCount), "min 3 on a 4-cell piece is exactly 3.");
            }
        }

        [Test]
        public void TryDecorate_WithAnUncappedMax_GoesUpToEveryCellButOne()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(DiamondRow(LEVEL, RED, chance: 1f, min: 1, max: 0));

            var buffer = new int[Line5.CellCount];
            int highest = 0;
            for (int draw = 0; draw < MANY_DRAWS; draw++)
            {
                Assert.IsTrue(decorator.TryDecorate(Line5, buffer));
                int count = CountGems(buffer, Line5.CellCount);
                Assert.That(count, Is.InRange(1, Line5.CellCount - 1));
                highest = Mathf.Max(highest, count);
            }

            Assert.AreEqual(Line5.CellCount - 1, highest, "Uncapped reaches the top of the band.");
        }

        /// <summary>The tunables belong to the level, so they come from its first row — the one
        /// <see cref="LevelCatalog.Find"/> returns — however many colour rows follow it.</summary>
        [Test]
        public void TryDecorate_ReadsTheTunablesFromTheLevelsFirstRowOnly()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(
                DiamondRow(LEVEL, RED, chance: 1f),
                DiamondRow(LEVEL, BLUE, chance: 0f),
                DiamondRow(LEVEL, GREEN, chance: 0f));

            Assert.AreEqual(MANY_DRAWS, CountDecoratedDraws(decorator, Square, MANY_DRAWS));
        }

        [Test]
        public void TryDecorate_WithNoActivePathLevel_FallsBackToTheDefaultRate()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(
                new PathRunModel(), activeLevel: PathRunModel.NO_ACTIVE_LEVEL, DiamondRow(LEVEL, RED, chance: 1f));

            const int draws = 2000;
            double share = CountDecoratedDraws(decorator, Square, draws) / (double)draws;

            Assert.That(share, Is.InRange(0.12, 0.28), $"Decorated share was {share:P1}, not the ~20% default.");
        }

        [Test]
        public void TryDecorate_WithAnActiveLevelTheCatalogDoesNotAuthor_FallsBackToTheDefaultRate()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(
                new PathRunModel(), activeLevel: LEVEL + 1, DiamondRow(LEVEL, RED, chance: 1f));

            const int draws = 2000;
            double share = CountDecoratedDraws(decorator, Square, draws) / (double)draws;

            Assert.That(share, Is.InRange(0.12, 0.28), $"Decorated share was {share:P1}, not the ~20% default.");
        }

        /// <summary>The seeded constructor without a catalog is what every pre-#396 test builds; it must
        /// keep dealing the default rate rather than, say, refusing to decorate at all.</summary>
        [Test]
        public void TryDecorate_WithoutACatalogInjected_FallsBackToTheDefaultRate()
        {
            var objectiveModel = new ObjectiveModel();
            objectiveModel.SetCurrentObjective(DiamondObjective(RED));
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Path;
            var decorator = new DiamondPieceDecorator(objectiveModel, gameModeModel, SEED);

            const int draws = 2000;
            double share = CountDecoratedDraws(decorator, Square, draws) / (double)draws;

            Assert.That(share, Is.InRange(0.12, 0.28), $"Decorated share was {share:P1}.");
        }

        // --- Three colours as three rows (AC2) ---

        [Test]
        public void FindAll_WithThreeDiamondRowsOfDifferentColours_ComposesThreeDistinctObjectives()
        {
            LevelCatalog catalog = ACatalogOf(
                DiamondRow(LEVEL, RED), DiamondRow(LEVEL, BLUE), DiamondRow(LEVEL, GREEN));

            IReadOnlyList<LevelObjectiveConfig> rows = catalog.FindAll(LEVEL);

            Assert.AreEqual(3, rows.Count);
            var definitions = new ObjectiveDefinition[3];
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                Assert.IsTrue(rows[rowIndex].IsValid(out string error), error);
                definitions[rowIndex] = rows[rowIndex].ToObjectiveDefinition(rowIndex);
                Assert.AreEqual(ObjectiveType.DiamondsCleared, definitions[rowIndex].Type);
                Assert.AreEqual(10, definitions[rowIndex].TargetValue);
            }

            Assert.AreEqual("level_13", definitions[0].Id);
            Assert.AreEqual("level_13_1", definitions[1].Id);
            Assert.AreEqual("level_13_2", definitions[2].Id);
            Assert.AreEqual(RED, definitions[0].RequiredColourId);
            Assert.AreEqual(BLUE, definitions[1].RequiredColourId);
            Assert.AreEqual(GREEN, definitions[2].RequiredColourId);
        }

        [Test]
        public void TryDecorate_OnAThreeColourLevel_DealsAllThreeColoursAndNothingElse()
        {
            DiamondPieceDecorator decorator = ADecoratorOnLevel(
                DiamondRow(LEVEL, RED, chance: 1f), DiamondRow(LEVEL, BLUE), DiamondRow(LEVEL, GREEN));

            var seen = new bool[Board.COLOUR_COUNT + 1];
            var buffer = new int[Line5.CellCount];
            for (int draw = 0; draw < MANY_DRAWS; draw++)
            {
                Assert.IsTrue(decorator.TryDecorate(Line5, buffer));
                for (int offsetIndex = 0; offsetIndex < buffer.Length; offsetIndex++)
                {
                    int colour = buffer[offsetIndex];
                    if (colour == TrayModel.NO_DIAMOND)
                    {
                        continue;
                    }

                    Assert.That(colour, Is.EqualTo(RED).Or.EqualTo(BLUE).Or.EqualTo(GREEN), "An unasked-for colour.");
                    seen[colour] = true;
                }
            }

            Assert.IsTrue(seen[RED] && seen[BLUE] && seen[GREEN], "Every named colour should come up.");
        }

#if UNITY_EDITOR
        // --- The shipped level (AC3) ---

        /// <summary>Level 20 introduces diamonds in the learning curve (issue #472): one colour, a small
        /// target, and gems dealt more often than the bare default so the goal is reachable.</summary>
        [Test]
        public void ShippedCatalog_Level20_IsTheDiamondIntroduction()
        {
            const int DiamondIntroLevel = 20;
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(SHIPPED_CATALOG_PATH);
            Assert.IsNotNull(catalog, $"No catalog at {SHIPPED_CATALOG_PATH}.");

            IReadOnlyList<LevelObjectiveConfig> rows = catalog.FindAll(DiamondIntroLevel);
            Assert.AreEqual(1, rows.Count, "The introduction asks for diamonds alone.");

            LevelObjectiveConfig row = rows[0];
            Assert.IsTrue(row.IsValid(out string error), error);
            Assert.AreEqual(ObjectiveType.DiamondsCleared, row.ObjectiveType);
            Assert.AreEqual(ObjectiveScope.PerRun, row.Scope);
            Assert.AreEqual(6, row.TargetValue);
            Assert.Greater(row.DiamondDecorationChance, LevelObjectiveConfig.DEFAULT_DIAMOND_DECORATION_CHANCE,
                "A diamond goal deals gems more often than the bare default.");
            Assert.LessOrEqual(row.DiamondDecorationChance, 1f);
            Assert.GreaterOrEqual(row.DiamondMinDecoratedCells, 1);
            Assert.IsTrue(
                row.DiamondMaxDecoratedCells == LevelObjectiveConfig.DIAMOND_MAX_DECORATED_CELLS_UNCAPPED
                || row.DiamondMaxDecoratedCells >= row.DiamondMinDecoratedCells);
        }

        [Test]
        public void ShippedCatalog_EveryRowIsStillValid()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(SHIPPED_CATALOG_PATH);
            Assert.IsNotNull(catalog);

            IReadOnlyList<LevelObjectiveConfig> levels = catalog.Levels;
            for (int rowIndex = 0; rowIndex < levels.Count; rowIndex++)
            {
                Assert.IsTrue(levels[rowIndex].IsValid(out string error),
                    $"Row {rowIndex} (level {levels[rowIndex].LevelNumber}): {error}");
            }
        }
#endif

        // --- Helpers ---

        private static ObjectiveProgress DiamondObjective(int colourId)
        {
            return new ObjectiveProgress(new ObjectiveDefinition(
                $"diamonds_{colourId}", ObjectiveType.DiamondsCleared, ObjectiveScope.PerRun, 10,
                requiredColourId: colourId));
        }

        /// <summary>A decorator playing <see cref="LEVEL"/> in Path mode, tracking exactly the objectives
        /// the given rows build — the state <c>LevelProgressionSystem.TryStartPathLevel</c> leaves.</summary>
        private static DiamondPieceDecorator ADecoratorOnLevel(params string[] rows)
            => ADecoratorOnLevel(new PathRunModel(), LEVEL, rows);

        private static DiamondPieceDecorator ADecoratorOnLevel(PathRunModel pathRunModel, int activeLevel, params string[] rows)
        {
            LevelCatalog catalog = ACatalogOf(rows);

            IReadOnlyList<LevelObjectiveConfig> configs = catalog.FindAll(LEVEL);
            var objectives = new List<ObjectiveProgress>(configs.Count);
            for (int configIndex = 0; configIndex < configs.Count; configIndex++)
            {
                objectives.Add(new ObjectiveProgress(configs[configIndex].ToObjectiveDefinition(configIndex)));
            }

            var objectiveModel = new ObjectiveModel();
            objectiveModel.SetObjectives(objectives);
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = GameMode.Path;
            pathRunModel.ActiveLevelNumber.Value = activeLevel;

            return new DiamondPieceDecorator(objectiveModel, gameModeModel, SEED, catalog, pathRunModel);
        }

        private static int CountDecoratedDraws(DiamondPieceDecorator decorator, Piece piece, int draws)
        {
            var buffer = new int[piece.CellCount];
            int decorated = 0;
            for (int draw = 0; draw < draws; draw++)
            {
                if (decorator.TryDecorate(piece, buffer))
                {
                    decorated++;
                    Assert.That(CountGems(buffer, piece.CellCount), Is.InRange(1, piece.CellCount - 1));
                }
            }

            return decorated;
        }

        private static int CountGems(int[] buffer, int cellCount)
        {
            int count = 0;
            for (int offsetIndex = 0; offsetIndex < cellCount; offsetIndex++)
            {
                if (buffer[offsetIndex] != TrayModel.NO_DIAMOND)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Builds a catalog from authored rows through <see cref="JsonUtility"/>, as every
        /// other catalog test does: the serialized field names are the asset's own contract.</summary>
        private static LevelCatalog ACatalogOf(params string[] levels)
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            JsonUtility.FromJsonOverwrite($"{{\"_levels\":[{string.Join(",", levels)}]}}", catalog);
            return catalog;
        }

        /// <summary>One DiamondsCleared row: 10 gems of <paramref name="colourId"/>. Diamond tunables are
        /// left out of the JSON when null, so the row takes the field defaults exactly as a row authored
        /// before the fields existed does.</summary>
        private static string DiamondRow(int levelNumber, int colourId, float? chance = null, int? min = null, int? max = null)
        {
            string row = "{"
                + $"\"_levelNumber\":{levelNumber},"
                + $"\"_objectiveType\":{(int)ObjectiveType.DiamondsCleared},"
                + $"\"_scope\":{(int)ObjectiveScope.PerRun},"
                + "\"_targetValue\":10,"
                + "\"_requiredLineCount\":1,"
                + $"\"_requiredColourId\":{colourId}";

            if (chance.HasValue)
            {
                row += $",\"_diamondDecorationChance\":{chance.Value.ToString(CultureInfo.InvariantCulture)}";
            }

            if (min.HasValue)
            {
                row += $",\"_diamondMinDecoratedCells\":{min.Value}";
            }

            if (max.HasValue)
            {
                row += $",\"_diamondMaxDecoratedCells\":{max.Value}";
            }

            return row + "}";
        }
    }
}
