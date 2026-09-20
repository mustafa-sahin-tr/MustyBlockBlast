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
    /// Plays a one-shot clip whenever any scoring bonus fires (#61) — a single flat cue, not attributed
    /// to which specific bonus rule(s) contributed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BonusSfxView : MonoBehaviour
    {
        [Header("Clip")]
        [Tooltip("Played once whenever a placement's combined bonus is greater than zero.")]
        [SerializeField] private AudioClip _bonusClip;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<BonusScoredMessage> _bonusScoredSubscriber;
        private ISfxService _sfxService;

        [Inject]
        public void Construct(ISubscriber<BonusScoredMessage> bonusScoredSubscriber, ISfxService sfxService)
        {
            _bonusScoredSubscriber = bonusScoredSubscriber;
            _sfxService = sfxService;
        }

        private void Start()
        {
            if (_bonusScoredSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(BonusSfxView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _bonusScoredSubscriber.Subscribe(OnBonusScored).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnBonusScored(BonusScoredMessage message) => _sfxService.PlayOneShot(_bonusClip);
    }
}
