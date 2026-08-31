#if UNITY_EDITOR || DEVELOPMENT_BUILD || ESCAPE_TESTFLIGHT
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Escape.QA
{
    // 플레이어 빌드에서 StreamingAssets의 QA 스크립트를 실행하고 모바일 제어 UI를 유지한다.
    public sealed class RuntimeQaRunner : MonoBehaviour
    {
        private const string RouteDirectory = "QA/Routes";
        private const float DefaultSpeed = 1f;
        private const float MinimumSpeed = 0.25f;
        private const float MaximumSpeed = 16f;
        private const float InteractionTimeoutSeconds = 60f;

        private static readonly string[] routeFileNames =
        {
            "demo.qa",
        };

        private static RuntimeQaRunner instance;
        private readonly Stack<IEnumerator> coroutineStack = new();
        private RuntimeQaExecutor executor;
        private Coroutine runningCoroutine;
        private string routeFileName = string.Empty;
        private string status = "대기 중";
        private float requestedSpeed = DefaultSpeed;
        private bool paused;
        private bool panelVisible;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle statusStyle;
        private GUIStyle buttonStyle;

        public static IReadOnlyList<string> RouteFileNames => routeFileNames;
        public static bool IsRunning => instance != null && instance.runningCoroutine != null;
        public static string Status => instance != null ? instance.status : "대기 중";

        // 선택한 내장 QA 파일을 지속 오브젝트에서 실행한다.
        public static bool StartRoute(string fileName)
        {
            return StartRoute(fileName, DefaultSpeed);
        }

        // 자동 검증에서 지정한 배속으로 내장 QA 파일을 실행한다.
        public static bool StartRoute(string fileName, float speed)
        {
            if (string.IsNullOrWhiteSpace(fileName) || Array.IndexOf(routeFileNames, fileName) < 0)
            {
                return false;
            }

            RuntimeQaRunner runner = Ensure();
            if (runner.runningCoroutine != null)
            {
                runner.status = "이미 QA가 실행 중입니다.";
                runner.panelVisible = true;
                return false;
            }

            string path = Path.Combine(Application.streamingAssetsPath, RouteDirectory, fileName);
            if (!File.Exists(path))
            {
                runner.status = $"QA 파일이 없습니다: {fileName}";
                runner.panelVisible = true;
                return false;
            }

            string scriptText = File.ReadAllText(path);
            runner.routeFileName = fileName;
            runner.requestedSpeed = Mathf.Clamp(speed, MinimumSpeed, MaximumSpeed);
            runner.paused = false;
            runner.panelVisible = true;
            runner.status = "실행 준비 중";
            runner.runningCoroutine = runner.StartCoroutine(runner.RunScript(scriptText));
            return true;
        }

        // 씬 전환 중에도 QA 코루틴과 제어 UI가 유지되는 단일 실행기를 만든다.
        private static RuntimeQaRunner Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            var runnerObject = new GameObject(nameof(RuntimeQaRunner));
            DontDestroyOnLoad(runnerObject);
            instance = runnerObject.AddComponent<RuntimeQaRunner>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // 중첩 IEnumerator를 직접 순회해 플레이어에서도 실패와 종료를 한곳에서 처리한다.
        private IEnumerator RunScript(string scriptText)
        {
            executor = new RuntimeQaExecutor();
            coroutineStack.Clear();
            coroutineStack.Push(executor.RunQaActionScript(
                scriptText,
                requestedSpeed,
                InteractionTimeoutSeconds,
                0f,
                false));
            status = "실행 중";

            try
            {
                while (coroutineStack.Count > 0)
                {
                    if (paused)
                    {
                        yield return null;
                        continue;
                    }

                    IEnumerator current = coroutineStack.Peek();
                    bool moved;
                    try
                    {
                        moved = current.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        status = $"실패: {exception.Message}";
                        Debug.LogException(exception);
                        yield break;
                    }

                    if (!moved)
                    {
                        DisposeEnumerator(coroutineStack.Pop());
                        continue;
                    }

                    if (current.Current is IEnumerator nested)
                    {
                        coroutineStack.Push(nested);
                        continue;
                    }

                    yield return current.Current;
                }

                status = "완료";
            }
            finally
            {
                DisposeCoroutineStack();
                runningCoroutine = null;
                paused = false;
                executor = null;
                Time.timeScale = 1f;
            }
        }

        // 현재 QA를 재개할 수 있는 일시정지 상태로 전환한다.
        private void TogglePause()
        {
            if (runningCoroutine == null)
            {
                return;
            }

            paused = !paused;
            if (paused)
            {
                Time.timeScale = 0f;
            }
            else
            {
                executor?.SetRuntimeExecutionSpeed(requestedSpeed);
            }
            status = paused ? "일시정지" : "실행 중";
        }

        // 실행기 내부 배속과 게임 시간을 함께 조절한다.
        private void ChangeSpeed(float multiplier)
        {
            requestedSpeed = Mathf.Clamp(requestedSpeed * multiplier, MinimumSpeed, MaximumSpeed);
            if (!paused)
            {
                executor?.SetRuntimeExecutionSpeed(requestedSpeed);
            }
        }

        // 현재 QA를 완전히 중단하고 게임 배속과 가상 입력 장치를 정리한다.
        private void StopRun()
        {
            if (runningCoroutine != null)
            {
                StopCoroutine(runningCoroutine);
                runningCoroutine = null;
            }

            DisposeCoroutineStack();
            executor = null;
            paused = false;
            Time.timeScale = 1f;
            status = "사용자가 실행을 종료했습니다.";
        }

        private void DisposeCoroutineStack()
        {
            while (coroutineStack.Count > 0)
            {
                DisposeEnumerator(coroutineStack.Pop());
            }
        }

        private static void DisposeEnumerator(IEnumerator enumerator)
        {
            if (enumerator is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        // 모바일 안전 영역 상단에 QA 상태와 속도·정지·종료 버튼을 표시한다.
        private void OnGUI()
        {
            if (!panelVisible)
            {
                return;
            }

            EnsureGuiStyles();
            Rect safeArea = Screen.safeArea;
            float scale = Mathf.Max(1f, Screen.dpi > 0f ? Screen.dpi / 180f : 1f);
            float width = Mathf.Min(safeArea.width - 24f * scale, 620f * scale);
            float height = runningCoroutine != null ? 170f * scale : 128f * scale;
            float x = safeArea.x + (safeArea.width - width) * 0.5f;
            float y = Screen.height - safeArea.yMax + 12f * scale;

            GUILayout.BeginArea(new Rect(x, y, width, height), panelStyle);
            GUILayout.Label($"QA · {routeFileName}", titleStyle);
            string command = executor != null && !string.IsNullOrWhiteSpace(executor.CurrentQaCommand)
                ? executor.CurrentQaCommand
                : status;
            GUILayout.Label($"{status} · {requestedSpeed:0.##}×\n{command}", statusStyle);

            GUILayout.BeginHorizontal();
            if (runningCoroutine != null)
            {
                if (GUILayout.Button("속도 -", buttonStyle))
                {
                    ChangeSpeed(0.5f);
                }

                if (GUILayout.Button("속도 +", buttonStyle))
                {
                    ChangeSpeed(2f);
                }

                if (GUILayout.Button(paused ? "계속" : "정지", buttonStyle))
                {
                    TogglePause();
                }

                if (GUILayout.Button("그만하기", buttonStyle))
                {
                    StopRun();
                }
            }
            else if (GUILayout.Button("닫기", buttonStyle))
            {
                panelVisible = false;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // 기기 DPI에 맞춰 손가락으로 누르기 쉬운 개발 UI 스타일을 구성한다.
        private void EnsureGuiStyles()
        {
            float scale = Mathf.Max(1f, Screen.dpi > 0f ? Screen.dpi / 180f : 1f);
            int titleSize = Mathf.RoundToInt(18f * scale);
            if (panelStyle != null && titleStyle.fontSize == titleSize)
            {
                return;
            }

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(
                    Mathf.RoundToInt(14f * scale),
                    Mathf.RoundToInt(14f * scale),
                    Mathf.RoundToInt(10f * scale),
                    Mathf.RoundToInt(10f * scale)),
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = titleSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(13f * scale),
                wordWrap = true,
                normal = { textColor = new Color(0.88f, 0.94f, 0.92f) },
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(15f * scale),
                fixedHeight = 44f * scale,
            };
        }
    }
}
#endif
