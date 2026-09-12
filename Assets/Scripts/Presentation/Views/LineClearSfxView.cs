using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Plays a layered SFX per <see cref="LinesClearedMessage.LineCount"/> tier, mirroring
    /// <see cref="LineClearBurstView"/>'s NICE/GREAT/AMAZING thresholds. Kept separate from the burst
    /// view so audio and visuals can be tuned independently. Every clear plays the base shatter; a
    /// multi-line clear layers an escalating chime and a voice call-out on top of it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LineClearSfxView : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Played on every clear, single line or not.")]
        [SerializeField] private AudioClip _lineClearClip;

        [Tooltip("Layered on top of the base clip when 2 or more lines clear at once.")]
        [SerializeField] private AudioClip _comboClip;

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
            _sfxService.PlayOneShot(_lineClearClip);

            AudioClip tierClip = ClipForLineCount(message.LineCount);
            if (tierClip == null)
            {
                return;
            }

            _sfxService.PlayOneShot(_comboClip);
            _sfxService.PlayOneShot(tierClip);
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
