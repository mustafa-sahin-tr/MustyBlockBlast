using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The decorative overlay sprite for each <see cref="CellSkinKind"/> (issue #324, sub-issue A).
    /// Mirrors <see cref="ObjectiveIconCatalog"/> exactly: one small catalog rather than per-kind
    /// serialized fields on every View that needs the art, because both <c>BoardView</c> (board cells)
    /// and <c>PieceTrayView</c>/<c>HoldSlotView</c> (dock/pocket cells) need the same four sprites.
    /// <para>
    /// A kind with no entry (or an entry with no sprite) is not an error: the skin still exists in the
    /// model (for a future objective to read — see the issue's "Gelecek bağımlılık" note), it simply
    /// draws no overlay, so authoring art can lag behind the feature without breaking anything.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Cell Skin Icon Catalog", fileName = "CellSkinIconCatalog")]
    public sealed class CellSkinIconCatalog : ScriptableObject
    {
        [Serializable]
        private sealed class Entry
        {
            [Tooltip("The skin this decorative overlay stands for.")]
            [SerializeField] private CellSkinKind _kind = CellSkinKind.Cake;

            [Tooltip("Decorative overlay sprite, drawn over the cell's flat colour face.")]
            [SerializeField] private Sprite _overlay;

            internal CellSkinKind Kind => _kind;

            internal Sprite Overlay => _overlay;
        }

        [Tooltip("One overlay sprite per skin kind. First match wins; a kind left out draws no overlay.")]
        [SerializeField] private List<Entry> _entries = new List<Entry>();

        /// <summary>The authored overlay for <paramref name="kind"/>, or null when none is authored or
        /// <paramref name="kind"/> is <see cref="CellSkinKind.None"/>.</summary>
        public Sprite Find(CellSkinKind kind)
        {
            if (kind == CellSkinKind.None || _entries == null)
            {
                return null;
            }

            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
            {
                Entry entry = _entries[entryIndex];
                if (entry != null && entry.Kind == kind)
                {
                    return entry.Overlay;
                }
            }

            return null;
        }
    }
}
