using System;
using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="LocalizationModel"/> and resolves every player-facing string. Loads the
    /// persisted language on construction — or, on a first launch with nothing persisted yet, the
    /// device's language — and writes it back on every change, so it must be resolved before any View
    /// subscribes — exactly like <see cref="SettingsSystem"/>.
    /// <para>
    /// Two independent fallbacks live here, and only here:
    /// an unset, unknown or corrupted saved locale code resolves to the default locale, and a key
    /// missing from the active locale's table is served from the default locale's table instead. A key
    /// missing from both is returned verbatim rather than throwing, so a bad key is a visible label
    /// instead of a crashed UI build.
    /// </para>
    /// </summary>
    public sealed class LocalizationSystem
    {
        private const string LOCALE_CODE_PREFS_KEY = "Settings.LocaleCode";

        /// <summary>
        /// The language an unmapped device falls back to. Only a hint: every code this class produces
        /// still goes through <see cref="SetLocale"/>, which resolves anything the model does not
        /// actually offer to the model's own default locale.
        /// </summary>
        private const string DEFAULT_LOCALE_CODE = "en";

        private readonly LocalizationModel _localizationModel;
        private readonly ILocalizedStringSource _stringSource;

        public LocalizationSystem(LocalizationModel localizationModel, ILocalizedStringSource stringSource)
        {
            _localizationModel = localizationModel;
            _stringSource = stringSource;

            // A saved preference is an explicit player choice, so it always wins. Only a player who
            // has never picked a language gets the device's one — and because SetLocale persists what
            // it selected, the detection runs exactly once in the lifetime of an install.
            string localeCode = PlayerPrefs.HasKey(LOCALE_CODE_PREFS_KEY)
                ? PlayerPrefs.GetString(LOCALE_CODE_PREFS_KEY, DEFAULT_LOCALE_CODE)
                : ResolveDeviceLocaleCode(Application.systemLanguage);

            SetLocale(localeCode);
        }

        /// <summary>
        /// Maps a device language onto a shipped locale code. Unmapped values — every
        /// <see cref="SystemLanguage"/> the game has no translation for, including
        /// <see cref="SystemLanguage.Unknown"/> — resolve to English rather than throwing, so no
        /// device can boot into an unlocalized UI.
        /// <para>
        /// Deliberately a pure function of the enum with no Unity state of its own: it is the only
        /// part of first-launch detection that can be unit-tested, since
        /// <see cref="Application.systemLanguage"/> can be read but never set.
        /// </para>
        /// </summary>
        public static string ResolveDeviceLocaleCode(SystemLanguage systemLanguage)
        {
            switch (systemLanguage)
            {
                case SystemLanguage.Spanish:
                    return "es";
                case SystemLanguage.Turkish:
                    return "tr";
                default:
                    return DEFAULT_LOCALE_CODE;
            }
        }

        /// <summary>
        /// Selects the locale with the given code (case-insensitive) and persists it. Unknown codes
        /// resolve to the default locale instead of throwing.
        /// </summary>
        public void SetLocale(string localeCode)
        {
            IReadOnlyList<LocaleDefinition> locales = _localizationModel.AvailableLocales;
            if (locales.Count == 0)
            {
                return;
            }

            LocaleDefinition selected = null;
            for (int localeIndex = 0; localeIndex < locales.Count; localeIndex++)
            {
                LocaleDefinition candidate = locales[localeIndex];
                if (candidate != null && CodesMatch(candidate.Code, localeCode))
                {
                    selected = candidate;
                    break;
                }
            }

            if (selected == null)
            {
                selected = locales[0];
            }

            if (selected == null)
            {
                return;
            }

            _localizationModel.CurrentLocale.Value = selected;
            PlayerPrefs.SetString(LOCALE_CODE_PREFS_KEY, selected.Code);
        }

        /// <summary>
        /// Resolves <paramref name="key"/> in the active locale, falling back to the default locale
        /// and finally to the key itself.
        /// </summary>
        public string Translate(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (_stringSource == null)
            {
                return key;
            }

            LocaleDefinition currentLocale = _localizationModel.CurrentLocale.Value;
            if (currentLocale != null && _stringSource.TryGetString(currentLocale.Code, key, out string value))
            {
                return value;
            }

            IReadOnlyList<LocaleDefinition> locales = _localizationModel.AvailableLocales;
            LocaleDefinition defaultLocale = locales.Count > 0 ? locales[0] : null;

            // Skipped when the active locale already is the default: re-asking the same table for a
            // key it just failed to answer can only fail again.
            bool defaultIsWorthTrying = defaultLocale != null
                && (currentLocale == null || !CodesMatch(defaultLocale.Code, currentLocale.Code));

            if (defaultIsWorthTrying
                && _stringSource.TryGetString(defaultLocale.Code, key, out string defaultValue))
            {
                return defaultValue;
            }

            return key;
        }

        /// <summary>Resolves a one-placeholder format entry, e.g. "{0}s".</summary>
        public string Format(string key, string argument0)
        {
            return string.Format(Translate(key), argument0);
        }

        /// <summary>Resolves a two-placeholder format entry.</summary>
        public string Format(string key, string argument0, string argument1)
        {
            return string.Format(Translate(key), argument0, argument1);
        }

        private static bool CodesMatch(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
