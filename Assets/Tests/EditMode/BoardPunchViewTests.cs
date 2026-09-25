using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Views;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the board punch's size and shape (issue #367 AC5) — small for one line, growing with
    /// lines and streak, never past the cap — and the settings switch that turns it off.
    /// </summary>
    public class BoardPunchViewTests
    {
        private const float BASE = 0.015f;
        private const float PER_LINE = 0.008f;
        private const float PER_STREAK = 0.004f;
        private const float CAP = 0.035f;
        private const float TOLERANCE = 0.0001f;

        private const string BOARD_PUNCH_PREFS_KEY = "Settings.BoardPunchEnabled";

        private static float Amplitude(int lineCount, int streak)
            => BoardPunchView.Amplitude(lineCount, streak, BASE, PER_LINE, PER_STREAK, CAP);

        [Test]
        public void Amplitude_ForOneLineOnAFreshStreak_IsTheBaseAmplitude()
        {
            Assert.AreEqual(BASE, Amplitude(1, 1), TOLERANCE);
        }

        [Test]
        public void Amplitude_ForNoLines_IsZero()
        {
            Assert.AreEqual(0f, Amplitude(0, 3), TOLERANCE);
        }

        [Test]
        public void Amplitude_GrowsWithLinesAndStreak()
        {
            Assert.Greater(Amplitude(2, 1), Amplitude(1, 1));
            Assert.Greater(Amplitude(1, 3), Amplitude(1, 1));
        }

        [Test]
        public void Amplitude_ForAHugeCombo_IsCapped()
        {
            Assert.AreEqual(CAP, Amplitude(6, 20), TOLERANCE);
        }

        [Test]
        public void Curve_StartsAndEndsAtRest_AndPeaksAtOne()
        {
            Assert.AreEqual(0f, BoardPunchView.Curve(0f), TOLERANCE);
            Assert.AreEqual(0f, BoardPunchView.Curve(1f), TOLERANCE);

            float peak = 0f;
            for (int sampleIndex = 0; sampleIndex <= 200; sampleIndex++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(BoardPunchView.Curve(sampleIndex / 200f)));
            }

            Assert.LessOrEqual(peak, 1f + TOLERANCE);
            Assert.Greater(peak, 0.99f);
        }

        [Test]
        public void SettingsSystem_BoardPunch_DefaultsOnAndPersistsWhenSwitchedOff()
        {
            bool hadKey = PlayerPrefs.HasKey(BOARD_PUNCH_PREFS_KEY);
            int savedValue = PlayerPrefs.GetInt(BOARD_PUNCH_PREFS_KEY, 1);
            try
            {
                PlayerPrefs.DeleteKey(BOARD_PUNCH_PREFS_KEY);
                var model = new SettingsModel(new ThemeDefinition[0]);
                var system = new SettingsSystem(model);
                Assert.IsTrue(model.BoardPunchEnabled.Value);

                system.SetBoardPunchEnabled(false);
                Assert.IsFalse(model.BoardPunchEnabled.Value);

                var reloadedModel = new SettingsModel(new ThemeDefinition[0]);
                new SettingsSystem(reloadedModel);
                Assert.IsFalse(reloadedModel.BoardPunchEnabled.Value);
            }
            finally
            {
                if (hadKey)
                {
                    PlayerPrefs.SetInt(BOARD_PUNCH_PREFS_KEY, savedValue);
                }
                else
                {
                    PlayerPrefs.DeleteKey(BOARD_PUNCH_PREFS_KEY);
                }
            }
        }
    }
}
