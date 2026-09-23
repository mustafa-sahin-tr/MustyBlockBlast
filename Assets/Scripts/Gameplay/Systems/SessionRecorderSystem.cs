using System;
using System.Globalization;
using System.IO;
using System.Text;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Development-only test aid: writes a plain-text log of the current run — tray draws,
    /// placements (each with a board snapshot), power-up uses and how the run ended — to one file
    /// that is overwritten at the start of every run, so it always holds only the most recently
    /// played run. Attach that file to a bug report in place of describing an odd board state from
    /// memory. Never compiled into a release build.
    /// </summary>
    public sealed class SessionRecorderSystem : IDisposable
    {
        private const string LOG_FILE_NAME = "last_session_log.txt";

        /// <summary>Indexed by <see cref="SpecialCellKind"/>; index 0 (<see cref="SpecialCellKind.None"/>)
        /// is never read — <see cref="CellGlyph"/> only consults this after ruling None out.</summary>
        private static readonly char[] SpecialCellGlyphs = { '?', 'E', 'L', 'G', 'V', 'K', 'O', 'T', 'D' };

        private readonly BoardModel _boardModel;
        private readonly TrayModel _trayModel;
        private readonly IDisposable _runStartedSubscription;
        private readonly IDisposable _piecePlacedSubscription;
        private readonly IDisposable _gameOverSubscription;
        private readonly IDisposable _trayRefilledSubscription;
        private readonly IDisposable _powerUpAppliedSubscription;
        private readonly IDisposable _specialPieceSpawnedSubscription;

        private readonly StringBuilder _lineBuilder = new StringBuilder(128);

        private StreamWriter _writer;
        private float _runStartTime;

        [Inject]
        public SessionRecorderSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<TrayRefilledMessage> trayRefilledSubscriber,
            ISubscriber<PowerUpAppliedMessage> powerUpAppliedSubscriber,
            ISubscriber<SpecialPieceSpawnedMessage> specialPieceSpawnedSubscriber)
        {
            _boardModel = boardModel;
            _trayModel = trayModel;

            _runStartedSubscription = runStartedSubscriber.Subscribe(OnRunStarted);
            _piecePlacedSubscription = piecePlacedSubscriber.Subscribe(OnPiecePlaced);
            _gameOverSubscription = gameOverSubscriber.Subscribe(OnGameOver);
            _trayRefilledSubscription = trayRefilledSubscriber.Subscribe(OnTrayRefilled);
            _powerUpAppliedSubscription = powerUpAppliedSubscriber.Subscribe(OnPowerUpApplied);
            _specialPieceSpawnedSubscription = specialPieceSpawnedSubscriber.Subscribe(OnSpecialPieceSpawned);
        }

        public void Dispose()
        {
            _runStartedSubscription.Dispose();
            _piecePlacedSubscription.Dispose();
            _gameOverSubscription.Dispose();
            _trayRefilledSubscription.Dispose();
            _powerUpAppliedSubscription.Dispose();
            _specialPieceSpawnedSubscription.Dispose();
            CloseWriter();
        }

        private void OnRunStarted(RunStartedMessage message)
        {
            // Overwritten, not appended: only the most recently played run is ever worth attaching
            // to a bug report, and each new run replaces whatever the previous one left behind.
            CloseWriter();

            try
            {
                string path = Path.Combine(Application.persistentDataPath, LOG_FILE_NAME);
                _writer = new StreamWriter(path, append: false) { AutoFlush = true };
            }
            catch (IOException exception)
            {
                Debug.LogWarning(
                    $"{nameof(SessionRecorderSystem)}: could not open '{LOG_FILE_NAME}' — {exception.Message}");
                return;
            }

            _runStartTime = Time.realtimeSinceStartup;
            WriteLine($"RUN STARTED board={_boardModel.Width}x{_boardModel.Height}");
            WriteTraySnapshot();
        }

        private void OnTrayRefilled(TrayRefilledMessage message)
        {
            WriteLine("TRAY REFILLED");
            WriteTraySnapshot();
        }

        private void OnPiecePlaced(PiecePlacedMessage message)
        {
            WriteLine(
                $"PLACE piece={message.PieceId} family={message.PieceFamily} anchor={message.Anchor} "
                + $"colour={message.ColourId} rowsCleared={message.RowsCleared} "
                + $"columnsCleared={message.ColumnsCleared} boardEmptyAfter={message.BoardEmptyAfterPlacement}");
            WriteBoardSnapshot();
        }

        private void OnPowerUpApplied(PowerUpAppliedMessage message)
        {
            WriteLine(
                $"POWERUP APPLIED kind={message.Kind} clearedCells={message.ClearedCellCount} "
                + $"clearedLines={message.ClearedLineCount}");
            WriteBoardSnapshot();
        }

        private void OnSpecialPieceSpawned(SpecialPieceSpawnedMessage message)
        {
            WriteLine($"SPECIAL PIECE SPAWNED kind={message.Kind} slot={message.SlotIndex}");
        }

        private void OnGameOver(GameOverMessage message)
        {
            // Never closes the file here: a NoMovesLeft ending can still be rescued (RunRescuedMessage),
            // after which the same run keeps writing to this same file. Only a fresh RunStartedMessage
            // ever rotates the log.
            WriteLine($"GAME OVER reason={message.Reason} rescueAvailable={message.IsRescueAvailable}");
            WriteBoardSnapshot();
        }

        private void WriteTraySnapshot()
        {
            _lineBuilder.Clear();
            _lineBuilder.Append("  tray:");
            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                _lineBuilder.Append(' ');
                AppendSlot(slotIndex);
            }

            WriteRaw(_lineBuilder.ToString());
        }

        private void AppendSlot(int slotIndex)
        {
            Piece piece = _trayModel.GetPiece(slotIndex);
            if (piece == null)
            {
                _lineBuilder.Append("[empty]");
                return;
            }

            _lineBuilder.Append('[');
            _lineBuilder.Append(piece.Id);
            _lineBuilder.Append(':');
            _lineBuilder.Append(_trayModel.GetColourId(slotIndex));

            SpecialPieceKind specialKind = _trayModel.GetSpecialKind(slotIndex);
            if (specialKind != SpecialPieceKind.None)
            {
                _lineBuilder.Append(':');
                _lineBuilder.Append(specialKind);
            }

            _lineBuilder.Append(']');
        }

        private void WriteBoardSnapshot()
        {
            for (int y = 0; y < _boardModel.Height; y++)
            {
                _lineBuilder.Clear();
                _lineBuilder.Append("  ");
                for (int x = 0; x < _boardModel.Width; x++)
                {
                    _lineBuilder.Append(CellGlyph(new GridPosition(x, y)));
                }

                WriteRaw(_lineBuilder.ToString());
            }
        }

        private char CellGlyph(GridPosition position)
        {
            if (!_boardModel.IsPlayable(position))
            {
                return '#';
            }

            int colourId = _boardModel.GetCell(position);
            if (colourId == Board.EMPTY)
            {
                return '.';
            }

            SpecialCellKind specialKind = _boardModel.GetSpecialKind(position);
            if (specialKind != SpecialCellKind.None)
            {
                return SpecialCellGlyphs[(int)specialKind];
            }

            return colourId < 10
                ? (char)('0' + colourId)
                : (char)('A' + colourId - 10);
        }

        private void WriteLine(string text)
        {
            if (_writer == null)
            {
                return;
            }

            float elapsed = Time.realtimeSinceStartup - _runStartTime;
            _writer.WriteLine(
                "[" + elapsed.ToString("0.000", CultureInfo.InvariantCulture) + "] " + text);
        }

        private void WriteRaw(string text)
        {
            _writer?.WriteLine(text);
        }

        private void CloseWriter()
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }
#endif
}
