namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The one id space for things a Path player collects off pieces (issue #484), shared by diamonds and
    /// fruits so both ride the same path — piece decoration, <see cref="Board"/> storage (and so Undo),
    /// and counting on every destruction path. Ids <c>1..Board.COLOUR_COUNT</c> are diamonds of that gem
    /// colour, exactly as before fruits existed; <see cref="FRUIT_FIRST_ID"/> onwards are fruits, one per
    /// <see cref="FruitKind"/>; 0 is "none". A per-id tally is <see cref="TALLY_LENGTH"/> long.
    /// </summary>
    public static class Collectibles
    {
        /// <summary>The collectible id of <see cref="FruitKind.Pomegranate"/>; the others follow in enum
        /// order. Well clear of the diamond colours, so the palette can grow without a collision.</summary>
        public const int FRUIT_FIRST_ID = 16;

        /// <summary>How many fruits exist — every <see cref="FruitKind"/>.</summary>
        public const int FRUIT_COUNT = 9;

        /// <summary>Length of a tally indexed by collectible id: every diamond colour and every fruit.</summary>
        public const int TALLY_LENGTH = FRUIT_FIRST_ID + FRUIT_COUNT;

        public static bool IsDiamond(int id) => id >= 1 && id <= Board.COLOUR_COUNT;

        public static bool IsFruit(int id) => id >= FRUIT_FIRST_ID && id < FRUIT_FIRST_ID + FRUIT_COUNT;

        /// <summary>Whether <paramref name="id"/> is any collectible — a diamond or a fruit.</summary>
        public static bool IsValid(int id) => IsDiamond(id) || IsFruit(id);

        public static int FruitId(FruitKind fruit) => FRUIT_FIRST_ID + (int)fruit;

        /// <summary>The fruit a fruit id stands for. Only meaningful when <see cref="IsFruit"/>.</summary>
        public static FruitKind FruitOf(int id) => (FruitKind)(id - FRUIT_FIRST_ID);

        /// <summary>Counts one collectible of <paramref name="id"/> into a <see cref="TALLY_LENGTH"/>-long
        /// tally; ignores 0 and anything outside the id space.</summary>
        public static void Increment(int[] tally, int id)
        {
            if (IsValid(id) && id < tally.Length)
            {
                tally[id]++;
            }
        }
    }
}
