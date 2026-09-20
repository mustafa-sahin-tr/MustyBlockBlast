using MessagePipe;
using MustyBlockBlast.Core;
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
    /// flight only ever plays where <see cref="PowerUpInventoryView"/> is on screen. Also plays a cue
    /// specific to a newly spawned special board cell (Explosive Core, Vortex, Chain Lightning, Score
    /// Gem) on <see cref="SpecialCellSpawnedMessage"/>, matching <see cref="PowerUpGrantAnimationView"/>'s
    /// own expanded scope — a special cell is a "win" too, just not one that lands in the inventory
    /// strip. Mirrors <see cref="BonusSfxView"/>: a plain per-message one-shot, no queuing — overlapping
    /// grants simply overlap on <see cref="ISfxService"/>'s pooled sources, same as any other
    /// simultaneous cue.
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

        /// <summary>Every special cell kind a reward can spawn on the board (see
        /// <c>BoardSystem.PublishSpecialCellSpawned</c>'s callers), in declaration order.</summary>
        private static readonly SpecialCellKind[] SpecialCellKinds =
        {
            SpecialCellKind.ExplosiveCore,
            SpecialCellKind.Vortex,
            SpecialCellKind.ChainLightning,
            SpecialCellKind.ScoreGem,
        };

        [Header("Power-Up Clips")]
        [Tooltip("One clip per PowerUpKind, in the order: Bomb, Row Clear, Column Clear, Joker, Colour "
            + "Cleanser, Rotate, Reroll, Double Score, Ghost Fit, Coin Sower, Hold. A kind granted with "
            + "no entry here plays nothing and logs an editor warning.")]
        [SerializeField] private AudioClip[] _clips = new AudioClip[Kinds.Length];

        [Header("Special Cell Clips")]
        [Tooltip("One clip per SpecialCellKind, in the order: Explosive Core, Vortex, Chain Lightning, "
            + "Score Gem. A kind spawned with no entry here plays nothing and logs an editor warning.")]
        [SerializeField] private AudioClip[] _specialCellClips = new AudioClip[SpecialCellKinds.Length];

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<PowerUpGrantedMessage> _powerUpGrantedSubscriber;
        private ISubscriber<SpecialCellSpawnedMessage> _specialCellSpawnedSubscriber;
        private ISfxService _sfxService;

        [Inject]
        public void Construct(
            ISubscriber<PowerUpGrantedMessage> powerUpGrantedSubscriber,
            ISubscriber<SpecialCellSpawnedMessage> specialCellSpawnedSubscriber,
            ISfxService sfxService)
        {
            _powerUpGrantedSubscriber = powerUpGrantedSubscriber;
            _specialCellSpawnedSubscriber = specialCellSpawnedSubscriber;
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

            if (_specialCellSpawnedSubscriber != null)
            {
                _specialCellSpawnedSubscriber.Subscribe(OnSpecialCellSpawned).AddTo(_disposables);
            }
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

        private void OnSpecialCellSpawned(SpecialCellSpawnedMessage message)
        {
            AudioClip clip = ClipFor(message.Kind);
            if (clip == null)
            {
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

        /// <summary>No editor warning for a missing entry here, unlike <see cref="ClipFor(PowerUpKind)"/>
        /// — <see cref="SpecialCellSpawnedMessage"/> also fires for level-authored kinds (Laser, Coin)
        /// this view deliberately has no cue for, so a null return is an ordinary, expected outcome, not
        /// a content gap to flag.</summary>
        private AudioClip ClipFor(SpecialCellKind kind)
        {
            for (int kindIndex = 0; kindIndex < SpecialCellKinds.Length; kindIndex++)
            {
                if (SpecialCellKinds[kindIndex] != kind)
                {
                    continue;
                }

                return _specialCellClips != null && kindIndex < _specialCellClips.Length
                    ? _specialCellClips[kindIndex]
                    : null;
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
