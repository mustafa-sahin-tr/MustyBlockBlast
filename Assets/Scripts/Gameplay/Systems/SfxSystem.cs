using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="SfxModel"/>. Plain C# — it never touches the Unity audio API. Playback requests
    /// are published and carried out by <c>SfxPlayerView</c>, which owns the AudioSource pool.
    /// </summary>
    public sealed class SfxSystem : ISfxService
    {
        private readonly SfxModel _sfxModel;
        private readonly IPublisher<PlaySfxRequestedMessage> _playSfxPublisher;

        public SfxSystem(SfxModel sfxModel, IPublisher<PlaySfxRequestedMessage> playSfxPublisher)
        {
            _sfxModel = sfxModel;
            _playSfxPublisher = playSfxPublisher;
        }

        public void PlayOneShot(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (_sfxModel.IsMuted.Value)
            {
                return;
            }

            _playSfxPublisher.Publish(new PlaySfxRequestedMessage(clip));
        }

        public void SetMuted(bool muted)
        {
            _sfxModel.IsMuted.Value = muted;
        }
    }
}
