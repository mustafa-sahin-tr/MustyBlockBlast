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
    /// Covers the inventory contract: holding none is a complete no-op, while any valid application is
    /// charged for — including one that happens to clear nothing — and every change survives a restart.
    /// </summary>
    public class PowerUpSystemTests
    {
        private TestMessageBroker<PowerUpAppliedMessage> _appliedBroker;
        private TestMessageBroker<PowerUpGrantedMessage> _grantedBroker;

        /// <summary>The blast channel a power-up fires when its clear destroys an explosive core. A
        /// field rather than an inline broker so a test can assert the blast was announced.</summary>
        private TestMessageBroker<ExplosiveCoreDetonatedMessage> _detonatedBroker;
        private TestMessageBroker<LaserFiredMessage> _laserFiredBroker;

        /// <summary>The pull channel a power-up fires when its clear destroys a vortex tile (issue #156).
        /// A field rather than an inline broker so a test can assert both ends of every move.</summary>
        private TestMessageBroker<VortexPulledMessage> _vortexPulledBroker;

        /// <summary>The coin channel a power-up fires when its clear destroys a coin cell. A field
        /// rather than an inline broker so a test can assert the payout was announced.</summary>
        private TestMessageBroker<CoinCellsClearedMessage> _coinCellsBroker;

        /// <summary>The economy the coin payout is read from. Held on a field so a test can quote the
        /// configured figure rather than restating it.</summary>
        private CurrencyConfig _currencyConfig;

        /// <summary>The frenzy window the double multiplier opens. Kept as a field so a test can read
        /// the model behind it without reaching back through the system under test.</summary>
        private DoubleMultiplierModel _doubleMultiplierModel;
        private DoubleMultiplierSystem _doubleMultiplierSystem;

        /// <summary>The Ghost Fit suggestion the system under test drives. Kept as a field for the same
        /// reason as the frenzy window: a test reads the hint without reaching back through the system.
        /// Assigned by <c>CreateSystem</c>, which is the only place that knows the board and tray it has
        /// to be built against.</summary>
        private GhostFitModel _ghostFitModel;
        private GhostFitSystem _ghostFitSystem;

        /// <summary>The placement channel the Ghost Fit suggestion listens on. Created per test in
        /// <see cref="ClearPersistedInventory"/> so a test can announce a placement and assert the hint
        /// came down with it.</summary>
        private TestMessageBroker<PiecePlacedMessage> _piecePlacedBroker;

        /// <summary>
        /// The progression frontier the level gate reads (see <c>PowerUpUnlockLevels</c>). Held on a
        /// field and opened at <see cref="ALL_KINDS_UNLOCKED_LEVEL"/> by default, so every test about
        /// the inventory contract exercises exactly what it did before the gate existed; the gating
        /// tests lower it themselves.
        /// </summary>
        private LevelProgressionModel _levelProgressionModel;

        /// <summary>A frontier past the last gate in the table, so no kind is withheld.</summary>
        private const int ALL_KINDS_UNLOCKED_LEVEL = 99;

        /// <summary>PowerUpSystem loads the inventory in its constructor, so a count left behind by a
        /// previous test would silently decide whether the next one can spend anything.</summary>
        [SetUp]
        public void ClearPersistedInventory()
        {
            DeleteInventoryKeys();
            _levelProgressionModel = new LevelProgressionModel();
            _levelProgressionModel.CurrentLevelNumber.Value = ALL_KINDS_UNLOCKED_LEVEL;
            _appliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _grantedBroker = new TestMessageBroker<PowerUpGrantedMessage>();
            _detonatedBroker = new TestMessageBroker<ExplosiveCoreDetonatedMessage>();
            _laserFiredBroker = new TestMessageBroker<LaserFiredMessage>();
            _vortexPulledBroker = new TestMessageBroker<VortexPulledMessage>();
            _coinCellsBroker = new TestMessageBroker<CoinCellsClearedMessage>();
            _currencyConfig = ScriptableObject.CreateInstance<CurrencyConfig>();
            _piecePlacedBroker = new TestMessageBroker<PiecePlacedMessage>();
            _doubleMultiplierModel = new DoubleMultiplierModel();
            _doubleMultiplierSystem = new DoubleMultiplierSystem(
                _doubleMultiplierModel,
                new RunPauseModel(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        [TearDown]
        public void ClearPersistedInventoryAfterwards()
        {
            DeleteInventoryKeys();
            if (_currencyConfig != null)
            {
                Object.DestroyImmediate(_currencyConfig);
            }
        }

        // --- Hold as a charged power-up (issue #202) ---

        private static readonly Piece HoldSingle = new Piece("hold_single", new[] { new GridPosition(0, 0) });

        private static readonly Piece HoldPair = new Piece(
            "hold_pair", new[] { new GridPosition(0, 0), new GridPosition(1, 0) });

        private static readonly Piece HoldTriple = new Piece(
            "hold_triple",
            new[] { new GridPosition(0, 0), new GridPosition(0, 1), new GridPosition(0, 2) });

        /// <summary>AC2's negative half, and the inventory contract at its plainest: with no charge the
        /// drop is a true no-op — the dock and the pocket are exactly as they were.</summary>
        [Test]
        public void TryApplyHold_HoldingNone_ParksNothingAndChangesNothing()
        {
            var model = new PowerUpModel();
            TrayModel trayModel = FilledTray();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool parked = system.TryApplyHold(0);

            Assert.IsFalse(parked);
            Assert.IsFalse(trayModel.IsHoldOccupied);
            Assert.AreSame(HoldSingle, trayModel.GetPiece(0));
            Assert.AreEqual(0, model.HoldCount.Value);
        }

        /// <summary>AC2: a park into an empty pocket is exactly the park it always was, and it costs one.</summary>
        [Test]
        public void TryApplyHold_IntoAnEmptyPocket_ParksThePieceAndSpendsOneCharge()
        {
            PersistCount(PowerUpKind.Hold, 2);
            var model = new PowerUpModel();
            TrayModel trayModel = FilledTray();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool parked = system.TryApplyHold(1);

            Assert.IsTrue(parked);
            Assert.AreSame(HoldPair, trayModel.HeldPiece);
            Assert.IsNull(trayModel.GetPiece(1));
            Assert.AreEqual(1, model.HoldCount.Value);
            Assert.AreEqual(1, PlayerPrefs.GetInt(PowerUpInventoryKey.For(PowerUpKind.Hold), -1));
        }

        /// <summary>AC3: swapping a new piece into an occupied pocket is the same one charge as a park —
        /// the previously parked piece comes back, and the count drops again.</summary>
        [Test]
        public void TryApplyHold_IntoAnOccupiedPocket_SwapsAndSpendsOneCharge()
        {
            PersistCount(PowerUpKind.Hold, 2);
            var model = new PowerUpModel();
            TrayModel trayModel = FilledTray();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.TryApplyHold(0);

            bool swapped = system.TryApplyHold(2);

            Assert.IsTrue(swapped);
            Assert.AreSame(HoldTriple, trayModel.HeldPiece);
            Assert.AreSame(HoldSingle, trayModel.GetPiece(2), "The previously parked piece takes the vacated slot.");
            Assert.AreEqual(0, model.HoldCount.Value);
        }

        /// <summary>AC5, the negative test named in the issue: with the last charge already spent on the
        /// park, the swap that would bring the piece back is refused — the dragged piece stays in the
        /// dock and the parked one stays parked.</summary>
        [Test]
        public void TryApplyHold_IntoAnOccupiedPocketHoldingNone_IsRefusedAndStrandsTheParkedPiece()
        {
            PersistCount(PowerUpKind.Hold, 1);
            var model = new PowerUpModel();
            TrayModel trayModel = FilledTray();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.TryApplyHold(0);

            bool swapped = system.TryApplyHold(2);

            Assert.IsFalse(swapped);
            Assert.AreSame(HoldSingle, trayModel.HeldPiece, "The parked piece must be untouched.");
            Assert.AreSame(HoldTriple, trayModel.GetPiece(2), "The dragged piece must stay in its slot.");
            Assert.AreEqual(0, model.HoldCount.Value);
        }

        /// <summary>The "peek before spend" contract: a park the mechanism refuses on its own terms —
        /// here, the last dock piece into an empty pocket — must not be charged for.</summary>
        [Test]
        public void TryApplyHold_WhenTheParkItselfIsRefused_SpendsNothing()
        {
            PersistCount(PowerUpKind.Hold, 1);
            var model = new PowerUpModel();
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, HoldSingle, 1);
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool parked = system.TryApplyHold(0);

            Assert.IsFalse(parked);
            Assert.IsFalse(trayModel.IsHoldOccupied);
            Assert.AreEqual(1, model.HoldCount.Value);
        }

        /// <summary>The count survives a restart exactly as every other kind's does (AC1).</summary>
        [Test]
        public void HoldCount_IsLoadedFromItsOwnPersistedKeyOnConstruction()
        {
            PersistCount(PowerUpKind.Hold, 3);
            var model = new PowerUpModel();

            CreateSystem(model, new BoardModel());

            Assert.AreEqual(3, model.HoldCount.Value);
            Assert.AreEqual("PowerUp.Inventory.Hold", PowerUpInventoryKey.For(PowerUpKind.Hold));
        }

        /// <summary>A park is not an application: nothing is published for the score or badge paths to
        /// misread as a clear, and the parked piece is a permutation rather than a consequence.</summary>
        [Test]
        public void TryApplyHold_PublishesNoApplication()
        {
            PersistCount(PowerUpKind.Hold, 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel(), FilledTray());

            system.TryApplyHold(0);

            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>Issue #203: the pocket's "earn one" tap goes through the same rewarded-ad path as an
        /// empty strip slot, and a grant is a grant — incremented, persisted and announced identically.</summary>
        [Test]
        public void GrantRewardAsync_ForHold_IncrementsPersistsAndPublishes()
        {
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), new StubRewardSource(granted: true));

            bool granted = system.GrantRewardAsync(PowerUpKind.Hold, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(granted);
            Assert.AreEqual(1, model.HoldCount.Value);
            Assert.AreEqual(1, PlayerPrefs.GetInt(PowerUpInventoryKey.For(PowerUpKind.Hold), 0));
            Assert.AreEqual(1, _grantedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Hold, _grantedBroker.Published[0].Kind);
            Assert.AreEqual(1, _grantedBroker.Published[0].NewInventoryCount);
        }

        /// <summary>A declined ad leaves the pocket exactly as it was: nothing banked, nothing announced.</summary>
        [Test]
        public void GrantRewardAsync_ForHoldWhenTheSourceRefuses_ChangesNothing()
        {
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), new StubRewardSource(granted: false));

            bool granted = system.GrantRewardAsync(PowerUpKind.Hold, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(granted);
            Assert.AreEqual(0, model.HoldCount.Value);
            Assert.AreEqual(0, _grantedBroker.Published.Count);
        }

        /// <summary>Hold is invoked by a drag onto the pocket, never armed and aimed: arming it is
        /// refused however many the player holds, like the other targetless kinds.</summary>
        [Test]
        public void Arm_Hold_IsRefusedEvenWhenHeld()
        {
            PersistCount(PowerUpKind.Hold, 1);
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.Hold);

            Assert.IsNull(model.Armed.Value);
        }

        private static TrayModel FilledTray()
        {
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, HoldSingle, 1);
            trayModel.SetSlot(1, HoldPair, 2);
            trayModel.SetSlot(2, HoldTriple, 3);
            return trayModel;
        }

        // --- The Coin Sower's bulk spend (issue #167) ---

        /// <summary>The ordinary case: the player holds what the level-start screen just bought, and the
        /// whole quantity is spent in one call.</summary>
        [Test]
        public void TrySpendCoinSowerBulk_WithEnoughHeld_SpendsExactlyThatMany()
        {
            PersistCount(PowerUpKind.CoinSower, 5);
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool spent = system.TrySpendCoinSowerBulk(3);

            Assert.IsTrue(spent);
            Assert.AreEqual(2, model.CoinSowerCount.Value);
            Assert.AreEqual(2, PlayerPrefs.GetInt(PowerUpInventoryKey.For(PowerUpKind.CoinSower), -1));
        }

        [Test]
        public void TrySpendCoinSowerBulk_ForEverythingHeld_LeavesTheSlotEmpty()
        {
            PersistCount(PowerUpKind.CoinSower, 4);
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            Assert.IsTrue(system.TrySpendCoinSowerBulk(4));
            Assert.AreEqual(0, model.CoinSowerCount.Value);
        }

        /// <summary>
        /// The atomicity guarantee, and the reason the count is checked in full before the first
        /// decrement: asking for one more than is held must leave every unit where it was, not spend all
        /// of them and report failure. A player charged for cells that were never sown is the failure
        /// this rules out.
        /// </summary>
        [Test]
        public void TrySpendCoinSowerBulk_WithTooFewHeld_SpendsNoneOfThem()
        {
            PersistCount(PowerUpKind.CoinSower, 2);
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool spent = system.TrySpendCoinSowerBulk(3);

            Assert.IsFalse(spent);
            Assert.AreEqual(2, model.CoinSowerCount.Value, "No partial decrement.");
            Assert.AreEqual(2, PlayerPrefs.GetInt(PowerUpInventoryKey.For(PowerUpKind.CoinSower), -1));
        }

        [Test]
        public void TrySpendCoinSowerBulk_WithNoneHeld_ChangesNothing()
        {
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            Assert.IsFalse(system.TrySpendCoinSowerBulk(1));
            Assert.AreEqual(0, model.CoinSowerCount.Value);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void TrySpendCoinSowerBulk_WithANonPositiveQuantity_ChangesNothing(int quantity)
        {
            PersistCount(PowerUpKind.CoinSower, 3);
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            Assert.IsFalse(system.TrySpendCoinSowerBulk(quantity));
            Assert.AreEqual(3, model.CoinSowerCount.Value);
        }

        /// <summary>The slot is a persisted inventory slot like every other: units bought at one level
        /// start and left unspent are still there after a relaunch.</summary>
        [Test]
        public void CoinSowerCount_IsLoadedFromPersistenceLikeEveryOtherKind()
        {
            PersistCount(PowerUpKind.CoinSower, 7);
            var model = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(model, new BoardModel());

            Assert.AreEqual(7, model.CoinSowerCount.Value);
        }

        /// <summary>
        /// The kind has no in-run lifecycle at all, so it must not be armable even when held: an armed
        /// Coin Sower would be released onto a board cell by an aim path that falls through to Bomb for
        /// every kind it does not name, spending a bomb the player did not select.
        /// </summary>
        [Test]
        public void Arm_CoinSower_IsRefusedEvenWhenHeld()
        {
            PersistCount(PowerUpKind.CoinSower, 3);
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.CoinSower);

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(3, model.CoinSowerCount.Value);
        }

        // --- Coin cells destroyed by a spent power-up (issue #166, AC3) ---

        /// <summary>AC3: a coin cell destroyed by a Bomb pays exactly as one destroyed by a completed
        /// line does. A bomb's footprint has no line to it, so the payout is the base amount.</summary>
        [Test]
        public void TryApplyBomb_OverACoinCell_AnnouncesTheConfiguredPayout()
        {
            var boardModel = new BoardModel();
            var coin = new GridPosition(4, 4);
            boardModel.Occupy(coin, 1);
            boardModel.SetSpecialKind(coin, SpecialCellKind.Coin);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(coin));

            Assert.AreEqual(1, _coinCellsBroker.Published.Count);
            Assert.AreEqual(_currencyConfig.CoinCellPayout, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>Two coin cells inside one bomb's footprint are one payout, summed — the same
        /// contract the placement path has.</summary>
        [Test]
        public void TryApplyBomb_OverTwoCoinCells_AnnouncesTheirSumOnce()
        {
            var boardModel = new BoardModel();
            var first = new GridPosition(4, 4);
            var second = new GridPosition(5, 5);
            boardModel.Occupy(first, 1);
            boardModel.Occupy(second, 1);
            boardModel.SetSpecialKind(first, SpecialCellKind.Coin);
            boardModel.SetSpecialKind(second, SpecialCellKind.Coin);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            system.TryApplyBomb(first);

            Assert.AreEqual(1, _coinCellsBroker.Published.Count);
            Assert.AreEqual(_currencyConfig.CoinCellPayout * 2, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>A Row Clear destroys along a row, so the axis is a single one and the payout is the
        /// base amount: the doubling is for an intersection, which one emptied line never is.</summary>
        [Test]
        public void TryApplyRowClear_OverACoinCell_AnnouncesTheBasePayout()
        {
            var boardModel = new BoardModel();
            var coin = new GridPosition(2, 3);
            boardModel.Occupy(coin, 1);
            boardModel.SetSpecialKind(coin, SpecialCellKind.Coin);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.RowClear);

            system.TryApplyRowClear(3);

            Assert.AreEqual(1, _coinCellsBroker.Published.Count);
            Assert.AreEqual(_currencyConfig.CoinCellPayout, _coinCellsBroker.Published[0].TotalCoins);
        }

        /// <summary>A power-up that destroyed no coin cell must announce no payout at all.</summary>
        [Test]
        public void TryApplyBomb_WithNoCoinCellInRange_AnnouncesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            system.TryApplyBomb(new GridPosition(4, 4));

            Assert.AreEqual(0, _coinCellsBroker.Published.Count);
        }

        /// <summary>
        /// AC6: a core destroyed by a spent power-up blasts exactly as one destroyed by a completed
        /// line does. The bomb is deliberately not centred on the core — its own 3x3 would then cover
        /// the whole blast and the chain would have nothing left to clear, so the test would pass
        /// whether or not the blast ran at all.
        /// </summary>
        [Test]
        public void TryApplyBomb_OverAnExplosiveCore_DetonatesItAndBlastsBeyondTheBombsOwnFootprint()
        {
            var boardModel = new BoardModel();
            var core = new GridPosition(4, 4);
            boardModel.Occupy(core, 1);
            boardModel.SetSpecialKind(core, SpecialCellKind.ExplosiveCore);
            boardModel.Occupy(new GridPosition(3, 3), 1);

            // Inside the core's blast footprint but outside the bomb's, so only the chain can reach it.
            var blastOnly = new GridPosition(5, 5);
            boardModel.Occupy(blastOnly, 1);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            bool applied = system.TryApplyBomb(new GridPosition(3, 3));

            Assert.IsTrue(applied);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(blastOnly), "The chained blast should reach here.");
            Assert.AreEqual(1, _detonatedBroker.Published.Count);
            Assert.AreEqual(1, _detonatedBroker.Published[0].ClearedCellCount);

            // The bomb still reports only what the bomb itself cleared: the blast is its own event.
            Assert.AreEqual(2, _appliedBroker.Published[0].ClearedCellCount);
        }

        /// <summary>A power-up that destroyed no special cell must publish no blast at all.</summary>
        [Test]
        public void TryApplyBomb_WithNoCoreInRange_PublishesNoDetonation()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            system.TryApplyBomb(new GridPosition(4, 4));

            Assert.AreEqual(0, _detonatedBroker.Published.Count);
        }

        // --- Lasers destroyed by a spent power-up (issue #125, AC5) ---

        /// <summary>
        /// AC5: a laser taken out by a Row Clear fires exactly as one taken out by a completed row does
        /// — the axis the resolver recorded is what decides the wipe, not which power-up was spent. The
        /// column reaches far outside the row the power-up cleared, so only the wipe can explain it.
        /// </summary>
        [Test]
        public void TryApplyRowClear_OverALaser_WipesTheLasersColumn()
        {
            var boardModel = new BoardModel();
            var laser = new GridPosition(2, 3);
            boardModel.Occupy(laser, 1);
            boardModel.SetSpecialKind(laser, SpecialCellKind.Laser);

            boardModel.Occupy(new GridPosition(2, 0), 1);
            boardModel.Occupy(new GridPosition(2, 7), 1);
            var survivor = new GridPosition(5, 5);
            boardModel.Occupy(survivor, 1);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.RowClear);

            Assert.IsTrue(system.TryApplyRowClear(3));

            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(2, 0)));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(2, 7)));
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(survivor), "Outside the wiped column.");

            Assert.AreEqual(1, _laserFiredBroker.Published.Count);
            Assert.AreEqual(2, _laserFiredBroker.Published[0].WipedCellCount);
        }

        /// <summary>AC5's other half: a Column Clear is a column clear, so the laser wipes its row.</summary>
        [Test]
        public void TryApplyColumnClear_OverALaser_WipesTheLasersRow()
        {
            var boardModel = new BoardModel();
            var laser = new GridPosition(3, 2);
            boardModel.Occupy(laser, 1);
            boardModel.SetSpecialKind(laser, SpecialCellKind.Laser);

            boardModel.Occupy(new GridPosition(0, 2), 1);
            boardModel.Occupy(new GridPosition(7, 2), 1);
            var survivor = new GridPosition(5, 5);
            boardModel.Occupy(survivor, 1);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.ColumnClear);

            Assert.IsTrue(system.TryApplyColumnClear(3));

            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(0, 2)));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(7, 2)));
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(survivor), "Outside the wiped row.");

            Assert.AreEqual(1, _laserFiredBroker.Published.Count);
            Assert.AreEqual(2, _laserFiredBroker.Published[0].WipedCellCount);
        }

        /// <summary>AC5 and the axis-less fallback: a Bomb is not a line, so there is no opposite to
        /// compute and the laser wipes both of its lines.</summary>
        [Test]
        public void TryApplyBomb_OverALaser_WipesBothOfItsLines()
        {
            var boardModel = new BoardModel();
            var laser = new GridPosition(4, 4);
            boardModel.Occupy(laser, 1);
            boardModel.SetSpecialKind(laser, SpecialCellKind.Laser);

            // Both far outside the bomb's own clamped 3x3, so only the wipe can reach them.
            boardModel.Occupy(new GridPosition(0, 4), 1);
            boardModel.Occupy(new GridPosition(4, 0), 1);
            var survivor = new GridPosition(0, 0);
            boardModel.Occupy(survivor, 1);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(laser));

            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(0, 4)), "Its row went.");
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(4, 0)), "Its column went.");
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(survivor), "On neither line.");

            Assert.AreEqual(1, _laserFiredBroker.Published.Count);
            Assert.AreEqual(2, _laserFiredBroker.Published[0].WipedCellCount);

            // The bomb still reports only what the bomb itself cleared: the wipe is its own event.
            Assert.AreEqual(1, _appliedBroker.Published[0].ClearedCellCount);
        }

        /// <summary>A power-up that destroyed no laser must publish no wipe at all.</summary>
        [Test]
        public void TryApplyRowClear_WithNoLaserInTheRow_PublishesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(2, 3), 1);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.RowClear);

            system.TryApplyRowClear(3);

            Assert.AreEqual(0, _laserFiredBroker.Published.Count);
        }

        // --- Vortex tiles destroyed by a spent power-up (issue #156, AC1) ---

        /// <summary>
        /// AC1: a vortex destroyed by a Bomb drags the board's isolated blocks inwards exactly as one
        /// destroyed by a completed line does — the same pull, reported through the same seam. The stray
        /// sits far outside the bomb's own 3x3, so only the pull can explain it having moved at all.
        /// </summary>
        [Test]
        public void TryApplyBomb_OverAVortex_PullsTheIsolatedBlocksInwards()
        {
            var boardModel = new BoardModel();
            var vortex = new GridPosition(4, 4);
            boardModel.Occupy(vortex, 1);
            boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            var stray = new GridPosition(0, 0);
            boardModel.Occupy(stray, 2);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(vortex));

            // The tie between the two equal distances goes to the horizontal, which is the rule
            // VortexEffect fixes so the same board always resolves the same way.
            var pulledTo = new GridPosition(1, 0);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(stray), "The cell it left is empty.");
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(pulledTo), "One step inwards.");

            Assert.AreEqual(1, _vortexPulledBroker.Published.Count);
            Assert.AreEqual(1, _vortexPulledBroker.Published[0].Pulls.Count);
            Assert.AreEqual(stray, _vortexPulledBroker.Published[0].Pulls[0].From);
            Assert.AreEqual(pulledTo, _vortexPulledBroker.Published[0].Pulls[0].To);
        }

        /// <summary>AC1 through a line-shaped kind: which power-up was spent decides nothing about the
        /// pull, exactly as it decides nothing about a laser's axis.</summary>
        [Test]
        public void TryApplyRowClear_OverAVortex_PullsTheIsolatedBlocksInwards()
        {
            var boardModel = new BoardModel();
            var vortex = new GridPosition(2, 3);
            boardModel.Occupy(vortex, 1);
            boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            var stray = new GridPosition(7, 7);
            boardModel.Occupy(stray, 2);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.RowClear);

            Assert.IsTrue(system.TryApplyRowClear(3));

            // Five columns away and four rows away, so the larger gap — the horizontal — closes first.
            var pulledTo = new GridPosition(6, 7);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(stray));
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(pulledTo));

            Assert.AreEqual(1, _vortexPulledBroker.Published.Count);
            Assert.AreEqual(stray, _vortexPulledBroker.Published[0].Pulls[0].From);
            Assert.AreEqual(pulledTo, _vortexPulledBroker.Published[0].Pulls[0].To);
        }

        /// <summary>A block with an occupied neighbour is not isolated, so a vortex that found nothing to
        /// move publishes nothing at all — subscribers read the message itself as "blocks moved".</summary>
        [Test]
        public void TryApplyBomb_OverAVortexWithNothingIsolated_PublishesNothing()
        {
            var boardModel = new BoardModel();
            var vortex = new GridPosition(4, 4);
            boardModel.Occupy(vortex, 1);
            boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            var left = new GridPosition(0, 0);
            var right = new GridPosition(1, 0);
            boardModel.Occupy(left, 2);
            boardModel.Occupy(right, 2);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(vortex));

            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(left));
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(right));
            Assert.AreEqual(0, _vortexPulledBroker.Published.Count);
        }

        /// <summary>A power-up that destroyed no vortex must publish no pull at all.</summary>
        [Test]
        public void TryApplyBomb_WithNoVortexInRange_PublishesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            boardModel.Occupy(new GridPosition(0, 0), 2);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            system.TryApplyBomb(new GridPosition(4, 4));

            Assert.AreEqual(0, _vortexPulledBroker.Published.Count);
        }

        /// <summary>
        /// The buffer that reports the pulls belongs to this System's own effect instance, so two
        /// applications in a row must report their own moves and never the sum of both — the reason the
        /// resolution is begun afresh each time.
        /// </summary>
        [Test]
        public void TryApplyBomb_OverASecondVortex_ReportsOnlyThatApplicationsPulls()
        {
            var boardModel = new BoardModel();
            var firstVortex = new GridPosition(4, 4);
            var secondVortex = new GridPosition(4, 0);
            boardModel.Occupy(firstVortex, 1);
            boardModel.Occupy(secondVortex, 1);
            boardModel.SetSpecialKind(firstVortex, SpecialCellKind.Vortex);
            boardModel.SetSpecialKind(secondVortex, SpecialCellKind.Vortex);

            var stray = new GridPosition(0, 7);
            boardModel.Occupy(stray, 2);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(firstVortex));
            Assert.IsTrue(system.TryApplyBomb(secondVortex));

            Assert.AreEqual(2, _vortexPulledBroker.Published.Count);
            Assert.AreEqual(
                1,
                _vortexPulledBroker.Published[1].Pulls.Count,
                "The second application reports its own move, not both.");
        }

        // --- Score gems destroyed by a spent power-up (issue #156, AC3) ---

        /// <summary>
        /// AC3: a gem destroyed by a Bomb is counted on the message that pays for the application, which
        /// is what <c>PowerUpScoreSystem</c> multiplies the whole gain by. Confirming cover for wiring
        /// that already exists: the count is read straight off the triggers before the effects run,
        /// precisely because a gem multiplies the event that destroyed it.
        /// </summary>
        [Test]
        public void TryApplyBomb_OverAScoreGem_ReportsItOnTheAppliedMessage()
        {
            var boardModel = new BoardModel();
            var gem = new GridPosition(4, 4);
            boardModel.Occupy(gem, 1);
            boardModel.SetSpecialKind(gem, SpecialCellKind.ScoreGem);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(gem));

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(1, _appliedBroker.Published[0].DestroyedScoreGemCount);
            Assert.AreEqual(1, _appliedBroker.Published[0].ClearedCellCount, "The gem destroyed nothing extra.");
        }

        /// <summary>AC3 through a line-shaped kind, and the count rather than the flag: two gems in the
        /// cleared row are both counted.</summary>
        [Test]
        public void TryApplyRowClear_OverTwoScoreGems_ReportsBothOfThem()
        {
            var boardModel = new BoardModel();
            var first = new GridPosition(1, 3);
            var second = new GridPosition(5, 3);
            boardModel.Occupy(first, 1);
            boardModel.Occupy(second, 1);
            boardModel.SetSpecialKind(first, SpecialCellKind.ScoreGem);
            boardModel.SetSpecialKind(second, SpecialCellKind.ScoreGem);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.RowClear);

            Assert.IsTrue(system.TryApplyRowClear(3));

            Assert.AreEqual(2, _appliedBroker.Published[0].DestroyedScoreGemCount);
        }

        /// <summary>A power-up that destroyed no gem reports none, so nothing is multiplied.</summary>
        [Test]
        public void TryApplyBomb_WithNoScoreGemInRange_ReportsNone()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            system.TryApplyBomb(new GridPosition(4, 4));

            Assert.AreEqual(0, _appliedBroker.Published[0].DestroyedScoreGemCount);
        }

        [Test]
        public void TryApplyBomb_WithNoBombsHeld_ChangesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyBomb(new GridPosition(4, 4));

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.BombCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(4, 4)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyBomb_WithABombHeld_SpendsItAndClearsTheArea()
        {
            PersistCount(PowerUpKind.Bomb, 2);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            boardModel.Occupy(new GridPosition(5, 5), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyBomb(new GridPosition(4, 4));

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.BombCount.Value);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(4, 4)));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(5, 5)));
            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Bomb, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(2, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyBomb_OverAnEmptyArea_StillSpendsItAndReportsZeroCleared()
        {
            // A deliberate, legal application: the player targeted a cell they were allowed to target,
            // so it is charged for even though it found nothing to destroy.
            PersistCount(PowerUpKind.Bomb, 1);
            var boardModel = new BoardModel();
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyBomb(new GridPosition(4, 4));

            Assert.IsTrue(applied);
            Assert.AreEqual(0, model.BombCount.Value);
            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyBomb_RaisesCellChangedForEveryClearedCell()
        {
            PersistCount(PowerUpKind.Bomb, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 0), 1);
            boardModel.Occupy(new GridPosition(1, 1), 1);
            var changed = new List<GridPosition>();
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            boardModel.CellChanged += (position, colourId) => changed.Add(position);

            system.TryApplyBomb(new GridPosition(0, 0));

            CollectionAssert.AreEquivalent(
                new[] { new GridPosition(0, 0), new GridPosition(1, 1) }, changed);
        }

        [Test]
        public void TryApplyRowClear_WithOneHeld_ClearsAPartiallyFilledRow()
        {
            PersistCount(PowerUpKind.RowClear, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(2, 3), 1);
            boardModel.Occupy(new GridPosition(6, 3), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyRowClear(3);

            Assert.IsTrue(applied);
            Assert.AreEqual(0, model.RowClearCount.Value);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(2, 3)));
            Assert.AreEqual(PowerUpKind.RowClear, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(2, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyColumnClear_WithNoneHeld_ChangesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(5, 0), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColumnClear(5);

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.ColumnClearCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(5, 0)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyColumnClear_WithOneHeld_ClearsTheColumn()
        {
            PersistCount(PowerUpKind.ColumnClear, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(5, 0), 1);
            boardModel.Occupy(new GridPosition(5, 7), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColumnClear(5);

            Assert.IsTrue(applied);
            Assert.AreEqual(0, model.ColumnClearCount.Value);
            Assert.AreEqual(2, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyRowClear_WithAnOutOfBoundsRow_ChangesNothingAndKeepsTheInventory()
        {
            PersistCount(PowerUpKind.RowClear, 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyRowClear(Board.SIZE);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.RowClearCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyBomb_AfterSpending_TheDecrementedCountIsLoadedByANewSystem()
        {
            PersistCount(PowerUpKind.Bomb, 3);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel());
            system.TryApplyBomb(new GridPosition(0, 0));

            // Same prefs, fresh objects — i.e. the next launch.
            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(2, reloadedModel.BombCount.Value);
        }

        [Test]
        public void TryApplyColorCleanser_WithNoneHeld_ChangesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(3, 3), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColorCleanser(new GridPosition(3, 3));

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.ColorCleanserCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(3, 3)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyColorCleanser_OnAnOccupiedCell_ClearsEveryCellOfThatColourOnly()
        {
            PersistCount(PowerUpKind.ColorCleanser, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 0), 1);
            boardModel.Occupy(new GridPosition(7, 7), 1);
            // A different colour, must survive.
            boardModel.Occupy(new GridPosition(4, 4), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColorCleanser(new GridPosition(0, 0));

            Assert.IsTrue(applied);
            Assert.AreEqual(0, model.ColorCleanserCount.Value);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(0, 0)));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(7, 7)));
            Assert.AreEqual(2, boardModel.GetCell(new GridPosition(4, 4)));
            Assert.AreEqual(PowerUpKind.ColorCleanser, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(2, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyColorCleanser_OnAnEmptyCell_IsRejected_KeepsInventoryAndArmedSelection()
        {
            // Mirrors TryApplyJoker's "peek before spend" contract, not the always-spend contract the
            // three region-clearing kinds follow.
            PersistCount(PowerUpKind.ColorCleanser, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(5, 5), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);
            system.Arm(PowerUpKind.ColorCleanser);

            bool applied = system.TryApplyColorCleanser(new GridPosition(2, 2));

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.ColorCleanserCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(5, 5)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.ColorCleanser, model.Armed.Value);
        }

        [Test]
        public void TryApplyColorCleanser_AfterSpending_TheDecrementedCountIsLoadedByANewSystem()
        {
            PersistCount(PowerUpKind.ColorCleanser, 2);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 0), 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.TryApplyColorCleanser(new GridPosition(0, 0));

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.ColorCleanserCount.Value);
        }

        [Test]
        public void TryApplyRotate_OnANonSymmetricalPiece_SwapsInTheRotatedCatalogPieceAndSpendsOne()
        {
            PersistCount(PowerUpKind.Rotate, 2);
            var trayModel = new TrayModel();
            trayModel.SetSlot(1, FindPiece("t_up"), colourId: 3);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyRotate(1);

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.RotateCount.Value);

            // The slot holds a real catalog piece, so its id still describes its shape.
            Assert.AreEqual("t_right", trayModel.GetPiece(1).Id);
            Assert.AreSame(FindPiece("t_right"), trayModel.GetPiece(1));
            Assert.AreEqual(3, trayModel.GetColourId(1));

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Rotate, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyRotate_LeavesTheOtherSlotsAlone()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h5"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("t_up"), colourId: 2);
            trayModel.SetSlot(2, FindPiece("corner3_bl"), colourId: 3);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel(), trayModel);

            system.TryApplyRotate(0);

            Assert.AreEqual("line_v5", trayModel.GetPiece(0).Id);
            Assert.AreEqual("t_up", trayModel.GetPiece(1).Id);
            Assert.AreEqual("corner3_bl", trayModel.GetPiece(2).Id);
        }

        [TestCase("single_1x1")]
        [TestCase("square_2x2")]
        [TestCase("square_3x3")]
        public void TryApplyRotate_OnAFullySymmetricalPiece_IsRejected_KeepsThePieceInventoryAndArm(string id)
        {
            // Same "peek before spend" contract as Joker and ColorCleanser: a piece whose rotation is
            // itself is a dead tap, so nothing is spent and the power-up stays armed to aim again.
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            Piece piece = FindPiece(id);
            trayModel.SetSlot(0, piece, colourId: 4);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);

            bool applied = system.TryApplyRotate(0);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreSame(piece, trayModel.GetPiece(0));
            Assert.AreEqual(4, trayModel.GetColourId(0));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Rotate, model.Armed.Value);
        }

        [Test]
        public void TryApplyRotate_WithNoneHeld_ChangesNothing()
        {
            var trayModel = new TrayModel();
            Piece piece = FindPiece("t_up");
            trayModel.SetSlot(0, piece, colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyRotate(0);

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.RotateCount.Value);
            Assert.AreSame(piece, trayModel.GetPiece(0));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [TestCase(-1)]
        [TestCase(TrayModel.SLOT_COUNT)]
        public void TryApplyRotate_WithAnOutOfRangeSlot_ChangesNothingAndKeepsTheInventory(int slotIndex)
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyRotate(slotIndex);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyRotate_OnAnEmptySlot_ChangesNothingAndKeepsTheInventory()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(2, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyRotate(0);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyRotate_AfterSpending_TheDecrementedCountIsLoadedByANewSystem()
        {
            PersistCount(PowerUpKind.Rotate, 2);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("corner2_missing_tr"), colourId: 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel(), trayModel);
            system.TryApplyRotate(0);

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.RotateCount.Value);
        }

        [Test]
        public void TryApplyRotate_OnSuccess_DropsTheArmedSelection()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);

            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.IsNull(model.Armed.Value);
        }

        /// <summary>
        /// A rotate changes <em>which shapes</em> the player holds, so unlike a park it can take the
        /// last legal move away. <c>BoardSystem</c> is asked to re-check, and the run ends exactly as
        /// the placement that exhausted the board would have ended it.
        /// </summary>
        [Test]
        public void TryApplyRotate_WhenTheTurnedPieceNoLongerFits_EndsTheRun()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var boardModel = new BoardModel();

            // One horizontal two-cell gap — line_h2 fits it, line_v2 cannot — plus five scattered
            // single-cell gaps, none of them vertically adjacent to anything, so line_v2 still fits
            // nowhere. The extra gaps exist only to hold the board under the 90% occupancy that would
            // otherwise earn a demolition hammer (issue #129) and reprieve the run this test is about
            // ending: 57 of 64 cells occupied is 89%.
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0), new GridPosition(1, 0),
                new GridPosition(3, 2), new GridPosition(5, 2), new GridPosition(7, 2),
                new GridPosition(0, 4), new GridPosition(2, 4));

            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h2"), colourId: 1);
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel, trayModel, gameOverBroker);

            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.AreEqual("line_v2", trayModel.GetPiece(0).Id);
            Assert.AreEqual(1, gameOverBroker.Published.Count);
            Assert.AreEqual(GameOverReason.NoMovesLeft, gameOverBroker.Published[0].Reason);
        }

        /// <summary>The other half of the same contract: the re-check is a question, not a verdict, so
        /// a rotate that leaves a move standing must not end anything.</summary>
        [Test]
        public void TryApplyRotate_WhenTheTurnedPieceStillFits_LeavesTheRunAlive()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var boardModel = new BoardModel();

            // The mirror image of the case above: only a vertical gap, so the turned piece is the one
            // that fits and the rotate is what keeps the run alive.
            FillBoardExcept(boardModel, new GridPosition(0, 0), new GridPosition(0, 1));

            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h2"), colourId: 1);
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel, trayModel, gameOverBroker);

            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.AreEqual("line_v2", trayModel.GetPiece(0).Id);
            Assert.AreEqual(0, gameOverBroker.Published.Count);
        }

        /// <summary>
        /// AC1. The expected set is produced by a second draw seeded identically and asked for the same
        /// thing, so the assertion is "all three slots hold the freshly drawn set" without depending on
        /// the order the System happens to pull pieces and colours in.
        /// </summary>
        [Test]
        public void TryApplyReroll_WithOneHeld_ReplacesAllThreeDockPiecesAndSpendsOne()
        {
            const int seed = 12345;
            PersistCount(PowerUpKind.Reroll, 2);

            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("line_h5"), colourId: 2);
            // Deliberately consumed: a reroll restocks every slot, not only the occupied ones.
            trayModel.SetSlot(2, null, Board.EMPTY);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateRerollSystem(
                model,
                boardModel,
                trayModel,
                seed,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            bool applied = system.TryApplyReroll();

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.RerollCount.Value);

            var expectedPieces = new Piece[TrayModel.SLOT_COUNT];
            var expectedColours = new int[TrayModel.SLOT_COUNT];
            new WeightedPieceDraw(seed).TryDrawSolvableSet(
                new BoardModel().Board, expectedPieces, expectedColours);

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.AreSame(expectedPieces[slotIndex], trayModel.GetPiece(slotIndex));
                Assert.AreEqual(expectedColours[slotIndex], trayModel.GetColourId(slotIndex));
            }

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Reroll, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        /// <summary>
        /// AC2. The board is filled but for a 2x2 corner, so most of the catalog cannot be placed at
        /// all — the reroll's guarantee is what makes the set it hands back playable.
        /// </summary>
        [Test]
        public void TryApplyReroll_OnAConstrainedBoard_DrawsASetWithALegalPlacement()
        {
            PersistCount(PowerUpKind.Reroll, 1);

            var boardModel = new BoardModel();
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 1),
                new GridPosition(1, 1));

            var trayModel = new TrayModel();
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                boardModel,
                trayModel,
                drawSeed: 7,
                new TestMessageBroker<TrayRefilledMessage>(),
                gameOverBroker);

            Assert.IsTrue(system.TryApplyReroll());

            var drawn = new List<Piece>(TrayModel.SLOT_COUNT);
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.IsNotNull(trayModel.GetPiece(slotIndex));
                drawn.Add(trayModel.GetPiece(slotIndex));
            }

            Assert.IsTrue(MoveAvailability.HasAnyMove(boardModel.Board, drawn));

            // The other half of the same claim: a set with a move in it cannot have ended the run.
            Assert.AreEqual(0, gameOverBroker.Published.Count);
        }

        /// <summary>AC3. Holding none is a complete no-op — nothing drawn, nothing spent.</summary>
        [Test]
        public void TryApplyReroll_WithNoneHeld_ChangesNothing()
        {
            var trayModel = new TrayModel();
            Piece first = FindPiece("t_up");
            Piece second = FindPiece("line_h2");
            trayModel.SetSlot(0, first, colourId: 1);
            trayModel.SetSlot(1, second, colourId: 2);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);

            bool applied = system.TryApplyReroll();

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.RerollCount.Value);
            Assert.AreSame(first, trayModel.GetPiece(0));
            Assert.AreEqual(1, trayModel.GetColourId(0));
            Assert.AreSame(second, trayModel.GetPiece(1));
            Assert.IsNull(trayModel.GetPiece(2));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>
        /// A reroll is a discard, not a dock played out, so it must not masquerade as a refill: the one
        /// subscriber of that message restarts the timed-mode countdown from full on it.
        /// </summary>
        [Test]
        public void TryApplyReroll_DoesNotPublishTrayRefilled()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            var trayRefilledBroker = new TestMessageBroker<TrayRefilledMessage>();
            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                new BoardModel(),
                new TrayModel(),
                drawSeed: 3,
                trayRefilledBroker,
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(0, trayRefilledBroker.Published.Count);
        }

        /// <summary>A parked piece is not on offer, so it is not part of what a reroll discards.</summary>
        [Test]
        public void TryApplyReroll_LeavesTheHoldSlotAlone()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h2"), colourId: 1);
            trayModel.SetHeld(FindPiece("square_3x3"), colourId: 2);

            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                new BoardModel(),
                trayModel,
                drawSeed: 11,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreSame(FindPiece("square_3x3"), trayModel.HeldPiece);
            Assert.AreEqual(2, trayModel.HeldColourId);
        }

        /// <summary>Reroll has no target, so there is nothing to aim it at and it must never become an
        /// armed selection — an armed reroll could only be released onto a cell that means nothing to
        /// it.</summary>
        [Test]
        public void Arm_WithReroll_DoesNotArmIt()
        {
            PersistCount(PowerUpKind.Reroll, 3);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.Reroll);

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(3, model.RerollCount.Value);
        }

        /// <summary>Applying a reroll while another kind is armed drops that selection, so the clock
        /// hold an armed kind carries is never stranded behind it.</summary>
        [Test]
        public void TryApplyReroll_WhileAnotherKindIsArmed_DropsThatSelection()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            PersistCount(PowerUpKind.Bomb, 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateRerollSystem(
                model,
                new BoardModel(),
                new TrayModel(),
                drawSeed: 5,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());
            system.Arm(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyReroll());

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(1, model.BombCount.Value);
        }

        [Test]
        public void TryApplyReroll_AfterSpending_TheDecrementedCountIsLoadedByANewSystem()
        {
            PersistCount(PowerUpKind.Reroll, 2);
            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                new BoardModel(),
                new TrayModel(),
                drawSeed: 9,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());
            system.TryApplyReroll();

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.RerollCount.Value);
        }

        /// <summary>
        /// AC5, end to end: on a full board no set can satisfy the guarantee, so the bounded draw gives
        /// up and hands back its last attempt. The player still gets three real pieces and is still
        /// charged for the reroll, and the re-check runs exactly as an exhausted board would make it —
        /// the same contract as a rotate that leaves nothing placeable. Since issue #129 that re-check
        /// reprieves a board this full with a demolition hammer rather than ending the run.
        /// </summary>
        [Test]
        public void TryApplyReroll_OnABoardNoPieceFits_IsStillSpentAndEarnsTheDemolitionHammer()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            var boardModel = new BoardModel();
            FillBoardExcept(boardModel);

            var trayModel = new TrayModel();
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateRerollSystem(
                model,
                boardModel,
                trayModel,
                drawSeed: 13,
                new TestMessageBroker<TrayRefilledMessage>(),
                gameOverBroker);

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(0, model.RerollCount.Value);
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Assert.IsNotNull(trayModel.GetPiece(slotIndex), "The fallback must still restock the dock.");
            }

            // Issue #129: a dead board that is also at least 90% full no longer ends the run outright —
            // it earns a one-off demolition hammer instead, injected into the dock as a last-resort
            // life-line. The reroll is still spent and the dock is still restocked (this test's real
            // subject); what changes is that the verdict is deferred rather than published.
            Assert.AreEqual(0, gameOverBroker.Published.Count);
            Assert.IsTrue(
                trayModel.GetSpecialKind(0) == SpecialPieceKind.DemolitionHammer,
                "The dead board should have been reprieved by a demolition hammer.");
        }

        /// <summary>Issue #95 AC1: the dock has zero legal placements before the reroll, and the
        /// guaranteed-solvable draw rescues it — this is exactly what "clutch" means for the
        /// RerollSave objective.</summary>
        [Test]
        public void TryApplyReroll_DockHadNoLegalMoves_PublishesWasClutchSaveTrue()
        {
            PersistCount(PowerUpKind.Reroll, 1);

            var boardModel = new BoardModel();
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 1),
                new GridPosition(1, 1));

            var trayModel = new TrayModel();
            // A 3x3 square cannot fit in a 2x2 opening, so none of these three qualify — the dock
            // starts with zero legal placements.
            trayModel.SetSlot(0, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(2, FindPiece("square_3x3"), colourId: 1);

            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                boardModel,
                trayModel,
                drawSeed: 7,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.IsTrue(_appliedBroker.Published[0].WasClutchSave);
        }

        /// <summary>Issue #95 AC2 (negative case): the dock already had a legal placement before the
        /// reroll, so however good the fresh draw is, this was never a rescue.</summary>
        [Test]
        public void TryApplyReroll_DockAlreadyHadALegalMove_PublishesWasClutchSaveFalse()
        {
            PersistCount(PowerUpKind.Reroll, 1);

            var boardModel = new BoardModel();
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 1),
                new GridPosition(1, 1));

            var trayModel = new TrayModel();
            // A 1x1 fits the 2x2 opening, so the dock already has a legal move before the reroll.
            trayModel.SetSlot(0, FindPiece("single_1x1"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(2, FindPiece("square_3x3"), colourId: 1);

            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                boardModel,
                trayModel,
                drawSeed: 7,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.IsFalse(_appliedBroker.Published[0].WasClutchSave);
        }

        /// <summary>
        /// Pins the Hold-slot exclusion documented in docs/game-design.md: the parked piece is not part
        /// of the "zero legal moves" pre-check, so a dead dock still counts as clutch even when the
        /// pocket held a piece that fits. This is also the only shape of this case a live run can ever
        /// reach — <c>CheckGameOver</c> counts the held piece, so a dead dock with an empty pocket has
        /// already ended the run and <c>TryRerollTray</c> would refuse outright.
        /// </summary>
        [Test]
        public void TryApplyReroll_DockDeadButHeldPieceFits_StillPublishesWasClutchSaveTrue()
        {
            PersistCount(PowerUpKind.Reroll, 1);

            var boardModel = new BoardModel();
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0),
                new GridPosition(1, 0),
                new GridPosition(0, 1),
                new GridPosition(1, 1));

            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("square_3x3"), colourId: 1);
            trayModel.SetSlot(2, FindPiece("square_3x3"), colourId: 1);
            // Fits the 2x2 opening — but it is parked, not on offer.
            trayModel.SetHeld(FindPiece("single_1x1"), colourId: 2);

            PowerUpSystem system = CreateRerollSystem(
                new PowerUpModel(),
                boardModel,
                trayModel,
                drawSeed: 7,
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<GameOverMessage>());

            Assert.IsTrue(system.TryApplyReroll());

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.IsTrue(_appliedBroker.Published[0].WasClutchSave);
        }

        /// <summary>A run that is already over refuses the reroll outright: nothing is drawn, the dock
        /// is left as it was and nothing is spent.</summary>
        [Test]
        public void TryApplyReroll_WhenTheRunIsAlreadyOver_ChangesNothing()
        {
            PersistCount(PowerUpKind.Reroll, 1);
            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            Piece parked = FindPiece("line_h2");
            trayModel.SetSlot(0, parked, colourId: 1);

            BoardSystem boardSystem = CreateBoardSystem(
                boardModel, trayModel, new TestMessageBroker<GameOverMessage>());
            boardSystem.ForceGameOver(GameOverReason.TimeUp);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(
                model, boardModel, trayModel, new StubRewardSource(granted: true), boardSystem);

            Assert.IsFalse(system.TryApplyReroll());

            Assert.AreEqual(1, model.RerollCount.Value);
            Assert.AreSame(parked, trayModel.GetPiece(0));
            Assert.IsNull(trayModel.GetPiece(1));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void GrantRewardAsync_WhenTheSourceGrants_IncrementsPersistsAndPublishes()
        {
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), new StubRewardSource(granted: true));

            bool granted = system.GrantRewardAsync(PowerUpKind.RowClear, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(granted);
            Assert.AreEqual(1, model.RowClearCount.Value);
            Assert.AreEqual(1, PlayerPrefs.GetInt(PowerUpInventoryKey.For(PowerUpKind.RowClear), 0));
            Assert.AreEqual(1, _grantedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.RowClear, _grantedBroker.Published[0].Kind);
            Assert.AreEqual(1, _grantedBroker.Published[0].NewInventoryCount);
        }

        [Test]
        public void GrantRewardAsync_WhenTheSourceRefuses_ChangesNothing()
        {
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), new StubRewardSource(granted: false));

            bool granted = system.GrantRewardAsync(PowerUpKind.Bomb, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(granted);
            Assert.AreEqual(0, model.BombCount.Value);
            Assert.AreEqual(0, _grantedBroker.Published.Count);
        }

        [Test]
        public void GrantRewardAsync_ThenANewSystem_LoadsTheGrantedCount()
        {
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel());
            system.GrantRewardAsync(PowerUpKind.ColumnClear, CancellationToken.None).GetAwaiter().GetResult();

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.ColumnClearCount.Value);
        }

        [Test]
        public void TryApplyDoubleMultiplier_WithNoneHeld_ChangesNothing()
        {
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyDoubleMultiplier();

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.DoubleMultiplierCount.Value);
            Assert.IsFalse(_doubleMultiplierModel.IsActive);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyDoubleMultiplier_WithOneHeld_SpendsItAndOpensAFullWindow()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyDoubleMultiplier();

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.DoubleMultiplierCount.Value);
            Assert.IsTrue(_doubleMultiplierModel.IsActive);
            Assert.AreEqual(
                DoubleMultiplierModel.WINDOW_SECONDS, _doubleMultiplierModel.RemainingSeconds.Value);
        }

        /// <summary>The badge counter that tracks "power-ups used" has to see this kind too, even
        /// though it clears nothing — the same contract Rotate and Reroll follow.</summary>
        [Test]
        public void TryApplyDoubleMultiplier_PublishesAnApplicationThatClearedNothing()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel());

            system.TryApplyDoubleMultiplier();

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.DoubleMultiplier, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        /// <summary>Targetless, so it is never an armed selection — exactly like the reroll.</summary>
        [Test]
        public void Arm_DoubleMultiplier_IsRefusedEvenWhenHeld()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 3);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.DoubleMultiplier);

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(3, model.DoubleMultiplierCount.Value);
        }

        [Test]
        public void TryApplyGhostFit_WithNoneHeld_ChangesNothing()
        {
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), TrayWithASinglePiece());

            bool applied = system.TryApplyGhostFit();

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.GhostFitCount.Value);
            Assert.AreEqual(GhostFitHintState.None, _ghostFitModel.Hint.Value.State);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyGhostFit_WithOneHeld_SpendsItAndSuggestsAMove()
        {
            PersistCount(PowerUpKind.GhostFit, 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), TrayWithASinglePiece());

            bool applied = system.TryApplyGhostFit();

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.GhostFitCount.Value);
            Assert.AreEqual(GhostFitHintState.Suggested, _ghostFitModel.Hint.Value.State);
            Assert.AreEqual(1, _ghostFitModel.SuggestedSlotIndex);
        }

        /// <summary>The suggestion describes a board that no longer exists once a piece lands on it, so
        /// the placement takes it down. This is also what ends the one case the input View deliberately
        /// keeps alive: dragging the suggested piece leaves the silhouette up to aim at, and the drop is
        /// what finally clears it.</summary>
        [Test]
        public void APlacement_DismissesTheGhostFitSuggestion()
        {
            PersistCount(PowerUpKind.GhostFit, 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel(), TrayWithASinglePiece());
            system.TryApplyGhostFit();
            Assert.AreEqual(GhostFitHintState.Suggested, _ghostFitModel.Hint.Value.State);

            _piecePlacedBroker.Publish(APlacementOf("single"));

            Assert.AreEqual(GhostFitHintState.None, _ghostFitModel.Hint.Value.State);
        }

        /// <summary>
        /// Acceptance criterion 3's distinction, at the System boundary the input View calls into:
        /// reaching for the suggested piece is the player acting on the hint, so the silhouette stays up
        /// for them to aim at.
        /// </summary>
        [Test]
        public void DismissUnlessSuggestedSlot_WithTheSuggestedSlot_KeepsTheSuggestion()
        {
            PersistCount(PowerUpKind.GhostFit, 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel(), TrayWithASinglePiece());
            system.TryApplyGhostFit();
            int suggestedSlot = _ghostFitModel.SuggestedSlotIndex;

            _ghostFitSystem.DismissUnlessSuggestedSlot(suggestedSlot);

            Assert.AreEqual(GhostFitHintState.Suggested, _ghostFitModel.Hint.Value.State);
            Assert.AreEqual(suggestedSlot, _ghostFitModel.SuggestedSlotIndex);
        }

        /// <summary>The other half of the same criterion: picking up any <em>other</em> piece is the
        /// player ignoring the hint, and drops it at once.</summary>
        [Test]
        public void DismissUnlessSuggestedSlot_WithAnyOtherSlot_DropsTheSuggestion()
        {
            PersistCount(PowerUpKind.GhostFit, 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel(), TrayWithASinglePiece());
            system.TryApplyGhostFit();
            int suggestedSlot = _ghostFitModel.SuggestedSlotIndex;

            _ghostFitSystem.DismissUnlessSuggestedSlot(suggestedSlot == 0 ? 1 : 0);

            Assert.AreEqual(GhostFitHintState.None, _ghostFitModel.Hint.Value.State);
        }

        /// <summary>The badge counter that tracks "power-ups used" has to see this kind too, even though
        /// it changes nothing at all — the same contract Rotate, Reroll and Double Multiplier follow.</summary>
        [Test]
        public void TryApplyGhostFit_PublishesAnApplicationThatClearedNothing()
        {
            PersistCount(PowerUpKind.GhostFit, 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel(), TrayWithASinglePiece());

            system.TryApplyGhostFit();

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.GhostFit, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        /// <summary>The negative case: a dock with no legal placement anywhere is reported as such, and
        /// the player is not charged for being told so.</summary>
        [Test]
        public void TryApplyGhostFit_WithNoLegalPlacementAnywhere_ReportsItAndSpendsNothing()
        {
            PersistCount(PowerUpKind.GhostFit, 1);
            var boardModel = new BoardModel();
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    boardModel.Occupy(new GridPosition(x, y), 1);
                }
            }

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel, TrayWithASinglePiece());

            bool applied = system.TryApplyGhostFit();

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.GhostFitCount.Value);
            Assert.AreEqual(GhostFitHintState.NoPlacements, _ghostFitModel.Hint.Value.State);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>A second tap on the icon takes the suggestion back down, the same way tapping an
        /// armed kind's icon cancels it — and costs nothing.</summary>
        [Test]
        public void TryApplyGhostFit_WhileAlreadySuggesting_DismissesAndSpendsNothing()
        {
            PersistCount(PowerUpKind.GhostFit, 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), TrayWithASinglePiece());
            system.TryApplyGhostFit();

            bool applied = system.TryApplyGhostFit();

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.GhostFitCount.Value);
            Assert.AreEqual(GhostFitHintState.None, _ghostFitModel.Hint.Value.State);
            Assert.AreEqual(1, _appliedBroker.Published.Count);
        }

        /// <summary>Another power-up's application changed the board or the dock the suggestion was
        /// computed from, so the suggestion goes with it rather than pointing at a board that moved.</summary>
        [Test]
        public void ApplyingAnotherPowerUp_DismissesTheGhostFitSuggestion()
        {
            PersistCount(PowerUpKind.GhostFit, 1);
            PersistCount(PowerUpKind.Bomb, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel, TrayWithASinglePiece());
            system.TryApplyGhostFit();

            system.TryApplyBomb(new GridPosition(4, 4));

            Assert.AreEqual(GhostFitHintState.None, _ghostFitModel.Hint.Value.State);
        }

        /// <summary>Targetless, so it is never an armed selection — exactly like the reroll and the
        /// double multiplier.</summary>
        [Test]
        public void Arm_GhostFit_IsRefusedEvenWhenHeld()
        {
            PersistCount(PowerUpKind.GhostFit, 3);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), TrayWithASinglePiece());

            system.Arm(PowerUpKind.GhostFit);

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(3, model.GhostFitCount.Value);
        }

        /// <summary>Re-activating restarts the window rather than stacking: the power-up is a doubling,
        /// not a multiplier that compounds with itself.</summary>
        [Test]
        public void TryApplyDoubleMultiplier_Twice_RestartsTheWindowRatherThanExtendingIt()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 2);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), new BoardModel());

            system.TryApplyDoubleMultiplier();
            _doubleMultiplierSystem.Advance(10f);
            system.TryApplyDoubleMultiplier();

            Assert.AreEqual(
                DoubleMultiplierModel.WINDOW_SECONDS, _doubleMultiplierModel.RemainingSeconds.Value);
        }

        [Test]
        public void TryApplyDoubleMultiplier_WithTheRunOver_IsRefused()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 1);
            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            BoardSystem boardSystem = CreateBoardSystem(
                boardModel, trayModel, new TestMessageBroker<GameOverMessage>());
            boardSystem.ForceGameOver(GameOverReason.TimeUp);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(
                model, boardModel, trayModel, new StubRewardSource(granted: true), boardSystem);

            bool applied = system.TryApplyDoubleMultiplier();

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.DoubleMultiplierCount.Value);
            Assert.IsFalse(_doubleMultiplierModel.IsActive);
        }

        /// <summary>
        /// A kind behind its level gate is refused exactly as one the player holds none of is: the
        /// inventory is irrelevant, so this deliberately stocks three of them first. Joker unlocks at
        /// level 5 (<see cref="PowerUpUnlockLevels"/>), so a frontier of 4 is one short.
        /// </summary>
        [Test]
        public void Arm_WithAKindBelowItsUnlockLevel_IsRefusedEvenWhenHeld()
        {
            PersistCount(PowerUpKind.Joker, 3);
            _levelProgressionModel.CurrentLevelNumber.Value = 4;
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.Joker);

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(3, model.JokerCount.Value);
        }

        [Test]
        public void Arm_WithAKindAtItsUnlockLevel_Arms()
        {
            PersistCount(PowerUpKind.Joker, 1);
            _levelProgressionModel.CurrentLevelNumber.Value = 5;
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.Joker);

            Assert.AreEqual(PowerUpKind.Joker, model.Armed.Value);
        }

        /// <summary>The starter three carry no gate at all, so a fresh install's level 1 is enough.</summary>
        [Test]
        public void Arm_WithAnUngatedKind_ArmsAtTheFirstLevel()
        {
            PersistCount(PowerUpKind.Bomb, 1);
            _levelProgressionModel.CurrentLevelNumber.Value = 1;
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.Bomb);

            Assert.AreEqual(PowerUpKind.Bomb, model.Armed.Value);
        }

        /// <summary>
        /// The gate is enforced at the point of spending too, not only at arming: a targeted
        /// application that somehow arrives for a locked kind must change nothing and charge nothing.
        /// </summary>
        [Test]
        public void TryApplyJoker_WithTheKindBelowItsUnlockLevel_ChangesNothing()
        {
            PersistCount(PowerUpKind.Joker, 2);
            _levelProgressionModel.CurrentLevelNumber.Value = 4;
            var boardModel = new BoardModel();
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyJoker(new GridPosition(3, 3));

            Assert.IsFalse(applied);
            Assert.AreEqual(2, model.JokerCount.Value);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(3, 3)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>The targetless kinds go through the same gate as the aimed ones.</summary>
        [Test]
        public void TryApplyDoubleMultiplier_WithTheKindBelowItsUnlockLevel_ChangesNothing()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 1);
            _levelProgressionModel.CurrentLevelNumber.Value = 24;
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyDoubleMultiplier();

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.DoubleMultiplierCount.Value);
            Assert.IsFalse(_doubleMultiplierModel.IsActive);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyBomb_WithAnUngatedKind_AppliesAtTheFirstLevel()
        {
            PersistCount(PowerUpKind.Bomb, 1);
            _levelProgressionModel.CurrentLevelNumber.Value = 1;
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            Assert.IsTrue(system.TryApplyBomb(new GridPosition(4, 4)));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(4, 4)));
        }

        /// <summary>
        /// AC3, at the system layer: the gate is read live, so crossing a kind's unlock level makes it
        /// usable in the same session. Nothing is rebuilt or reloaded between the refusal and the
        /// acceptance below — the only thing that changes is the frontier.
        /// </summary>
        [Test]
        public void ReachingTheUnlockLevel_MakesTheKindUsableWithoutRebuildingAnything()
        {
            PersistCount(PowerUpKind.Joker, 1);
            _levelProgressionModel.CurrentLevelNumber.Value = 4;
            var boardModel = new BoardModel();
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            Assert.IsFalse(system.TryApplyJoker(new GridPosition(3, 3)), "locked before the level up");

            _levelProgressionModel.CurrentLevelNumber.Value = 5;

            Assert.IsTrue(system.TryApplyJoker(new GridPosition(3, 3)), "unlocked by the level up alone");
            Assert.AreEqual(0, model.JokerCount.Value);
        }

        /// <summary>
        /// AC4's negative case. A kind held while its gate is ahead of the player keeps every one of
        /// its counts: the gate refuses to spend an inventory, it never confiscates one — not on
        /// construction (which loads the persisted counts), not on a refused arm, not on a refused
        /// apply, and not when the frontier moves backwards under it.
        /// </summary>
        [Test]
        public void AKindHeldBelowItsUnlockLevel_KeepsItsCountThroughEveryRefusal()
        {
            PersistCount(PowerUpKind.GhostFit, 4);
            _levelProgressionModel.CurrentLevelNumber.Value = 1;
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            Assert.AreEqual(4, model.GhostFitCount.Value, "loaded despite the gate");

            system.Arm(PowerUpKind.GhostFit);
            system.TryApplyGhostFit();

            Assert.AreEqual(4, model.GhostFitCount.Value, "untouched by the refusals");
            Assert.AreEqual(
                4,
                PlayerPrefs.GetInt(PowerUpInventoryKey.For(PowerUpKind.GhostFit), 0),
                "and never rewritten behind them");
        }

        /// <summary>Builds a system whose reroll draws are reproducible, and hands back the brokers and
        /// board system the reroll tests need to observe.</summary>
        private PowerUpSystem CreateRerollSystem(
            PowerUpModel model,
            BoardModel boardModel,
            TrayModel trayModel,
            int drawSeed,
            TestMessageBroker<TrayRefilledMessage> trayRefilledBroker,
            TestMessageBroker<GameOverMessage> gameOverBroker)
        {
            BoardSystem boardSystem = CreateBoardSystem(
                boardModel,
                trayModel,
                gameOverBroker,
                new WeightedPieceDraw(drawSeed),
                trayRefilledBroker);

            return CreateSystem(
                model, boardModel, trayModel, new StubRewardSource(granted: true), boardSystem);
        }

        private PowerUpSystem CreateSystem(PowerUpModel model, BoardModel boardModel)
        {
            return CreateSystem(model, boardModel, new StubRewardSource(granted: true));
        }

        private PowerUpSystem CreateSystem(PowerUpModel model, BoardModel boardModel, IRewardSource rewardSource)
        {
            return CreateSystem(model, boardModel, new TrayModel(), rewardSource);
        }

        private PowerUpSystem CreateSystem(PowerUpModel model, BoardModel boardModel, TrayModel trayModel)
        {
            return CreateSystem(model, boardModel, trayModel, new StubRewardSource(granted: true));
        }

        /// <summary>For the game-over re-check tests: routes the board system's game-over messages to a
        /// broker the test can read, which is how it observes whether the rotate ended the run.</summary>
        private PowerUpSystem CreateSystem(
            PowerUpModel model,
            BoardModel boardModel,
            TrayModel trayModel,
            TestMessageBroker<GameOverMessage> gameOverBroker)
        {
            return CreateSystem(
                model,
                boardModel,
                trayModel,
                new StubRewardSource(granted: true),
                CreateBoardSystem(boardModel, trayModel, gameOverBroker));
        }

        private PowerUpSystem CreateSystem(
            PowerUpModel model, BoardModel boardModel, TrayModel trayModel, IRewardSource rewardSource)
        {
            return CreateSystem(
                model, boardModel, trayModel, rewardSource, CreateBoardSystem(boardModel, trayModel));
        }

        private PowerUpSystem CreateSystem(
            PowerUpModel model,
            BoardModel boardModel,
            TrayModel trayModel,
            IRewardSource rewardSource,
            BoardSystem boardSystem)
        {
            _ghostFitModel = new GhostFitModel();
            _ghostFitSystem = new GhostFitSystem(
                _ghostFitModel,
                boardModel,
                trayModel,
                new ScoreModel(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                // Held on a field so a test can publish a placement through it and watch the suggestion
                // go down, which is how the run-of-play dismissal is observed without a scene.
                _piecePlacedBroker,
                // The very broker the system under test publishes through, so "another power-up was
                // applied, drop the suggestion" is live here rather than stubbed out.
                _appliedBroker);

            return new PowerUpSystem(
                model,
                _levelProgressionModel,
                boardModel,
                trayModel,
                boardSystem,
                CreateTimerRunSystem(boardSystem),
                _doubleMultiplierSystem,
                _ghostFitSystem,
                _ghostFitModel,
                rewardSource,
                _appliedBroker,
                _grantedBroker,
                _detonatedBroker,
                _laserFiredBroker,
                _vortexPulledBroker,
                _coinCellsBroker,
                _currencyConfig,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        /// <summary>
        /// A real, unstarted <see cref="BoardSystem"/>: <c>PowerUpSystem</c> reads only its
        /// <c>IsGameOver</c> flag, which is false until the run is started or checked, so the board
        /// each test set up by hand is left exactly as it was.
        /// </summary>
        private static BoardSystem CreateBoardSystem(BoardModel boardModel, TrayModel trayModel)
        {
            return CreateBoardSystem(boardModel, trayModel, new TestMessageBroker<GameOverMessage>());
        }

        private static BoardSystem CreateBoardSystem(
            BoardModel boardModel, TrayModel trayModel, TestMessageBroker<GameOverMessage> gameOverBroker)
        {
            return CreateBoardSystem(
                boardModel,
                trayModel,
                gameOverBroker,
                new WeightedPieceDraw(),
                new TestMessageBroker<TrayRefilledMessage>());
        }

        /// <summary>For the reroll tests, which need a seeded draw (so the set is reproducible) and a
        /// readable tray-refill broker (so "a reroll is not a refill" can be asserted).</summary>
        private static BoardSystem CreateBoardSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            TestMessageBroker<GameOverMessage> gameOverBroker,
            WeightedPieceDraw pieceDraw,
            TestMessageBroker<TrayRefilledMessage> trayRefilledBroker)
        {
            return new BoardSystem(
                boardModel,
                trayModel,
                new PerfectRoundModel(),
                pieceDraw,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                gameOverBroker,
                trayRefilledBroker,
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                reinforcedCellSeeder: null);
        }

        /// <summary>Occupies every board cell except the ones named, so a test can state the one gap it
        /// wants a piece to have to fit into.</summary>
        private static void FillBoardExcept(BoardModel boardModel, params GridPosition[] emptyCells)
        {
            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    var position = new GridPosition(x, y);
                    if (!Contains(emptyCells, position))
                    {
                        boardModel.Occupy(position, 1);
                    }
                }
            }
        }

        private static bool Contains(GridPosition[] cells, GridPosition cell)
        {
            for (int index = 0; index < cells.Length; index++)
            {
                if (cells[index].Equals(cell))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Only ever asked to hold and release the countdown here; it is never ticked.</summary>
        private static TimerRunSystem CreateTimerRunSystem(BoardSystem boardSystem)
        {
            var timedModeConfig = ScriptableObject.CreateInstance<TimedModeConfig>();
            return new TimerRunSystem(
                new TimerModel(),
                new RunPauseModel(),
                new GameModeSystem(new GameModeModel(), boardSystem),
                new TimedModeSystem(new TimedModeModel(), timedModeConfig),
                boardSystem,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        /// <summary>A dock holding one piece, in the middle slot, so a suggestion that names slot 1 can
        /// only have come from searching the dock rather than defaulting to the first slot.</summary>
        /// <summary>A placement announcement with everything but the piece id at its default. Nothing
        /// subscribed here reads the rest, and spelling all fourteen arguments out at each call site
        /// would bury what the test is actually about.</summary>
        private static PiecePlacedMessage APlacementOf(string pieceId)
            => new PiecePlacedMessage(
                pieceId,
                new GridPosition(0, 0),
                PieceFamily.Single,
                cellCount: 1,
                colourId: 1,
                linesCleared: 0,
                rowsCleared: 0,
                columnsCleared: 0,
                monochromeLineCount: 0,
                boardEmptyAfterPlacement: false,
                occupiedCellCountBeforeClear: 1,
                anyCornerCleared: false,
                centerCoreEmptyAfterPlacement: true,
                hasIsolatedHolesAfterPlacement: false);

        private static TrayModel TrayWithASinglePiece()
        {
            var trayModel = new TrayModel();
            trayModel.SetSlot(1, new Piece("single", new[] { new GridPosition(0, 0) }), colourId: 2);
            return trayModel;
        }

        private static void PersistCount(PowerUpKind kind, int count)
        {
            PlayerPrefs.SetInt(PowerUpInventoryKey.For(kind), count);
        }

        private static void DeleteInventoryKeys()
        {
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Bomb));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.RowClear));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.ColumnClear));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Joker));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.ColorCleanser));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Rotate));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Reroll));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.DoubleMultiplier));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.GhostFit));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.CoinSower));
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.Hold));
        }

        private static Piece FindPiece(string id)
        {
            foreach (Piece piece in PieceCatalog.AllPieces)
            {
                if (piece.Id == id)
                {
                    return piece;
                }
            }

            Assert.Fail($"Piece '{id}' not found in catalog.");
            return null;
        }

        /// <summary>Completes synchronously so these stay plain synchronous EditMode tests.</summary>
        private sealed class StubRewardSource : IRewardSource
        {
            private readonly bool _granted;

            internal StubRewardSource(bool granted)
            {
                _granted = granted;
            }

            public UniTask<RewardResult> RequestRewardAsync(PowerUpKind kind, CancellationToken cancellationToken)
            {
                return UniTask.FromResult(new RewardResult(kind, _granted));
            }
        }
    }
}
