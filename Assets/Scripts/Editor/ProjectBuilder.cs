using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Escape.EditorTools
{
    /// <summary>현재 커밋의 Git 태그에서 플레이어 버전을 읽는다.</summary>
    internal static class GitTagVersion
    {
        private static readonly Regex SemanticVersionPattern = new(
            @"(?<!\d)(\d+\.\d+\.\d+)(?:[-+][0-9A-Za-z.-]+)?$",
            RegexOptions.Compiled);

        /// <summary>HEAD에 직접 붙은 태그에서 세 자리 숫자 버전을 반환한다.</summary>
        internal static bool TryGetCurrent(out string version)
        {
            version = null;

            try
            {
                string projectPath = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectPath))
                {
                    return false;
                }

                ProcessStartInfo startInfo = new("git", "tag --points-at HEAD --sort=-version:refname")
                {
                    WorkingDirectory = projectPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using Process process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    return false;
                }

                foreach (string tag in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Match match = SemanticVersionPattern.Match(tag.Trim());
                    if (!match.Success)
                    {
                        continue;
                    }

                    version = match.Groups[1].Value;
                    return true;
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"[GitTagVersion] Git 태그 버전을 읽지 못했습니다: {exception.Message}");
            }

            return false;
        }
    }

    /// <summary>에디터와 일반 빌드의 플레이어 버전을 현재 Git 태그에 맞춘다.</summary>
    [InitializeOnLoad]
    internal sealed class GitTagVersionSync : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        static GitTagVersionSync()
        {
            SyncPlayerVersion();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>Play 진입 직전에 최신 태그를 다시 확인한다.</summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                SyncPlayerVersion();
            }
        }

        /// <summary>Build Settings를 통한 일반 빌드에도 현재 태그 버전을 적용한다.</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            ProjectBuilder.ResetBuildTimeScale();
            if (!ProjectBuilder.HasExplicitBuildVersion())
            {
                SyncPlayerVersion();
            }
        }

        /// <summary>태그 버전이 현재 설정과 다를 때만 Player Settings를 갱신한다.</summary>
        private static void SyncPlayerVersion()
        {
            if (!GitTagVersion.TryGetCurrent(out string version) || PlayerSettings.bundleVersion == version)
            {
                return;
            }

            PlayerSettings.bundleVersion = version;
            UnityEngine.Debug.Log($"[GitTagVersion] Player version synced to {version}.");
        }
    }

    /// <summary>플랫폼별 플레이어 설정과 자동 빌드를 구성한다.</summary>
    public static class ProjectBuilder
    {
        private const string TitleScenePath = "Assets/Scenes/0_TitleScene.unity";
        private const string RoomScenePath = "Assets/Scenes/1_RoomScene.unity";
        private const string LockPickScenePath = "Assets/Scenes/4_LockPickScene.unity";
        private const string IosOutputPath = "Builds/iOS";
        private const string AndroidOutputDir = "Builds/Android";
        private const string AndroidOutputPath = "Builds/Android/Escape House of Bonds.apk";
        private const string WindowsOutputDir = "Builds/Windows";
        private const string WindowsOutputPath = "Builds/Windows/EscapeHouse.exe";
        private const string StoveWindowsOutputDir = "Builds/StoveWindows";
        private const string StoveWindowsOutputPath = "Builds/StoveWindows/EscapeHouse.exe";
        private const string WebGlOutputDir = "docs";
        private const string ApplicationIconDirectory = "Assets/AppIcon";
        private const string ApplicationIconFilePrefix = "icon_";
        private const string ApplicationIconSourcePath = "Assets/AppIcon/icon_1024.png";
        private const string AppStoreIconSourcePath = ApplicationIconSourcePath;
        private const string AppStoreIconFileName = "Icon-AppStore-1024.png";
        private const string ApplicationDisplayName = "Escape! House of Bonds";
        private const string IosApplicationDisplayName = "Escape! House of Bonds";
        private const string BundleIdentifier = "com.unyaunyagames.escapeunity";
        private const string DefaultVersion = "1.0.0";
        private const string DefaultBuildNumber = "8";
        private const string IosExportComplianceKey = "ITSAppUsesNonExemptEncryption";
        private static readonly int[] ApplicationIconSizes = { 16, 32, 64, 128, 256, 512, 1024 };
        private static readonly (string Locale, string DisplayName)[] IosLocalizedApplicationDisplayNames =
        {
            ("ko", "탈출! 인연의 집"),
            ("ja", "脱出！絆の家"),
        };

        public static void BuildIos()
        {
#if UNITY_IOS
            string version = GetBuildVersion("ESCAPEUNITY_IOS_VERSION");
            string buildNumber = GetEnvironmentValue("ESCAPEUNITY_IOS_BUILD_NUMBER", DefaultBuildNumber);
            bool enableTestFlightQa = bool.TryParse(
                GetEnvironmentValue("ESCAPEUNITY_UPLOAD_TO_TESTFLIGHT", "false"),
                out bool uploadToTestFlight) && uploadToTestFlight;

            EnsureBuildScenesRegistered();
            ConfigureCommonPlayerSettings();
            PlayerSettings.bundleVersion = version;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleIdentifier);
            PlayerSettings.iOS.buildNumber = buildNumber;

            BuildPlayerOptions options = new()
            {
                scenes = GetEnabledScenePaths(),
                locationPathName = IosOutputPath,
                target = BuildTarget.iOS,
                extraScriptingDefines = enableTestFlightQa
                    ? new[] { "ESCAPE_TESTFLIGHT" }
                    : Array.Empty<string>(),
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"iOS build failed: {report.summary.result}");
            }

            EnsureIosAppStoreIcon();
            ConfigureIosBundleMetadata();

            Debug.Log(
                $"[ProjectBuilder] iOS build succeeded: {version} ({buildNumber}), " +
                $"TestFlight QA: {enableTestFlightQa}");
