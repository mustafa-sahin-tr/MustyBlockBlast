namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published by <c>BadgeSystem</c> the moment a badge latches unlocked. Carries the id only: the
    /// listener that needs more looks the badge up in the catalog by it, exactly as the save data does.
    /// Nothing is paid on this message — the coin reward is claimed later by a tap (issue #221).
    /// </summary>
    public readonly struct BadgeUnlockedMessage
    {
        public BadgeUnlockedMessage(string badgeId)
        {
            BadgeId = badgeId;
        }

        public string BadgeId { get; }
    }
}
