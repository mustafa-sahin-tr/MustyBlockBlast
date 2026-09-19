namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// Maps a level number to its <see cref="LevelPathZoneKind"/>. A fixed ten-level interval cycling
    /// through the four zones — not authored per level and not derived from
    /// <see cref="MustyBlockBlast.Gameplay.Settings.ThemeDefinition"/> — so the path reads as "somewhere
    /// new every ten levels" without any content authoring, and repeats forever for the endless
    /// catalog.
    /// </summary>
    public static class LevelPathZones
    {
        /// <summary>How many consecutive levels share one zone before the cycle advances.</summary>
        public const int LEVELS_PER_ZONE = 10;

        private const int ZONE_COUNT = 4;

        /// <summary>The zone level <paramref name="levelNumber"/> (1-based) falls in.</summary>
        public static LevelPathZoneKind ZoneFor(int levelNumber)
        {
            int levelIndex = levelNumber - 1;
            if (levelIndex < 0)
            {
                levelIndex = 0;
            }

            int zoneIndex = (levelIndex / LEVELS_PER_ZONE) % ZONE_COUNT;
            return (LevelPathZoneKind)zoneIndex;
        }
    }
}
