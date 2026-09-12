using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Plays the end-of-run audio and stops the background loop. The impact SFX differs per
    /// <see cref="GameOverReason"/> so timing out reads differently from running out of moves; the
    /// musical sting plays either way.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameOverSfxView : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Played when the board has no legal moves left.")]
        [SerializeField] private AudioClip _noMovesLeftClip;

        [Tooltip("Played when the timed-mode clock reaches zero.")]
        [SerializeField] private AudioClip _timeUpClip;

        [Tooltip("Musical sting layered on top of the impact SFX, regardless of reason.")]
        [SerializeField] private AudioClip _stingClip;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<GameOverMessage> _gameOverSubscriber;
        private ISfxService _sfxService;
        private IMusicService _musicService;

        [Inject]
        public void Construct(
            ISubscriber<GameOverMessage> gameOverSubscriber, ISfxService sfxService, IMusicService musicService)
        {
            _gameOverSubscriber = gameOverSubscriber;
            _sfxService = sfxService;
            _musicService = musicService;
        }

        private void Start()
        {
            if (_gameOverSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(GameOverSfxView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _gameOverSubscriber.Subscribe(OnGameOver).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnGameOver(GameOverMessage message)
        {
            _musicService.Stop();

            AudioClip impactClip = message.Reason == GameOverReason.TimeUp ? _timeUpClip : _noMovesLeftClip;
            _sfxService.PlayOneShot(impactClip);
            _sfxService.PlayOneShot(_stingClip);
        }
    }
}
