using UnityEngine;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>Published when a clip should be played fire-and-forget. Never published while muted.</summary>
    public readonly struct PlaySfxRequestedMessage
    {
        public PlaySfxRequestedMessage(AudioClip clip)
        {
            Clip = clip;
        }

        public AudioClip Clip { get; }
    }
}
