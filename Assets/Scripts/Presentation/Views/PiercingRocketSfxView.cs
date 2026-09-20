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
    /// Plays a one-shot cue whenever a <see cref="MustyBlockBlast.Core.SpecialPieceKind.PiercingRocket"/>
    /// wipe fires, so the row-and-column sweep is announced rather than just happening. Mirrors
    /// <see cref="BonusSfxView"/>: one flat cue, no tiering.
    /// <para>
    /// Audio only, deliberately. <see cref="PiercingRocketFiredMessage"/> carries a count and no
    /// positions, so there is nothing here to site a burst on — the cells the wipe emptied are already
    /// repainted by <c>BoardView</c>, which is told about them through the board model's cleared-cell
    /// notification, and that fade is the wipe's visual. A louder, sited effect would need the message
    /// to carry the wiped cells; see the note on this View's registration.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PiercingRocketSfxView : MonoBehaviour
    {
        [Header("Clip")]
        [Tooltip("Played once whenever a Piercing Rocket's wipe empties at least one cell.")]
        [SerializeField] private AudioClip _rocketClip;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<PiercingRocketFiredMessage> _rocketFiredSubscriber;
        private ISfxService _sfxService;

        [Inject]
        public void Construct(
            ISubscriber<PiercingRocketFiredMessage> rocketFiredSubscriber, ISfxService sfxService)
        {
            _rocketFiredSubscriber = rocketFiredSubscriber;
            _sfxService = sfxService;
        }

        private void Start()
        {
            if (_rocketFiredSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(PiercingRocketSfxView)} was not injected. Is it registered in the LifetimeScope?",
                    this);
                return;
            }

            _rocketFiredSubscriber.Subscribe(OnRocketFired).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        // The message is only published when the wipe actually emptied something, so there is no
        // zero-count case to filter out here.
        private void OnRocketFired(PiercingRocketFiredMessage message) => _sfxService.PlayOneShot(_rocketClip);
    }
}
