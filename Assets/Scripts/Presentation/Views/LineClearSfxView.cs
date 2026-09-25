using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using VContainer;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Plays a layered SFX per <see cref="LinesClearedMessage.LineCount"/> tier, mirroring
    /// <see cref="LineClearBurstView"/>'s NICE/GREAT/AMAZING thresholds. Kept separate from the burst
    /// view so audio and visuals can be tuned independently. Every clear plays the base shatter; a
    /// multi-line clear layers an escalating chime and a voice call-out on top of it.
    /// <para>
    /// The tier voice ("NICE!"/"GREAT!"/"AMAZING!") is deliberately skipped when the same placement
    /// also grants a power-up or spawns a special board cell — those already have their own win sound
    /// (<see cref="PowerUpGrantSfxView"/>), and hearing both at once buries the rarer, more specific
    /// cue under the common one. <c>BoardSystem.TryPlacePiece</c> publishes any grant/spawn message
    /// strictly after <see cref="LinesClearedMessage"/>, in the same synchronous call, so the tier
    /// voice's own playback is deferred by one frame to give a same-placement grant a chance to flag
    /// itself first — see <see cref="OnLinesCleared"/>.
    /// </para>
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
        private ISubscriber<PowerUpGrantedMessage> _powerUpGrantedSubscriber;
        private ISubscriber<SpecialCellSpawnedMessage> _specialCellSpawnedSubscriber;
        private ISfxService _sfxService;

        /// <summary>The board (issue #333): asked which Classic skin the cleared blocks wear, so a skin with
        /// its own clear sound plays it in place of <see cref="_lineClearClip"/>.</summary>
        private BoardView _boardView;

        private CancellationToken _destroyToken;
        private bool _grantedOrSpawnedThisPlacement;

        [Inject]
        public void Construct(
            ISubscriber<LinesClearedMessage> linesClearedSubscriber,
            ISubscriber<PowerUpGrantedMessage> powerUpGrantedSubscriber,
            ISubscriber<SpecialCellSpawnedMessage> specialCellSpawnedSubscriber,
            ISfxService sfxService,
            BoardView boardView)
        {
            _boardView = boardView;
            _linesClearedSubscriber = linesClearedSubscriber;
            _powerUpGrantedSubscriber = powerUpGrantedSubscriber;
            _specialCellSpawnedSubscriber = specialCellSpawnedSubscriber;
            _sfxService = sfxService;
        }

        private void Awake() => _destroyToken = this.GetCancellationTokenOnDestroy();

        private void Start()
        {
            if (_linesClearedSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(LineClearSfxView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _linesClearedSubscriber.Subscribe(OnLinesCleared).AddTo(_disposables);

            if (_powerUpGrantedSubscriber != null)
            {
                _powerUpGrantedSubscriber.Subscribe(_ => _grantedOrSpawnedThisPlacement = true).AddTo(_disposables);
            }

            if (_specialCellSpawnedSubscriber != null)
            {
                _specialCellSpawnedSubscriber.Subscribe(_ => _grantedOrSpawnedThisPlacement = true)
                    .AddTo(_disposables);
            }
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnLinesCleared(LinesClearedMessage message)
        {
            _sfxService.PlayOneShot(BaseClearClip(message));

            AudioClip tierClip = ClipForLineCount(message.LineCount);
            if (tierClip == null)
            {
                return;
            }

            _sfxService.PlayOneShot(_comboClip);
            PlayTierVoiceUnlessGrantedAsync(tierClip).Forget();
        }

        /// <summary>
        /// Waits one frame so a grant/spawn message this same placement is about to publish (always
        /// strictly after <see cref="LinesClearedMessage"/>, in the same synchronous call) has a chance
        /// to set <see cref="_grantedOrSpawnedThisPlacement"/> before the tier voice would play.
        /// </summary>
        /// <summary>The clear's base sound: that of the Classic skin most of the cleared blocks were placed
        /// in (jelly squish, fruit slice, wood crack…) when it has one, otherwise the ordinary line-clear
        /// clip. The combo and tier voices are layered on top either way.</summary>
        private AudioClip BaseClearClip(LinesClearedMessage message)
        {
            AudioClip skinClip = _boardView != null ? _boardView.SkinClearSoundFor(message.Rows, message.Columns) : null;
            return skinClip != null ? skinClip : _lineClearClip;
        }

        private async UniTaskVoid PlayTierVoiceUnlessGrantedAsync(AudioClip tierClip)
        {
            _grantedOrSpawnedThisPlacement = false;

            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, _destroyToken);
            }
            catch (System.OperationCanceledException)
            {
                // The view was destroyed before the next frame — nothing left to play.
                return;
            }

            bool suppress = _grantedOrSpawnedThisPlacement;
            _grantedOrSpawnedThisPlacement = false;

            if (suppress)
            {
                return;
            }

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
