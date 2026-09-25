namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>How a Classic skin's cells break when a line clears them (issue #333).</summary>
    public enum ClassicSkinClearEffect
    {
        /// <summary>The ordinary clear animation — the plain colour blocks use it.</summary>
        None = 0,

        /// <summary>The jelly squashes and bursts into droplets of its own colour.</summary>
        JellySplat = 1,

        /// <summary>The fruit is sliced in two halves that fall apart, with juice droplets.</summary>
        FruitSlice = 2,

        /// <summary>The block cracks into splinters and a little dust.</summary>
        WoodSplinters = 3,

        /// <summary>The stone crumbles into tumbling rubble chunks.</summary>
        StoneCrumble = 4,

        /// <summary>The crystal shatters into spinning shards of its own colour.</summary>
        CrystalShatter = 5,
    }
}
