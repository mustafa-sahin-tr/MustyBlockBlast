using MessagePipe;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Plays a one-shot cue specific to the granted <see cref="PowerUpKind"/> whenever a
    /// <see cref="PowerUpGrantedMessage"/> fires (issue #353) — every earning path (ad reward, level-up
    /// reward, badge, purchase) plays the sound, unlike <see cref="PowerUpGrantAnimationView"/>, whose
    /// flight only ever plays where <see cref="PowerUpInventoryView"/> is on screen. Mirrors
    /// <see cref="BonusSfxView"/>: a plain per-message one-shot, no queuing — overlapping grants simply
    /// overlap on <see cref="ISfxService"/>'s pooled sources, same as any other simultaneous cue.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowerUpGrantSfxView : MonoBehaviour
    {
        /// <summary>Every kind that can be granted (see <c>PowerUpSystem.Grant</c>), in declaration
        /// order, so a newly appended <see cref="PowerUpKind"/> has an obvious next slot here too.</summary>
        private static readonly PowerUpKind[] Kinds =
        {
            PowerUpKind.Bomb,
            PowerUpKind.RowClear,
            PowerUpKind.ColumnClear,
            PowerUpKind.Joker,
            PowerUpKind.ColorCleanser,
            PowerUpKind.Rotate,
            PowerUpKind.Reroll,
            PowerUpKind.DoubleMultiplier,
            PowerUpKind.GhostFit,
            PowerUpKind.CoinSower,
            PowerUpKind.Hold,
        };

        [Header("Clips")]
        [Tooltip("One clip per PowerUpKind, in the order: Bomb, Row Clear, Column Clear, Joker, Colour "
            + "Cleanser, Rotate, Reroll, Double Score, Ghost Fit, Coin Sower, Hold. A kind granted with "
            + "no entry here plays nothing and logs an editor warning.")]
        [SerializeField] private AudioClip[] _clips = new AudioClip[Kinds.Length];

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<PowerUpGrantedMessage> _powerUpGrantedSubscriber;
        private ISfxService _sfxService;

        [Inject]
        public void Construct(ISubscriber<PowerUpGrantedMessage> powerUpGrantedSubscriber, ISfxService sfxService)
        {
            _powerUpGrantedSubscriber = powerUpGrantedSubscriber;
            _sfxService = sfxService;
        }

        private void Start()
        {
            if (_powerUpGrantedSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(PowerUpGrantSfxView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _powerUpGrantedSubscriber.Subscribe(OnPowerUpGranted).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnPowerUpGranted(PowerUpGrantedMessage message)
        {
            AudioClip clip = ClipFor(message.Kind);
            if (clip == null)
            {
                LogMissingClipWarning(message.Kind);
                return;
            }

            _sfxService.PlayOneShot(clip);
        }

        private AudioClip ClipFor(PowerUpKind kind)
        {
            for (int kindIndex = 0; kindIndex < Kinds.Length; kindIndex++)
            {
                if (Kinds[kindIndex] != kind)
                {
                    continue;
                }

                return _clips != null && kindIndex < _clips.Length ? _clips[kindIndex] : null;
            }

            return null;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogMissingClipWarning(PowerUpKind kind)
        {
            Debug.LogWarning(
                $"{nameof(PowerUpGrantSfxView)} has no audio clip assigned for {kind}; skipping its grant sound.",
                this);
        }
    }
}
