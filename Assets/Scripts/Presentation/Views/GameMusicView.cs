using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using VContainer;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Starts the gameplay background loop once when the gameplay scene loads. Deliberately knows
    /// nothing about run state or game mode: <see cref="GameOverSfxView"/> is the one that stops it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameMusicView : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("Looping background track. Missing/null degrades silently to no music.")]
        [SerializeField] private AudioClip _bgmLoopClip;

        private IMusicService _musicService;

        [Inject]
        public void Construct(IMusicService musicService)
        {
            _musicService = musicService;
        }

        private void Start()
        {
            if (_musicService == null)
            {
                Debug.LogError(
                    $"{nameof(GameMusicView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _musicService.PlayLoop(_bgmLoopClip);
        }
    }
}
