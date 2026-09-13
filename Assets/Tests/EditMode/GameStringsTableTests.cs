// The whole file is editor-only: it reads the String Table assets through UnityEditor.Localization,
// which does not exist in a player build. The assembly is already Editor-only, so the guard is
// belt-and-braces rather than load-bearing.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using MustyBlockBlast.Gameplay.Localization;
using NUnit.Framework;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Guards the shipped <c>GameStrings</c> String Table Collection itself, which
    /// <see cref="LocalizationSystemTests"/> deliberately does not touch — that suite tests the
    /// fallback rules against an in-memory table.
    /// <para>
    /// The failure this catches is translation drift: a key added to
    /// <see cref="LocalizationKeys"/> and filled in for English only. Nothing about that is a compile
    /// error and nothing about it is visible in English play-testing — it surfaces as an English
    /// label in the middle of a Spanish screen, months later. Reading the constants by reflection
    /// rather than listing them here means a new key is covered the moment it is declared.
    /// </para>
    /// Runs against the assets through the editor API, so no Addressables initialization and no play
    /// mode are involved.
    /// </summary>
    public class GameStringsTableTests
    {
        /// <summary>Locale codes the game ships. A new language is added here and to the table.</summary>
        private static readonly string[] ShippedLocaleCodes = { "en", "es", "tr" };

        /// <summary>
        /// The seconds suffix is the reason the parent localization issue exists: it used to be a
        /// hardcoded Turkish "sn" that every language got. Pinning the exact per-locale template makes
        /// a regression to one shared suffix a red test rather than a shipped bug.
        /// </summary>
        private static readonly Dictionary<string, string> ExpectedSecondsFormats = new Dictionary<string, string>
        {
            { "en", "{0}s" },
            { "es", "{0} s" },
            { "tr", "{0} sn" },
        };

        [Test]
        public void EveryShippedLocaleHasATable()
        {
            StringTableCollection collection = GetCollection();

            for (int localeIndex = 0; localeIndex < ShippedLocaleCodes.Length; localeIndex++)
            {
                string localeCode = ShippedLocaleCodes[localeIndex];
                Assert.IsNotNull(
                    FindTable(collection, localeCode),
                    $"No '{localeCode}' String Table in the {LocalizationKeys.TABLE_NAME} collection.");
            }
        }

        [Test]
        public void EveryKeyIsTranslatedInEveryShippedLocale()
        {
            StringTableCollection collection = GetCollection();
            IReadOnlyList<string> keys = GetDeclaredKeys();

            // Asserted rather than assumed: reflection silently returning nothing would turn this
            // whole test into a no-op that passes forever.
            Assert.Greater(keys.Count, 0, "No keys were found on LocalizationKeys.");

            for (int localeIndex = 0; localeIndex < ShippedLocaleCodes.Length; localeIndex++)
            {
                string localeCode = ShippedLocaleCodes[localeIndex];
                StringTable table = FindTable(collection, localeCode);
                Assert.IsNotNull(table, $"No '{localeCode}' String Table.");

                for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                {
                    string key = keys[keyIndex];
                    StringTableEntry entry = table.GetEntry(key);

                    Assert.IsNotNull(entry, $"'{key}' has no entry in the '{localeCode}' table.");
                    Assert.IsFalse(
                        string.IsNullOrWhiteSpace(entry.Value),
                        $"'{key}' is empty in the '{localeCode}' table.");
                }
            }
        }

        [Test]
        public void NoLocaleHasAnEntryThatIsNotADeclaredKey()
        {
            StringTableCollection collection = GetCollection();
            IReadOnlyList<string> declaredKeys = GetDeclaredKeys();
            var declared = new HashSet<string>(declaredKeys);

            // The other direction of drift: a key renamed in C# leaves its old row behind, and the
            // rename is only half done until the stale row goes too.
            foreach (SharedTableData.SharedTableEntry sharedEntry in collection.SharedData.Entries)
            {
                Assert.IsTrue(
                    declared.Contains(sharedEntry.Key),
                    $"The table has '{sharedEntry.Key}', which no LocalizationKeys constant declares.");
            }
        }

        [Test]
        public void TheSecondsSuffixIsSpelledPerLocale()
        {
            StringTableCollection collection = GetCollection();

            foreach (KeyValuePair<string, string> expected in ExpectedSecondsFormats)
            {
                StringTable table = FindTable(collection, expected.Key);
                Assert.IsNotNull(table, $"No '{expected.Key}' String Table.");

                StringTableEntry entry = table.GetEntry(LocalizationKeys.FORMAT_SECONDS);
                Assert.IsNotNull(entry, $"'{LocalizationKeys.FORMAT_SECONDS}' is missing from '{expected.Key}'.");
                Assert.AreEqual(
                    expected.Value,
                    entry.Value,
                    $"The seconds suffix for '{expected.Key}' is not the language's own spelling.");
            }
        }

        private static StringTableCollection GetCollection()
        {
            StringTableCollection collection =
                LocalizationEditorSettings.GetStringTableCollection(LocalizationKeys.TABLE_NAME);
            Assert.IsNotNull(collection, $"No '{LocalizationKeys.TABLE_NAME}' String Table Collection exists.");
            return collection;
        }

        private static StringTable FindTable(StringTableCollection collection, string localeCode)
        {
            foreach (StringTable table in collection.StringTables)
            {
                if (table != null && table.LocaleIdentifier.Code == localeCode)
                {
                    return table;
                }
            }

            return null;
        }

        /// <summary>
        /// Every entry key declared on <see cref="LocalizationKeys"/>, minus the collection name,
        /// which names the table rather than a row in it.
        /// </summary>
        private static IReadOnlyList<string> GetDeclaredKeys()
        {
            var keys = new List<string>(32);
            FieldInfo[] fields = typeof(LocalizationKeys)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                FieldInfo field = fields[fieldIndex];
                if (!field.IsLiteral || field.FieldType != typeof(string))
                {
                    continue;
                }

                if (field.Name == nameof(LocalizationKeys.TABLE_NAME))
                {
                    continue;
                }

                keys.Add((string)field.GetRawConstantValue());
            }

            return keys;
        }
    }
}
#endif