#else
            throw new InvalidOperationException(
                "iOS build requires Unity iOS Build Support and the iOS build target. " +
                "Run Unity with -buildTarget iOS on an editor installation that includes iOS support.");
#endif
        }

        public static void BuildAndroid()
        {
            string version = GetBuildVersion("ESCAPEUNITY_ANDROID_VERSION");
            string buildNumber = GetEnvironmentValue("ESCAPEUNITY_ANDROID_BUILD_NUMBER", DefaultBuildNumber);
            int versionCode = ParseAndroidVersionCode(buildNumber);

            EnsureBuildScenesRegistered();
            ConfigureCommonPlayerSettings();
            PlayerSettings.bundleVersion = version;
            ConfigureAndroidPlayerSettings(versionCode);
            EditorUserBuildSettings.buildAppBundle = false;

            Directory.CreateDirectory(AndroidOutputDir);

            BuildPlayerOptions options = new()
            {
                scenes = GetEnabledScenePaths(),
                locationPathName = AndroidOutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Android build failed: {report.summary.result}");
            }

            Debug.Log($"[ProjectBuilder] Android build succeeded: {version} (versionCode {versionCode})");
        }

        public static void BuildWindows()
        {
            string version = GetBuildVersion("ESCAPEUNITY_WINDOWS_VERSION");

            EnsureBuildScenesRegistered();
            ConfigureCommonPlayerSettings();
            ConfigureWindowsPlayerSettings();
            PlayerSettings.bundleVersion = version;

            Directory.CreateDirectory(WindowsOutputDir);

            BuildPlayerOptions options = new()
            {
                scenes = GetEnabledScenePaths(),
                locationPathName = WindowsOutputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Windows build failed: {report.summary.result}");
            }

            Debug.Log($"[ProjectBuilder] Windows build succeeded: {version}");
        }

        /// <summary>Steam 초기화를 제외한 STOVE 전용 Windows 빌드를 생성한다.</summary>
        public static void BuildStoveWindows()
        {
            string version = GetBuildVersion("ESCAPEUNITY_STOVE_VERSION");

            EnsureBuildScenesRegistered();
            ConfigureCommonPlayerSettings();
            ConfigureWindowsPlayerSettings();
            PlayerSettings.bundleVersion = version;

            Directory.CreateDirectory(StoveWindowsOutputDir);

            BuildPlayerOptions options = new()
            {
                scenes = GetEnabledScenePaths(),
                locationPathName = StoveWindowsOutputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                extraScriptingDefines = new[] { "STOVE_BUILD" },
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"STOVE Windows build failed: {report.summary.result}");
            }

            Debug.Log($"[ProjectBuilder] STOVE Windows build succeeded: {version}");
        }

        /// <summary>GitHub Pages에서 바로 배포할 수 있는 WebGL 빌드를 생성한다.</summary>
        [MenuItem("EscapeUnity/Build WebGL for GitHub Pages")]
        public static void BuildWebGl()
        {
            string version = GetBuildVersion("ESCAPEUNITY_WEBGL_VERSION");
            string outputPath = Path.Combine(GetProjectPath(), WebGlOutputDir);

            EnsureBuildScenesRegistered();
            ResetBuildTimeScale();
            ConfigureWebGlPlayerSettings();
            PlayerSettings.bundleVersion = version;

            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }

            BuildPlayerOptions options = new()
            {
                scenes = GetEnabledScenePaths(),
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"WebGL build failed: {report.summary.result}");
            }

            File.WriteAllText(Path.Combine(outputPath, ".nojekyll"), string.Empty);
            Debug.Log($"[ProjectBuilder] WebGL build succeeded: {version}");
        }

        // Bake the default player settings into ProjectSettings without
        // building. Usable via -executeMethod.
        public static void ConfigureDefaults()
        {
            int androidVersionCode = ParseAndroidVersionCode(DefaultBuildNumber);

            ConfigureCommonPlayerSettings();
            ConfigureAndroidPlayerSettings(androidVersionCode);
            ConfigureWindowsPlayerSettings();
            PlayerSettings.bundleVersion = GetBuildVersion(null);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBuilder] Default player settings applied.");
        }

        // 모든 플랫폼에서 가로 화면과 우냐우냐게임즈 커스텀 스플래시를 유지한다.
        private static void ConfigureCommonPlayerSettings()
        {
            ResetBuildTimeScale();
            PlayerSettings.productName = ApplicationDisplayName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleIdentifier);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Landscape default window for the desktop standalone player.
            // 256x192 is the game's base resolution (4:3); use a 3x integer
            // multiple so the pixel-art scales without distortion. The in-game
            // settings menu lets players pick other 4:3 presets at runtime.
            PlayerSettings.defaultScreenWidth = 768;
            PlayerSettings.defaultScreenHeight = 576;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.forceSingleInstance = true;

            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            ApplyApplicationIcons();
        }

        /// <summary>QA 실행 배속이 에디터 설정에 남아도 플레이어 빌드는 항상 1배속으로 시작하게 한다.</summary>
        internal static void ResetBuildTimeScale()
        {
            if (!Mathf.Approximately(Time.timeScale, 1f))
            {
                Debug.LogWarning($"[ProjectBuilder] 빌드 전 Time.timeScale을 {Time.timeScale:0.##}에서 1로 복구합니다.");
            }

            Time.timeScale = 1f;
        }

        private static void ConfigureWindowsPlayerSettings()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }

        /// <summary>별도 응답 헤더를 설정할 수 없는 GitHub Pages용 압축 설정을 적용한다.</summary>
        private static void ConfigureWebGlPlayerSettings()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
        }

        private static void ConfigureAndroidPlayerSettings(int versionCode)
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleIdentifier);
            PlayerSettings.Android.bundleVersionCode = versionCode;
        }

        private static void ApplyApplicationIcons()
        {
            SetApplicationIcon(NamedBuildTarget.Unknown);
            SetApplicationIcon(NamedBuildTarget.Standalone);
            SetApplicationIcon(NamedBuildTarget.iOS);
            SetApplicationIcon(NamedBuildTarget.Android);
        }

        // Unity가 요구하는 아이콘 슬롯마다 같은 크기의 PNG를 연결한다.
        private static void SetApplicationIcon(NamedBuildTarget buildTarget)
        {
            int[] iconSizes = PlayerSettings.GetIconSizes(buildTarget, IconKind.Application);
            if (iconSizes.Length == 0)
            {
                return;
            }

            Texture2D[] icons = iconSizes.Select(LoadApplicationIcon).ToArray();
            PlayerSettings.SetIcons(buildTarget, icons, IconKind.Application);
        }

        private static Texture2D LoadApplicationIcon(int size)
        {
            int iconSize = GetApplicationIconSize(size);
            string path = $"{ApplicationIconDirectory}/{ApplicationIconFilePrefix}{iconSize}.png";
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (icon == null)
            {
                throw new FileNotFoundException($"Missing {iconSize}x{iconSize} application icon.", path);
            }

            return icon;
        }

        private static int GetApplicationIconSize(int requestedSize)
        {
            foreach (int iconSize in ApplicationIconSizes)
            {
                if (iconSize >= requestedSize)
                {
                    return iconSize;
                }
            }

            return ApplicationIconSizes[^1];
        }

        // Android versionCode must fit in a signed 32-bit int (max 2,100,000,000).
        // A YYYYMMDDHHMM timestamp overflows it, so fall back to Unix seconds.
        private static int ParseAndroidVersionCode(string value)
        {
            if (int.TryParse(value, out int code) && code > 0)
            {
                return code;
            }

            return (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % int.MaxValue);
        }

        private static void EnsureBuildScenesRegistered()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(TitleScenePath, true),
                new EditorBuildSettingsScene(RoomScenePath, true),
                new EditorBuildSettingsScene(LockPickScenePath, true),
            };
        }

        private static string[] GetEnabledScenePaths()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        private static string GetEnvironmentValue(string key, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        /// <summary>현재 Unity 프로젝트의 절대 경로를 반환한다.</summary>
        private static string GetProjectPath()
        {
            return Directory.GetParent(Application.dataPath)?.FullName ??
                   throw new InvalidOperationException("Unity project path could not be resolved.");
        }

        /// <summary>명시된 버전, 현재 Git 태그, 기본값 순서로 빌드 버전을 결정한다.</summary>
        private static string GetBuildVersion(string environmentKey)
        {
            if (!string.IsNullOrEmpty(environmentKey))
            {
                string explicitVersion = Environment.GetEnvironmentVariable(environmentKey);
                if (!string.IsNullOrWhiteSpace(explicitVersion))
                {
                    return explicitVersion.Trim();
                }
            }

            return GitTagVersion.TryGetCurrent(out string tagVersion) ? tagVersion : DefaultVersion;
        }

        /// <summary>CI나 호출자가 플랫폼 버전을 명시했는지 확인한다.</summary>
        internal static bool HasExplicitBuildVersion()
        {
            return HasEnvironmentValue("ESCAPEUNITY_IOS_VERSION") ||
                   HasEnvironmentValue("ESCAPEUNITY_ANDROID_VERSION") ||
                   HasEnvironmentValue("ESCAPEUNITY_WINDOWS_VERSION") ||
                   HasEnvironmentValue("ESCAPEUNITY_STOVE_VERSION") ||
                   HasEnvironmentValue("ESCAPEUNITY_WEBGL_VERSION");
        }

        /// <summary>환경변수에 공백이 아닌 값이 있는지 확인한다.</summary>
        private static bool HasEnvironmentValue(string key)
        {
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key));
        }

        private static void EnsureIosAppStoreIcon()
        {
            string iconSetPath = Path.Combine(
                IosOutputPath,
                "Unity-iPhone/Images.xcassets/AppIcon.appiconset");
            string contentsPath = Path.Combine(iconSetPath, "Contents.json");
            string destinationPath = Path.Combine(iconSetPath, AppStoreIconFileName);

            if (!File.Exists(AppStoreIconSourcePath))
            {
                throw new FileNotFoundException("Missing iOS App Store icon source.", AppStoreIconSourcePath);
            }

            if (!Directory.Exists(iconSetPath) || !File.Exists(contentsPath))
            {
                throw new DirectoryNotFoundException($"Missing generated iOS app icon set: {iconSetPath}");
            }

            File.Copy(AppStoreIconSourcePath, destinationPath, true);

            string contents = File.ReadAllText(contentsPath);
            if (contents.Contains($"\"filename\" : \"{AppStoreIconFileName}\""))
            {
                return;
            }

            const string marker = "\n\t],\n\t\"info\"";
            string appStoreIconEntry =
                "\n\t\t,\n" +
                "\t\t{\n" +
                $"\t\t\t\"filename\" : \"{AppStoreIconFileName}\",\n" +
                "\t\t\t\"idiom\" : \"ios-marketing\",\n" +
                "\t\t\t\"scale\" : \"1x\",\n" +
                "\t\t\t\"size\" : \"1024x1024\"\n" +
                "\t\t}\n";

            if (!contents.Contains(marker))
            {
                throw new InvalidOperationException($"Unexpected app icon Contents.json format: {contentsPath}");
            }

            contents = contents.Replace(marker, appStoreIconEntry + "\t],\n\t\"info\"");
            File.WriteAllText(contentsPath, contents);
        }

