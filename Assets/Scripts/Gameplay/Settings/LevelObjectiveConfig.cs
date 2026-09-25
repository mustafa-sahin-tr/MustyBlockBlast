using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One authored level: the objective the player must meet to clear it. Inspector-editable rather
    /// than hardcoded, so the goal of any level can be retuned or replaced as an asset edit — see
    /// <see cref="LevelCatalog"/>, which owns the ordered list of these.
    /// <para>
    /// Mirrors <see cref="ObjectiveDefinition"/>'s shape but in Unity serialization terms; the
    /// immutable Core definition is built on demand by <see cref="ToObjectiveDefinition"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class LevelObjectiveConfig
    {
        /// <summary>Prefix for the generated objective id, e.g. <c>level_7</c>. Stable across edits of
        /// everything except the level number, which is what the save data keys cumulative progress on.</summary>
        private const string OBJECTIVE_ID_PREFIX = "level_";

        /// <summary>Separator between the level number and the objective's index within that level,
        /// e.g. <c>level_7_1</c> for a level's second objective. Never appended to the first one.</summary>
        private const string OBJECTIVE_ID_INDEX_SEPARATOR = "_";

        /// <summary>Shared, never-mutated empty for a row whose list field is null — which
        /// <see cref="JsonUtility"/> can produce for a row authored before the field existed.</summary>
        private static readonly ReinforcedCellAuthoring[] EmptyReinforcedCells =
            new ReinforcedCellAuthoring[0];

        /// <summary>Shared, never-mutated empty for a row whose <see cref="_timerCells"/> field is null
        /// — the same never-existed-yet case <see cref="EmptyReinforcedCells"/> covers.</summary>
        private static readonly TimerCellAuthoring[] EmptyTimerCells = new TimerCellAuthoring[0];

        /// <summary>Shared, never-mutated empty for a row whose <see cref="_targetIceCells"/> field is
        /// null — the same never-existed-yet case <see cref="EmptyReinforcedCells"/> covers.</summary>
        private static readonly TargetIceCellAuthoring[] EmptyTargetIceCells = new TargetIceCellAuthoring[0];

        /// <summary>Shared, never-mutated empty for a row whose <see cref="_lockedCells"/> field is
        /// null — the same never-existed-yet case <see cref="EmptyReinforcedCells"/> covers.</summary>
        private static readonly LockedCellAuthoring[] EmptyLockedCells = new LockedCellAuthoring[0];

        /// <summary>Shared, never-mutated empty for a row whose <see cref="_powerStarCells"/> field is
        /// null — the same never-existed-yet case <see cref="EmptyReinforcedCells"/> covers.</summary>
        private static readonly PowerStarCellAuthoring[] EmptyPowerStarCells = new PowerStarCellAuthoring[0];

        /// <summary>Shared, never-mutated empty for a row whose <see cref="_puzzleLinkGroups"/> field is
        /// null — the same never-existed-yet case <see cref="EmptyReinforcedCells"/> covers.</summary>
        private static readonly PuzzleLinkGroupAuthoring[] EmptyPuzzleLinkGroups = new PuzzleLinkGroupAuthoring[0];

        /// <summary>Shared, never-mutated empty for a row whose <see cref="_bannedPowerUps"/> field is
        /// null — the same never-existed-yet case <see cref="EmptyReinforcedCells"/> covers.</summary>
        private static readonly PowerUpKind[] EmptyBannedPowerUps = new PowerUpKind[0];

        /// <summary>
        /// Share of eligible (multi-cell) draws a diamond level decorates when it authors nothing else:
        /// the "~20%" of #390, and exactly the rate every diamond level dealt at before
        /// <see cref="_diamondDecorationChance"/> existed. Also what <c>DiamondPieceDecorator</c> falls
        /// back to when no level row is in play at all.
        /// </summary>
        internal const float DEFAULT_DIAMOND_DECORATION_CHANCE = 0.2f;

        /// <summary>Fewest diamonds a decorated piece carries by default: one.</summary>
        internal const int DEFAULT_DIAMOND_MIN_DECORATED_CELLS = 1;

        /// <summary>
        /// The <see cref="_diamondMaxDecoratedCells"/> value that means "no authored cap": a decorated
        /// piece may carry up to every cell but one, which is the range #390 AC1 fixes and the one every
        /// diamond level dealt with before the field existed.
        /// </summary>
        internal const int DIAMOND_MAX_DECORATED_CELLS_UNCAPPED = 0;

        [Tooltip("1-based level number. This is the identity of the level, not its position in the list.")]
        [SerializeField] private int _levelNumber = 1;

        [Tooltip("What the objective measures.")]
        [SerializeField] private ObjectiveType _objectiveType = ObjectiveType.SimultaneousLineClear;

        [Tooltip("PerRun resets on game over; Cumulative survives runs and app restarts.")]
        [SerializeField] private ObjectiveScope _scope = ObjectiveScope.PerRun;

        [Tooltip("Value progress must reach to clear the level. Must be greater than zero.")]
        [SerializeField] private int _targetValue = 1;

        [Tooltip("Line count a placement must clear. Exact match for SimultaneousLineClear, minimum " +
            "(\"at least\") for AtLeastLineClear. Unused otherwise.")]
        [SerializeField] private int _requiredLineCount = 2;

        [Tooltip("Shape family a placed piece must belong to. Used by PieceFamilyCount only.")]
        [SerializeField] private PieceFamily _requiredPieceFamily = PieceFamily.Single;

        [Tooltip("Minimum board occupancy (cells filled) a qualifying clear must meet. Used by " +
            "ClutchRecoveryClear only.")]
        [SerializeField] private int _requiredOccupancyThreshold = 52;

        [Tooltip("Exact catalog piece id (e.g. \"square_3x3\", \"line_h5\") a placement must use. " +
            "Used by PieceIdCount and PieceIdLineClear only.")]
        [SerializeField] private string _requiredPieceId = "square_3x3";

        [Tooltip("Colour id (1..5) a ColourCleared objective counts cells of, or a DiamondsCleared objective "
            + "counts diamonds of. The id is theme-agnostic: the swatch the player sees comes from the active "
            + "theme at runtime. Unused otherwise.")]
        [SerializeField] private int _requiredColourId = 1;

        // The authored per-level power-up reward (_grantsLevelUpReward / _levelUpReward) was removed
        // in issue #462: every level now rewards by a rule in code — see LevelCompletionRewards. The
        // catalog asset may still carry the two orphaned YAML keys until it is next re-saved; Unity
        // ignores keys with no matching field, so they are harmless.

        [Tooltip("Score paid into the run's own score when this level is completed in Path mode. " +
            "0 (the default) means the level pays nothing, which is what every level authored before " +
            "this field existed does. Ignored in Endless and Timed, where completing a level does not " +
            "end a run.")]
        [SerializeField] private int _completionScoreBonus;

        [Tooltip("Coin cells this level seeds onto its board. 0 (the default) means the level seeds " +
            "none, which is what every level authored before this field existed does.")]
        [SerializeField] private int _coinCellCount;

        [Tooltip("Width of the rolling window in seconds for RollingLineClearWindow, or the deadline in " +
            "seconds from run start for EarlyScoreRush. Unused otherwise.")]
        [SerializeField] private float _windowSeconds = 15f;

        [Header("Board shape")]
        [Tooltip("Columns on this level's board. 8 (the default) is the standard square board every " +
            "level authored before board shapes existed uses.")]
        [SerializeField] private int _boardWidth = Board.SIZE;

        [Tooltip("Rows on this level's board. 8 (the default) is the standard square board.")]
        [SerializeField] private int _boardHeight = Board.SIZE;

        [Tooltip("Cells inside the board rectangle that are permanently unplayable. Empty (the " +
            "default) means a plain rectangle with no holes.")]
        [SerializeField] private List<BoardHoleCell> _boardHoles = new List<BoardHoleCell>();

        [Tooltip("Cells pre-filled with a reinforced block that resists line clears. Empty (the " +
            "default) means the level starts on a bare board, which is what every level authored " +
            "before reinforced cells existed does.")]
        [SerializeField] private List<ReinforcedCellAuthoring> _reinforcedCells =
            new List<ReinforcedCellAuthoring>();

        [Tooltip("Cells pre-filled with a timer block that converts to an ordinary cell once its " +
            "placement countdown reaches 0. Empty (the default) means the level authors none, which " +
            "is what every level authored before timer cells existed does.")]
        [SerializeField] private List<TimerCellAuthoring> _timerCells = new List<TimerCellAuthoring>();

        [Tooltip("Empty, playable positions marked with 1-3 levels of ice. The player fills one with " +
            "any piece and clears the line through it to melt one level; at 0 it is an ordinary cell. " +
            "Empty (the default) means the level authors none. Do not combine with a DiamondsCleared " +
            "objective — a diamond never lands on ice.")]
        [SerializeField] private List<TargetIceCellAuthoring> _targetIceCells =
            new List<TargetIceCellAuthoring>();

        [Tooltip("Cells pre-filled with a locked block no piece can be placed on. It unlocks into an "
            + "ordinary EMPTY cell once 1-3 DIFFERENT orthogonal neighbours have each been cleared at "
            + "least once. Its visual skin is rolled at random per cell at run start. Empty (the "
            + "default) means the level authors none.")]
        [SerializeField] private List<LockedCellAuthoring> _lockedCells = new List<LockedCellAuthoring>();

        [Tooltip("Cells pre-filled with a power star (issue #482). Line clears through it charge it "
            + "instead of removing it (+1 per line); at 3 charges it bursts and destroys the 3x3 around "
            + "it. A power-up that destroys it bursts it at once. Empty (the default) means none.")]
        [SerializeField] private List<PowerStarCellAuthoring> _powerStarCells = new List<PowerStarCellAuthoring>();

        [Tooltip("Puzzle-link groups (issue #483): 2-3 orthogonally connected cells locked together. A "
            + "group goes only if every member is hit in the same move (e.g. two lines at once), and then "
            + "pays a bonus; a partial hit removes none of it. Empty (the default) means none.")]
        [SerializeField] private List<PuzzleLinkGroupAuthoring> _puzzleLinkGroups = new List<PuzzleLinkGroupAuthoring>();

        [Tooltip("Power-up kinds this level's Path-mode run refuses to arm or spend. Empty (the " +
            "default) bans nothing, which is what every level authored before this field existed " +
            "does. Ignored entirely outside Path mode.")]
        [SerializeField] private List<PowerUpKind> _bannedPowerUps = new List<PowerUpKind>();

        [Header("Diamonds")]
        [Tooltip("Share (0..1) of dealt multi-cell pieces that carry diamonds while this level's " +
            "DiamondsCleared objective is active. 0.2 (the default) is the ~20% every diamond level " +
            "dealt at before this field existed. Read from the level's FIRST row only — it tunes the " +
            "level, not one of its objectives. Ignored by a level without a DiamondsCleared objective.")]
        [Range(0f, 1f)]
        [SerializeField] private float _diamondDecorationChance = DEFAULT_DIAMOND_DECORATION_CHANCE;

        [Tooltip("Fewest diamonds a decorated piece carries. 1 (the default) is what every diamond level " +
            "dealt with before this field existed. Clamped to the piece at draw time: a piece never has " +
            "every cell decorated, so a 2-cell piece always carries exactly one however high this is.")]
        [SerializeField] private int _diamondMinDecoratedCells = DEFAULT_DIAMOND_MIN_DECORATED_CELLS;

        [Tooltip("Most diamonds a decorated piece carries. 0 (the default) means no cap — up to every " +
            "cell but one, which is what every diamond level dealt with before this field existed. Any " +
            "other value is clamped to the piece at draw time and must not be below the minimum.")]
        [SerializeField] private int _diamondMaxDecoratedCells = DIAMOND_MAX_DECORATED_CELLS_UNCAPPED;

        /// <summary>1-based level number; <see cref="LevelCatalog"/> looks levels up by this, not by index.</summary>
        public int LevelNumber => _levelNumber;

        public ObjectiveType ObjectiveType => _objectiveType;

        public ObjectiveScope Scope => _scope;

        /// <summary>The authored target, exactly as the Inspector holds it. Not necessarily the target
        /// the built <see cref="ObjectiveDefinition"/> carries — see <see cref="ToObjectiveDefinition"/>
        /// for the one type that overrides it.</summary>
        public int TargetValue => _targetValue;

        /// <summary>
        /// Points completing this level adds to that run's score in <see cref="GameMode.Path"/>. Zero
        /// for a level that pays nothing.
        /// <para>
        /// Authored per level rather than computed from the level number: which levels pay, and how much, is content
        /// to be tuned in the catalog asset, not a formula in C#. Defaulting to zero is what keeps
        /// every level authored before this field existed behaving exactly as it did.
        /// </para>
        /// </summary>
        public int CompletionScoreBonus => _completionScoreBonus;

        /// <summary>
        /// How many <see cref="MustyBlockBlast.Core.SpecialCellKind.Coin"/> cells this level puts on the
        /// board for the player to destroy. Zero for a level that seeds none.
        /// <para>
        /// Authored per level rather than derived from the level number, matching
        /// <see cref="CompletionScoreBonus"/>'s philosophy: which
        /// levels are worth coins, and how many, is content to be tuned in the catalog asset rather than a
        /// formula in C#. Defaulting to zero is what keeps every level authored before this field existed
        /// behaving exactly as it did.
        /// </para>
        /// </summary>
        public int CoinCellCount => _coinCellCount;

        /// <summary>Colour id a <see cref="ObjectiveType.ColourCleared"/> objective counts.</summary>
        public int RequiredColourId => _requiredColourId;

        /// <summary>
        /// The reinforced cells this level pre-fills its board with, in authored order. Empty for a
        /// level that authors none, which is every level authored before the mechanic existed.
        /// <para>
        /// Never null — a row deserialized without the field at all gets the empty list the field
        /// initialiser supplies, so a caller never has to guard it.
        /// </para>
        /// </summary>
        public IReadOnlyList<ReinforcedCellAuthoring> ReinforcedCells =>
            _reinforcedCells ?? (IReadOnlyList<ReinforcedCellAuthoring>)EmptyReinforcedCells;

        /// <summary>
        /// The timer cells this level pre-fills its board with, in authored order. Empty for a level
        /// that authors none, which is every level authored before the mechanic existed.
        /// <para>
        /// Never null, mirroring <see cref="ReinforcedCells"/>.
        /// </para>
        /// </summary>
        public IReadOnlyList<TimerCellAuthoring> TimerCells =>
            _timerCells ?? (IReadOnlyList<TimerCellAuthoring>)EmptyTimerCells;

        /// <summary>
        /// The ice sockets this level marks on its board (issue #433), in authored order — positions
        /// that start EMPTY, not pre-filled, unlike the two lists above. Empty for a level that authors
        /// none, which is every level authored before the mechanic existed.
        /// <para>
        /// Never null, mirroring <see cref="ReinforcedCells"/>.
        /// </para>
        /// </summary>
        public IReadOnlyList<TargetIceCellAuthoring> TargetIceCells =>
            _targetIceCells ?? (IReadOnlyList<TargetIceCellAuthoring>)EmptyTargetIceCells;

        /// <summary>
        /// The locked cells this level pre-fills its board with (issue #434), in authored order. Empty
        /// for a level that authors none, which is every level authored before the mechanic existed.
        /// <para>
        /// Never null, mirroring <see cref="ReinforcedCells"/>.
        /// </para>
        /// </summary>
        public IReadOnlyList<LockedCellAuthoring> LockedCells =>
            _lockedCells ?? (IReadOnlyList<LockedCellAuthoring>)EmptyLockedCells;

        /// <summary>The power stars this level pre-fills its board with (issue #482), in authored order.
        /// Never null, mirroring <see cref="ReinforcedCells"/>.</summary>
        public IReadOnlyList<PowerStarCellAuthoring> PowerStarCells =>
            _powerStarCells ?? (IReadOnlyList<PowerStarCellAuthoring>)EmptyPowerStarCells;

        /// <summary>The puzzle-link groups this level pre-fills its board with (issue #483), in authored
        /// order — group N gets id N+1. Never null, mirroring <see cref="ReinforcedCells"/>.</summary>
        public IReadOnlyList<PuzzleLinkGroupAuthoring> PuzzleLinkGroups =>
            _puzzleLinkGroups ?? (IReadOnlyList<PuzzleLinkGroupAuthoring>)EmptyPuzzleLinkGroups;

        /// <summary>
        /// Power-up kinds this level's Path-mode run refuses to arm or spend (see
        /// <see cref="PowerUpSystem"/>). Empty for a level that bans none, which is every level
        /// authored before this mechanic existed.
        /// <para>
        /// Never null — a row deserialized without the field at all gets the empty list the field
        /// initialiser supplies, mirroring <see cref="ReinforcedCells"/>.
        /// </para>
        /// <para>
        /// This is also the public seam a future View binds to (e.g. to grey out a banned power-up's
        /// icon) — no separate System-level API is exposed for that; the accessor is the whole seam.
        /// </para>
        /// </summary>
        public IReadOnlyList<PowerUpKind> BannedPowerUps =>
            _bannedPowerUps ?? (IReadOnlyList<PowerUpKind>)EmptyBannedPowerUps;

        /// <summary>
        /// Share (0..1) of dealt multi-cell pieces <c>DiamondPieceDecorator</c> decorates while this
        /// level's <see cref="ObjectiveType.DiamondsCleared"/> objective is active.
        /// <para>
        /// Level-wide rather than per-objective, so — like <see cref="CompletionScoreBonus"/> and every
        /// other tunable that belongs to the level rather than to one of its rows — it is read from the
        /// row <see cref="LevelCatalog.Find"/> returns: the level's first. A level authoring three
        /// colours as three rows tunes its spawn rate once, on the first of them. Defaulting to
        /// <see cref="DEFAULT_DIAMOND_DECORATION_CHANCE"/> is what keeps every diamond level authored
        /// before this field existed dealing exactly as it did.
        /// </para>
        /// </summary>
        public float DiamondDecorationChance => _diamondDecorationChance;

        /// <summary>Fewest diamonds a decorated piece carries; clamped to the piece at draw time. Read
        /// from the level's first row, as <see cref="DiamondDecorationChance"/> is.</summary>
        public int DiamondMinDecoratedCells => _diamondMinDecoratedCells;

        /// <summary>Most diamonds a decorated piece carries, or
        /// <see cref="DIAMOND_MAX_DECORATED_CELLS_UNCAPPED"/> for "every cell but one"; clamped to the
        /// piece at draw time. Read from the level's first row, as <see cref="DiamondDecorationChance"/>
        /// is.</summary>
        public int DiamondMaxDecoratedCells => _diamondMaxDecoratedCells;

        /// <summary>
        /// Builds this level's board outline. Returns the shared <see cref="BoardShape.Standard"/>
        /// instance — never a fresh equivalent — whenever the authored fields describe the plain 8x8
        /// square, so every level authored before this field existed keeps pointing at exactly the
        /// shape the game has always used rather than an equal-but-different one.
        /// <para>
        /// Not yet read by <see cref="LevelCatalog"/> or <c>BoardSystem</c>: wiring a level's shape into
        /// the board it runs on is a later sub-issue of the board-shapes epic. This lands the authoring
        /// side so a level <em>can</em> be given a shape, with a default that changes nothing.
        /// </para>
        /// </summary>
        public BoardShape ToBoardShape()
        {
            if (_boardWidth == Board.SIZE && _boardHeight == Board.SIZE
                && (_boardHoles == null || _boardHoles.Count == 0))
            {
                return BoardShape.Standard;
            }

            var holes = new List<GridPosition>(_boardHoles.Count);
            for (int i = 0; i < _boardHoles.Count; i++)
            {
                holes.Add(_boardHoles[i].ToGridPosition());
            }

            return new BoardShape(_boardWidth, _boardHeight, holes);
        }

        /// <summary>
        /// Builds the immutable Core definition for this level. Throws the same way
        /// <see cref="ObjectiveDefinition"/> does on an invalid combination — see
        /// <see cref="IsValid"/> for a non-throwing check.
        /// <para>
        /// <paramref name="objectiveIndexInLevel"/> is this row's position among the rows authored for
        /// the same level number, and exists only to keep the generated ids unique now that a level may
        /// carry several objectives at once. Index 0 — every level authored before multi-objective
        /// levels existed, and the primary row of every one authored since — keeps the bare
        /// <c>level_7</c> id it has always had, so no shipped level's persisted cumulative progress
        /// (which is keyed by objective id) is orphaned by this. Later rows get <c>level_7_1</c>,
        /// <c>level_7_2</c>, and so on.
        /// </para>
        /// <para>
        /// For <see cref="ObjectiveType.ReinforcedCellsCleared"/> the authored <c>_targetValue</c> is
        /// deliberately IGNORED in favour of the number of reinforced cells this level authors: that
        /// objective is "clear all of them" by definition, and a designer must not be able to author a
        /// target that disagrees with the board — a subset target would complete the level with
        /// reinforced blocks still standing, and an over-large one could never complete at all. The
        /// same override applies to <see cref="ObjectiveType.IceCellsCleared"/>, against the count of
        /// ice sockets authored (issue #433 AC7).
        /// </para>
        /// </summary>
        public ObjectiveDefinition ToObjectiveDefinition(int objectiveIndexInLevel = 0)
        {
            return new ObjectiveDefinition(
                BuildObjectiveId(objectiveIndexInLevel),
                _objectiveType,
                _scope,
                EffectiveTargetValue(),
                _requiredLineCount,
                _requiredPieceFamily,
                _requiredOccupancyThreshold,
                _requiredPieceId,
                _windowSeconds,
                IsColourScoped(_objectiveType) ? _requiredColourId : 0);
        }

        /// <summary>The types that read <c>_requiredColourId</c>: <see cref="ObjectiveType.ColourCleared"/>
        /// (by block colour) and <see cref="ObjectiveType.DiamondsCleared"/> (by the diamond's own colour).
        /// Every other type ignores the field entirely.</summary>
        private static bool IsColourScoped(ObjectiveType type)
            => type == ObjectiveType.ColourCleared || type == ObjectiveType.DiamondsCleared;

        /// <summary>
        /// The target <see cref="ToObjectiveDefinition"/> actually builds with: the authored
        /// <c>_targetValue</c> for every type except <see cref="ObjectiveType.ReinforcedCellsCleared"/>
        /// (always the count of reinforced cells this level authors) and
        /// <see cref="ObjectiveType.IceCellsCleared"/> (always the count of ice sockets it authors) —
        /// see that method for why the authored value cannot be trusted for either.
        /// <para>
        /// Overridden here rather than hidden in the Inspector: nothing in this class shows or hides a
        /// field based on <see cref="_objectiveType"/> today (every type-specific field is simply
        /// documented as unused by the others), and inventing a custom Inspector for this one field
        /// would be a lone exception to that convention.
        /// </para>
        /// </summary>
        private int EffectiveTargetValue()
        {
            if (_objectiveType == ObjectiveType.ReinforcedCellsCleared)
            {
                return ReinforcedCells.Count;
            }

            if (_objectiveType == ObjectiveType.IceCellsCleared)
            {
                return TargetIceCells.Count;
            }

            return _targetValue;
        }

        /// <summary>
        /// The id <see cref="ToObjectiveDefinition"/> stamps on this row. Suffixed only from the second
        /// row of a level onwards — see that method for why index 0 must stay bare.
        /// </summary>
        private string BuildObjectiveId(int objectiveIndexInLevel)
        {
            if (objectiveIndexInLevel <= 0)
            {
                return OBJECTIVE_ID_PREFIX + _levelNumber;
            }

            return OBJECTIVE_ID_PREFIX + _levelNumber + OBJECTIVE_ID_INDEX_SEPARATOR + objectiveIndexInLevel;
        }

        /// <summary>
        /// Whether <see cref="ToObjectiveDefinition"/> would succeed. Mirrors
        /// <see cref="ObjectiveDefinition"/>'s constructor guards so a bad entry can be reported —
        /// in the Inspector and at load time — instead of throwing mid-run.
        /// </summary>
        public bool IsValid(out string error)
        {
            if (_levelNumber <= 0)
            {
                error = "Level number must be 1 or greater.";
                return false;
            }

            if (_targetValue <= 0)
            {
                error = "Target value must be greater than zero — an objective with a non-positive target can never complete.";
                return false;
            }

            if (_completionScoreBonus < 0)
            {
                error = "Completion score bonus cannot be negative — a level must never charge the player for clearing it.";
                return false;
            }

            if (_objectiveType == ObjectiveType.ScoreInRun && _scope == ObjectiveScope.Cumulative)
            {
                error = "ScoreInRun objectives cannot be Cumulative — they always reset with the run.";
                return false;
            }

            if ((_objectiveType == ObjectiveType.SimultaneousLineClear
                    || _objectiveType == ObjectiveType.AtLeastLineClear)
                && _requiredLineCount <= 0)
            {
                error = $"{_objectiveType} needs a required line count of 1 or more, or no placement can ever qualify.";
                return false;
            }

            if (_boardWidth <= 0 || _boardHeight <= 0)
            {
                error = "Board width and height must both be 1 or greater.";
                return false;
            }

            for (int i = 0; i < _boardHoles.Count; i++)
            {
                GridPosition hole = _boardHoles[i].ToGridPosition();
                if (hole.X < 0 || hole.X >= _boardWidth || hole.Y < 0 || hole.Y >= _boardHeight)
                {
                    error = $"Hole cell {hole} is outside this level's {_boardWidth}x{_boardHeight} board.";
                    return false;
                }
            }

            IReadOnlyList<ReinforcedCellAuthoring> reinforcedCells = ReinforcedCells;
            for (int i = 0; i < reinforcedCells.Count; i++)
            {
                ReinforcedCellAuthoring reinforced = reinforcedCells[i];
                if (reinforced == null)
                {
                    error = "A reinforced cell entry is empty — remove the row or fill it in.";
                    return false;
                }

                GridPosition position = reinforced.ToGridPosition();
                if (position.X < 0 || position.X >= _boardWidth
                    || position.Y < 0 || position.Y >= _boardHeight)
                {
                    error = $"Reinforced cell {position} is outside this level's {_boardWidth}x{_boardHeight} board.";
                    return false;
                }

                if (reinforced.HitCount < ReinforcedCellAuthoring.MIN_HIT_COUNT
                    || reinforced.HitCount > ReinforcedCellAuthoring.MAX_HIT_COUNT)
                {
                    error = $"Reinforced cell {position} needs a hit count between "
                        + $"{ReinforcedCellAuthoring.MIN_HIT_COUNT} and {ReinforcedCellAuthoring.MAX_HIT_COUNT}.";
                    return false;
                }

                // A cell cannot be both: a hole can never hold a block, so a reinforced block authored
                // on one could never be placed, and the level would silently open without it.
                if (IsAuthoredHole(position))
                {
                    error = $"Reinforced cell {position} is also authored as a hole — a cell cannot be both.";
                    return false;
                }
            }

            IReadOnlyList<TimerCellAuthoring> timerCells = TimerCells;
            for (int i = 0; i < timerCells.Count; i++)
            {
                TimerCellAuthoring timerCell = timerCells[i];
                if (timerCell == null)
                {
                    error = "A timer cell entry is empty — remove the row or fill it in.";
                    return false;
                }

                GridPosition position = timerCell.ToGridPosition();
                if (position.X < 0 || position.X >= _boardWidth
                    || position.Y < 0 || position.Y >= _boardHeight)
                {
                    error = $"Timer cell {position} is outside this level's {_boardWidth}x{_boardHeight} board.";
                    return false;
                }

                if (timerCell.StartingCountdown < TimerCellAuthoring.MIN_STARTING_COUNTDOWN
                    || timerCell.StartingCountdown > TimerCellAuthoring.MAX_STARTING_COUNTDOWN)
                {
                    error = $"Timer cell {position} needs a starting countdown between "
                        + $"{TimerCellAuthoring.MIN_STARTING_COUNTDOWN} and {TimerCellAuthoring.MAX_STARTING_COUNTDOWN}.";
                    return false;
                }

                // A cell cannot be both: a hole can never hold a block, so a timer block authored on one
                // could never be placed, and the level would silently open without it.
                if (IsAuthoredHole(position))
                {
                    error = $"Timer cell {position} is also authored as a hole — a cell cannot be both.";
                    return false;
                }

                // Nor can a cell be authored as both a reinforced cell and a timer cell: each mechanic
                // brings its own block to a previously empty cell, and a cell cannot be pre-filled twice.
                for (int reinforcedIndex = 0; reinforcedIndex < reinforcedCells.Count; reinforcedIndex++)
                {
                    ReinforcedCellAuthoring reinforced = reinforcedCells[reinforcedIndex];
                    if (reinforced != null && reinforced.ToGridPosition().Equals(position))
                    {
                        error = $"Timer cell {position} is also authored as a reinforced cell — a cell cannot be both.";
                        return false;
                    }
                }
            }

            IReadOnlyList<TargetIceCellAuthoring> targetIceCells = TargetIceCells;
            for (int i = 0; i < targetIceCells.Count; i++)
            {
                TargetIceCellAuthoring iceCell = targetIceCells[i];
                if (iceCell == null)
                {
                    error = "An ice socket entry is empty — remove the row or fill it in.";
                    return false;
                }

                GridPosition position = iceCell.ToGridPosition();
                if (position.X < 0 || position.X >= _boardWidth
                    || position.Y < 0 || position.Y >= _boardHeight)
                {
                    error = $"Ice socket {position} is outside this level's {_boardWidth}x{_boardHeight} board.";
                    return false;
                }

                if (iceCell.IceLevel < TargetIceCellAuthoring.MIN_ICE_LEVEL
                    || iceCell.IceLevel > TargetIceCellAuthoring.MAX_ICE_LEVEL)
                {
                    error = $"Ice socket {position} needs an ice level between "
                        + $"{TargetIceCellAuthoring.MIN_ICE_LEVEL} and {TargetIceCellAuthoring.MAX_ICE_LEVEL}.";
                    return false;
                }

                // A socket must be a cell the player can fill: nothing can ever stand on a hole, so
                // its ice could never melt and the objective could never complete.
                if (IsAuthoredHole(position))
                {
                    error = $"Ice socket {position} is also authored as a hole — a cell cannot be both.";
                    return false;
                }

                // Nor may it sit under a pre-filled block: a socket starts EMPTY by definition, and a
                // reinforced or timer block seeded on the same cell would contradict that (and the
                // seeder would skip the socket).
                for (int reinforcedIndex = 0; reinforcedIndex < reinforcedCells.Count; reinforcedIndex++)
                {
                    ReinforcedCellAuthoring reinforced = reinforcedCells[reinforcedIndex];
                    if (reinforced != null && reinforced.ToGridPosition().Equals(position))
                    {
                        error = $"Ice socket {position} is also authored as a reinforced cell — a cell cannot be both.";
                        return false;
                    }
                }

                for (int timerIndex = 0; timerIndex < timerCells.Count; timerIndex++)
                {
                    TimerCellAuthoring timerCell = timerCells[timerIndex];
                    if (timerCell != null && timerCell.ToGridPosition().Equals(position))
                    {
                        error = $"Ice socket {position} is also authored as a timer cell — a cell cannot be both.";
                        return false;
                    }
                }

                // Two entries for one position would count as two sockets towards the objective's
                // forced target while only one could ever melt, so the level could never complete.
                for (int earlier = 0; earlier < i; earlier++)
                {
                    TargetIceCellAuthoring earlierCell = targetIceCells[earlier];
                    if (earlierCell != null && earlierCell.ToGridPosition().Equals(position))
                    {
                        error = $"Ice socket {position} is authored more than once — remove the duplicate entry.";
                        return false;
                    }
                }
            }

            IReadOnlyList<LockedCellAuthoring> lockedCells = LockedCells;
            for (int i = 0; i < lockedCells.Count; i++)
            {
                LockedCellAuthoring lockedCell = lockedCells[i];
                if (lockedCell == null)
                {
                    error = "A locked cell entry is empty — remove the row or fill it in.";
                    return false;
                }

                GridPosition position = lockedCell.ToGridPosition();
                if (position.X < 0 || position.X >= _boardWidth
                    || position.Y < 0 || position.Y >= _boardHeight)
                {
                    error = $"Locked cell {position} is outside this level's {_boardWidth}x{_boardHeight} board.";
                    return false;
                }

                if (lockedCell.UnlockThreshold < LockedCellAuthoring.MIN_UNLOCK_THRESHOLD
                    || lockedCell.UnlockThreshold > LockedCellAuthoring.MAX_UNLOCK_THRESHOLD)
                {
                    error = $"Locked cell {position} needs an unlock threshold between "
                        + $"{LockedCellAuthoring.MIN_UNLOCK_THRESHOLD} and {LockedCellAuthoring.MAX_UNLOCK_THRESHOLD}.";
                    return false;
                }

                // A cell cannot be both: a hole can never hold a block, so a lock authored on one could
                // never be placed, and the level would silently open without it.
                if (IsAuthoredHole(position))
                {
                    error = $"Locked cell {position} is also authored as a hole — a cell cannot be both.";
                    return false;
                }

                // Nor can it share a cell with any other authored mechanic: the two pre-filling ones
                // each bring their own block (a cell cannot be pre-filled twice), and an ice socket
                // starts EMPTY by definition, which a lock standing on it would contradict.
                for (int reinforcedIndex = 0; reinforcedIndex < reinforcedCells.Count; reinforcedIndex++)
                {
                    ReinforcedCellAuthoring reinforced = reinforcedCells[reinforcedIndex];
                    if (reinforced != null && reinforced.ToGridPosition().Equals(position))
                    {
                        error = $"Locked cell {position} is also authored as a reinforced cell — a cell cannot be both.";
                        return false;
                    }
                }

                for (int timerIndex = 0; timerIndex < timerCells.Count; timerIndex++)
                {
                    TimerCellAuthoring timerCell = timerCells[timerIndex];
                    if (timerCell != null && timerCell.ToGridPosition().Equals(position))
                    {
                        error = $"Locked cell {position} is also authored as a timer cell — a cell cannot be both.";
                        return false;
                    }
                }

                for (int iceIndex = 0; iceIndex < targetIceCells.Count; iceIndex++)
                {
                    TargetIceCellAuthoring iceCell = targetIceCells[iceIndex];
                    if (iceCell != null && iceCell.ToGridPosition().Equals(position))
                    {
                        error = $"Locked cell {position} is also authored as an ice socket — a cell cannot be both.";
                        return false;
                    }
                }

                for (int earlier = 0; earlier < i; earlier++)
                {
                    LockedCellAuthoring earlierCell = lockedCells[earlier];
                    if (earlierCell != null && earlierCell.ToGridPosition().Equals(position))
                    {
                        error = $"Locked cell {position} is authored more than once — remove the duplicate entry.";
                        return false;
                    }
                }

                // AC5: the threshold counts DISTINCT neighbours, and a cell on an edge, in a corner or
                // beside a hole has fewer than four. A threshold above what the position really has
                // could never be met, leaving the cell locked forever — named here, at authoring time,
                // rather than discovered by a player.
                int neighbourCount = CountPlayableOrthogonalNeighbours(position);
                if (lockedCell.UnlockThreshold > neighbourCount)
                {
                    error = $"Locked cell {position} needs {lockedCell.UnlockThreshold} distinct neighbours cleared "
                        + $"but only has {neighbourCount} playable orthogonal neighbour(s) — it could never unlock.";
                    return false;
                }
            }

            if (!ArePowerStarCellsValid(reinforcedCells, timerCells, targetIceCells, lockedCells, out error))
            {
                return false;
            }

            if (!ArePuzzleLinkGroupsValid(reinforcedCells, timerCells, targetIceCells, lockedCells, out error))
            {
                return false;
            }

            // The target for this type is the reinforced-cell count (see EffectiveTargetValue), so a
            // level authoring none would build an ObjectiveDefinition with target 0 — which throws.
            // Caught here, where every other type-specific precondition is, rather than at construction.
            if (_objectiveType == ObjectiveType.ReinforcedCellsCleared && reinforcedCells.Count == 0)
            {
                error = "ReinforcedCellsCleared needs at least one authored reinforced cell — its target "
                    + "is always \"all of them\", and there is nothing to clear.";
                return false;
            }

            // Same reasoning for the ice-socket objective (issue #433 AC7).
            if (_objectiveType == ObjectiveType.IceCellsCleared && targetIceCells.Count == 0)
            {
                error = "IceCellsCleared needs at least one authored ice socket — its target is always "
                    + "\"all of them\", and there is nothing to melt.";
                return false;
            }

            // Bounded by the cells a block could actually stand on, so a shaped board's threshold cannot
            // be authored above an occupancy it can never reach. Identical to 64 on the standard board.
            int playableCellCount = MaxPlayableCellCount();
            if (_objectiveType == ObjectiveType.ClutchRecoveryClear
                && (_requiredOccupancyThreshold <= 0 || _requiredOccupancyThreshold > playableCellCount))
            {
                error = $"ClutchRecoveryClear needs an occupancy threshold between 1 and {playableCellCount}.";
                return false;
            }

            if (_objectiveType == ObjectiveType.PieceIdCount || _objectiveType == ObjectiveType.PieceIdLineClear)
            {
                if (string.IsNullOrEmpty(_requiredPieceId))
                {
                    error = $"{_objectiveType} needs a required piece id.";
                    return false;
                }

                if (!PieceIdExistsInCatalog(_requiredPieceId))
                {
                    error = $"\"{_requiredPieceId}\" is not a piece id in PieceCatalog — check for a typo.";
                    return false;
                }
            }

            if (IsColourScoped(_objectiveType)
                && (_requiredColourId < 1 || _requiredColourId > Board.COLOUR_COUNT))
            {
                error = $"{_objectiveType} needs a required colour id between 1 and {Board.COLOUR_COUNT} — no other id is ever drawn.";
                return false;
            }

            if (_objectiveType == ObjectiveType.RollingLineClearWindow || _objectiveType == ObjectiveType.EarlyScoreRush)
            {
                if (_windowSeconds <= 0f)
                {
                    error = $"{_objectiveType} needs a window/deadline of more than 0 seconds.";
                    return false;
                }

                if (_scope == ObjectiveScope.Cumulative)
                {
                    error = $"{_objectiveType} objectives cannot be Cumulative — their window is measured against a per-run clock.";
                    return false;
                }
            }

            // Checked on every row, not just the first: the fields are harmless defaults on a row that
            // is never read for them, and a bad value on a later row is still a typo worth naming.
            if (_diamondDecorationChance < 0f || _diamondDecorationChance > 1f)
            {
                error = "Diamond decoration chance must be between 0 and 1 — it is a share of dealt pieces.";
                return false;
            }

            if (_diamondMinDecoratedCells < DEFAULT_DIAMOND_MIN_DECORATED_CELLS)
            {
                error = "Diamond min decorated cells must be 1 or more — a decorated piece always carries at least one diamond.";
                return false;
            }

            if (_diamondMaxDecoratedCells != DIAMOND_MAX_DECORATED_CELLS_UNCAPPED
                && _diamondMaxDecoratedCells < _diamondMinDecoratedCells)
            {
                error = $"Diamond max decorated cells must be {DIAMOND_MAX_DECORATED_CELLS_UNCAPPED} (no cap) or at least the minimum ({_diamondMinDecoratedCells}).";
                return false;
            }

            IReadOnlyList<PowerUpKind> bannedPowerUps = BannedPowerUps;
            for (int i = 0; i < bannedPowerUps.Count; i++)
            {
                PowerUpKind banned = bannedPowerUps[i];
                if (!Enum.IsDefined(typeof(PowerUpKind), banned))
                {
                    error = $"Banned power-up entry {i} is not a defined PowerUpKind value.";
                    return false;
                }

                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (bannedPowerUps[earlier] == banned)
                    {
                        error = $"\"{banned}\" is banned more than once — remove the duplicate entry.";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        /// <summary>Playable cells on this level's board, counted from the authored fields without
        /// building a <see cref="BoardShape"/> — validation runs on entries that may not yet be legal
        /// enough to build one from. Duplicate hole entries are tolerated by de-duplicating on the
        /// bounding rectangle, so a repeated cell cannot push the count negative.</summary>
        private int MaxPlayableCellCount()
        {
            int holeCount = 0;
            for (int i = 0; i < _boardHoles.Count; i++)
            {
                GridPosition hole = _boardHoles[i].ToGridPosition();
                bool alreadyCounted = false;
                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (_boardHoles[earlier].ToGridPosition().Equals(hole))
                    {
                        alreadyCounted = true;
                        break;
                    }
                }

                if (!alreadyCounted)
                {
                    holeCount++;
                }
            }

            return Mathf.Max(1, (_boardWidth * _boardHeight) - holeCount);
        }

        /// <summary>
        /// The puzzle-link groups' share of <see cref="IsValid"/> (issue #483 AC2): each group 2-3 cells,
        /// orthogonally connected into one piece, every cell on the board, not on a hole and not claimed
        /// by any other authored cell (a group brings its own blocks) or by another group.
        /// </summary>
        private bool ArePuzzleLinkGroupsValid(
            IReadOnlyList<ReinforcedCellAuthoring> reinforcedCells,
            IReadOnlyList<TimerCellAuthoring> timerCells,
            IReadOnlyList<TargetIceCellAuthoring> targetIceCells,
            IReadOnlyList<LockedCellAuthoring> lockedCells,
            out string error)
        {
            IReadOnlyList<PuzzleLinkGroupAuthoring> groups = PuzzleLinkGroups;
            IReadOnlyList<PowerStarCellAuthoring> starCells = PowerStarCells;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                PuzzleLinkGroupAuthoring group = groups[groupIndex];
                if (group == null)
                {
                    error = "A puzzle-link group entry is empty — remove the row or fill it in.";
                    return false;
                }

                int label = groupIndex + 1;
                if (group.CellCount < PuzzleLinkGroupAuthoring.MIN_GROUP_SIZE
                    || group.CellCount > PuzzleLinkGroupAuthoring.MAX_GROUP_SIZE)
                {
                    error = $"Puzzle-link group {label} has {group.CellCount} cell(s); a group needs "
                        + $"{PuzzleLinkGroupAuthoring.MIN_GROUP_SIZE}-{PuzzleLinkGroupAuthoring.MAX_GROUP_SIZE}.";
                    return false;
                }

                if (!group.IsConnected())
                {
                    error = $"Puzzle-link group {label} is not one piece: every cell must sit directly above, "
                        + "below, left or right of another cell of the group.";
                    return false;
                }

                for (int cellIndex = 0; cellIndex < group.CellCount; cellIndex++)
                {
                    GridPosition position = group.CellAt(cellIndex);
                    if (position.X < 0 || position.X >= _boardWidth || position.Y < 0 || position.Y >= _boardHeight)
                    {
                        error = $"Puzzle-link group {label} cell {position} is outside this level's "
                            + $"{_boardWidth}x{_boardHeight} board.";
                        return false;
                    }

                    if (IsAuthoredHole(position))
                    {
                        error = $"Puzzle-link group {label} cell {position} is also authored as a hole — a cell cannot be both.";
                        return false;
                    }

                    if (IsClaimedByAnotherMechanic(position, reinforcedCells, timerCells, targetIceCells, lockedCells, starCells)
                        || IsClaimedByAnotherPuzzleCell(position, groups, groupIndex, cellIndex))
                    {
                        error = $"Puzzle-link group {label} cell {position} shares its cell with another authored "
                            + "cell — a cell cannot be pre-filled twice.";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        private static bool IsClaimedByAnotherMechanic(
            GridPosition position,
            IReadOnlyList<ReinforcedCellAuthoring> reinforcedCells,
            IReadOnlyList<TimerCellAuthoring> timerCells,
            IReadOnlyList<TargetIceCellAuthoring> targetIceCells,
            IReadOnlyList<LockedCellAuthoring> lockedCells,
            IReadOnlyList<PowerStarCellAuthoring> starCells)
        {
            for (int index = 0; index < reinforcedCells.Count; index++)
            {
                if (reinforcedCells[index] != null && reinforcedCells[index].ToGridPosition().Equals(position))
                {
                    return true;
                }
            }

            for (int index = 0; index < timerCells.Count; index++)
            {
                if (timerCells[index] != null && timerCells[index].ToGridPosition().Equals(position))
                {
                    return true;
                }
            }

            for (int index = 0; index < targetIceCells.Count; index++)
            {
                if (targetIceCells[index] != null && targetIceCells[index].ToGridPosition().Equals(position))
                {
                    return true;
                }
            }

            for (int index = 0; index < lockedCells.Count; index++)
            {
                if (lockedCells[index] != null && lockedCells[index].ToGridPosition().Equals(position))
                {
                    return true;
                }
            }

            for (int index = 0; index < starCells.Count; index++)
            {
                if (starCells[index] != null && starCells[index].ToGridPosition().Equals(position))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether <paramref name="position"/> was already used by an earlier cell — of an earlier
        /// group, or earlier in its own group.</summary>
        private static bool IsClaimedByAnotherPuzzleCell(
            GridPosition position, IReadOnlyList<PuzzleLinkGroupAuthoring> groups, int groupIndex, int cellIndex)
        {
            for (int earlierGroup = 0; earlierGroup <= groupIndex; earlierGroup++)
            {
                PuzzleLinkGroupAuthoring group = groups[earlierGroup];
                if (group == null)
                {
                    continue;
                }

                int limit = earlierGroup == groupIndex ? cellIndex : group.CellCount;
                for (int index = 0; index < limit; index++)
                {
                    if (group.CellAt(index).Equals(position))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The power stars' share of <see cref="IsValid"/> (issue #482): each one on the board, not on a
        /// hole, authored once, and never on a cell another mechanic already claims — a star brings its
        /// own block, so it cannot share a cell with a reinforced, timer or locked block, and an ice
        /// socket starts empty by definition.
        /// </summary>
        private bool ArePowerStarCellsValid(
            IReadOnlyList<ReinforcedCellAuthoring> reinforcedCells,
            IReadOnlyList<TimerCellAuthoring> timerCells,
            IReadOnlyList<TargetIceCellAuthoring> targetIceCells,
            IReadOnlyList<LockedCellAuthoring> lockedCells,
            out string error)
        {
            IReadOnlyList<PowerStarCellAuthoring> starCells = PowerStarCells;
            for (int i = 0; i < starCells.Count; i++)
            {
                PowerStarCellAuthoring starCell = starCells[i];
                if (starCell == null)
                {
                    error = "A power star entry is empty — remove the row or fill it in.";
                    return false;
                }

                GridPosition position = starCell.ToGridPosition();
                if (position.X < 0 || position.X >= _boardWidth || position.Y < 0 || position.Y >= _boardHeight)
                {
                    error = $"Power star {position} is outside this level's {_boardWidth}x{_boardHeight} board.";
                    return false;
                }

                if (IsAuthoredHole(position))
                {
                    error = $"Power star {position} is also authored as a hole — a cell cannot be both.";
                    return false;
                }

                bool claimed = false;
                for (int index = 0; index < reinforcedCells.Count && !claimed; index++)
                {
                    claimed = reinforcedCells[index] != null && reinforcedCells[index].ToGridPosition().Equals(position);
                }

                for (int index = 0; index < timerCells.Count && !claimed; index++)
                {
                    claimed = timerCells[index] != null && timerCells[index].ToGridPosition().Equals(position);
                }

                for (int index = 0; index < targetIceCells.Count && !claimed; index++)
                {
                    claimed = targetIceCells[index] != null && targetIceCells[index].ToGridPosition().Equals(position);
                }

                for (int index = 0; index < lockedCells.Count && !claimed; index++)
                {
                    claimed = lockedCells[index] != null && lockedCells[index].ToGridPosition().Equals(position);
                }

                for (int index = 0; index < i && !claimed; index++)
                {
                    claimed = starCells[index] != null && starCells[index].ToGridPosition().Equals(position);
                }

                if (claimed)
                {
                    error = $"Power star {position} shares its cell with another authored cell — a cell cannot "
                        + "be pre-filled twice.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>How many of <paramref name="position"/>'s four orthogonal neighbours are inside this
        /// level's board rectangle and not authored holes — the most distinct neighbour clears a lock
        /// there could ever accumulate, and so the ceiling on its threshold (issue #434 AC5). Counted
        /// from the authored fields without building a <see cref="BoardShape"/>, for the reason
        /// <see cref="MaxPlayableCellCount"/> is.</summary>
        private int CountPlayableOrthogonalNeighbours(GridPosition position)
        {
            int count = 0;
            count += IsPlayableAuthoredCell(new GridPosition(position.X, position.Y + 1)) ? 1 : 0;
            count += IsPlayableAuthoredCell(new GridPosition(position.X, position.Y - 1)) ? 1 : 0;
            count += IsPlayableAuthoredCell(new GridPosition(position.X - 1, position.Y)) ? 1 : 0;
            count += IsPlayableAuthoredCell(new GridPosition(position.X + 1, position.Y)) ? 1 : 0;
            return count;
        }

        /// <summary>True when <paramref name="position"/> is inside the authored rectangle and not an
        /// authored hole — a cell a block could stand on and be destroyed from.</summary>
        private bool IsPlayableAuthoredCell(GridPosition position)
        {
            return position.X >= 0 && position.X < _boardWidth
                && position.Y >= 0 && position.Y < _boardHeight
                && !IsAuthoredHole(position);
        }

        /// <summary>True when <paramref name="position"/> is one of this level's authored hole cells.</summary>
        private bool IsAuthoredHole(GridPosition position)
        {
            if (_boardHoles == null)
            {
                return false;
            }

            for (int i = 0; i < _boardHoles.Count; i++)
            {
                if (_boardHoles[i].ToGridPosition().Equals(position))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PieceIdExistsInCatalog(string pieceId)
        {
            IReadOnlyList<Piece> allPieces = PieceCatalog.AllPieces;
            for (int pieceIndex = 0; pieceIndex < allPieces.Count; pieceIndex++)
            {
                if (allPieces[pieceIndex].Id == pieceId)
                {
                    return true;
                }
            }

            return false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Called by <see cref="LevelCatalog.OnValidate"/>: clamps the obviously-invalid numeric fields
        /// so the developer editing the catalog gets immediate feedback rather than a runtime throw.
        /// The scope/type conflict is left alone — silently rewriting an authored choice would be worse
        /// than reporting it — and is surfaced as a console warning instead.
        /// </summary>
        internal void ValidateInEditor()
        {
            _levelNumber = Mathf.Max(1, _levelNumber);
            _targetValue = Mathf.Max(1, _targetValue);
            _requiredLineCount = Mathf.Max(1, _requiredLineCount);
            _boardWidth = Mathf.Max(1, _boardWidth);
            _boardHeight = Mathf.Max(1, _boardHeight);
            _requiredOccupancyThreshold = Mathf.Clamp(_requiredOccupancyThreshold, 1, MaxPlayableCellCount());
            _windowSeconds = Mathf.Max(1f, _windowSeconds);
            _completionScoreBonus = Mathf.Max(0, _completionScoreBonus);
            _coinCellCount = Mathf.Max(0, _coinCellCount);
            _requiredColourId = Mathf.Clamp(_requiredColourId, 1, Board.COLOUR_COUNT);
            _diamondDecorationChance = Mathf.Clamp01(_diamondDecorationChance);
            _diamondMinDecoratedCells = Mathf.Max(DEFAULT_DIAMOND_MIN_DECORATED_CELLS, _diamondMinDecoratedCells);
            _diamondMaxDecoratedCells = Mathf.Max(DIAMOND_MAX_DECORATED_CELLS_UNCAPPED, _diamondMaxDecoratedCells);

            // Clamped per entry rather than reported, for the reason every numeric field above is: a
            // hit count outside the range is a typo with one sensible reading, and the developer sees
            // the corrected value immediately. A position out of bounds or on a hole is left alone and
            // surfaced by IsValid — silently moving an authored cell would be worse than naming it.
            if (_reinforcedCells != null)
            {
                for (int i = 0; i < _reinforcedCells.Count; i++)
                {
                    if (_reinforcedCells[i] != null)
                    {
                        _reinforcedCells[i].ValidateInEditor();
                    }
                }
            }

            if (_timerCells != null)
            {
                for (int i = 0; i < _timerCells.Count; i++)
                {
                    if (_timerCells[i] != null)
                    {
                        _timerCells[i].ValidateInEditor();
                    }
                }
            }

            if (_targetIceCells != null)
            {
                for (int i = 0; i < _targetIceCells.Count; i++)
                {
                    if (_targetIceCells[i] != null)
                    {
                        _targetIceCells[i].ValidateInEditor();
                    }
                }
            }

            if (_lockedCells != null)
            {
                for (int i = 0; i < _lockedCells.Count; i++)
                {
                    if (_lockedCells[i] != null)
                    {
                        _lockedCells[i].ValidateInEditor();
                    }
                }
            }
        }
#endif
    }
}
