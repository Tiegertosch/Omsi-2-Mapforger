using System;
using System.IO;
using OmsiMapEditor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OmsiMapEditor.Editor
{
    /// <summary>Creates and assigns the initial URP assets on first project import.</summary>
    [InitializeOnLoad]
    public static class UrpProjectSetup
    {
        private const string PipelineAssetPath = "Assets/_Project/Settings/EditorPipeline.asset";
        private const string RendererAssetPath = "Assets/_Project/Settings/EditorRenderer.asset";
        private const string TemporaryRendererAssetPath = "Assets/UniversalRenderer.asset";
        private const string SettingsPath = "Assets/_Project/Settings/Resources/editor-settings.json";
        private const string SetupKey = "OmsiMapEditor.UrpSetupComplete";

        static UrpProjectSetup()
        {
            EditorApplication.delayCall += ConfigurePipeline;
        }

        [MenuItem("OMSI MapWorks/Setup/Configure URP")]
        public static void ConfigurePipeline()
        {
            if (SessionState.GetBool(SetupKey, false))
            {
                return;
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererAssetPath);
            if (rendererData == null)
            {
                rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(TemporaryRendererAssetPath);
            }

            if (rendererData == null)
            {
                var assetFactory = pipeline != null ? pipeline : ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                rendererData = assetFactory.LoadBuiltinRendererData();
                if (rendererData == null)
                {
                    Debug.LogError("Could not create the default URP renderer. Use Assets > Create > Rendering > URP Asset.");
                    return;
                }
            }

            if (AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererAssetPath) == null &&
                AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(TemporaryRendererAssetPath) != null)
            {
                var moveError = AssetDatabase.MoveAsset(TemporaryRendererAssetPath, RendererAssetPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    Debug.LogError("Could not move the URP renderer into project settings: " + moveError);
                    return;
                }
            }

            if (pipeline == null)
            {
                pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);
            }

            var serializedPipeline = new SerializedObject(pipeline);
            var rendererList = serializedPipeline.FindProperty("m_RendererDataList");
            var defaultRenderer = serializedPipeline.FindProperty("m_DefaultRendererIndex");
            if (rendererList == null || !rendererList.isArray || defaultRenderer == null)
            {
                Debug.LogError("URP renderer data could not be assigned. Create a URP asset from the Assets menu.");
                return;
            }

            rendererList.arraySize = 1;
            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            defaultRenderer.intValue = 0;
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            ApplyPlayerSettings();
            SessionState.SetBool(SetupKey, true);
            Debug.Log("OMSI MapWorks URP pipeline is configured.");
        }

        /// <summary>Copies the application values from editor-settings.json into the Unity player settings.</summary>
        [MenuItem("OMSI MapWorks/Setup/Apply Editor Settings")]
        public static void ApplyPlayerSettings()
        {
            MapforgeEditorSettings settings;
            try
            {
                settings = JsonUtility.FromJson<MapforgeEditorSettings>(File.ReadAllText(SettingsPath));
                if (settings == null)
                    throw new FormatException("The file is empty.");
                settings.Validate();
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not apply " + SettingsPath + ": " + exception.Message);
                return;
            }

            var application = settings.application;
            PlayerSettings.companyName = application.displayName;
            PlayerSettings.productName = application.displayName;
            PlayerSettings.defaultScreenWidth = application.defaultWindowWidth;
            PlayerSettings.defaultScreenHeight = application.defaultWindowHeight;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = application.startMaximized ? FullScreenMode.MaximizedWindow : FullScreenMode.Windowed;
        }
    }
}
