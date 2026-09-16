using System;
using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
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

        private readonly BoardModel _boardModel;
        private readonly TrayModel _trayModel;
        private readonly PerfectRoundModel _perfectRoundModel;
        private readonly WeightedPieceDraw _pieceDraw;
        private readonly PlacementSnapper _placementSnapper = new PlacementSnapper();
        private readonly IPublisher<RunStartedMessage> _runStartedPublisher;
        private readonly IPublisher<PiecePlacedMessage> _piecePlacedPublisher;
        private readonly IPublisher<LinesClearedMessage> _linesClearedPublisher;
        private readonly IPublisher<GameOverMessage> _gameOverPublisher;
        private readonly IPublisher<TrayRefilledMessage> _trayRefilledPublisher;
        private readonly IPublisher<ExplosiveCoreDetonatedMessage> _explosiveCoreDetonatedPublisher;
        private readonly IPublisher<LaserFiredMessage> _laserFiredPublisher;
        private readonly IPublisher<PiercingRocketFiredMessage> _piercingRocketFiredPublisher;
        private readonly IPublisher<VortexPulledMessage> _vortexPulledPublisher;
        private readonly IPublisher<ChainLightningTriggeredMessage> _chainLightningTriggeredPublisher;

        // One long-lived effect per kind, reset per placement rather than reallocated — each owns the
        // buffer its destroyed cells are reported through.
        private readonly ExplosiveCoreEffect _explosiveCoreEffect = new ExplosiveCoreEffect();
        private readonly LaserEffect _laserEffect = new LaserEffect();

        /// <summary>The odd one out among the board-mutating effects: it moves blocks instead of
        /// destroying them, so what it reports is a list of moves rather than a count of emptied
        /// cells.</summary>
        private readonly VortexEffect _vortexEffect = new VortexEffect();

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

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public BoardSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            PerfectRoundModel perfectRoundModel,
            WeightedPieceDraw pieceDraw,
            IPublisher<RunStartedMessage> runStartedPublisher,
            IPublisher<PiecePlacedMessage> piecePlacedPublisher,
            IPublisher<LinesClearedMessage> linesClearedPublisher,
            IPublisher<GameOverMessage> gameOverPublisher,
            IPublisher<TrayRefilledMessage> trayRefilledPublisher,
            IPublisher<ExplosiveCoreDetonatedMessage> explosiveCoreDetonatedPublisher,
            IPublisher<LaserFiredMessage> laserFiredPublisher,
            IPublisher<PiercingRocketFiredMessage> piercingRocketFiredPublisher,
            IPublisher<VortexPulledMessage> vortexPulledPublisher,
            IPublisher<ChainLightningTriggeredMessage> chainLightningTriggeredPublisher)
            : this(
                boardModel, trayModel, perfectRoundModel, pieceDraw, runStartedPublisher,
                piecePlacedPublisher, linesClearedPublisher, gameOverPublisher, trayRefilledPublisher,
                explosiveCoreDetonatedPublisher, laserFiredPublisher, piercingRocketFiredPublisher,
                vortexPulledPublisher, chainLightningTriggeredPublisher, Environment.TickCount)
        {
        }

        internal BoardSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            PerfectRoundModel perfectRoundModel,
            WeightedPieceDraw pieceDraw,
            IPublisher<RunStartedMessage> runStartedPublisher,
            IPublisher<PiecePlacedMessage> piecePlacedPublisher,
            IPublisher<LinesClearedMessage> linesClearedPublisher,
            IPublisher<GameOverMessage> gameOverPublisher,
            IPublisher<TrayRefilledMessage> trayRefilledPublisher,
            IPublisher<ExplosiveCoreDetonatedMessage> explosiveCoreDetonatedPublisher,
            IPublisher<LaserFiredMessage> laserFiredPublisher,
            IPublisher<PiercingRocketFiredMessage> piercingRocketFiredPublisher,
            IPublisher<VortexPulledMessage> vortexPulledPublisher,
            IPublisher<ChainLightningTriggeredMessage> chainLightningTriggeredPublisher,
            int seed)
        {
            _random = new Random(seed);

            // After the stream it draws from, necessarily: the effect keeps the reference it is handed,
            // and there is exactly one stream per run for every random decision to come out of.
            _chainLightningEffect = new ChainLightningEffect(_random);

            // Built from the live board's own outline, not from a default square: a scratch board that
            // disagreed with the real one about geometry could not be copied onto at all.
            _previewScratchBoard = new Board(boardModel.Shape);
            _explosiveCoreDetonatedPublisher = explosiveCoreDetonatedPublisher;
            _laserFiredPublisher = laserFiredPublisher;
            _piercingRocketFiredPublisher = piercingRocketFiredPublisher;
            _vortexPulledPublisher = vortexPulledPublisher;
            _chainLightningTriggeredPublisher = chainLightningTriggeredPublisher;
            _specialCellEffects = new CompositeSpecialCellEffect(
                _explosiveCoreEffect, _laserEffect, _scoreGemEffect, _vortexEffect, _chainLightningEffect);
            _boardModel = boardModel;
            _trayModel = trayModel;
            _perfectRoundModel = perfectRoundModel;
            _pieceDraw = pieceDraw;
            _runStartedPublisher = runStartedPublisher;
            _piecePlacedPublisher = piecePlacedPublisher;
            _linesClearedPublisher = linesClearedPublisher;
            _gameOverPublisher = gameOverPublisher;
            _trayRefilledPublisher = trayRefilledPublisher;
        }

        public bool IsGameOver { get; private set; }

        void IStartable.Start() => StartNewRun();

        public void StartNewRun()
        {
            _boardModel.ClearAll();

            // Dropped before the refill below, which is the thing that would otherwise pay them: a
            // special piece is earned by the run that triggered it and must never be handed to the next
            // one (AC2).
            _goldenInjectionPending = false;
            _piercingRocketInjectionPending = false;
            _hammerGrantedThisRun = false;

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

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                _boardModel.Occupy(anchor + piece.Offsets[i], colourId);
            }

            _trayModel.ConsumeSlot(slotIndex);

            // Read before the resolver mutates the board — once a line clears, its cells are gone and
            // "how full was the board under this placement" can no longer be answered.
            int occupiedCellCountBeforeClear = _boardModel.Board.OccupiedCellCount();

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

            // A blast clears a region, not lines, so it is announced the way a power-up's region clear
            // is rather than through the line-clear path — and before the messages below, so the
            // board state they report already includes what the blast removed.
            IReadOnlyList<GridPosition> blastedCells = _explosiveCoreEffect.BlastedCells;
            bool anyBlasted = blastedCells.Count > 0;
            if (anyBlasted)
            {
                _boardModel.NotifyPowerUpCleared(blastedCells);
            }

            // A laser's wipe is announced the same way and for the same reason: it empties a line
            // whether or not that line was full, so no LinesClearedMessage describes it.
            IReadOnlyList<GridPosition> wipedCells = _laserEffect.WipedCells;
            bool anyWiped = wipedCells.Count > 0;
            if (anyWiped)
            {
                _boardModel.NotifyPowerUpCleared(wipedCells);
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

            // A vortex's pulls are announced through their own seam rather than the cleared-cells one:
            // nothing was destroyed, so there is no cell to fade — each move empties one cell and fills
            // another, and both ends have to reach the View together or a block would appear to
            // duplicate itself.
            IReadOnlyList<VortexPull> pulls = _vortexEffect.Pulls;
            bool anyPulled = pulls.Count > 0;
            if (anyPulled)
            {
                _boardModel.NotifyPulled(pulls);
            }

            bool anyCornerCleared = AnyCornerTouched(
                _boardModel.Board, clearResult.ClearedRows, clearResult.ClearedColumns);

            _piecePlacedPublisher.Publish(new PiecePlacedMessage(
                piece.Id, anchor, PieceFamilyClassifier.Classify(piece.Id), piece.CellCount, colourId,
                clearResult.LineCount, clearResult.ClearedRows.Count, clearResult.ClearedColumns.Count,
                clearResult.MonochromeLineCount, _boardModel.Board.IsEmpty(), occupiedCellCountBeforeClear,
                anyCornerCleared, _boardModel.Board.IsCenterCoreEmpty(), _boardModel.Board.HasIsolatedEmptyCells(),
                _scoreGemEffect.DestroyedCount));

            if (clearResult.AnyCleared)
            {
                _linesClearedPublisher.Publish(new LinesClearedMessage(
                    clearResult.ClearedRows, clearResult.ClearedColumns, clearResult.ClearedCellCount));
            }

            // Published after the two messages above so a subscriber that reacts to a blast sees a
            // placement that has already been fully reported, and so the View's line-clear animation
            // claims its own cells before the blast sweep does.
            if (anyBlasted)
            {
                _explosiveCoreDetonatedPublisher.Publish(
                    new ExplosiveCoreDetonatedMessage(blastedCells.Count));
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

            if (anyPulled)
            {
                // Copied, unlike every count above: the effect's list is a buffer it overwrites on the
                // next placement, and a subscriber animating the slide over several frames would
                // otherwise be reading next move's data halfway through. One small list per placement
                // that pulled something — never per frame.
                _vortexPulledPublisher.Publish(new VortexPulledMessage(new List<VortexPull>(pulls)));
            }

            // Last of all, and deliberately after PiecePlacedMessage: the spawn occupies a cell, and
            // that message reports whether this placement emptied the board, cleared the centre core
            // and so on. Spawning first would quietly cost the player every perfect-clear reward they
            // just earned. It also reads the board as the whole cascade left it, not mid-cascade: what
            // matters is whether the intersection is free once everything has settled.
            TrySpawnExplosiveCore(clearResult, colourId);

            // Same section, same reasons, and deliberately after the core: the two rewards can be
            // earned by one placement, and the core picks its cell first so a vortex can never take the
            // intersection the core's rule is defined on. The occupancy handed over is the pre-clear
            // one already read above, not a second reading — see TrySpawnVortex.
            TrySpawnVortex(clearResult, colourId, occupiedCellCountBeforeClear);

            // Same section, same reasons, and last of the three: one placement can earn more than one
            // reward, and each selector skips a cell that already carries a kind, so spawning in a fixed
            // order is what keeps two rewards off the same cell. The shape that earned this one is read
            // from the piece itself — the only spawn rule in the game that depends on what was placed
            // rather than only on what cleared.
            TrySpawnChainLightning(clearResult, colourId, piece.Id);

            // Armed in the same section and for the same reason as the spawn above: it is this
            // placement's reward, read off the placement's own (primary) clear rather than off the whole
            // cascade, because the reward is for the lines the player lined up. Unlike a core it is not
            // put on the board — it is owed to the next refill, which is where a dock injection can
            // happen at all.
            if (clearResult.LineCount >= ROCKET_TRIGGER_LINE_COUNT)
            {
                _piercingRocketInjectionPending = true;
            }

            // Recorded after the placement has been fully reported, for the same reason the spawn above
            // is: this is bookkeeping about the round, and nothing that reads the placement should see
            // it half-updated.
            _perfectRoundModel.RecordPlacement(clearResult.AnyCleared);

            if (_trayModel.IsEmpty)
            {
                // Before the refill, which is what resets the round: this placement emptied the dock, so
                // the cycle ends here and its reward — if it earned one — is owed now.
                TrySpawnScoreGem();
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
        public bool TryHoldPiece(int slotIndex)
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

            // The kind travels with the piece in both directions. A golden 1x1 set aside and taken back
            // out is the same golden 1x1 — dropping the tag on the way through the pocket would quietly
            // demote a piece the player earned.
            _trayModel.SetHeld(piece, _trayModel.GetColourId(slotIndex), _trayModel.GetSpecialKind(slotIndex));
            _trayModel.SetSlot(slotIndex, previouslyHeld, previouslyHeldColourId, previouslyHeldKind);

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

            IsGameOver = true;
            _gameOverPublisher.Publish(new GameOverMessage(reason));
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
                _trayModel.SetSlot(slotIndex, _rerollPieceBuffer[slotIndex], _rerollColourBuffer[slotIndex]);
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
            _hammerTriggerBuffer.Clear();
            SpecialCellDetection.CollectTriggered(
                _boardModel.Board, _hammerClearedBuffer, _hammerTriggerBuffer);

            _boardModel.Board.Clear(target);
            _boardModel.NotifyPowerUpCleared(_hammerClearedBuffer);

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
        }

        /// <summary>
        /// Spawns this placement's <see cref="SpecialCellKind.Vortex"/> reward, when it earned one: a
        /// placement that cleared at least one line on a board that was more than
        /// <see cref="VortexSpawnSelector.OCCUPANCY_THRESHOLD"/>-full always does, with no probability
        /// roll (see <see cref="VortexSpawnSelector"/>). A placement that earned none, or one whose
        /// cleared lines hold no valid cell, is silently skipped — not an error state.
        /// <para>
        /// <paramref name="occupiedCellCountBeforeClear"/> is the reading taken in
        /// <see cref="TryPlacePiece"/> the moment the piece landed, and is threaded through rather than
        /// re-measured here: by now the clear has already emptied the very cells that made the board
        /// crowded, so a second reading would answer a different question and the reward would never
        /// fire. It is the same value <see cref="PiecePlacedMessage"/> reports, so the vortex and the
        /// clutch-recovery objective can never disagree about how full the board was.
        /// </para>
        /// <para>
        /// Reads <paramref name="clearResult"/>, the placement's own (primary) clear, rather than the
        /// whole cascade, exactly as the explosive core's rule does: the reward is for the line the
        /// player lined up.
        /// </para>
        /// </summary>
        private void TrySpawnVortex(LineClearResult clearResult, int colourId, int occupiedCellCountBeforeClear)
        {
            GridPosition? spawn = VortexSpawnSelector.SelectSpawnPosition(
                _boardModel.Board, clearResult.ClearedRows, clearResult.ClearedColumns,
                occupiedCellCountBeforeClear);
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
        /// </summary>
        private void TrySpawnChainLightning(LineClearResult clearResult, int colourId, string pieceId)
        {
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
        private void TrySpawnScoreGem()
        {
            if (!_perfectRoundModel.IsPerfectRound)
            {
                return;
            }

            GridPosition? spawn = ScoreGemSpawnSelector.SelectSpawnPosition(_boardModel.Board, _random);
            if (spawn == null)
            {
                return;
            }

            _boardModel.SetSpecialKind(spawn.Value, SpecialCellKind.ScoreGem);
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

            for (int i = 0; i < _hammerTriggerBuffer.Count; i++)
            {
                _explosiveCoreEffect.Apply(_boardModel.Board, _hammerTriggerBuffer[i]);
                _laserEffect.Apply(_boardModel.Board, _hammerTriggerBuffer[i]);
            }

            IReadOnlyList<GridPosition> blastedCells = _explosiveCoreEffect.BlastedCells;
            if (blastedCells.Count > 0)
            {
                _boardModel.NotifyPowerUpCleared(blastedCells);
                _explosiveCoreDetonatedPublisher.Publish(
                    new ExplosiveCoreDetonatedMessage(blastedCells.Count));
            }

            IReadOnlyList<GridPosition> wipedCells = _laserEffect.WipedCells;
            if (wipedCells.Count > 0)
            {
                _boardModel.NotifyPowerUpCleared(wipedCells);
                _laserFiredPublisher.Publish(new LaserFiredMessage(wipedCells.Count));
            }
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
        }

        private void RefillTray()
        {
            for (int i = 0; i < TrayModel.SLOT_COUNT; i++)
            {
                _trayModel.SetSlot(i, _pieceDraw.DrawPiece(), _pieceDraw.DrawColourId());
            }

            // After the ordinary draw, never instead of it: the injection overrides the slots it claims
            // and leaves the rest weighted exactly as they were drawn (AC3).
            ApplyPendingInjections();

            // Every refill starts a fresh round, whether or not the one that just ended earned anything
            // — and a run start goes through here too (StartNewRun refills), so no round state can
            // survive into the next run.
            _perfectRoundModel.ResetCycle();

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

            // The parked piece counts as a move the player still has. Swapping it back into a dock slot
            // is always legal and costs nothing, so a board where only the parked piece fits is not a
            // dead end — without this, pocketing the one piece that fits would end a run the player
            // could still play on from.
            if (_trayModel.HeldPiece != null)
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

            IsGameOver = true;
            _gameOverPublisher.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));
        }

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
