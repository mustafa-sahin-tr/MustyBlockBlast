using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using VContainer;
using VContainer.Unity;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns the run: placement, line clearing, tray refill and game-over detection. All rules come
    /// from Core; this System only sequences them and publishes what happened.
    /// </summary>
    public sealed class BoardSystem : IStartable, IDisposable
    {
        /// <summary>How far (in cells) <see cref="ResolvePlacementAnchor"/> will search for a legal
        /// placement when the raw pointer anchor itself is illegal.</summary>
        private const int SnapSearchRadius = 2;

        /// <summary>How full the board must be before a <see cref="SpecialPieceKind.DemolitionHammer"/>
        /// is injected as a last resort. Checked alongside "no legal move left", never on its own: a
        /// crowded board the player can still play on has not earned a life-line.</summary>
        private const float HAMMER_OCCUPANCY_THRESHOLD = 0.9f;

        /// <summary>How many lines one move must clear to earn a
        /// <see cref="SpecialPieceKind.PiercingRocket"/> on the next refill.</summary>
        private const int ROCKET_TRIGGER_LINE_COUNT = 3;

        /// <summary>
        /// Issue #400: false pauses chain lightning formation without deleting it, exactly as
        /// <see cref="LaserSpawnSystem"/> pauses the laser (#399). While this is false,
        /// <see cref="TrySpawnChainLightning"/> returns before its selector runs, so a 3x3 square or a
        /// 1x5 bar that clears a line earns nothing from this mechanic. <see cref="ChainLightningEffect"/>
        /// and <see cref="ChainLightningSpawnSelector"/> stay intact for a possible revival: flip the
        /// flag and the tile forms again exactly as the method's own doc describes.
        /// <para>A <c>static readonly</c> rather than a <c>const</c> so the compiler does not flag the
        /// gated body as unreachable code.</para>
        /// </summary>
        private static readonly bool ChainLightningFormationEnabled = false;

        private readonly BoardModel _boardModel;
        private readonly TrayModel _trayModel;
        private readonly ScoreGemProgressModel _scoreGemProgressModel;
        private readonly VortexProgressModel _vortexProgressModel;
        private readonly WeightedPieceDraw _pieceDraw;

        /// <summary>
        /// Puts the level's authored reinforced cells on the board at the opening of a run. Called
        /// inline from <see cref="StartNewRun"/> rather than subscribing to the message that method
        /// publishes — see <see cref="LevelReinforcedCellSeeder"/> for why the ordering has to be a
        /// property of this call stack.
        /// <para>
        /// Nullable, and null in most unit tests: a board exercised by hand authors no level, and the
        /// seeder's three collaborators are level-catalog concerns those tests have no reason to build.
        /// </para>
        /// </summary>
        private readonly LevelReinforcedCellSeeder _reinforcedCellSeeder;

        /// <summary>
        /// Puts the level's authored timer cells on the board at the opening of a run. Called inline
        /// from <see cref="StartNewRun"/> immediately after <see cref="_reinforcedCellSeeder"/>, for
        /// exactly the same reason — see <see cref="LevelTimerCellSeeder"/>.
        /// <para>
        /// Nullable, and null in most unit tests, for the same reason <see cref="_reinforcedCellSeeder"/>
        /// is.
        /// </para>
        /// </summary>
        private readonly LevelTimerCellSeeder _timerCellSeeder;

        /// <summary>
        /// Marks the level's authored ice sockets on the board at the opening of a run (issue #433).
        /// Called inline from <see cref="StartNewRun"/> immediately after <see cref="_timerCellSeeder"/>,
        /// for exactly the same reason — see <see cref="LevelTargetIceCellSeeder"/>. Unlike the two
        /// seeders above it occupies nothing: a socket starts the run empty.
        /// <para>
        /// Nullable, and null in most unit tests, for the same reason <see cref="_reinforcedCellSeeder"/>
        /// is.
        /// </para>
        /// </summary>
        private readonly LevelTargetIceCellSeeder _targetIceCellSeeder;

        /// <summary>
        /// Which rule set the current run is played under. Read only by the per-placement timer tick
        /// (issue #307 AC4), to decide whether an expired <see cref="SpecialCellKind.Timer"/> cell ends
        /// the run outright (<see cref="GameMode.Path"/>) or merely loses its own objective credit
        /// (Endless/Timed).
        /// <para>
        /// Nullable, and null in most unit tests, for the same reason <see cref="_powerUpModel"/> is —
        /// treated as "not Path mode", which is every test that never opts a board into Path.
        /// </para>
        /// </summary>
        private readonly GameModeModel _gameModeModel;

        /// <summary>
        /// Read, never written, by <see cref="CheckGameOver"/>: a parked piece only counts as a move
        /// the player still has while <see cref="PowerUpModel.HoldCount"/> can pay for the swap that
        /// brings it back. Spending is <see cref="PowerUpSystem"/>'s alone — the Model rather than
        /// that System is taken here because that System already depends on this one.
        /// <para>
        /// Nullable, and null in most unit tests, for the reason <see cref="_reinforcedCellSeeder"/>
        /// is: a board exercised by hand has no inventory, and without one the parked piece is read
        /// the way it was before Hold had a charge — always swappable back out.
        /// </para>
        /// </summary>
        private readonly PowerUpModel _powerUpModel;

        /// <summary>
        /// Rolls the diamond decoration of every piece this System deals (issue #394). Asked once per
        /// dealt slot from <see cref="DealSlot"/>; it gates itself to a Path run with an active diamond
        /// objective, so this System never learns why a draw came out plain.
        /// <para>
        /// Nullable, and null in most unit tests, for the reason <see cref="_reinforcedCellSeeder"/> is:
        /// without one, no piece is ever decorated and every deal is exactly what it was.
        /// </para>
        /// </summary>
        private readonly DiamondPieceDecorator _diamondPieceDecorator;

        /// <summary>Receives one dealt piece's decoration from <see cref="_diamondPieceDecorator"/>
        /// before it is copied onto the tray. Grown to the largest piece dealt and reused, so a
        /// decorated deal allocates nothing after the first of its size.</summary>
        private int[] _diamondDecorationBuffer = Array.Empty<int>();

        /// <summary>Holds the parked piece's decoration across the one moment in
        /// <see cref="TryParkPiece"/> when the Hold slot has been overwritten and the vacated dock slot
        /// not yet written. Grown and reused for the same reason as <see cref="_diamondDecorationBuffer"/>.</summary>
        private int[] _heldDiamondScratch = Array.Empty<int>();

        private readonly PlacementSnapper _placementSnapper = new PlacementSnapper();
        private readonly IPublisher<RunStartedMessage> _runStartedPublisher;
        private readonly IPublisher<PiecePlacedMessage> _piecePlacedPublisher;
        private readonly IPublisher<LinesClearedMessage> _linesClearedPublisher;
        private readonly IPublisher<GameOverMessage> _gameOverPublisher;
        private readonly IPublisher<TrayRefilledMessage> _trayRefilledPublisher;
        private readonly IPublisher<ExplosiveCoreDetonatedMessage> _explosiveCoreDetonatedPublisher;
        private readonly IPublisher<LaserFiredMessage> _laserFiredPublisher;
        private readonly IPublisher<PiercingRocketFiredMessage> _piercingRocketFiredPublisher;
        private readonly IPublisher<VortexIslandFilledMessage> _vortexIslandFilledPublisher;
        private readonly IPublisher<ChainLightningTriggeredMessage> _chainLightningTriggeredPublisher;
        private readonly IPublisher<CoinCellsClearedMessage> _coinCellsClearedPublisher;

        /// <summary>Optional: null in every existing test construction, which predates issue #278.
        /// Guarded on every publish so an un-injected instance behaves exactly as it did before.</summary>
        private readonly IPublisher<SpecialCellSpawnedMessage> _specialCellSpawnedPublisher;

        /// <summary>Optional, for the same reason <see cref="_specialCellSpawnedPublisher"/> is.</summary>
        private readonly IPublisher<SpecialPieceSpawnedMessage> _specialPieceSpawnedPublisher;

        /// <summary>
        /// Pays for the no-moves rescue (issue #370) — the rewarded ad behind
        /// <see cref="TryApplyNoMovesRescueAsync"/>. Optional, and null in every test construction that
        /// predates the rescue: without a source there is nothing that could pay, so no ending is ever
        /// marked rescue-available and the run ends exactly as it did before.
        /// </summary>
        private readonly IRescueRewardSource _rescueRewardSource;

        /// <summary>Optional, for the same reason <see cref="_specialCellSpawnedPublisher"/> is.</summary>
        private readonly IPublisher<RunRescuedMessage> _runRescuedPublisher;

        // One long-lived effect per kind, reset per placement rather than reallocated — each owns the
        // buffer its destroyed cells are reported through.
        private readonly ExplosiveCoreEffect _explosiveCoreEffect = new ExplosiveCoreEffect();
        private readonly LaserEffect _laserEffect = new LaserEffect();

        /// <summary>The odd one out among the board-mutating effects: it creates blocks instead of
        /// destroying them (or hands its own tag off), so what it reports is a list of filled/hand-off
        /// cells rather than a count of emptied ones. Built in the constructor rather than here because
        /// it takes <see cref="_random"/>, for the same reason <see cref="_chainLightningEffect"/> is.</summary>
        private readonly VortexEffect _vortexEffect;

        /// <summary>The one effect that needs a random stream to do its work — it picks the cells it
        /// vaporizes rather than deriving them from geometry. Built in the constructor rather than here
        /// because it takes <see cref="_random"/>, which a field initialiser would read before the
        /// constructor body has assigned it; it shares that one stream deliberately, so a run has a
        /// single seeded source of randomness and replays identically from it.</summary>
        private readonly ChainLightningEffect _chainLightningEffect;

        /// <summary>The piercing rocket's wipe. Not part of <see cref="_specialCellEffects"/>: it is
        /// driven by a <em>piece</em> being placed rather than by a cell being destroyed, so it has no
        /// <see cref="SpecialCellTrigger"/> to be dispatched on and the cascade loop never sees it.</summary>
        private readonly PiercingRocketEffect _piercingRocketEffect = new PiercingRocketEffect();

        /// <summary>The odd one out: it changes nothing on the board and only counts the gems this
        /// placement's resolution destroyed, because the cascade loop keeps its triggers to itself and
        /// this is the one seam through which that count can be learned.</summary>
        private readonly ScoreGemEffect _scoreGemEffect = new ScoreGemEffect();

        /// <summary>The Timer counterpart of <see cref="_scoreGemEffect"/>: changes nothing on the
        /// board, only counts the <see cref="SpecialCellKind.Timer"/> cells this placement's resolution
        /// destroyed BEFORE their countdown reached 0 — the objective data
        /// <see cref="PiecePlacedMessage.TimerCellsClearedInTimeCount"/> is assembled from (issue #307
        /// AC5).</summary>
        private readonly TimerCellClearEffect _timerCellClearEffect = new TimerCellClearEffect();

        /// <summary>The Diamond counterpart of <see cref="_timerCellClearEffect"/>: changes nothing on
        /// the board, only counts the <see cref="SpecialCellKind.Diamond"/> cells this placement's
        /// resolution destroyed, per gem colour — the objective data
        /// <see cref="PiecePlacedMessage.DestroyedDiamondCountByColour"/> is assembled from (issue #393
        /// AC3). Deliberately never read by scoring: a diamond carries no bonus (AC4).</summary>
        private readonly DiamondClearEffect _diamondClearEffect = new DiamondClearEffect();

        /// <summary>The per-placement sum of every destruction path's diamond tally — the phase-based
        /// clears via <see cref="_diamondClearEffect"/> plus the three blast/wipe/strike effects' own
        /// counters. Owned and reused so the once-per-placement sum allocates nothing; handed to
        /// <see cref="PiecePlacedMessage"/> as a snapshot copy, because this buffer is overwritten on
        /// the next placement and a subscriber must never read the next move's data.</summary>
        private readonly int[] _destroyedDiamondCountByColourBuffer = new int[ColourTally.LENGTH];

        /// <summary>The other effect that changes nothing on the board: it only totals what the coin
        /// cells this placement's resolution destroyed are worth. Built in the constructor rather than
        /// here because the per-cell payout is an economy number read from
        /// <see cref="Gameplay.Settings.CurrencyConfig"/>, which Core must not know about.</summary>
        private readonly CoinEffect _coinEffect;

        /// <summary>Every installed effect, presented to the cascade loop as the one effect it takes.
        /// Each effect ignores a trigger of a kind that is not its own, so a trigger reaching both of
        /// them is exactly equivalent to dispatching on the kind.</summary>
        private readonly ISpecialCellEffect _specialCellEffects;

        /// <summary>Used to break a tie between equally valid explosive-core spawn cells, which cannot
        /// arise on the current board shape (see
        /// <see cref="ExplosiveCoreSpawnSelector.SelectSpawnPosition"/>), and to pick which occupied
        /// cell a perfect round's score gem lands on. Whether a qualifying placement or a qualifying
        /// round spawns anything at all is fully deterministic and never touches this.</summary>
        private readonly Random _random;
        // Sized for the three dock slots plus the parked piece, which CheckGameOver appends.
        private readonly List<Piece> _remainingBuffer = new List<Piece>(TrayModel.SLOT_COUNT + 1);
        private readonly Board _previewScratchBoard;
        // Reroll draws a whole set at once and only then writes it to the tray, so a draw that has to
        // be retried never touches a slot. Owned here and reused, so a reroll allocates nothing.
        private readonly Piece[] _rerollPieceBuffer = new Piece[TrayModel.SLOT_COUNT];
        private readonly int[] _rerollColourBuffer = new int[TrayModel.SLOT_COUNT];
        private readonly List<int> _previewRowsBuffer = new List<int>(Board.SIZE);
        private readonly List<int> _previewColumnsBuffer = new List<int>(Board.SIZE);

        /// <summary>Holds the one cell a demolition hammer destroys, so announcing it reuses the same
        /// "here is a list of emptied cells" seam every other non-line destruction does. Owned and
        /// reused, so a hammer costs no allocation.</summary>
        private readonly List<GridPosition> _hammerClearedBuffer = new List<GridPosition>(1);

        /// <summary>The triggers a hammer's single destroyed cell produced — at most one. Reused for the
        /// same reason <see cref="_hammerClearedBuffer"/> is.</summary>
        private readonly List<SpecialCellTrigger> _hammerTriggerBuffer = new List<SpecialCellTrigger>(1);

        /// <summary>Every <see cref="SpecialCellKind.Timer"/> cell <see cref="TimerCellTick"/> converted
        /// to an ordinary cell on the most recent placement. Owned and reused so the once-per-placement
        /// tick allocates nothing (issue #307).</summary>
        private readonly List<GridPosition> _expiredTimerCellsBuffer = new List<GridPosition>();

        /// <summary>
        /// Dock injections owed to the next refill: a golden 1x1 earned by a five-long combo streak
        /// (armed by <see cref="GoldenPieceTriggerSystem"/>) and a piercing rocket earned by a move that
        /// cleared <see cref="ROCKET_TRIGGER_LINE_COUNT"/> lines at once.
        /// <para>
        /// Plain bools rather than a queue: each is earned again by earning its trigger again, so two
        /// golden pieces owed at once is not a thing that can happen — the flag is set by the one event
        /// that can set it and cleared by the one refill that pays it.
        /// </para>
        /// <para>
        /// Run-bound, exactly as the pieces are: <see cref="StartNewRun"/> drops both, so nothing earned
        /// in one run can be handed to the next (AC2).
        /// </para>
        /// </summary>
        private bool _goldenInjectionPending;
        private bool _piercingRocketInjectionPending;

        /// <summary>
        /// Whether this run has already been handed its <see cref="SpecialPieceKind.DemolitionHammer"/>.
        /// <para>
        /// Load-bearing, not a nicety. A hammer destroys one cell, which leaves a hole the smallest
        /// piece fits — so the very next dead end on the same crowded board would meet the trigger's two
        /// conditions all over again and hand out another, and a run that qualifies once could never end
        /// at all. One reprieve per run is what keeps "no moves left" a real ending.
        /// </para>
        /// <para>
        /// Run-bound like the two flags above: <see cref="StartNewRun"/> clears it, so every run gets its
        /// own life-line and none inherits a spent one (AC2).
        /// </para>
        /// </summary>
        private bool _hammerGrantedThisRun;

        /// <summary>
        /// Whether the ending most recently announced can still be taken back through
        /// <see cref="TryApplyNoMovesRescueAsync"/>. Set by the one place that can offer it — the
        /// <see cref="GameOverReason.NoMovesLeft"/> tail of <see cref="CheckGameOver"/>, once the hammer
        /// has failed — and consumed by the accept, the decline, a failed request and
        /// <see cref="StartNewRun"/> alike, so nothing about an offer can outlive the ending it was made
        /// for. Deliberately not a per-run counter: the rescue is unlimited, and each new dead end makes
        /// its own offer.
        /// </summary>
        private bool _isRescueOffered;

        /// <summary>
        /// True from the moment a rescue request is handed to <see cref="_rescueRewardSource"/> until
        /// its answer has been acted on. The re-entrancy guard for the one <c>await</c> in this System:
        /// a second tap while the ad is up must not open a second request, and — since the run is still
        /// over throughout — nothing else can change the board or the dock in the meantime.
        /// </summary>
        private bool _isRescueRequestPending;

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public BoardSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            ScoreGemProgressModel scoreGemProgressModel,
            VortexProgressModel vortexProgressModel,
            WeightedPieceDraw pieceDraw,
            IPublisher<RunStartedMessage> runStartedPublisher,
            IPublisher<PiecePlacedMessage> piecePlacedPublisher,
            IPublisher<LinesClearedMessage> linesClearedPublisher,
            IPublisher<GameOverMessage> gameOverPublisher,
            IPublisher<TrayRefilledMessage> trayRefilledPublisher,
            IPublisher<ExplosiveCoreDetonatedMessage> explosiveCoreDetonatedPublisher,
            IPublisher<LaserFiredMessage> laserFiredPublisher,
            IPublisher<PiercingRocketFiredMessage> piercingRocketFiredPublisher,
            IPublisher<VortexIslandFilledMessage> vortexIslandFilledPublisher,
            IPublisher<ChainLightningTriggeredMessage> chainLightningTriggeredPublisher,
            IPublisher<CoinCellsClearedMessage> coinCellsClearedPublisher,
            CurrencyConfig currencyConfig,
            LevelReinforcedCellSeeder reinforcedCellSeeder,
            PowerUpModel powerUpModel = null,
            IPublisher<SpecialCellSpawnedMessage> specialCellSpawnedPublisher = null,
            IPublisher<SpecialPieceSpawnedMessage> specialPieceSpawnedPublisher = null,
            LevelTimerCellSeeder timerCellSeeder = null,
            GameModeModel gameModeModel = null,
            IRescueRewardSource rescueRewardSource = null,
            IPublisher<RunRescuedMessage> runRescuedPublisher = null,
            DiamondPieceDecorator diamondPieceDecorator = null,
            LevelTargetIceCellSeeder targetIceCellSeeder = null)
            : this(
                boardModel, trayModel, scoreGemProgressModel, vortexProgressModel, pieceDraw,
                runStartedPublisher, piecePlacedPublisher, linesClearedPublisher, gameOverPublisher,
                trayRefilledPublisher, explosiveCoreDetonatedPublisher, laserFiredPublisher,
                piercingRocketFiredPublisher, vortexIslandFilledPublisher, chainLightningTriggeredPublisher,
                coinCellsClearedPublisher, currencyConfig, Environment.TickCount, reinforcedCellSeeder,
                powerUpModel, specialCellSpawnedPublisher, specialPieceSpawnedPublisher,
                timerCellSeeder, gameModeModel, rescueRewardSource, runRescuedPublisher,
                diamondPieceDecorator, targetIceCellSeeder)
        {
        }

        internal BoardSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            ScoreGemProgressModel scoreGemProgressModel,
            VortexProgressModel vortexProgressModel,
            WeightedPieceDraw pieceDraw,
            IPublisher<RunStartedMessage> runStartedPublisher,
            IPublisher<PiecePlacedMessage> piecePlacedPublisher,
            IPublisher<LinesClearedMessage> linesClearedPublisher,
            IPublisher<GameOverMessage> gameOverPublisher,
            IPublisher<TrayRefilledMessage> trayRefilledPublisher,
            IPublisher<ExplosiveCoreDetonatedMessage> explosiveCoreDetonatedPublisher,
            IPublisher<LaserFiredMessage> laserFiredPublisher,
            IPublisher<PiercingRocketFiredMessage> piercingRocketFiredPublisher,
            IPublisher<VortexIslandFilledMessage> vortexIslandFilledPublisher,
            IPublisher<ChainLightningTriggeredMessage> chainLightningTriggeredPublisher,
            IPublisher<CoinCellsClearedMessage> coinCellsClearedPublisher,
            CurrencyConfig currencyConfig,
            int seed,
            LevelReinforcedCellSeeder reinforcedCellSeeder = null,
            PowerUpModel powerUpModel = null,
            IPublisher<SpecialCellSpawnedMessage> specialCellSpawnedPublisher = null,
            IPublisher<SpecialPieceSpawnedMessage> specialPieceSpawnedPublisher = null,
            LevelTimerCellSeeder timerCellSeeder = null,
            GameModeModel gameModeModel = null,
            IRescueRewardSource rescueRewardSource = null,
            IPublisher<RunRescuedMessage> runRescuedPublisher = null,
            DiamondPieceDecorator diamondPieceDecorator = null,
            LevelTargetIceCellSeeder targetIceCellSeeder = null)
        {
            _reinforcedCellSeeder = reinforcedCellSeeder;
            _timerCellSeeder = timerCellSeeder;
            _targetIceCellSeeder = targetIceCellSeeder;
            _diamondPieceDecorator = diamondPieceDecorator;
            _gameModeModel = gameModeModel;
            _powerUpModel = powerUpModel;
            _rescueRewardSource = rescueRewardSource;
            _runRescuedPublisher = runRescuedPublisher;
            _specialCellSpawnedPublisher = specialCellSpawnedPublisher;
            _specialPieceSpawnedPublisher = specialPieceSpawnedPublisher;
            _random = new Random(seed);

            // After the stream it draws from, necessarily: the effect keeps the reference it is handed,
            // and there is exactly one stream per run for every random decision to come out of.
            _chainLightningEffect = new ChainLightningEffect(_random);

            // Same reason, same stream: a fill's colour and a hand-off's target both draw from it.
            _vortexEffect = new VortexEffect(_random);

            // Built from the live board's own outline, not from a default square: a scratch board that
            // disagreed with the real one about geometry could not be copied onto at all.
            _previewScratchBoard = new Board(boardModel.Shape);
            _explosiveCoreDetonatedPublisher = explosiveCoreDetonatedPublisher;
            _laserFiredPublisher = laserFiredPublisher;
            _piercingRocketFiredPublisher = piercingRocketFiredPublisher;
            _vortexIslandFilledPublisher = vortexIslandFilledPublisher;
            _chainLightningTriggeredPublisher = chainLightningTriggeredPublisher;
            _coinCellsClearedPublisher = coinCellsClearedPublisher;
            _coinEffect = new CoinEffect(currencyConfig.CoinCellPayout);
            _specialCellEffects = new CompositeSpecialCellEffect(
                _explosiveCoreEffect, _laserEffect, _scoreGemEffect, _vortexEffect, _chainLightningEffect,
                _coinEffect, _timerCellClearEffect, _diamondClearEffect);
            _boardModel = boardModel;
            _trayModel = trayModel;
            _scoreGemProgressModel = scoreGemProgressModel;
            _vortexProgressModel = vortexProgressModel;
            _pieceDraw = pieceDraw;
            _runStartedPublisher = runStartedPublisher;
            _piecePlacedPublisher = piecePlacedPublisher;
            _linesClearedPublisher = linesClearedPublisher;
            _gameOverPublisher = gameOverPublisher;
            _trayRefilledPublisher = trayRefilledPublisher;
        }

        public bool IsGameOver { get; private set; }

        /// <summary>Whether special cells may spawn under the active mode's ruleset (issue #355). True
        /// when no <see cref="GameModeModel"/> was injected at all — every hand-built test board — so
        /// this gate is inert everywhere it predates it, and reads <see cref="GameModeModel.ExtrasEnabled"/>
        /// otherwise, exactly as <see cref="PowerUpSystem"/> does for the power-up side of the same rule.</summary>
        private bool ExtrasEnabled => _gameModeModel == null || _gameModeModel.ExtrasEnabled;

        void IStartable.Start() => StartNewRun();

        public void StartNewRun()
        {
            _boardModel.ClearAll();

            // Straight after the board is emptied and before the dock is dealt: a reinforced cell is
            // pre-filled and occupied from board creation (AC1), so it has to be standing there before
            // the player is handed anything to place — and certainly before the first line they could
            // complete through it. Synchronous, in this one call stack, rather than driven by the
            // RunStartedMessage published below, which would put the seeding after the refill and make
            // the ordering a property of subscription order.
            if (_reinforcedCellSeeder != null)
            {
                _reinforcedCellSeeder.Seed(_boardModel);
            }

            // Straight after, same call stack, same reasoning: a timer cell also brings its own
            // pre-filled block and must already be standing before the player's first placement (issue
            // #307 AC7/AC8). Order relative to the reinforced-cell seeder above is arbitrary — the two
            // mechanics are mutually exclusive per cell (LevelObjectiveConfig.IsValid refuses a cell
            // authored as both) so neither can ever contend for the same position.
            if (_timerCellSeeder != null)
            {
                _timerCellSeeder.Seed(_boardModel);
            }

            // Last of the three seeders, same call stack, same reasoning (issue #433): the ice marker
            // has to be on the board before the player's first placement can land on it. After the
            // two pre-filling seeders so its "is this cell empty" check sees their blocks — the three
            // mechanics are mutually exclusive per cell (LevelObjectiveConfig.IsValid refuses a cell
            // authored as more than one), so in a valid level the order never matters.
            if (_targetIceCellSeeder != null)
            {
                _targetIceCellSeeder.Seed(_boardModel);
            }

            // Dropped before the refill below, which is the thing that would otherwise pay them: a
            // special piece is earned by the run that triggered it and must never be handed to the next
            // one (AC2).
            _goldenInjectionPending = false;
            _piercingRocketInjectionPending = false;
            _hammerGrantedThisRun = false;

            // An offer belongs to the ending it was made for, and this run has no ending yet. The
            // pending flag is left alone on purpose: a request already in flight against the old run
            // sees IsGameOver false when it lands and refuses itself (see TryApplyNoMovesRescueAsync).
            _isRescueOffered = false;

            // Same reason: cross-clear progress towards the next Score Gem, and line-clear progress
            // towards the next Vortex, belong to the run that earned them and must not carry into the
            // next one.
            _scoreGemProgressModel.Reset();
            _vortexProgressModel.Reset();

            // A parked piece belongs to the run that parked it; carrying it into the next one would
            // hand the player a free piece they never drew.
            _trayModel.ClearHold();
            RefillTray();
            IsGameOver = false;
            _runStartedPublisher.Publish(new RunStartedMessage());
            CheckGameOver();
        }

        /// <summary>True when the tray piece in <paramref name="slotIndex"/> fits at
        /// <paramref name="anchor"/>. Used by the drag preview.</summary>
        public bool CanPlace(int slotIndex, GridPosition anchor)
        {
            if (IsGameOver || !IsValidSlot(slotIndex))
            {
                return false;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                return false;
            }

            // The one dock piece that is never dropped on the board: a hammer is aimed at an occupied
            // cell and destroys it (see TryUseDemolitionHammer). Refused here rather than only in
            // TryPlacePiece, so the drag preview refuses it for the same reason the placement does.
            if (_trayModel.GetSpecialKind(slotIndex) == SpecialPieceKind.DemolitionHammer)
            {
                return false;
            }

            return PlacementRules.CanPlace(_boardModel.Board, piece, anchor);
        }

        /// <summary>Non-mutating query: which rows/columns would clear if the tray piece in
        /// <paramref name="slotIndex"/> were placed at <paramref name="anchor"/>. Returns an empty result
        /// when the placement itself is illegal. Safe to call every drag-update frame — reuses internal
        /// scratch buffers rather than allocating. Used by the drag-preview highlight.</summary>
        public LineClearResult GetWouldClearLines(int slotIndex, GridPosition anchor)
        {
            if (IsGameOver || !IsValidSlot(slotIndex))
            {
                return EmptyPreview();
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                return EmptyPreview();
            }

            return LineClearResolver.PreviewClears(
                _boardModel.Board, piece, anchor, _previewScratchBoard, _previewRowsBuffer, _previewColumnsBuffer);
        }

        private LineClearResult EmptyPreview()
        {
            _previewRowsBuffer.Clear();
            _previewColumnsBuffer.Clear();
            return new LineClearResult(_previewRowsBuffer, _previewColumnsBuffer, 0, 0);
        }

        /// <summary>Starts a new placement-preview session by clearing any sticky lock left over
        /// from a previous drag. Call once when a drag begins.</summary>
        public void BeginPlacementPreview() => _placementSnapper.Reset();

        /// <summary>Resolves the anchor to preview/place at for this frame's raw pointer anchor,
        /// applying the sticky-lock and nearest-candidate snapping on top of it. <paramref name="isValid"/>
        /// is false only when no legal placement exists within the search radius.</summary>
        public GridPosition ResolvePlacementAnchor(int slotIndex, GridPosition rawAnchor, out bool isValid)
        {
            if (IsGameOver || !IsValidSlot(slotIndex))
            {
                isValid = false;
                return rawAnchor;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                isValid = false;
                return rawAnchor;
            }

            SnapResult result = _placementSnapper.Resolve(_boardModel.Board, piece, rawAnchor, SnapSearchRadius);
            isValid = result.IsValid;
            return result.Anchor;
        }

        /// <summary>Places the tray piece if legal, resolves clears, refills the tray when empty and
        /// re-checks game over. Returns false when the placement was illegal (nothing changed).</summary>
        public bool TryPlacePiece(int slotIndex, GridPosition anchor)
        {
            if (!CanPlace(slotIndex, anchor))
            {
                return false;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            int colourId = _trayModel.GetColourId(slotIndex);

            // Read before the slot is consumed, which resets it: this is the last moment the piece about
            // to be placed is known to be a special one.
            SpecialPieceKind pieceKind = _trayModel.GetSpecialKind(slotIndex);

            // Same moment, same reason, for the decoration: it is read off the slot per offset as each
            // cell is written, and the slot is consumed only once the loop is done (issue #394 AC5).
            bool hasDiamonds = _trayModel.HasDiamonds(slotIndex);

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                GridPosition position = anchor + piece.Offsets[i];
                int diamondColourId = hasDiamonds ? _trayModel.GetDiamondColourId(slotIndex, i) : TrayModel.NO_DIAMOND;

                // A decorated cell lands as a diamond only where DiamondCellRules allows one (AC3);
                // anywhere else — and every undecorated cell — is placed exactly as it always was.
                if (diamondColourId != TrayModel.NO_DIAMOND
                    && DiamondCellRules.CanCarryDiamond(_boardModel.Board, position))
                {
                    _boardModel.OccupyDiamond(position, colourId, diamondColourId);
                }
                else
                {
                    _boardModel.Occupy(position, colourId);
                }
            }

            _trayModel.ConsumeSlot(slotIndex);

            // Read before the resolver mutates the board — once a line clears, its cells are gone and
            // "how full was the board under this placement" can no longer be answered.
            int occupiedCellCountBeforeClear = _boardModel.Board.OccupiedCellCount();

            // Same moment, same reason, for the ice sockets (issue #433): "how many were still icy
            // before anything below destroyed a cell" is the half of the melted count that has to be
            // read now. The melt itself happens inside Board.TryDamage on every destruction path —
            // the rocket wipe, the primary clear, every cascaded phase and every effect's own
            // blast/wipe/strike alike — so a before/after scan is what counts all of them without any
            // of them having to report it.
            int iceCellsBefore = _boardModel.Board.CountIceCells();

            // The cascading resolver, not the single-pass one: a cleared special cell may complete
            // further lines, and this is the one call site where that chaining is allowed to happen
            // (a drag preview still uses LineClearResolver directly, via GetWouldClearLines). It is
            // synchronous and resolves state only — spacing the phases out visually is Presentation's
            // job, and CascadeClearResult.Phases is the ordered list it will replay.
            //
            // Reset before resolving, never after: the effect reports the cells it blasted through a
            // buffer it owns, and this is what makes that buffer mean "this placement's blast" rather
            // than "every blast since the run started".
            _explosiveCoreEffect.BeginResolution();
            _laserEffect.BeginResolution();
            _scoreGemEffect.BeginResolution();
            _piercingRocketEffect.BeginResolution();
            _vortexEffect.BeginResolution();
            _chainLightningEffect.BeginResolution();
            _coinEffect.BeginResolution();
            _timerCellClearEffect.BeginResolution();
            _diamondClearEffect.BeginResolution();

            // Before the cascade, deliberately. The rocket empties its row and column whether or not
            // either was full, so running it first is what keeps a cell from being removed by the wipe
            // *and* counted by a completed line — the cascade re-reads fullness on the board the wipe
            // left behind, which is also how a wipe that completes a new line still chains.
            if (pieceKind == SpecialPieceKind.PiercingRocket)
            {
                ApplyPiercingRocket(anchor);
            }

            CascadeClearResult cascade = CascadeClearResolver.ResolveCascade(
                _boardModel.Board, _specialCellEffects);

            // Everything published below reports the PRIMARY phase only — the clear this placement
            // itself caused. Clears a special cell's effect went on to cause are deliberately not
            // summed into it: the player did not line them up, so folding them in would inflate the
            // combo streak and every line-count objective, and the reward policy for cascades is a
            // decision the first sub-issue with a real effect should make explicitly rather than
            // inherit from a summation here. Today the distinction is moot — CascadePhaseCount is
            // always 0 — which is exactly why it is safe to fix the contract now.
            LineClearResult clearResult = cascade.Primary;

            // Every phase repaints, though: the View has to be told about cells any phase emptied.
            for (int phaseIndex = 0; phaseIndex < cascade.Phases.Count; phaseIndex++)
            {
                LineClearResult phase = cascade.Phases[phaseIndex];
                if (phase.AnyCleared)
                {
                    _boardModel.NotifyCleared(phase);
                }
            }

            // A laser's wipe is announced the same way and for the same reason: it empties a line
            // whether or not that line was full, so no LinesClearedMessage describes it.
            IReadOnlyList<GridPosition> wipedCells = _laserEffect.WipedCells;
            bool anyWiped = wipedCells.Count > 0;
            if (anyWiped)
            {
                _boardModel.NotifyPowerUpCleared(wipedCells);
            }

            // An explosive core's bonus wipe is the same shape of event as a laser's (issue #398): one
            // opposite line emptied whether or not it was full, so it is announced the same way.
            IReadOnlyList<GridPosition> coreWipedCells = _explosiveCoreEffect.WipedCells;
            bool anyCoreWiped = coreWipedCells.Count > 0;
            if (anyCoreWiped)
            {
                _boardModel.NotifyPowerUpCleared(coreWipedCells);
            }

            // A rocket's wipe is announced the same way and for the same reason as a laser's: it empties
            // two lines whether or not they were full, so no LinesClearedMessage describes it.
            IReadOnlyList<GridPosition> rocketWipedCells = _piercingRocketEffect.WipedCells;
            bool anyRocketWiped = rocketWipedCells.Count > 0;
            if (anyRocketWiped)
            {
                _boardModel.NotifyPowerUpCleared(rocketWipedCells);
            }

            // A chain lightning's strike is announced the same way as a blast and a wipe: it empties
            // cells scattered anywhere on the board, which is not a line and not a region, so no
            // LinesClearedMessage could describe it either.
            IReadOnlyList<GridPosition> vaporizedCells = _chainLightningEffect.VaporizedCells;
            bool anyVaporized = vaporizedCells.Count > 0;
            if (anyVaporized)
            {
                _boardModel.NotifyPowerUpCleared(vaporizedCells);
            }

            // A vortex's work is announced through its own seam rather than the cleared-cells one:
            // nothing was destroyed, so there is no cell to fade — a fill creates a cell and a hand-off
            // only relabels one, neither of which the "this cell is empty now" path can describe.
            IReadOnlyList<GridPosition> islandFilledCells = _vortexEffect.FilledCells;
            IReadOnlyList<GridPosition> vortexHandOffTargets = _vortexEffect.HandOffTargets;
            bool anyIslandFilled = islandFilledCells.Count > 0;
            bool anyHandedOff = vortexHandOffTargets.Count > 0;
            if (anyIslandFilled)
            {
                _boardModel.NotifyIslandFilled(islandFilledCells);
            }

            if (anyHandedOff)
            {
                _boardModel.NotifyVortexHandedOff(vortexHandOffTargets);
            }

            // After every emptied/filled cell has been announced: a reinforced cell this resolution only
            // damaged is still standing, so nothing above repaints it, and this is the signal that says
            // "still here, but closer to breaking". Cheap and idempotent — see NotifyHitCountsRefreshed.
            _boardModel.NotifyHitCountsRefreshed();

            // And the ice sockets, for the mirror-image reason (issue #433): a socket whose block was
            // just destroyed is EMPTY now, and the "this cell emptied" notifications above say nothing
            // about the thinner ice still sitting on it. Same scan, same cost class.
            _boardModel.NotifyIceLevelsRefreshed();

            // The other half of the melted count: sockets still icy now. Ice only ever goes down, so the
            // difference is exactly the number of sockets this whole resolution melted to 0.
            int iceCellsMelted = iceCellsBefore - _boardModel.Board.CountIceCells();

            bool anyCornerCleared = AnyCornerTouched(
                _boardModel.Board, clearResult.ClearedRows, clearResult.ClearedColumns);

            // Summed from every destruction path this resolution could have taken a timer cell out
            // through — the phase-based clears (primary and cascaded, via _timerCellClearEffect, which
            // the composite applies to every SpecialCellTrigger the cascade collects) AND a special
            // cell's own blast/wipe/strike mid-cascade (the three effects' own counters). Deliberately
            // NOT read off cascade.TotalReinforcedCellsFullyClearedCount's approach of trusting
            // CascadeClearResult alone — that property inherits the known reinforced-cell gap this sum
            // exists to not repeat (issue #307 AC11).
            int timerCellsClearedInTime = _timerCellClearEffect.DestroyedCount
                + _laserEffect.TimerCellsDestroyedCount
                + _explosiveCoreEffect.TimerCellsDestroyedCount
                + _chainLightningEffect.TimerCellsDestroyedCount;

            // The same every-destruction-path sum for diamonds (issue #393 AC3), per gem colour: the
            // phase-based clears through _diamondClearEffect plus the three effects that destroy cells
            // outside a clear phase. Summed into a reused buffer, then copied for the message, because
            // the message may outlive this placement in a subscriber's hands and the buffer will not.
            int[] destroyedDiamondCountByColour = SumDestroyedDiamondsByColour();

            _piecePlacedPublisher.Publish(new PiecePlacedMessage(
                piece.Id, anchor, PieceFamilyClassifier.Classify(piece.Id), piece.CellCount, colourId,
                clearResult.LineCount, clearResult.ClearedRows.Count, clearResult.ClearedColumns.Count,
                clearResult.MonochromeLineCount, _boardModel.Board.IsEmpty(), occupiedCellCountBeforeClear,
                anyCornerCleared, _boardModel.Board.IsCenterCoreEmpty(), _boardModel.Board.HasIsolatedEmptyCells(),
                _scoreGemEffect.DestroyedCount, cascade.TotalReinforcedCellsFullyClearedCount,
                cascade.TotalDestroyedCellCountByColour, timerCellsClearedInTime, destroyedDiamondCountByColour,
                iceCellsMelted));

            if (clearResult.AnyCleared)
            {
                _linesClearedPublisher.Publish(new LinesClearedMessage(
                    clearResult.ClearedRows, clearResult.ClearedColumns, clearResult.ClearedCellCount));
            }

            // Published after the two messages above so a subscriber that reacts to a detonation sees a
            // placement that has already been fully reported, and so the View's line-clear animation
            // claims its own cells before the detonation's sweep does.
            if (anyCoreWiped)
            {
                _explosiveCoreDetonatedPublisher.Publish(
                    new ExplosiveCoreDetonatedMessage(coreWipedCells.Count));
            }

            if (anyWiped)
            {
                _laserFiredPublisher.Publish(new LaserFiredMessage(wipedCells.Count));
            }

            if (anyRocketWiped)
            {
                _piercingRocketFiredPublisher.Publish(
                    new PiercingRocketFiredMessage(rocketWipedCells.Count));
            }

            if (anyVaporized)
            {
                // A count, like the three above and unlike the pulls below: the cells it emptied have
                // already reached the View through the ordinary "this cell is empty now" path, so there
                // is nothing about them a list would add — and nothing to copy.
                _chainLightningTriggeredPublisher.Publish(
                    new ChainLightningTriggeredMessage(vaporizedCells.Count));
            }

            if (anyIslandFilled || anyHandedOff)
            {
                // Copied, unlike every count above: the effect's lists are buffers it overwrites on the
                // next placement, and a subscriber animating the fill over several frames would
                // otherwise be reading next move's data halfway through. One small pair of lists per
                // placement that did either — never per frame.
                _vortexIslandFilledPublisher.Publish(new VortexIslandFilledMessage(
                    new List<GridPosition>(islandFilledCells), new List<GridPosition>(vortexHandOffTargets)));
            }

            // Announced last of all, and on its own channel: a coin cell changed nothing on the board, so
            // there is no cell to repaint and no clear this could be folded into. A total rather than a
            // count, because the intersection doubling is already applied — and a message rather than a
            // write, because CurrencySystem is the one and only writer of the coin balance. Read off the
            // whole resolution, not just the primary phase: a coin destroyed by a cascade, or by the
            // rocket wipe that ran before it, was destroyed just the same and is owed just the same.
            PublishCoinsAwarded();

            // Last of all, and deliberately after PiecePlacedMessage: the spawn occupies a cell, and
            // that message reports whether this placement emptied the board, cleared the centre core
            // and so on. Spawning first would quietly cost the player every perfect-clear reward they
            // just earned. It also reads the board as the whole cascade left it, not mid-cascade: what
            // matters is whether the intersection is free once everything has settled.
            //
            // The whole block is gated on ExtrasEnabled (issue #355): Classic mode (GameMode.Timed) has
            // no special cells at all, so none of these five rewards may ever appear on its board,
            // whatever this placement cleared.
            if (ExtrasEnabled)
            {
                TrySpawnExplosiveCore(clearResult, colourId);

                // Same section, same reasons, and deliberately after the core: the two rewards can be
                // earned by one placement, and the core picks its cell first so a vortex can never take
                // the intersection the core's rule is defined on.
                TrySpawnVortex(clearResult, colourId);

                // Same section, same reasons, and last of the four: one placement can earn more than one
                // reward, and each selector skips a cell that already carries a kind, so spawning in a
                // fixed order is what keeps two rewards off the same cell. The shape that earned this one
                // is read from the piece itself — the only spawn rule in the game that depends on what
                // was placed rather than only on what cleared.
                TrySpawnChainLightning(clearResult, colourId, piece.Id);

                // Read off this placement's own (primary) clear, same as the three spawns above — a gem
                // is owed to the placement that reached its cross-clear count, not deferred to whenever
                // the dock happens to empty.
                TrySpawnScoreGem(clearResult);

                // Same section, last of five: the one reward that reads the piece's own shape and its own
                // cells rather than only the lines that cleared (issue #352). Placed after every spawn
                // above so it naturally lands on whichever occupied cell none of them already claimed —
                // PerfectMatchSpawnSelector excludes any cell that already carries a kind, exactly as the
                // other selectors' own "already special" checks do.
                TrySpawnPerfectMatchBonus(piece, anchor, clearResult);
            }

            // Armed in the same section and for the same reason as the spawns above: it is this
            // placement's reward, read off the placement's own (primary) clear rather than off the whole
            // cascade, because the reward is for the lines the player lined up. Unlike a core it is not
            // put on the board — it is owed to the next refill, which is where a dock injection can
            // happen at all.
            if (clearResult.LineCount >= ROCKET_TRIGGER_LINE_COUNT)
            {
                _piercingRocketInjectionPending = true;
            }

            // The global per-placement timer tick (issue #307 AC2/AC9): every SpecialCellKind.Timer cell
            // still on the board loses exactly one placement of countdown, whether or not this placement
            // touched it. Deliberately unconditional on anything above — it runs for a placement that
            // cleared nothing exactly as it does for one that cascaded through half the board — and
            // deliberately AFTER every clear/spawn/reward above has already settled, so a cell this very
            // placement cleared (and is therefore already gone) is correctly skipped by the scan rather
            // than ticked down and immediately "expired" a second time.
            TimerCellTick.Tick(_boardModel.Board, _expiredTimerCellsBuffer);
            if (_expiredTimerCellsBuffer.Count > 0)
            {
                _boardModel.NotifyTimerCellsExpired(_expiredTimerCellsBuffer);

                // Path mode: any single expiry ends the run immediately as a failure, unconditionally —
                // not gated on whether the level's objective could still mathematically be met some
                // other way (issue #307 AC4). Endless/Timed: the run simply continues; the expired
                // cell already lost its own objective credit by no longer being able to produce a
                // SpecialCellTrigger of kind Timer when it eventually clears.
                if (_gameModeModel != null && _gameModeModel.CurrentMode.Value == GameMode.Path)
                {
                    ForceGameOver(GameOverReason.ObjectiveMissed);
                }
            }

            // Repainted the same way NotifyHitCountsRefreshed repaints a reinforced cell's damage: a
            // scan over every still-standing timer cell, cheap enough once per placement. Safe to call
            // even when the run just ended above — CheckGameOver, not this, is what reads IsGameOver.
            _boardModel.NotifyTimerCountdownsRefreshed();

            if (_trayModel.IsEmpty)
            {
                RefillTray();
            }

            CheckGameOver();
            return true;
        }

        /// <summary>
        /// Parks the dock piece in <paramref name="slotIndex"/> into the Hold slot, swapping it with
        /// whatever was already parked there. Atomic by construction: the vacated dock slot is
        /// overwritten with the previously held piece (or emptied) in the same call, so no action can
        /// ever leave two pieces in one slot or the same piece in two places.
        /// <para>
        /// The mechanism only. Whether the player may park at all is a Hold charge question, and that
        /// gate — and the spend behind it — is <see cref="PowerUpSystem.TryApplyHold"/>'s, the one
        /// caller outside tests. Internal, like <see cref="TryRerollTray"/>, for the same reason: a
        /// View reaching this directly would park for free.
        /// </para>
        /// <para>
        /// This is explicitly <em>not</em> a placement. Nothing is put on the board, so nothing scores,
        /// no line can clear, the combo streak is neither advanced nor broken, and the tray is not
        /// refilled — the pieces involved were already drawn and are merely somewhere else now.
        /// </para>
        /// <para>
        /// Refused when it would leave the dock with nothing to drag. The Hold slot is fed from the
        /// dock and only ever emptied by the swap that refills it, so a dock emptied by parking could
        /// never be refilled (a refill is a placement's consequence) and the run would be stuck with no
        /// piece to move. A swap can never hit this case: the held piece takes the vacated slot.
        /// </para>
        /// Returns false when nothing changed.
        /// </summary>
        internal bool TryParkPiece(int slotIndex)
        {
            if (IsGameOver || !IsValidSlot(slotIndex))
            {
                return false;
            }

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                return false;
            }

            // A hammer is not a piece to park: it is never dragged anywhere, and pocketing it would take
            // the life-line out of the dock the game-over check reads it from.
            if (_trayModel.GetSpecialKind(slotIndex) == SpecialPieceKind.DemolitionHammer)
            {
                return false;
            }

            if (!_trayModel.IsHoldOccupied && _trayModel.OccupiedSlotCount == 1)
            {
                return false;
            }

            Piece previouslyHeld = _trayModel.HeldPiece;
            int previouslyHeldColourId = _trayModel.HeldColourId;
            SpecialPieceKind previouslyHeldKind = _trayModel.HeldSpecialKind;

            // The decoration travels too, and has to be copied out first: SetHeld below overwrites the
            // pocket's buffer, which is the only place the outgoing piece's gems are recorded.
            IReadOnlyList<int> previouslyHeldDiamonds = null;
            if (previouslyHeld != null && _trayModel.HasHeldDiamonds)
            {
                EnsureCapacity(ref _heldDiamondScratch, previouslyHeld.CellCount);
                CopyInto(_trayModel.HeldDiamondColourIds, _heldDiamondScratch);
                previouslyHeldDiamonds = _heldDiamondScratch;
            }

            // The kind travels with the piece in both directions. A golden 1x1 set aside and taken back
            // out is the same golden 1x1 — dropping the tag on the way through the pocket would quietly
            // demote a piece the player earned. The same goes for a diamond-decorated piece (issue #394).
            _trayModel.SetHeld(
                piece, _trayModel.GetColourId(slotIndex), _trayModel.GetSpecialKind(slotIndex),
                _trayModel.GetDiamondColourIds(slotIndex));
            _trayModel.SetSlot(
                slotIndex, previouslyHeld, previouslyHeldColourId, previouslyHeldKind, previouslyHeldDiamonds);

            // No game-over re-check: a park only permutes pieces between the dock and the pocket, so
            // the set of pieces the player can still play is exactly the one CheckGameOver last found
            // a move in.
            return true;
        }

        /// <summary>
        /// Ends the run for a reason the board cannot detect itself — the timed-mode clock expiring, or
        /// a Path-mode level's objective being met — with the caller supplying that reason. Kept here
        /// so the game-over
        /// invariant has exactly one owner; callers must never set their own end-of-run state.
        /// Already-over runs are a no-op.
        /// </summary>
        public void ForceGameOver(GameOverReason reason)
        {
            if (IsGameOver)
            {
                return;
            }

            // Never rescue-available from here: a rescue answers "no piece fits", and every reason a
            // caller can supply is something else (the clock, an objective). The one NoMovesLeft ending
            // that can be rescued is CheckGameOver's own, which reaches EndRun directly.
            EndRun(reason, isRescueAvailable: false);
        }

        /// <summary>
        /// Takes back the <see cref="GameOverReason.NoMovesLeft"/> ending most recently announced, if it
        /// was marked <see cref="GameOverMessage.IsRescueAvailable"/> (issue #370): asks
        /// <see cref="IRescueRewardSource"/> to pay for it and, once granted, replaces all three dock
        /// slots with a fresh set from <see cref="WeightedPieceDraw.TryDrawLineClearingSet"/>, clears
        /// <see cref="IsGameOver"/>, publishes <see cref="RunRescuedMessage"/> and re-checks the new dock.
        /// <para>
        /// Returns whether the rescue was granted and applied — <em>not</em> whether the run survived
        /// it. The replacement set is biased towards a line clear and preferred solvable, but on a board
        /// no catalog piece fits it can still be dead; that is a fresh dead end, and the re-check ends
        /// the run again, marked rescue-available again, exactly as the first one was. The rescue is
        /// unlimited, not capped: there is no retry budget beyond the draw's own.
        /// </para>
        /// <para>
        /// Whole-set discard, Hold slot untouched — <see cref="TryRerollTray"/>'s contract, for the same
        /// reasons: every dock piece was just found unplaceable so none is worth keeping, and a parked
        /// piece was set aside deliberately and is not on offer. No <see cref="TrayRefilledMessage"/>
        /// either, for the reason that method gives. Unlike a reroll, nothing here reads or spends the
        /// inventory: the ad is the whole price, and <see cref="PowerUpKind.Reroll"/>'s count is neither
        /// consulted nor changed.
        /// </para>
        /// <para>
        /// Refused — false, nothing requested, nothing changed — when the run is not over, when the
        /// ending was not rescue-available (or the offer was already consumed by an earlier accept,
        /// decline or failed request), and while a request is still in flight. A refusal
        /// (<see cref="RescueRewardResult.Granted"/> false), a cancellation and a source that throws all
        /// leave the run ended exactly as it was, with the offer consumed: no second attempt against the
        /// same ending, no <see cref="GameOverMessage"/> re-published for it, and no state left pending.
        /// </para>
        /// </summary>
        public async UniTask<bool> TryApplyNoMovesRescueAsync(CancellationToken cancellationToken)
        {
            if (!IsGameOver || !_isRescueOffered || _isRescueRequestPending || _rescueRewardSource == null)
            {
                return false;
            }

            _isRescueRequestPending = true;
            bool granted;
            try
            {
                RescueRewardResult result = await _rescueRewardSource.RequestRescueRewardAsync(cancellationToken);
                granted = result.Granted;
            }
            catch (OperationCanceledException)
            {
                // The caller went away mid-request — a View destroyed, a scene unloaded. Nothing to
                // undo: the run was over before the request and is over still.
                granted = false;
            }
            catch (Exception)
            {
                // A source that errors (an SDK failing to load a fill, say) is treated as a refusal for
                // the run's sake — over, offer consumed, nothing pending — but the error itself is not
                // this System's to hide: it goes on to the caller, exactly as PowerUpSystem's grant lets
                // its source's errors through.
                _isRescueOffered = false;
                throw;
            }
            finally
            {
                _isRescueRequestPending = false;
            }

            // Re-read after the await, not before it: a new run can have opened while the ad was up
            // (StartNewRun does not wait for anyone), and a rescue that landed on it would hand the
            // player a free reroll of a dock that was never dead.
            if (!granted || !IsGameOver || !_isRescueOffered)
            {
                _isRescueOffered = false;
                return false;
            }

            _isRescueOffered = false;

            // The return value is intentionally ignored, as TryRerollTray ignores its draw's: false only
            // means no attempt produced a clearing piece, and the buffers then hold the best merely
            // solvable set seen — or, on a board nothing fits, a complete set the re-check below ends the
            // run on. Either way the player gets three real pieces.
            _pieceDraw.TryDrawLineClearingSet(_boardModel.Board, _rerollPieceBuffer, _rerollColourBuffer, out _);

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                DealSlot(slotIndex, _rerollPieceBuffer[slotIndex], _rerollColourBuffer[slotIndex]);
            }

            // Live again before anyone is told, so a subscriber that reacts to the rescue by reading
            // this System (can the strip re-enable? may the player drag?) gets the answer the message
            // implies. Published before the re-check, so "rescued" always precedes any second "over".
            IsGameOver = false;
            if (_runRescuedPublisher != null)
            {
                _runRescuedPublisher.Publish(new RunRescuedMessage());
            }

            CheckGameOver();
            return true;
        }

        /// <summary>
        /// Declines the rescue most recently offered (issue #370): the run stays ended exactly as it was,
        /// and the offer is consumed so a later <see cref="TryApplyNoMovesRescueAsync"/> against the same
        /// ending is refused rather than honoured. No message is published — the ending already was, and
        /// nothing about it has changed. A no-op when nothing is on offer.
        /// </summary>
        public void DeclineNoMovesRescue() => _isRescueOffered = false;

        /// <summary>
        /// The one place a run ends. Sets the invariant and publishes the ending; when
        /// <paramref name="isRescueAvailable"/> is set, also records that this ending may be taken back.
        /// </summary>
        private void EndRun(GameOverReason reason, bool isRescueAvailable)
        {
            IsGameOver = true;
            _isRescueOffered = isRescueAvailable;
            _gameOverPublisher.Publish(new GameOverMessage(reason, isRescueAvailable));
        }

        /// <summary>
        /// Replaces all three dock pieces with a freshly drawn set for the Reroll power-up. Unlike a
        /// refill this does not wait for the dock to empty: whatever is in the three slots is discarded,
        /// occupied or not.
        /// <para>
        /// The set comes from <see cref="WeightedPieceDraw.TryDrawSolvableSet"/>, so at least one of the
        /// three pieces fits the current board — the one draw in the game that is guaranteed. Ordinary
        /// refills keep using the unguaranteed <see cref="WeightedPieceDraw.DrawPiece"/>, deliberately:
        /// an unplayable refill is a legitimate game over (docs/game-design.md, "Drawing pieces").
        /// </para>
        /// <para>
        /// Deliberately does <em>not</em> publish <see cref="TrayRefilledMessage"/>. That message means
        /// "the dock ran dry and was restocked", and its only subscriber restarts the timed-mode
        /// countdown from full on it. A reroll never empties the dock and is a discard rather than a
        /// dock played out, so firing it would quietly turn Reroll into a full clock reset in Timed mode
        /// — a far stronger effect than the one this power-up is scoped to. Views do not need it either:
        /// they repaint from <see cref="TrayModel.SlotChanged"/>, which every write below raises.
        /// </para>
        /// <para>
        /// The Hold slot is untouched: a parked piece was set aside deliberately and is not on offer, so
        /// it is not part of what a reroll discards.
        /// </para>
        /// Returns false when the run is already over — nothing is drawn and nothing changes.
        /// </summary>
        internal bool TryRerollTray(out bool wasClutchSave)
        {
            wasClutchSave = false;

            if (IsGameOver)
            {
                return false;
            }

            // Read before the draw overwrites the dock — this is about the tray as it stood the moment
            // Reroll was spent, not about whatever the fresh draw happens to contain. Only the dock
            // counts, deliberately excluding the Hold slot: since CheckGameOver already ends the run
            // the moment dock AND Hold are both dead, a dead dock with a live run only ever means the
            // Hold slot was already the thing keeping the player alive — so this is never a genuine
            // rescue, just a signal that Reroll was spent while the held piece was doing that job.
            // See ObjectiveType.RerollSave's doc comment for the full reasoning.
            _trayModel.CollectRemaining(_remainingBuffer);
            bool hadNoLegalMoves = !MoveAvailability.HasAnyMove(_boardModel.Board, _remainingBuffer);

            // The return value is intentionally ignored: false means the bounded retry gave up and the
            // buffers hold the last attempt instead. The player still gets three real pieces, and the
            // re-check below ends the run if none of them fits — exactly as it would for any other
            // change to the shapes on offer.
            _pieceDraw.TryDrawSolvableSet(_boardModel.Board, _rerollPieceBuffer, _rerollColourBuffer);

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                DealSlot(slotIndex, _rerollPieceBuffer[slotIndex], _rerollColourBuffer[slotIndex]);
            }

            wasClutchSave = hadNoLegalMoves && MoveAvailability.HasAnyMove(_boardModel.Board, _rerollPieceBuffer);

            RecheckGameOver();
            return true;
        }

        /// <summary>
        /// Arms a <see cref="SpecialPieceKind.Golden"/> injection for the next refill, on behalf of
        /// <see cref="GoldenPieceTriggerSystem"/> — whose trigger is the combo streak, a score concept
        /// this System deliberately never reads.
        /// <para>
        /// A request, not a write: the dock belongs to this System, and a refill is the only moment a
        /// slot can be overridden, so the caller says "the player earned one" and this decides when it
        /// lands. Arming twice before a refill is idempotent, which is the intended reading — the reward
        /// is one piece, not a queue of them.
        /// </para>
        /// </summary>
        internal void RequestGoldenPieceInjection() => _goldenInjectionPending = true;

        /// <summary>
        /// Spends the <see cref="SpecialPieceKind.DemolitionHammer"/> in <paramref name="slotIndex"/> on
        /// <paramref name="target"/>, destroying that one occupied cell and nothing else.
        /// <para>
        /// The one dock piece that is used rather than placed, so it has its own entry point instead of
        /// going through <see cref="TryPlacePiece"/>: there is no shape to fit and no anchor to snap —
        /// the player arms the slot and taps a cell, exactly as they aim a power-up. It is nonetheless
        /// <em>not</em> a <see cref="Gameplay.PowerUpKind"/> and never enters the inventory: it is
        /// injected by a board condition, consumed by this call, and gone (AC2, AC7).
        /// </para>
        /// <para>
        /// Follows the "peek before spend" contract <see cref="PowerUpSystem.TryApplyJoker"/> sets: an
        /// empty target, an off-board one, a slot holding something else and an already-over run are all
        /// refused outright — nothing is destroyed and nothing is consumed, so a misplaced tap does not
        /// cost the player their life-line.
        /// </para>
        /// </summary>
        public bool TryUseDemolitionHammer(int slotIndex, GridPosition target)
        {
            if (IsGameOver || !IsValidSlot(slotIndex)
                || _trayModel.GetSpecialKind(slotIndex) != SpecialPieceKind.DemolitionHammer
                || !_boardModel.Board.IsPlayable(target)
                || !_boardModel.Board.IsOccupied(target))
            {
                return false;
            }

            // Read before the cell is cleared, which resets its kind: a special block destroyed by a
            // hammer fires exactly as one destroyed by a completed line does, so this goes through the
            // same detection pass every other destroying path uses. No axis — a single cell is not a
            // line, so a laser caught here has no opposite to compute and wipes both ways.
            _hammerClearedBuffer.Clear();
            _hammerClearedBuffer.Add(target);

            // Through the same damage gate, in the same order, as every other destroying path: a hammer
            // swung at a reinforced cell spends one of its hits and leaves it standing (AC5), which
            // empties this buffer — so the cell is reported as neither destroyed nor triggering, and
            // detection below still reads the kind of a cell that really is going.
            //
            // The returned count (reinforced cells finished off, issue #154's figure) is deliberately
            // dropped: a hammer publishes no message that carries it, and inventing one for a count
            // nothing reads yet is #154's call to make.
            ReinforcedCellDamage.SpendHits(_boardModel.Board, _hammerClearedBuffer);

            _hammerTriggerBuffer.Clear();
            SpecialCellDetection.CollectTriggered(
                _boardModel.Board, _hammerClearedBuffer, _hammerTriggerBuffer);

            ReinforcedCellDamage.RemoveAll(_boardModel.Board, _hammerClearedBuffer);

            _boardModel.NotifyPowerUpCleared(_hammerClearedBuffer);
            _boardModel.NotifyHitCountsRefreshed();

            // A hammered block on an ice socket melted one level on its way out (Board.TryDamage), and
            // the socket is empty now — repainted for the reason the placement path repaints its own.
            // The objective credit for a socket a hammer melts to 0 is dropped along with the
            // reinforced count above: a hammer publishes no message that could carry it.
            _boardModel.NotifyIceLevelsRefreshed();

            // Consumed before the effects below can end the run, so AC7 holds whatever they go on to do:
            // the hammer is spent exactly once, at the moment it was used.
            _trayModel.ConsumeSlot(slotIndex);

            ApplyHammerTriggeredSpecials();

            // Destroying a cell can never complete a line, so there is nothing to cascade — but the dock
            // may now be empty, and a hammer is not a placement, so nothing else would refill it. Without
            // this the run would end on the very next check with an empty dock.
            if (_trayModel.IsEmpty)
            {
                RefillTray();
            }

            RecheckGameOver();
            return true;
        }

        /// <summary>
        /// Re-runs the no-moves-left check after something outside this system changed which shapes the
        /// player holds — the Rotate power-up, which swaps a dock slot's piece for another orientation
        /// of it, and Reroll, which replaces all three at once.
        /// <para>
        /// Deliberately a request, not a verdict: the caller says "the tray's shapes changed", and this
        /// system alone decides whether that ends the run, so the game-over invariant keeps its single
        /// owner. Re-checking a run that is already over is a no-op.
        /// </para>
        /// </summary>
        internal void RecheckGameOver()
        {
            if (IsGameOver)
            {
                return;
            }

            CheckGameOver();
        }

        public void Dispose()
        {
        }

        private static bool IsValidSlot(int slotIndex)
            => slotIndex >= 0 && slotIndex < TrayModel.SLOT_COUNT;

        /// <summary>
        /// True when this clear touched a board corner. Clearing row 0 or row SIZE-1 alone already
        /// touches two corners (every cell in that row, including columns 0 and SIZE-1, is cleared);
        /// symmetrically for column 0/SIZE-1 — so checking membership of just these four indices,
        /// without cross-referencing specific (row, column) pairs, is sufficient.
        /// </summary>
        private static bool AnyCornerTouched(
            Board board, IReadOnlyList<int> clearedRows, IReadOnlyList<int> clearedColumns)
        {
            // The last row index is the board's height and the last column index its width, which are
            // the same number only on a square board.
            return ContainsEdgeIndex(clearedRows, board.Height - 1)
                || ContainsEdgeIndex(clearedColumns, board.Width - 1);
        }

        private static bool ContainsEdgeIndex(IReadOnlyList<int> indices, int lastIndex)
        {
            for (int indexPosition = 0; indexPosition < indices.Count; indexPosition++)
            {
                if (indices[indexPosition] == 0 || indices[indexPosition] == lastIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Spawns this placement's <see cref="SpecialCellKind.ExplosiveCore"/> reward, when it earned
        /// one: a placement that cleared at least one row and at least one column always does, with no
        /// probability roll (see <see cref="ExplosiveCoreSpawnSelector"/>). A placement that earned
        /// none, or one whose intersection has no valid cell, is silently skipped — not an error state.
        /// <para>
        /// Reads <paramref name="clearResult"/>, the placement's own (primary) clear, rather than the
        /// whole cascade: the reward is for the row and column the player lined up, exactly as scoring
        /// and objectives read that same phase.
        /// </para>
        /// </summary>
        private void TrySpawnExplosiveCore(LineClearResult clearResult, int colourId)
        {
            GridPosition? spawn = ExplosiveCoreSpawnSelector.SelectSpawnPosition(
                _boardModel.Board, clearResult.ClearedRows, clearResult.ClearedColumns, _random);
            if (spawn == null)
            {
                return;
            }

            GridPosition position = spawn.Value;

            // The ordinary case: the intersection was emptied by this very clear, so the core needs a
            // block to sit on. It borrows the placed piece's colour rather than introducing one of its
            // own — the icon is what marks it as special, not a bespoke colour id no theme defines.
            // The other case is the neighbour fallback, which lands on a cell that is already occupied:
            // that block is converted into a core in place, so its colour and occupancy are left alone.
            if (!_boardModel.Board.IsOccupied(position))
            {
                _boardModel.Occupy(position, colourId);
            }

            // After any occupancy write, so the View is told the cell is filled before it is told what
            // the filled cell is.
            _boardModel.SetSpecialKind(position, SpecialCellKind.ExplosiveCore);
            PublishSpecialCellSpawned(SpecialCellKind.ExplosiveCore, position);
        }

        /// <summary>
        /// Spawns this placement's <see cref="SpecialCellKind.Vortex"/> reward, when it earned one:
        /// every row and column cleared, run-wide, feeds one running total
        /// (<see cref="VortexProgressModel"/>), and the placement that carries the total to 5 or beyond
        /// always spawns one, with no probability roll. A placement that earned none, or one whose
        /// cleared lines hold no valid cell, is silently skipped — not an error state.
        /// <para>
        /// Reads <paramref name="clearResult"/>, the placement's own (primary) clear, rather than the
        /// whole cascade, exactly as the explosive core's rule does: the reward is for the lines the
        /// player lined up.
        /// </para>
        /// </summary>
        private void TrySpawnVortex(LineClearResult clearResult, int colourId)
        {
            if (!_vortexProgressModel.RecordLinesCleared(clearResult.LineCount))
            {
                return;
            }

            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                _boardModel.Board, clearResult.ClearedRows, clearResult.ClearedColumns);
            if (spawn == null)
            {
                return;
            }

            GridPosition position = spawn.Value;

            // The cell was emptied by this very clear, so the tile needs a block to sit on. It borrows
            // the placed piece's colour for the reason a core does: the icon is what marks it special,
            // not a bespoke colour id no theme defines.
            _boardModel.Occupy(position, colourId);

            // After the occupancy write, so the View is told the cell is filled before it is told what
            // the filled cell is.
            _boardModel.SetSpecialKind(position, SpecialCellKind.Vortex);
            PublishSpecialCellSpawned(SpecialCellKind.Vortex, position);
        }

        /// <summary>
        /// Spawns this placement's <see cref="SpecialCellKind.ChainLightning"/> reward, when it earned
        /// one: a placement of a 3x3 square or a 1x5 bar that also cleared at least one line always
        /// does, with no probability roll (see <see cref="ChainLightningSpawnSelector"/>). A placement
        /// that earned none, or one whose cleared lines hold no valid cell, is silently skipped — not an
        /// error state (AC4).
        /// <para>
        /// <paramref name="pieceId"/> is the piece this placement actually put down, threaded through
        /// rather than re-derived: the board cannot be asked afterwards which shape filled a line, and by
        /// now the line in question has been cleared away entirely.
        /// </para>
        /// <para>
        /// Reads <paramref name="clearResult"/>, the placement's own (primary) clear, rather than the
        /// whole cascade, exactly as the core's and the vortex's rules do: the reward is for the line the
        /// player lined up with that piece, not for one a special cell's effect went on to complete.
        /// </para>
        /// <para>
        /// Formation is currently switched off (issue #400): <see cref="ChainLightningFormationEnabled"/>
        /// gates this method before anything else runs, so a qualifying placement is reached and
        /// ignored. Everything below the gate is left intact for a possible revival.
        /// </para>
        /// </summary>
        private void TrySpawnChainLightning(LineClearResult clearResult, int colourId, string pieceId)
        {
            if (!ChainLightningFormationEnabled)
            {
                // Issue #400: formation paused. Checked first so no piece id, clear, or board state can
                // reach the spawn below.
                return;
            }

            GridPosition? spawn = ChainLightningSpawnSelector.SelectSpawnPosition(
                _boardModel.Board, clearResult.ClearedRows, clearResult.ClearedColumns, pieceId);
            if (spawn == null)
            {
                return;
            }

            GridPosition position = spawn.Value;

            // The cell was emptied by this very clear, so the tile needs a block to sit on. It borrows
            // the placed piece's colour for the reason a core and a vortex do: the icon is what marks it
            // special, not a bespoke colour id no theme defines.
            _boardModel.Occupy(position, colourId);

            // After the occupancy write, so the View is told the cell is filled before it is told what
            // the filled cell is.
            _boardModel.SetSpecialKind(position, SpecialCellKind.ChainLightning);
            PublishSpecialCellSpawned(SpecialCellKind.ChainLightning, position);
        }

        /// <summary>
        /// Spawns the finished dock cycle's <see cref="SpecialCellKind.ScoreGem"/> reward, when it
        /// earned one: a cycle in which every piece played cleared at least one line always does, with
        /// no probability roll (see <see cref="ScoreGemSpawnSelector"/>). A cycle that earned none, or
        /// one that ended with no block left on the board to convert, is silently skipped — not an
        /// error state.
        /// <para>
        /// Converted in place, exactly as a laser is: the block keeps its colour and its occupancy, and
        /// the icon is what marks it as special. Nothing is occupied here, so the placement that emptied
        /// the board to finish the round cannot have that achievement quietly taken back by its own
        /// reward.
        /// </para>
        /// </summary>
        /// <summary>
        /// Every placement that clears a row and a column at the same time — the same condition
        /// <see cref="TrySpawnExplosiveCore"/> reacts to — advances the cross-clear count by one; the
        /// count reaching two is what actually spawns the gem, and resets it back to zero for the next
        /// pair. Deliberately not gated by a streak: an ordinary placement in between two cross-clears
        /// advances neither count, so it cannot cost the player progress already earned.
        /// </summary>
        private void TrySpawnScoreGem(LineClearResult clearResult)
        {
            bool clearedBothAxes = clearResult.ClearedRows.Count > 0 && clearResult.ClearedColumns.Count > 0;
            if (!clearedBothAxes || !_scoreGemProgressModel.RecordCrossClear())
            {
                return;
            }

            GridPosition? spawn = ScoreGemSpawnSelector.SelectSpawnPosition(_boardModel.Board, _random);
            if (spawn == null)
            {
                return;
            }

            _boardModel.SetSpecialKind(spawn.Value, SpecialCellKind.ScoreGem);
            PublishSpecialCellSpawned(SpecialCellKind.ScoreGem, spawn.Value);
        }

        /// <summary>
        /// Spawns this placement's "Perfect Match" reward — a <see cref="SpecialCellKind.Vortex"/> at a
        /// uniformly random occupied cell — when it earned one (issue #352, kind changed by issue #397):
        /// <see cref="PerfectMatchQualifier"/> says the piece just placed is neither a single cell nor a
        /// straight line AND every cell it occupied was itself swept away by this placement's own
        /// (primary) clear. A vortex rather than an explosive core because the core is defined by the
        /// cross-clear that forms it (<see cref="TrySpawnExplosiveCore"/>), which this mechanic has
        /// nothing to do with. Same board mechanism the other spawns above use. A placement that did not
        /// qualify, or one with no eligible cell left once the spawns above have claimed theirs, is
        /// silently skipped — not an error state.
        /// <para>
        /// Reads <paramref name="clearResult"/>, the placement's own (primary) clear, exactly as the
        /// three spawns above do: the reward is for the piece the player placed and the lines it lined
        /// up, never for something a cascade phase went on to clear.
        /// </para>
        /// </summary>
        private void TrySpawnPerfectMatchBonus(Piece piece, GridPosition anchor, LineClearResult clearResult)
        {
            if (!PerfectMatchQualifier.Qualifies(piece, anchor, clearResult))
            {
                return;
            }

            GridPosition? spawn = PerfectMatchSpawnSelector.SelectSpawnPosition(_boardModel.Board, _random);
            if (spawn == null)
            {
                return;
            }

            // Chosen only from cells that are already occupied (see PerfectMatchSpawnSelector), so the
            // block to sit on already exists — unlike the core's own cross-clear reward, there is
            // nothing here to occupy first.
            _boardModel.SetSpecialKind(spawn.Value, SpecialCellKind.Vortex);
            PublishSpecialCellSpawned(SpecialCellKind.Vortex, spawn.Value);
        }

        /// <summary>
        /// Fires the piercing rocket just placed at <paramref name="origin"/>: its row and its column
        /// are emptied whole, and any special cell they took out fires in turn through the same effects
        /// a completed line's would.
        /// <para>
        /// The rocket's own cell is on both lines and goes with them — that is the self-destruct, and it
        /// needs no separate step.
        /// </para>
        /// </summary>
        private void ApplyPiercingRocket(GridPosition origin)
        {
            _piercingRocketEffect.Apply(_boardModel.Board, origin);

            IReadOnlyList<SpecialCellTrigger> triggers = _piercingRocketEffect.TriggeredSpecials;
            for (int i = 0; i < triggers.Count; i++)
            {
                // No kind filter: each installed effect's own guard is the filter, exactly as it is for
                // the cascade loop — and these effects are mid-resolution already (BeginResolution ran
                // just above), so whatever they blast is folded into the same placement's report.
                _specialCellEffects.Apply(_boardModel.Board, triggers[i]);
            }
        }

        /// <summary>
        /// Every diamond this placement's resolution destroyed, per gem colour, across every
        /// destruction path — or null when it destroyed none, so a placement that touched no diamond
        /// publishes exactly the null the message's older overloads always did. Returns a fresh copy
        /// when non-null; see <see cref="_destroyedDiamondCountByColourBuffer"/> for why.
        /// </summary>
        private int[] SumDestroyedDiamondsByColour()
        {
            Array.Clear(_destroyedDiamondCountByColourBuffer, 0, _destroyedDiamondCountByColourBuffer.Length);
            ColourTally.Add(_diamondClearEffect.DestroyedCountByColour, _destroyedDiamondCountByColourBuffer);
            ColourTally.Add(_laserEffect.DiamondsDestroyedCountByColour, _destroyedDiamondCountByColourBuffer);
            ColourTally.Add(_explosiveCoreEffect.DiamondsDestroyedCountByColour, _destroyedDiamondCountByColourBuffer);
            ColourTally.Add(_chainLightningEffect.DiamondsDestroyedCountByColour, _destroyedDiamondCountByColourBuffer);

            bool anyDestroyed = false;
            for (int colourId = 1; colourId < _destroyedDiamondCountByColourBuffer.Length; colourId++)
            {
                if (_destroyedDiamondCountByColourBuffer[colourId] > 0)
                {
                    anyDestroyed = true;
                    break;
                }
            }

            if (!anyDestroyed)
            {
                return null;
            }

            int[] copy = new int[_destroyedDiamondCountByColourBuffer.Length];
            Array.Copy(_destroyedDiamondCountByColourBuffer, copy, copy.Length);
            return copy;
        }

        /// <summary>
        /// Detonates the special cell a hammer's single destroyed cell was, if it was one. Its own
        /// resolution rather than a placement's: a hammer is not a placement, so nothing here feeds the
        /// line-clear report — the blasts and wipes are announced on their own, exactly as
        /// <see cref="PowerUpSystem"/> announces a power-up's.
        /// </summary>
        private void ApplyHammerTriggeredSpecials()
        {
            if (_hammerTriggerBuffer.Count == 0)
            {
                return;
            }

            _explosiveCoreEffect.BeginResolution();
            _laserEffect.BeginResolution();

            // And the vortex, for the same reason: a vortex tile a hammer destroys fills the board's
            // islands (or hands off) exactly as one destroyed by a completed line does. A fill is not a
            // destruction, so unlike the two above it can never chain into ending the run — it only adds
            // to what is already standing.
            _vortexEffect.BeginResolution();

            // The coin effect goes with them for the same reason they are here at all: a coin cell a
            // hammer destroys was destroyed, so it pays exactly as one taken out by a completed line
            // does. A hammer is a single cell and so never an intersection, which the effect reads off
            // the trigger's ClearAxis.None on its own.
            _coinEffect.BeginResolution();

            for (int i = 0; i < _hammerTriggerBuffer.Count; i++)
            {
                _explosiveCoreEffect.Apply(_boardModel.Board, _hammerTriggerBuffer[i]);
                _laserEffect.Apply(_boardModel.Board, _hammerTriggerBuffer[i]);
                _vortexEffect.Apply(_boardModel.Board, _hammerTriggerBuffer[i]);
                _coinEffect.Apply(_boardModel.Board, _hammerTriggerBuffer[i]);
            }

            // The vortex's fill never clears a line itself — it only fills island cells and relies on a
            // resolver re-checking fullness to actually clear whatever that completed (see
            // VortexEffect). A hammer's one-shot destruction has no resolver of its own, so this
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

            // Announced exactly as the placement path announces a core's bonus wipe (issue #398).
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

            // Announced exactly as the placement path announces its vortex work — its own seam, and a
            // copy of the buffers so a subscriber animating the fill is not reading the next
            // resolution's data halfway through.
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
                _vortexIslandFilledPublisher.Publish(new VortexIslandFilledMessage(
                    new List<GridPosition>(islandFilledCells), new List<GridPosition>(vortexHandOffTargets)));
            }

            // A hammered special's blast/wipe/strike can melt a socket too, and nothing above repaints
            // an empty cell's ice (issue #433).
            _boardModel.NotifyIceLevelsRefreshed();

            PublishCoinsAwarded();
        }

        /// <summary>
        /// Announces what the coin cells the current resolution destroyed are worth, when it destroyed
        /// any. Shared by the two resolutions this System runs — a placement's cascade and a hammer's
        /// single cell — so "a coin cell pays, whatever destroyed it" has one statement rather than two
        /// copies of it.
        /// <para>
        /// Silent on zero, which is the overwhelmingly common case: a resolution that destroyed no coin
        /// cell is not an event, so no subscriber ever has to handle a nil payout.
        /// </para>
        /// </summary>
        private void PublishCoinsAwarded()
        {
            int coinsAwarded = _coinEffect.TotalCoinsAwarded;
            if (coinsAwarded <= 0)
            {
                return;
            }

            _coinCellsClearedPublisher.Publish(new CoinCellsClearedMessage(coinsAwarded));
        }

        /// <summary>
        /// Overwrites the dock slots this refill owes a special piece with that piece, after the ordinary
        /// draw has filled all three. Overwriting rather than drawing differently is what satisfies AC3:
        /// only the triggered slots are replaced, and every other slot is still exactly what
        /// <see cref="WeightedPieceDraw"/> weighted it to be.
        /// <para>
        /// Slots are claimed from 0 upwards, so two rewards owed at the same refill never land on the
        /// same slot and at least one normally drawn piece always survives.
        /// </para>
        /// <para>
        /// Deliberately outside <see cref="WeightedPieceDraw.TryDrawSolvableSet"/>'s guarantee (AC4):
        /// that method is scoped to the Reroll power-up, and an injected piece is an override applied
        /// after the draw, so a dock containing one carries no solvability promise. The 1x1 every kind is
        /// offered on is in any case the piece most likely to fit.
        /// </para>
        /// </summary>
        private void ApplyPendingInjections()
        {
            int nextSlot = 0;

            if (_goldenInjectionPending)
            {
                InjectSpecialPiece(nextSlot, SpecialPieceKind.Golden);
                nextSlot++;
                _goldenInjectionPending = false;
            }

            if (_piercingRocketInjectionPending)
            {
                InjectSpecialPiece(nextSlot, SpecialPieceKind.PiercingRocket);
                _piercingRocketInjectionPending = false;
            }
        }

        /// <summary>Writes one special piece into a dock slot. Every kind is offered on the ordinary
        /// 1x1, so the tag is the whole difference — and the colour is drawn exactly as a normal piece's
        /// is, because colour is cosmetic and the icon is what marks the piece as special.</summary>
        private void InjectSpecialPiece(int slotIndex, SpecialPieceKind kind)
        {
            _trayModel.SetSlot(slotIndex, PieceCatalog.SingleCell, _pieceDraw.DrawColourId(), kind);

            if (_specialPieceSpawnedPublisher != null)
            {
                _specialPieceSpawnedPublisher.Publish(new SpecialPieceSpawnedMessage(kind, slotIndex));
            }
        }

        private void PublishSpecialCellSpawned(SpecialCellKind kind, GridPosition position)
        {
            if (_specialCellSpawnedPublisher != null)
            {
                _specialCellSpawnedPublisher.Publish(new SpecialCellSpawnedMessage(kind, position));
            }
        }

        /// <summary>
        /// Writes one ordinarily drawn piece into a dock slot, decorated with diamonds when
        /// <see cref="_diamondPieceDecorator"/> says so (issue #394). The one seam every ordinary deal —
        /// refill, reroll, rescue — goes through, so no draw path can miss the decoration roll; the
        /// special-piece injections deliberately do not, as a 1x1 is never decorated anyway.
        /// </summary>
        private void DealSlot(int slotIndex, Piece piece, int colourId)
        {
            if (_diamondPieceDecorator == null || piece == null)
            {
                _trayModel.SetSlot(slotIndex, piece, colourId);
                return;
            }

            EnsureCapacity(ref _diamondDecorationBuffer, piece.CellCount);
            bool decorated = _diamondPieceDecorator.TryDecorate(piece, _diamondDecorationBuffer);
            _trayModel.SetSlot(
                slotIndex, piece, colourId, SpecialPieceKind.None, decorated ? _diamondDecorationBuffer : null);
        }

        private static void EnsureCapacity(ref int[] buffer, int length)
        {
            if (buffer.Length < length)
            {
                buffer = new int[length];
            }
        }

        private static void CopyInto(IReadOnlyList<int> source, int[] destination)
        {
            Array.Clear(destination, 0, destination.Length);
            int count = source.Count < destination.Length ? source.Count : destination.Length;
            for (int index = 0; index < count; index++)
            {
                destination[index] = source[index];
            }
        }

        private void RefillTray()
        {
            for (int i = 0; i < TrayModel.SLOT_COUNT; i++)
            {
                DealSlot(i, _pieceDraw.DrawPiece(), _pieceDraw.DrawColourId());
            }

            // After the ordinary draw, never instead of it: the injection overrides the slots it claims
            // and leaves the rest weighted exactly as they were drawn (AC3).
            ApplyPendingInjections();

            // Published from here rather than from the two call sites, so the opening draw of a run
            // and every mid-run refill are indistinguishable to subscribers.
            _trayRefilledPublisher.Publish(new TrayRefilledMessage());
        }

        private void CheckGameOver()
        {
            // A run that is already over stays over, for the reason it was ended with. Load-bearing
            // only for Path mode, where a level completing mid-placement ends the run through
            // ForceGameOver from inside the PiecePlacedMessage publish — the tail of TryPlacePiece then
            // still reaches here, and without this guard a board that also happens to have no legal
            // move left would publish a second, contradicting GameOverMessage over the success one.
            // Inert for Endless and Timed: nothing ends a run of theirs part-way through a placement.
            if (IsGameOver)
            {
                return;
            }

            // An unused hammer is a move, and one no board shape can refuse: it destroys an occupied
            // cell rather than fitting into an empty region, so MoveAvailability — which only ever asks
            // "does this shape fit" — would answer for its 1x1 and get the wrong question. Checked
            // before that call rather than folded into it, so the run cannot be declared over on the
            // very next check while the life-line it was just handed is still sitting in the dock.
            if (_trayModel.HasSpecialPiece(SpecialPieceKind.DemolitionHammer))
            {
                return;
            }

            _trayModel.CollectRemaining(_remainingBuffer);

            // The parked piece counts as a move the player still has only while they can get it back.
            // The swap that returns it costs a Hold charge (PowerUpSystem.TryApplyHold), so behind an
            // empty inventory the piece is stuck and a board only it fits is a dead end after all —
            // counting it there would leave the run alive with nothing the player can actually do.
            // With a charge in hand the old reading holds: pocketing the one piece that fits must not
            // end a run the player could still play on from.
            if (_trayModel.HeldPiece != null && CanRetrieveHeldPiece())
            {
                _remainingBuffer.Add(_trayModel.HeldPiece);
            }

            if (MoveAvailability.HasAnyMove(_boardModel.Board, _remainingBuffer))
            {
                return;
            }

            // The last thing tried before the run ends: a board this full with nothing left to play has
            // earned the life-line, and handing it over here — rather than at some future refill, of
            // which there may be none — is the only moment it can still save the run.
            if (TryInjectDemolitionHammer())
            {
                return;
            }

            // Only once the hammer has failed — spent already, or the board not full enough to earn it —
            // is the ad-gated rescue put on the table (issue #370), and only from this one NoMovesLeft
            // path: it answers "no piece fits", which no other ending is about. Unconditional on mode,
            // and unconditional on how many times this run has already been rescued. Without a source
            // to pay for it there is no offer to make, and the run ends as it always did.
            EndRun(GameOverReason.NoMovesLeft, isRescueAvailable: _rescueRewardSource != null);
        }

        /// <summary>Whether the swap that brings the parked piece back can be paid for. Without an
        /// inventory to read (see <see cref="_powerUpModel"/>) the answer is the pre-charge one: yes.</summary>
        private bool CanRetrieveHeldPiece()
            => _powerUpModel == null || _powerUpModel.HoldCount.Value > 0;

        /// <summary>
        /// Injects a <see cref="SpecialPieceKind.DemolitionHammer"/> into the dock when the board is at
        /// least <see cref="HAMMER_OCCUPANCY_THRESHOLD"/> full. Called only from
        /// <see cref="CheckGameOver"/>, with "no legal move left" already established, so the two
        /// conditions the issue names are both in hand. Returns whether the run was saved.
        /// <para>
        /// "No hammer already in the dock" needs no check of its own: the caller returns early on
        /// exactly that condition, so reaching here means there is none — which is what makes the
        /// injection happen once per trigger rather than once per check.
        /// </para>
        /// <para>
        /// It takes an empty slot when there is one and slot 0 otherwise. Overwriting costs the player
        /// nothing: every piece in the dock was just found to be unplaceable, so the one discarded was a
        /// piece they could not have used.
        /// </para>
        /// </summary>
        private bool TryInjectDemolitionHammer()
        {
            if (_hammerGrantedThisRun)
            {
                return false;
            }

            // Measured against the cells a block could actually stand on, so a shape's holes do not
            // make a board look permanently un-crowded. Identical to SIZE * SIZE on the standard board.
            float occupancy =
                _boardModel.Board.OccupiedCellCount() / (float)_boardModel.Board.PlayableCellCount;
            if (occupancy < HAMMER_OCCUPANCY_THRESHOLD)
            {
                return false;
            }

            int emptySlot = _trayModel.FindFirstEmptySlot();
            InjectSpecialPiece(emptySlot >= 0 ? emptySlot : 0, SpecialPieceKind.DemolitionHammer);
            _hammerGrantedThisRun = true;
            return true;
        }
    }
}