#if UNITY_IOS
        // iOS 기본 표시명과 언어별 홈 화면 이름, 수출 규정 값을 생성 프로젝트에 반영한다.
        private static void ConfigureIosBundleMetadata()
        {
            string plistPath = Path.Combine(IosOutputPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                throw new FileNotFoundException("Missing generated iOS Info.plist.", plistPath);
            }

            PlistDocument plist = new();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("CFBundleDisplayName", IosApplicationDisplayName);
            plist.root.SetBoolean(IosExportComplianceKey, false);
            plist.WriteToFile(plistPath);

            string projectPath = PBXProject.GetPBXProjectPath(IosOutputPath);
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException("Missing generated iOS Xcode project.", projectPath);
            }

            PBXProject project = new();
            project.ReadFromFile(projectPath);
            string targetGuid = project.GetUnityMainTargetGuid();

            foreach ((string locale, string displayName) in IosLocalizedApplicationDisplayNames)
            {
                string localizationDirectory = $"{locale}.lproj";
                string relativePath = $"{localizationDirectory}/InfoPlist.strings";
                string absoluteDirectory = Path.Combine(IosOutputPath, localizationDirectory);
                string absolutePath = Path.Combine(IosOutputPath, relativePath);

                Directory.CreateDirectory(absoluteDirectory);
                File.WriteAllText(
                    absolutePath,
                    $"\"CFBundleDisplayName\" = \"{displayName}\";\n",
                    new UTF8Encoding(false));

                string fileGuid = project.AddFile(relativePath, relativePath, PBXSourceTree.Source);
                project.AddFileToBuild(targetGuid, fileGuid);
                project.AddKnownRegion(locale);
            }

            project.WriteToFile(projectPath);
        }
#endif
    }
}
