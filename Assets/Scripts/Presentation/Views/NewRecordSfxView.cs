using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Plays a one-shot clip the moment a new high score is set mid-run.</summary>
    [DisallowMultipleComponent]
    public sealed class NewRecordSfxView : MonoBehaviour
    {
        [Header("Clip")]
        [Tooltip("Played once, the moment the player's score first exceeds their previous best.")]
        [SerializeField] private AudioClip _newRecordClip;

        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private ISubscriber<NewRecordMessage> _newRecordSubscriber;
        private ISfxService _sfxService;

        [Inject]
        public void Construct(ISubscriber<NewRecordMessage> newRecordSubscriber, ISfxService sfxService)
        {
            _newRecordSubscriber = newRecordSubscriber;
            _sfxService = sfxService;
        }

        private void Start()
        {
            if (_newRecordSubscriber == null)
            {
                Debug.LogError(
                    $"{nameof(NewRecordSfxView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _newRecordSubscriber.Subscribe(OnNewRecord).AddTo(_disposables);
        }

        private void OnDestroy() => _disposables.Dispose();

        private void OnNewRecord(NewRecordMessage message) => _sfxService.PlayOneShot(_newRecordClip);
    }
}
