using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Per-level board shapes (issue #472): a Path run is played on its level's authored outline, every
    /// other run on the standard square, and the board model swaps shapes only when asked.
    /// </summary>
    public class LevelBoardShapeTests
    {
        private const string HOLED_LEVEL =
            "{\"_levelNumber\":1,\"_objectiveType\":2,\"_targetValue\":100,"
            + "\"_boardWidth\":8,\"_boardHeight\":8,\"_boardHoles\":[{\"_x\":0,\"_y\":0},{\"_x\":7,\"_y\":7}]}";

        private const string PLAIN_LEVEL = "{\"_levelNumber\":2,\"_objectiveType\":2,\"_targetValue\":100}";

        private LevelCatalog _catalog;
        private PathRunModel _pathRunModel;
        private GameModeModel _gameModeModel;
        private LevelBoardShapeSource _source;

        [SetUp]
        public void CreateSource()
        {
            _catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            JsonUtility.FromJsonOverwrite($"{{\"_levels\":[{HOLED_LEVEL},{PLAIN_LEVEL}]}}", _catalog);
            _pathRunModel = new PathRunModel();
            _gameModeModel = new GameModeModel();
            _source = new LevelBoardShapeSource(_catalog, _pathRunModel, _gameModeModel);
        }

        [Test]
        public void APathRunOnAHoledLevel_GetsThatLevelsHoles()
        {
            _gameModeModel.CurrentMode.Value = GameMode.Path;
            _pathRunModel.ActiveLevelNumber.Value = 1;

            BoardShape shape = _source.ShapeForNewRun();

            Assert.IsTrue(shape.IsHole(new GridPosition(0, 0)));
            Assert.IsTrue(shape.IsHole(new GridPosition(7, 7)));
            Assert.IsFalse(shape.IsHole(new GridPosition(3, 3)));
            Assert.AreSame(shape, _source.ShapeForNewRun(), "The same level must hand back the same instance.");
        }

        [Test]
        public void APlainLevel_AndEveryNonPathRun_GetTheStandardSquare()
        {
            _gameModeModel.CurrentMode.Value = GameMode.Path;
            _pathRunModel.ActiveLevelNumber.Value = 2;
            Assert.AreSame(BoardShape.Standard, _source.ShapeForNewRun());

            // Endless rolls through levels inside one run, so it never takes a level's shape.
            _gameModeModel.CurrentMode.Value = GameMode.Endless;
            _pathRunModel.ActiveLevelNumber.Value = 1;
            Assert.AreSame(BoardShape.Standard, _source.ShapeForNewRun());
        }

        [Test]
        public void BoardModel_ApplyShape_SwapsToAnEmptyBoardOfThatOutline_AndAnnouncesIt()
        {
            var model = new BoardModel();
            model.Occupy(new GridPosition(3, 3), 1);
            int shapeChanges = 0;
            model.ShapeChanged += () => shapeChanges++;

            _gameModeModel.CurrentMode.Value = GameMode.Path;
            _pathRunModel.ActiveLevelNumber.Value = 1;
            BoardShape holed = _source.ShapeForNewRun();

            Assert.IsTrue(model.ApplyShape(holed));
            Assert.AreEqual(1, shapeChanges);
            Assert.IsTrue(model.IsHole(new GridPosition(0, 0)));
            Assert.AreEqual(Board.EMPTY, model.GetCell(new GridPosition(3, 3)));
            Assert.AreEqual(62, model.Board.PlayableCellCount);

            Assert.IsFalse(model.ApplyShape(holed), "The same shape again is a no-op.");
            Assert.AreEqual(1, shapeChanges);
        }
    }
}
