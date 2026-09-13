namespace MustyBlockBlast.Gameplay.Localization
{
    /// <summary>
    /// Read-only view over the project's string tables, keyed by locale code and entry key.
    /// <para>
    /// This is the seam that keeps the Localization package out of the Gameplay assembly: the shipping
    /// implementation lives in Presentation and reads Unity's String Table Collection, while tests
    /// substitute an in-memory table. Implementations answer for one locale at a time and must not
    /// apply any fallback themselves — the fallback chain is <see cref="Systems.LocalizationSystem"/>'s
    /// job, so there is exactly one place it is defined and tested.
    /// </para>
    /// </summary>
    public interface ILocalizedStringSource
    {
        /// <summary>
        /// Looks up <paramref name="key"/> in <paramref name="localeCode"/> only. Returns false (with
        /// <paramref name="value"/> null) when the locale or the key is unknown, or the entry is empty.
        /// </summary>
        bool TryGetString(string localeCode, string key, out string value);
    }
}
