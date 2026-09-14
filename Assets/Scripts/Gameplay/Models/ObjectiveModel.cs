using System.Collections.Generic;
using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The objectives currently being tracked. Seeding is left to the caller — in the running game
    /// that is <c>LevelProgressionSystem</c>, which sets the current level's objective through
    /// <see cref="SetCurrentObjective"/> (see docs/game-design.md, "Levels &amp; Objectives").
    /// </summary>
    public sealed class ObjectiveModel
    {
        private readonly List<ObjectiveProgress> _trackedObjectives = new List<ObjectiveProgress>();

        public IReadOnlyList<ObjectiveProgress> TrackedObjectives => _trackedObjectives;

        /// <summary>
        /// The one objective the HUD shows. The first tracked objective is the active one while the
        /// game runs a single objective at a time; null when nothing is tracked, which the HUD reads
        /// as "hide myself". How a level picks and advances between several objectives is content
        /// work, not model work, so that decision does not belong here yet.
        /// </summary>
        public ObjectiveProgress CurrentObjective
            => _trackedObjectives.Count > 0 ? _trackedObjectives[0] : null;

        /// <summary>
        /// Makes <paramref name="progress"/> the one tracked objective, replacing whatever was there.
        /// Replacing rather than appending is what keeps the previous level's objective from carrying
        /// on collecting placements after the player has moved past it — the game shows and tracks
        /// exactly one objective at a time. A null clears the tracking, which the HUD reads as "hide".
        /// </summary>
        public void SetCurrentObjective(ObjectiveProgress progress)
        {
            _trackedObjectives.Clear();

            if (progress != null)
            {
                _trackedObjectives.Add(progress);
            }
        }
    }
}
