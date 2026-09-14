using System;
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

        [Tooltip("1-based level number. This is the identity of the level, not its position in the list.")]
        [SerializeField] private int _levelNumber = 1;

        [Tooltip("What the objective measures.")]
        [SerializeField] private ObjectiveType _objectiveType = ObjectiveType.SimultaneousLineClear;

        [Tooltip("PerRun resets on game over; Cumulative survives runs and app restarts.")]
        [SerializeField] private ObjectiveScope _scope = ObjectiveScope.PerRun;

        [Tooltip("Value progress must reach to clear the level. Must be greater than zero.")]
        [SerializeField] private int _targetValue = 1;

        [Tooltip("Exact simultaneous line count a placement must clear. Used by SimultaneousLineClear only.")]
        [SerializeField] private int _requiredLineCount = 2;

        [Tooltip("Shape family a placed piece must belong to. Used by PieceFamilyCount only.")]
        [SerializeField] private PieceFamily _requiredPieceFamily = PieceFamily.Single;

        [Tooltip("Minimum board occupancy (cells filled) a qualifying clear must meet. Used by " +
            "ClutchRecoveryClear only.")]
        [SerializeField] private int _requiredOccupancyThreshold = 52;

        [Tooltip("Grant a power-up when this level is completed. Off by default — milestone levels " +
            "are the reward levels, not every level.")]
        [SerializeField] private bool _grantsLevelUpReward;

        [Tooltip("Power-up granted on completing this level. Used only when Grants Level Up Reward is on.")]
        [SerializeField] private PowerUpKind _levelUpReward = PowerUpKind.RowClear;

        /// <summary>1-based level number; <see cref="LevelCatalog"/> looks levels up by this, not by index.</summary>
        public int LevelNumber => _levelNumber;

        public ObjectiveType ObjectiveType => _objectiveType;

        public ObjectiveScope Scope => _scope;

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
        /// Builds the immutable Core definition for this level. Throws the same way
        /// <see cref="ObjectiveDefinition"/> does on an invalid combination — see
        /// <see cref="IsValid"/> for a non-throwing check.
        /// </summary>
        public ObjectiveDefinition ToObjectiveDefinition()
        {
            return new ObjectiveDefinition(
                OBJECTIVE_ID_PREFIX + _levelNumber,
                _objectiveType,
                _scope,
                _targetValue,
                _requiredLineCount,
                _requiredPieceFamily,
                _requiredOccupancyThreshold);
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

            if (_objectiveType == ObjectiveType.ScoreInRun && _scope == ObjectiveScope.Cumulative)
            {
                error = "ScoreInRun objectives cannot be Cumulative — they always reset with the run.";
                return false;
            }

            if (_objectiveType == ObjectiveType.SimultaneousLineClear && _requiredLineCount <= 0)
            {
                error = "SimultaneousLineClear needs a required line count of 1 or more, or no placement can ever qualify.";
                return false;
            }

            if (_objectiveType == ObjectiveType.ClutchRecoveryClear
                && (_requiredOccupancyThreshold <= 0 || _requiredOccupancyThreshold > Board.SIZE * Board.SIZE))
            {
                error = $"ClutchRecoveryClear needs an occupancy threshold between 1 and {Board.SIZE * Board.SIZE}.";
                return false;
            }

            error = null;
            return true;
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
            _requiredOccupancyThreshold = Mathf.Clamp(_requiredOccupancyThreshold, 1, Board.SIZE * Board.SIZE);
        }
#endif
    }
}
