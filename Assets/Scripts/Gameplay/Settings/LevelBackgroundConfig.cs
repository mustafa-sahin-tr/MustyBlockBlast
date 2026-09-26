using System;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One background image and the Path levels it covers (issue #523). Normally a group of ten
    /// levels; a row covering a single level is how one level would get its own image. See
    /// <see cref="LevelBackgroundCatalog"/>, which owns the list.
    /// </summary>
    [Serializable]
    public sealed class LevelBackgroundConfig
    {
        [Tooltip("First 1-based level this image covers (inclusive).")]
        [SerializeField] private int _firstLevel = 1;

        [Tooltip("Last 1-based level this image covers (inclusive).")]
        [SerializeField] private int _lastLevel = 10;

        [Tooltip("Full-screen portrait background, blur and veil already baked in. Shown with cover-fill.")]
        [SerializeField] private Sprite _background;

        public LevelBackgroundConfig()
        {
        }

        public LevelBackgroundConfig(int firstLevel, int lastLevel, Sprite background)
        {
            _firstLevel = firstLevel;
            _lastLevel = lastLevel;
            _background = background;
        }

        public int FirstLevel => _firstLevel;

        public int LastLevel => _lastLevel;

        /// <summary>The image, or null when none is authored yet.</summary>
        public Sprite Background => _background;

        public bool Covers(int levelNumber) => levelNumber >= _firstLevel && levelNumber <= _lastLevel;
    }
}
