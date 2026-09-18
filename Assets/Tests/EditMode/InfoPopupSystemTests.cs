using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="InfoPopupSystem"/>'s core contract: an unseen trigger auto-opens its popup
    /// exactly once, a second trigger for the same id never reopens it, a trigger arriving while
    /// another popup is already open is marked seen without interrupting what is on screen (so it is
    /// never shown automatically later, only through a manual <see cref="InfoPopupSystem.Open"/>), the
    /// "seen" flag survives across a fresh System instance (the PlayerPrefs contract, not just the
    /// in-memory one), <see cref="InfoPopupSystem.Open"/> always opens regardless of seen state, and the
    /// one-shot already-granted-power-ups migration marks without ever auto-opening a popup.
    /// </summary>
    public sealed class InfoPopupSystemTests
    {
        private const string MIGRATION_KEY = "InfoPopup.Migration.AlreadyGrantedPowerUps.Done";

        private InfoPopupModel _model;
        private PowerUpModel _powerUpModel;
        private TestMessageBroker<SpecialCellSpawnedMessage> _specialCellSpawnedBroker;
        private TestMessageBroker<PowerUpGrantedMessage> _powerUpGrantedBroker;
        private TestMessageBroker<HoldFirstUseMessage> _holdFirstUseBroker;
        private TestMessageBroker<SpecialPieceSpawnedMessage> _specialPieceSpawnedBroker;
        private InfoPopupSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            DeleteAllInfoPopupKeys();

            _model = new InfoPopupModel();
            _powerUpModel = new PowerUpModel();
            _specialCellSpawnedBroker = new TestMessageBroker<SpecialCellSpawnedMessage>();
            _powerUpGrantedBroker = new TestMessageBroker<PowerUpGrantedMessage>();
            _holdFirstUseBroker = new TestMessageBroker<HoldFirstUseMessage>();
            _specialPieceSpawnedBroker = new TestMessageBroker<SpecialPieceSpawnedMessage>();

            _system = CreateInfoPopupSystem();
        }

        [TearDown]
        public void DisposeSystem()
        {
            _system.Dispose();
            DeleteAllInfoPopupKeys();
        }

        private InfoPopupSystem CreateInfoPopupSystem()
        {
            return new InfoPopupSystem(
                _model,
                _powerUpModel,
                _specialCellSpawnedBroker,
                _powerUpGrantedBroker,
                _holdFirstUseBroker,
                _specialPieceSpawnedBroker);
        }

        // --- an unseen trigger auto-opens its popup ---

        [Test]
        public void HoldFirstUseMessage_Unseen_AutoOpensTheHoldPopup()
        {
            _holdFirstUseBroker.Publish(new HoldFirstUseMessage());

            InfoPopupContent? content = _model.OpenContent.Value;
            Assert.IsNotNull(content);
            Assert.AreEqual(InfoPopupSubjectKind.Hold, content.Value.SubjectKind);
        }

        [Test]
        public void SpecialCellSpawnedMessage_None_IsIgnored()
        {
            _specialCellSpawnedBroker.Publish(
                new SpecialCellSpawnedMessage(SpecialCellKind.None, new GridPosition(0, 0)));

            Assert.IsNull(_model.OpenContent.Value);
        }

        [Test]
        public void SpecialPieceSpawnedMessage_None_IsIgnored()
        {
            _specialPieceSpawnedBroker.Publish(new SpecialPieceSpawnedMessage(SpecialPieceKind.None, 0));

            Assert.IsNull(_model.OpenContent.Value);
        }

        [Test]
        public void PowerUpGrantedMessage_Unseen_AutoOpensThePowerUpPopup()
        {
            _powerUpGrantedBroker.Publish(new PowerUpGrantedMessage(PowerUpKind.Bomb, 1));

            InfoPopupContent? content = _model.OpenContent.Value;
            Assert.IsNotNull(content);
            Assert.AreEqual(InfoPopupSubjectKind.PowerUp, content.Value.SubjectKind);
            Assert.AreEqual((int)PowerUpKind.Bomb, content.Value.KindValue);
        }

        // --- a second trigger for the same id never reopens it ---

        [Test]
        public void HoldFirstUseMessage_PublishedTwice_OnlyAutoOpensOnce()
        {
            _holdFirstUseBroker.Publish(new HoldFirstUseMessage());
            _system.Close();

            _holdFirstUseBroker.Publish(new HoldFirstUseMessage());

            Assert.IsNull(_model.OpenContent.Value);
        }

        [Test]
        public void SpecialCellSpawnedMessage_SameKindTwice_OnlyAutoOpensOnce()
        {
            _specialCellSpawnedBroker.Publish(
                new SpecialCellSpawnedMessage(SpecialCellKind.ExplosiveCore, new GridPosition(0, 0)));
            _system.Close();

            _specialCellSpawnedBroker.Publish(
                new SpecialCellSpawnedMessage(SpecialCellKind.ExplosiveCore, new GridPosition(1, 1)));

            Assert.IsNull(_model.OpenContent.Value);
        }

        [Test]
        public void SeenFlag_SurvivesAFreshSystemInstance()
        {
            _specialCellSpawnedBroker.Publish(
                new SpecialCellSpawnedMessage(SpecialCellKind.ExplosiveCore, new GridPosition(0, 0)));
            _system.Dispose();

            // A fresh System (and Model — the same "new run of the app" the seen-flag has to survive)
            // over a fresh broker: the flag it checks must come from PlayerPrefs, not from any
            // in-memory state the old instance held.
            _model = new InfoPopupModel();
            var freshBroker = new TestMessageBroker<SpecialCellSpawnedMessage>();
            _system = new InfoPopupSystem(
                _model, _powerUpModel, freshBroker, _powerUpGrantedBroker, _holdFirstUseBroker,
                _specialPieceSpawnedBroker);

            freshBroker.Publish(new SpecialCellSpawnedMessage(SpecialCellKind.ExplosiveCore, new GridPosition(2, 2)));

            Assert.IsNull(_model.OpenContent.Value);
        }

        // --- a trigger while something else is open is marked seen without interrupting it ---

        [Test]
        public void Trigger_WhileAnotherPopupIsOpen_DoesNotReplaceIt()
        {
            _holdFirstUseBroker.Publish(new HoldFirstUseMessage());
            InfoPopupContent? firstContent = _model.OpenContent.Value;

            _powerUpGrantedBroker.Publish(new PowerUpGrantedMessage(PowerUpKind.Bomb, 1));

            Assert.AreEqual(firstContent.Value.Id, _model.OpenContent.Value.Value.Id);
        }

        [Test]
        public void Trigger_WhileAnotherPopupIsOpen_IsStillMarkedSeenForLater()
        {
            _holdFirstUseBroker.Publish(new HoldFirstUseMessage());
            _powerUpGrantedBroker.Publish(new PowerUpGrantedMessage(PowerUpKind.Bomb, 1));
            _system.Close();

            // The pre-empted trigger must not still be "unseen" — otherwise the next Bomb grant would
            // wrongly auto-open it, stealing a popup slot from whatever else is on screen by then.
            _powerUpGrantedBroker.Publish(new PowerUpGrantedMessage(PowerUpKind.Bomb, 2));

            Assert.IsNull(_model.OpenContent.Value);
        }

        // --- Open() always opens, regardless of seen state ---

        [Test]
        public void Open_AlreadySeenSubject_StillOpensIt()
        {
            _holdFirstUseBroker.Publish(new HoldFirstUseMessage());
            _system.Close();

            _system.Open(InfoPopupSubjectKind.Hold, -1);

            Assert.IsNotNull(_model.OpenContent.Value);
            Assert.AreEqual(InfoPopupSubjectKind.Hold, _model.OpenContent.Value.Value.SubjectKind);
        }

        [Test]
        public void Open_NeverAutoShownBefore_MarksItSeen()
        {
            _system.Open(InfoPopupSubjectKind.SpecialCell, (int)SpecialCellKind.ExplosiveCore);
            _system.Close();

            // Now that it has been shown manually, the auto-trigger for the same subject must not
            // reopen it.
            _specialCellSpawnedBroker.Publish(
                new SpecialCellSpawnedMessage(SpecialCellKind.ExplosiveCore, new GridPosition(0, 0)));

            Assert.IsNull(_model.OpenContent.Value);
        }

        // --- migration ---

        [Test]
        public void Migration_AlreadyGrantedPowerUp_MarksSeenWithoutOpeningAPopup()
        {
            // [SetUp] already ran the migration once, with BombCount still 0 — clear its one-shot flag
            // so the migration genuinely runs again against the state this test sets up, rather than
            // silently no-op'ing on the already-consumed flag.
            PlayerPrefs.DeleteKey(MIGRATION_KEY);
            _powerUpModel.BombCount.Value = 1;
            _system.Dispose();

            _system = CreateInfoPopupSystem();

            Assert.IsNull(_model.OpenContent.Value);

            _powerUpGrantedBroker.Publish(new PowerUpGrantedMessage(PowerUpKind.Bomb, 2));
            Assert.IsNull(_model.OpenContent.Value);
        }

        [Test]
        public void Migration_RunsExactlyOnce()
        {
            PlayerPrefs.DeleteKey(MIGRATION_KEY);
            _powerUpModel.BombCount.Value = 1;
            _system.Dispose();
            _system = CreateInfoPopupSystem();

            Assert.AreEqual(1, PlayerPrefs.GetInt(MIGRATION_KEY, 0));

            // A kind granted for the first time genuinely, after the migration has already run once,
            // must still auto-open — the migration marking Bomb seen must not fire again and swallow
            // an unrelated kind's first grant.
            _powerUpGrantedBroker.Publish(new PowerUpGrantedMessage(PowerUpKind.RowClear, 1));
            Assert.IsNotNull(_model.OpenContent.Value);

            // And a second migration attempt (e.g. a later boot) must not re-mark RowClear seen out
            // from under the player just because Bomb happens to already be marked.
            _system.Close();
            _system.Dispose();
            _system = CreateInfoPopupSystem();
            _powerUpGrantedBroker.Publish(new PowerUpGrantedMessage(PowerUpKind.ColumnClear, 1));
            Assert.IsNotNull(_model.OpenContent.Value);
        }

        private static void DeleteAllInfoPopupKeys()
        {
            PlayerPrefs.DeleteKey(MIGRATION_KEY);
            PlayerPrefs.DeleteKey("InfoPopup.Seen.Hold");
            PlayerPrefs.DeleteKey("InfoPopup.Seen.SpecialCell_ExplosiveCore");
            PlayerPrefs.DeleteKey("InfoPopup.Seen.PowerUp_Bomb");
            PlayerPrefs.DeleteKey("InfoPopup.Seen.PowerUp_RowClear");
            PlayerPrefs.DeleteKey("InfoPopup.Seen.PowerUp_ColumnClear");
            PlayerPrefs.DeleteKey("InfoPopup.Seen.SpecialPiece_Golden");
        }
    }
}
