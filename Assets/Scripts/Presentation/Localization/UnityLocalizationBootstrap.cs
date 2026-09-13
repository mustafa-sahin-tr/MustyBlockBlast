using UnityEngine;
using UnityEngine.Localization.Settings;

namespace MustyBlockBlast.Presentation.Localization
{
    /// <summary>
    /// Forces Unity Localization's Addressables-backed initialization to finish before anything reads
    /// a locale or a table.
    /// <para>
    /// Without this, <c>LocalizationSettings.AvailableLocales</c> reports zero locales and every table
    /// lookup returns null until initialization happens to complete on its own — which is exactly the
    /// window the LifetimeScope builds its container in. Blocking is the right trade here: the whole
    /// collection is a few dozen short strings, and the alternative is a frame of untranslated UI on
    /// every boot.
    /// </para>
    /// </summary>
    internal static class UnityLocalizationBootstrap
    {
        /// <summary>
        /// Safe and cheap to call repeatedly — an already-completed initialization handle returns
        /// immediately. Deliberately not memoised in a static bool: entering play mode with domain
        /// reload disabled would keep that flag true across a reset of the localization state.
        /// </summary>
        internal static void EnsureInitialized()
        {
            // Guarded rather than assumed: without a settings asset the property below creates one on
            // the fly, which would hide a broken setup behind an empty locale list.
            if (!LocalizationSettings.HasSettings)
            {
                Debug.LogError(
                    "No LocalizationSettings asset is active, so no string can be localized. "
                    + "Assign one under Project Settings > Localization.");
                return;
            }

            LocalizationSettings.InitializationOperation.WaitForCompletion();
        }
    }
}
