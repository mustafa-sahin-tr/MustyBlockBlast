using System.Collections.Generic;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The Path levels' background images (issue #488/#523): one image per group of levels, looked up
    /// by level number. Kept apart from <see cref="LevelIdentityCatalog"/> on purpose — that catalog is
    /// loaded by the level path's nodes, and full-screen textures do not belong in its memory.
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Level Background Catalog", fileName = "LevelBackgroundCatalog")]
    public sealed class LevelBackgroundCatalog : ScriptableObject
    {
        [Tooltip("One row per level range. When ranges overlap, the narrowest one wins, so a single-level "
            + "row overrides its group.")]
        [SerializeField] private List<LevelBackgroundConfig> _backgrounds = new List<LevelBackgroundConfig>();

        /// <summary>Replaces every row. For tests and editor tooling; the game only reads.</summary>
        public void SetBackgrounds(List<LevelBackgroundConfig> backgrounds)
        {
            _backgrounds = backgrounds ?? new List<LevelBackgroundConfig>();
        }

        /// <summary>
        /// The image for <paramref name="levelNumber"/>, or null when no row with an image covers it.
        /// A linear scan: ~10 rows, read only when the level or a setting changes.
        /// </summary>
        public Sprite Find(int levelNumber)
        {
            if (_backgrounds == null)
            {
                return null;
            }

            Sprite best = null;
            int bestSpan = int.MaxValue;
            for (int rowIndex = 0; rowIndex < _backgrounds.Count; rowIndex++)
            {
                LevelBackgroundConfig row = _backgrounds[rowIndex];
                if (row == null || row.Background == null || !row.Covers(levelNumber))
                {
                    continue;
                }

                int span = row.LastLevel - row.FirstLevel;
                if (span < bestSpan)
                {
                    best = row.Background;
                    bestSpan = span;
                }
            }

            return best;
        }

        /// <summary>
        /// What the screen background should show: the level's image only when the setting is on and a
        /// Path level is being played; null means "the theme gradient" — Endless, Classic, the setting
        /// off, no active level, or a level without an image.
        /// </summary>
        public Sprite Resolve(bool levelBackgroundsEnabled, GameMode mode, int activeLevelNumber)
        {
            if (!levelBackgroundsEnabled || mode != GameMode.Path || activeLevelNumber <= 0)
            {
                return null;
            }

            return Find(activeLevelNumber);
        }
    }
}
