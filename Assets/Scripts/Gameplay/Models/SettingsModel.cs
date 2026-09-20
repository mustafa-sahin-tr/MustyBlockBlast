using System.Collections.Generic;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Runtime player-settings state. Currently only the selected theme, but this is the home for
    /// every future setting (sound on/off, haptics, ...) — it is deliberately not theme-only.
    /// Mutated exclusively by <c>SettingsSystem</c>.
    /// </summary>
    public sealed class SettingsModel
    {
        public SettingsModel(IReadOnlyList<ThemeDefinition> availableThemes)
        {
            AvailableThemes = availableThemes ?? new ThemeDefinition[0];

            // Contract: the first entry of the scene-configured list is the default theme (Yaz).
            // SettingsSystem falls back to it whenever no valid theme id is persisted.
            ThemeDefinition defaultTheme = AvailableThemes.Count > 0 ? AvailableThemes[0] : null;
            CurrentTheme = new ReactiveProperty<ThemeDefinition>(defaultTheme);
        }

        /// <summary>Selectable themes in display order. Index 0 is the default.</summary>
        public IReadOnlyList<ThemeDefinition> AvailableThemes { get; }

        /// <summary>The theme Views render with. Never null once at least one theme is configured.</summary>
        public ReactiveProperty<ThemeDefinition> CurrentTheme { get; }
    }
}
