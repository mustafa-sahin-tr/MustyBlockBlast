using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// A one-shot "open the level path picker as soon as gameplay boots" request, carried across the
    /// scene boundary (issue #379). The mode-select scene raises it when Macera Modu is picked — that
    /// mode is played one chosen level at a time, so the player still has a level to choose — and the
    /// gameplay scene consumes it once on boot.
    /// <para>
    /// Lives in PlayerPrefs because nothing else crosses a scene load here: every scene has its own
    /// container and the two never share an instance of anything. It is a flag, not a setting — it is
    /// cleared the moment it is read, so a later plain boot into gameplay never re-opens the picker.
    /// </para>
    /// </summary>
    public sealed class LevelPathOpenRequestSystem
    {
        private const string PENDING_OPEN_PREFS_KEY = "ModeSelect.PendingLevelPathOpen";

        /// <summary>Asks the next gameplay boot to open the level path picker.</summary>
        public void Request()
        {
            PlayerPrefs.SetInt(PENDING_OPEN_PREFS_KEY, 1);
        }

        /// <summary>
        /// Whether a request is pending. Clears it as part of answering, so each request opens the
        /// picker exactly once.
        /// </summary>
        public bool TryConsume()
        {
            if (PlayerPrefs.GetInt(PENDING_OPEN_PREFS_KEY, 0) == 0)
            {
                return false;
            }

            PlayerPrefs.DeleteKey(PENDING_OPEN_PREFS_KEY);
            return true;
        }
    }
}
