using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Thin input adapter for the splash scene: any pointer press anywhere on screen asks
    /// <see cref="SplashSystem"/> to skip the wait. Zero logic — whether a skip is still allowed is
    /// the System's decision.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SplashInputView : MonoBehaviour
    {
        private InputAction _pointerPressAction;

        private SplashSystem _splashSystem;

        [Inject]
        public void Construct(SplashSystem splashSystem)
        {
            _splashSystem = splashSystem;
        }

        private void Awake()
        {
            // Raw pointer state, read directly from the action: no Canvas or GraphicRaycaster is
            // involved, so a press anywhere on screen counts regardless of what is drawn there.
            _pointerPressAction = new InputAction("SplashSkip", InputActionType.Button, "<Pointer>/press");
        }

        private void OnEnable()
        {
            _pointerPressAction.started += OnPressStarted;
            _pointerPressAction.Enable();
        }

        private void OnDisable()
        {
            _pointerPressAction.started -= OnPressStarted;
            _pointerPressAction.Disable();
        }

        private void OnDestroy()
        {
            _pointerPressAction?.Dispose();
        }

        private void OnPressStarted(InputAction.CallbackContext context)
        {
            if (_splashSystem == null)
            {
                Debug.LogError(
                    $"{nameof(SplashInputView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _splashSystem.RequestSkip();
        }
    }
}
