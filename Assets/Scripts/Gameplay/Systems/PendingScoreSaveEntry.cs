using System;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// One queued score as it sits on disk. The mode is stored as its name rather than its numeric
    /// value so reordering <see cref="GameMode"/> can never silently re-file a queued Endless score onto
    /// the Timed boards; a name that no longer parses is dropped on load rather than guessed at.
    /// <para>
    /// Shaped for <see cref="UnityEngine.JsonUtility"/>: public fields only, no properties.
    /// </para>
    /// </summary>
    [Serializable]
    internal sealed class PendingScoreSaveEntry
    {
        public string mode;

        public int score;
    }
}
