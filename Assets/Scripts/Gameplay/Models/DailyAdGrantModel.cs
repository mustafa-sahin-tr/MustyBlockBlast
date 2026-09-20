using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// How many coin-rewarding ads the player may still watch today, and which local calendar day that
    /// count applies to (issue #257). Mutated exclusively by <see cref="Systems.CurrencySystem"/>, which
    /// both decrements it on a grant and resets it when the day rolls over.
    /// <para>
    /// Deliberately not a slice of <see cref="ProfileModel"/>. Everything on that model is account-bound —
    /// carried across a linked-account migration with the name and the coin balance — while this counter
    /// is device-local by design (issue #257's decision): a player who links a second device gets a fresh
    /// three ads there too, rather than sharing one daily allowance between devices. It sits beside
    /// <see cref="SettingsModel"/> for the same reason that model is not on <see cref="ProfileModel"/>
    /// either — both are "state that belongs to this install", not to the player's identity.
    /// </para>
    /// </summary>
    public sealed class DailyAdGrantModel
    {
        /// <summary>Coin-rewarding ads still available for <see cref="DayMarker"/>. Reset to the
        /// configured cap whenever the stored day no longer matches today.</summary>
        public ReactiveProperty<int> RemainingToday { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// The local calendar day <see cref="RemainingToday"/> was last reset for, as a sortable
        /// "yyyyMMdd" string. Empty until the first load or reset — an empty marker never matches a real
        /// day, so the very first read always rolls over into a fresh cap rather than reading as
        /// "exhausted".
        /// </summary>
        public ReactiveProperty<string> DayMarker { get; } = new ReactiveProperty<string>(string.Empty);
    }
}
