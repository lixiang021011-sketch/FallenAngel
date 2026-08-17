#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FallenAngel.Core
{
    /// <summary>
    /// Android APK 打包（命令行: Unity.exe -batchmode -quit -projectPath ... -executeMethod FallenAngel.Core.BuildApk.BuildAndroidApk）
    /// 流程: 重建场景（无确认）→ 保存到 Assets/Scenes → 注册 Build Settings → 切 Android 平台 → 构建 APK
    /// </summary>
    public static class BuildApk
    {
        const string ScenePath = "Assets/Scenes/FallenAngel.unity";
        const string ApkPath = "Builds/FallenAngel.apk";
        const string PackageName = "com.lorxer.fallenangel";

        [MenuItem("Tools/FallenAngel/Build Android APK")]
        public static void BuildAndroidApk()
        {
            // ---- 1. 重建并保存场景（工程不保存场景，打包必须先构建）----
            SceneBuilder.BuildDefaultScene(false);
            if (!Directory.Exists("Assets/Scenes"))
                Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[BuildApk] 场景已保存: " + ScenePath);

            // ---- 1.5 清理 SDK/JDK 显式路径：全部走 Unity 默认路径 ----
            // SDK 位于 PlaybackEngines/AndroidPlayer/SDK（已补 cmdline-tools/latest/bin/sdkmanager.bat，
            //   否则 Unity 报 "Android SDK command-line tools component is not found"）；
            // JDK 位于 PlaybackEngines/AndroidPlayer/OpenJDK（已替换为 17——cmdline-tools 12.0
            //   要求 Java 17，原装 JDK 11 会 UnsupportedClassVersionError）。
            // 注意：不可显式设置 JdkPath（自装 JDK 目录校验失败会被判 "JDK not found" 且不再回退默认）。
            EditorPrefs.DeleteKey("AndroidSdkRoot");
            EditorPrefs.DeleteKey("JdkPath");

            // ---- 2. Android 平台与包设置 ----
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                Debug.Log("[BuildApk] switchAndroid=" + switched + " active=" + EditorUserBuildSettings.activeBuildTarget);
            }
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageName);
            PlayerSettings.companyName = "LorXer";
            PlayerSettings.productName = "FallenAngel";
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel33;
            // Mono 后端免 NDK（demo 用；正式版换 IL2CPP 需另装 NDK r23b）
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.Mono2x);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidETC2Fallback = AndroidETC2Fallback.Quality32Bit;
            Debug.Log("[BuildApk] 平台设置完成: " + PackageName + " IL2CPP/ARM64 minSdk24");

            // ---- 3. 构建 APK ----
            if (!Directory.Exists("Builds"))
                Directory.CreateDirectory("Builds");
            Debug.Log($"[BuildApk] 构建前检查: arch={PlayerSettings.Android.targetArchitectures} backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}");
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android, // 必须显式指定，否则读不到 Android 架构设置
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildApk] 构建成功: {ApkPath} 大小 {report.summary.totalSize / 1048576f:F1}MB");
            }
            else
            {
                Debug.LogError($"[BuildApk] 构建失败: {report.summary.result} 错误 {report.summary.totalErrors}");
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
#endif
