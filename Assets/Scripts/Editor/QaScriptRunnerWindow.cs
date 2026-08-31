using System.Globalization;
using Escape.QA;
using UnityEditor;
using UnityEngine;

namespace Escape.EditorTools
{
    // 데모 전용 .qa 파일을 Play Mode에서 실행하고 현재 상태를 표시한다.
    [InitializeOnLoad]
    public sealed class QaScriptRunnerWindow : EditorWindow
    {
        private const string DemoRouteFileName = "demo.qa";
        private const string DemoRouteAssetPath = "Assets/StreamingAssets/QA/Routes/demo.qa";
        private const string PendingRunKey = "Escape.Demo.QA.PendingRun";
        private const string CommandLineRunKey = "Escape.Demo.QA.CommandLineRun";
        private const string CommandLineStartedKey = "Escape.Demo.QA.CommandLineStarted";
        private const string CommandLineDeadlineKey = "Escape.Demo.QA.CommandLineDeadline";
        private const float CommandLineExecutionSpeed = 8f;
        private const double CommandLineTimeoutSeconds = 300d;

        static QaScriptRunnerWindow()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            EditorApplication.update -= MonitorCommandLineRun;
            EditorApplication.update += MonitorCommandLineRun;
            EditorApplication.delayCall += TryStartPendingRun;
        }

        // Tools 메뉴에서 데모 QA 실행기 창을 연다.
        [MenuItem("Tools/Escape/QA/QA 실행기")]
        private static void OpenWindow()
        {
            var window = GetWindow<QaScriptRunnerWindow>("데모 QA 실행기");
            window.minSize = new Vector2(420f, 180f);
            window.Show();
        }

        // Unity batchmode에서 demo.qa를 실행하고 성공 여부를 프로세스 종료 코드로 반환한다.
        public static void RunDemoQaFromCommandLine()
        {
            SessionState.SetBool(CommandLineRunKey, true);
            SessionState.SetBool(CommandLineStartedKey, false);
            SessionState.SetString(
                CommandLineDeadlineKey,
                (EditorApplication.timeSinceStartup + CommandLineTimeoutSeconds)
                    .ToString(CultureInfo.InvariantCulture));
            SessionState.SetBool(PendingRunKey, true);

            if (EditorApplication.isPlaying)
            {
                TryStartPendingRun();
                return;
            }

            EditorApplication.EnterPlaymode();
        }

        // 실행 경로와 상태, 시작 버튼을 간단히 표시한다.
        private void OnGUI()
        {
            EditorGUILayout.LabelField("EscapeUnity Demo QA", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("스크립트", DemoRouteAssetPath);
            EditorGUILayout.LabelField("상태", EditorApplication.isPlaying
                ? RuntimeQaRunner.Status
                : "Play Mode 진입 전");
            EditorGUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(RuntimeQaRunner.IsRunning))
            {
                if (GUILayout.Button("demo.qa 실행", GUILayout.Height(34f)))
                {
                    QueueRun();
                }
            }

            EditorGUILayout.HelpBox(
                "Edit Mode에서 실행하면 Play Mode로 전환한 뒤 자동으로 시작합니다.",
                MessageType.Info);
        }

        // 상태 문구가 재생 중에도 갱신되도록 창을 다시 그린다.
        private void OnInspectorUpdate()
        {
            Repaint();
        }

        // 현재 모드에 따라 즉시 시작하거나 Play Mode 진입 뒤 실행을 예약한다.
        private static void QueueRun()
        {
            SessionState.SetBool(PendingRunKey, true);
            if (EditorApplication.isPlaying)
            {
                TryStartPendingRun();
                return;
            }

            EditorApplication.EnterPlaymode();
        }

        // Play Mode 진입이 완료되면 예약된 QA 실행을 다음 Editor 프레임에 넘긴다.
        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += TryStartPendingRun;
            }
        }

        // 예약 상태를 한 번만 소비하고 StreamingAssets의 데모 루트를 실행한다.
        private static void TryStartPendingRun()
        {
            if (!EditorApplication.isPlaying ||
                !SessionState.GetBool(PendingRunKey, false))
            {
                return;
            }

            SessionState.SetBool(PendingRunKey, false);
            bool commandLineRun = SessionState.GetBool(CommandLineRunKey, false);
            float speed = commandLineRun ? CommandLineExecutionSpeed : 1f;
            if (!RuntimeQaRunner.StartRoute(DemoRouteFileName, speed))
            {
                Debug.LogError($"[Demo QA] 실행하지 못했습니다: {DemoRouteAssetPath}");
                if (commandLineRun)
                {
                    CompleteCommandLineRun(1);
                }

                return;
            }

            if (commandLineRun)
            {
                SessionState.SetBool(CommandLineStartedKey, true);
                Debug.Log($"[Demo QA] batchmode 실행 시작: {DemoRouteAssetPath}, {speed:0.##}x");
            }
        }

        // batchmode 실행의 완료·실패·제한시간을 감시한다.
        private static void MonitorCommandLineRun()
        {
            if (!SessionState.GetBool(CommandLineRunKey, false))
            {
                return;
            }

            string deadlineText = SessionState.GetString(CommandLineDeadlineKey, "0");
            double.TryParse(
                deadlineText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double deadline);
            if (deadline > 0d && EditorApplication.timeSinceStartup >= deadline)
            {
                Debug.LogError($"[Demo QA] {CommandLineTimeoutSeconds:0}초 제한시간을 초과했습니다.");
                CompleteCommandLineRun(1);
                return;
            }

            if (!SessionState.GetBool(CommandLineStartedKey, false) || RuntimeQaRunner.IsRunning)
            {
                return;
            }

            string status = RuntimeQaRunner.Status;
            bool succeeded = string.Equals(status, "완료", System.StringComparison.Ordinal);
            if (succeeded)
            {
                Debug.Log("[Demo QA] demo.qa 실행을 완료했습니다.");
            }
            else
            {
                Debug.LogError($"[Demo QA] demo.qa 실행 실패: {status}");
            }

            CompleteCommandLineRun(succeeded ? 0 : 1);
        }

        // batchmode 예약 상태를 정리하고 Unity를 지정 코드로 종료한다.
        private static void CompleteCommandLineRun(int exitCode)
        {
            SessionState.EraseBool(PendingRunKey);
            SessionState.EraseBool(CommandLineRunKey);
            SessionState.EraseBool(CommandLineStartedKey);
            SessionState.EraseString(CommandLineDeadlineKey);
            EditorApplication.Exit(exitCode);
        }
    }
}
