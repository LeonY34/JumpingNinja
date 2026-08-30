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
        private const string BackgroundPatternPath = "Assets/Art/World/ninja-background-pattern.png";
        private const string HazardBlockPath = "Assets/Art/World/hazard-block.png";
        private const string WallBlockPath = "Assets/Art/World/safe-wall-block.png";

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

            Sprite backgroundPattern = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPatternPath);
            Sprite hazardBlock = AssetDatabase.LoadAssetAtPath<Sprite>(HazardBlockPath);
            Sprite wallBlock = AssetDatabase.LoadAssetAtPath<Sprite>(WallBlockPath);
            if (backgroundPattern == null || config.backgroundPatternSprite != backgroundPattern ||
                hazardBlock == null || config.hazardBlockSprite != hazardBlock ||
                wallBlock == null || config.wallBlockSprite != wallBlock)
            {
                throw new System.InvalidOperationException("The V1 config does not reference the world visual assets.");
            }

            if (config.jumpAnimationDuration <= 0f || config.deathAnimationDuration <= 0f)
            {
                throw new System.InvalidOperationException("The Ninja animation durations must be greater than zero.");
            }

            if (config.SafePlayerColliderScale < 0.5f || config.SafePlayerColliderScale >= 1f)
            {
                throw new System.InvalidOperationException("The Ninja collider must be smaller than its visual bounds.");
            }

            if (config.SafeMapWidth < 8 || config.SafeLayerHeight < 8 || config.SafeCameraWidth > config.SafeMapWidth)
            {
                throw new System.InvalidOperationException("The V1 map and camera configuration is invalid.");
            }

            if (config.SafeMapWidth != 15 || config.SafeCameraWidth != 9f || config.SafeLayerHeight != 9 ||
                config.visualThemeLayerInterval != 10 || config.backgroundThemeColors == null ||
                config.backgroundThemeColors.Length == 0 || config.hazardThemeTints == null ||
                config.hazardThemeTints.Length == 0)
            {
                throw new System.InvalidOperationException("The V1 map dimensions or ten-level visual themes are not configured.");
            }

            if (config.GetBackgroundColor(0) == config.GetBackgroundColor(10) ||
                config.GetHazardTint(0) == config.GetHazardTint(10))
            {
                throw new System.InvalidOperationException("The world visuals do not change after ten levels.");
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

            if (PlayerSettings.bundleVersion != "1.0.4" || PlayerSettings.Android.bundleVersionCode != 4)
            {
                throw new System.InvalidOperationException("The release version is not configured as v1.0.4.");
            }

            if (PlayerSettings.defaultScreenWidth != 540 || PlayerSettings.defaultScreenHeight != 960 ||
                PlayerSettings.fullScreenMode != FullScreenMode.Windowed || PlayerSettings.resizableWindow)
            {
                throw new System.InvalidOperationException("The Windows player is not configured as a fixed portrait window.");
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
            PlayerSettings.bundleVersion = "1.0.4";
            PlayerSettings.Android.bundleVersionCode = 4;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.potatoedmice.jumpingninja");
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = false;
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

            Sprite backgroundPattern = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPatternPath);
            Sprite hazardBlock = AssetDatabase.LoadAssetAtPath<Sprite>(HazardBlockPath);
            Sprite wallBlock = AssetDatabase.LoadAssetAtPath<Sprite>(WallBlockPath);
            if (backgroundPattern == null || hazardBlock == null || wallBlock == null)
            {
                throw new System.InvalidOperationException("A world background, hazard block, or wall block Sprite is missing.");
            }

            config.logo = logo;
            config.ninjaSprite = ninjaSprite;
            config.backgroundPatternSprite = backgroundPattern;
            config.hazardBlockSprite = hazardBlock;
            config.wallBlockSprite = wallBlock;
            config.mapWidth = 15;
            config.cameraVisibleWidth = 9f;
            config.layerHeight = 9;
            config.playerStartY = 4.5f;
            config.playerColliderScale = 0.82f;
            config.visualThemeLayerInterval = 10;
            EditorUtility.SetDirty(config);
        }
    }
}
