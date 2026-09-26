using System.Collections.Generic;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Settings;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the Path levels' background lookup (issue #523): group boundaries, a single-level
    /// override, missing art, and when the background must fall back to the theme gradient.
    /// </summary>
    public class LevelBackgroundCatalogTests
    {
        private LevelBackgroundCatalog _catalog;
        private Sprite _spring;
        private Sprite _winter;
        private Sprite _override;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _spring = CreateSprite("Spring");
            _winter = CreateSprite("Winter");
            _override = CreateSprite("Override");

            _catalog = ScriptableObject.CreateInstance<LevelBackgroundCatalog>();
            _created.Add(_catalog);
            _catalog.SetBackgrounds(new List<LevelBackgroundConfig>
            {
                new LevelBackgroundConfig(1, 10, _spring),
                new LevelBackgroundConfig(11, 20, _winter),
                new LevelBackgroundConfig(15, 15, _override),
                new LevelBackgroundConfig(21, 30, null),
            });
        }

        [TearDown]
        public void TearDown()
        {
            for (int createdIndex = 0; createdIndex < _created.Count; createdIndex++)
            {
                Object.DestroyImmediate(_created[createdIndex]);
            }

            _created.Clear();
        }

        [TestCase(1)]
        [TestCase(10)]
        public void Find_GroupBoundaries_ReturnFirstGroup(int levelNumber)
        {
            Assert.AreSame(_spring, _catalog.Find(levelNumber));
        }

        [TestCase(11)]
        [TestCase(20)]
        public void Find_NextGroupBoundaries_ReturnSecondGroup(int levelNumber)
        {
            Assert.AreSame(_winter, _catalog.Find(levelNumber));
        }

        [Test]
        public void Find_SingleLevelRow_OverridesItsGroup()
        {
            Assert.AreSame(_override, _catalog.Find(15));
            Assert.AreSame(_winter, _catalog.Find(14));
        }

        [TestCase(0)]
        [TestCase(25)]
        [TestCase(100)]
        [TestCase(101)]
        public void Find_UncoveredOrUnauthoredLevel_ReturnsNull(int levelNumber)
        {
            Assert.IsNull(_catalog.Find(levelNumber));
        }

        [Test]
        public void Resolve_PathLevelWithSettingOn_ReturnsLevelImage()
        {
            Assert.AreSame(_spring, _catalog.Resolve(true, GameMode.Path, 5));
        }

        [Test]
        public void Resolve_SettingOff_FallsBackToGradient()
        {
            Assert.IsNull(_catalog.Resolve(false, GameMode.Path, 5));
        }

        [TestCase(GameMode.Endless)]
        [TestCase(GameMode.Timed)]
        public void Resolve_NonPathModes_FallBackToGradient(GameMode mode)
        {
            Assert.IsNull(_catalog.Resolve(true, mode, 5));
        }

        [Test]
        public void Resolve_NoActiveLevel_FallsBackToGradient()
        {
            Assert.IsNull(_catalog.Resolve(true, GameMode.Path, 0));
        }

        [Test]
        public void Resolve_EmptyCatalog_FallsBackToGradient()
        {
            var empty = ScriptableObject.CreateInstance<LevelBackgroundCatalog>();
            _created.Add(empty);

            Assert.IsNull(empty.Resolve(true, GameMode.Path, 5));
        }

        private Sprite CreateSprite(string spriteName)
        {
            var texture = new Texture2D(4, 8);
            _created.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 8f), new Vector2(0.5f, 0.5f));
            sprite.name = spriteName;
            _created.Add(sprite);
            return sprite;
        }
    }
}
