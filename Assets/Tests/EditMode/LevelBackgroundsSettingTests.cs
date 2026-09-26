using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the "Level Backgrounds" settings switch (issue #522): on by default, persisted across a
    /// reload, and independent of the other switches.
    /// </summary>
    public class LevelBackgroundsSettingTests
    {
        private const string LEVEL_BACKGROUNDS_PREFS_KEY = "Settings.LevelBackgroundsEnabled";
        private const string BOARD_PUNCH_PREFS_KEY = "Settings.BoardPunchEnabled";

        private bool _hadLevelBackgroundsKey;
        private int _savedLevelBackgrounds;
        private bool _hadBoardPunchKey;
        private int _savedBoardPunch;

        [SetUp]
        public void SetUp()
        {
            _hadLevelBackgroundsKey = PlayerPrefs.HasKey(LEVEL_BACKGROUNDS_PREFS_KEY);
            _savedLevelBackgrounds = PlayerPrefs.GetInt(LEVEL_BACKGROUNDS_PREFS_KEY, 1);
            _hadBoardPunchKey = PlayerPrefs.HasKey(BOARD_PUNCH_PREFS_KEY);
            _savedBoardPunch = PlayerPrefs.GetInt(BOARD_PUNCH_PREFS_KEY, 1);
            PlayerPrefs.DeleteKey(LEVEL_BACKGROUNDS_PREFS_KEY);
            PlayerPrefs.DeleteKey(BOARD_PUNCH_PREFS_KEY);
        }

        [TearDown]
        public void TearDown()
        {
            Restore(LEVEL_BACKGROUNDS_PREFS_KEY, _hadLevelBackgroundsKey, _savedLevelBackgrounds);
            Restore(BOARD_PUNCH_PREFS_KEY, _hadBoardPunchKey, _savedBoardPunch);
        }

        [Test]
        public void SettingsSystem_LevelBackgrounds_DefaultsOnWhenNothingIsSaved()
        {
            var model = new SettingsModel(new ThemeDefinition[0]);
            new SettingsSystem(model);

            Assert.IsTrue(model.LevelBackgroundsEnabled.Value);
        }

        [Test]
        public void SettingsSystem_LevelBackgrounds_PersistsWhenSwitchedOffAndBackOn()
        {
            var model = new SettingsModel(new ThemeDefinition[0]);
            var system = new SettingsSystem(model);

            system.SetLevelBackgroundsEnabled(false);
            Assert.IsFalse(model.LevelBackgroundsEnabled.Value);

            var reloadedModel = new SettingsModel(new ThemeDefinition[0]);
            var reloadedSystem = new SettingsSystem(reloadedModel);
            Assert.IsFalse(reloadedModel.LevelBackgroundsEnabled.Value);

            reloadedSystem.SetLevelBackgroundsEnabled(true);
            var secondReloadModel = new SettingsModel(new ThemeDefinition[0]);
            new SettingsSystem(secondReloadModel);
            Assert.IsTrue(secondReloadModel.LevelBackgroundsEnabled.Value);
        }

        [Test]
        public void SettingsSystem_LevelBackgrounds_SwitchingOffLeavesBoardPunchUntouched()
        {
            var model = new SettingsModel(new ThemeDefinition[0]);
            var system = new SettingsSystem(model);

            system.SetLevelBackgroundsEnabled(false);

            Assert.IsTrue(model.BoardPunchEnabled.Value);
            Assert.IsFalse(PlayerPrefs.HasKey(BOARD_PUNCH_PREFS_KEY));
        }

        private static void Restore(string key, bool hadKey, int savedValue)
        {
            if (hadKey)
            {
                PlayerPrefs.SetInt(key, savedValue);
            }
            else
            {
                PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
