using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The authored glyph for each <see cref="ObjectiveType"/>: full-colour illustrated art (issue
    /// #322), rendered as-is by the objective info card — no runtime tint, unlike
    /// <see cref="BadgeConfig.Icon"/>'s theme-tinted badge wall.
    /// <para>
    /// A type with no entry (or an entry with no sprite) is not an error: the views fall back to the
    /// procedural glyph they drew before any art existed, so authoring can lag behind new objective
    /// types without leaving a blank icon on the HUD.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Objective Icon Catalog", fileName = "ObjectiveIconCatalog")]
    public sealed class ObjectiveIconCatalog : ScriptableObject
    {
        [Serializable]
        private sealed class Entry
        {
            [Tooltip("The objective type this glyph stands for.")]
            [SerializeField] private ObjectiveType _type = ObjectiveType.SimultaneousLineClear;

            [Tooltip("Full-colour illustrated icon, rendered as-is (no runtime tint).")]
            [SerializeField] private Sprite _icon;

            internal ObjectiveType Type => _type;

            internal Sprite Icon => _icon;
        }

        [Tooltip("One glyph per objective type. First match wins; a type left out falls back to the " +
            "procedural glyph.")]
        [SerializeField] private List<Entry> _entries = new List<Entry>();

        /// <summary>The authored glyph for <paramref name="type"/>, or null when none is authored.</summary>
        public Sprite Find(ObjectiveType type)
        {
            if (_entries == null)
            {
                return null;
            }

            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
            {
                Entry entry = _entries[entryIndex];
                if (entry != null && entry.Type == type)
                {
                    return entry.Icon;
                }
            }

            return null;
        }
    }
}
