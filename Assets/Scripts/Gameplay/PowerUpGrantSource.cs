namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// Why a power-up entered the inventory, carried on <see cref="Messages.PowerUpGrantedMessage"/> so
    /// the grant fly-in can say it (issue #464): an earned reward reads "YOU WON!", a streak bonus reads
    /// "STREAK BONUS", and a purchase says neither.
    /// </summary>
    public enum PowerUpGrantSource
    {
        /// <summary>Earned: a level's reward, a badge, a rewarded ad.</summary>
        Reward,

        /// <summary>Bought with coins in the shop.</summary>
        Purchase,

        /// <summary>A rule-based bonus, such as the first-try streak (issue #464).</summary>
        StreakBonus,
    }
}
