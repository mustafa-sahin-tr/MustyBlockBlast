using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Runtime language state. Mirrors <see cref="SettingsModel"/>: a catalogue of what can be picked
    /// plus one reactive "what is picked", so Views re-render their labels on a language switch the
    /// same way they already re-paint on a theme switch.
    /// Mutated exclusively by <c>LocalizationSystem</c>.
    /// </summary>
    public sealed class LocalizationModel
    {
        public LocalizationModel(IReadOnlyList<LocaleDefinition> availableLocales)
        {
            AvailableLocales = availableLocales ?? new LocaleDefinition[0];

            // Contract: the first entry is the default locale (English) and the fallback every other
            // locale falls back to. LocalizationSystem relies on this for both an unknown saved code
            // and a key missing from the active locale's table.
            LocaleDefinition defaultLocale = AvailableLocales.Count > 0 ? AvailableLocales[0] : null;
            CurrentLocale = new ReactiveProperty<LocaleDefinition>(defaultLocale);
        }

        /// <summary>Selectable locales in display order. Index 0 is the default (English).</summary>
        public IReadOnlyList<LocaleDefinition> AvailableLocales { get; }

        /// <summary>The locale Views render with. Never null once at least one locale is configured.</summary>
        public ReactiveProperty<LocaleDefinition> CurrentLocale { get; }
    }
}
