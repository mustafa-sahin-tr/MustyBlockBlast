using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="PowerUpModel"/>: earning, spending and persisting the power-up inventory, plus
    /// applying a spent power-up to <see cref="BoardModel"/> — or, for <see cref="PowerUpKind.Rotate"/>
    /// and <see cref="PowerUpKind.Reroll"/>, to the tray. Which model an application mutates is
    /// secondary: this is the one and only spender of the inventory, so every kind's armed-and-spend
    /// lifecycle belongs here.
    /// <para>
    /// It deliberately does not score. Applying publishes <see cref="PowerUpAppliedMessage"/> and
    /// <see cref="PowerUpScoreSystem"/> turns that into points, mirroring how placements reach
    /// <see cref="ScoreSystem"/> — which is also what keeps power-up scoring out of the streak logic.
    /// </para>
    /// <para>
    /// Every arm and every application is also gated on the player's level (see
    /// <see cref="PowerUpUnlockLevels"/>), checked exactly where "holds none of it" is and refused just
    /// as completely. The gate is read, never written: it can refuse to spend an inventory but never
    /// takes one away.
    /// </para>
    /// <para>
    /// Spending is charged for a valid target even when the target turns out to be empty: the player
    /// made a deliberate, legal application. Only holding none of the power-up is a true no-op — except
    /// for the three kinds that have illegal targets at all and charge nothing for one:
    /// <see cref="PowerUpKind.Joker"/> (an already-occupied cell), <see cref="PowerUpKind.ColorCleanser"/>
    /// (an empty one) and <see cref="PowerUpKind.Rotate"/> (an empty slot, or a piece too symmetrical
    /// to have a distinct rotation).
    /// </para>
    /// <para>
    /// <see cref="PowerUpKind.Reroll"/>, <see cref="PowerUpKind.DoubleMultiplier"/> and
    /// <see cref="PowerUpKind.GhostFit"/> sit outside that lifecycle entirely: none has a target to aim
    /// at, so none is ever armed and all three are applied on the tap that selects them.
    /// </para>
    /// <para>
    /// It also owns the armed selection: selecting a power-up arms it immediately (there is no queue),
    /// and the run's clock is held for as long as it stays armed. Arming, cancelling and applying all
    /// leave through <see cref="Disarm"/>, so "armed" and "the clock is held for it" can never drift
    /// apart.
    /// </para>
    /// </summary>
    public sealed class PowerUpSystem : IDisposable
    {
        /// <summary>
        /// Colour a joker's fill takes. Fixed rather than drawn: colour is cosmetic and never affects
        /// placement or clearing, and a constant makes a joker cell recognisably its own thing.
        /// </summary>
        private const int JOKER_FILL_COLOUR_ID = 1;

        private readonly PowerUpModel _powerUpModel;
        private readonly LevelProgressionModel _levelProgressionModel;
        private readonly BoardModel _boardModel;
        private readonly TrayModel _trayModel;
        private readonly BoardSystem _boardSystem;
        private readonly TimerRunSystem _timerRunSystem;
        private readonly DoubleMultiplierSystem _doubleMultiplierSystem;
        private readonly GhostFitSystem _ghostFitSystem;
        private readonly GhostFitModel _ghostFitModel;
        private readonly IRewardSource _rewardSource;
        private readonly IPublisher<PowerUpAppliedMessage> _appliedPublisher;
        private readonly IPublisher<PowerUpGrantedMessage> _grantedPublisher;
        private readonly IPublisher<ExplosiveCoreDetonatedMessage> _explosiveCoreDetonatedPublisher;
        private readonly IDisposable _runStartedSubscription;
        private readonly IDisposable _gameOverSubscription;

        /// <summary>
        /// Its own instance rather than the one <see cref="BoardSystem"/> owns. The two can never run
        /// at once — a power-up is applied from an input callback, a placement's cascade from another,
        /// and neither re-enters the other — so sharing would buy nothing, while a second instance
        /// keeps each System's blast buffer meaning "the blast I just caused".
        /// </summary>
        private readonly ExplosiveCoreEffect _explosiveCoreEffect = new ExplosiveCoreEffect();

        public PowerUpSystem(
            PowerUpModel powerUpModel,
            LevelProgressionModel levelProgressionModel,
            BoardModel boardModel,
            TrayModel trayModel,
            BoardSystem boardSystem,
            TimerRunSystem timerRunSystem,
            DoubleMultiplierSystem doubleMultiplierSystem,
            GhostFitSystem ghostFitSystem,
            GhostFitModel ghostFitModel,
            IRewardSource rewardSource,
            IPublisher<PowerUpAppliedMessage> appliedPublisher,
            IPublisher<PowerUpGrantedMessage> grantedPublisher,
            IPublisher<ExplosiveCoreDetonatedMessage> explosiveCoreDetonatedPublisher,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _explosiveCoreDetonatedPublisher = explosiveCoreDetonatedPublisher;
            _powerUpModel = powerUpModel;
            _levelProgressionModel = levelProgressionModel;
            _boardModel = boardModel;
            _trayModel = trayModel;
            _boardSystem = boardSystem;
            _timerRunSystem = timerRunSystem;
            _doubleMultiplierSystem = doubleMultiplierSystem;
            _ghostFitSystem = ghostFitSystem;
            _ghostFitModel = ghostFitModel;
            _rewardSource = rewardSource;
            _appliedPublisher = appliedPublisher;
            _grantedPublisher = grantedPublisher;

            LoadPersistedCount(PowerUpKind.Bomb);
            LoadPersistedCount(PowerUpKind.RowClear);
            LoadPersistedCount(PowerUpKind.ColumnClear);
            LoadPersistedCount(PowerUpKind.Joker);
            LoadPersistedCount(PowerUpKind.ColorCleanser);
            LoadPersistedCount(PowerUpKind.Rotate);
            LoadPersistedCount(PowerUpKind.Reroll);
            LoadPersistedCount(PowerUpKind.DoubleMultiplier);
            LoadPersistedCount(PowerUpKind.GhostFit);

            // An armed selection belongs to the run it was made in: it must not survive either end of
            // a run boundary, or the next run would open with a power-up already aimed and its clock
            // held.
            _runStartedSubscription = runStartedSubscriber.Subscribe(OnRunStarted);
            _gameOverSubscription = gameOverSubscriber.Subscribe(OnGameOver);
        }

        /// <summary>
        /// Selects <paramref name="kind"/> and aims it at the board immediately, holding the run's
        /// clock until it is applied or cancelled. Holding none of that kind, a kind still behind its
        /// level gate (see <see cref="PowerUpUnlockLevels"/>), or a run that is already over, is a
        /// no-op: none arms, so none can be spent by a follow-up tap.
        /// <para>
        /// <see cref="PowerUpKind.Reroll"/>, <see cref="PowerUpKind.DoubleMultiplier"/> and
        /// <see cref="PowerUpKind.GhostFit"/> are refused outright, however many the player holds: none
        /// has a target, so an armed one could only ever be released onto a board cell that means
        /// nothing to it. <see cref="TryApplyReroll"/>, <see cref="TryApplyDoubleMultiplier"/> and
        /// <see cref="TryApplyGhostFit"/> are their whole interface.
        /// </para>
        /// </summary>
        public void Arm(PowerUpKind kind)
        {
            if (kind == PowerUpKind.Reroll || kind == PowerUpKind.DoubleMultiplier
                || kind == PowerUpKind.GhostFit
                || _boardSystem.IsGameOver || IsLocked(kind) || CountOf(kind).Value <= 0)
            {
                return;
            }

            _powerUpModel.Armed.Value = kind;
            _timerRunSystem.SetPowerUpArmedPaused(true);
        }

        /// <summary>Drops the armed selection without spending anything. A no-op when nothing is armed.</summary>
        public void CancelArm()
        {
            if (_powerUpModel.Armed.Value == null)
            {
                return;
            }

            Disarm();
        }

        /// <summary>Spends one bomb on the 3x3 area around <paramref name="center"/>. False when the
        /// player holds none or the target is off the board — nothing is changed in either case.</summary>
        public bool TryApplyBomb(GridPosition center)
        {
            if (!Board.IsInside(center) || IsLocked(PowerUpKind.Bomb) || !TrySpend(PowerUpKind.Bomb))
            {
                return false;
            }

            Apply(PowerUpKind.Bomb, PowerUpClearResolver.ResolveBombClear(_boardModel.Board, center));
            Disarm();
            return true;
        }

        /// <summary>Spends one row clear on <paramref name="row"/>, full or not.</summary>
        public bool TryApplyRowClear(int row)
        {
            if (!IsValidLineIndex(row) || IsLocked(PowerUpKind.RowClear) || !TrySpend(PowerUpKind.RowClear))
            {
                return false;
            }

            Apply(PowerUpKind.RowClear, PowerUpClearResolver.ResolveRowClear(_boardModel.Board, row));
            Disarm();
            return true;
        }

        /// <summary>Spends one column clear on <paramref name="column"/>, full or not.</summary>
        public bool TryApplyColumnClear(int column)
        {
            if (!IsValidLineIndex(column) || IsLocked(PowerUpKind.ColumnClear)
                || !TrySpend(PowerUpKind.ColumnClear))
            {
                return false;
            }

            Apply(PowerUpKind.ColumnClear, PowerUpClearResolver.ResolveColumnClear(_boardModel.Board, column));
            Disarm();
            return true;
        }

        /// <summary>
        /// Spends one joker on <paramref name="target"/>: fills that cell, then clears its row and/or
        /// column if the fill completed them.
        /// <para>
        /// Unlike the other three kinds a joker has illegal targets — an off-board cell or an already
        /// occupied one. Those are refused outright: nothing is spent, nothing is disarmed and nothing
        /// is published, so the player simply aims again rather than losing the power-up to a misplaced
        /// tap. Only a real fill is charged for, whether or not it went on to clear anything.
        /// </para>
        /// </summary>
        public bool TryApplyJoker(GridPosition target)
        {
            // Peeked rather than spent: the fill below decides whether this tap is legal at all, and
            // an illegal one must leave the inventory exactly as it found it.
            if (IsLocked(PowerUpKind.Joker) || CountOf(PowerUpKind.Joker).Value <= 0)
            {
                return false;
            }

            // Legality lives in the resolver, not here: a rejected result is the single, authoritative
            // statement that the board was not touched.
            JokerFillResult result = JokerFillResolver.ResolveFill(
                _boardModel.Board, target, JOKER_FILL_COLOUR_ID);
            if (!result.Filled)
            {
                return false;
            }

            TrySpend(PowerUpKind.Joker);

            _boardModel.NotifyFilled(result.Position, JOKER_FILL_COLOUR_ID);
            if (result.AnyCleared)
            {
                _boardModel.NotifyPowerUpCleared(result.ClearedCells);
            }

            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Joker, result.ClearedCellCount, result.LineCount));

            // A joker completes lines rather than clearing a region, but a core standing in one of
            // those lines is destroyed just the same — and a destroyed core blasts whatever destroyed
            // it, so this path applies its triggers exactly as the region-clearing kinds do.
            ApplyTriggeredSpecials(result.TriggeredSpecials);

            Disarm();
            return true;
        }

        /// <summary>
        /// Spends one colour cleanser on <paramref name="target"/>: clears every cell on the board
        /// sharing that cell's colour. An empty target has no colour to extract and is refused outright
        /// — the same "peek before spending" contract as <see cref="TryApplyJoker"/>: nothing is spent,
        /// nothing is disarmed, the player simply aims again.
        /// </summary>
        public bool TryApplyColorCleanser(GridPosition target)
        {
            // Peeked rather than spent, mirroring TryApplyJoker: legality here is "does the resolver
            // find a colour to clear", and that must be checked before a single count is touched.
            if (!Board.IsInside(target) || IsLocked(PowerUpKind.ColorCleanser)
                || CountOf(PowerUpKind.ColorCleanser).Value <= 0)
            {
                return false;
            }

            PowerUpClearResult result = PowerUpClearResolver.ResolveColorCleanser(_boardModel.Board, target);
            if (!result.AnyCleared)
            {
                return false;
            }

            TrySpend(PowerUpKind.ColorCleanser);
            Apply(PowerUpKind.ColorCleanser, result);
            Disarm();
            return true;
        }

        /// <summary>
        /// Spends one rotate on the dock piece in <paramref name="slotIndex"/>, turning it 90 degrees
        /// clockwise. The only application aimed at the tray rather than the board: no cell changes, so
        /// nothing clears and nothing scores.
        /// <para>
        /// Rotating swaps the slot to the catalog piece that already describes that orientation rather
        /// than rewriting the piece's offsets, so the slot always holds a real catalog piece whose id
        /// still matches its shape (see <see cref="PieceRotator"/>).
        /// </para>
        /// <para>
        /// Follows the "peek before spend" contract of <see cref="TryApplyJoker"/>: an out-of-range or
        /// empty slot, and a fully symmetrical piece whose rotation would be a no-op, are all refused
        /// outright — nothing is spent, nothing is disarmed, the player simply aims again.
        /// </para>
        /// </summary>
        public bool TryApplyRotate(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= TrayModel.SLOT_COUNT
                || IsLocked(PowerUpKind.Rotate) || CountOf(PowerUpKind.Rotate).Value <= 0)
            {
                return false;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null || !PieceRotator.TryRotateClockwise(piece, out Piece rotated))
            {
                return false;
            }

            TrySpend(PowerUpKind.Rotate);

            // Colour is carried over untouched: rotating changes the shape on offer, never which piece
            // it is to the player.
            _trayModel.SetSlot(slotIndex, rotated, _trayModel.GetColourId(slotIndex));

            // Published so an application is an application whatever it targeted — the badge counter
            // that tracks "power-ups used" must see this one too. Zero cleared cells means scoring and
            // the board's clear animation both ignore it, which is exactly right: nothing was cleared.
            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Rotate, clearedCellCount: 0, clearedLineCount: 0));

            Disarm();

            // Disarmed first, then re-checked: unlike a park (which only permutes pieces between dock
            // and pocket), this changes *which shapes* the player holds, so the move that kept the run
            // alive may no longer exist — and a game over raised from here must not find a selection
            // still armed behind it.
            _boardSystem.RecheckGameOver();
            return true;
        }

        /// <summary>
        /// Spends one reroll: discards all three dock pieces and draws three new ones, at least one of
        /// which fits the current board. The second kind aimed at the tray rather than the board, so
        /// like Rotate it clears nothing and scores nothing.
        /// <para>
        /// The odd one out in this class: it has no target parameter at all. There is nothing to aim at
        /// — the whole dock is the subject — so it is applied on the tap that selects it rather than
        /// armed first. It still ends in <see cref="Disarm"/>, which is what lets a player who had some
        /// other kind armed reach for this one without leaving that selection (and the clock hold it
        /// carries) stranded behind the reroll.
        /// </para>
        /// <para>
        /// Follows the "peek before spend" contract of <see cref="TryApplyJoker"/>: holding none, or a
        /// run already over, is refused outright before a single count is touched.
        /// </para>
        /// </summary>
        public bool TryApplyReroll()
        {
            if (_boardSystem.IsGameOver || IsLocked(PowerUpKind.Reroll)
                || CountOf(PowerUpKind.Reroll).Value <= 0)
            {
                return false;
            }

            // BoardSystem owns the tray and the draw, and is the only thing allowed to decide the run is
            // over — so it performs the reroll and this only pays for it.
            if (!_boardSystem.TryRerollTray(out bool wasClutchSave))
            {
                return false;
            }

            TrySpend(PowerUpKind.Reroll);

            // Published for the same reason Rotate is: an application is an application whatever it
            // targeted, and the "power-ups used" badge counter must see this one. Zero cleared cells
            // means scoring and the clear animation both correctly ignore it. wasClutchSave feeds the
            // RerollSave objective the same way Bomb's EmptiedLineCount feeds BombInducedLineClear.
            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Reroll, clearedCellCount: 0, clearedLineCount: 0, emptiedLineCount: 0,
                wasClutchSave));

            Disarm();
            return true;
        }

        /// <summary>
        /// Spends one double multiplier: opens a fixed-length window during which every score gain —
        /// placements and power-up clears alike — is worth double.
        /// <para>
        /// Targetless like <see cref="TryApplyReroll"/>, so it is applied on the tap that selects it
        /// rather than armed first, and it likewise ends in <see cref="Disarm"/> so reaching for this
        /// one never strands another kind's selection (and the clock hold it carries) behind it.
        /// </para>
        /// <para>
        /// The only application that changes nothing at the moment it happens: no cell, no tray slot.
        /// It publishes <see cref="PowerUpAppliedMessage"/> anyway, for the same reason Rotate and
        /// Reroll do — the "power-ups used" badge counter must see it — with zero cleared cells, which
        /// is what keeps <see cref="PowerUpScoreSystem"/> from paying for the activation itself.
        /// </para>
        /// <para>
        /// Follows the "peek before spend" contract of <see cref="TryApplyJoker"/>: holding none, or a
        /// run already over, is refused outright before a single count is touched.
        /// </para>
        /// </summary>
        public bool TryApplyDoubleMultiplier()
        {
            if (_boardSystem.IsGameOver || IsLocked(PowerUpKind.DoubleMultiplier)
                || CountOf(PowerUpKind.DoubleMultiplier).Value <= 0)
            {
                return false;
            }

            TrySpend(PowerUpKind.DoubleMultiplier);

            // Opened before the publish, so any scoring this message goes on to trigger is already
            // inside the window rather than depending on subscriber order.
            _doubleMultiplierSystem.Activate();

            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                PowerUpKind.DoubleMultiplier, clearedCellCount: 0, clearedLineCount: 0));

            Disarm();
            return true;
        }

        /// <summary>
        /// Spends one Ghost Fit: searches every dock piece against every board anchor and puts the best
        /// move it finds on screen as a suggestion (see <see cref="GhostFitSystem"/>).
        /// <para>
        /// Targetless like <see cref="TryApplyReroll"/> and <see cref="TryApplyDoubleMultiplier"/>, so
        /// it is applied on the tap that selects it, and it likewise ends in <see cref="Disarm"/> so
        /// reaching for it never strands another kind's selection (and the clock hold it carries).
        /// </para>
        /// <para>
        /// Unlike every other kind it changes no game state at all — not a cell, not a tray slot, not a
        /// score window. What it buys is a suggestion, which the player may act on or ignore. It still
        /// publishes <see cref="PowerUpAppliedMessage"/>, for the reason Rotate, Reroll and Double
        /// Multiplier do: the "power-ups used" badge counter must see it. Zero cleared cells keeps
        /// <see cref="PowerUpScoreSystem"/> from paying for it.
        /// </para>
        /// <para>
        /// Two refusals, both free, both following the "peek before spend" contract of
        /// <see cref="TryApplyJoker"/>. A second tap while a suggestion is already on screen is the
        /// dismiss gesture — the same "tap it again to take it back" idiom as cancelling an armed kind —
        /// so it takes the hint down and spends nothing. And a board where no dock piece fits anywhere
        /// has no move to point at: the search reports that as the "no placements possible" state, which
        /// is shown to the player but never charged for.
        /// </para>
        /// </summary>
        public bool TryApplyGhostFit()
        {
            if (_boardSystem.IsGameOver || IsLocked(PowerUpKind.GhostFit)
                || CountOf(PowerUpKind.GhostFit).Value <= 0)
            {
                return false;
            }

            if (_ghostFitModel.IsSuggesting)
            {
                _ghostFitSystem.Dismiss();
                return false;
            }

            // Searched before a single count is touched: a dock with no legal placement anywhere is a
            // refusal, and the player must not pay for being told so.
            if (!_ghostFitSystem.TryShowSuggestion())
            {
                return false;
            }

            TrySpend(PowerUpKind.GhostFit);

            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                PowerUpKind.GhostFit, clearedCellCount: 0, clearedLineCount: 0));

            Disarm();
            return true;
        }

        /// <summary>Asks <see cref="IRewardSource"/> for one <paramref name="kind"/> and banks it if
        /// granted. Returns whether it was granted; a refusal leaves the inventory untouched.</summary>
        public async UniTask<bool> GrantRewardAsync(PowerUpKind kind, CancellationToken cancellationToken)
        {
            RewardResult result = await _rewardSource.RequestRewardAsync(kind, cancellationToken);
            if (!result.Granted)
            {
                return false;
            }

            Grant(result.Kind);
            return true;
        }

        /// <summary>
        /// Banks one <paramref name="kind"/> unconditionally and immediately, without asking
        /// <see cref="IRewardSource"/>. For rewards the player has already earned by playing — a badge
        /// unlock, say — where there is nothing left to gate on: the achievement *is* the grant, so
        /// routing it through the ad-watch seam would let a declined ad swallow a reward the player
        /// already won.
        /// <para>
        /// Shares <see cref="Grant"/> with <see cref="GrantRewardAsync"/>, so both paths mutate,
        /// persist and publish identically — there is exactly one place a grant happens.
        /// </para>
        /// </summary>
        public void GrantDirect(PowerUpKind kind) => Grant(kind);

        public void Dispose()
        {
            _runStartedSubscription.Dispose();
            _gameOverSubscription.Dispose();
        }

        /// <summary>Shared exit from armed mode: a successful application, an explicit cancel and both
        /// run boundaries all land here, so the clock is never left held by a selection that is gone.</summary>
        private void Disarm()
        {
            _powerUpModel.Armed.Value = null;
            _timerRunSystem.SetPowerUpArmedPaused(false);
        }

        private void OnRunStarted(RunStartedMessage message) => Disarm();

        private void OnGameOver(GameOverMessage message) => Disarm();

        /// <summary>Delegates to Core so the index a power-up will accept and the geometry the preview
        /// draws for it can never disagree about which rows/columns exist.</summary>
        private static bool IsValidLineIndex(int index) => PowerUpTargetCells.IsValidLineIndex(index);

        /// <summary>
        /// Whether <paramref name="kind"/> is still behind its level gate (see
        /// <see cref="PowerUpUnlockLevels"/>). Guarded alongside "holds none" in every arm and apply
        /// path, so a kind the player has not reached yet is refused even when they somehow hold one —
        /// a grant is not a licence, and the two conditions must be equally hard to get past.
        /// <para>
        /// Read only. Nothing here writes <see cref="PowerUpModel"/>, so a gate raised above the
        /// player's level can never discard a count they have already earned: it only refuses to spend
        /// it until they get there.
        /// </para>
        /// </summary>
        private bool IsLocked(PowerUpKind kind)
            => !PowerUpUnlockLevels.IsUnlockedAt(kind, _levelProgressionModel.CurrentLevelNumber.Value);

        /// <summary>
        /// The one and only way a power-up enters the inventory: increment, persist, announce. Every
        /// earning path funnels through here so the three steps can never drift out of step with each
        /// other, whatever gate (or lack of one) got the player this far.
        /// </summary>
        private void Grant(PowerUpKind kind)
        {
            ReactiveProperty<int> count = CountOf(kind);
            count.Value += 1;
            Persist(kind, count.Value);
            _grantedPublisher.Publish(new PowerUpGrantedMessage(kind, count.Value));
        }

        /// <summary>Decrements and persists the inventory, or reports that there was none to spend.</summary>
        private bool TrySpend(PowerUpKind kind)
        {
            ReactiveProperty<int> count = CountOf(kind);
            if (count.Value <= 0)
            {
                return false;
            }

            count.Value -= 1;
            Persist(kind, count.Value);
            return true;
        }

        private void Apply(PowerUpKind kind, PowerUpClearResult result)
        {
            if (result.AnyCleared)
            {
                _boardModel.NotifyPowerUpCleared(result.ClearedCells);
            }

            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                kind, result.ClearedCellCount, clearedLineCount: 0, emptiedLineCount: result.EmptiedLineCount));

            ApplyTriggeredSpecials(result.TriggeredSpecials);
        }

        /// <summary>
        /// Detonates the special cells this power-up's clear destroyed. A special block behaves the
        /// same whatever destroyed it, so a core taken out by a Bomb blasts exactly as one taken out by
        /// a completed line does — <see cref="PowerUpClearResolver"/> detects them through the same
        /// <see cref="SpecialCellDetection"/> pass a placement's clear uses, and this applies them
        /// through the same <see cref="ExplosiveCoreEffect"/>.
        /// <para>
        /// Deliberately does <em>not</em> spawn a new core, however many lines the power-up emptied:
        /// the reward is for a placement that closed a row and a column, and spending a power-up is not
        /// a placement.
        /// </para>
        /// </summary>
        private void ApplyTriggeredSpecials(IReadOnlyList<SpecialCellTrigger> triggers)
        {
            if (triggers == null || triggers.Count == 0)
            {
                return;
            }

            _explosiveCoreEffect.BeginResolution();

            for (int i = 0; i < triggers.Count; i++)
            {
                if (triggers[i].Kind != SpecialCellKind.ExplosiveCore)
                {
                    continue;
                }

                _explosiveCoreEffect.Apply(_boardModel.Board, triggers[i]);
            }

            IReadOnlyList<GridPosition> blastedCells = _explosiveCoreEffect.BlastedCells;
            if (blastedCells.Count == 0)
            {
                return;
            }

            _boardModel.NotifyPowerUpCleared(blastedCells);
            _explosiveCoreDetonatedPublisher.Publish(new ExplosiveCoreDetonatedMessage(blastedCells.Count));
        }

        private ReactiveProperty<int> CountOf(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.RowClear:
                    return _powerUpModel.RowClearCount;
                case PowerUpKind.ColumnClear:
                    return _powerUpModel.ColumnClearCount;
                case PowerUpKind.Joker:
                    return _powerUpModel.JokerCount;
                case PowerUpKind.ColorCleanser:
                    return _powerUpModel.ColorCleanserCount;
                case PowerUpKind.Rotate:
                    return _powerUpModel.RotateCount;
                case PowerUpKind.Reroll:
                    return _powerUpModel.RerollCount;
                case PowerUpKind.DoubleMultiplier:
                    return _powerUpModel.DoubleMultiplierCount;
                case PowerUpKind.GhostFit:
                    return _powerUpModel.GhostFitCount;
                default:
                    return _powerUpModel.BombCount;
            }
        }

        private void LoadPersistedCount(PowerUpKind kind)
        {
            CountOf(kind).Value = PlayerPrefs.GetInt(PowerUpInventoryKey.For(kind), 0);
        }

        private static void Persist(PowerUpKind kind, int count)
        {
            PlayerPrefs.SetInt(PowerUpInventoryKey.For(kind), count);
        }
    }
}
