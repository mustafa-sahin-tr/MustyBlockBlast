using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Thin input adapter for <see cref="DebugCheatSystem"/>. Never compiled into a release build.
    /// Every binding here is ad hoc rather than a <c>PlayerControls</c> entry — these keys exist for
    /// development only and have no business in the shipped action asset.
    /// <para>
    /// Two independent tools live behind it:
    /// <list type="bullet">
    /// <item><b>F9</b> sets up the row+column cross-clear scenario (<see cref="DebugCheatSystem.SetupCrossClearScenario"/>).</item>
    /// <item><b>P</b> toggles freeform paint mode: <b>1</b>-<b>5</b> pick the active colour, <b>0</b>
    /// picks erase, dragging the pointer across the board toggles each cell it crosses — an empty cell
    /// is filled with the active colour, an already-filled one (any colour) is cleared, so painting and
    /// erasing are the one gesture — <b>A</b>/<b>S</b>/<b>D</b> step dock slots 1-3 through every
    /// piece shape in <see cref="PieceCatalog.AllPieces"/> (add Shift to step backwards) — and <b>G</b>
    /// steps the tracked objective through every <see cref="ObjectiveType"/> (add Shift to step
    /// backwards) via <see cref="DebugCheatSystem.CycleObjective"/> — so any board state, any offered
    /// shape and any objective, for any level, is reachable by hand.</item>
    /// </list>
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugCheatInputView : MonoBehaviour
    {
        private InputAction _crossClearAction;
        private InputAction _paintToggleAction;
        private InputAction _pointerPressAction;
        private InputAction _pointerPositionAction;
        private InputAction _shiftAction;
        private InputAction _objectiveCycleAction;
        private readonly InputAction[] _colourKeyActions = new InputAction[Board.COLOUR_COUNT + 1];
        private readonly InputAction[] _slotCycleActions = new InputAction[TrayModel.SLOT_COUNT];

        private DebugCheatSystem _debugCheatSystem;
        private BoardView _boardView;

        private bool _isPaintModeActive;
        private int _activePaintColourId = 1;

        /// <summary>The cell a paint stroke last toggled, so holding the pointer still over one cell
        /// does not flip it back and forth every frame — only entering a *new* cell toggles it, exactly
        /// as a stroke crossing a boundary once should.</summary>
        private GridPosition _lastToggledCell;
        private bool _hasLastToggledCell;

        [Inject]
        public void Construct(DebugCheatSystem debugCheatSystem, BoardView boardView)
        {
            _debugCheatSystem = debugCheatSystem;
            _boardView = boardView;
        }

        private void Awake()
        {
            _crossClearAction = new InputAction("DebugCrossClearSetup", InputActionType.Button, "<Keyboard>/f9");
            _paintToggleAction = new InputAction("DebugPaintToggle", InputActionType.Button, "<Keyboard>/p");
            _pointerPressAction = new InputAction("DebugPaintPress", InputActionType.Button, "<Pointer>/press");
            _pointerPositionAction =
                new InputAction("DebugPaintPointerPosition", InputActionType.Value, "<Pointer>/position");
            _shiftAction = new InputAction("DebugPaintShift", InputActionType.Button, "<Keyboard>/shift");
            _objectiveCycleAction = new InputAction("DebugObjectiveCycle", InputActionType.Button, "<Keyboard>/g");

            // Colour keys 0-5: 0 is erase, 1..Board.COLOUR_COUNT select a paint colour.
            for (int colourId = 0; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                _colourKeyActions[colourId] = new InputAction(
                    $"DebugPaintColour{colourId}", InputActionType.Button, $"<Keyboard>/{colourId}");
            }

            // A/S/D rather than F1-F3: the latter are OS/editor shortcuts on some platforms (F1 =
            // Help is the common one), and A/S/D sit right under the left hand next to the pointer.
            string[] slotKeys = { "a", "s", "d" };
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _slotCycleActions[slotIndex] = new InputAction(
                    $"DebugPaintSlot{slotIndex}Cycle", InputActionType.Button, $"<Keyboard>/{slotKeys[slotIndex]}");
            }
        }

        private void OnEnable()
        {
            _crossClearAction.performed += OnCrossClearPerformed;
            _crossClearAction.Enable();

            _paintToggleAction.performed += OnPaintTogglePerformed;
            _paintToggleAction.Enable();

            _pointerPressAction.Enable();
            _pointerPositionAction.Enable();

            _shiftAction.Enable();

            _objectiveCycleAction.performed += OnObjectiveCyclePerformed;
            _objectiveCycleAction.Enable();

            for (int colourId = 0; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                int capturedColourId = colourId;
                _colourKeyActions[colourId].performed += _ => OnColourKeyPerformed(capturedColourId);
                _colourKeyActions[colourId].Enable();
            }

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                int capturedSlotIndex = slotIndex;
                _slotCycleActions[slotIndex].performed += _ => OnSlotCyclePerformed(capturedSlotIndex);
                _slotCycleActions[slotIndex].Enable();
            }
        }

        private void OnDisable()
        {
            _crossClearAction.performed -= OnCrossClearPerformed;
            _crossClearAction.Disable();

            _paintToggleAction.performed -= OnPaintTogglePerformed;
            _paintToggleAction.Disable();

            _pointerPressAction.Disable();
            _pointerPositionAction.Disable();

            _shiftAction.Disable();

            _objectiveCycleAction.performed -= OnObjectiveCyclePerformed;
            _objectiveCycleAction.Disable();

            for (int colourId = 0; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                _colourKeyActions[colourId].Disable();
            }

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _slotCycleActions[slotIndex].Disable();
            }
        }

        private void OnDestroy()
        {
            _crossClearAction?.Dispose();
            _paintToggleAction?.Dispose();
            _pointerPressAction?.Dispose();
            _pointerPositionAction?.Dispose();
            _shiftAction?.Dispose();
            _objectiveCycleAction?.Dispose();

            for (int colourId = 0; colourId <= Board.COLOUR_COUNT; colourId++)
            {
                _colourKeyActions[colourId]?.Dispose();
            }

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _slotCycleActions[slotIndex]?.Dispose();
            }
        }

        // Continuous input (the pointer, while paint mode is held over the board) is read here rather
        // than from a callback, exactly as the InputView pattern requires: a callback only fires on a
        // press edge, and a paint stroke needs every frame the pointer stays down.
        private void Update()
        {
            if (!_isPaintModeActive || !_pointerPressAction.IsPressed())
            {
                _hasLastToggledCell = false;
                return;
            }

            Vector2 screenPosition = _pointerPositionAction.ReadValue<Vector2>();
            if (!_boardView.TryGetCell(screenPosition, out GridPosition cell))
            {
                return;
            }

            if (_hasLastToggledCell && cell.Equals(_lastToggledCell))
            {
                return;
            }

            _debugCheatSystem.ToggleCell(cell, _activePaintColourId);
            _lastToggledCell = cell;
            _hasLastToggledCell = true;

            // An erase writes the board through the same low-level path BoardModel.ClearAll uses, which
            // BoardView deliberately never repaints on its own — it holds the pre-clear look and waits
            // for whoever cleared it to start the fade (see ForceSweepPendingCells's doc). A fill needs
            // no such nudge: OnCellChanged's fill branch always repaints synchronously. Calling this
            // unconditionally is harmless either way — it is a no-op when nothing is pending.
            _boardView.ForceSweepPendingCells();
        }

        private void OnCrossClearPerformed(InputAction.CallbackContext context)
        {
            if (!EnsureInjected())
            {
                return;
            }

            _debugCheatSystem.SetupCrossClearScenario();

            // SetupCrossClearScenario wipes the whole board first; every cell outside the row/column it
            // refills afterwards is left pending exactly as a paint-mode erase is (see the same call in
            // Update()) — this claims those too.
            _boardView.ForceSweepPendingCells();
        }

        private void OnPaintTogglePerformed(InputAction.CallbackContext context)
        {
            _isPaintModeActive = !_isPaintModeActive;
            Debug.Log($"{nameof(DebugCheatInputView)}: paint mode {(_isPaintModeActive ? "ON" : "OFF")} "
                + $"(active colour {_activePaintColourId}).");
        }

        private void OnColourKeyPerformed(int colourId)
        {
            if (!_isPaintModeActive)
            {
                return;
            }

            _activePaintColourId = colourId;
            Debug.Log($"{nameof(DebugCheatInputView)}: active paint colour -> "
                + (colourId == Board.EMPTY ? "erase" : colourId.ToString()));
        }

        private void OnSlotCyclePerformed(int slotIndex)
        {
            if (!_isPaintModeActive || !EnsureInjected())
            {
                return;
            }

            bool forward = !_shiftAction.IsPressed();
            _debugCheatSystem.CycleTraySlotPiece(slotIndex, _activePaintColourId, forward);
        }

        private void OnObjectiveCyclePerformed(InputAction.CallbackContext context)
        {
            if (!_isPaintModeActive || !EnsureInjected())
            {
                return;
            }

            bool forward = !_shiftAction.IsPressed();
            _debugCheatSystem.CycleObjective(forward);
        }

        private bool EnsureInjected()
        {
            if (_debugCheatSystem != null && _boardView != null)
            {
                return true;
            }

            Debug.LogError(
                $"{nameof(DebugCheatInputView)} was not injected. Is it registered in the LifetimeScope?", this);
            return false;
        }
    }
#endif
}
