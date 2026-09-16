using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Renders the three offered pieces in a card below the board. Reads <see cref="TrayModel"/>
    /// only; picking pieces up is the input View's job.
    /// <para>
    /// A slot whose piece carries a <see cref="SpecialPieceKind"/> is painted through
    /// <see cref="SpecialPieceVisuals"/> — gold for a golden 1x1, a glyph on an ordinary plate for the
    /// rocket and the hammer — which is the same seam the pocket and the drag ghost paint through, so
    /// one piece looks the same wherever it is.
    /// </para>
    /// <para>
    /// A slot is rebuilt from scratch whenever its piece changes, and the centring offsets are derived
    /// from that piece's own bounds each time — so a slot whose piece changed shape (the Rotate
    /// power-up swapping in another orientation) re-centres its new bounding box with no extra work.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PieceTrayView : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector2 _anchoredPosition = new Vector2(0f, -640f);
        [SerializeField] private Vector2 _cardSize = new Vector2(1000f, 300f);
        [SerializeField] private float _slotWidth = 300f;
        [SerializeField] private float _trayCellSize = 56f;
        [SerializeField] private float _trayCellSpacing = 5f;

        [Header("Cell style")]
        [SerializeField] private float _cellInset = 2f;
        [SerializeField] private float _cellBevelThickness = 6f;

        [Header("Rotate aim")]
        [Tooltip("Scale applied to the slot a Rotate is being aimed at, marking it as a live target.")]
        [SerializeField] private float _aimedSlotScale = 1.06f;

        private readonly List<CellView>[] _slotCells = new List<CellView>[TrayModel.SLOT_COUNT];
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        private RectTransform _rectTransform;
        private RectTransform[] _slotRects;

        /// <summary>Per slot: the child the piece's cells hang from. The aim highlight scales this
        /// rather than the slot itself, so lifting a slot never moves the rect
        /// <see cref="GetSlotIndexAt"/> hit-tests against and the target cannot shift under the finger
        /// that is aiming at it.</summary>
        private RectTransform[] _slotContentRects;

        /// <summary>The slot currently lifted as a Rotate target, or -1 for none.</summary>
        private int _aimedSlot = -1;

        /// <summary>The slot currently pulsing as a Ghost Fit suggestion, or -1 for none.</summary>
        private int _hintedSlot = -1;
        private Canvas _canvas;
        private TrayModel _trayModel;
        private SettingsModel _settingsModel;
        private ThemeDefinition _currentTheme;

        [Inject]
        public void Construct(TrayModel trayModel, SettingsModel settingsModel)
        {
            _trayModel = trayModel;
            _settingsModel = settingsModel;
        }

        private void Awake()
        {
            _rectTransform = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();

            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.sizeDelta = _cardSize;
            _rectTransform.anchoredPosition = _anchoredPosition;

            // No card visual behind the tray — the app background is enough, a separate tray card
            // just adds visual clutter. This is a plain layout container for the three slots.
            var cardObject = new GameObject("TrayLayout", typeof(RectTransform));
            var card = (RectTransform)cardObject.transform;
            card.SetParent(_rectTransform, false);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = _cardSize;
            card.anchoredPosition = Vector2.zero;

            BuildSlots(card);
        }

        private void Start()
        {
            if (_trayModel == null || _settingsModel == null)
            {
                Debug.LogError($"{nameof(PieceTrayView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            // Subscribed first so _currentTheme is set before the initial slot rebuild paints a cell.
            _settingsModel.CurrentTheme.Subscribe(OnThemeChanged).AddTo(_disposables);

            _trayModel.SlotChanged += OnSlotChanged;
            for (int i = 0; i < TrayModel.SLOT_COUNT; i++)
            {
                RebuildSlot(i);
            }
        }

        private void OnDestroy()
        {
            _disposables.Dispose();

            if (_trayModel != null)
            {
                _trayModel.SlotChanged -= OnSlotChanged;
            }
        }

        /// <summary>Slot index under a screen point, or -1.</summary>
        internal int GetSlotIndexAt(Vector2 screenPosition)
        {
            Camera eventCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            for (int i = 0; i < _slotRects.Length; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_slotRects[i], screenPosition, eventCamera))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Marks one dock slot as the live target of an armed Rotate, or -1 for none. Lifting the slot
        /// costs no extra object and no extra draw call — the same trick the armed power-up icon uses —
        /// and the lift is small enough that the widest piece the tray can hold still clears its
        /// neighbours.
        /// </summary>
        internal void SetAimedSlot(int slotIndex)
        {
            if (_aimedSlot == slotIndex || _slotContentRects == null)
            {
                return;
            }

            if (_aimedSlot >= 0)
            {
                _slotContentRects[_aimedSlot].localScale = Vector3.one;
            }

            _aimedSlot = slotIndex;

            if (_aimedSlot >= 0)
            {
                _slotContentRects[_aimedSlot].localScale =
                    new Vector3(_aimedSlotScale, _aimedSlotScale, 1f);
            }
        }

        /// <summary>
        /// Pulses one dock slot as the piece a Ghost Fit suggestion points at, or -1 for none.
        /// <paramref name="pulse"/> runs 0..1 and is driven by the caller's animation, so this stays a
        /// pure "draw it this big" instruction with no clock of its own.
        /// <para>
        /// Shares <see cref="SetAimedSlot"/>'s scale trick and, deliberately, its scale ceiling, so a
        /// lifted slot reads the same however it was lifted and still clears its neighbours at the
        /// widest piece. The two can never fight over the same slot: a suggestion is dismissed by the
        /// press that arms anything, so no Rotate can be aimed while one is up.
        /// </para>
        /// </summary>
        internal void SetHintedSlot(int slotIndex, float pulse)
        {
            if (_slotContentRects == null)
            {
                return;
            }

            if (_hintedSlot >= 0 && _hintedSlot != slotIndex)
            {
                _slotContentRects[_hintedSlot].localScale = Vector3.one;
            }

            _hintedSlot = slotIndex;

            if (_hintedSlot < 0)
            {
                return;
            }

            float scale = Mathf.Lerp(1f, _aimedSlotScale, Mathf.Clamp01(pulse));
            _slotContentRects[_hintedSlot].localScale = new Vector3(scale, scale, 1f);
        }

        internal void SetSlotVisible(int slotIndex, bool isVisible)
        {
            List<CellView> cells = _slotCells[slotIndex];
            for (int i = 0; i < cells.Count; i++)
            {
                cells[i].gameObject.SetActive(isVisible);
            }
        }

        private void BuildSlots(RectTransform card)
        {
            _slotRects = new RectTransform[TrayModel.SLOT_COUNT];
            _slotContentRects = new RectTransform[TrayModel.SLOT_COUNT];
            float pitch = _cardSize.x / TrayModel.SLOT_COUNT;
            float originX = (-_cardSize.x * 0.5f) + (pitch * 0.5f);

            for (int i = 0; i < TrayModel.SLOT_COUNT; i++)
            {
                var slotObject = new GameObject($"Slot_{i}", typeof(RectTransform));
                var slotRect = (RectTransform)slotObject.transform;
                slotRect.SetParent(card, false);
                slotRect.anchorMin = new Vector2(0.5f, 0.5f);
                slotRect.anchorMax = new Vector2(0.5f, 0.5f);
                slotRect.pivot = new Vector2(0.5f, 0.5f);
                slotRect.sizeDelta = new Vector2(_slotWidth, _cardSize.y - 40f);
                slotRect.anchoredPosition = new Vector2(originX + (i * pitch), 0f);

                var contentObject = new GameObject("Content", typeof(RectTransform));
                var contentRect = (RectTransform)contentObject.transform;
                contentRect.SetParent(slotRect, false);
                contentRect.anchorMin = new Vector2(0.5f, 0.5f);
                contentRect.anchorMax = new Vector2(0.5f, 0.5f);
                contentRect.pivot = new Vector2(0.5f, 0.5f);
                contentRect.sizeDelta = slotRect.sizeDelta;
                contentRect.anchoredPosition = Vector2.zero;

                _slotRects[i] = slotRect;
                _slotContentRects[i] = contentRect;
                _slotCells[i] = new List<CellView>(9);
            }
        }

        private void OnSlotChanged(int slotIndex) => RebuildSlot(slotIndex);

        /// <summary>Adopts a new theme: repaints the card and the pieces currently sitting in the
        /// tray, so a mid-run theme switch is not deferred until the next draw.</summary>
        private void OnThemeChanged(ThemeDefinition theme)
        {
            if (theme == null)
            {
                return;
            }

            _currentTheme = theme;

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                List<CellView> cells = _slotCells[slotIndex];
                if (cells == null || cells.Count == 0)
                {
                    continue;
                }

                int colourId = _trayModel.GetColourId(slotIndex);

                // Re-read rather than cached: a golden piece is painted gold instead of in the theme's
                // colours, so a repaint that ignored the kind would quietly demote it to an ordinary
                // block the next time the player switched theme.
                SpecialPieceKind specialKind = _trayModel.GetSpecialKind(slotIndex);
                for (int i = 0; i < cells.Count; i++)
                {
                    ApplyCellLook(cells[i], colourId, specialKind);
                }
            }
        }

        private void ApplyCellLook(CellView cell, int colourId, SpecialPieceKind specialKind)
        {
            if (_currentTheme == null)
            {
                return;
            }

            SpecialPieceVisuals.Apply(cell, specialKind, _currentTheme, colourId);
        }

        private void RebuildSlot(int slotIndex)
        {
            List<CellView> cells = _slotCells[slotIndex];
            for (int i = 0; i < cells.Count; i++)
            {
                Destroy(cells[i].gameObject);
            }

            cells.Clear();

            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                return;
            }

            int colourId = _trayModel.GetColourId(slotIndex);
            SpecialPieceKind specialKind = _trayModel.GetSpecialKind(slotIndex);
            PieceLayout.GetBounds(piece, out int width, out int height);

            float pitch = _trayCellSize + _trayCellSpacing;
            float offsetX = -((width - 1) * pitch) * 0.5f;
            float offsetY = -((height - 1) * pitch) * 0.5f;

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                GridPosition offset = piece.Offsets[i];
                CellView cell = CellFactory.CreateCell(
                    _slotContentRects[slotIndex],
                    $"TrayCell_{i}",
                    _trayCellSize,
                    _cellInset,
                    _cellBevelThickness);
                var rect = (RectTransform)cell.transform;
                rect.anchoredPosition = new Vector2(offsetX + (offset.X * pitch), offsetY + (offset.Y * pitch));
                ApplyCellLook(cell, colourId, specialKind);
                cells.Add(cell);
            }
        }
    }
}
