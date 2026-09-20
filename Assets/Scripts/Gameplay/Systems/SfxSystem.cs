using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="SfxModel"/>. Plain C# — it never touches the Unity audio API. Playback requests
    /// are published and carried out by <c>SfxPlayerView</c>, which owns the AudioSource pool.
    /// <para>
    /// Loads the persisted mute flag on construction and writes it back on every change, so it must be
    /// resolved before any View subscribes to <see cref="SfxModel.IsMuted"/>.
    /// </para>
    /// </summary>
    public sealed class SfxSystem : ISfxService
    {
        private const string MUTED_PREFS_KEY = "Settings.SoundMuted";

        private readonly SfxModel _sfxModel;
        private readonly IPublisher<PlaySfxRequestedMessage> _playSfxPublisher;

        public SfxSystem(SfxModel sfxModel, IPublisher<PlaySfxRequestedMessage> playSfxPublisher)
        {
            _sfxModel = sfxModel;
            _playSfxPublisher = playSfxPublisher;

            // Default 0: a fresh install starts unmuted.
            _sfxModel.IsMuted.Value = PlayerPrefs.GetInt(MUTED_PREFS_KEY, 0) != 0;
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
            PlayerPrefs.SetInt(MUTED_PREFS_KEY, muted ? 1 : 0);
        }
    }
}
