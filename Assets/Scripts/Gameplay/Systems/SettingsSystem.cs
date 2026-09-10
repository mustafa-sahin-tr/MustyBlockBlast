using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="SettingsModel"/>. Loads the persisted theme selection on construction and
    /// writes it back on every change, so it must be resolved before any View subscribes.
    /// An unset, unknown or corrupted saved id silently falls back to the default theme.
    /// </summary>
    public sealed class SettingsSystem
    {
        private const string THEME_ID_PREFS_KEY = "Settings.ThemeId";

        private readonly SettingsModel _settingsModel;

        public SettingsSystem(SettingsModel settingsModel)
        {
            _settingsModel = settingsModel;

            IReadOnlyList<ThemeDefinition> themes = _settingsModel.AvailableThemes;
            int defaultThemeId = themes.Count > 0 ? themes[0].Id : 0;
            SetTheme(PlayerPrefs.GetInt(THEME_ID_PREFS_KEY, defaultThemeId));
        }

        /// <summary>
        /// Selects the theme with the given <see cref="ThemeDefinition.Id"/> and persists it.
        /// Unknown ids resolve to the default theme instead of throwing.
        /// </summary>
        public void SetTheme(int themeId)
        {
            IReadOnlyList<ThemeDefinition> themes = _settingsModel.AvailableThemes;
            if (themes.Count == 0)
            {
                return;
            }

            ThemeDefinition selected = null;
            for (int themeIndex = 0; themeIndex < themes.Count; themeIndex++)
            {
                ThemeDefinition candidate = themes[themeIndex];
                if (candidate != null && candidate.Id == themeId)
                {
                    selected = candidate;
                    break;
                }
            }

            if (selected == null)
            {
                selected = themes[0];
            }

            if (selected == null)
            {
                return;
            }

            _settingsModel.CurrentTheme.Value = selected;
            PlayerPrefs.SetInt(THEME_ID_PREFS_KEY, selected.Id);
        }
    }
}
