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
        /// The primary tracked objective — the first one — or null when nothing is tracked. A level may
        /// now ask for several objectives at once, so this is no longer "the objective": the HUD draws
        /// the whole of <see cref="TrackedObjectives"/>. Kept because a level's first row is still the
        /// one that carries the level's identity (its id is the unsuffixed <c>level_N</c>), and callers
        /// that genuinely mean "the primary one" should say so rather than index the list themselves.
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

        /// <summary>
        /// Makes <paramref name="objectives"/> the tracked set, replacing whatever was there. The
        /// multi-objective form of <see cref="SetCurrentObjective"/>, and replacing rather than
        /// appending for the same reason: a level's objectives must not carry on collecting placements
        /// once the player has moved past that level. A null or empty list clears the tracking, which
        /// the HUD reads as "hide".
        /// <para>
        /// Null entries are skipped rather than stored: everything downstream dereferences
        /// <see cref="ObjectiveProgress.Definition"/>, so one bad content row must not be able to put a
        /// hole in the list every consumer would then have to guard.
        /// </para>
        /// </summary>
        public void SetObjectives(IReadOnlyList<ObjectiveProgress> objectives)
        {
            _trackedObjectives.Clear();

            if (objectives == null)
            {
                return;
            }

            for (int objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
            {
                ObjectiveProgress progress = objectives[objectiveIndex];
                if (progress != null)
                {
                    _trackedObjectives.Add(progress);
                }
            }
        }
    }
}
