using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>Looping background music playback. Injected wherever a feature needs to start or stop the score.</summary>
    public interface IMusicService
    {
        /// <summary>Starts looping <paramref name="clip"/>, replacing whatever was playing. Null clip is a no-op.</summary>
        void PlayLoop(AudioClip clip);

        /// <summary>Stops whatever is currently looping.</summary>
        void Stop();
    }
}
