using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Plays a soft snap SFX on every legal piece placement, cleared lines or not.</summary>
    [DisallowMultipleComponent]
    public sealed class BlockPlaceSfxView : MonoBehaviour
    {
        [Header("Clips")]
        [SerializeField] private AudioClip _placeClip;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<PiecePlacedMessage> _piecePlacedSubscriber;
        private ISfxService _sfxService;

        [Inject]
        public void Construct(ISubscriber<PiecePlacedMessage> piecePlacedSubscriber, ISfxService sfxService)
        {
            _piecePlacedSubscriber = piecePlacedSubscriber;
            _sfxService = sfxService;
        }

        private void Start()
        {
            if (_piecePlacedSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(BlockPlaceSfxView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _piecePlacedSubscriber.Subscribe(OnPiecePlaced).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnPiecePlaced(PiecePlacedMessage message) => _sfxService.PlayOneShot(_placeClip);
    }
}
