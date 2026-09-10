using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Renders the three offered pieces in a card below the board. Reads <see cref="TrayModel"/>
    /// only; picking pieces up is the input View's job.
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
        [SerializeField] private float _cellBevelThickness = 4f;

        [Header("Palette")]
        [SerializeField] private BlockPalette _palette;

        private readonly List<CellView>[] _slotCells = new List<CellView>[TrayModel.SLOT_COUNT];

        private RectTransform _rectTransform;
        private RectTransform[] _slotRects;
        private Canvas _canvas;
        private TrayModel _trayModel;

        [Inject]
        public void Construct(TrayModel trayModel)
        {
            _trayModel = trayModel;
        }

        private void Awake()
        {
            if (_palette == null)
            {
                _palette = BlockPalette.CreateDefault();
            }

            _rectTransform = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>();

            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _rectTransform.sizeDelta = _cardSize;
            _rectTransform.anchoredPosition = _anchoredPosition;

            RectTransform card = CellFactory.CreateCard(
                _rectTransform, "TrayCard", _cardSize, _palette.CardBackground, _palette.CardShadow);

            BuildSlots(card);
        }

        private void Start()
        {
            if (_trayModel == null)
            {
                Debug.LogError($"{nameof(PieceTrayView)} was not injected. Is it registered in the LifetimeScope?", this);
                return;
            }

            _trayModel.SlotChanged += OnSlotChanged;
            for (int i = 0; i < TrayModel.SLOT_COUNT; i++)
            {
                RebuildSlot(i);
            }
        }

        private void OnDestroy()
        {
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

                _slotRects[i] = slotRect;
                _slotCells[i] = new List<CellView>(9);
            }
        }

        private void OnSlotChanged(int slotIndex) => RebuildSlot(slotIndex);

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
            PieceLayout.GetBounds(piece, out int width, out int height);

            float pitch = _trayCellSize + _trayCellSpacing;
            float offsetX = -((width - 1) * pitch) * 0.5f;
            float offsetY = -((height - 1) * pitch) * 0.5f;

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                GridPosition offset = piece.Offsets[i];
                CellView cell = CellFactory.CreateCell(
                    _slotRects[slotIndex], $"TrayCell_{i}", _trayCellSize, _cellInset, _cellBevelThickness);
                var rect = (RectTransform)cell.transform;
                rect.anchoredPosition = new Vector2(offsetX + (offset.X * pitch), offsetY + (offset.Y * pitch));
                cell.SetEmbossedColours(
                    _palette.GetFill(colourId), _palette.GetHighlight(colourId), _palette.GetShade(colourId));
                cells.Add(cell);
            }
        }
    }
}
