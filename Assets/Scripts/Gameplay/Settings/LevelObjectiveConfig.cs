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

        /// <summary>Shared, never-mutated empty for a row whose <see cref="_bannedPowerUps"/> field is
        /// null — the same never-existed-yet case <see cref="EmptyReinforcedCells"/> covers.</summary>
        private static readonly PowerUpKind[] EmptyBannedPowerUps = new PowerUpKind[0];

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

        [Tooltip("Piece colour id (1..5) whose cells a ColourCleared objective counts. The id is theme-"
            + "agnostic: the swatch the player sees comes from the active theme at runtime. Unused otherwise.")]
        [SerializeField] private int _requiredColourId = 1;

        [Tooltip("Grant a power-up when this level is completed. Off by default — milestone levels " +
            "are the reward levels, not every level.")]
        [SerializeField] private bool _grantsLevelUpReward;

        [Tooltip("Power-up granted on completing this level. Used only when Grants Level Up Reward is on.")]
        [SerializeField] private PowerUpKind _levelUpReward = PowerUpKind.RowClear;

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

        [Tooltip("Power-up kinds this level's Path-mode run refuses to arm or spend. Empty (the " +
            "default) bans nothing, which is what every level authored before this field existed " +
            "does. Ignored entirely outside Path mode.")]
        [SerializeField] private List<PowerUpKind> _bannedPowerUps = new List<PowerUpKind>();

        /// <summary>1-based level number; <see cref="LevelCatalog"/> looks levels up by this, not by index.</summary>
        public int LevelNumber => _levelNumber;

        public ObjectiveType ObjectiveType => _objectiveType;

        public ObjectiveScope Scope => _scope;

        /// <summary>The authored target, exactly as the Inspector holds it. Not necessarily the target
        /// the built <see cref="ObjectiveDefinition"/> carries — see <see cref="ToObjectiveDefinition"/>
        /// for the one type that overrides it.</summary>
        public int TargetValue => _targetValue;

        /// <summary>
        /// Whether completing this level pays out <see cref="LevelUpReward"/>. False for most levels:
        /// which levels reward, and with what, is authored content rather than a rule, so it is tuned
        /// in the catalog asset rather than derived from the level number in code.
        /// </summary>
        public bool GrantsLevelUpReward => _grantsLevelUpReward;

        /// <summary>The power-up completing this level grants. Meaningless unless
        /// <see cref="GrantsLevelUpReward"/> is true.</summary>
        public PowerUpKind LevelUpReward => _levelUpReward;

        /// <summary>
        /// Points completing this level adds to that run's score in <see cref="GameMode.Path"/>. Zero
        /// for a level that pays nothing.
        /// <para>
        /// Authored per level rather than computed from the level number, matching
        /// <see cref="GrantsLevelUpReward"/>'s philosophy: which levels pay, and how much, is content
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
        /// <see cref="GrantsLevelUpReward"/>'s and <see cref="CompletionScoreBonus"/>'s philosophy: which
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
        /// reinforced blocks still standing, and an over-large one could never complete at all.
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
                _objectiveType == ObjectiveType.ColourCleared ? _requiredColourId : 0);
        }

        /// <summary>
        /// The target <see cref="ToObjectiveDefinition"/> actually builds with: the authored
        /// <c>_targetValue</c> for every type except <see cref="ObjectiveType.ReinforcedCellsCleared"/>,
        /// which is always the count of reinforced cells this level authors — see that method for why
        /// the authored value cannot be trusted for it.
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

            // The target for this type is the reinforced-cell count (see EffectiveTargetValue), so a
            // level authoring none would build an ObjectiveDefinition with target 0 — which throws.
            // Caught here, where every other type-specific precondition is, rather than at construction.
            if (_objectiveType == ObjectiveType.ReinforcedCellsCleared && reinforcedCells.Count == 0)
            {
                error = "ReinforcedCellsCleared needs at least one authored reinforced cell — its target "
                    + "is always \"all of them\", and there is nothing to clear.";
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

            if (_objectiveType == ObjectiveType.ColourCleared
                && (_requiredColourId < 1 || _requiredColourId > Board.COLOUR_COUNT))
            {
                error = $"ColourCleared needs a required colour id between 1 and {Board.COLOUR_COUNT} — no other id is ever drawn.";
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
        }
#endif
    }
}
