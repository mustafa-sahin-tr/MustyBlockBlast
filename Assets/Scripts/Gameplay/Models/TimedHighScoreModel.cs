using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The persisted best score for the timed round length currently in play. Deliberately separate
    /// from <see cref="ScoreModel.HighScore"/>, which stays the endless-mode best: timed difficulty
    /// depends on the round length, so the two are not comparable.
    /// <para>
    /// A single value rather than one per duration: <c>TimedHighScoreSystem</c> repoints it at the
    /// active duration's stored best when a run starts, so Views only ever need "the best that is
    /// relevant right now".
    /// </para>
    /// </summary>
    public sealed class TimedHighScoreModel
    {
        /// <summary>Best score for the active timed duration. Meaningless while in endless mode.</summary>
        public ReactiveProperty<int> Best { get; } = new ReactiveProperty<int>(0);
    }
}
