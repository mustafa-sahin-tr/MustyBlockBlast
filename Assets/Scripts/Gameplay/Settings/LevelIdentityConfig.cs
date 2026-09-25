using System;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One level's identity on the level-start card (issue #464): its name and its icon. Content, not
    /// rules — which level asks for what stays in <see cref="LevelCatalog"/>; this is only what the
    /// level is called and how it looks. See <see cref="LevelIdentityCatalog"/>, which owns the list.
    /// </summary>
    [Serializable]
    public sealed class LevelIdentityConfig
    {
        [Tooltip("1-based level this row names. Must match a LevelCatalog level number.")]
        [SerializeField] private int _levelNumber = 1;

        [Tooltip("String Table key of the level's name (e.g. level.name.1).")]
        [SerializeField] private string _nameKey = string.Empty;

        [Tooltip("Full-colour square icon shown at the top of the level-start card.")]
        [SerializeField] private Sprite _icon;

        public int LevelNumber => _levelNumber;

        public string NameKey => _nameKey;

        /// <summary>The level's icon, or null when none is authored.</summary>
        public Sprite Icon => _icon;
    }
}
