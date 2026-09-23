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

        /// <summary>The vortex channel a power-up fires when its clear destroys a vortex tile (issue
        /// #156 / issue #349). A field rather than an inline broker so a test can assert the fill or the
        /// hand-off it reported.</summary>
        private TestMessageBroker<VortexIslandFilledMessage> _vortexIslandFilledBroker;

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

        /// <summary>
        /// The Path-level ban list source (issue #317). Empty by default — Endless, and populated only
        /// by the tests that exercise a banned kind — so every existing test's inventory contract is
        /// unaffected.
        /// </summary>
        private LevelCatalog _levelCatalog;

        private GameModeModel _gameModeModel;
        private PathRunModel _pathRunModel;

        /// <summary>PowerUpSystem loads the inventory in its constructor, so a count left behind by a
        /// previous test would silently decide whether the next one can spend anything.</summary>
        [SetUp]
        public void ClearPersistedInventory()
        {
            DeleteInventoryKeys();
            _levelProgressionModel = new LevelProgressionModel();
            _levelProgressionModel.CurrentLevelNumber.Value = ALL_KINDS_UNLOCKED_LEVEL;
            _levelCatalog = ScriptableObject.CreateInstance<LevelCatalog>();
            _gameModeModel = new GameModeModel();
            _pathRunModel = new PathRunModel();
            _appliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();
            _grantedBroker = new TestMessageBroker<PowerUpGrantedMessage>();
            _detonatedBroker = new TestMessageBroker<ExplosiveCoreDetonatedMessage>();
            _laserFiredBroker = new TestMessageBroker<LaserFiredMessage>();
            _vortexIslandFilledBroker = new TestMessageBroker<VortexIslandFilledMessage>();
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

            if (_levelCatalog != null)
            {
                Object.DestroyImmediate(_levelCatalog);
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

        /// <summary>
        /// Issue #404: one ad can be worth more than one unit. The source is asked once and the quantity
        /// is banked as that many single grants, so the count, the persisted value and the message
        /// stream all agree — two messages carrying the running count, exactly as
        /// <c>GrantPurchased</c> publishes a purchase of two.
        /// </summary>
        [Test]
        public void GrantRewardAsync_WithAQuantityGreaterThanOne_GrantsThatManyUnits()
        {
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), new StubRewardSource(granted: true));

            bool granted = system.GrantRewardAsync(PowerUpKind.CoinSower, CancellationToken.None, quantity: 2)
                .GetAwaiter().GetResult();

            Assert.IsTrue(granted);
            Assert.AreEqual(2, model.CoinSowerCount.Value);
            Assert.AreEqual(2, PlayerPrefs.GetInt(PowerUpInventoryKey.For(PowerUpKind.CoinSower), 0));
            Assert.AreEqual(2, _grantedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.CoinSower, _grantedBroker.Published[0].Kind);
            Assert.AreEqual(1, _grantedBroker.Published[0].NewInventoryCount);
            Assert.AreEqual(PowerUpKind.CoinSower, _grantedBroker.Published[1].Kind);
            Assert.AreEqual(2, _grantedBroker.Published[1].NewInventoryCount);
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

        // --- Path-level power-up ban list (issue #317) ---

        private const int BANNED_LEVEL_NUMBER = 5;

        /// <summary>Puts <paramref name="banned"/> on <see cref="BANNED_LEVEL_NUMBER"/>'s ban list in
        /// <see cref="_levelCatalog"/>. Built through <see cref="JsonUtility"/> rather than reflection,
        /// for the same reason <c>LevelProgressionSystemPathModeTests.ACatalogOf</c> is: the serialized
        /// field name is the asset's own contract.</summary>
        private void BanPowerUpOnLevel(PowerUpKind banned)
        {
            string levelJson = $"{{\"_levelNumber\":{BANNED_LEVEL_NUMBER},\"_bannedPowerUps\":[{(int)banned}]}}";
            JsonUtility.FromJsonOverwrite($"{{\"_levels\":[{levelJson}]}}", _levelCatalog);
        }

        /// <summary>Arms the active Path run at <see cref="BANNED_LEVEL_NUMBER"/>. Call after
        /// <see cref="BanPowerUpOnLevel"/> so the level the run is bound to is the one that bans it.</summary>
        private void EnterPathRunAtBannedLevel()
        {
            _gameModeModel.CurrentMode.Value = GameMode.Path;
            _pathRunModel.ActiveLevelNumber.Value = BANNED_LEVEL_NUMBER;
        }

        /// <summary>AC3: a kind on the active Path level's ban list refuses to arm, exactly as a still-
        /// locked kind does — nothing arms, nothing is spent.</summary>
        [Test]
        public void Arm_BannedInActivePathLevel_DoesNotArm()
        {
            PersistCount(PowerUpKind.Bomb, 3);
            BanPowerUpOnLevel(PowerUpKind.Bomb);
            EnterPathRunAtBannedLevel();
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.Bomb);

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(3, model.BombCount.Value);
        }

        // --- issue #355: Classic mode (GameMode.Timed) has no power-ups ---

        /// <summary>Classic mode's ruleset (<c>GameModeModel.ExtrasEnabled</c> false for
        /// <see cref="GameMode.Timed"/>) refuses to arm a targeted power-up even while holding one,
        /// exactly as a level-gated or Path-banned kind is refused.</summary>
        [Test]
        public void Arm_InClassicMode_DoesNotArmEvenWhenHeld()
        {
            PersistCount(PowerUpKind.Bomb, 3);
            _gameModeModel.CurrentMode.Value = GameMode.Timed;
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.Bomb);

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(3, model.BombCount.Value);
        }

        /// <summary>The three targetless kinds are applied directly rather than armed, so each needs its
        /// own refusal in Classic mode.</summary>
        [Test]
        public void TryApplyReroll_InClassicMode_DoesNotApplyEvenWhenHeld()
        {
            PersistCount(PowerUpKind.Reroll, 2);
            _gameModeModel.CurrentMode.Value = GameMode.Timed;
            var model = new PowerUpModel();
            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, new Piece("test_single", new[] { new GridPosition(0, 0) }), 1);
            trayModel.SetSlot(1, new Piece("test_single", new[] { new GridPosition(0, 0) }), 1);
            trayModel.SetSlot(2, new Piece("test_single", new[] { new GridPosition(0, 0) }), 1);
            PowerUpSystem system = CreateSystem(model, boardModel, trayModel);

            bool applied = system.TryApplyReroll();

            Assert.IsFalse(applied);
            Assert.AreEqual(2, model.RerollCount.Value);
        }

        [Test]
        public void TryApplyDoubleMultiplier_InClassicMode_DoesNotApplyEvenWhenHeld()
        {
            PersistCount(PowerUpKind.DoubleMultiplier, 2);
            _gameModeModel.CurrentMode.Value = GameMode.Timed;
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyDoubleMultiplier();

            Assert.IsFalse(applied);
            Assert.AreEqual(2, model.DoubleMultiplierCount.Value);
        }

        [Test]
        public void TryApplyGhostFit_InClassicMode_DoesNotApplyEvenWhenHeld()
        {
            PersistCount(PowerUpKind.GhostFit, 2);
            _gameModeModel.CurrentMode.Value = GameMode.Timed;
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyGhostFit();

            Assert.IsFalse(applied);
            Assert.AreEqual(2, model.GhostFitCount.Value);
        }

        /// <summary>AC4: the ban is enforced independently of Arm — calling the apply method directly
        /// with a banned kind still refuses and spends nothing.</summary>
        [Test]
        public void TryApplyBomb_BannedInActivePathLevel_ChangesNothing()
        {
            PersistCount(PowerUpKind.Bomb, 3);
            BanPowerUpOnLevel(PowerUpKind.Bomb);
            EnterPathRunAtBannedLevel();
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyBomb(new GridPosition(4, 4));

            Assert.IsFalse(applied);
            Assert.AreEqual(3, model.BombCount.Value);
        }

        /// <summary>AC4 for the bulk spend path: Coin Sower has no Arm at all, so its ban has to be
        /// enforced at the one spend method it has.</summary>
        [Test]
        public void TrySpendCoinSowerBulk_BannedInActivePathLevel_ChangesNothing()
        {
            PersistCount(PowerUpKind.CoinSower, 5);
            BanPowerUpOnLevel(PowerUpKind.CoinSower);
            EnterPathRunAtBannedLevel();
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool spent = system.TrySpendCoinSowerBulk(3);

            Assert.IsFalse(spent);
            Assert.AreEqual(5, model.CoinSowerCount.Value);
        }

        /// <summary>AC2's negative half: the same banned kind, on the same level, is fully spendable
        /// outside Path mode — Endless ignores the level's ban list entirely.</summary>
        [Test]
        public void TryApplyBomb_BannedOnLevelButInEndlessMode_StillApplies()
        {
            PersistCount(PowerUpKind.Bomb, 3);
            BanPowerUpOnLevel(PowerUpKind.Bomb);
            _pathRunModel.ActiveLevelNumber.Value = BANNED_LEVEL_NUMBER;

            // Deliberately left at GameModeModel's default (Endless) — the ban applies only in Path.
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyBomb(new GridPosition(4, 4));

            Assert.IsTrue(applied);
            Assert.AreEqual(2, model.BombCount.Value);
        }

        /// <summary>AC7: a level authoring no ban list (every level before this field existed) changes
        /// nothing in Path mode — every kind stays exactly as spendable as it is today.</summary>
        [Test]
        public void TryApplyBomb_InPathModeWithNoAuthoredBanList_StillApplies()
        {
            PersistCount(PowerUpKind.Bomb, 3);
            EnterPathRunAtBannedLevel();

            // _levelCatalog stays empty — no level, let alone a ban list, is authored for it.
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            bool applied = system.TryApplyBomb(new GridPosition(4, 4));

            Assert.IsTrue(applied);
            Assert.AreEqual(2, model.BombCount.Value);
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
        /// Issue #398 AC1: a core destroyed by a spent power-up was destroyed with no axis, so its bonus
        /// wipe takes both its row and its column — beyond the bomb's own 3x3 footprint — exactly as one
        /// destroyed by a completed line wipes the line at right angles to it.
        /// </summary>
        [Test]
        public void TryApplyBomb_OverAnExplosiveCore_WipesTheCoresRowAndColumn()
        {
            var boardModel = new BoardModel();
            var core = new GridPosition(4, 4);
            boardModel.Occupy(core, 1);
            boardModel.SetSpecialKind(core, SpecialCellKind.ExplosiveCore);

            // Well outside the bomb's 3x3: one on the core's row, one on its column, one on neither.
            boardModel.Occupy(new GridPosition(0, 4), 1);
            boardModel.Occupy(new GridPosition(4, 0), 1);
            var survivor = new GridPosition(0, 0);
            boardModel.Occupy(survivor, 1);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            bool applied = system.TryApplyBomb(core);

            Assert.IsTrue(applied);
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(0, 4)), "On the wiped row.");
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(4, 0)), "On the wiped column.");
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(survivor), "On neither line.");
            Assert.AreEqual(1, _detonatedBroker.Published.Count);
            Assert.AreEqual(2, _detonatedBroker.Published[0].WipedCellCount);
        }

        /// <summary>Issue #398 AC2: a core destroyed by a spent power-up with nothing else on its row
        /// or column does nothing — no wipe reported, and no hand-off of its kind.</summary>
        [Test]
        public void TryApplyBomb_OverAnExplosiveCoreWithEmptyLines_IsANoOpWithNoHandOff()
        {
            var boardModel = new BoardModel();
            var core = new GridPosition(4, 4);
            boardModel.Occupy(core, 1);
            boardModel.SetSpecialKind(core, SpecialCellKind.ExplosiveCore);

            var bystander = new GridPosition(7, 7);
            boardModel.Occupy(bystander, 1);

            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            bool applied = system.TryApplyBomb(core);

            Assert.IsTrue(applied);
            Assert.AreEqual(SpecialCellKind.None, boardModel.GetSpecialKind(bystander), "No hand-off.");
            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(bystander), "Off both lines.");
            Assert.AreEqual(0, _detonatedBroker.Published.Count);
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

        // --- Vortex tiles destroyed by a spent power-up (issue #156 AC1 / issue #349) ---

        /// <summary>
        /// A vortex destroyed by a Bomb fills every island the board has exactly as one destroyed by a
        /// completed line does — the same fill, reported through the same seam. The island sits far
        /// outside the bomb's own 3x3, so only the fill can explain it having been reclaimed.
        /// </summary>
        [Test]
        public void TryApplyBomb_OverAVortexWithAnIsland_FillsIt()
        {
            var boardModel = new BoardModel();
            var vortex = new GridPosition(4, 4);
            boardModel.Occupy(vortex, 1);
            boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            // A one-cell island, boxed in on every side, well outside the bomb's clamped 3x3 (3..5, 3..5).
            OccupyRing(boardModel, 0, 0, 2, 2);
            var island = new GridPosition(1, 1);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(vortex));

            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(island), "The island was reclaimed.");
            Assert.AreEqual(1, _vortexIslandFilledBroker.Published.Count);
            Assert.AreEqual(1, _vortexIslandFilledBroker.Published[0].FilledCells.Count);
            Assert.AreEqual(island, _vortexIslandFilledBroker.Published[0].FilledCells[0]);
            Assert.AreEqual(0, _vortexIslandFilledBroker.Published[0].HandOffTargets.Count);
        }

        /// <summary>Through a line-shaped kind: which power-up was spent decides nothing about the fill,
        /// exactly as it decides nothing about a laser's axis.</summary>
        [Test]
        public void TryApplyRowClear_OverAVortexWithAnIsland_FillsIt()
        {
            var boardModel = new BoardModel();
            var vortex = new GridPosition(2, 3);
            boardModel.Occupy(vortex, 1);
            boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            // A one-cell island well away from row 3, which this row clear empties outright.
            OccupyRing(boardModel, 5, 5, 7, 7);
            var island = new GridPosition(6, 6);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.RowClear);

            Assert.IsTrue(system.TryApplyRowClear(3));

            Assert.AreNotEqual(Board.EMPTY, boardModel.GetCell(island), "The island was reclaimed.");
            Assert.AreEqual(1, _vortexIslandFilledBroker.Published.Count);
            Assert.AreEqual(island, _vortexIslandFilledBroker.Published[0].FilledCells[0]);
        }

        /// <summary>With no island on the board, a destroyed vortex hands its tag off to an eligible cell
        /// instead of filling anything.</summary>
        [Test]
        public void TryApplyBomb_OverAVortexWithNoIsland_HandsOffToAnEligibleCell()
        {
            var boardModel = new BoardModel();
            var vortex = new GridPosition(4, 4);
            boardModel.Occupy(vortex, 1);
            boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            var eligible = new GridPosition(0, 0);
            boardModel.Occupy(eligible, 2);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(vortex));

            Assert.AreEqual(SpecialCellKind.Vortex, boardModel.GetSpecialKind(eligible));
            Assert.AreEqual(1, _vortexIslandFilledBroker.Published.Count);
            Assert.AreEqual(0, _vortexIslandFilledBroker.Published[0].FilledCells.Count);
            Assert.AreEqual(1, _vortexIslandFilledBroker.Published[0].HandOffTargets.Count);
            Assert.AreEqual(eligible, _vortexIslandFilledBroker.Published[0].HandOffTargets[0]);
        }

        /// <summary>No island and no eligible hand-off cell either publishes nothing at all — subscribers
        /// read the message itself as "the vortex did something".</summary>
        [Test]
        public void TryApplyBomb_OverAVortexWithNoIslandAndNoEligibleCell_PublishesNothing()
        {
            var boardModel = new BoardModel();
            var vortex = new GridPosition(4, 4);
            boardModel.Occupy(vortex, 1);
            boardModel.SetSpecialKind(vortex, SpecialCellKind.Vortex);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(vortex));

            Assert.AreEqual(0, _vortexIslandFilledBroker.Published.Count);
        }

        /// <summary>A power-up that destroyed no vortex must publish nothing at all.</summary>
        [Test]
        public void TryApplyBomb_WithNoVortexInRange_PublishesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            boardModel.Occupy(new GridPosition(0, 0), 2);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);

            system.TryApplyBomb(new GridPosition(4, 4));

            Assert.AreEqual(0, _vortexIslandFilledBroker.Published.Count);
        }

        /// <summary>
        /// The buffers that report a fill/hand-off belong to this System's own effect instance, so two
        /// applications in a row must report only their own work and never the sum of both — the reason
        /// the resolution is begun afresh each time. Both vortices here have no island of their own, so
        /// each hands off to its own eligible bystander; whichever of the two bystanders the first
        /// application picks becomes ineligible (it now carries the tag), so the second is left with
        /// exactly the other one — a deterministic outcome without needing to know which is which.
        /// </summary>
        [Test]
        public void TryApplyBomb_OverASecondVortex_ReportsOnlyThatApplicationsWork()
        {
            var boardModel = new BoardModel();
            var firstVortex = new GridPosition(1, 1);
            var secondVortex = new GridPosition(1, 6);
            boardModel.Occupy(firstVortex, 1);
            boardModel.Occupy(secondVortex, 1);
            boardModel.SetSpecialKind(firstVortex, SpecialCellKind.Vortex);
            boardModel.SetSpecialKind(secondVortex, SpecialCellKind.Vortex);

            var bystanderA = new GridPosition(6, 1);
            var bystanderB = new GridPosition(6, 6);
            boardModel.Occupy(bystanderA, 2);
            boardModel.Occupy(bystanderB, 2);

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.GrantDirect(PowerUpKind.Bomb);
            system.GrantDirect(PowerUpKind.Bomb);

            Assert.IsTrue(system.TryApplyBomb(firstVortex));
            Assert.IsTrue(system.TryApplyBomb(secondVortex));

            Assert.AreEqual(2, _vortexIslandFilledBroker.Published.Count);
            Assert.AreEqual(1, _vortexIslandFilledBroker.Published[0].HandOffTargets.Count);
            Assert.AreEqual(1, _vortexIslandFilledBroker.Published[1].HandOffTargets.Count);
            Assert.AreNotEqual(
                _vortexIslandFilledBroker.Published[0].HandOffTargets[0],
                _vortexIslandFilledBroker.Published[1].HandOffTargets[0],
                "The second application reports its own hand-off, not both.");
        }

        /// <summary>Occupies the border ring of the [minX..maxX] x [minY..maxY] rectangle, leaving its
        /// centre cell empty and boxed in on every side — a one-cell island. The rectangle must be at
        /// least 3x3 for a centre to exist.</summary>
        private static void OccupyRing(BoardModel boardModel, int minX, int minY, int maxX, int maxY)
        {
            int centerX = (minX + maxX) / 2;
            int centerY = (minY + maxY) / 2;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (x == centerX && y == centerY)
                    {
                        continue;
                    }

                    boardModel.Occupy(new GridPosition(x, y), 1);
                }
            }
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
        public void TryApplyColorCleanser_OnAnOccupiedCell_MessageCarriesTargetAndClearedPositions()
        {
            // Issue #332: BoardView's beam visual reads these two fields to know where to draw from
            // (TargetCell) and to (ClearedCellPositions, minus the trigger itself). Additive-only —
            // the resolver's own clearing decision is untouched, this only asserts what already flows
            // out of PowerUpClearResult is now also on the published message.
            PersistCount(PowerUpKind.ColorCleanser, 1);
            var boardModel = new BoardModel();
            var trigger = new GridPosition(0, 0);
            var otherMatch = new GridPosition(7, 7);
            boardModel.Occupy(trigger, 1);
            boardModel.Occupy(otherMatch, 1);
            boardModel.Occupy(new GridPosition(4, 4), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColorCleanser(trigger);

            Assert.IsTrue(applied);
            PowerUpAppliedMessage message = _appliedBroker.Published[0];
            Assert.AreEqual(trigger, message.TargetCell);
            Assert.IsNotNull(message.ClearedCellPositions);
            Assert.AreEqual(2, message.ClearedCellPositions.Count);
            CollectionAssert.Contains(message.ClearedCellPositions, trigger);
            CollectionAssert.Contains(message.ClearedCellPositions, otherMatch);
        }

        [Test]
        public void TryApplyColorCleanser_WithOnlyTheTriggerOfThatColour_ClearedPositionsHasNoOtherCell()
        {
            // AC3's data-driven negative case: the only cell of the target's colour is the trigger
            // itself, so ClearedCellPositions carries exactly one entry (the trigger) and no beam has
            // anything else to point at — BoardView's beam loop naturally draws zero beams from this,
            // with no special-casing needed on either side.
            PersistCount(PowerUpKind.ColorCleanser, 1);
            var boardModel = new BoardModel();
            var trigger = new GridPosition(3, 3);
            boardModel.Occupy(trigger, 1);
            boardModel.Occupy(new GridPosition(4, 4), 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyColorCleanser(trigger);

            Assert.IsTrue(applied);
            PowerUpAppliedMessage message = _appliedBroker.Published[0];
            Assert.AreEqual(trigger, message.TargetCell);
            Assert.IsNotNull(message.ClearedCellPositions);
            Assert.AreEqual(1, message.ClearedCellPositions.Count);
            Assert.AreEqual(trigger, message.ClearedCellPositions[0]);
        }

        [Test]
        public void TryApplyBomb_DoesNotPopulateColorCleanserOnlyFields()
        {
            // Every other kind must leave TargetCell/ClearedCellPositions null — they are additive
            // exposure for Color Cleanser's beam only, not a general-purpose field every kind fills in.
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            PowerUpModel model = new PowerUpModel();
            PersistCount(PowerUpKind.Bomb, 1);
            PowerUpSystem system = CreateSystem(model, boardModel);

            system.TryApplyBomb(new GridPosition(4, 4));

            PowerUpAppliedMessage message = _appliedBroker.Published[0];
            Assert.AreEqual(PowerUpKind.Bomb, message.Kind);
            Assert.IsNull(message.TargetCell);
            Assert.IsNull(message.ClearedCellPositions);
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

        // --- Paint Cross (issue #295) ---

        private const int PAINT_COLOUR = 3;

        [Test]
        public void TryApplyPaintCross_WithNoneHeld_ChangesNothing()
        {
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(3, 3), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyPaintCross(new GridPosition(3, 3), PAINT_COLOUR);

            Assert.IsFalse(applied);
            Assert.AreEqual(0, model.PaintCrossCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(3, 3)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyPaintCross_OnAnOccupiedCross_PaintsTheCrossSpendsOneAndDisarms()
        {
            PersistCount(PowerUpKind.PaintCross, 2);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 4), 1);
            boardModel.Occupy(new GridPosition(4, 7), 2);
            // Off the cross: must keep its colour.
            boardModel.Occupy(new GridPosition(6, 6), 5);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);
            system.Arm(PowerUpKind.PaintCross);

            bool applied = system.TryApplyPaintCross(new GridPosition(4, 4), PAINT_COLOUR);

            Assert.IsTrue(applied);
            Assert.AreEqual(1, model.PaintCrossCount.Value);
            Assert.AreEqual(1, PlayerPrefs.GetInt(PowerUpInventoryKey.For(PowerUpKind.PaintCross), -1));
            Assert.AreEqual(PAINT_COLOUR, boardModel.GetCell(new GridPosition(0, 4)));
            Assert.AreEqual(PAINT_COLOUR, boardModel.GetCell(new GridPosition(4, 7)));
            Assert.AreEqual(Board.EMPTY, boardModel.GetCell(new GridPosition(4, 4)), "Empty cells stay empty.");
            Assert.AreEqual(5, boardModel.GetCell(new GridPosition(6, 6)));
            Assert.IsNull(model.Armed.Value, "A successful application disarms.");
        }

        /// <summary>A paint is announced as an application with nothing cleared and no colour tally: the
        /// "power-ups used" counter sees it, while scoring and every colour objective — which read
        /// <c>ClearedCellCount</c> and <c>DestroyedCellCountByColour</c> — ignore it.</summary>
        [Test]
        public void TryApplyPaintCross_PublishesAnApplicationWithNothingClearedAndNoColourTally()
        {
            PersistCount(PowerUpKind.PaintCross, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);

            system.TryApplyPaintCross(new GridPosition(4, 4), PAINT_COLOUR);

            Assert.AreEqual(1, _appliedBroker.Published.Count);
            PowerUpAppliedMessage message = _appliedBroker.Published[0];
            Assert.AreEqual(PowerUpKind.PaintCross, message.Kind);
            Assert.AreEqual(0, message.ClearedCellCount);
            Assert.AreEqual(0, message.ClearedLineCount);
            Assert.AreEqual(0, message.EmptiedLineCount);
            Assert.IsNull(message.DestroyedCellCountByColour);
            Assert.IsNull(message.DestroyedDiamondCountByColour);
            Assert.AreEqual(0, message.DestroyedScoreGemCount);
        }

        [Test]
        public void TryApplyPaintCross_OnAnEmptyCross_IsRejected_KeepsInventoryAndArmedSelection()
        {
            // Mirrors ColorCleanser's "peek before spend" contract: a cross with nothing on it is the
            // illegal target, and it must leave the inventory and the armed selection untouched.
            PersistCount(PowerUpKind.PaintCross, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(7, 7), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);
            system.Arm(PowerUpKind.PaintCross);

            bool applied = system.TryApplyPaintCross(new GridPosition(2, 2), PAINT_COLOUR);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.PaintCrossCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(7, 7)));
            Assert.AreEqual(0, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.PaintCross, model.Armed.Value);
        }

        [TestCase(Board.EMPTY)]
        [TestCase(Board.COLOUR_COUNT + 1)]
        public void TryApplyPaintCross_WithAColourOutsideThePalette_IsRejectedAndSpendsNothing(int colourId)
        {
            PersistCount(PowerUpKind.PaintCross, 1);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyPaintCross(new GridPosition(4, 4), colourId);

            Assert.IsFalse(applied);
            Assert.AreEqual(1, model.PaintCrossCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(4, 4)));
        }

        /// <summary>Colour never affects clearing: painting a full row monochrome leaves it standing.</summary>
        [Test]
        public void TryApplyPaintCross_OnAFullRow_ClearsNothing()
        {
            PersistCount(PowerUpKind.PaintCross, 1);
            var boardModel = new BoardModel();
            for (int x = 0; x < Board.SIZE; x++)
            {
                boardModel.Occupy(new GridPosition(x, 2), (x % Board.COLOUR_COUNT) + 1);
            }

            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);

            Assert.IsTrue(system.TryApplyPaintCross(new GridPosition(3, 2), PAINT_COLOUR));

            for (int x = 0; x < Board.SIZE; x++)
            {
                Assert.AreEqual(PAINT_COLOUR, boardModel.GetCell(new GridPosition(x, 2)), $"({x},2) still standing, repainted.");
            }
        }

        /// <summary>A painted special cell keeps its kind and hit count — the System changes nothing
        /// the resolver does not, and the resolver changes only colour.</summary>
        [Test]
        public void TryApplyPaintCross_OverAReinforcedCoreCell_KeepsItsKindAndHits()
        {
            PersistCount(PowerUpKind.PaintCross, 1);
            var boardModel = new BoardModel();
            var core = new GridPosition(4, 4);
            boardModel.OccupyReinforced(core, 1, hitCount: 2);
            boardModel.SetSpecialKind(core, SpecialCellKind.ExplosiveCore);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);

            Assert.IsTrue(system.TryApplyPaintCross(core, PAINT_COLOUR));

            Assert.AreEqual(PAINT_COLOUR, boardModel.GetCell(core));
            Assert.AreEqual(SpecialCellKind.ExplosiveCore, boardModel.GetSpecialKind(core));
            Assert.AreEqual(2, boardModel.GetHitCount(core));
            Assert.AreEqual(0, _detonatedBroker.Published.Count, "Nothing was destroyed, so nothing detonates.");
        }

        [Test]
        public void TryApplyPaintCross_BannedInActivePathLevel_ChangesNothing()
        {
            PersistCount(PowerUpKind.PaintCross, 3);
            BanPowerUpOnLevel(PowerUpKind.PaintCross);
            EnterPathRunAtBannedLevel();
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyPaintCross(new GridPosition(4, 4), PAINT_COLOUR);

            Assert.IsFalse(applied);
            Assert.AreEqual(3, model.PaintCrossCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(4, 4)));
        }

        [Test]
        public void TryApplyPaintCross_BelowItsUnlockLevel_ChangesNothing()
        {
            PersistCount(PowerUpKind.PaintCross, 3);
            _levelProgressionModel.CurrentLevelNumber.Value = PowerUpUnlockLevels.LevelFor(PowerUpKind.PaintCross) - 1;
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(4, 4), 1);
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel);

            bool applied = system.TryApplyPaintCross(new GridPosition(4, 4), PAINT_COLOUR);

            Assert.IsFalse(applied);
            Assert.AreEqual(3, model.PaintCrossCount.Value);
            Assert.AreEqual(1, boardModel.GetCell(new GridPosition(4, 4)));
        }

        [Test]
        public void Arm_PaintCross_WhenHeld_Arms()
        {
            PersistCount(PowerUpKind.PaintCross, 1);
            var model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel());

            system.Arm(PowerUpKind.PaintCross);

            Assert.AreEqual(PowerUpKind.PaintCross, model.Armed.Value);
        }

        [Test]
        public void TryApplyPaintCross_AfterSpending_TheDecrementedCountIsLoadedByANewSystem()
        {
            PersistCount(PowerUpKind.PaintCross, 2);
            var boardModel = new BoardModel();
            boardModel.Occupy(new GridPosition(0, 0), 1);
            PowerUpSystem system = CreateSystem(new PowerUpModel(), boardModel);
            system.TryApplyPaintCross(new GridPosition(0, 0), PAINT_COLOUR);

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.PaintCrossCount.Value);
            Assert.AreEqual("PowerUp.Inventory.PaintCross", PowerUpInventoryKey.For(PowerUpKind.PaintCross));
        }

        // --- Rotate as a free preview, paid for on commit (issue #373) ---

        /// <summary>The tap itself is free: it swaps the piece and nothing else. The charge, the
        /// application message and the disarm all wait for the session to be settled.</summary>
        [Test]
        public void TryApplyRotate_OnANonSymmetricalPiece_SwapsInTheRotatedCatalogPieceWithoutSpendingYet()
        {
            PersistCount(PowerUpKind.Rotate, 2);
            var trayModel = new TrayModel();
            trayModel.SetSlot(1, FindPiece("t_up"), colourId: 3);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);

            bool applied = system.TryApplyRotate(1);

            Assert.IsTrue(applied);
            Assert.AreEqual(2, model.RotateCount.Value);
            Assert.AreEqual(PowerUpKind.Rotate, model.Armed.Value);

            // The slot holds a real catalog piece, so its id still describes its shape.
            Assert.AreEqual("t_right", trayModel.GetPiece(1).Id);
            Assert.AreSame(FindPiece("t_right"), trayModel.GetPiece(1));
            Assert.AreEqual(3, trayModel.GetColourId(1));

            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>Dropping the selection after a net change is what pays: exactly one charge and
        /// exactly one application message, however the piece got to its final shape.</summary>
        [Test]
        public void CancelArm_AfterOneRotateTap_SpendsOneAndPublishesOnce()
        {
            PersistCount(PowerUpKind.Rotate, 2);
            var trayModel = new TrayModel();
            trayModel.SetSlot(1, FindPiece("t_up"), colourId: 3);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);
            system.TryApplyRotate(1);

            system.CancelArm();

            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual("t_right", trayModel.GetPiece(1).Id);
            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Rotate, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(0, _appliedBroker.Published[0].ClearedCellCount);
        }

        [Test]
        public void TryApplyRotate_TwiceOnTheSameSlot_KeepsTurningForFreeAndStaysArmed()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);

            Assert.IsTrue(system.TryApplyRotate(0));
            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.AreEqual("t_down", trayModel.GetPiece(0).Id);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(PowerUpKind.Rotate, model.Armed.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>The issue's own case: a full turn back to where the piece started, then let go —
        /// the dock is as the player found it, so nothing is spent and nothing is announced.</summary>
        [Test]
        public void CancelArm_AfterAFullTurnBackToTheOriginalShape_SpendsNothing()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            Piece original = FindPiece("t_up");
            trayModel.SetSlot(0, original, colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);
            for (int tapIndex = 0; tapIndex < 4; tapIndex++)
            {
                Assert.IsTrue(system.TryApplyRotate(0));
            }

            system.CancelArm();

            Assert.AreSame(original, trayModel.GetPiece(0));
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        [Test]
        public void CancelArm_AfterThreeTaps_SpendsExactlyOneAndPublishesExactlyOnce()
        {
            PersistCount(PowerUpKind.Rotate, 3);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);
            for (int tapIndex = 0; tapIndex < 3; tapIndex++)
            {
                Assert.IsTrue(system.TryApplyRotate(0));
            }

            system.CancelArm();

            Assert.AreEqual("t_left", trayModel.GetPiece(0).Id);
            Assert.AreEqual(2, model.RotateCount.Value);
            Assert.AreEqual(1, _appliedBroker.Published.Count);
        }

        /// <summary>Moving on to another piece settles the first: its net change decides its charge
        /// before the second slot's session opens, so the two are paid for independently.</summary>
        [Test]
        public void TryApplyRotate_OnADifferentSlot_CommitsTheChangedFirstSlotBeforeStartingTheSecond()
        {
            PersistCount(PowerUpKind.Rotate, 3);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("line_h5"), colourId: 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.IsTrue(system.TryApplyRotate(1));

            // Slot 0 was paid for on the switch; slot 1's session is still open and unpaid.
            Assert.AreEqual(2, model.RotateCount.Value);
            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Rotate, model.Armed.Value);
            Assert.AreEqual("t_right", trayModel.GetPiece(0).Id);
            Assert.AreEqual("line_v5", trayModel.GetPiece(1).Id);

            system.CancelArm();

            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(2, _appliedBroker.Published.Count);
        }

        [Test]
        public void TryApplyRotate_OnADifferentSlot_CommitsAnUnchangedFirstSlotForFree()
        {
            PersistCount(PowerUpKind.Rotate, 2);
            var trayModel = new TrayModel();
            Piece original = FindPiece("line_h2");
            trayModel.SetSlot(0, original, colourId: 1);
            trayModel.SetSlot(1, FindPiece("t_up"), colourId: 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);

            // A two-cell line is back to itself after two turns.
            Assert.IsTrue(system.TryApplyRotate(0));
            Assert.IsTrue(system.TryApplyRotate(0));
            Assert.IsTrue(system.TryApplyRotate(1));

            Assert.AreSame(original, trayModel.GetPiece(0));
            Assert.AreEqual(2, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);

            system.CancelArm();

            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(1, _appliedBroker.Published.Count);
        }

        /// <summary>A refused tap on a symmetrical piece is a dead tap in every sense: it neither
        /// settles nor disturbs the session already open on another slot.</summary>
        [Test]
        public void TryApplyRotate_OnASymmetricalPiece_DoesNotDisturbTheSessionOpenOnAnotherSlot()
        {
            PersistCount(PowerUpKind.Rotate, 2);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            trayModel.SetSlot(1, FindPiece("square_2x2"), colourId: 2);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.IsFalse(system.TryApplyRotate(1));

            Assert.AreEqual(2, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Rotate, model.Armed.Value);

            // Still the same session: turning slot 0 back and letting go costs nothing.
            Assert.IsTrue(system.TryApplyRotate(0));
            Assert.IsTrue(system.TryApplyRotate(0));
            Assert.IsTrue(system.TryApplyRotate(0));
            system.CancelArm();

            Assert.AreEqual("t_up", trayModel.GetPiece(0).Id);
            Assert.AreEqual(2, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>Arm replaces the selection without disarming, so it is its own choke point: reaching
        /// for another kind mid-session settles the rotate as part of the switch.</summary>
        [Test]
        public void Arm_OfAnotherKind_WhileARotateSessionChangedThePiece_SpendsTheRotate()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            PersistCount(PowerUpKind.Bomb, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));

            system.Arm(PowerUpKind.Bomb);

            Assert.AreEqual(PowerUpKind.Bomb, model.Armed.Value);
            Assert.AreEqual(0, model.RotateCount.Value);
            Assert.AreEqual(1, model.BombCount.Value);
            Assert.AreEqual(1, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Rotate, _appliedBroker.Published[0].Kind);
        }

        /// <summary>The targetless kinds end in Disarm, so applying one mid-session settles the rotate
        /// on the way through.</summary>
        [Test]
        public void TryApplyReroll_WhileARotateSessionChangedThePiece_SpendsTheRotateToo()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            PersistCount(PowerUpKind.Reroll, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.IsTrue(system.TryApplyReroll());

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(0, model.RotateCount.Value);
            Assert.AreEqual(0, model.RerollCount.Value);
            Assert.AreEqual(2, _appliedBroker.Published.Count);
            Assert.AreEqual(PowerUpKind.Reroll, _appliedBroker.Published[0].Kind);
            Assert.AreEqual(PowerUpKind.Rotate, _appliedBroker.Published[1].Kind);
        }

        [Test]
        public void OnGameOver_WhileARotateSessionChangedThePiece_SettlesItBySpendingOne()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            var runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpSystem system = CreateSystem(
                model, new BoardModel(), trayModel, runStartedBroker, gameOverBroker);
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));

            gameOverBroker.Publish(new GameOverMessage(GameOverReason.TimeUp));

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(0, model.RotateCount.Value);
            Assert.AreEqual(1, _appliedBroker.Published.Count);
        }

        [Test]
        public void OnGameOver_WhileARotateSessionTurnedThePieceBack_SettlesItForFree()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h2"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            var runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpSystem system = CreateSystem(
                model, new BoardModel(), trayModel, runStartedBroker, gameOverBroker);
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));
            Assert.IsTrue(system.TryApplyRotate(0));

            gameOverBroker.Publish(new GameOverMessage(GameOverReason.TimeUp));

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>BoardSystem.StartNewRun redraws the whole tray before RunStartedMessage goes out, so
        /// an open session's remembered piece is already stale by the time this handler runs — settling
        /// it against the fresh tray would charge (or not) for a reason unrelated to anything the player
        /// did. The session is discarded instead: free, whichever way the piece happened to change.</summary>
        [Test]
        public void OnRunStarted_WhileARotateSessionChangedThePiece_DiscardsItForFree()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            var runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpSystem system = CreateSystem(
                model, new BoardModel(), trayModel, runStartedBroker, gameOverBroker);
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));

            runStartedBroker.Publish(new RunStartedMessage());

            Assert.IsNull(model.Armed.Value);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>A second session on the same slot, opened after the first was settled, is its own
        /// affair: the earlier charge is not refunded by turning the piece back afterwards.</summary>
        [Test]
        public void TryApplyRotate_AfterACommit_OpensAFreshSessionAgainstTheCommittedShape()
        {
            PersistCount(PowerUpKind.Rotate, 2);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));
            system.CancelArm();
            Assert.AreEqual(1, model.RotateCount.Value);

            // Three more turns bring it back to t_up — a net change from the committed t_right.
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));
            Assert.IsTrue(system.TryApplyRotate(0));
            Assert.IsTrue(system.TryApplyRotate(0));
            system.CancelArm();

            Assert.AreEqual("t_up", trayModel.GetPiece(0).Id);
            Assert.AreEqual(0, model.RotateCount.Value);
            Assert.AreEqual(2, _appliedBroker.Published.Count);
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
            system.Arm(PowerUpKind.Rotate);
            system.TryApplyRotate(0);
            system.CancelArm();

            PowerUpModel reloadedModel = new PowerUpModel();
            PowerUpSystem unused = CreateSystem(reloadedModel, new BoardModel());

            Assert.AreEqual(1, reloadedModel.RotateCount.Value);
        }

        /// <summary>The selection outlives the tap on purpose: it is what lets the player keep
        /// turning the same piece, and it is dropping it that settles the charge.</summary>
        [Test]
        public void TryApplyRotate_OnSuccess_KeepsTheSelectionArmed()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("t_up"), colourId: 1);
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, new BoardModel(), trayModel);
            system.Arm(PowerUpKind.Rotate);

            Assert.IsTrue(system.TryApplyRotate(0));

            Assert.AreEqual(PowerUpKind.Rotate, model.Armed.Value);
        }

        /// <summary>
        /// A rotate changes <em>which shapes</em> the player holds, so unlike a park it can take the
        /// last legal move away. <c>BoardSystem</c> is asked to re-check when the session is settled,
        /// and the run ends exactly as the placement that exhausted the board would have ended it.
        /// </summary>
        [Test]
        public void CancelArm_WhenTheTurnedPieceNoLongerFits_EndsTheRun()
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
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel, trayModel, gameOverBroker);
            system.Arm(PowerUpKind.Rotate);

            Assert.IsTrue(system.TryApplyRotate(0));

            // The preview alone ends nothing: the player may still turn it back.
            Assert.AreEqual("line_v2", trayModel.GetPiece(0).Id);
            Assert.AreEqual(0, gameOverBroker.Published.Count);

            system.CancelArm();

            Assert.AreEqual(1, gameOverBroker.Published.Count);
            Assert.AreEqual(GameOverReason.NoMovesLeft, gameOverBroker.Published[0].Reason);
            Assert.AreEqual(0, model.RotateCount.Value);
            Assert.IsNull(model.Armed.Value);
        }

        /// <summary>The re-check belongs to the settle, not the tap: a piece that passed through a
        /// dead orientation on its way back to a live one ends nothing and costs nothing.</summary>
        [Test]
        public void CancelArm_WhenThePieceWasTurnedThroughADeadOrientationAndBack_LeavesTheRunAliveForFree()
        {
            PersistCount(PowerUpKind.Rotate, 1);
            var boardModel = new BoardModel();

            // The same board as above: line_h2 fits, line_v2 does not.
            FillBoardExcept(
                boardModel,
                new GridPosition(0, 0), new GridPosition(1, 0),
                new GridPosition(3, 2), new GridPosition(5, 2), new GridPosition(7, 2),
                new GridPosition(0, 4), new GridPosition(2, 4));

            var trayModel = new TrayModel();
            trayModel.SetSlot(0, FindPiece("line_h2"), colourId: 1);
            var gameOverBroker = new TestMessageBroker<GameOverMessage>();
            PowerUpModel model = new PowerUpModel();
            PowerUpSystem system = CreateSystem(model, boardModel, trayModel, gameOverBroker);
            system.Arm(PowerUpKind.Rotate);
            Assert.IsTrue(system.TryApplyRotate(0));
            Assert.IsTrue(system.TryApplyRotate(0));

            system.CancelArm();

            Assert.AreEqual("line_h2", trayModel.GetPiece(0).Id);
            Assert.AreEqual(0, gameOverBroker.Published.Count);
            Assert.AreEqual(1, model.RotateCount.Value);
            Assert.AreEqual(0, _appliedBroker.Published.Count);
        }

        /// <summary>The other half of the same contract: the re-check is a question, not a verdict, so
        /// a rotate that leaves a move standing must not end anything.</summary>
        [Test]
        public void CancelArm_WhenTheTurnedPieceStillFits_LeavesTheRunAlive()
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
            system.Arm(PowerUpKind.Rotate);

            Assert.IsTrue(system.TryApplyRotate(0));
            system.CancelArm();

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

        /// <summary>For the rotate-session tests (issue #373): the run boundaries the system under test
        /// itself listens on, so a test can end or restart the run and watch an open session settle.</summary>
        private PowerUpSystem CreateSystem(
            PowerUpModel model,
            BoardModel boardModel,
            TrayModel trayModel,
            TestMessageBroker<RunStartedMessage> runStartedBroker,
            TestMessageBroker<GameOverMessage> gameOverBroker)
        {
            return CreateSystem(
                model,
                boardModel,
                trayModel,
                new StubRewardSource(granted: true),
                CreateBoardSystem(boardModel, trayModel),
                runStartedBroker,
                gameOverBroker);
        }

        private PowerUpSystem CreateSystem(
            PowerUpModel model,
            BoardModel boardModel,
            TrayModel trayModel,
            IRewardSource rewardSource,
            BoardSystem boardSystem)
        {
            return CreateSystem(
                model,
                boardModel,
                trayModel,
                rewardSource,
                boardSystem,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        private PowerUpSystem CreateSystem(
            PowerUpModel model,
            BoardModel boardModel,
            TrayModel trayModel,
            IRewardSource rewardSource,
            BoardSystem boardSystem,
            TestMessageBroker<RunStartedMessage> runStartedBroker,
            TestMessageBroker<GameOverMessage> gameOverBroker)
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
                _levelCatalog,
                _gameModeModel,
                _pathRunModel,
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
                _vortexIslandFilledBroker,
                _coinCellsBroker,
                _currencyConfig,
                runStartedBroker,
                gameOverBroker);
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
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                pieceDraw,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                gameOverBroker,
                trayRefilledBroker,
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
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
            PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(PowerUpKind.PaintCross));
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
