using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace MustyBlockBlast.Presentation.Localization
{
    /// <summary>
    /// Backs <see cref="ILocalizedStringSource"/> with the project's <c>GameStrings</c> String Table
    /// Collection. This is the only class in the game that touches the Localization package: keeping
    /// it behind the interface is what lets the fallback rules live in (and be tested from) the
    /// Gameplay assembly, which has no Addressables dependency.
    /// <para>
    /// Answers for one locale at a time and applies no fallback of its own — an unknown locale or key
    /// is simply "false", and <c>LocalizationSystem</c> decides what to do about it.
    /// </para>
    /// </summary>
    internal sealed class UnityLocalizedStringSource : ILocalizedStringSource
    {
        /// <summary>
        /// Loaded tables by locale code. Also caches misses (as null) so a genuinely absent table
        /// costs one Addressables round-trip, not one per label per repaint.
        /// </summary>
        private readonly Dictionary<string, StringTable> _tablesByLocaleCode =
            new Dictionary<string, StringTable>(4);

        public bool TryGetString(string localeCode, string key, out string value)
        {
            value = null;

            if (string.IsNullOrEmpty(localeCode) || string.IsNullOrEmpty(key))
            {
                return false;
            }

            StringTable table = GetTable(localeCode);
            if (table == null)
            {
                return false;
            }

            StringTableEntry entry = table.GetEntry(key);

            // Value, not LocalizedValue: entries are plain format strings composed by
            // LocalizationSystem.Format, so Smart Format must not get a second pass at them.
            if (entry == null || string.IsNullOrEmpty(entry.Value))
            {
                return false;
            }

            value = entry.Value;
            return true;
        }

        private StringTable GetTable(string localeCode)
        {
            if (_tablesByLocaleCode.TryGetValue(localeCode, out StringTable cached))
            {
                return cached;
            }

            StringTable table = LoadTable(localeCode);
            _tablesByLocaleCode.Add(localeCode, table);
            return table;
        }

        private static StringTable LoadTable(string localeCode)
        {
            UnityLocalizationBootstrap.EnsureInitialized();

            if (!LocalizationSettings.HasSettings)
            {
                return null;
            }

            ILocalesProvider localesProvider = LocalizationSettings.AvailableLocales;
            Locale locale = localesProvider == null ? null : localesProvider.GetLocale(localeCode);
            if (locale == null)
            {
                return null;
            }

            // Synchronous by design: the table is a handful of short strings and every caller is a
            // View painting a label right now, so an async load would flash untranslated UI.
            return LocalizationSettings.StringDatabase.GetTable(LocalizationKeys.TABLE_NAME, locale);
        }
    }
}
