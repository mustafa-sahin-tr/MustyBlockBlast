using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Owns the single looping <see cref="AudioSource"/> for background music. Reacts to
    /// <see cref="SfxModel.IsMuted"/> directly by muting/unmuting the source in place, rather than
    /// stopping it — so unmuting resumes exactly where the track left off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicPlayerView : MonoBehaviour
    {
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<PlayMusicRequestedMessage> _playSubscriber;
        private ISubscriber<StopMusicRequestedMessage> _stopSubscriber;
        private SfxModel _sfxModel;
        private AudioSource _source;

        [Inject]
        public void Construct(
            ISubscriber<PlayMusicRequestedMessage> playSubscriber,
            ISubscriber<StopMusicRequestedMessage> stopSubscriber,
            SfxModel sfxModel)
        {
            _playSubscriber = playSubscriber;
            _stopSubscriber = stopSubscriber;
            _sfxModel = sfxModel;
        }

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f;
        }

        private void Start()
        {
            if (_playSubscriber == null || _stopSubscriber == null || _sfxModel == null)
            {
                Debug.LogError(
                    $"{nameof(MusicPlayerView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _playSubscriber.Subscribe(OnPlayRequested).AddTo(_disposables);
            _stopSubscriber.Subscribe(OnStopRequested).AddTo(_disposables);
            _sfxModel.IsMuted.Subscribe(OnMutedChanged).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnPlayRequested(PlayMusicRequestedMessage message)
        {
            if (message.Clip == null)
            {
                return;
            }

            _source.clip = message.Clip;
            _source.Play();
        }

        private void OnStopRequested(StopMusicRequestedMessage message)
        {
            _source.Stop();
            _source.clip = null;
        }

        private void OnMutedChanged(bool muted) => _source.mute = muted;
    }
}
