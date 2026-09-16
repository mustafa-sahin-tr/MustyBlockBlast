using System;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One authored hole cell on a level's board, in Unity serialization terms.
    /// <para>
    /// Exists because <see cref="GridPosition"/> is a Core type with get-only properties and no
    /// <see cref="SerializableAttribute"/> — Unity cannot serialize it, and giving it serialization
    /// attributes would drag a Unity concern into a pure-C# assembly. This is the authoring mirror,
    /// converted to the Core type by <see cref="ToGridPosition"/>, exactly as
    /// <c>LevelObjectiveConfig</c> mirrors <c>ObjectiveDefinition</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class BoardHoleCell
    {
        [Tooltip("Column, 0-based from the left.")]
        [SerializeField] private int _x;

        [Tooltip("Row, 0-based from the bottom.")]
        [SerializeField] private int _y;

        internal GridPosition ToGridPosition() => new GridPosition(_x, _y);
    }
}
