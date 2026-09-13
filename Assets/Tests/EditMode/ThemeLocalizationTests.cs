// Editor-only: it loads the shipped ThemeDefinition assets through AssetDatabase, which does not
// exist in a player build. The assembly is already Editor-only, so the guard is belt-and-braces
// rather than load-bearing.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Settings;
using NUnit.Framework;
using UnityEditor;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Guards against a theme being added later without wiring localization: every shipped
    /// <see cref="ThemeDefinition"/> asset must declare a non-empty
    /// <see cref="ThemeDefinition.TranslationKey"/>, and that key must be one
    /// <see cref="LocalizationKeys"/> actually declares — otherwise
    /// <see cref="GameStringsTableTests.NoLocaleHasAnEntryThatIsNotADeclaredKey"/> would not catch a
    /// typo'd key that simply resolves to itself at runtime.
    /// </summary>
    public class ThemeLocalizationTests
    {
        [Test]
        public void EveryShippedThemeHasATranslationKeyDeclaredInLocalizationKeys()
        {
            ThemeDefinition[] themes = LoadAllThemeAssets();
            IReadOnlyList<string> declaredKeys = GetDeclaredKeys();
            var declared = new HashSet<string>(declaredKeys);

            Assert.Greater(themes.Length, 0, "No ThemeDefinition assets were found under Assets/.");

            for (int themeIndex = 0; themeIndex < themes.Length; themeIndex++)
            {
                ThemeDefinition theme = themes[themeIndex];
                Assert.IsFalse(
                    string.IsNullOrEmpty(theme.TranslationKey),
                    $"'{theme.name}' (id={theme.Id}) has no TranslationKey set.");
                Assert.IsTrue(
                    declared.Contains(theme.TranslationKey),
                    $"'{theme.name}' has TranslationKey '{theme.TranslationKey}', which no " +
                    "LocalizationKeys constant declares.");
            }
        }

        private static ThemeDefinition[] LoadAllThemeAssets()
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nameof(ThemeDefinition)}");
            var themes = new List<ThemeDefinition>(guids.Length);

            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                ThemeDefinition theme = AssetDatabase.LoadAssetAtPath<ThemeDefinition>(path);
                if (theme != null)
                {
                    themes.Add(theme);
                }
            }

            return themes.ToArray();
        }

        /// <summary>Every entry key declared on <see cref="LocalizationKeys"/>, minus the collection name.</summary>
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
