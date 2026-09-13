namespace MustyBlockBlast.Gameplay.Localization
{
    /// <summary>
    /// One selectable language. Deliberately a plain C# class rather than Unity Localization's
    /// <c>Locale</c> asset: the Gameplay assembly stays free of the Localization package, so the
    /// locale-selection and fallback rules are unit-testable without Addressables.
    /// The Presentation layer maps the project's real Locale assets onto these.
    /// </summary>
    public sealed class LocaleDefinition
    {
        public LocaleDefinition(string code, string displayName)
        {
            Code = code ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName) ? Code : displayName;
        }

        /// <summary>Locale identifier code, e.g. "en". Matched case-insensitively when selecting.</summary>
        public string Code { get; }

        /// <summary>
        /// Name shown to the player, written in its own language (e.g. "Türkçe"). Unused until the
        /// language picker lands, but it is what makes this a Definition rather than a bare code.
        /// </summary>
        public string DisplayName { get; }
    }
}
