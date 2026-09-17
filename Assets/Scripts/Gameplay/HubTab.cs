namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// Which section of the hub screen is being looked at. The hub is the one place the four
    /// out-of-run screens live, so this is the whole of "where am I in the menus": every section is
    /// two taps from the board — the settings cog, then a tab.
    /// <para>
    /// Declaration order is display order in the tab bar, so moving a tab is a move here rather than a
    /// change to the layout code.
    /// </para>
    /// </summary>
    public enum HubTab
    {
        /// <summary>The default, and the one the settings cog lands on: the cog is the only way in, so
        /// the section it is named after has to be what opening it shows.</summary>
        Settings,

        PowerUpShop,

        Leaderboard,

        Profile,

        Badges,
    }
}
