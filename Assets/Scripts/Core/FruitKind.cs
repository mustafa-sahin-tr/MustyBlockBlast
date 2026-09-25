namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The fruits a Path level can ask the player to collect (issue #484) — the first set of nine, in the
    /// mockup's order. A fruit rides on a piece cell exactly as a diamond does and is stored in the same
    /// per-cell collectible id (see <see cref="Collectibles"/>). Members may be appended, never reordered:
    /// the value is part of a fruit's collectible id and of every authored level.
    /// </summary>
    public enum FruitKind
    {
        Pomegranate = 0,
        PassionFruit = 1,
        Peach = 2,
        Fig = 3,
        Coconut = 4,
        Mangosteen = 5,
        Guava = 6,
        Rambutan = 7,
        Avocado = 8,
    }
}
