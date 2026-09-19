using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Settings;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="LevelObjectiveConfig.BannedPowerUps"/> (issue #317): absent means nothing is
    /// banned, an authored list survives untouched, and <c>IsValid</c> rejects an undefined enum value
    /// or a duplicate entry.
    /// <para>
    /// Rows are authored through <see cref="JsonUtility"/> rather than reflection, exactly as
    /// <c>LevelObjectiveConfigCoinCellTests</c> authors its rows: the serialized field names are the
    /// asset's own contract, so a row written this way is what the Inspector would have produced.
    /// </para>
    /// </summary>
    public class LevelObjectiveConfigBannedPowerUpTests
    {
        /// <summary>AC7's compatibility claim: a row authored before this field existed has no
        /// <c>_bannedPowerUps</c> in its serialized data at all, and bans nothing.</summary>
        [Test]
        public void BannedPowerUps_OnARowAuthoredBeforeTheFieldExisted_IsEmpty()
        {
            LevelObjectiveConfig config = ARow("{\"_levelNumber\":1,\"_targetValue\":1}");

            Assert.AreEqual(0, config.BannedPowerUps.Count);
        }

        [Test]
        public void BannedPowerUps_OnARowAuthoringTwoKinds_ReturnsBothInOrder()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_bannedPowerUps\":[0,3]}");

            Assert.AreEqual(2, config.BannedPowerUps.Count);
            Assert.AreEqual(PowerUpKind.Bomb, config.BannedPowerUps[0]);
            Assert.AreEqual(PowerUpKind.Joker, config.BannedPowerUps[1]);
        }

        /// <summary>An empty (default) list is exactly what every existing level's row deserializes to
        /// — AC7's other half, that authoring the field explicitly as empty behaves identically to never
        /// having authored it.</summary>
        [Test]
        public void IsValid_WithAnEmptyBanList_Passes()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_bannedPowerUps\":[]}");

            Assert.IsTrue(config.IsValid(out string error), error);
        }

        [Test]
        public void IsValid_WithARealBannedKind_Passes()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_bannedPowerUps\":[1]}");

            Assert.IsTrue(config.IsValid(out string error), error);
        }

        /// <summary>AC5: a value outside the enum's defined range — the shape a hand-edited or corrupted
        /// asset could produce — is rejected rather than silently accepted as a nonsense ban.</summary>
        [Test]
        public void IsValid_WithAnUndefinedPowerUpKindValue_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_bannedPowerUps\":[999]}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotEmpty(error);
        }

        /// <summary>AC5: the same kind authored twice is a typo, not two independent bans, and is
        /// rejected the same way an out-of-range reinforced cell is.</summary>
        [Test]
        public void IsValid_WithADuplicateBannedKind_Fails()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,\"_bannedPowerUps\":[2,2]}");

            Assert.IsFalse(config.IsValid(out string error));
            Assert.IsNotEmpty(error);
        }

        /// <summary>AC6: <c>ValidateInEditor</c> is validation-only for this field — it must not rewrite
        /// an invalid ban list, mirroring how out-of-bounds reinforced cells are reported rather than
        /// clamped.</summary>
        [Test]
        public void ValidateInEditor_WithADuplicateBannedKind_LeavesTheListUntouched()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_bannedPowerUps\":[2,2]}");

            config.ValidateInEditor();

            Assert.AreEqual(2, config.BannedPowerUps.Count);
            Assert.AreEqual(PowerUpKind.ColumnClear, config.BannedPowerUps[0]);
            Assert.AreEqual(PowerUpKind.ColumnClear, config.BannedPowerUps[1]);
        }

        private static LevelObjectiveConfig ARow(string json)
            => JsonUtility.FromJson<LevelObjectiveConfig>(json);
    }
}
