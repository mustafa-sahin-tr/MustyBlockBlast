using UnityEngine;

namespace Mtafasahin.MobileServices
{
    /// <summary>Fire-and-forget sound effect playback. Injected wherever a feature needs to make noise.</summary>
    public interface ISfxService
    {
        /// <summary>Plays <paramref name="clip"/> without blocking. Null clips and muted state are no-ops.</summary>
        void PlayOneShot(AudioClip clip);

        /// <summary>Mutes or unmutes all future play requests. Already playing clips are unaffected.</summary>
        void SetMuted(bool muted);
    }
}
