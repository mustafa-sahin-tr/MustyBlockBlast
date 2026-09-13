namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Builds the persistence key for one power-up's inventory count. One key per kind, so adding a
    /// kind never migrates or invalidates the others' saved counts.
    /// </summary>
    internal static class PowerUpInventoryKey
    {
        private const string KEY_PREFIX = "PowerUp.Inventory.";

        /// <summary>Stable storage key for the given kind, e.g. <c>PowerUp.Inventory.Bomb</c>.</summary>
        public static string For(PowerUpKind kind) => KEY_PREFIX + kind;
    }
}
