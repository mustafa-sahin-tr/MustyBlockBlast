using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #513 — the family-objective piece guarantee: on a level with an incomplete
    /// <see cref="ObjectiveType.PieceFamilyCount"/> objective, every
    /// <see cref="FamilyPieceGuarantee.REFILL_WINDOW"/> normal refills hold at least one piece of that
    /// family.
    /// <para>
    /// Most tests compare a guarded system against an unguarded reference built from the same seeds:
    /// the guarantee changes nothing until it overwrites a slot, so every refill up to the first forced
    /// one must match the reference exactly. Seeds are searched for rather than hard-coded, so the
    /// tests pin the rule, not whatever the catalog's current weights happen to deal for seed 1.
    /// </para>
    /// </summary>
    public sealed class FamilyPieceGuaranteeTests
    {
        private const int BOARD_SEED = 1;
        private const int MAX_SEED_SEARCH = 5000;

        /// <summary>A 3x3 hole in the corner of an otherwise full board: below the Demolition Hammer's
        /// occupancy threshold, so a dead dock reaches the no-moves rescue rather than the hammer.</summary>
        private const int CORNER_HOLE_SIZE = 3;

        private readonly List<CurrencyConfig> _configs = new List<CurrencyConfig>();

        [TearDown]
        public void DestroyConfigs()
        {
            for (int configIndex = 0; configIndex < _configs.Count; configIndex++)
            {
                if (_configs[configIndex] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_configs[configIndex]);
                }
            }

            _configs.Clear();
        }

        // --- AC1: forced on the window's last refill ---

        [Test]
        public void NoSquareInRefillsOneToFour_RefillFiveHoldsASquare()
        {
            int drawSeed = FindSeed(refills => !AnyHolds(refills, PieceFamily.Square, 1, 4));

            Harness reference = new Harness(this, drawSeed, withGuarantee: false);
            Harness guarded = new Harness(this, drawSeed, withGuarantee: true, PieceFamily.Square);
            List<Piece[]> expected = reference.PlayRefills(4);
            List<Piece[]> actual = guarded.PlayRefills(5);

            for (int refillIndex = 0; refillIndex < 4; refillIndex++)
            {
                AssertSameDock(expected[refillIndex], actual[refillIndex], refillIndex + 1);
            }

            Assert.IsTrue(Holds(actual[4], PieceFamily.Square), "Refill 5 must be forced to hold a Square.");
        }

        /// <summary>AC3: the forced piece replaces exactly one slot — the last — and the draw's other
        /// two slots are what an unguarded draw would have dealt.</summary>
        [Test]
        public void TheForcedSquare_ReplacesOnlyTheLastSlot()
        {
            int drawSeed = FindSeed(refills => !AnyHolds(refills, PieceFamily.Square, 1, 5));

            Harness reference = new Harness(this, drawSeed, withGuarantee: false);
            Harness guarded = new Harness(this, drawSeed, withGuarantee: true, PieceFamily.Square);
            List<Piece[]> expected = reference.PlayRefills(5);
            List<Piece[]> actual = guarded.PlayRefills(5);

            Assert.AreSame(expected[4][0], actual[4][0]);
            Assert.AreSame(expected[4][1], actual[4][1]);
            Assert.AreEqual(PieceFamily.Square, PieceFamilyClassifier.Classify(actual[4][2].Id));
        }

        // --- AC2: a natural appearance restarts the window ---

        [Test]
        public void ASquareOnRefillTwo_NoForcingThroughRefillSix_ForcedOnRefillSeven()
        {
            int drawSeed = FindSeed(refills =>
                Holds(refills[1], PieceFamily.Square) && !AnyHolds(refills, PieceFamily.Square, 3, 6), 6);

            Harness reference = new Harness(this, drawSeed, withGuarantee: false);
            Harness guarded = new Harness(this, drawSeed, withGuarantee: true, PieceFamily.Square);
            List<Piece[]> expected = reference.PlayRefills(6);
            List<Piece[]> actual = guarded.PlayRefills(7);

            for (int refillIndex = 0; refillIndex < 6; refillIndex++)
            {
                AssertSameDock(expected[refillIndex], actual[refillIndex], refillIndex + 1);
            }

            Assert.IsTrue(Holds(actual[6], PieceFamily.Square), "Refill 7 must be forced to hold a Square.");
        }

        // --- AC4: independent families, slot contention ---

        [Test]
        public void TwoFamiliesDueTogether_BothGetASlot()
        {
            int drawSeed = FindSeed(refills =>
                !AnyHolds(refills, PieceFamily.Square, 1, 4) && !AnyHolds(refills, PieceFamily.SShape, 1, 4));

            Harness guarded = new Harness(
                this, drawSeed, withGuarantee: true, PieceFamily.Square, PieceFamily.SShape);
            List<Piece[]> actual = guarded.PlayRefills(5);

            Assert.IsTrue(Holds(actual[4], PieceFamily.Square));
            Assert.IsTrue(Holds(actual[4], PieceFamily.SShape));
        }

        [Test]
        public void TwoFamilies_KeepIndependentCounters()
        {
            int drawSeed = FindSeed(refills =>
                Holds(refills[1], PieceFamily.Square)
                && !AnyHolds(refills, PieceFamily.Square, 3, 4)
                && !AnyHolds(refills, PieceFamily.SShape, 1, 4));

            Harness guarded = new Harness(
                this, drawSeed, withGuarantee: true, PieceFamily.Square, PieceFamily.SShape);
            guarded.PlayRefills(4);

            Assert.AreEqual(2, guarded.Guarantee.RefillsWithout(PieceFamily.Square));
            Assert.AreEqual(4, guarded.Guarantee.RefillsWithout(PieceFamily.SShape));
            Assert.IsFalse(guarded.Guarantee.IsDue(PieceFamily.Square));
            Assert.IsTrue(guarded.Guarantee.IsDue(PieceFamily.SShape));

            Piece[] fifth = guarded.PlayRefill();
            Assert.IsTrue(Holds(fifth, PieceFamily.SShape));
        }

        /// <summary>AC4: an earned special piece keeps its slot, and a due family that cannot get one
        /// stays due and is paid on the next refill.</summary>
        [Test]
        public void MoreDueFamiliesThanFreeSlots_TheInjectionWins_AndTheLeftoverIsPaidNextRefill()
        {
            PieceFamily[] families = { PieceFamily.Square, PieceFamily.SShape, PieceFamily.ZShape };
            Harness guarded = new Harness(this, drawSeed: 7, withGuarantee: true, families);
            guarded.PlayRefill();
            PrimeAllDue(guarded.Guarantee);

            guarded.Sut.RequestGoldenPieceInjection();
            Piece[] dock = guarded.PlayRefill();

            Assert.AreEqual(SpecialPieceKind.Golden, guarded.TrayModel.GetSpecialKind(0));
            int dealtCount = 0;
            PieceFamily leftover = PieceFamily.Single;
            for (int familyIndex = 0; familyIndex < families.Length; familyIndex++)
            {
                if (Holds(dock, families[familyIndex]))
                {
                    dealtCount++;
                }
                else
                {
                    leftover = families[familyIndex];
                }
            }

            Assert.AreEqual(2, dealtCount, "Two free slots, three due families: two are paid.");
            Assert.IsTrue(guarded.Guarantee.IsDue(leftover), "The unpaid family stays due.");

            Piece[] next = guarded.PlayRefill();
            Assert.IsTrue(Holds(next, leftover), "The leftover guarantee is paid on the next refill.");
        }

        // --- AC6: a completed objective is no longer forced ---

        [Test]
        public void ACompletedFamilyObjective_IsNeverForced()
        {
            int drawSeed = FindSeed(refills => !AnyHolds(refills, PieceFamily.Square, 1, 5));

            Harness reference = new Harness(this, drawSeed, withGuarantee: false);
            Harness guarded = new Harness(this, drawSeed, withGuarantee: true, PieceFamily.Square);
            ObjectiveProgress objective = guarded.ObjectiveModel.TrackedObjectives[0];
            objective.RestoreProgress(objective.Definition.TargetValue);
            Assert.IsTrue(objective.IsComplete);

            List<Piece[]> expected = reference.PlayRefills(5);
            List<Piece[]> actual = guarded.PlayRefills(5);

            for (int refillIndex = 0; refillIndex < 5; refillIndex++)
            {
                AssertSameDock(expected[refillIndex], actual[refillIndex], refillIndex + 1);
            }
        }

        // --- AC5: a level without a family objective is untouched ---

        [Test]
        public void ALevelWithoutAFamilyObjective_DealsExactlyWhatAnUnguardedDrawDeals(
            [Values(1, 42, 1234)] int drawSeed)
        {
            Harness reference = new Harness(this, drawSeed, withGuarantee: false);
            Harness guarded = new Harness(this, drawSeed, withGuarantee: true);
            guarded.ObjectiveModel.SetCurrentObjective(new ObjectiveProgress(
                new ObjectiveDefinition("score", ObjectiveType.ScoreInRun, ObjectiveScope.PerRun, 1000)));

            List<Piece[]> expected = reference.PlayRefills(20);
            List<Piece[]> actual = guarded.PlayRefills(20);

            for (int refillIndex = 0; refillIndex < expected.Count; refillIndex++)
            {
                AssertSameDock(expected[refillIndex], actual[refillIndex], refillIndex + 1);
            }
        }

        // --- AC7: reroll and rescue leave every counter alone ---

        [Test]
        public void Reroll_NeverChangesAFamilyCounter_EvenWhenItDealsThatFamily()
        {
            Harness guarded = new Harness(this, drawSeed: 3, withGuarantee: true, PieceFamily.Square);
            guarded.PlayRefills(2);
            int[] before = SnapshotCounters(guarded.Guarantee);

            bool dealtASquare = false;
            for (int rerollIndex = 0; rerollIndex < 500 && !dealtASquare; rerollIndex++)
            {
                Assert.IsTrue(guarded.Sut.TryRerollTray(out bool _));
                dealtASquare = Holds(guarded.SnapshotDock(), PieceFamily.Square);
                AssertCountersEqual(before, SnapshotCounters(guarded.Guarantee));
            }

            Assert.IsTrue(dealtASquare, "The search should have produced a reroll that deals a Square.");
        }

        [Test]
        public void TheNoMovesRescue_NeverChangesAFamilyCounter()
        {
            Harness guarded = new Harness(
                this, drawSeed: 3, withGuarantee: true, new StubRescueRewardSource(), PieceFamily.Square);
            guarded.PlayRefills(2);
            int[] before = SnapshotCounters(guarded.Guarantee);

            guarded.LayOutADeadBoardBelowTheHammerThreshold();
            guarded.Sut.RecheckGameOver();
            Assert.IsTrue(guarded.Sut.IsGameOver);

            bool rescued = guarded.Sut.TryApplyNoMovesRescueAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(rescued);
            AssertCountersEqual(before, SnapshotCounters(guarded.Guarantee));
        }

        // --- AC8: counters reset with every run ---

        [Test]
        public void StartNewRun_OpensAFreshWindow()
        {
            int drawSeed = FindSeed(refills => !AnyHolds(refills, PieceFamily.Square, 1, 4));
            Harness guarded = new Harness(this, drawSeed, withGuarantee: true, PieceFamily.Square);
            guarded.PlayRefills(4);
            Assert.IsTrue(guarded.Guarantee.IsDue(PieceFamily.Square));

            guarded.Sut.StartNewRun();

            Assert.LessOrEqual(guarded.Guarantee.RefillsWithout(PieceFamily.Square), 1);
            Assert.IsFalse(guarded.Guarantee.IsDue(PieceFamily.Square));
        }

        /// <summary>The run's opening refill happens before RunStartedMessage resets the previous run's
        /// progress, so it may read a stale IsComplete. That must not matter: the counts never read an
        /// objective, and the opening refill is never due.</summary>
        [Test]
        public void AStaleCompleteObjectiveAtRunStart_DoesNotDelayTheWindow()
        {
            int drawSeed = FindSeed(refills => !AnyHolds(refills, PieceFamily.Square, 1, 4));
            Harness guarded = new Harness(this, drawSeed, withGuarantee: true, PieceFamily.Square);
            ObjectiveProgress objective = guarded.ObjectiveModel.TrackedObjectives[0];
            objective.RestoreProgress(objective.Definition.TargetValue);

            guarded.Sut.StartNewRun();
            objective.ResetForNewRun();

            Assert.AreEqual(1, guarded.Guarantee.RefillsWithout(PieceFamily.Square));
        }

        // --- WeightedPieceDraw.DrawPieceOfFamily ---

        [Test]
        public void EveryFamily_HasCatalogPieces()
        {
            for (int familyIndex = 0; familyIndex < PieceFamilyClassifier.FamilyCount; familyIndex++)
            {
                bool found = false;
                for (int pieceIndex = 0; pieceIndex < PieceCatalog.AllPieces.Count && !found; pieceIndex++)
                {
                    found = (int)PieceFamilyClassifier.Classify(PieceCatalog.AllPieces[pieceIndex].Id) == familyIndex;
                }

                Assert.IsTrue(found, $"{(PieceFamily)familyIndex} has no catalog piece.");
            }
        }

        [Test]
        public void DrawPieceOfFamily_OnlyEverDealsThatFamily()
        {
            WeightedPieceDraw draw = new WeightedPieceDraw(seed: 11);
            for (int familyIndex = 0; familyIndex < PieceFamilyClassifier.FamilyCount; familyIndex++)
            {
                PieceFamily family = (PieceFamily)familyIndex;
                for (int drawIndex = 0; drawIndex < 200; drawIndex++)
                {
                    Piece piece = draw.DrawPieceOfFamily(family);
                    Assert.AreEqual(family, PieceFamilyClassifier.Classify(piece.Id));
                }
            }
        }

        [Test]
        public void DrawPieceOfFamily_KeepsTheSizeWeighting()
        {
            WeightedPieceDraw draw = new WeightedPieceDraw(seed: 5);
            int smallSquares = 0;
            int largeSquares = 0;
            for (int drawIndex = 0; drawIndex < 2000; drawIndex++)
            {
                Piece piece = draw.DrawPieceOfFamily(PieceFamily.Square);
                if (piece.CellCount == 4)
                {
                    smallSquares++;
                }
                else
                {
                    largeSquares++;
                }
            }

            Assert.Greater(largeSquares, 0, "The 3x3 is rarer, not excluded.");
            Assert.Greater(smallSquares, largeSquares * 3, "The 2x2 must stay far likelier than the 3x3.");
        }

        // --- helpers ---

        /// <summary>First draw seed whose unguarded refills satisfy <paramref name="predicate"/>.</summary>
        private int FindSeed(Func<List<Piece[]>, bool> predicate, int refillCount = 5)
        {
            for (int drawSeed = 0; drawSeed < MAX_SEED_SEARCH; drawSeed++)
            {
                Harness reference = new Harness(this, drawSeed, withGuarantee: false);
                if (predicate(reference.PlayRefills(refillCount)))
                {
                    return drawSeed;
                }
            }

            Assert.Fail("No seed satisfied the precondition.");
            return -1;
        }

        /// <summary>Whether any refill numbered <paramref name="firstRefill"/> to
        /// <paramref name="lastRefill"/> (1-based, inclusive) holds <paramref name="family"/>.</summary>
        private static bool AnyHolds(List<Piece[]> refills, PieceFamily family, int firstRefill, int lastRefill)
        {
            for (int refillNumber = firstRefill; refillNumber <= lastRefill; refillNumber++)
            {
                if (Holds(refills[refillNumber - 1], family))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Holds(Piece[] dock, PieceFamily family)
        {
            for (int slotIndex = 0; slotIndex < dock.Length; slotIndex++)
            {
                if (dock[slotIndex] != null && PieceFamilyClassifier.Classify(dock[slotIndex].Id) == family)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertSameDock(Piece[] expected, Piece[] actual, int refillNumber)
        {
            for (int slotIndex = 0; slotIndex < expected.Length; slotIndex++)
            {
                Assert.AreSame(expected[slotIndex], actual[slotIndex], $"Refill {refillNumber}, slot {slotIndex}.");
            }
        }

        private static void PrimeAllDue(FamilyPieceGuarantee guarantee)
        {
            TrayModel singles = new TrayModel();
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                singles.SetSlot(slotIndex, PieceCatalog.SingleCell, 1);
            }

            for (int refillIndex = 0; refillIndex < FamilyPieceGuarantee.REFILL_WINDOW; refillIndex++)
            {
                guarantee.RecordRefill(singles);
            }
        }

        private static int[] SnapshotCounters(FamilyPieceGuarantee guarantee)
        {
            int[] counters = new int[PieceFamilyClassifier.FamilyCount];
            for (int familyIndex = 0; familyIndex < counters.Length; familyIndex++)
            {
                counters[familyIndex] = guarantee.RefillsWithout((PieceFamily)familyIndex);
            }

            return counters;
        }

        private static void AssertCountersEqual(int[] expected, int[] actual)
        {
            for (int familyIndex = 0; familyIndex < expected.Length; familyIndex++)
            {
                Assert.AreEqual(expected[familyIndex], actual[familyIndex], $"{(PieceFamily)familyIndex} counter.");
            }
        }

        /// <summary>One board, dock and (optionally) guarantee, driven one refill at a time.</summary>
        private sealed class Harness
        {
            private bool _started;

            internal Harness(FamilyPieceGuaranteeTests owner, int drawSeed, bool withGuarantee, params PieceFamily[] families)
                : this(owner, drawSeed, withGuarantee, null, families)
            {
            }

            internal Harness(
                FamilyPieceGuaranteeTests owner,
                int drawSeed,
                bool withGuarantee,
                IRescueRewardSource rescueRewardSource,
                params PieceFamily[] families)
            {
                BoardModel = new BoardModel();
                TrayModel = new TrayModel();
                ObjectiveModel = new ObjectiveModel();

                List<ObjectiveProgress> objectives = new List<ObjectiveProgress>();
                for (int familyIndex = 0; familyIndex < families.Length; familyIndex++)
                {
                    objectives.Add(new ObjectiveProgress(new ObjectiveDefinition(
                        "family_" + familyIndex, ObjectiveType.PieceFamilyCount, ObjectiveScope.PerRun, 10,
                        requiredPieceFamily: families[familyIndex])));
                }

                ObjectiveModel.SetObjectives(objectives);
                Guarantee = withGuarantee ? new FamilyPieceGuarantee(ObjectiveModel) : null;

                CurrencyConfig currencyConfig = ScriptableObject.CreateInstance<CurrencyConfig>();
                owner._configs.Add(currencyConfig);

                Sut = new BoardSystem(
                    BoardModel,
                    TrayModel,
                    new ScoreGemProgressModel(),
                    new VortexProgressModel(),
                    new WeightedPieceDraw(drawSeed),
                    new TestMessageBroker<RunStartedMessage>(),
                    new TestMessageBroker<PiecePlacedMessage>(),
                    new TestMessageBroker<LinesClearedMessage>(),
                    new TestMessageBroker<GameOverMessage>(),
                    new TestMessageBroker<TrayRefilledMessage>(),
                    new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                    new TestMessageBroker<LaserFiredMessage>(),
                    new TestMessageBroker<PiercingRocketFiredMessage>(),
                    new TestMessageBroker<VortexIslandFilledMessage>(),
                    new TestMessageBroker<ChainLightningTriggeredMessage>(),
                    new TestMessageBroker<CoinCellsClearedMessage>(),
                    currencyConfig,
                    seed: BOARD_SEED,
                    powerUpModel: new PowerUpModel(),
                    gameModeModel: new GameModeModel(),
                    rescueRewardSource: rescueRewardSource,
                    runRescuedPublisher: new TestMessageBroker<RunRescuedMessage>(),
                    familyPieceGuarantee: Guarantee);
            }

            internal BoardModel BoardModel { get; }
            internal TrayModel TrayModel { get; }
            internal ObjectiveModel ObjectiveModel { get; }
            internal FamilyPieceGuarantee Guarantee { get; }
            internal BoardSystem Sut { get; }

            /// <summary>Refill 1 is the run's opening deal; every later one is forced by emptying the
            /// dock and placing a lone 1x1 on an emptied board, which is a normal refill.</summary>
            internal Piece[] PlayRefill()
            {
                if (!_started)
                {
                    _started = true;
                    Sut.StartNewRun();
                    return SnapshotDock();
                }

                BoardModel.ClearAll();
                for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
                {
                    TrayModel.SetSlot(slotIndex, null, Board.EMPTY);
                }

                TrayModel.SetSlot(0, PieceCatalog.SingleCell, 1);
                Assert.IsTrue(Sut.TryPlacePiece(0, new GridPosition(0, 0)));
                return SnapshotDock();
            }

            internal List<Piece[]> PlayRefills(int count)
            {
                List<Piece[]> refills = new List<Piece[]>(count);
                for (int refillIndex = 0; refillIndex < count; refillIndex++)
                {
                    refills.Add(PlayRefill());
                }

                return refills;
            }

            internal Piece[] SnapshotDock()
            {
                Piece[] dock = new Piece[TrayModel.SLOT_COUNT];
                for (int slotIndex = 0; slotIndex < dock.Length; slotIndex++)
                {
                    dock[slotIndex] = TrayModel.GetPiece(slotIndex);
                }

                return dock;
            }

            internal void LayOutADeadBoardBelowTheHammerThreshold()
            {
                BoardModel.ClearAll();
                for (int y = 0; y < Board.SIZE; y++)
                {
                    for (int x = 0; x < Board.SIZE; x++)
                    {
                        if (x >= CORNER_HOLE_SIZE || y >= CORNER_HOLE_SIZE)
                        {
                            BoardModel.Occupy(new GridPosition(x, y), 1);
                        }
                    }
                }

                Piece bar = FindPiece("line_h5");
                for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
                {
                    TrayModel.SetSlot(slotIndex, bar, 1);
                }
            }

            private static Piece FindPiece(string id)
            {
                for (int pieceIndex = 0; pieceIndex < PieceCatalog.AllPieces.Count; pieceIndex++)
                {
                    if (PieceCatalog.AllPieces[pieceIndex].Id == id)
                    {
                        return PieceCatalog.AllPieces[pieceIndex];
                    }
                }

                Assert.Fail($"No catalog piece {id}.");
                return null;
            }
        }

        private sealed class StubRescueRewardSource : IRescueRewardSource
        {
            public UniTask<RescueRewardResult> RequestRescueRewardAsync(CancellationToken cancellationToken)
                => UniTask.FromResult(new RescueRewardResult(true));
        }
    }
}
