using System.Linq;
using JumpingNinja;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace JumpingNinjaEditor
{
    public static class V1ProjectSetup
    {
        private const string ConfigPath = "Assets/Resources/JumpingNinjaConfig.asset";
        private const string LogoPath = "Assets/Logos/logo.png";
        private const string NinjaSpritePath = "Assets/Art/Ninja/ninja-head.png";

        [MenuItem("Jumping Ninja/Configure V1 Android Project")]
        public static void ConfigureV1()
        {
            ConfigurePlayerSettings();
            CreateOrUpdateConfig();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool switched = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ||
                            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            if (!switched)
            {
                throw new System.InvalidOperationException("Unity could not switch the active build target to Android.");
            }

            Debug.Log("JUMPING_NINJA_V1_SETUP_OK");
        }

        [MenuItem("Jumping Ninja/Validate V1 Project")]
        public static void ValidateV1()
        {
            JumpingNinjaConfig config = AssetDatabase.LoadAssetAtPath<JumpingNinjaConfig>(ConfigPath);
            if (config == null)
            {
                throw new System.InvalidOperationException($"Missing config asset at {ConfigPath}.");
            }

            if (config.logo == null)
            {
                throw new System.InvalidOperationException("The V1 config does not reference the game logo.");
            }

            Sprite ninjaSprite = AssetDatabase.LoadAssetAtPath<Sprite>(NinjaSpritePath);
            if (ninjaSprite == null || config.ninjaSprite != ninjaSprite)
            {
                throw new System.InvalidOperationException("The V1 config does not reference the square ninja sprite.");
            }

            if (config.jumpAnimationDuration <= 0f || config.deathAnimationDuration <= 0f)
            {
                throw new System.InvalidOperationException("The Ninja animation durations must be greater than zero.");
            }

            if (config.SafeMapWidth < 8 || config.SafeLayerHeight < 8 || config.SafeCameraWidth > config.SafeMapWidth)
            {
                throw new System.InvalidOperationException("The V1 map and camera configuration is invalid.");
            }

            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.Portrait)
            {
                throw new System.InvalidOperationException("Android orientation is not locked to portrait.");
            }

            string identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (identifier != "com.potatoedmice.jumpingninja")
            {
                throw new System.InvalidOperationException($"Unexpected Android application identifier: {identifier}");
            }

            bool hasScene = EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == "Assets/Scenes/SampleScene.unity");
            if (!hasScene)
            {
                throw new System.InvalidOperationException("SampleScene is not enabled in build settings.");
            }

            Debug.Log("JUMPING_NINJA_V1_VALIDATION_OK");
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "Potatoed Mice";
            PlayerSettings.productName = "Jumping Ninja";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.potatoedmice.jumpingninja");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        }

        private static void CreateOrUpdateConfig()
        {
            JumpingNinjaConfig config = AssetDatabase.LoadAssetAtPath<JumpingNinjaConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<JumpingNinjaConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            Sprite logo = AssetDatabase.LoadAllAssetsAtPath(LogoPath).OfType<Sprite>().FirstOrDefault();
            if (logo == null)
            {
                throw new System.InvalidOperationException($"No Sprite was found in {LogoPath}.");
            }

            Sprite ninjaSprite = AssetDatabase.LoadAssetAtPath<Sprite>(NinjaSpritePath);
            if (ninjaSprite == null)
            {
                throw new System.InvalidOperationException($"No Sprite was found in {NinjaSpritePath}.");
            }

            config.logo = logo;
            config.ninjaSprite = ninjaSprite;
            EditorUtility.SetDirty(config);
        }
    }
}
