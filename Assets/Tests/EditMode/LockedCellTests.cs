using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Cover for the locked cell / "Kilitli Hücre" (issue #434): a level-authored, pre-occupied obstacle
    /// that no piece can be placed on, that is hole-like to the line-clear rule while locked, and that
    /// unlocks into an ordinary EMPTY cell once a threshold (1-3) of its DISTINCT orthogonal neighbours
    /// have each been destroyed at least once.
    /// <para>
    /// The file is deliberately ordered by what it is protecting. First the <see cref="Board"/>
    /// primitives, including the regression guard that is the mirror image of the ice socket's: a lock
    /// belongs to the BLOCK, so <see cref="Board.Clear"/> DOES reset it (the opposite assertion from
    /// <c>TargetIceCellTests.Clear_DoesNotResetTheIceLevel</c>). Then the fan-out seam itself
    /// (<see cref="Board.TryDamage"/>), with the single most important test in the file — AC4, the same
    /// neighbour destroyed twice counts ONCE, which a plain-integer counter would fail. Then the
    /// hole-like line rule (AC7), Clone/CopyFrom (what Undo is built on, AC10), each acceptance criterion
    /// through the Core resolvers and the real <see cref="BoardSystem"/> — including the explicit
    /// cascade-safety case: a neighbour destroyed by an explosive core's wipe mid-cascade, not by a
    /// completed line, counts just the same — then level authoring (AC2/AC5) and run-start seeding.
    /// </para>
    /// <para>
    /// <b>AC10 (undo restores a lock)</b> is covered only at the <see cref="Board.Clone"/>/
    /// <see cref="Board.CopyFrom"/> level, exactly as every other special cell's tests cover their own:
    /// one-step undo does not exist anywhere in this project yet.
    /// </para>
    /// <para>
    /// The View's skin overlay (three procedurally drawn plates, one layer peeled per stage) is not
    /// covered: it is a MonoBehaviour layer with no logic beyond a clamp, and the model event that
    /// drives it (<see cref="BoardModel.LockedCellChanged"/>) IS covered below.
    /// </para>
    /// </summary>
    public class LockedCellTests
    {
        private const int COLOUR = 3;

        private const int SKIN = 1;

        private static readonly Piece Single = new Piece("test_single", new[] { new GridPosition(0, 0) });

        // --- Board primitives ---

        [Test]
        public void OccupyLocked_SetsTheBlockTheKindTheThresholdAndTheSkin()
        {
            var board = new Board();
            var position = new GridPosition(3, 3);

            board.OccupyLocked(position, COLOUR, 3, SKIN);

            Assert.AreEqual(COLOUR, board[position]);
            Assert.AreEqual(SpecialCellKind.Locked, board.GetSpecialKind(position));
            Assert.IsTrue(board.IsLocked(position));
            Assert.AreEqual(3, board.GetLockedThreshold(position));
            Assert.AreEqual(0, board.GetLockedProgressMask(position), "Nothing has counted yet.");
            Assert.AreEqual(0, board.GetLockedProgressCount(position));
            Assert.AreEqual(SKIN, board.GetLockedSkin(position));
            Assert.IsFalse(board.IsLocked(new GridPosition(4, 4)), "Nothing else is locked.");
        }

        /// <summary>AC1: the one structural inversion from the ice socket. Marking a position icy leaves it
        /// EMPTY; locking one OCCUPIES it — the block is the lock, and its being occupied is the whole
        /// placement guard.</summary>
        [Test]
        public void OccupyLocked_OccupiesTheCell()
        {
            var board = new Board();
            var position = new GridPosition(3, 3);

            board.OccupyLocked(position, COLOUR, 2, SKIN);

            Assert.IsTrue(board.IsOccupied(position));
            Assert.IsFalse(board.IsEmpty());
            Assert.AreEqual(1, board.OccupiedCellCount());
        }

        [TestCase(0)]
        [TestCase(5)]
        public void OccupyLocked_WithAThresholdOutsideOneToFour_Throws(int threshold)
        {
            var board = new Board();

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => board.OccupyLocked(new GridPosition(0, 0), COLOUR, threshold, SKIN));
        }

        [TestCase(-1)]
        [TestCase(Board.LOCKED_SKIN_COUNT)]
        public void OccupyLocked_WithASkinOutsideTheRoll_Throws(int skin)
        {
            var board = new Board();

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => board.OccupyLocked(new GridPosition(0, 0), COLOUR, 2, skin));
        }

        [Test]
        public void OccupyLocked_OnAHole_Throws()
        {
            var shape = new BoardShape(Board.SIZE, Board.SIZE, new[] { new GridPosition(1, 1) });
            var board = new Board(shape);

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => board.OccupyLocked(new GridPosition(1, 1), COLOUR, 2, SKIN));
        }

        /// <summary>The regression guard, inverted from the ice socket's: a lock is the BLOCK standing on
        /// the cell, so Clear resets every part of it along with the colour — a lock a Bomb blows away
        /// must not leave a threshold behind for the next piece to inherit.</summary>
        [Test]
        public void Clear_ResetsTheLockedStateAlongWithTheBlock()
        {
            var board = new Board();
            var position = new GridPosition(4, 4);
            board.OccupyLocked(position, COLOUR, 3, SKIN);
            board.TryDamage(new GridPosition(4, 5));

            board.Clear(position);

            Assert.IsFalse(board.IsOccupied(position));
            Assert.IsFalse(board.IsLocked(position));
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(position));
            Assert.AreEqual(0, board.GetLockedThreshold(position));
            Assert.AreEqual(0, board.GetLockedProgressMask(position));
            Assert.AreEqual(0, board.GetLockedSkin(position));
        }

        // --- Board.TryDamage: the one seam every destruction path fans out from ---

        /// <summary>AC3 at the primitive, per direction: destroying the cell on a given side of the lock
        /// sets exactly that side's bit — from the LOCK's point of view, not the destroyed cell's.</summary>
        [TestCase(0, 1, Board.LOCKED_NEIGHBOUR_UP)]
        [TestCase(0, -1, Board.LOCKED_NEIGHBOUR_DOWN)]
        [TestCase(-1, 0, Board.LOCKED_NEIGHBOUR_LEFT)]
        [TestCase(1, 0, Board.LOCKED_NEIGHBOUR_RIGHT)]
        public void TryDamage_OnANeighbour_SetsThatDirectionsBitOnTheLock(int dx, int dy, int expectedBit)
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 4);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);
            var neighbour = new GridPosition(4 + dx, 4 + dy);
            board.Occupy(neighbour, COLOUR);

            Assert.IsTrue(board.TryDamage(neighbour));

            Assert.AreEqual(expectedBit, board.GetLockedProgressMask(lockPosition));
            Assert.AreEqual(1, board.GetLockedProgressCount(lockPosition));
            Assert.IsTrue(board.IsLocked(lockPosition), "Threshold 3, one counted: still locked.");
            Assert.IsTrue(board.IsOccupied(lockPosition));
        }

        /// <summary>
        /// <b>AC4 — the single most important test in this file.</b> The SAME neighbour destroyed twice
        /// counts as ONE distinct neighbour, not two. A plain integer counter would read 2 here and unlock
        /// a threshold-2 lock by farming one adjacent square; the 4-bit mask reads 1.
        /// </summary>
        [Test]
        public void TryDamage_OnTheSameNeighbourTwice_CountsItOnce()
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 4);
            board.OccupyLocked(lockPosition, COLOUR, 2, SKIN);
            var neighbour = new GridPosition(4, 5);

            board.Occupy(neighbour, COLOUR);
            Assert.IsTrue(board.TryDamage(neighbour));
            board.Occupy(neighbour, COLOUR);
            Assert.IsTrue(board.TryDamage(neighbour));

            Assert.AreEqual(1, board.GetLockedProgressCount(lockPosition), "One neighbour position, however many times.");
            Assert.AreEqual(Board.LOCKED_NEIGHBOUR_UP, board.GetLockedProgressMask(lockPosition));
            Assert.IsTrue(board.IsLocked(lockPosition), "Threshold 2 is not met by one square cleared twice.");
        }

        /// <summary>Diagonals are explicitly not neighbours (AC3).</summary>
        [Test]
        public void TryDamage_OnADiagonalCell_CountsNothing()
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 4);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);
            var diagonal = new GridPosition(5, 5);
            board.Occupy(diagonal, COLOUR);

            board.TryDamage(diagonal);

            Assert.AreEqual(0, board.GetLockedProgressCount(lockPosition));
            Assert.IsTrue(board.IsLocked(lockPosition));
        }

        /// <summary>A hit merely spent on a reinforced neighbour destroys nothing, so it counts nothing:
        /// the neighbour is still standing.</summary>
        [Test]
        public void TryDamage_SpendingAHitOnAReinforcedNeighbour_CountsNothing()
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 4);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);
            var neighbour = new GridPosition(3, 4);
            board.OccupyReinforced(neighbour, COLOUR, 3, 0);

            Assert.IsFalse(board.TryDamage(neighbour));

            Assert.AreEqual(0, board.GetLockedProgressCount(lockPosition));
            Assert.IsTrue(board.IsLocked(lockPosition));
        }

        /// <summary>AC6: three DIFFERENT neighbours each destroyed once meet a threshold of 3, and the
        /// unlock leaves a completely ordinary empty cell — no kind, no threshold, no skin, no block.</summary>
        [Test]
        public void TryDamage_OnThreeDistinctNeighbours_UnlocksAThresholdThreeCellIntoAnOrdinaryEmptyOne()
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 4);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);

            DestroyAt(board, new GridPosition(4, 5));
            Assert.IsTrue(board.IsLocked(lockPosition));
            DestroyAt(board, new GridPosition(4, 3));
            Assert.IsTrue(board.IsLocked(lockPosition));
            DestroyAt(board, new GridPosition(3, 4));

            Assert.IsFalse(board.IsLocked(lockPosition));
            Assert.IsFalse(board.IsOccupied(lockPosition));
            Assert.AreEqual(Board.EMPTY, board[lockPosition]);
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(lockPosition));
            Assert.AreEqual(0, board.GetLockedThreshold(lockPosition));
            Assert.AreEqual(0, board.GetLockedProgressMask(lockPosition));
            Assert.AreEqual(0, board.GetLockedSkin(lockPosition));
            Assert.IsTrue(DiamondCellRules.CanCarryDiamond(board, lockPosition), "Indistinguishable from a never-locked cell.");
            Assert.IsTrue(PlacementRules.CanPlace(board, Single, lockPosition), "And playable.");

            // A fourth neighbour destroyed afterwards touches nothing: there is no lock there any more.
            DestroyAt(board, new GridPosition(5, 4));
            Assert.AreEqual(Board.EMPTY, board[lockPosition]);
            Assert.AreEqual(0, board.GetLockedProgressMask(lockPosition));
        }

        /// <summary>A threshold of 1 — the "almost open" lock — opens on the very first distinct
        /// neighbour destroyed beside it.</summary>
        [Test]
        public void TryDamage_OnOneNeighbourOfAThresholdOneCell_Unlocks()
        {
            var board = new Board();
            var lockPosition = new GridPosition(0, 0);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);

            DestroyAt(board, new GridPosition(1, 0));

            Assert.IsFalse(board.IsLocked(lockPosition));
            Assert.IsFalse(board.IsOccupied(lockPosition));
        }

        /// <summary>Unlocking is not a destruction: nothing about the unlocked cell's own neighbours
        /// changes, and — the AC8 negative case at the primitive — the unlock is silent, returning
        /// nothing to any caller that could score or pay it.</summary>
        [Test]
        public void TryDamage_ThatUnlocksACell_DoesNotCascadeIntoTheUnlockedCellsOtherNeighbours()
        {
            var board = new Board();
            var lockA = new GridPosition(4, 4);
            var lockB = new GridPosition(4, 2);
            board.OccupyLocked(lockA, COLOUR, 1, SKIN);
            board.OccupyLocked(lockB, COLOUR, 1, SKIN);

            // (4,3) sits between the two locks. Destroying it opens BOTH (each counts it as a neighbour),
            // but opening lock A must not itself count as "a destroyed neighbour" for anything.
            DestroyAt(board, new GridPosition(4, 3));

            Assert.IsFalse(board.IsLocked(lockA));
            Assert.IsFalse(board.IsLocked(lockB));

            // A third lock adjacent only to lock A is untouched: lock A unlocking was not a destruction.
            var board2 = new Board();
            board2.OccupyLocked(new GridPosition(4, 4), COLOUR, 1, SKIN);
            board2.OccupyLocked(new GridPosition(4, 5), COLOUR, 1, SKIN);
            DestroyAt(board2, new GridPosition(3, 4));
            Assert.IsFalse(board2.IsLocked(new GridPosition(4, 4)), "Opened by (3,4).");
            Assert.IsTrue(board2.IsLocked(new GridPosition(4, 5)), "Its only destroyed-adjacent cell was a lock opening, not a block dying.");
            Assert.AreEqual(0, board2.GetLockedProgressCount(new GridPosition(4, 5)));
        }

        // --- Board.TryDamage aimed AT a lock: opens one level, never destroys outright ---

        /// <summary>The product decision for a hit that targets the lock itself (a Bomb, hammer, strike
        /// or laser over its own position): it advances the lock by exactly 1, as one more distinct
        /// neighbour would, and the lock stays standing — reported as an absorbed hit, exactly as a
        /// reinforced cell with hits to spare is by the same method.</summary>
        [Test]
        public void TryDamage_AimedAtALock_OpensOneLevelAndLeavesItStanding()
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 4);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);

            Assert.IsFalse(board.TryDamage(lockPosition), "Absorbed: the cell survives.");

            Assert.IsTrue(board.IsLocked(lockPosition));
            Assert.IsTrue(board.IsOccupied(lockPosition));
            Assert.AreEqual(1, board.GetLockedProgressCount(lockPosition));
        }

        [Test]
        public void TryDamage_AimedAtAThresholdOneLock_OpensItOnTheFirstHit()
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 4);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);

            Assert.IsTrue(board.TryDamage(lockPosition), "Reached its threshold: cleared by this hit.");

            Assert.IsFalse(board.IsLocked(lockPosition));
            Assert.IsFalse(board.IsOccupied(lockPosition));
            Assert.AreEqual(SpecialCellKind.None, board.GetSpecialKind(lockPosition));
            Assert.AreEqual(0, board.GetLockedThreshold(lockPosition));
            Assert.AreEqual(0, board.GetLockedProgressMask(lockPosition));
        }

        [Test]
        public void TryDamage_AimedAtAThresholdThreeLockThreeTimes_OpensItExactlyOnTheThird()
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 4);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);

            Assert.IsFalse(board.TryDamage(lockPosition));
            Assert.AreEqual(1, board.GetLockedProgressCount(lockPosition));
            Assert.IsTrue(board.IsLocked(lockPosition));

            Assert.IsFalse(board.TryDamage(lockPosition));
            Assert.AreEqual(2, board.GetLockedProgressCount(lockPosition));
            Assert.IsTrue(board.IsLocked(lockPosition));

            Assert.IsTrue(board.TryDamage(lockPosition));
            Assert.IsFalse(board.IsLocked(lockPosition));
            Assert.IsFalse(board.IsOccupied(lockPosition));
        }

        /// <summary>
        /// Direct hits and distinct-neighbour clears share ONE 4-bit mask, so they can neither
        /// double-count nor conflict. A direct hit takes the lowest unset bit — UP first — so a later
        /// destruction of the cell above the lock, which is the UP neighbour, adds nothing (the bit is
        /// already spent); a different neighbour adds one; and a second direct hit takes the next unset
        /// bit. The popcount is what the threshold is measured against, and it rises by exactly 1 per
        /// distinct source.
        /// </summary>
        [Test]
        public void TryDamage_MixingDirectHitsAndDistinctNeighbours_SharesOneMaskWithoutDoubleCounting()
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 4);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);

            Assert.IsFalse(board.TryDamage(lockPosition), "Direct hit 1.");
            Assert.AreEqual(Board.LOCKED_NEIGHBOUR_UP, board.GetLockedProgressMask(lockPosition));
            Assert.AreEqual(1, board.GetLockedProgressCount(lockPosition));

            // The cell above the lock IS the UP neighbour: its bit is already spent by the direct hit.
            DestroyAt(board, new GridPosition(4, 5));
            Assert.AreEqual(Board.LOCKED_NEIGHBOUR_UP, board.GetLockedProgressMask(lockPosition));
            Assert.AreEqual(1, board.GetLockedProgressCount(lockPosition), "Same bit, no double count.");
            Assert.IsTrue(board.IsLocked(lockPosition));

            // A different neighbour, to the left, is a new distinct source.
            DestroyAt(board, new GridPosition(3, 4));
            Assert.AreEqual(Board.LOCKED_NEIGHBOUR_UP | Board.LOCKED_NEIGHBOUR_LEFT, board.GetLockedProgressMask(lockPosition));
            Assert.AreEqual(2, board.GetLockedProgressCount(lockPosition));
            Assert.IsTrue(board.IsLocked(lockPosition));

            // Direct hit 2 takes the next unset bit (DOWN), reaching threshold 3: opened by this hit.
            Assert.IsTrue(board.TryDamage(lockPosition), "Direct hit 2 reaches the threshold.");
            Assert.IsFalse(board.IsLocked(lockPosition));
            Assert.IsFalse(board.IsOccupied(lockPosition));
        }

        /// <summary>A lock that absorbed a direct hit is still standing, so it was never destroyed and
        /// must credit nothing to the locks beside it — the fan-out is for destroyed cells only.</summary>
        [Test]
        public void TryDamage_AimedAtALockThatSurvives_CreditsNothingToItsOwnNeighbours()
        {
            var board = new Board();
            var lockA = new GridPosition(4, 4);
            var lockB = new GridPosition(4, 5);
            board.OccupyLocked(lockA, COLOUR, 3, SKIN);
            board.OccupyLocked(lockB, COLOUR, 1, SKIN);

            Assert.IsFalse(board.TryDamage(lockA));

            Assert.IsTrue(board.IsLocked(lockA));
            Assert.IsTrue(board.IsLocked(lockB), "A still-standing lock is not a destroyed neighbour.");
            Assert.AreEqual(0, board.GetLockedProgressCount(lockB));
        }

        /// <summary>The counterpart: a lock a direct hit DID open was removed by that hit, a destroyed
        /// cell like any other, so it fans out to its neighbours exactly as an ordinary block would.
        /// (A lock opened from BESIDE, by a neighbour's destruction, still fans out nothing — see
        /// <see cref="TryDamage_ThatUnlocksACell_DoesNotCascadeIntoTheUnlockedCellsOtherNeighbours"/>.)</summary>
        [Test]
        public void TryDamage_AimedAtALockThatOpens_CountsAsADestroyedNeighbourForTheLockBesideIt()
        {
            var board = new Board();
            var lockA = new GridPosition(4, 4);
            var lockB = new GridPosition(4, 5);
            board.OccupyLocked(lockA, COLOUR, 1, SKIN);
            board.OccupyLocked(lockB, COLOUR, 2, SKIN);

            Assert.IsTrue(board.TryDamage(lockA));

            Assert.IsFalse(board.IsLocked(lockA));
            Assert.IsTrue(board.IsLocked(lockB));
            Assert.AreEqual(Board.LOCKED_NEIGHBOUR_DOWN, board.GetLockedProgressMask(lockB), "Lock A lies below lock B.");
        }

        /// <summary>A resolver's removal pass can reach a lock that an earlier removal in the SAME pass
        /// already opened from beside it. Damaging an empty cell destroys nothing, so it must credit
        /// nothing — otherwise the lock's own neighbours would be handed a phantom destruction.</summary>
        [Test]
        public void TryDamage_OnAnEmptyCell_CreditsNothingToTheLockBesideIt()
        {
            var board = new Board();
            var lockPosition = new GridPosition(4, 5);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);

            board.TryDamage(new GridPosition(4, 4));

            Assert.IsTrue(board.IsLocked(lockPosition));
            Assert.AreEqual(0, board.GetLockedProgressCount(lockPosition));
        }

        // --- AC7: hole-like to the line rule while locked ---

        /// <summary>The positive half of AC7, which is what keeps a lock from soft-locking a level: a row
        /// whose every OTHER playable cell is filled IS full — the lock is skipped exactly as a hole is,
        /// so the row can complete through it.</summary>
        [Test]
        public void IsRowFull_SkipsAStillLockedCell()
        {
            var board = new Board();
            var lockPosition = new GridPosition(3, 0);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);
            for (int x = 0; x < Board.SIZE; x++)
            {
                if (x != 3)
                {
                    board.Occupy(new GridPosition(x, 0), COLOUR);
                }
            }

            Assert.IsTrue(board.IsRowFull(0), "Full through the lock, as through a hole.");

            // Literally the hole's answer: the same row on a board where that cell IS a hole.
            var holed = new Board(new BoardShape(Board.SIZE, Board.SIZE, new[] { lockPosition }));
            for (int x = 0; x < Board.SIZE; x++)
            {
                if (x != 3)
                {
                    holed.Occupy(new GridPosition(x, 0), COLOUR);
                }
            }

            Assert.AreEqual(holed.IsRowFull(0), board.IsRowFull(0));
        }

        /// <summary>And the lock itself never makes a row full: it does not count as a filled cell, so a
        /// row with one empty cell beside it is still one short.</summary>
        [Test]
        public void IsRowFull_DoesNotCountTheLockAsFilled()
        {
            var board = new Board();
            board.OccupyLocked(new GridPosition(3, 0), COLOUR, 3, SKIN);
            for (int x = 1; x < Board.SIZE; x++)
            {
                if (x != 3)
                {
                    board.Occupy(new GridPosition(x, 0), COLOUR);
                }
            }

            Assert.IsFalse(board.IsRowFull(0), "(0,0) is empty; the lock at (3,0) does not fill in for it.");
        }

        [Test]
        public void IsColumnFull_SkipsAStillLockedCell()
        {
            var board = new Board();
            board.OccupyLocked(new GridPosition(0, 3), COLOUR, 3, SKIN);
            for (int y = 0; y < Board.SIZE; y++)
            {
                if (y != 3)
                {
                    board.Occupy(new GridPosition(0, y), COLOUR);
                }
            }

            Assert.IsTrue(board.IsColumnFull(0));
        }

        /// <summary>The all-holes guard, extended: a line whose every playable cell is locked is never
        /// full, or it would "clear" — destroying nothing — on every single pass forever.</summary>
        [Test]
        public void IsRowFull_AndIsColumnFull_AreFalseForALineMadeEntirelyOfLocks()
        {
            var board = new Board();
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.OccupyLocked(new GridPosition(x, 0), COLOUR, 2, SKIN);
            }

            Assert.IsFalse(board.IsRowFull(0));
            Assert.IsFalse(board.IsColumnFull(0), "Column 0 holds one lock and seven empties — not full either way.");
        }

        /// <summary>The explosive core's "missing exactly one" scan skips a lock the same way.</summary>
        [Test]
        public void IsRowOneCellFromFull_SkipsAStillLockedCell()
        {
            var board = new Board();
            board.OccupyLocked(new GridPosition(3, 0), COLOUR, 3, SKIN);
            for (int x = 1; x < Board.SIZE; x++)
            {
                if (x != 3)
                {
                    board.Occupy(new GridPosition(x, 0), COLOUR);
                }
            }

            Assert.IsTrue(board.IsRowOneCellFromFull(0), "Only (0,0) is missing; the lock is not a gap.");
            Assert.IsFalse(board.IsColumnOneCellFromFull(3), "Column 3: the lock plus seven empties is not one short.");
        }

        /// <summary>The second half of AC7: a completed line's cell list never contains the lock.</summary>
        [Test]
        public void CollectRowCells_AndCollectColumnCells_OmitAStillLockedCell()
        {
            var board = new Board();
            var lockPosition = new GridPosition(3, 2);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);

            var row = new List<GridPosition>();
            board.CollectRowCells(2, row);
            var column = new List<GridPosition>();
            board.CollectColumnCells(3, column);

            Assert.AreEqual(Board.SIZE - 1, row.Count);
            Assert.IsFalse(row.Contains(lockPosition));
            Assert.AreEqual(Board.SIZE - 1, column.Count);
            Assert.IsFalse(column.Contains(lockPosition));
        }

        /// <summary>The negative case the issue calls out: a clear of the lock's OWN row leaves the lock
        /// itself completely untouched — still locked, still occupied, still the same threshold and skin.
        /// Its two in-row neighbours were destroyed, so they count (AC3 says a neighbour of any kind,
        /// destroyed any way, counts) — but with a threshold of 3 that is not enough, and nothing about
        /// the lock's own cell changed.</summary>
        [Test]
        public void ResolveClears_ThroughALockedCellsOwnRow_DestroysEveryOtherCellAndLeavesTheLockStanding()
        {
            var board = new Board();
            var lockPosition = new GridPosition(3, 5);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);
            for (int x = 0; x < Board.SIZE; x++)
            {
                if (x != 3)
                {
                    board.Occupy(new GridPosition(x, 5), COLOUR);
                }
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(1, result.LineCount);
            Assert.AreEqual(Board.SIZE - 1, result.ClearedCellCount, "Seven cells destroyed; the lock is not one of them.");
            Assert.IsTrue(board.IsLocked(lockPosition));
            Assert.IsTrue(board.IsOccupied(lockPosition));
            Assert.AreEqual(COLOUR, board[lockPosition]);
            Assert.AreEqual(3, board.GetLockedThreshold(lockPosition));
            Assert.AreEqual(SKIN, board.GetLockedSkin(lockPosition));
            Assert.AreEqual(
                Board.LOCKED_NEIGHBOUR_LEFT | Board.LOCKED_NEIGHBOUR_RIGHT, board.GetLockedProgressMask(lockPosition),
                "Its left and right neighbours were destroyed by the line, and count.");
            Assert.AreEqual(1, board.OccupiedCellCount(), "Only the lock remains.");
        }

        /// <summary>A row-and-column intersection beside the lock is one destruction of one neighbour:
        /// one bit, exactly as a reinforced cell there spends one hit.</summary>
        [Test]
        public void ResolveClears_WithARowAndAColumnCrossingBesideTheLock_CountsThatNeighbourOnce()
        {
            var board = new Board();
            var lockPosition = new GridPosition(5, 4);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);
            FillRow(board, 3);
            for (int y = 0; y < Board.SIZE; y++)
            {
                var cell = new GridPosition(4, y);
                if (!board.IsOccupied(cell))
                {
                    board.Occupy(cell, COLOUR);
                }
            }

            LineClearResult result = LineClearResolver.ResolveClears(board);

            Assert.AreEqual(2, result.LineCount);
            // Row 3 destroyed (5,3) — below the lock; column 4 destroyed (4,4) — left of it.
            Assert.AreEqual(
                Board.LOCKED_NEIGHBOUR_DOWN | Board.LOCKED_NEIGHBOUR_LEFT, board.GetLockedProgressMask(lockPosition));
            Assert.AreEqual(2, board.GetLockedProgressCount(lockPosition));
            Assert.IsTrue(board.IsLocked(lockPosition));
        }

        // --- AC1: placement is refused by occupancy alone ---

        [Test]
        public void CanPlace_OntoALockedCell_IsRefusedExactlyAsAnyOccupiedCellIs()
        {
            var locked = new Board();
            var plain = new Board();
            var position = new GridPosition(3, 3);
            locked.OccupyLocked(position, COLOUR, 2, SKIN);
            plain.Occupy(position, COLOUR);

            Assert.IsFalse(PlacementRules.CanPlace(locked, Single, position));
            Assert.AreEqual(
                PlacementRules.CanPlace(plain, Single, position),
                PlacementRules.CanPlace(locked, Single, position));
        }

        // --- Clone / CopyFrom: what Undo is built on (AC10) ---

        [Test]
        public void Clone_CopiesTheLockedState_AndTheCopyIsIndependent()
        {
            var board = new Board();
            var position = new GridPosition(5, 2);
            board.OccupyLocked(position, COLOUR, 3, 2);
            DestroyAt(board, new GridPosition(5, 3));

            Board copy = board.Clone();
            Assert.IsTrue(copy.IsLocked(position));
            Assert.AreEqual(3, copy.GetLockedThreshold(position));
            Assert.AreEqual(Board.LOCKED_NEIGHBOUR_UP, copy.GetLockedProgressMask(position));
            Assert.AreEqual(2, copy.GetLockedSkin(position));

            DestroyAt(copy, new GridPosition(5, 1));
            Assert.AreEqual(1, board.GetLockedProgressCount(position), "Mutating the clone leaves the original alone.");
            Assert.AreEqual(2, copy.GetLockedProgressCount(position));
        }

        [Test]
        public void CopyFrom_CopiesTheLockedState()
        {
            var source = new Board();
            var position = new GridPosition(6, 6);
            source.OccupyLocked(position, COLOUR, 2, 0);
            DestroyAt(source, new GridPosition(6, 5));

            var destination = new Board();
            destination.CopyFrom(source);

            Assert.IsTrue(destination.IsLocked(position));
            Assert.AreEqual(2, destination.GetLockedThreshold(position));
            Assert.AreEqual(Board.LOCKED_NEIGHBOUR_DOWN, destination.GetLockedProgressMask(position));
            Assert.AreEqual(0, destination.GetLockedSkin(position));
        }

        /// <summary>The Undo-relevant halves: restoring a snapshot taken BEFORE the unlock brings the lock
        /// back with its pre-unlock progress and skin, and restoring one with no lock at all clears a
        /// stale lock rather than leaving it behind.</summary>
        [Test]
        public void CopyFrom_ASnapshotTakenBeforeAnUnlock_RestoresTheLock_AndOneWithNoLockDropsIt()
        {
            var live = new Board();
            var position = new GridPosition(3, 3);
            live.OccupyLocked(position, COLOUR, 2, 1);
            DestroyAt(live, new GridPosition(3, 4));
            Board snapshot = live.Clone();

            DestroyAt(live, new GridPosition(2, 3));
            Assert.IsFalse(live.IsLocked(position), "Unlocked on the live board.");

            live.CopyFrom(snapshot);
            Assert.IsTrue(live.IsLocked(position), "Undo brought the lock back...");
            Assert.AreEqual(1, live.GetLockedProgressCount(position), "...at its pre-placement progress...");
            Assert.AreEqual(1, live.GetLockedSkin(position), "...wearing the same skin.");

            live.CopyFrom(new Board());
            Assert.IsFalse(live.IsLocked(position));
            Assert.AreEqual(0, live.GetLockedThreshold(position));
            Assert.AreEqual(0, live.GetLockedProgressMask(position));
        }

        // --- Every destruction path counts alike (AC3) ---

        [Test]
        public void ResolveBombClear_DestroyingANeighbour_Counts()
        {
            var board = new Board();
            var lockPosition = new GridPosition(0, 0);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);

            // A bomb centred at (1,2) covers rows 1..3 of columns 0..2: it takes (0,1), the block above
            // the lock, and does not reach the lock itself at (0,0).
            board.Occupy(new GridPosition(0, 1), COLOUR);
            PowerUpClearResolver.ResolveBombClear(board, new GridPosition(1, 2));

            Assert.IsFalse(board.IsOccupied(new GridPosition(0, 1)), "The bomb took the neighbour above the lock.");
            Assert.IsFalse(board.IsLocked(lockPosition), "And the threshold-1 lock opened.");
        }

        /// <summary>The product decision for a power-up aimed AT the lock rather than beside it: a direct
        /// hit does not destroy a lock, it opens ONE level — the same effect as one more distinct
        /// neighbour destroyed. A Bomb over a threshold-3 lock leaves it standing at progress 1, and the
        /// bomb's cleared count does not include it, because nothing was destroyed there.</summary>
        [Test]
        public void ResolveBombClear_OverTheLockItself_OpensOneLevelAndLeavesItStanding()
        {
            var board = new Board();
            var lockPosition = new GridPosition(3, 3);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, lockPosition);

            Assert.AreEqual(0, result.ClearedCellCount, "The lock absorbed the hit; nothing was cleared.");
            Assert.IsTrue(board.IsLocked(lockPosition));
            Assert.IsTrue(board.IsOccupied(lockPosition));
            Assert.AreEqual(1, board.GetLockedProgressCount(lockPosition));
        }

        [Test]
        public void ResolveBombClear_OverTheLockItselfThreeTimes_OpensAThresholdThreeLockOnTheThird()
        {
            var board = new Board();
            var lockPosition = new GridPosition(3, 3);
            board.OccupyLocked(lockPosition, COLOUR, 3, SKIN);

            Assert.AreEqual(0, PowerUpClearResolver.ResolveBombClear(board, lockPosition).ClearedCellCount);
            Assert.AreEqual(0, PowerUpClearResolver.ResolveBombClear(board, lockPosition).ClearedCellCount);
            Assert.IsTrue(board.IsLocked(lockPosition));
            Assert.AreEqual(2, board.GetLockedProgressCount(lockPosition));

            PowerUpClearResult third = PowerUpClearResolver.ResolveBombClear(board, lockPosition);

            Assert.AreEqual(1, third.ClearedCellCount, "The hit that reaches the threshold destroyed the cell.");
            Assert.IsFalse(board.IsLocked(lockPosition));
            Assert.IsFalse(board.IsOccupied(lockPosition));
        }

        /// <summary>The gate and the primitive must agree: a bomb whose blast holds BOTH a lock and the
        /// neighbour that will open it reports exactly two cleared cells and leaves both empty, whichever
        /// order the resolver removes them in.</summary>
        [Test]
        public void ResolveBombClear_OverALockAndTheNeighbourThatOpensIt_ClearsBoth()
        {
            var board = new Board();
            var lockPosition = new GridPosition(3, 3);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);
            board.Occupy(new GridPosition(3, 4), COLOUR);

            PowerUpClearResult result = PowerUpClearResolver.ResolveBombClear(board, lockPosition);

            Assert.AreEqual(2, result.ClearedCellCount);
            Assert.IsFalse(board.IsOccupied(lockPosition));
            Assert.IsFalse(board.IsLocked(lockPosition));
            Assert.IsFalse(board.IsOccupied(new GridPosition(3, 4)));
        }

        [Test]
        public void ResolveFill_CompletingARowBesideALock_Counts()
        {
            var board = new Board();
            var lockPosition = new GridPosition(2, 3);
            board.OccupyLocked(lockPosition, COLOUR, 1, SKIN);
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                board.Occupy(new GridPosition(x, 2), COLOUR);
            }

            JokerFillResult result = JokerFillResolver.ResolveFill(board, new GridPosition(7, 2), COLOUR);

            Assert.IsTrue(result.Filled);
            Assert.AreEqual(1, result.LineCount);
            Assert.IsFalse(board.IsLocked(lockPosition), "(2,2) below the lock went with the row.");
        }

        [Test]
        public void LaserEffect_WipingThroughANeighbour_Counts_AndSkipsTheLockItself()
        {
            var board = new Board();
            var lockPosition = new GridPosition(0, 6);
            board.OccupyLocked(lockPosition, COLOUR, 2, SKIN);
            board.Occupy(new GridPosition(0, 5), COLOUR);
            board.Occupy(new GridPosition(0, 7), COLOUR);

            var laser = new LaserEffect();
            laser.BeginResolution();

            // A laser destroyed by a row clear wipes its column — column 0, which holds the lock and both
            // of its column neighbours. The neighbours go; the lock, hole-like to the wipe, stays.
            laser.Apply(board, new SpecialCellTrigger(new GridPosition(0, 2), SpecialCellKind.Laser, ClearAxis.Row));

            Assert.IsFalse(board.IsOccupied(new GridPosition(0, 5)));
            Assert.IsFalse(board.IsOccupied(new GridPosition(0, 7)));
            Assert.IsFalse(board.IsLocked(lockPosition), "Two distinct neighbours: threshold 2 met.");
            Assert.IsFalse(board.IsOccupied(lockPosition), "Unlocked into an empty cell — NOT wiped; there is no difference to see here, so:");

            // ...the same wipe against a threshold-3 lock shows the lock survived the line untouched.
            var board2 = new Board();
            board2.OccupyLocked(lockPosition, COLOUR, 3, SKIN);
            board2.Occupy(new GridPosition(0, 5), COLOUR);
            var laser2 = new LaserEffect();
            laser2.BeginResolution();
            laser2.Apply(board2, new SpecialCellTrigger(new GridPosition(0, 2), SpecialCellKind.Laser, ClearAxis.Row));
            Assert.IsTrue(board2.IsLocked(lockPosition), "Wiped column 0, lock still standing.");
            Assert.AreEqual(1, board2.GetLockedProgressCount(lockPosition));
        }

        // --- Through the real System: placement, cascade (AC3 negative case), the event ---

        /// <summary>AC4 end to end, the way a player would try it: complete the row above the lock, fill
        /// the same cell again, complete it again. Two clears of ONE neighbour position: progress 1.</summary>
        [Test]
        public void TryPlacePiece_ClearingTheSameNeighbourTwice_CountsItOnce()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel, out _);

            var lockPosition = new GridPosition(Board.SIZE - 1, 2);
            boardModel.OccupyLocked(lockPosition, COLOUR, 2, SKIN);
            var neighbour = new GridPosition(Board.SIZE - 1, 3);

            for (int cycle = 0; cycle < 2; cycle++)
            {
                for (int x = 0; x < Board.SIZE - 1; x++)
                {
                    boardModel.Occupy(new GridPosition(x, 3), COLOUR);
                }

                trayModel.SetSlot(0, Single, COLOUR);
                Assert.IsTrue(system.TryPlacePiece(0, neighbour));
                Assert.AreEqual(Board.EMPTY, boardModel.GetCell(neighbour), "Row 3 cleared.");
            }

            Assert.IsTrue(boardModel.IsLocked(lockPosition), "Still locked: one distinct neighbour, not two.");
            Assert.AreEqual(1, boardModel.GetLockedProgressCount(lockPosition));
            Assert.AreEqual(Board.LOCKED_NEIGHBOUR_UP, boardModel.GetLockedProgressMask(lockPosition));
        }

        /// <summary>AC6 end to end: the row above, then the row below, open a threshold-2 lock, and the
        /// placement that opened it scored exactly what its own line scored — no bonus for the unlock
        /// (AC8), which is checked by the cleared-cell count the message carries being the line's own.</summary>
        [Test]
        public void TryPlacePiece_ClearingTwoDistinctNeighbours_UnlocksAndCreditsNothingForIt()
        {
            BoardSystem system = CreateSystem(
                out BoardModel boardModel, out TrayModel trayModel,
                out TestMessageBroker<PiecePlacedMessage> placedBroker,
                linesClearedBroker: out TestMessageBroker<LinesClearedMessage> linesClearedBroker);

            var lockPosition = new GridPosition(Board.SIZE - 1, 2);
            boardModel.OccupyLocked(lockPosition, COLOUR, 2, SKIN);

            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                boardModel.Occupy(new GridPosition(x, 3), COLOUR);
                boardModel.Occupy(new GridPosition(x, 1), COLOUR);
            }

            trayModel.SetSlot(0, Single, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));
            Assert.IsTrue(boardModel.IsLocked(lockPosition));

            trayModel.SetSlot(0, Single, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 1)));

            Assert.IsFalse(boardModel.IsLocked(lockPosition));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(lockPosition));
            Assert.AreEqual(SpecialCellKind.None, boardModel.GetSpecialKind(lockPosition));
            Assert.AreEqual(2, placedBroker.Published.Count);
            Assert.AreEqual(1, placedBroker.Published[1].LinesCleared);
            Assert.AreEqual(0, placedBroker.Published[1].ReinforcedCellsFullyClearedCount);
            Assert.AreEqual(0, placedBroker.Published[1].IceCellsMeltedCount);
            Assert.AreEqual(2, linesClearedBroker.Published.Count);
            Assert.AreEqual(
                linesClearedBroker.Published[0].ClearedCellCount, linesClearedBroker.Published[1].ClearedCellCount,
                "The unlocking placement destroyed exactly as many cells as the one before it: the unlock added none.");
        }

        /// <summary>
        /// AC3, the explicit cascade-safety negative case: the lock's neighbour is destroyed NOT by the
        /// line the player completed but by an explosive core's wipe mid-cascade. The core sits in the
        /// completed row and wipes its column, which holds a block beside the lock two rows away. The
        /// count must advance and the lock must open — the Reinforced Cell cascade-reporting gap (#249)
        /// is what this guards against.
        /// </summary>
        [Test]
        public void TryPlacePiece_WhereACascadedWipeDestroysTheLocksNeighbour_Counts()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel, out _);
            trayModel.SetSlot(0, Single, COLOUR);

            // Row 5 is full but for the gap the piece will fill; the core at (0,5) is destroyed by that
            // row clear and wipes column 0.
            var gap = new GridPosition(3, 5);
            for (int x = 0; x < Board.SIZE; x++)
            {
                var cell = new GridPosition(x, 5);
                if (!cell.Equals(gap))
                {
                    boardModel.Occupy(cell, COLOUR);
                }
            }

            boardModel.SetSpecialKind(new GridPosition(0, 5), SpecialCellKind.ExplosiveCore);

            // The lock at (1,1), threshold 1, with a bystander block at (0,1) — in column 0, nowhere near
            // the row the player completes.
            var lockPosition = new GridPosition(1, 1);
            boardModel.OccupyLocked(lockPosition, COLOUR, 1, SKIN);
            boardModel.Occupy(new GridPosition(0, 1), COLOUR);

            Assert.IsTrue(system.TryPlacePiece(0, gap));

            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(0, 1)), "The wipe took the neighbour.");
            Assert.IsFalse(boardModel.IsLocked(lockPosition), "And the lock counted it and opened.");
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(lockPosition));
        }

        [Test]
        public void TryPlacePiece_WithNoClear_LeavesEveryLockUntouched()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel, out _);
            trayModel.SetSlot(0, Single, COLOUR);

            var lockPosition = new GridPosition(6, 6);
            boardModel.OccupyLocked(lockPosition, COLOUR, 2, SKIN);

            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(6, 7)), "Placing beside a lock is ordinary.");

            Assert.IsTrue(boardModel.IsLocked(lockPosition));
            Assert.AreEqual(0, boardModel.GetLockedProgressCount(lockPosition));
        }

        /// <summary>The View's seam: seeding announces the lock, and a placement that advances or opens it
        /// re-announces the position through <see cref="BoardModel.LockedCellChanged"/> — including the
        /// opened, now-empty position, which no cell-change event ever describes.</summary>
        [Test]
        public void OccupyLocked_AndAPlacementThatUnlocks_AnnounceThePosition()
        {
            BoardSystem system = CreateSystem(out BoardModel boardModel, out TrayModel trayModel, out _);
            var announced = new List<GridPosition>();
            boardModel.LockedCellChanged += position => announced.Add(position);

            var lockPosition = new GridPosition(Board.SIZE - 1, 2);
            boardModel.OccupyLocked(lockPosition, COLOUR, 1, SKIN);
            Assert.IsTrue(announced.Contains(lockPosition), "Seeding announced it.");
            Assert.IsTrue(boardModel.IsLocked(lockPosition));

            announced.Clear();
            for (int x = 0; x < Board.SIZE - 1; x++)
            {
                boardModel.Occupy(new GridPosition(x, 3), COLOUR);
            }

            trayModel.SetSlot(0, Single, COLOUR);
            Assert.IsTrue(system.TryPlacePiece(0, new GridPosition(Board.SIZE - 1, 3)));

            Assert.IsTrue(announced.Contains(lockPosition), "The opened position was announced after the clear.");
            Assert.IsFalse(boardModel.IsLocked(lockPosition));
        }

        // --- Level authoring (AC2, AC5) ---

        [Test]
        public void LockedCells_OnARowAuthoredBeforeTheFieldExisted_IsEmpty()
        {
            LevelObjectiveConfig config = ARow("{\"_levelNumber\":1,\"_targetValue\":1}");

            Assert.AreEqual(0, config.LockedCells.Count);
        }

        [Test]
        public void LockedCells_OnARowAuthoringTwo_ReadsBothPositionsAndThresholds()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_lockedCells\":["
                + "{\"_x\":1,\"_y\":2,\"_unlockThreshold\":1},{\"_x\":5,\"_y\":6,\"_unlockThreshold\":3}]}");

            Assert.AreEqual(2, config.LockedCells.Count);
            Assert.AreEqual(1, config.LockedCells[0].UnlockThreshold);
            Assert.AreEqual(3, config.LockedCells[1].UnlockThreshold);
        }

        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(3, 3)]
        [TestCase(9, 3)]
        [TestCase(-3, 1)]
        public void ValidateInEditor_ClampsTheThresholdIntoRange(int authored, int expected)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_lockedCells\":["
                + $"{{\"_x\":1,\"_y\":1,\"_unlockThreshold\":{authored}}}]}}");

            config.ValidateInEditor();

            Assert.AreEqual(expected, config.LockedCells[0].UnlockThreshold);
        }

        [TestCase(8, 1)]
        [TestCase(1, 8)]
        [TestCase(-1, 1)]
        public void IsValid_WithALockOutsideTheBoard_Fails(int x, int y)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_lockedCells\":["
                + $"{{\"_x\":{x},\"_y\":{y},\"_unlockThreshold\":1}}]}}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [TestCase(0)]
        [TestCase(4)]
        public void IsValid_WithAThresholdOutsideOneToThree_Fails(int threshold)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_lockedCells\":["
                + $"{{\"_x\":3,\"_y\":3,\"_unlockThreshold\":{threshold}}}]}}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void IsValid_WithALockOnAHole_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_boardHoles\":[{\"_x\":2,\"_y\":2}],"
                + "\"_lockedCells\":[{\"_x\":2,\"_y\":2,\"_unlockThreshold\":1}]}");

            Assert.IsFalse(config.IsValid(out string error));
            StringAssert.Contains("hole", error);
        }

        [Test]
        public void IsValid_WithALockOnAReinforcedTimerOrIceCell_Fails()
        {
            LevelObjectiveConfig onReinforced = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_reinforcedCells\":[{\"_x\":2,\"_y\":2,\"_hitCount\":2}],"
                + "\"_lockedCells\":[{\"_x\":2,\"_y\":2,\"_unlockThreshold\":1}]}");
            LevelObjectiveConfig onTimer = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_timerCells\":[{\"_x\":3,\"_y\":3,\"_startingCountdown\":3}],"
                + "\"_lockedCells\":[{\"_x\":3,\"_y\":3,\"_unlockThreshold\":1}]}");
            LevelObjectiveConfig onIce = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + "\"_targetIceCells\":[{\"_x\":4,\"_y\":4,\"_iceLevel\":1}],"
                + "\"_lockedCells\":[{\"_x\":4,\"_y\":4,\"_unlockThreshold\":1}]}");

            Assert.IsFalse(onReinforced.IsValid(out string reinforcedError));
            StringAssert.Contains("reinforced", reinforcedError);
            Assert.IsFalse(onTimer.IsValid(out string timerError));
            StringAssert.Contains("timer", timerError);
            Assert.IsFalse(onIce.IsValid(out string iceError));
            StringAssert.Contains("ice", iceError);
        }

        [Test]
        public void IsValid_WithTheSameLockAuthoredTwice_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_lockedCells\":["
                + "{\"_x\":1,\"_y\":1,\"_unlockThreshold\":1},{\"_x\":1,\"_y\":1,\"_unlockThreshold\":2}]}");

            Assert.IsFalse(config.IsValid(out string error));
            StringAssert.Contains("more than once", error);
        }

        /// <summary>AC5: a corner has only two orthogonal neighbours, so a threshold of 3 there could never
        /// be met and the lock would stand forever. Rejected at authoring time, not discovered in play.</summary>
        [Test]
        public void IsValid_WithAThresholdAboveTheCornersRealNeighbourCount_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_lockedCells\":["
                + "{\"_x\":0,\"_y\":0,\"_unlockThreshold\":3}]}");

            Assert.IsFalse(config.IsValid(out string error));
            StringAssert.Contains("neighbour", error);
        }

        /// <summary>AC5, the hole case: an edge cell normally has three neighbours, but a hole beside it
        /// takes one away — threshold 3 fails, threshold 2 passes.</summary>
        [Test]
        public void IsValid_CountsAHoleAsAMissingNeighbour()
        {
            const string HOLE_AND_LOCK = "\"_boardHoles\":[{\"_x\":1,\"_y\":0}],\"_lockedCells\":[{\"_x\":2,\"_y\":0,\"_unlockThreshold\":";
            LevelObjectiveConfig three = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1," + HOLE_AND_LOCK + "3}]}");
            LevelObjectiveConfig two = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1," + HOLE_AND_LOCK + "2}]}");

            Assert.IsFalse(three.IsValid(out string error));
            StringAssert.Contains("neighbour", error);
            Assert.IsTrue(two.IsValid(out string twoError), twoError);
        }

        [TestCase(0, 0, 2)]
        [TestCase(3, 0, 3)]
        [TestCase(3, 3, 3)]
        [TestCase(7, 7, 1)]
        public void IsValid_WithAWellFormedLock_Passes(int x, int y, int threshold)
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_lockedCells\":["
                + $"{{\"_x\":{x},\"_y\":{y},\"_unlockThreshold\":{threshold}}}]}}");

            Assert.IsTrue(config.IsValid(out string error), error);
        }

        // --- Level-start seeding (AC1, AC9) ---

        [Test]
        public void StartNewRun_OnALevelAuthoringLocks_OccupiesThemWithTheirThresholdsAndARolledSkin()
        {
            var catalog = ACatalogOf(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_lockedCells\":["
                + "{\"_x\":1,\"_y\":1,\"_unlockThreshold\":1},{\"_x\":6,\"_y\":7,\"_unlockThreshold\":3}]}");

            BoardSystem system = CreateSystem(out BoardModel boardModel, out _, out _, ASeederFor(catalog));

            system.StartNewRun();

            var first = new GridPosition(1, 1);
            var second = new GridPosition(6, 7);
            Assert.IsTrue(boardModel.IsLocked(first));
            Assert.IsTrue(boardModel.IsLocked(second));
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(first), "A lock brings its own block (AC1).");
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(second));
            Assert.AreEqual(1, boardModel.GetLockedThreshold(first));
            Assert.AreEqual(3, boardModel.GetLockedThreshold(second));
            Assert.AreEqual(0, boardModel.GetLockedProgressCount(first));
            Assert.That(boardModel.GetLockedSkin(first), Is.InRange(0, Board.LOCKED_SKIN_COUNT - 1));
            Assert.That(boardModel.GetLockedSkin(second), Is.InRange(0, Board.LOCKED_SKIN_COUNT - 1));

            Object.DestroyImmediate(catalog);
        }

        /// <summary>AC9: the skin is a roll, not a constant — over enough seeds every approved look comes
        /// up — and a fixed seed pins it, so the roll is deterministic where a test needs it to be.</summary>
        [Test]
        public void StartNewRun_RollsEverySkinAcrossSeeds_AndTheSameSeedRollsTheSameSkin()
        {
            var catalog = ACatalogOf(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_lockedCells\":[{\"_x\":3,\"_y\":3,\"_unlockThreshold\":2}]}");
            var seen = new HashSet<int>();

            for (int seed = 1; seed <= 40; seed++)
            {
                BoardSystem system = CreateSystem(out BoardModel boardModel, out _, out _, ASeederFor(catalog, seed));
                system.StartNewRun();
                seen.Add(boardModel.GetLockedSkin(new GridPosition(3, 3)));
            }

            Assert.AreEqual(Board.LOCKED_SKIN_COUNT, seen.Count, "Every approved skin was rolled at least once.");

            BoardSystem systemA = CreateSystem(out BoardModel modelA, out _, out _, ASeederFor(catalog, 7));
            BoardSystem systemB = CreateSystem(out BoardModel modelB, out _, out _, ASeederFor(catalog, 7));
            systemA.StartNewRun();
            systemB.StartNewRun();
            Assert.AreEqual(modelA.GetLockedSkin(new GridPosition(3, 3)), modelB.GetLockedSkin(new GridPosition(3, 3)));

            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void StartNewRun_OnALevelAuthoringNone_LocksNothing()
        {
            var catalog = ACatalogOf("{\"_levelNumber\":1,\"_targetValue\":1}");

            BoardSystem system = CreateSystem(out BoardModel boardModel, out _, out _, ASeederFor(catalog));

            system.StartNewRun();

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    Assert.IsFalse(boardModel.IsLocked(new GridPosition(x, y)));
                }
            }

            Object.DestroyImmediate(catalog);
        }

        /// <summary>A new run inherits nothing: a lock half-opened last run is re-seeded at zero progress
        /// with its authored threshold, and a lock the new level does not author is gone.</summary>
        [Test]
        public void StartNewRun_Twice_ResetsProgressToZeroAndDropsUnauthoredLocks()
        {
            var catalog = ACatalogOf(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_lockedCells\":[{\"_x\":1,\"_y\":1,\"_unlockThreshold\":3}]}");

            BoardSystem system = CreateSystem(out BoardModel boardModel, out _, out _, ASeederFor(catalog));

            system.StartNewRun();
            DestroyAt(boardModel.Board, new GridPosition(1, 2));
            Assert.AreEqual(1, boardModel.GetLockedProgressCount(new GridPosition(1, 1)));
            boardModel.OccupyLocked(new GridPosition(5, 5), COLOUR, 2, SKIN);

            system.StartNewRun();

            Assert.IsTrue(boardModel.IsLocked(new GridPosition(1, 1)), "Re-seeded.");
            Assert.AreEqual(3, boardModel.GetLockedThreshold(new GridPosition(1, 1)));
            Assert.AreEqual(0, boardModel.GetLockedProgressCount(new GridPosition(1, 1)), "At zero progress: no stale count leaks between runs.");
            Assert.IsFalse(boardModel.IsLocked(new GridPosition(5, 5)), "Not authored, so gone.");

            Object.DestroyImmediate(catalog);
        }

        // --- Helpers ---

        /// <summary>Puts a block on <paramref name="position"/> and destroys it through the damage gate —
        /// the one seam every real destruction path goes through.</summary>
        private static void DestroyAt(Board board, GridPosition position)
        {
            if (!board.IsOccupied(position))
            {
                board.Occupy(position, COLOUR);
            }

            Assert.IsTrue(board.TryDamage(position));
        }

        private static void FillRow(Board board, int y)
        {
            for (int x = 0; x < Board.SIZE; x++)
            {
                board.Occupy(new GridPosition(x, y), COLOUR);
            }
        }

        private static LevelLockedCellSeeder ASeederFor(LevelCatalog catalog, int seed = 1)
        {
            var progressionModel = new LevelProgressionModel();
            progressionModel.CurrentLevelNumber.Value = 1;
            return new LevelLockedCellSeeder(
                catalog, progressionModel, new PathRunModel(), new WeightedPieceDraw(seed: 1), seed);
        }

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out TestMessageBroker<PiecePlacedMessage> placedBroker,
            LevelLockedCellSeeder seeder = null)
        {
            return CreateSystem(out boardModel, out trayModel, out placedBroker, out _, seeder);
        }

        private static BoardSystem CreateSystem(
            out BoardModel boardModel,
            out TrayModel trayModel,
            out TestMessageBroker<PiecePlacedMessage> placedBroker,
            out TestMessageBroker<LinesClearedMessage> linesClearedBroker,
            LevelLockedCellSeeder seeder = null)
        {
            boardModel = new BoardModel();
            trayModel = new TrayModel();
            placedBroker = new TestMessageBroker<PiecePlacedMessage>();
            linesClearedBroker = new TestMessageBroker<LinesClearedMessage>();

            // Deliberately unstarted (unless a test starts it), as the reinforced, timer and ice fixtures
            // are: StartNewRun would draw over the board and dock a test lays out by hand.
            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(seed: 1),
                new TestMessageBroker<RunStartedMessage>(),
                placedBroker,
                linesClearedBroker,
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                seed: 1,
                lockedCellSeeder: seeder);
        }

        /// <summary>Authors one catalog row through <see cref="JsonUtility"/>, exactly as
        /// <c>TargetIceCellTests</c> does: the serialized field names are the asset's own contract.</summary>
        private static LevelObjectiveConfig ARow(string json)
        {
            return JsonUtility.FromJson<LevelObjectiveConfig>(json);
        }

        private static LevelCatalog ACatalogOf(params string[] levels)
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            JsonUtility.FromJsonOverwrite($"{{\"_levels\":[{string.Join(",", levels)}]}}", catalog);
            return catalog;
        }
    }
}
