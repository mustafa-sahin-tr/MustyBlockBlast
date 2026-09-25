using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Decides the board outline a new run is played on (issue #472): the active Path level's
    /// authored shape (<see cref="LevelObjectiveConfig.ToBoardShape"/> — holes in the 8x8 frame), or the
    /// plain square everywhere else. <see cref="BoardSystem.StartNewRun"/> asks it once per run, before
    /// the board is cleared and seeded.
    /// <para>
    /// Path mode only, because only a Path run is bounded to one level. Endless and Timed roll from
    /// level to level inside a single run, and a board cannot change shape mid-run, so they always
    /// play the standard square — the shape belongs to the level's own run.
    /// </para>
    /// <para>
    /// Shapes are cached per level so the same level always hands back the same instance: helpers that
    /// keep a scratch board rebuild it only when the shape reference changes, so a replay of the same
    /// level reuses theirs.
    /// </para>
    /// </summary>
    public sealed class LevelBoardShapeSource
    {
        private readonly LevelCatalog _levelCatalog;
        private readonly PathRunModel _pathRunModel;
        private readonly GameModeModel _gameModeModel;
        private readonly Dictionary<int, BoardShape> _shapeByLevel = new Dictionary<int, BoardShape>();

        [Inject]
        public LevelBoardShapeSource(LevelCatalog levelCatalog, PathRunModel pathRunModel, GameModeModel gameModeModel)
        {
            _levelCatalog = levelCatalog;
            _pathRunModel = pathRunModel;
            _gameModeModel = gameModeModel;
        }

        /// <summary>The outline the run about to start should be played on.</summary>
        internal BoardShape ShapeForNewRun()
        {
            if (_gameModeModel == null || _gameModeModel.CurrentMode.Value != GameMode.Path || _levelCatalog == null)
            {
                return BoardShape.Standard;
            }

            int levelNumber = _pathRunModel.ActiveLevelNumber.Value;
            if (levelNumber == PathRunModel.NO_ACTIVE_LEVEL)
            {
                return BoardShape.Standard;
            }

            if (_shapeByLevel.TryGetValue(levelNumber, out BoardShape cached))
            {
                return cached;
            }

            LevelObjectiveConfig level = _levelCatalog.Find(levelNumber);
            BoardShape shape = level != null && level.IsValid(out _) ? level.ToBoardShape() : BoardShape.Standard;
            _shapeByLevel[levelNumber] = shape;
            return shape;
        }
    }
}
