using UnityEngine;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>Published when a clip should start looping as background music.</summary>
    public readonly struct PlayMusicRequestedMessage
    {
        public PlayMusicRequestedMessage(AudioClip clip)
        {
            Clip = clip;
        }

        public AudioClip Clip { get; }
    }
}
