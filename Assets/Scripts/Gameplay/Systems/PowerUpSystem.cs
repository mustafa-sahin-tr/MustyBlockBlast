using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
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
    /// for the four kinds that have illegal targets at all and charge nothing for one:
    /// <see cref="PowerUpKind.Joker"/> (an already-occupied cell), <see cref="PowerUpKind.ColorCleanser"/>
    /// (an empty one), <see cref="PowerUpKind.PaintCross"/> (a cross with nothing standing on it) and
    /// <see cref="PowerUpKind.Rotate"/> (an empty slot, or a piece too symmetrical to have a distinct
    /// rotation).
    /// </para>
    /// <para>
    /// <see cref="PowerUpKind.Reroll"/>, <see cref="PowerUpKind.DoubleMultiplier"/> and
    /// <see cref="PowerUpKind.GhostFit"/> sit outside that lifecycle entirely: none has a target to aim
    /// at, so none is ever armed and all three are applied on the tap that selects them.
    /// </para>
    /// <para>
    /// <see cref="PowerUpKind.CoinSower"/> sits further out still: it is not spent during a run at all.
    /// It is bought and sown at a level-start screen, before the run it dresses exists, so its only
    /// interface here is <see cref="TrySpendCoinSowerBulk"/> — no arm, no target, no application
    /// message. It is nonetheless earned and spent through this class's own <c>Grant</c>/<c>TrySpend</c>
    /// pair, because "the one and only spender of the inventory" has no exceptions.
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
        private readonly LevelCatalog _levelCatalog;
        private readonly GameModeModel _gameModeModel;
        private readonly PathRunModel _pathRunModel;
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
        private readonly IPublisher<LaserFiredMessage> _laserFiredPublisher;
        private readonly IPublisher<VortexIslandFilledMessage> _vortexIslandFilledPublisher;

        /// <summary>Optional: null in every existing test construction, which predates issue #278.
        /// Guarded on every publish so an un-injected instance behaves exactly as it did before.</summary>
        private readonly IPublisher<PowerUpUnlockedMessage> _powerUpUnlockedPublisher;

        /// <summary>Optional, for the same reason <see cref="_powerUpUnlockedPublisher"/> is.</summary>
        private readonly IPublisher<HoldFirstUseMessage> _holdFirstUsePublisher;

        private readonly IDisposable _runStartedSubscription;
        private readonly IDisposable _gameOverSubscription;

        /// <summary>
        /// Watches the player's progression frontier for a power-up's level gate (see
        /// <see cref="PowerUpUnlockLevels"/>) being newly crossed, and publishes
        /// <see cref="PowerUpUnlockedMessage"/> for exactly that kind — never retroactively for a kind
        /// already unlocked when this System was constructed (see <see cref="OnLevelNumberChanged"/>).
        /// Owned here rather than by a dedicated System: this class already loads and owns every power-up's
        /// unlock state, so the crossing that first makes one spendable is this class's event to notice.
        /// </summary>
        private readonly IDisposable _levelNumberSubscription;

        /// <summary>Baseline for <see cref="OnLevelNumberChanged"/>: the frontier last observed, so the
        /// immediate replay every <c>ReactiveProperty{T}.Subscribe</c> fires on subscription is treated
        /// as "this is where the player already is", never as a crossing to announce.</summary>
        private int _lastObservedLevelNumber;

        /// <summary>No rotate session open — see <see cref="_rotateSessionSlotIndex"/>.</summary>
        private const int NO_ROTATE_SESSION = -1;

        /// <summary>
        /// The dock slot a rotate session is open on, or <see cref="NO_ROTATE_SESSION"/> (issue #373).
        /// A session opens on the first <see cref="TryApplyRotate"/> tap on a slot and stays open across
        /// every further tap on that same slot, each of which turns the piece for free. It is settled by
        /// <see cref="CommitRotateSession"/> — from every path that drops the armed selection, and from
        /// a tap that moves on to a different slot — which is the one moment a charge can be spent.
        /// </summary>
        private int _rotateSessionSlotIndex = NO_ROTATE_SESSION;

        /// <summary>
        /// The piece <see cref="_rotateSessionSlotIndex"/> held before the session's first turn. Compared
        /// by <see cref="Piece.Id"/> at commit time: <see cref="PieceRotator"/> only ever swaps in catalog
        /// pieces, so an id match means the player turned the piece all the way back to where it started.
        /// </summary>
        private Piece _rotateSessionOriginalPiece;

        /// <summary>Receives a rotated piece's re-indexed diamond decoration (issue #394) before it is
        /// copied onto the tray. Grown to the largest piece rotated and reused, so a rotate allocates
        /// nothing after the first of its size.</summary>
        private int[] _rotatedDiamondBuffer = Array.Empty<int>();

        /// <summary>
        /// Its own instance rather than the one <see cref="BoardSystem"/> owns. The two can never run
        /// at once — a power-up is applied from an input callback, a placement's cascade from another,
        /// and neither re-enters the other — so sharing would buy nothing, while a second instance
        /// keeps each System's own totals meaning "what the power-up I just spent caused".
        /// </summary>
        private readonly ExplosiveCoreEffect _explosiveCoreEffect = new ExplosiveCoreEffect();

        /// <summary>Its own instance for the same reason <see cref="_explosiveCoreEffect"/> is.</summary>
        private readonly LaserEffect _laserEffect = new LaserEffect();

        /// <summary>
        /// Its own instance for the same reason the two above are, and with the same reading of what its
        /// buffers mean: <see cref="VortexEffect.FilledCells"/>/<see cref="VortexEffect.HandOffTargets"/>
        /// have to list "what the power-up I just spent did", never a running total shared with whatever
        /// the last placement resolved.
        /// <para>
        /// The odd one out among the three, as it is in <see cref="BoardSystem"/>: it creates blocks (or
        /// hands its tag off) rather than destroying them, so what it reports is a list of filled/handed-off
        /// cells and it is announced through the vortex seam instead of the cleared-cells one. Needs
        /// <see cref="_random"/>, so it is built in the constructor rather than here — see that field.
        /// </para>
        /// </summary>
        private readonly VortexEffect _vortexEffect;

        /// <summary>
        /// The random stream <see cref="_vortexEffect"/> draws a fill's colour and a hand-off's target
        /// from. This System has no seeded, replayable run of its own — unlike <see cref="BoardSystem"/>,
        /// which shares one stream across every random decision a run makes — so an ordinary unseeded
        /// stream is enough: nothing here needs to reproduce a run.
        /// </summary>
        private readonly System.Random _random = new System.Random();

        /// <summary>
        /// Its own instance for the same reason the two above are: its total has to mean "the coins the
        /// power-up I just spent earned", never a running sum shared with whatever the last placement
        /// resolved. Built in the constructor rather than here because the per-cell payout is an economy
        /// number read from <see cref="CurrencyConfig"/>, which Core must not know about.
        /// </summary>
        private readonly CoinEffect _coinEffect;

        /// <summary>Every effect a power-up's clear can trigger, presented to
        /// <see cref="CascadeClearResolver"/> as one — see <see cref="ApplyTriggeredSpecials"/>, the one
        /// place this is used, for why a power-up needs the resolver at all.</summary>
        private readonly ISpecialCellEffect _specialCellEffects;

        private readonly IPublisher<CoinCellsClearedMessage> _coinCellsClearedPublisher;

        public PowerUpSystem(
            PowerUpModel powerUpModel,
            LevelProgressionModel levelProgressionModel,
            LevelCatalog levelCatalog,
            GameModeModel gameModeModel,
            PathRunModel pathRunModel,
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
            IPublisher<LaserFiredMessage> laserFiredPublisher,
            IPublisher<VortexIslandFilledMessage> vortexIslandFilledPublisher,
            IPublisher<CoinCellsClearedMessage> coinCellsClearedPublisher,
            CurrencyConfig currencyConfig,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            IPublisher<PowerUpUnlockedMessage> powerUpUnlockedPublisher = null,
            IPublisher<HoldFirstUseMessage> holdFirstUsePublisher = null)
        {
            _explosiveCoreDetonatedPublisher = explosiveCoreDetonatedPublisher;
            _laserFiredPublisher = laserFiredPublisher;
            _vortexIslandFilledPublisher = vortexIslandFilledPublisher;
            _coinCellsClearedPublisher = coinCellsClearedPublisher;
            _powerUpUnlockedPublisher = powerUpUnlockedPublisher;
            _holdFirstUsePublisher = holdFirstUsePublisher;
            _coinEffect = new CoinEffect(currencyConfig.CoinCellPayout);
            _vortexEffect = new VortexEffect(_random);
            _specialCellEffects = new CompositeSpecialCellEffect(
                _explosiveCoreEffect, _laserEffect, _vortexEffect, _coinEffect);
            _powerUpModel = powerUpModel;
            _levelProgressionModel = levelProgressionModel;
            _levelCatalog = levelCatalog;
            _gameModeModel = gameModeModel;
            _pathRunModel = pathRunModel;
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

            // Baseline before subscribing, so the immediate replay every ReactiveProperty<T>.Subscribe
            // fires with the current value is recognised as "already there" rather than a crossing —
            // see _lastObservedLevelNumber.
            _lastObservedLevelNumber = levelProgressionModel.CurrentLevelNumber.Value;
            _levelNumberSubscription = levelProgressionModel.CurrentLevelNumber.Subscribe(OnLevelNumberChanged);

            LoadPersistedCount(PowerUpKind.Bomb);
            LoadPersistedCount(PowerUpKind.RowClear);
            LoadPersistedCount(PowerUpKind.ColumnClear);
            LoadPersistedCount(PowerUpKind.Joker);
            LoadPersistedCount(PowerUpKind.ColorCleanser);
            LoadPersistedCount(PowerUpKind.Rotate);
            LoadPersistedCount(PowerUpKind.Reroll);
            LoadPersistedCount(PowerUpKind.DoubleMultiplier);
            LoadPersistedCount(PowerUpKind.GhostFit);
            LoadPersistedCount(PowerUpKind.CoinSower);
            LoadPersistedCount(PowerUpKind.Hold);
            LoadPersistedCount(PowerUpKind.PaintCross);

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
        /// <para>
        /// <see cref="PowerUpKind.CoinSower"/> is refused for a stronger reason still: it is never spent
        /// during a run at all. It is bought and sown at a level-start screen through
        /// <see cref="TrySpendCoinSowerBulk"/>, so there is no moment in a run at which arming it could
        /// mean anything — and an armed one would be released onto a board cell by an aim path that falls
        /// through to Bomb for every kind it does not name, spending a bomb for it.
        /// </para>
        /// <para>
        /// <see cref="PowerUpKind.Hold"/> is refused for the same shape of reason: it is invoked by
        /// dragging a tray piece onto the pocket, never by arming and tapping a target, so
        /// <see cref="TryApplyHold"/> is its whole interface and an armed Hold could only ever be
        /// released onto a board cell that means nothing to it.
        /// </para>
        /// </summary>
        public void Arm(PowerUpKind kind)
        {
            // Arming overwrites the selection outright rather than going through Disarm, so a rotate
            // session left open by the previous selection has to be settled here or it would be
            // stranded. Before the guard, so a run this commit happens to end is refused below.
            CommitRotateSession();

            if (kind == PowerUpKind.Reroll || kind == PowerUpKind.DoubleMultiplier
                || kind == PowerUpKind.GhostFit || kind == PowerUpKind.CoinSower || kind == PowerUpKind.Hold
                || _boardSystem.IsGameOver || IsLocked(kind) || IsBannedInActivePathLevel(kind)
                || CountOf(kind).Value <= 0 || !_gameModeModel.ExtrasEnabled)
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
            if (!_boardModel.Board.IsInside(center) || IsLocked(PowerUpKind.Bomb)
                || IsBannedInActivePathLevel(PowerUpKind.Bomb) || !TrySpend(PowerUpKind.Bomb))
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
            if (!IsValidRowIndex(row) || IsLocked(PowerUpKind.RowClear)
                || IsBannedInActivePathLevel(PowerUpKind.RowClear) || !TrySpend(PowerUpKind.RowClear))
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
            if (!IsValidColumnIndex(column) || IsLocked(PowerUpKind.ColumnClear)
                || IsBannedInActivePathLevel(PowerUpKind.ColumnClear) || !TrySpend(PowerUpKind.ColumnClear))
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
            if (IsLocked(PowerUpKind.Joker) || IsBannedInActivePathLevel(PowerUpKind.Joker)
                || CountOf(PowerUpKind.Joker).Value <= 0)
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

            // The gem count is read from the triggers rather than from the effects below, so it is known
            // before the message that pays for this application is published — a gem multiplies the
            // event that destroyed it, so the count has to be in hand at the moment that event scores.
            // Same reason as the region-clearing path: a reinforced cell the completed line only
            // damaged is still standing and needs repainting.
            _boardModel.NotifyHitCountsRefreshed();

            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Joker, result.ClearedCellCount, result.LineCount, emptiedLineCount: 0,
                wasClutchSave: false,
                destroyedScoreGemCount: ScoreGemEffect.CountDestroyed(result.TriggeredSpecials),
                reinforcedCellsFullyClearedCount: result.ReinforcedCellsFullyClearedCount,
                destroyedCellCountByColour: result.DestroyedCellCountByColour,
                timerCellsClearedInTimeCount: TimerCellClearEffect.CountDestroyed(result.TriggeredSpecials),
                destroyedDiamondCountByColour: DiamondClearEffect.CountDestroyedByColour(result.TriggeredSpecials)));

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
            if (!_boardModel.Board.IsPlayable(target) || IsLocked(PowerUpKind.ColorCleanser)
                || IsBannedInActivePathLevel(PowerUpKind.ColorCleanser)
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
            Apply(PowerUpKind.ColorCleanser, result, target);
            Disarm();
            return true;
        }

        /// <summary>
        /// Spends one Paint Cross on <paramref name="target"/> (issue #295): recolours every occupied
        /// cell of that cell's row and column to <paramref name="colourId"/>, leaving empty cells empty
        /// and clearing nothing. A cross with no occupied cell has nothing to paint and is refused
        /// outright — the same "peek before spending" contract as <see cref="TryApplyJoker"/> and
        /// <see cref="TryApplyColorCleanser"/>: nothing is spent, nothing is disarmed, the player simply
        /// aims again. An out-of-range colour is refused the same way; the palette the picker offers is
        /// <c>1..</c><see cref="Board.COLOUR_COUNT"/>, and anything else is a caller bug rather than a
        /// board to paint.
        /// <para>
        /// The one board-mutating kind that does not go through <see cref="Apply"/>: a paint is not a
        /// clear, so there are no cleared cells to announce, no triggers to detonate and no colour tally
        /// to hand to the objectives — <see cref="PowerUpAppliedMessage"/> is published with zero
        /// cleared cells and no tally, exactly as the tray-aimed kinds publish theirs, so the "power-ups
        /// used" counter sees it while scoring, the clear animation and every colour objective ignore
        /// it. A painted cell reaches a colour objective only when something later destroys it.
        /// </para>
        /// <para>
        /// The colour is chosen by the player <em>after</em> the target tap, in a picker the Presentation
        /// layer owns; this method is that picker's confirm. Cancelling the picker never reaches here,
        /// which is what leaves the kind armed and unspent in that case.
        /// </para>
        /// </summary>
        public bool TryApplyPaintCross(GridPosition target, int colourId)
        {
            // Peeked rather than spent, mirroring TryApplyColorCleanser: legality here is "does the
            // resolver find anything standing on the cross", and that must be checked before a single
            // count is touched.
            if (!_boardModel.Board.IsPlayable(target) || colourId < 1 || colourId > Board.COLOUR_COUNT
                || IsLocked(PowerUpKind.PaintCross) || IsBannedInActivePathLevel(PowerUpKind.PaintCross)
                || CountOf(PowerUpKind.PaintCross).Value <= 0)
            {
                return false;
            }

            PowerUpPaintResult result = PowerUpPaintResolver.ResolvePaintCross(_boardModel.Board, target, colourId);
            if (!result.AnyPainted)
            {
                return false;
            }

            TrySpend(PowerUpKind.PaintCross);

            _boardModel.NotifyPainted(result.PaintedCells);

            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                PowerUpKind.PaintCross, clearedCellCount: 0, clearedLineCount: 0));

            Disarm();
            return true;
        }

        /// <summary>
        /// Turns the dock piece in <paramref name="slotIndex"/> 90 degrees clockwise, for free, as a
        /// preview (issue #373). The only application aimed at the tray rather than the board: no cell
        /// changes, so nothing clears and nothing scores.
        /// <para>
        /// A rotate is not paid for tap by tap. The first tap on a slot opens a <em>session</em> on it,
        /// remembering the piece as it was; every further tap on the same slot keeps turning it, still
        /// free, with the power-up still armed. The session is settled by
        /// <see cref="CommitRotateSession"/> when the armed selection is dropped — cancelled, replaced by
        /// arming or applying another kind, or ended with the run — and only then is exactly one charge
        /// spent, and only if the piece's final shape differs from the one the session opened on. Turning
        /// it all the way back round and leaving it costs nothing. A tap that moves on to a different
        /// slot settles the first slot's session before opening one on the second.
        /// </para>
        /// <para>
        /// Rotating swaps the slot to the catalog piece that already describes that orientation rather
        /// than rewriting the piece's offsets, so the slot always holds a real catalog piece whose id
        /// still matches its shape (see <see cref="PieceRotator"/>) — which is also what lets the commit
        /// recognise "back where it started" by id.
        /// </para>
        /// <para>
        /// Follows the "peek before spend" contract of <see cref="TryApplyJoker"/>: an out-of-range or
        /// empty slot, and a fully symmetrical piece whose rotation would be a no-op, are all refused
        /// outright — nothing is spent, nothing is disarmed, no session is opened or settled, the
        /// player simply aims again.
        /// </para>
        /// </summary>
        public bool TryApplyRotate(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= TrayModel.SLOT_COUNT
                || IsLocked(PowerUpKind.Rotate) || IsBannedInActivePathLevel(PowerUpKind.Rotate)
                || CountOf(PowerUpKind.Rotate).Value <= 0)
            {
                return false;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null || !PieceRotator.TryRotateClockwise(piece, out Piece rotated))
            {
                return false;
            }

            // Moving on to another piece settles the one being left behind first: whether that piece
            // ended up changed decides its charge, and it must not be carried into the new session.
            if (_rotateSessionSlotIndex != NO_ROTATE_SESSION && _rotateSessionSlotIndex != slotIndex)
            {
                CommitRotateSession();

                // That settle can end the run (see CommitRotateSession), and a run that is over has no
                // dock left to keep turning.
                if (_boardSystem.IsGameOver)
                {
                    return false;
                }
            }

            if (_rotateSessionSlotIndex == NO_ROTATE_SESSION)
            {
                _rotateSessionSlotIndex = slotIndex;
                _rotateSessionOriginalPiece = piece;
            }

            // Colour is carried over untouched: rotating changes the shape on offer, never which piece
            // it is to the player. So is the diamond decoration (issue #394) — re-indexed onto the
            // turned piece's own offset order, so each gem stays on the cell it was on.
            IReadOnlyList<int> rotatedDiamonds = null;
            if (_trayModel.HasDiamonds(slotIndex))
            {
                if (_rotatedDiamondBuffer.Length < rotated.CellCount)
                {
                    _rotatedDiamondBuffer = new int[rotated.CellCount];
                }

                PieceRotator.MapCellValuesClockwise(
                    piece, rotated, _trayModel.GetDiamondColourIds(slotIndex), _rotatedDiamondBuffer);
                rotatedDiamonds = _rotatedDiamondBuffer;
            }

            _trayModel.SetSlot(
                slotIndex, rotated, _trayModel.GetColourId(slotIndex), SpecialPieceKind.None, rotatedDiamonds);
            return true;
        }

        /// <summary>
        /// Settles the open rotate session, if any (issue #373): spends the one charge a net change
        /// costs, or nothing when the piece was turned back to where it started. The only place a
        /// rotate is ever paid for, published or re-checked, so however many taps a session took it
        /// counts as at most one application.
        /// <para>
        /// Reached from <see cref="Disarm"/> (cancel, another kind's successful application, both run
        /// boundaries), from <see cref="Arm"/> (which replaces the selection without disarming), from
        /// <see cref="TryApplyHold"/> (which permutes the very slots a session is keyed on) and from a
        /// <see cref="TryApplyRotate"/> tap that moves on to a different slot. Clears its own state
        /// before doing anything else, so the game-over re-check it can raise — which lands back in
        /// <see cref="Disarm"/> — finds nothing left to settle.
        /// </para>
        /// </summary>
        private void CommitRotateSession()
        {
            if (_rotateSessionSlotIndex == NO_ROTATE_SESSION)
            {
                return;
            }

            int slotIndex = _rotateSessionSlotIndex;
            Piece originalPiece = _rotateSessionOriginalPiece;
            _rotateSessionSlotIndex = NO_ROTATE_SESSION;
            _rotateSessionOriginalPiece = null;

            Piece currentPiece = _trayModel.GetPiece(slotIndex);
            if (currentPiece == null)
            {
                return;
            }

            // Back to its original orientation: the dock is exactly as the player found it, so there is
            // nothing to pay for, nothing to announce and nothing for the game-over check to re-read.
            if (currentPiece.Id == originalPiece.Id)
            {
                return;
            }

            TrySpend(PowerUpKind.Rotate);

            // Published so an application is an application whatever it targeted — the badge counter
            // that tracks "power-ups used" must see this one too. Zero cleared cells means scoring and
            // the board's clear animation both ignore it, which is exactly right: nothing was cleared.
            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Rotate, clearedCellCount: 0, clearedLineCount: 0));

            // Unlike a park (which only permutes pieces between dock and pocket), this changes *which
            // shapes* the player holds, so the move that kept the run alive may no longer exist.
            _boardSystem.RecheckGameOver();
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
                || IsBannedInActivePathLevel(PowerUpKind.Reroll) || CountOf(PowerUpKind.Reroll).Value <= 0
                || !_gameModeModel.ExtrasEnabled)
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
                || IsBannedInActivePathLevel(PowerUpKind.DoubleMultiplier)
                || CountOf(PowerUpKind.DoubleMultiplier).Value <= 0 || !_gameModeModel.ExtrasEnabled)
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
                || IsBannedInActivePathLevel(PowerUpKind.GhostFit) || CountOf(PowerUpKind.GhostFit).Value <= 0
                || !_gameModeModel.ExtrasEnabled)
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

        /// <summary>
        /// Spends one Hold charge to park the dock piece in <paramref name="slotIndex"/> into the
        /// pocket, swapping it with whatever was already parked there.
        /// <see cref="BoardSystem.TryParkPiece"/> is the mechanism; this is its charge gate and its
        /// spender. The third kind aimed at the tray rather than the board, so like Rotate and Reroll
        /// it clears nothing and scores nothing.
        /// <para>
        /// Follows the "peek before spend" contract of <see cref="TryApplyJoker"/>: holding none is
        /// refused outright before a single count is touched, and every other way a park can fail —
        /// an out-of-range or empty slot, a demolition hammer, a park that would leave the dock with
        /// nothing to drag, a run already over — is reported by <see cref="BoardSystem.TryParkPiece"/>
        /// before anything is spent. Only a park that actually happened is charged for.
        /// </para>
        /// <para>
        /// Parking into an empty pocket and swapping a new piece into an occupied one cost the same
        /// one charge. The swap is the only way a parked piece ever comes back, so gating it exactly
        /// like a park is what keeps a zero-charge pocket from being free to empty — and what can leave
        /// a parked piece stuck until a charge is earned, which the game-over check accounts for.
        /// </para>
        /// <para>
        /// Never armed (see <see cref="Arm"/>): the pocket is invoked by a drag, so there is no armed
        /// selection to release here and, unlike the tray-aimed kinds above, nothing to
        /// <see cref="Disarm"/>. Deliberately publishes no <see cref="PowerUpAppliedMessage"/> either:
        /// a park is a permutation of dock and pocket, not an application with a consequence to score
        /// or count, and the objectives keyed to that message are all about kinds that touch the board.
        /// </para>
        /// </summary>
        public bool TryApplyHold(int slotIndex)
        {
            if (IsLocked(PowerUpKind.Hold) || IsBannedInActivePathLevel(PowerUpKind.Hold)
                || CountOf(PowerUpKind.Hold).Value <= 0 || !_gameModeModel.ExtrasEnabled)
            {
                return false;
            }

            // A park moves pieces between the very slots a rotate session is keyed on, so an open one
            // is settled against the dock as it stands before anything is permuted. Unreachable
            // through the input path today (a press while armed is an aim, never a drag), so this is
            // a guard against the session outliving the piece it was about, not a gameplay rule.
            CommitRotateSession();

            if (!_boardSystem.TryParkPiece(slotIndex))
            {
                return false;
            }

            TrySpend(PowerUpKind.Hold);

            // Published on every successful use, not only the first — see HoldFirstUseMessage, whose
            // publisher stays deliberately dumb about "firstness".
            if (_holdFirstUsePublisher != null)
            {
                _holdFirstUsePublisher.Publish(new HoldFirstUseMessage());
            }

            return true;
        }

        /// <summary>
        /// Asks <see cref="IRewardSource"/> for <paramref name="kind"/> and banks <paramref name="quantity"/>
        /// of it if granted. Returns whether it was granted; a refusal leaves the inventory untouched.
        /// <para>
        /// One ad, one ask, <paramref name="quantity"/> units: the source is consulted once however many
        /// units the ad is worth, so a kind that pays two per ad (<see cref="PowerUpKind.CoinSower"/>,
        /// issue #404) costs the player one watch, not two. Defaults to one so every existing caller keeps
        /// earning exactly what it did.
        /// </para>
        /// <para>
        /// Loops rather than adding in one step, for the reason <see cref="GrantPurchased"/> loops: a
        /// grant of two publishes two <see cref="PowerUpGrantedMessage"/>s with the running count in each,
        /// so anything counting grants sees two because two were granted.
        /// </para>
        /// </summary>
        public async UniTask<bool> GrantRewardAsync(
            PowerUpKind kind, CancellationToken cancellationToken, int quantity = 1)
        {
            RewardResult result = await _rewardSource.RequestRewardAsync(kind, cancellationToken);
            if (!result.Granted)
            {
                return false;
            }

            for (int grantIndex = 0; grantIndex < quantity; grantIndex++)
            {
                Grant(result.Kind);
            }

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

        /// <summary>
        /// Banks <paramref name="quantity"/> of <paramref name="kind"/> for a purchase that has already
        /// been paid for. Called by <see cref="CurrencySystem.TryPurchasePowerUp"/> and by nothing else:
        /// it takes no payment and asks no question, so the coins must be debited before it is reached.
        /// <para>
        /// Shares <see cref="Grant"/> with <see cref="GrantDirect"/> and
        /// <see cref="GrantRewardAsync"/>, so all three earning paths mutate, persist and publish
        /// identically — a bought power-up is indistinguishable from an earned one once it is in the
        /// inventory, which is exactly right: there is only one inventory.
        /// </para>
        /// <para>
        /// Deliberately does not re-check the level gate. <see cref="IsLocked"/> is private to this
        /// class and stays that way; <see cref="PowerUpUnlockLevels.IsUnlockedAt"/> is the public seam,
        /// and the caller checks it there before a single coin is moved. Checking again here would put
        /// the same gate in two places and invite them to disagree — and this path cannot honour a
        /// refusal anyway, since by the time it runs the player has already been charged.
        /// </para>
        /// <para>
        /// Loops rather than adding <paramref name="quantity"/> in one step, so a purchase of three
        /// publishes three <see cref="PowerUpGrantedMessage"/>s with the running count in each. Anything
        /// counting grants — the badge counters, a "+1" flourish — sees three grants because three were
        /// granted, not one grant of three it would have to learn to read.
        /// </para>
        /// </summary>
        public void GrantPurchased(PowerUpKind kind, int quantity)
        {
            for (int grantIndex = 0; grantIndex < quantity; grantIndex++)
            {
                Grant(kind);
            }
        }

        /// <summary>
        /// Spends <paramref name="quantity"/> banked Coin Sower charges in one go — the whole spending
        /// interface of a kind that has no armed-and-aimed lifecycle. Returns whether it was spent; a
        /// refusal leaves the inventory exactly as it found it.
        /// <para>
        /// The charges arrive through the same seam <see cref="PowerUpKind.Hold"/>'s do —
        /// <see cref="GrantRewardAsync"/>, two per ad (issue #404) — and sit in the persisted inventory
        /// until the level-start picker asks for several of them at once. The kind differs from Hold only
        /// in how it leaves: in bulk here, rather than one at a time mid-run.
        /// </para>
        /// <para>
        /// Checked in full before a single decrement, rather than decrementing until it runs dry. This is
        /// the one spend that asks for several at once, so "not enough held" is a real answer here where
        /// for every other kind it is a one-unit peek — and discovering the shortfall half way through
        /// would leave the player with cells sown for charges that were never there. Atomic or nothing.
        /// </para>
        /// <para>
        /// Then a loop over the same single-unit <see cref="TrySpend"/> the other kinds use, mirroring
        /// <see cref="GrantPurchased"/>'s loop over single-unit grants: each unit is decremented and
        /// persisted through the one code path a spend has ever gone through, so a bulk spend cannot
        /// drift from a single one.
        /// </para>
        /// <para>
        /// Does not check the level gate, for the reason <see cref="GrantPurchased"/> does not: the
        /// picker reads the gate at the public <see cref="PowerUpUnlockLevels.IsUnlockedAt"/> seam and
        /// offers nothing below it. Checking again here would put the same gate in two places.
        /// </para>
        /// </summary>
        public bool TrySpendCoinSowerBulk(int quantity)
        {
            if (quantity <= 0 || IsBannedInActivePathLevel(PowerUpKind.CoinSower)
                || _powerUpModel.CoinSowerCount.Value < quantity)
            {
                return false;
            }

            for (int spendIndex = 0; spendIndex < quantity; spendIndex++)
            {
                TrySpend(PowerUpKind.CoinSower);
            }

            return true;
        }

        public void Dispose()
        {
            _runStartedSubscription.Dispose();
            _gameOverSubscription.Dispose();
            _levelNumberSubscription.Dispose();
        }

        /// <summary>Shared exit from armed mode: a successful application, an explicit cancel and both
        /// run boundaries all land here, so the clock is never left held by a selection that is gone —
        /// and, being the one choke point every dropped selection passes through, it is also where an
        /// open rotate session is settled (see <see cref="CommitRotateSession"/>).</summary>
        private void Disarm()
        {
            // Selection dropped first, session settled second: the settle can raise a game over, and
            // a game over must not find a selection still armed behind it. The re-entry that game over
            // makes into this method finds the session already cleared and the drop already done.
            _powerUpModel.Armed.Value = null;
            _timerRunSystem.SetPowerUpArmedPaused(false);
            CommitRotateSession();
        }

        // A run boundary, not a settle: BoardSystem.StartNewRun redraws the whole tray before this
        // message goes out, so by the time it arrives the slot an open session was keyed on no longer
        // holds the piece the session was about — comparing against it would charge (or not) for a
        // reason that has nothing to do with anything the player did. Discarded before Disarm rather
        // than run through CommitRotateSession, which is only correct while the tray it reads is still
        // the one the session opened against.
        private void OnRunStarted(RunStartedMessage message)
        {
            DiscardRotateSession();
            Disarm();
        }

        /// <summary>Drops an open rotate session with no spend, no publish and no game-over re-check —
        /// the settle <see cref="CommitRotateSession"/> performs, minus the settling. For the one caller
        /// where committing would be wrong: see <see cref="OnRunStarted"/>.</summary>
        private void DiscardRotateSession()
        {
            _rotateSessionSlotIndex = NO_ROTATE_SESSION;
            _rotateSessionOriginalPiece = null;
        }

        private void OnGameOver(GameOverMessage message) => Disarm();

        /// <summary>
        /// Publishes <see cref="PowerUpUnlockedMessage"/> for every kind whose gate falls strictly
        /// between the last observed frontier and <paramref name="newLevelNumber"/> — a range rather
        /// than a single equality check, so a frontier that jumps by more than one level (a Path replay,
        /// a debug skip) still announces every gate it crossed instead of only the last. The immediate
        /// replay <c>Subscribe</c> fires with the value already recorded as the baseline is a no-op here.
        /// </summary>
        private void OnLevelNumberChanged(int newLevelNumber)
        {
            int previousLevelNumber = _lastObservedLevelNumber;
            _lastObservedLevelNumber = newLevelNumber;

            if (newLevelNumber <= previousLevelNumber || _powerUpUnlockedPublisher == null)
            {
                return;
            }

            PublishNewlyUnlockedKind(PowerUpKind.Joker, previousLevelNumber, newLevelNumber);
            PublishNewlyUnlockedKind(PowerUpKind.ColorCleanser, previousLevelNumber, newLevelNumber);
            PublishNewlyUnlockedKind(PowerUpKind.Rotate, previousLevelNumber, newLevelNumber);
            PublishNewlyUnlockedKind(PowerUpKind.Reroll, previousLevelNumber, newLevelNumber);
            PublishNewlyUnlockedKind(PowerUpKind.DoubleMultiplier, previousLevelNumber, newLevelNumber);
            PublishNewlyUnlockedKind(PowerUpKind.GhostFit, previousLevelNumber, newLevelNumber);
            PublishNewlyUnlockedKind(PowerUpKind.CoinSower, previousLevelNumber, newLevelNumber);
            PublishNewlyUnlockedKind(PowerUpKind.PaintCross, previousLevelNumber, newLevelNumber);
        }

        private void PublishNewlyUnlockedKind(PowerUpKind kind, int previousLevelNumber, int newLevelNumber)
        {
            int gateLevel = PowerUpUnlockLevels.LevelFor(kind);
            if (gateLevel > previousLevelNumber && gateLevel <= newLevelNumber)
            {
                _powerUpUnlockedPublisher.Publish(new PowerUpUnlockedMessage(kind));
            }
        }

        /// <summary>Delegates to Core so the index a power-up will accept and the geometry the preview
        /// draws for it can never disagree about which rows/columns exist.</summary>
        private bool IsValidRowIndex(int index)
            => PowerUpTargetCells.IsValidRowIndex(_boardModel.Shape, index);

        private bool IsValidColumnIndex(int index)
            => PowerUpTargetCells.IsValidColumnIndex(_boardModel.Shape, index);

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
        /// Whether <paramref name="kind"/> is on the active Path level's authored ban list (see
        /// <see cref="LevelObjectiveConfig.BannedPowerUps"/>). Checked alongside <see cref="IsLocked"/>
        /// in every arm and apply path, refusing the same way.
        /// <para>
        /// False outside <see cref="GameMode.Path"/>, with no active Path level, or for a level the
        /// catalog does not author — Endless and Timed are never affected, whatever a level's config
        /// contains, and a level with no ban list bans nothing.
        /// </para>
        /// </summary>
        private bool IsBannedInActivePathLevel(PowerUpKind kind)
        {
            if (_gameModeModel.CurrentMode.Value != GameMode.Path)
            {
                return false;
            }

            int activeLevelNumber = _pathRunModel.ActiveLevelNumber.Value;
            if (activeLevelNumber == PathRunModel.NO_ACTIVE_LEVEL || _levelCatalog == null)
            {
                return false;
            }

            LevelObjectiveConfig level = _levelCatalog.Find(activeLevelNumber);
            if (level == null)
            {
                return false;
            }

            IReadOnlyList<PowerUpKind> bannedPowerUps = level.BannedPowerUps;
            for (int i = 0; i < bannedPowerUps.Count; i++)
            {
                if (bannedPowerUps[i] == kind)
                {
                    return true;
                }
            }

            return false;
        }

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

        /// <summary>
        /// <paramref name="targetCell"/> is the cell the player aimed at, and is only ever passed for
        /// <see cref="PowerUpKind.ColorCleanser"/> (issue #332) — Bomb/Row/Column pass none, since none
        /// of them target a single cell in the sense <c>PowerUpAppliedMessage.TargetCell</c> means. When
        /// present, <see cref="PowerUpClearResult.ClearedCells"/> is also handed to the message as
        /// <c>ClearedCellPositions</c> so the Presentation layer's beam visual has both ends of every
        /// beam to draw without changing anything about what this clear actually destroys.
        /// </summary>
        private void Apply(PowerUpKind kind, PowerUpClearResult result, GridPosition? targetCell = null)
        {
            if (result.AnyCleared)
            {
                _boardModel.NotifyPowerUpCleared(result.ClearedCells);
            }

            // The gem count is read from the triggers rather than from the effects applied below, for the
            // reason the joker path states: a gem multiplies the event that destroyed it, so the count
            // has to be in hand at the moment that event scores — which is this publish.
            // A reinforced cell this clear only damaged is still standing, so nothing above repaints it.
            _boardModel.NotifyHitCountsRefreshed();

            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                kind, result.ClearedCellCount, clearedLineCount: 0,
                emptiedLineCount: result.EmptiedLineCount, wasClutchSave: false,
                destroyedScoreGemCount: ScoreGemEffect.CountDestroyed(result.TriggeredSpecials),
                reinforcedCellsFullyClearedCount: result.ReinforcedCellsFullyClearedCount,
                destroyedCellCountByColour: result.DestroyedCellCountByColour,
                timerCellsClearedInTimeCount: TimerCellClearEffect.CountDestroyed(result.TriggeredSpecials),
                targetCell: targetCell,
                clearedCellPositions: targetCell.HasValue ? result.ClearedCells : null,
                destroyedDiamondCountByColour: DiamondClearEffect.CountDestroyedByColour(result.TriggeredSpecials)));

            ApplyTriggeredSpecials(result.TriggeredSpecials);
        }

        /// <summary>
        /// Detonates the special cells this power-up's clear destroyed. A special block behaves the
        /// same whatever destroyed it, so a core taken out by a Bomb blasts exactly as one taken out by
        /// a completed line does — <see cref="PowerUpClearResolver"/> detects them through the same
        /// <see cref="SpecialCellDetection"/> pass a placement's clear uses, and this applies them
        /// through the same effects.
        /// <para>
        /// Every installed effect sees every trigger and ignores the kinds that are not its own, so a
        /// Row Clear that destroys a laser wipes that laser's column exactly as a completed row would —
        /// the axis the resolver recorded is what decides it, not which power-up was spent.
        /// </para>
        /// <para>
        /// <see cref="SpecialCellKind.ScoreGem"/> is deliberately absent from the loop: it destroys
        /// nothing, so there is no board effect to apply, and its one consequence — tripling this
        /// application's score — was already read off the same triggers and published above.
        /// </para>
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
            _laserEffect.BeginResolution();
            _vortexEffect.BeginResolution();
            _coinEffect.BeginResolution();

            for (int i = 0; i < triggers.Count; i++)
            {
                // No kind filter here: each effect's own guard is the filter, so adding a kind is
                // adding an effect rather than editing this loop.
                _explosiveCoreEffect.Apply(_boardModel.Board, triggers[i]);
                _laserEffect.Apply(_boardModel.Board, triggers[i]);
                _vortexEffect.Apply(_boardModel.Board, triggers[i]);
                _coinEffect.Apply(_boardModel.Board, triggers[i]);
            }

            // The vortex's fill never clears a line itself — it only fills island cells and relies on a
            // resolver re-checking fullness to actually clear whatever that completed (see
            // VortexEffect). A power-up's one-shot clear has no resolver of its own, so this
            // follow-through gives it one: exactly CascadeClearResolver's own loop, seeded by whatever
            // the fill above just completed and by nothing else — no other path can leave a full line
            // sitting on the board between placements (a core's or laser's wipe only ever removes
            // cells). A special cell caught inside a line this uncovers re-triggers through the same
            // mechanism, chained as far as the resolver's own cap allows.
            CascadeClearResult fillFollowThrough =
                CascadeClearResolver.ResolveCascade(_boardModel.Board, _specialCellEffects);
            for (int phaseIndex = 0; phaseIndex < fillFollowThrough.Phases.Count; phaseIndex++)
            {
                LineClearResult phase = fillFollowThrough.Phases[phaseIndex];
                if (phase.AnyCleared)
                {
                    _boardModel.NotifyCleared(phase);
                }
            }

            // Announced exactly as BoardSystem announces a core's bonus wipe (issue #398): the cells it
            // emptied through the "this cell is empty now" path, then the count for scoring.
            IReadOnlyList<GridPosition> coreWipedCells = _explosiveCoreEffect.WipedCells;
            if (coreWipedCells.Count > 0)
            {
                _boardModel.NotifyPowerUpCleared(coreWipedCells);
                _explosiveCoreDetonatedPublisher.Publish(
                    new ExplosiveCoreDetonatedMessage(coreWipedCells.Count));
            }

            IReadOnlyList<GridPosition> wipedCells = _laserEffect.WipedCells;
            if (wipedCells.Count > 0)
            {
                _boardModel.NotifyPowerUpCleared(wipedCells);
                _laserFiredPublisher.Publish(new LaserFiredMessage(wipedCells.Count));
            }

            // A vortex's work is announced through its own seam rather than the cleared-cells one,
            // exactly as the placement path announces it: nothing was destroyed, so there is no cell to
            // fade — a fill creates a cell and a hand-off only relabels one. Published only when
            // something actually happened, which is the contract VortexIslandFilledMessage states.
            IReadOnlyList<GridPosition> islandFilledCells = _vortexEffect.FilledCells;
            IReadOnlyList<GridPosition> vortexHandOffTargets = _vortexEffect.HandOffTargets;
            if (islandFilledCells.Count > 0)
            {
                _boardModel.NotifyIslandFilled(islandFilledCells);
            }

            if (vortexHandOffTargets.Count > 0)
            {
                _boardModel.NotifyVortexHandedOff(vortexHandOffTargets);
            }

            if (islandFilledCells.Count > 0 || vortexHandOffTargets.Count > 0)
            {
                // Copied, unlike the counts above and for the reason the placement path copies it: the
                // effect's lists are buffers this instance overwrites on the next application, and a
                // subscriber animating the fill over several frames would otherwise read the next one's
                // data halfway through. One small pair of lists per application that did either.
                _vortexIslandFilledPublisher.Publish(new VortexIslandFilledMessage(
                    new List<GridPosition>(islandFilledCells), new List<GridPosition>(vortexHandOffTargets)));
            }

            // A coin cell a Bomb destroys pays exactly as one a completed line destroys does, which is
            // the whole point of this method existing: a special block behaves the same whatever
            // destroyed it. Nothing on the board changed for it, so unlike the two above there is no
            // cell to repaint — only a payout to announce, which CurrencySystem (the one writer of the
            // balance) credits.
            int coinsAwarded = _coinEffect.TotalCoinsAwarded;
            if (coinsAwarded > 0)
            {
                _coinCellsClearedPublisher.Publish(new CoinCellsClearedMessage(coinsAwarded));
            }

            // A blast or a wipe can damage a reinforced cell without destroying it, and a cell that is
            // still standing is repainted by none of the sweeps above.
            _boardModel.NotifyHitCountsRefreshed();
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
                case PowerUpKind.CoinSower:
                    return _powerUpModel.CoinSowerCount;
                case PowerUpKind.Hold:
                    return _powerUpModel.HoldCount;
                case PowerUpKind.PaintCross:
                    return _powerUpModel.PaintCrossCount;
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
