using System.Collections.Generic;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The game's level content: an ordered list of authored <see cref="LevelObjectiveConfig"/> rows.
    /// This is the asset the developer opens to add, edit or retune levels before shipping — level
    /// content is deliberately not in C#.
    /// <para>
    /// Indexing contract: a level is identified by its <see cref="LevelObjectiveConfig.LevelNumber"/>,
    /// not by its position in the list. <see cref="Find"/> scans for the number, so reordering rows in
    /// the Inspector never changes which level is which; the list order is presentation only.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Level Catalog", fileName = "LevelCatalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        private static readonly LevelObjectiveConfig[] EmptyLevels = new LevelObjectiveConfig[0];

        [Tooltip("Authored levels. Identified by their Level Number field — list order is cosmetic.")]
        [SerializeField] private List<LevelObjectiveConfig> _levels = new List<LevelObjectiveConfig>();

        /// <summary>Every authored level, in list order. Never null.</summary>
        public IReadOnlyList<LevelObjectiveConfig> Levels => _levels ?? (IReadOnlyList<LevelObjectiveConfig>)EmptyLevels;

        /// <summary>
        /// Highest authored level number, or 0 when the catalog is empty. The progression clamps to
        /// this: the last level stays displayed rather than advancing into nothing.
        /// </summary>
        public int MaxLevelNumber
        {
            get
            {
                int max = 0;
                IReadOnlyList<LevelObjectiveConfig> levels = Levels;
                for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
                {
                    LevelObjectiveConfig level = levels[levelIndex];
                    if (level != null && level.LevelNumber > max)
                    {
                        max = level.LevelNumber;
                    }
                }

                return max;
            }
        }

        /// <summary>The level with this number, or null when none is authored. First match wins.</summary>
        public LevelObjectiveConfig Find(int levelNumber)
        {
            IReadOnlyList<LevelObjectiveConfig> levels = Levels;
            for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
            {
                LevelObjectiveConfig level = levels[levelIndex];
                if (level != null && level.LevelNumber == levelNumber)
                {
                    return level;
                }
            }

            return null;
        }

        /// <summary>
        /// Every row authored for this level number, in list order; empty when none is. A level may be
        /// authored as several rows — one per simultaneous objective — and their list order is what
        /// decides the index each objective's id is suffixed with, so it is part of the save contract
        /// rather than cosmetic. Reordering two rows of the same level swaps their ids; reordering rows
        /// of different levels still changes nothing.
        /// <para>
        /// <see cref="Find"/> remains the right lookup for anything that belongs to the level rather
        /// than to one of its objectives — the completion bonus and the level-up reward, which are paid
        /// once per level however many objectives it asks for.
        /// </para>
        /// </summary>
        public IReadOnlyList<LevelObjectiveConfig> FindAll(int levelNumber)
        {
            List<LevelObjectiveConfig> matches = null;

            IReadOnlyList<LevelObjectiveConfig> levels = Levels;
            for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
            {
                LevelObjectiveConfig level = levels[levelIndex];
                if (level == null || level.LevelNumber != levelNumber)
                {
                    continue;
                }

                // Allocated only once a match exists, so the common "level does not exist" answer costs
                // nothing and the shared empty array is handed back instead.
                matches ??= new List<LevelObjectiveConfig>(2);
                matches.Add(level);
            }

            return matches ?? (IReadOnlyList<LevelObjectiveConfig>)EmptyLevels;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-time feedback for the developer authoring levels: clamps the numeric fields and logs
        /// any entry that would throw when its <c>ObjectiveDefinition</c> is built, so a bad row is
        /// caught while editing rather than on the play session that reaches that level.
        /// </summary>
        private void OnValidate()
        {
            if (_levels == null)
            {
                return;
            }

            for (int levelIndex = 0; levelIndex < _levels.Count; levelIndex++)
            {
                LevelObjectiveConfig level = _levels[levelIndex];
                if (level == null)
                {
                    continue;
                }

                level.ValidateInEditor();
                if (!level.IsValid(out string error))
                {
                    Debug.LogWarning(
                        $"{nameof(LevelCatalog)} entry {levelIndex} (level {level.LevelNumber}) is invalid: {error}",
                        this);
                }
            }
        }
#endif
    }
}
