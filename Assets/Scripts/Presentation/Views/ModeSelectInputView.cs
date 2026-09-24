using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Thin input adapter for the mode-select scene (issue #379): every pointer press is forwarded, with
    /// its screen position, to <see cref="ModeSelectPanelView.HandleTap"/>, which hit-tests it against
    /// the plates. Zero logic — what a tap means is the panel's and then <see cref="ModeSelectSystem"/>'s
    /// decision. The one <c>PlayerControls</c>-style owner in its scene, as <see cref="BoardInputView"/>
    /// is in gameplay and <see cref="SplashInputView"/> is on the splash.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModeSelectInputView : MonoBehaviour
    {
        private InputAction _pointerPressAction;

        private ModeSelectPanelView _panelView;

        [Inject]
        public void Construct(ModeSelectPanelView panelView)
        {
            _panelView = panelView;
        }

        private void Awake()
        {
            // Raw pointer state, read directly from the action: no GraphicRaycaster or EventSystem is
            // involved, so the scene needs neither and nothing drawn can swallow a press.
            _pointerPressAction = new InputAction("ModeSelectPress", InputActionType.Button, "<Pointer>/press");
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
            if (_panelView == null)
            {
                Debug.LogError(
                    $"{nameof(ModeSelectInputView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Read position straight off the pointer that generated this press, not a second, independently
            // enabled action: on Android a cross-action read of a same-event position could momentarily lag
            // a frame behind (e.g. a still-default (0,0) if this was the very first pointer event this
            // scene has seen), landing the hit-test outside the tapped button and dropping the first tap.
            Vector2 screenPosition = context.control.device is Pointer pointer
                ? pointer.position.ReadValue()
                : Vector2.zero;

            _panelView.HandleTap(screenPosition);
        }
    }
}
