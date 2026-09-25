using System.Collections.Generic;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Runtime player-settings state: the selected theme and whether the board punch plays. This is
    /// the home for every future setting (haptics, ...) — it is deliberately not theme-only.
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

        /// <summary>
        /// Whether the board card plays its short scale punch on a line clear (issue #367 AC5). On by
        /// default; the player can switch it off from the settings card. Presentation-only — no rule
        /// reads it.
        /// </summary>
        public ReactiveProperty<bool> BoardPunchEnabled { get; } = new ReactiveProperty<bool>(true);
    }
}
