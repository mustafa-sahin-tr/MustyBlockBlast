using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Plays a distinct SFX per <see cref="LinesClearedMessage.LineCount"/> tier, mirroring
    /// <see cref="LineClearBurstView"/>'s NICE/GREAT/AMAZING thresholds. Kept separate from the burst
    /// view so audio and visuals can be tuned independently. Single-line clears play nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LineClearSfxView : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Played when exactly 2 lines clear at once (\"NICE!\").")]
        [SerializeField] private AudioClip _niceClip;

        [Tooltip("Played when exactly 3 lines clear at once (\"GREAT!\").")]
        [SerializeField] private AudioClip _greatClip;

        [Tooltip("Played when 4 or more lines clear at once (\"AMAZING!\").")]
        [SerializeField] private AudioClip _amazingClip;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<LinesClearedMessage> _linesClearedSubscriber;
        private ISfxService _sfxService;

        [Inject]
        public void Construct(ISubscriber<LinesClearedMessage> linesClearedSubscriber, ISfxService sfxService)
        {
            _linesClearedSubscriber = linesClearedSubscriber;
            _sfxService = sfxService;
        }

        private void Start()
        {
            if (_linesClearedSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(LineClearSfxView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _linesClearedSubscriber.Subscribe(OnLinesCleared).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnLinesCleared(LinesClearedMessage message)
        {
            AudioClip clip = ClipForLineCount(message.LineCount);
            if (clip == null)
            {
                return;
            }

            _sfxService.PlayOneShot(clip);
        }

        private AudioClip ClipForLineCount(int lineCount)
        {
            if (lineCount == 2)
            {
                return _niceClip;
            }

            if (lineCount == 3)
            {
                return _greatClip;
            }

            if (lineCount >= 4)
            {
                return _amazingClip;
            }

            return null;
        }
    }
}
