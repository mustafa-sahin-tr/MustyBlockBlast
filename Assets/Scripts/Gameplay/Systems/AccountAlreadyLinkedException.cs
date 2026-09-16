using System;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Thrown when a platform credential is already attached to a different backend identity. Exists as
    /// its own type — rather than as a flag on a generic auth failure — because it is the one link error
    /// with a real answer for the player ("that Apple ID already owns another save"), so callers can
    /// catch it and say so instead of showing the same "something went wrong" retry as a dropped
    /// connection. Plain C# for the same reason <see cref="IAuthService"/> is: the Unity Gaming Services
    /// exception it wraps never crosses the seam.
    /// </summary>
    public sealed class AccountAlreadyLinkedException : Exception
    {
        /// <summary>
        /// Which credential was rejected, e.g. "Apple" or "GooglePlayGames". A string rather than an enum
        /// so adding a provider stays a one-line change in the service that knows about it.
        /// </summary>
        public string Provider { get; }

        public AccountAlreadyLinkedException(string provider, Exception innerException)
            : this(provider, BuildMessage(provider), innerException)
        {
        }

        public AccountAlreadyLinkedException(string provider, string message, Exception innerException)
            : base(message, innerException)
        {
            Provider = provider ?? string.Empty;
        }

        private static string BuildMessage(string provider) =>
            $"The {provider} credential is already linked to a different player account.";
    }
}
