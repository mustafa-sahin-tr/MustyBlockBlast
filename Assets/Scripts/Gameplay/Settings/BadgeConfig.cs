using System;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One authored badge: the lifetime counter it watches, how high that counter must get, and how
    /// many coins the player may claim once it does. Inspector-editable rather than hardcoded, so the whole badge
    /// set can be retuned or extended as an asset edit — see <see cref="BadgeCatalog"/>, which owns
    /// the list of these.
    /// <para>
    /// Mirrors <see cref="BadgeDefinition"/>'s shape in Unity serialization terms; the immutable Core
    /// definition is built on demand by <see cref="ToBadgeDefinition"/>. The reward lives here rather
    /// than on the Core definition because it is content, not rule: the rule ("has the counter reached
    /// the threshold") is the whole of <see cref="BadgeDefinition"/>, and the payout is retuned as an
    /// asset edit without touching Core.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class BadgeConfig
    {
        [Tooltip("Stable identifier. This is what persistence stores for an unlocked badge — changing it "
            + "re-locks the badge for existing players, so retune the threshold instead.")]
        [SerializeField] private string _id = "badge";

        [Tooltip("Fallback name shown on the badge tile when Display Name Key is empty or has no "
            + "translation. Authored content, not a String Table key.")]
        [SerializeField] private string _displayName = "Badge";

        [Tooltip("String Table key of the tile name (e.g. badge.name.first_steps), so the badge reads in "
            + "the player's language. Leave empty to show Display Name as authored.")]
        [SerializeField] private string _displayNameKey = string.Empty;

        [Tooltip("White-on-transparent glyph shown on the badge tile, tinted at runtime from the theme. "
            + "Authored content like the name; a badge without one draws no glyph.")]
        [SerializeField] private Sprite _icon;

        [Tooltip("Which lifetime counter this badge watches.")]
        [SerializeField] private BadgeStatType _statType = BadgeStatType.TotalPiecesPlaced;

        [Tooltip("Value the counter must reach to unlock. Must be greater than zero.")]
        [SerializeField] private long _threshold = 1L;

        [Tooltip("Coins the player may claim once this badge unlocks. Claimed by tapping the badge, never "
            + "paid automatically. Zero means the badge is a trophy only and its tile never becomes tappable.")]
        [SerializeField] private int _coinReward = 0;

        /// <summary>Stable identifier; <see cref="BadgeCatalog"/> looks badges up by this, not by index.</summary>
        public string Id => _id;

        public string DisplayName => _displayName;

        /// <summary>String Table key of the tile name, or empty when the badge is named only by
        /// <see cref="DisplayName"/>.</summary>
        public string DisplayNameKey => _displayNameKey;

        /// <summary>The tile glyph, or null when none is authored.</summary>
        public Sprite Icon => _icon;

        public BadgeStatType StatType => _statType;

        public long Threshold => _threshold;

        /// <summary>Coins one claim of this badge pays. Never negative; zero means nothing to claim.</summary>
        public int CoinReward => _coinReward;

        /// <summary>
        /// Builds the immutable Core definition for this badge. Throws the same way
        /// <see cref="BadgeDefinition"/> does on an invalid threshold — see <see cref="IsValid"/> for a
        /// non-throwing check.
        /// </summary>
        public BadgeDefinition ToBadgeDefinition() => new BadgeDefinition(_id, _statType, _threshold);

        /// <summary>
        /// Whether <see cref="ToBadgeDefinition"/> would succeed and the result would be usable.
        /// Mirrors <see cref="BadgeDefinition"/>'s constructor guard, plus the id check the constructor
        /// cannot make: an unidentified badge cannot be persisted as unlocked, so it would re-grant its
        /// reward on every launch.
        /// </summary>
        public bool IsValid(out string error)
        {
            if (string.IsNullOrEmpty(_id))
            {
                error = "Id must not be empty — an unidentified badge cannot be persisted as unlocked.";
                return false;
            }

            if (_threshold <= 0L)
            {
                error = "Threshold must be greater than zero — a badge with a non-positive threshold would unlock instantly.";
                return false;
            }

            if (_coinReward < 0)
            {
                error = "Coin Reward must not be negative — a claim would fine the player.";
                return false;
            }

            error = null;
            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Called by <see cref="BadgeCatalog.OnValidate"/>: clamps the numeric fields so the developer
        /// authoring badges gets immediate feedback rather than a throw on the next play session. The
        /// id is left alone — silently inventing one would be worse than reporting the blank — and is
        /// surfaced as a console warning instead.
        /// </summary>
        internal void ValidateInEditor()
        {
            if (_threshold < 1L)
            {
                _threshold = 1L;
            }

            if (_coinReward < 0)
            {
                _coinReward = 0;
            }
        }
#endif
    }
}
