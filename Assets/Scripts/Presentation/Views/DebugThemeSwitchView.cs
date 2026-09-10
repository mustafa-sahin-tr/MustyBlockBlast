using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// TEMPORARY manual-test hook: press T to cycle through the available themes. This exists only
    /// so the theme plumbing can be verified by hand before a real picker exists. DELETE this class
    /// (and its scene component plus its LifetimeScope registration) once the theme picker UI ships.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugThemeSwitchView : MonoBehaviour
    {
        private SettingsSystem _settingsSystem;
        private SettingsModel _settingsModel;

        [Inject]
        public void Construct(SettingsSystem settingsSystem, SettingsModel settingsModel)
        {
            _settingsSystem = settingsSystem;
            _settingsModel = settingsModel;
        }

        private void Update()
        {
            if (_settingsSystem == null || _settingsModel == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (!keyboard.tKey.wasPressedThisFrame)
            {
                return;
            }

            CycleTheme();
        }

        private void CycleTheme()
        {
            IReadOnlyList<ThemeDefinition> themes = _settingsModel.AvailableThemes;
            if (themes.Count == 0)
            {
                return;
            }

            ThemeDefinition current = _settingsModel.CurrentTheme.Value;
            int currentIndex = -1;
            for (int themeIndex = 0; themeIndex < themes.Count; themeIndex++)
            {
                if (themes[themeIndex] == current)
                {
                    currentIndex = themeIndex;
                    break;
                }
            }

            ThemeDefinition next = themes[(currentIndex + 1) % themes.Count];
            if (next == null)
            {
                return;
            }

            _settingsSystem.SetTheme(next.Id);
        }
    }
}
