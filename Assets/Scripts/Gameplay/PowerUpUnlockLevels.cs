namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The level each <see cref="PowerUpKind"/> becomes available at. Ten authored numbers that change
    /// only when the roster does, so they live in code next to the enum they key off rather than in a
    /// ScriptableObject: there is nothing here for a designer to tune per scene or per build, and a
    /// switch cannot drift out of sync with the enum the way a hand-filled asset can.
    /// <para>
    /// The first three kinds are available from a fresh install; the remaining seven arrive one every
    /// five levels. Level 0 means "never gated" — a fresh install starts at level 1, so a gate of 0
    /// is unlocked before the player has done anything at all.
    /// </para>
    /// <para>
    /// This is a visibility and usability gate only. It is read at display time and at spend time and
    /// never written back, so it can neither grant nor take away a single held power-up: raising a
    /// gate above the player's level hides and refuses the kind, it never discards the inventory
    /// behind it (see <c>PowerUpModel</c>, which is persisted entirely separately).
    /// </para>
    /// </summary>
    public static class PowerUpUnlockLevels
    {
        /// <summary>Gate value meaning "available from the start".</summary>
        public const int ALWAYS_UNLOCKED = 0;

        /// <summary>The level <paramref name="kind"/> unlocks at, or <see cref="ALWAYS_UNLOCKED"/>.</summary>
        public static int LevelFor(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.Joker:
                    return 5;
                case PowerUpKind.ColorCleanser:
                    return 10;
                case PowerUpKind.Rotate:
                    return 15;
                case PowerUpKind.Reroll:
                    return 20;
                case PowerUpKind.DoubleMultiplier:
                    return 25;
                case PowerUpKind.GhostFit:
                    return 30;
                case PowerUpKind.CoinSower:
                    return 35;
                default:
                    // Bomb, RowClear and ColumnClear: the starter three, offered from the first run.
                    return ALWAYS_UNLOCKED;
            }
        }

        /// <summary>
        /// Whether <paramref name="kind"/> is available to a player whose progression frontier is
        /// <paramref name="currentLevelNumber"/> (<c>LevelProgressionModel.CurrentLevelNumber</c> — the
        /// linear frontier, not whichever level a Path run happens to be replaying).
        /// </summary>
        public static bool IsUnlockedAt(PowerUpKind kind, int currentLevelNumber)
            => currentLevelNumber >= LevelFor(kind);
    }
}
