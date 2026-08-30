using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Escape.EditorTools
{
    public static class ThirdPartyNoticesBuildProcessor
    {
        private const string NoticeFileName = "THIRD-PARTY-NOTICES.txt";

        // 플레이어와 함께 배포할 수 있도록 고지 파일을 빌드 결과의 최상위 폴더에 복사한다.
        [PostProcessBuild(1000)]
        private static void CopyNoticeFile(BuildTarget target, string pathToBuiltProject)
        {
            string sourcePath = Path.Combine(Application.dataPath, "Resources", "Data", NoticeFileName);
            if (!File.Exists(sourcePath))
            {
                Debug.LogError($"[ThirdPartyNotices] 고지 파일이 없습니다: {sourcePath}");
                return;
            }

            string outputDirectory = ResolveOutputDirectory(pathToBuiltProject);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                Debug.LogError($"[ThirdPartyNotices] 빌드 출력 경로를 확인할 수 없습니다: {pathToBuiltProject}");
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            string destinationPath = Path.Combine(outputDirectory, NoticeFileName);
            File.Copy(sourcePath, destinationPath, true);
            Debug.Log($"[ThirdPartyNotices] 고지 파일 복사 완료: {destinationPath}");
        }

        // 실행 파일, 앱 번들, 폴더형 빌드에 맞는 배포 루트를 반환한다.
        private static string ResolveOutputDirectory(string pathToBuiltProject)
        {
            if (string.IsNullOrWhiteSpace(pathToBuiltProject))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(pathToBuiltProject);
            if (string.Equals(Path.GetExtension(fullPath), ".app", System.StringComparison.OrdinalIgnoreCase))
            {
                return Directory.GetParent(fullPath)?.FullName;
            }

            if (Directory.Exists(fullPath) || string.IsNullOrEmpty(Path.GetExtension(fullPath)))
            {
                return fullPath;
            }

            return Path.GetDirectoryName(fullPath);
        }
    }
}
