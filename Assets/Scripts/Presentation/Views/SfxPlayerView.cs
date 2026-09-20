using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using Mtafasahin.Reactive;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Owns a fixed pool of 2D <see cref="AudioSource"/>s and plays whatever
    /// <see cref="PlaySfxRequestedMessage"/> asks for. Sources are cycled round robin so overlapping
    /// requests in the same frame never allocate, never leak and rarely cut each other off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SfxPlayerView : MonoBehaviour
    {
        [Header("Pool")]
        [Tooltip("Number of AudioSources created in Awake. Also the maximum number of overlapping SFX.")]
        [SerializeField] private int _poolSize = 6;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<PlaySfxRequestedMessage> _playSfxSubscriber;
        private AudioSource[] _sources;
        private int _nextSourceIndex;

        [Inject]
        public void Construct(ISubscriber<PlaySfxRequestedMessage> playSfxSubscriber)
        {
            _playSfxSubscriber = playSfxSubscriber;
        }

        private void Awake()
        {
            int poolSize = Mathf.Max(1, _poolSize);
            _sources = new AudioSource[poolSize];

            for (int sourceIndex = 0; sourceIndex < poolSize; sourceIndex++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                _sources[sourceIndex] = source;
            }
        }

        private void Start()
        {
            if (_playSfxSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(SfxPlayerView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _playSfxSubscriber.Subscribe(OnPlaySfxRequested).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnPlaySfxRequested(PlaySfxRequestedMessage message)
        {
            AudioClip clip = message.Clip;
            if (clip == null)
            {
                return;
            }

            AudioSource source = _sources[_nextSourceIndex];
            _nextSourceIndex = (_nextSourceIndex + 1) % _sources.Length;
            source.PlayOneShot(clip);
        }
    }
}
