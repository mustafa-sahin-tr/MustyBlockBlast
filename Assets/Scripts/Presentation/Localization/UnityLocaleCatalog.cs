using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace MustyBlockBlast.Presentation.Localization
{
    /// <summary>
    /// Projects the project's configured Locale assets onto the Gameplay layer's
    /// <see cref="LocaleDefinition"/> list. Adding a language is therefore a Locale asset plus its
    /// String Table column — no change to this class or to any View.
    /// </summary>
    internal static class UnityLocaleCatalog
    {
        /// <summary>
        /// The default and fallback language. Forced to index 0 of the built list, because both of
        /// <c>LocalizationSystem</c>'s fallbacks are defined as "index 0".
        /// </summary>
        private const string DEFAULT_LOCALE_CODE = "en";

        private const string DEFAULT_LOCALE_DISPLAY_NAME = "English";

        internal static IReadOnlyList<LocaleDefinition> Build()
        {
            var definitions = new List<LocaleDefinition>(4);

            // Called from the LifetimeScope's Configure, which is early enough that the locale list is
            // still empty unless initialization is forced first.
            UnityLocalizationBootstrap.EnsureInitialized();

            ILocalesProvider localesProvider =
                LocalizationSettings.HasSettings ? LocalizationSettings.AvailableLocales : null;
            IList<Locale> locales = localesProvider == null ? null : localesProvider.Locales;

            if (locales != null)
            {
                for (int localeIndex = 0; localeIndex < locales.Count; localeIndex++)
                {
                    Locale locale = locales[localeIndex];
                    if (locale == null)
                    {
                        continue;
                    }

                    var definition = new LocaleDefinition(locale.Identifier.Code, locale.LocaleName);

                    // English first, whatever order the Locale assets happen to be listed in.
                    if (definition.Code == DEFAULT_LOCALE_CODE)
                    {
                        definitions.Insert(0, definition);
                    }
                    else
                    {
                        definitions.Add(definition);
                    }
                }
            }

            if (definitions.Count == 0)
            {
                // Keeps the scene bootable and turns a missing Localization setup into one readable
                // console line instead of a container full of null locales. Labels will render their
                // own keys until the setup is fixed.
                Debug.LogError(
                    "No Locale assets are configured, so no language can be selected. "
                    + "Check Project Settings > Localization.");
                definitions.Add(new LocaleDefinition(DEFAULT_LOCALE_CODE, DEFAULT_LOCALE_DISPLAY_NAME));
            }

            return definitions;
        }
    }
}
