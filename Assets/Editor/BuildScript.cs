using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// 원버튼 Android 빌드 — Player Settings(패키지명·제품명·화면 방향·아키텍처)까지 코드가 강제해
// "설정 빠뜨린 빌드"가 나올 수 없게 한다. APK(로컬 배포·테스트)와 AAB(스토어 제출) 둘 다 낸다.
//
// ┌─ 새 프로젝트에서 고칠 곳은 아래 상수 블록뿐이다. ─────────────────────┐
public static class BuildScript
{
    private const string ApplicationId = "com.pmj.pitchsimulator";
    private const string ProductName = "PitchSimulator";
    private const string OutputDir = "Builds";

    // 가로 게임 기본값. 세로 게임이면 Portrait로 바꾸고 AllowPortrait를 true로.
    private const UIOrientation DefaultOrientation = UIOrientation.AutoRotation;
    private const bool AllowLandscape = true;
    private const bool AllowPortrait = false;
    // └──────────────────────────────────────────────────────────────────┘

    [MenuItem("Tools/Build/Android APK (로컬 배포)")]
    public static void BuildApk() => Build(appBundle: false);

    [MenuItem("Tools/Build/Android AAB (스토어 제출)")]
    public static void BuildAab() => Build(appBundle: true);

    private static void Build(bool appBundle)
    {
        ApplyPlayerSettings();

        // Google Play는 AAB만 받는다. APK는 테스터에게 파일로 넘기거나 실기기에 바로 꽂을 때 쓴다.
        // 스토어에 올릴 AAB는 서명이 필요하다 — 키스토어는 비밀번호가 붙으므로 코드에 넣지 않고
        // Player Settings > Publishing Settings에 등록하거나, CI에서는 환경변수로 주입한다.
        EditorUserBuildSettings.buildAppBundle = appBundle;

        Directory.CreateDirectory(OutputDir);
        string outputPath = Path.Combine(OutputDir, $"{ProductName}.{(appBundle ? "aab" : "apk")}");

        var options = new BuildPlayerOptions
        {
            // 씬 목록은 Build Settings(원본)를 그대로 읽는다 — 복사본을 들고 있다가
            // 씬 추가를 못 따라가 "화면 하나가 빠진 빌드"가 나올 뻔한 실사례가 있다
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = outputPath,
            target = BuildTarget.Android,      // 플랫폼 스위치 포함 (첫 실행은 리임포트로 기기에 따라 3~10분)
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"[BuildScript] 빌드 성공 → {outputPath} ({report.summary.totalSize / (1024 * 1024)}MB)");
        }
        else
        {
            Debug.LogError($"[BuildScript] 빌드 실패: {report.summary.result}");
        }
    }

    // 빌드마다 강제한다 — Inspector 상태에 의존하면 누가 한 번 바꿔놓은 게 그대로 나간다.
    private static void ApplyPlayerSettings()
    {
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
        PlayerSettings.productName = ProductName;

        PlayerSettings.defaultInterfaceOrientation = DefaultOrientation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = AllowLandscape;
        PlayerSettings.allowedAutorotateToLandscapeRight = AllowLandscape;
        PlayerSettings.allowedAutorotateToPortrait = AllowPortrait;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = AllowPortrait;

        // IL2CPP + ARM64/ARMv7: Mono/ARMv7만으론 64비트 전용 최신 폰에서 설치가 거부된다
        // (실사례: 타인 폰에 파일로 전달했다가 설치 실패). NDK가 필요하고 빌드 시간이 늘어나는 건 감수한다.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
    }
}
