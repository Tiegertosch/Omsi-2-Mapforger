using System;
using System.IO;
using NUnit.Framework;
using OmsiMapEditor.Core;
using UnityEngine;

namespace OmsiMapEditor.Tests
{
    public sealed class MapforgeEditorSettingsTests
    {
        private const string SettingsPath = "Assets/_Project/Settings/Resources/editor-settings.json";

        [Test]
        public void ProjectSettingsFileIsValid()
        {
            var settings = JsonUtility.FromJson<MapforgeEditorSettings>(File.ReadAllText(SettingsPath));

            Assert.IsNotNull(settings);
            Assert.DoesNotThrow(settings.Validate);
            Assert.AreEqual("OMSI Map Editor", settings.application.displayName);
        }

        [Test]
        public void ProjectSettingsFileLoadsAsRuntimeResource()
        {
            var asset = Resources.Load<TextAsset>(MapforgeEditorSettings.ResourceName);

            Assert.IsNotNull(asset, "editor-settings.json must stay inside a Resources folder so the player can read it.");
        }

        [Test]
        public void ValidateRejectsOutOfRangeScale()
        {
            var settings = new MapforgeEditorSettings();
            settings.@interface.scale = 5f;

            Assert.Throws<FormatException>(settings.Validate);
        }

        [Test]
        public void ValidateRejectsDefaultWindowBelowMinimum()
        {
            var settings = new MapforgeEditorSettings();
            settings.application.defaultWindowWidth = 800;

            Assert.Throws<FormatException>(settings.Validate);
        }

        [Test]
        public void DefaultsAreValid()
        {
            Assert.DoesNotThrow(new MapforgeEditorSettings().Validate);
        }
    }
}
