using System.Collections.Generic;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// Every level's name and icon (issue #464), looked up by level number. Kept apart from
    /// <see cref="LevelCatalog"/> on purpose: that catalog holds one row per <em>objective</em> (a level
    /// may have several), while a level has exactly one name and one icon.
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Level Identity Catalog", fileName = "LevelIdentityCatalog")]
    public sealed class LevelIdentityCatalog : ScriptableObject
    {
        [Tooltip("One row per level. Looked up by Level Number — list order does not matter.")]
        [SerializeField] private List<LevelIdentityConfig> _levels = new List<LevelIdentityConfig>();

        /// <summary>The identity of <paramref name="levelNumber"/>, or null when none is authored. A
        /// linear scan: the list is ~100 rows and is read once per card open.</summary>
        public LevelIdentityConfig Find(int levelNumber)
        {
            if (_levels == null)
            {
                return null;
            }

            for (int levelIndex = 0; levelIndex < _levels.Count; levelIndex++)
            {
                LevelIdentityConfig level = _levels[levelIndex];
                if (level != null && level.LevelNumber == levelNumber)
                {
                    return level;
                }
            }

            return null;
        }
    }
}
