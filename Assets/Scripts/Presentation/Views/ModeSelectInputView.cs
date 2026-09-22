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
        private InputAction _pointerPositionAction;

        private ModeSelectPanelView _panelView;

        [Inject]
        public void Construct(ModeSelectPanelView panelView)
        {
            _panelView = panelView;
        }

        private void Awake()
        {
            // Raw pointer state, read directly from the actions: no GraphicRaycaster or EventSystem is
            // involved, so the scene needs neither and nothing drawn can swallow a press.
            _pointerPressAction = new InputAction("ModeSelectPress", InputActionType.Button, "<Pointer>/press");
            _pointerPositionAction = new InputAction("ModeSelectPosition", InputActionType.Value, "<Pointer>/position");
        }

        private void OnEnable()
        {
            _pointerPressAction.started += OnPressStarted;
            _pointerPressAction.Enable();
            _pointerPositionAction.Enable();
        }

        private void OnDisable()
        {
            _pointerPressAction.started -= OnPressStarted;
            _pointerPressAction.Disable();
            _pointerPositionAction.Disable();
        }

        private void OnDestroy()
        {
            _pointerPressAction?.Dispose();
            _pointerPositionAction?.Dispose();
        }

        private void OnPressStarted(InputAction.CallbackContext context)
        {
            if (_panelView == null)
            {
                Debug.LogError(
                    $"{nameof(ModeSelectInputView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _panelView.HandleTap(_pointerPositionAction.ReadValue<Vector2>());
        }
    }
}
