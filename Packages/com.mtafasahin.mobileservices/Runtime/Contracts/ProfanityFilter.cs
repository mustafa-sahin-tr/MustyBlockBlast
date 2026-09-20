using System;

namespace Mtafasahin.MobileServices
{
    /// <summary>
    /// Rejects display names carrying obviously offensive words. A deliberately small, local,
    /// substring-based screen — it is the cheap first gate in front of a name that will be shown to
    /// other players, not a moderation system.
    /// <para>
    /// STARTING POINT, NOT AN EXHAUSTIVE LIST. It catches the blunt cases and nothing else: it does not
    /// handle leetspeak, spacing tricks, or any language but English, and substring matching means it
    /// will occasionally reject an innocent name that happens to contain a blocked word. Both failure
    /// directions are accepted on purpose at this size — the real defence is a server-side list, which
    /// belongs behind the same <see cref="ProfileSystem.UpdateDisplayNameAsync"/> seam this sits in.
    /// </para>
    /// <para>
    /// Static rather than injected: a pure string predicate with no state, no configuration and no
    /// alternative implementation to swap in. If the list ever moves to a downloadable asset this
    /// becomes an interface behind the same call site.
    /// </para>
    /// </summary>
    public static class ProfanityFilter
    {
        /// <summary>
        /// Blocked substrings, lowercase. Matched case-insensitively against the whole candidate name.
        /// </summary>
        private static readonly string[] BlockedWords =
        {
            "anus",
            "arse",
            "bastard",
            "bitch",
            "bollock",
            "cock",
            "cunt",
            "dick",
            "fag",
            "fuck",
            "nigg",
            "penis",
            "piss",
            "prick",
            "pussy",
            "rape",
            "retard",
            "shit",
            "slut",
            "twat",
            "wank",
            "whore",
        };

        /// <summary>
        /// True when the name carries a blocked word. A null or empty name is not profane — emptiness is
        /// a separate validation failure and is rejected by the caller on its own terms.
        /// </summary>
        public static bool ContainsProfanity(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int wordIndex = 0; wordIndex < BlockedWords.Length; wordIndex++)
            {
                // IndexOf with an explicit ordinal-ignore-case comparison rather than ToLower(): it
                // allocates nothing, and it cannot be defeated by a culture whose casing rules differ
                // from the invariant one.
                if (text.IndexOf(BlockedWords[wordIndex], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
