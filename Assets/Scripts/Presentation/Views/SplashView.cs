using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Mtafasahin.MobileServices;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Plays the boot jingle once when the splash scene loads. Deliberately knows nothing about
    /// <see cref="SplashSystem"/>: audio must never influence splash timing or the transition.
    /// <para>
    /// Mute handling and the null-clip case both live in <see cref="ISfxService.PlayOneShot"/>, so
    /// this View is a single unconditional call.
    /// </para>
    /// </summary>
    // Runs after default-order scripts so SfxPlayerView.Start() has already subscribed by the time
    // the jingle request is published. Start order between MonoBehaviours is otherwise undefined,
    // which would drop the very first (and only) request.
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class SplashView : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("Optional boot jingle. Missing/null degrades silently to no sound.")]
        [SerializeField] private AudioClip _jingleClip;

        // Data seam only: intentionally never read from code. Holding the reference here is what
        // makes swapping the branding a pure Inspector edit instead of a code change. Any logic
        // that reads it would defeat that, so there is none.
        [Header("Visual")]
        [Tooltip("Splash logo target. Swap its sprite in the Inspector to rebrand — no code change.")]
        [SerializeField] private Image _logoImage;

        private ISfxService _sfxService;

        [Inject]
        public void Construct(ISfxService sfxService)
        {
            _sfxService = sfxService;
        }

        // Start, not Awake: SfxPlayerView builds its AudioSource pool in Awake and subscribes in
        // Start, so an Awake-time request would be published before anyone can play it.
        private void Start()
        {
            if (_sfxService == null)
            {
                Debug.LogError(
                    $"{nameof(SplashView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _sfxService.PlayOneShot(_jingleClip);
        }
    }
}
