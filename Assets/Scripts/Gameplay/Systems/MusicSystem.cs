using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using UnityEngine;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Plain C# — never touches the Unity audio API. Playback is carried out by
    /// <c>MusicPlayerView</c>, which owns the single looping AudioSource. Muting is handled reactively
    /// by that View subscribing to <see cref="Models.SfxModel.IsMuted"/> directly, so this System has
    /// no mute concept of its own: play/stop requests are unconditional.
    /// </summary>
    public sealed class MusicSystem : IMusicService
    {
        private readonly IPublisher<PlayMusicRequestedMessage> _playPublisher;
        private readonly IPublisher<StopMusicRequestedMessage> _stopPublisher;

        public MusicSystem(
            IPublisher<PlayMusicRequestedMessage> playPublisher,
            IPublisher<StopMusicRequestedMessage> stopPublisher)
        {
            _playPublisher = playPublisher;
            _stopPublisher = stopPublisher;
        }

        public void PlayLoop(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            _playPublisher.Publish(new PlayMusicRequestedMessage(clip));
        }

        public void Stop()
        {
            _stopPublisher.Publish(new StopMusicRequestedMessage());
        }
    }
}
