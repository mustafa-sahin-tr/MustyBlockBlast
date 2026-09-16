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

        private readonly BoardModel _boardModel;
        private readonly TrayModel _trayModel;
        private readonly WeightedPieceDraw _pieceDraw;
        private readonly PlacementSnapper _placementSnapper = new PlacementSnapper();
        private readonly IPublisher<RunStartedMessage> _runStartedPublisher;
        private readonly IPublisher<PiecePlacedMessage> _piecePlacedPublisher;
        private readonly IPublisher<LinesClearedMessage> _linesClearedPublisher;
        private readonly IPublisher<GameOverMessage> _gameOverPublisher;
        private readonly IPublisher<TrayRefilledMessage> _trayRefilledPublisher;
        private readonly IPublisher<ExplosiveCoreDetonatedMessage> _explosiveCoreDetonatedPublisher;
        private readonly IPublisher<LaserFiredMessage> _laserFiredPublisher;

        // One long-lived effect per kind, reset per placement rather than reallocated — each owns the
        // buffer its destroyed cells are reported through.
        private readonly ExplosiveCoreEffect _explosiveCoreEffect = new ExplosiveCoreEffect();
        private readonly LaserEffect _laserEffect = new LaserEffect();

        /// <summary>Every installed effect, presented to the cascade loop as the one effect it takes.
        /// Each effect ignores a trigger of a kind that is not its own, so a trigger reaching both of
        /// them is exactly equivalent to dispatching on the kind.</summary>
        private readonly ISpecialCellEffect _specialCellEffects;

        /// <summary>Only ever used to break a tie between equally valid explosive-core spawn cells,
        /// which cannot arise on the current board shape (see
        /// <see cref="ExplosiveCoreSpawnSelector.SelectSpawnPosition"/>). Whether a qualifying placement
        /// spawns one at all is fully deterministic and never touches this.</summary>
        private readonly Random _random;
        // Sized for the three dock slots plus the parked piece, which CheckGameOver appends.
        private readonly List<Piece> _remainingBuffer = new List<Piece>(TrayModel.SLOT_COUNT + 1);
        private readonly Board _previewScratchBoard = new Board();
        // Reroll draws a whole set at once and only then writes it to the tray, so a draw that has to
        // be retried never touches a slot. Owned here and reused, so a reroll allocates nothing.
        private readonly Piece[] _rerollPieceBuffer = new Piece[TrayModel.SLOT_COUNT];
        private readonly int[] _rerollColourBuffer = new int[TrayModel.SLOT_COUNT];
        private readonly List<int> _previewRowsBuffer = new List<int>(Board.SIZE);
        private readonly List<int> _previewColumnsBuffer = new List<int>(Board.SIZE);

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public BoardSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            WeightedPieceDraw pieceDraw,
            IPublisher<RunStartedMessage> runStartedPublisher,
            IPublisher<PiecePlacedMessage> piecePlacedPublisher,
            IPublisher<LinesClearedMessage> linesClearedPublisher,
            IPublisher<GameOverMessage> gameOverPublisher,
            IPublisher<TrayRefilledMessage> trayRefilledPublisher,
            IPublisher<ExplosiveCoreDetonatedMessage> explosiveCoreDetonatedPublisher,
            IPublisher<LaserFiredMessage> laserFiredPublisher)
            : this(
                boardModel, trayModel, pieceDraw, runStartedPublisher, piecePlacedPublisher,
                linesClearedPublisher, gameOverPublisher, trayRefilledPublisher,
                explosiveCoreDetonatedPublisher, laserFiredPublisher, Environment.TickCount)
        {
        }

        internal BoardSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            WeightedPieceDraw pieceDraw,
            IPublisher<RunStartedMessage> runStartedPublisher,
            IPublisher<PiecePlacedMessage> piecePlacedPublisher,
            IPublisher<LinesClearedMessage> linesClearedPublisher,
            IPublisher<GameOverMessage> gameOverPublisher,
            IPublisher<TrayRefilledMessage> trayRefilledPublisher,
            IPublisher<ExplosiveCoreDetonatedMessage> explosiveCoreDetonatedPublisher,
            IPublisher<LaserFiredMessage> laserFiredPublisher,
            int seed)
        {
            _random = new Random(seed);
            _explosiveCoreDetonatedPublisher = explosiveCoreDetonatedPublisher;
            _laserFiredPublisher = laserFiredPublisher;
            _specialCellEffects = new CompositeSpecialCellEffect(_explosiveCoreEffect, _laserEffect);
            _boardModel = boardModel;
            _trayModel = trayModel;
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

            bool anyCornerCleared = AnyCornerTouched(clearResult.ClearedRows, clearResult.ClearedColumns);

            _piecePlacedPublisher.Publish(new PiecePlacedMessage(
                piece.Id, anchor, PieceFamilyClassifier.Classify(piece.Id), piece.CellCount, colourId,
                clearResult.LineCount, clearResult.ClearedRows.Count, clearResult.ClearedColumns.Count,
                clearResult.MonochromeLineCount, _boardModel.Board.IsEmpty(), occupiedCellCountBeforeClear,
                anyCornerCleared, _boardModel.Board.IsCenterCoreEmpty(), _boardModel.Board.HasIsolatedEmptyCells()));

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

            // Last of all, and deliberately after PiecePlacedMessage: the spawn occupies a cell, and
            // that message reports whether this placement emptied the board, cleared the centre core
            // and so on. Spawning first would quietly cost the player every perfect-clear reward they
            // just earned. It also reads the board as the whole cascade left it, not mid-cascade: what
            // matters is whether the intersection is free once everything has settled.
            TrySpawnExplosiveCore(clearResult, colourId);

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

            if (!_trayModel.IsHoldOccupied && _trayModel.OccupiedSlotCount == 1)
            {
                return false;
            }

            Piece previouslyHeld = _trayModel.HeldPiece;
            int previouslyHeldColourId = _trayModel.HeldColourId;

            _trayModel.SetHeld(piece, _trayModel.GetColourId(slotIndex));
            _trayModel.SetSlot(slotIndex, previouslyHeld, previouslyHeldColourId);

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
        private static bool AnyCornerTouched(IReadOnlyList<int> clearedRows, IReadOnlyList<int> clearedColumns)
        {
            return ContainsEdgeIndex(clearedRows) || ContainsEdgeIndex(clearedColumns);
        }

        private static bool ContainsEdgeIndex(IReadOnlyList<int> indices)
        {
            for (int indexPosition = 0; indexPosition < indices.Count; indexPosition++)
            {
                if (indices[indexPosition] == 0 || indices[indexPosition] == Board.SIZE - 1)
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

        private void RefillTray()
        {
            for (int i = 0; i < TrayModel.SLOT_COUNT; i++)
            {
                _trayModel.SetSlot(i, _pieceDraw.DrawPiece(), _pieceDraw.DrawColourId());
            }

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

            IsGameOver = true;
            _gameOverPublisher.Publish(new GameOverMessage(GameOverReason.NoMovesLeft));
        }
    }
}
