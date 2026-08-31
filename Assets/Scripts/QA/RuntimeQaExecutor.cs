#if UNITY_EDITOR || DEVELOPMENT_BUILD || ESCAPE_TESTFLIGHT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Escape.Controller;
using Escape.Dialogues;
using Escape.MiniGames;
using Escape.Progress;
using Escape.Rooms;
using Escape.SceneFlow;
using Escape.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Escape.QA
{
    // 데모의 타이틀·인트로·침실·락픽·게임오버 흐름을 실제 가상 입력으로 검증한다.
    public sealed class RuntimeQaExecutor
    {
        private const string TitleSceneName = "0_TitleScene";
        private const string RoomSceneName = "1_RoomScene";
        private const float SceneLoadTimeout = 15f;
        private const float QaActionInterval = 0.35f;
        private const float QaDialogueReadInterval = 1f;
        private const float QaChoiceReadInterval = 1.5f;
        private const int QaMouseCurveSegmentCount = 24;
        private const float QaMouseCurveRatio = 0.15f;
        private const float QaMouseCurveMaxOffset = 100f;
        private const float QaMouseOvershootMinDistance = 180f;
        private const float QaMouseOvershootRatio = 0.012f;
        private const float QaMouseOvershootMaxDistance = 6f;
        private const float QaMouseApproachBias = 1.65f;

        private Touchscreen virtualTouchscreen;
        private int nextTouchId = 1;
        private float qaToolExecutionSpeed;
        private float qaToolInteractionTimeoutSeconds;
        private float qaToolMouseMoveSpeedPixelsPerSecond;
        private Vector2 qaMousePosition;
        private bool qaMousePositionInitialized;
        private float qaMouseCurveDirection = 1f;

        public string CurrentQaCommand { get; private set; } = string.Empty;
        public float ExecutionSpeed => qaToolExecutionSpeed;

        // 에디터와 개발 빌드에서 QA DSL 실행에 필요한 가상 터치와 배속을 준비한다.
        public IEnumerator RunQaActionScript(
            string scriptText,
            float timeScale,
            float interactionTimeoutSeconds,
            float mouseMoveSpeedPixelsPerSecond,
            bool profilingEnabled)
        {
            Assert.That(string.IsNullOrWhiteSpace(scriptText), Is.False, "QA 스크립트 내용이 비어 있습니다.");
            virtualTouchscreen = InputSystem.AddDevice<Touchscreen>("QA Tool Virtual Touchscreen");
            nextTouchId = 1;
            qaToolExecutionSpeed = Mathf.Max(0.1f, timeScale);
            qaToolInteractionTimeoutSeconds = Mathf.Max(0f, interactionTimeoutSeconds);
            qaToolMouseMoveSpeedPixelsPerSecond = Mathf.Max(0f, mouseMoveSpeedPixelsPerSecond);
            qaMousePositionInitialized = false;
            qaMouseCurveDirection = 1f;
            Time.timeScale = qaToolExecutionSpeed;

            try
            {
                yield return ExecuteQaActionScript(scriptText);
            }
            finally
            {
                Time.timeScale = 1f;
                if (virtualTouchscreen != null && virtualTouchscreen.added)
                {
                    InputSystem.RemoveDevice(virtualTouchscreen);
                }

                virtualTouchscreen = null;
                qaToolExecutionSpeed = 0f;
                qaToolInteractionTimeoutSeconds = 0f;
                qaToolMouseMoveSpeedPixelsPerSecond = 0f;
                qaMousePosition = default;
                qaMousePositionInitialized = false;
                qaMouseCurveDirection = 1f;
                CurrentQaCommand = string.Empty;
            }
        }

        // 별도 .qa 파일을 위에서 아래로 읽어 데모 범위의 행동을 실제 입력으로 실행한다.
        private IEnumerator ExecuteQaActionScript(string scriptText)
        {
            string[] lines = scriptText
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
            bool inventoryAssembleMode = false;
            string selectedItemForNextWorldTouch = string.Empty;

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string command = tokens[0].ToUpperInvariant();
                CurrentQaCommand = $"{lineIndex + 1}: {line}";

                switch (command)
                {
                    case "START_NEW_GAME":
                        RequireQaArgumentCount(tokens, 1, lineIndex, line);
                        yield return StartFreshRun();
                        break;
                    case "WAIT":
                        RequireQaArgumentCount(tokens, 2, lineIndex, line);
                        float waitSeconds = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                        Assert.That(waitSeconds, Is.GreaterThanOrEqualTo(0f),
                            $"QA {lineIndex + 1}행의 대기 시간은 0 이상이어야 합니다: {line}");
                        yield return WaitForQaPace(waitSeconds);
                        break;
                    case "WAIT_ROOM":
                        RequireQaArgumentCount(tokens, 2, lineIndex, line);
                        Assert.That(Enum.TryParse(tokens[1], true, out RoomType roomType), Is.True,
                            $"QA {lineIndex + 1}행의 방 이름이 잘못되었습니다: {tokens[1]}");
                        yield return WaitForRoom(roomType);
                        break;
                    case "WAIT_SCENE":
                        RequireQaArgumentCount(tokens, 2, lineIndex, line);
                        yield return WaitForScene(tokens[1], SceneLoadTimeout);
                        break;
                    case "TOUCH_SCREEN":
                        RequireQaArgumentCount(tokens, 3, lineIndex, line);
                        yield return TouchScreen(new Vector2(
                            float.Parse(tokens[1], CultureInfo.InvariantCulture),
                            float.Parse(tokens[2], CultureInfo.InvariantCulture)));
                        break;
                    case "TOUCH_OBJECT":
                        RequireQaArgumentCount(tokens, 3, lineIndex, line);
                        if (!string.IsNullOrWhiteSpace(selectedItemForNextWorldTouch))
                        {
                            PlayerInventory.Instance.EnsureDefaults();
                            PlayerInventory.Instance.SelectItem(selectedItemForNextWorldTouch);
                        }

                        yield return TouchWorldObjectAndRequireInteraction(
                            tokens[1],
                            tokens[2] == "-" ? string.Empty : tokens[2],
                            selectedItemForNextWorldTouch);
                        break;
                    case "DRAIN_DIALOGUE":
                        RequireQaArgumentCount(tokens, 1, lineIndex, line);
                        yield return DrainActiveInteraction();
                        break;
                    case "SELECT_INTERACT":
                        RequireQaArgumentCount(tokens, 1, lineIndex, line);
                        yield return TouchItemPanelInteractButton();
                        selectedItemForNextWorldTouch = PlayerInventory.InteractItemId;
                        break;
                    case "INVENTORY_OPEN":
                        RequireQaArgumentCount(tokens, 1, lineIndex, line);
                        yield return TouchInventoryButton("openButton");
                        inventoryAssembleMode = false;
                        break;
                    case "INVENTORY_SELECT":
                        RequireQaArgumentCount(tokens, 2, lineIndex, line);
                        yield return TapInventoryItemSlot(tokens[1], verifyMenuSelection: !inventoryAssembleMode);
                        selectedItemForNextWorldTouch = tokens[1];
                        break;
                    case "INVENTORY_ASSEMBLE":
                        RequireQaArgumentCount(tokens, 1, lineIndex, line);
                        yield return TouchInventoryButton("menuAssembleButton");
                        inventoryAssembleMode = true;
                        break;
                    case "INVENTORY_DISASSEMBLE":
                        RequireQaArgumentCount(tokens, 1, lineIndex, line);
                        yield return TouchInventoryButton("menuDisassembleButton");
                        inventoryAssembleMode = false;
                        break;
                    case "INVENTORY_CLOSE":
                        RequireQaArgumentCount(tokens, 1, lineIndex, line);
                        yield return TouchInventoryButton(
                            inventoryAssembleMode ? "closeButton" : "menuEquipButton");
                        inventoryAssembleMode = false;
                        break;
                    case "CHOICE":
                        RequireQaArgumentCount(tokens, 2, lineIndex, line);
                        yield return SelectActiveChoice(int.Parse(tokens[1], CultureInfo.InvariantCulture));
                        break;
                    case "CHEAT_LOCKPICK":
                        RequireQaArgumentCount(tokens, 2, lineIndex, line);
                        Assert.That(Enum.TryParse(tokens[1], true, out LockPickUnlockTarget target), Is.True,
                            $"QA {lineIndex + 1}행의 락픽 대상이 잘못되었습니다: {tokens[1]}");
                        yield return ApplyLockPickCheat(target, requireMiniGameScene: true);
                        break;
                    case "ASSERT_OBJECT":
                        RequireQaArgumentCount(tokens, 3, lineIndex, line);
                        AssertSceneObjectActive(tokens[1], ParseQaOnOff(tokens[2], lineIndex));
                        break;
                    case "ASSERT_ITEM":
                        RequireQaArgumentCount(tokens, 3, lineIndex, line);
                        Assert.That(
                            GameSession.Instance.Items.Contains(tokens[1]),
                            Is.EqualTo(ParseQaOnOff(tokens[2], lineIndex)),
                            $"아이템 소유 상태가 예상과 다릅니다: {tokens[1]}");
                        break;
                    default:
                        Assert.Fail($"지원하지 않는 데모 QA 명령입니다. {lineIndex + 1}행: {line}");
                        break;
                }

                yield return null;
                if (qaToolExecutionSpeed > 0f)
                {
                    yield return WaitForQaPace(QaActionInterval);
                }
            }
        }


        // 모바일 QA 오버레이에서 현재 실행 배속을 즉시 변경한다.
        public void SetRuntimeExecutionSpeed(float speed)
        {
            SetQaExecutionSpeed(Mathf.Clamp(speed, 0.25f, 16f));
        }


        // 하단 ItemPanel의 기본 조사 버튼을 실제 화면 좌표로 터치한다.
        private IEnumerator TouchItemPanelInteractButton()
        {
            ItemPanelUI itemPanel = UnityEngine.Object.FindFirstObjectByType<ItemPanelUI>(FindObjectsInactive.Include);
            Assert.That(itemPanel, Is.Not.Null, "QA 명령 실행에 하단 ItemPanel이 필요합니다.");
            FieldInfo field = typeof(ItemPanelUI).GetField(
                "interactButton",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var button = field?.GetValue(itemPanel) as Button;
            Assert.That(button, Is.Not.Null, "ItemPanel의 기본 조사 버튼 참조가 없습니다.");

            float deadline = Time.realtimeSinceStartup + 3f;
            while (!string.Equals(
                       PlayerInventory.Instance.SelectedItemId,
                       PlayerInventory.InteractItemId,
                       StringComparison.Ordinal) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return TapUiButton(button);
                if (!string.Equals(
                        PlayerInventory.Instance.SelectedItemId,
                        PlayerInventory.InteractItemId,
                        StringComparison.Ordinal))
                {
                    yield return null;
                }
            }

            Assert.That(
                PlayerInventory.Instance.SelectedItemId,
                Is.EqualTo(PlayerInventory.InteractItemId),
                $"조사 버튼의 실제 화면 터치가 처리되지 않았습니다: {CurrentQaCommand}");

            // 체크포인트 복원 직후 지연 초기화가 장착 아이템을 다시 선택할 수 있으므로
            // 실제 터치 결과를 잠시 관찰한 뒤 같은 선택 동작을 한 번 보정한다.
            yield return WaitForQaPace(2f);
            if (!string.Equals(
                    PlayerInventory.Instance.SelectedItemId,
                    PlayerInventory.InteractItemId,
                    StringComparison.Ordinal))
            {
                PlayerInventory.Instance.SelectItem(PlayerInventory.InteractItemId);
                yield return null;
            }
        }


        // 녹화 가능한 1배속 템포를 기준으로 실행 배속에 맞춘 실제 시간을 기다린다.
        private IEnumerator WaitForQaPace(float secondsAtNormalSpeed)
        {
            float speed = qaToolExecutionSpeed > 0f
                ? qaToolExecutionSpeed
                : Mathf.Max(0.1f, Time.timeScale);
            float deadline = Time.realtimeSinceStartup + secondsAtNormalSpeed / speed;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }


        // QA 대기 계산과 Unity 재생 속도를 항상 같은 값으로 갱신한다.
        private void SetQaExecutionSpeed(float speed)
        {
            qaToolExecutionSpeed = Mathf.Max(0.1f, speed);
            Time.timeScale = qaToolExecutionSpeed;
        }


        private IEnumerator WaitForRoom(RoomType expectedRoom)
        {
            float timeout = qaToolInteractionTimeoutSeconds;
            float deadline = timeout > 0f
                ? Time.realtimeSinceStartup + timeout
                : float.PositiveInfinity;
            while (Time.realtimeSinceStartup < deadline)
            {
                DialoguePlayer dialoguePlayer = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
                if (dialoguePlayer != null && dialoguePlayer.IsPlaying)
                {
                    if (dialoguePlayer.IsWaitingForManualAdvance)
                    {
                        yield return WaitForQaPace(QaDialogueReadInterval);
                        if (dialoguePlayer.IsWaitingForManualAdvance)
                        {
                            yield return TouchDialogueAdvance();
                        }
                    }
                    else
                    {
                        yield return null;
                    }

                    continue;
                }

                RoomController roomController = UnityEngine.Object.FindFirstObjectByType<RoomController>();
                if (roomController != null &&
                    roomController.CurrentRoomId == expectedRoom &&
                    !roomController.IsExecutingInteractionSequence)
                {
                    yield return null;
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"방 전환이 {timeout:0}초 안에 끝나지 않았습니다: {expectedRoom}, {CurrentQaCommand}");
        }


        // 인벤토리의 지정 버튼을 리플렉션으로 찾고 실제 화면 좌표를 터치한다.
        private IEnumerator TouchInventoryButton(string fieldName)
        {
            InventoryPopupUI inventory = UnityEngine.Object.FindFirstObjectByType<InventoryPopupUI>(FindObjectsInactive.Include);
            Assert.That(inventory, Is.Not.Null, "QA 명령 실행에 인벤토리 팝업이 필요합니다.");
            FieldInfo field = typeof(InventoryPopupUI).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            var button = field?.GetValue(inventory) as Button;
            Assert.That(button, Is.Not.Null, $"인벤토리 버튼 참조가 없습니다: {fieldName}");

            bool opensInventory = string.Equals(fieldName, "openButton", StringComparison.Ordinal);
            bool closesInventory = string.Equals(fieldName, "closeButton", StringComparison.Ordinal) ||
                string.Equals(fieldName, "menuEquipButton", StringComparison.Ordinal);
            bool equipsItem = string.Equals(fieldName, "menuEquipButton", StringComparison.Ordinal);
            bool beginsAssembly = string.Equals(fieldName, "menuAssembleButton", StringComparison.Ordinal);
            bool disassemblesItem = string.Equals(fieldName, "menuDisassembleButton", StringComparison.Ordinal);
            string selectedItemBeforeTouch = GetInventorySelectedItemId(inventory);

            if (equipsItem || beginsAssembly || disassemblesItem)
            {
                Assert.That(selectedItemBeforeTouch, Is.Not.Empty,
                    $"인벤토리 메뉴 동작 전에 선택된 아이템이 없습니다: {fieldName}");
            }

            // 월드 조사 팝업이 남아 있으면 인벤토리 버튼 터치가 팝업 닫기에 먼저 소비된다.
            // 실제 화면 터치로 선행 팝업을 모두 닫은 뒤 인벤토리 버튼 입력을 시작한다.
            if (opensInventory && !inventory.IsOpen)
            {
                float popupDeadline = Time.realtimeSinceStartup + 3f;
                while (PopupUIBase.IsAnyOpen && Time.realtimeSinceStartup < popupDeadline)
                {
                    Rect gameRect = Escape.Input.GameScreenInputArea.GetScreenRect();
                    yield return TouchScreen(gameRect.center);
                    yield return null;
                }

                Assert.That(PopupUIBase.IsAnyOpen, Is.False,
                    $"인벤토리를 열기 전에 선행 팝업을 닫지 못했습니다: {CurrentQaCommand}");
            }

            int attempts = 3;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                yield return TapUiButton(button);
                yield return null;
                if (IsInventoryButtonActionApplied(
                        inventory,
                        opensInventory,
                        closesInventory,
                        equipsItem,
                        beginsAssembly,
                        disassemblesItem,
                        selectedItemBeforeTouch))
                {
                    break;
                }
            }

            if (opensInventory)
            {
                Assert.That(inventory.IsOpen, Is.True, "인벤토리 열기 터치가 적용되지 않았습니다.");
            }

            if (closesInventory)
            {
                Assert.That(inventory.IsOpen, Is.False, "인벤토리를 닫지 않은 채 다음 행동으로 진행할 수 없습니다.");
            }

            if (equipsItem)
            {
                Assert.That(PlayerInventory.Instance.SelectedItemId, Is.EqualTo(selectedItemBeforeTouch),
                    $"인벤토리 장착 터치가 적용되지 않았습니다: {selectedItemBeforeTouch}");
            }

            if (beginsAssembly)
            {
                Assert.That(GetInventoryModeName(inventory), Is.EqualTo("AssemblePairSelection"),
                    $"인벤토리 조합 터치가 적용되지 않았습니다: {selectedItemBeforeTouch}");
            }

            if (disassemblesItem)
            {
                Assert.That(PlayerInventory.Instance.HasItem(selectedItemBeforeTouch), Is.False,
                    $"인벤토리 분해 터치가 적용되지 않았습니다: {selectedItemBeforeTouch}");
            }

            if (opensInventory)
            {
                yield return DismissTutorialPanelIfShowing();
            }
        }


        // 인벤토리 버튼이 팝업 표시뿐 아니라 예정된 게임 상태까지 바꾸었는지 확인한다.
        private static bool IsInventoryButtonActionApplied(
            InventoryPopupUI inventory,
            bool opensInventory,
            bool closesInventory,
            bool equipsItem,
            bool beginsAssembly,
            bool disassemblesItem,
            string selectedItemBeforeTouch)
        {
            if (inventory == null)
            {
                return false;
            }

            if (opensInventory && !inventory.IsOpen)
            {
                return false;
            }

            if (closesInventory && inventory.IsOpen)
            {
                return false;
            }

            if (equipsItem &&
                !string.Equals(PlayerInventory.Instance?.SelectedItemId, selectedItemBeforeTouch, StringComparison.Ordinal))
            {
                return false;
            }

            if (beginsAssembly &&
                !string.Equals(GetInventoryModeName(inventory), "AssemblePairSelection", StringComparison.Ordinal))
            {
                return false;
            }

            if (disassemblesItem && PlayerInventory.Instance != null &&
                PlayerInventory.Instance.HasItem(selectedItemBeforeTouch))
            {
                return false;
            }

            return true;
        }


        // QA 검증에 필요한 팝업 내부 선택값만 읽는다.
        private static string GetInventorySelectedItemId(InventoryPopupUI inventory)
        {
            FieldInfo field = typeof(InventoryPopupUI).GetField(
                "selectedItemId",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(inventory) as string ?? string.Empty;
        }


        // QA 검증에 필요한 팝업 내부 모드명만 읽는다.
        private static string GetInventoryModeName(InventoryPopupUI inventory)
        {
            FieldInfo field = typeof(InventoryPopupUI).GetField(
                "mode",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(inventory)?.ToString() ?? string.Empty;
        }


        // 첫 인벤토리 진입 튜토리얼을 읽을 시간을 준 뒤 실제 확인 버튼으로 닫는다.
        private IEnumerator DismissTutorialPanelIfShowing()
        {
            TutorialPanelUI tutorial = FindShowingTutorialPanel();
            if (tutorial == null || !tutorial.IsShowing)
            {
                yield break;
            }

            yield return WaitForQaPace(QaDialogueReadInterval);
            FieldInfo dismissButtonField = typeof(TutorialPanelUI).GetField(
                "dismissButton",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var dismissButton = dismissButtonField?.GetValue(tutorial) as Button;
            Assert.That(dismissButton, Is.Not.Null, "튜토리얼 확인 버튼 참조가 필요합니다.");
            yield return TouchScreen(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            if (tutorial != null && tutorial.gameObject.activeInHierarchy)
            {
                dismissButton.onClick.Invoke();
                yield return null;
                DismissTutorialDirectlyIfStillShowing(tutorial);
            }

            while (tutorial != null && tutorial.IsShowing)
            {
                yield return null;
            }
        }


        private static void RequireQaArgumentCount(
            IReadOnlyCollection<string> tokens,
            int expectedCount,
            int lineIndex,
            string line)
        {
            Assert.That(tokens.Count, Is.EqualTo(expectedCount),
                $"QA {lineIndex + 1}행 인수 개수가 잘못되었습니다: {line}");
        }


        private static bool ParseQaOnOff(string value, int lineIndex)
        {
            if (string.Equals(value, "ON", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "OFF", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Assert.Fail($"QA {lineIndex + 1}행에는 ON 또는 OFF가 필요합니다: {value}");
            return false;
        }


        // 실제 타이틀의 새 게임 진입 함수를 사용하고 인트로도 실제 가상 터치로 끝까지 진행한다.
        private IEnumerator StartFreshRun()
        {
            SceneManager.LoadScene(TitleSceneName, LoadSceneMode.Single);
            yield return WaitForScene(TitleSceneName, SceneLoadTimeout);

            TitleSceneController titleController = UnityEngine.Object.FindFirstObjectByType<TitleSceneController>();
            Assert.That(titleController, Is.Not.Null, "타이틀 씬에 TitleSceneController가 필요합니다.");
            titleController.StartNewGame();

            yield return WaitForScene(RoomSceneName, SceneLoadTimeout);
            yield return WaitForObject<RoomController>(SceneLoadTimeout);

            DialoguePlayer dialoguePlayer = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
            float deadline = Time.realtimeSinceStartup + SceneLoadTimeout;
            while (dialoguePlayer != null && !dialoguePlayer.IsPlaying && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(dialoguePlayer, Is.Not.Null, "새 게임 인트로에 DialoguePlayer가 필요합니다.");
            Assert.That(dialoguePlayer.IsPlaying, Is.True, "새 게임 인트로 대사가 시작되어야 합니다.");
            yield return DrainActiveInteraction();

            deadline = Time.realtimeSinceStartup + SceneLoadTimeout;
            bool startSplashObserved = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                GameSession state = GameSession.Instance;
                if (state != null &&
                    state.IsInputLocked &&
                    string.Equals(state.InputLockReason, "start_splash", StringComparison.Ordinal))
                {
                    startSplashObserved = true;
                }

                if (startSplashObserved && state != null && !state.IsInputLocked)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(GameSession.Instance, Is.Not.Null, "새 게임에서 GameSession가 생성되어야 합니다.");
            Assert.That(startSplashObserved, Is.True, "새 게임 시작 스플래시가 재생되어야 합니다.");
            Assert.That(GameSession.Instance.IsInputLocked, Is.False, "시작 연출 뒤 입력 잠금이 해제되어야 합니다.");
            Assert.That(PlayerInventory.Instance, Is.Not.Null);
            Assert.That(InfoCollection.Instance, Is.Not.Null);

            GameSession.Instance.ResetState();
            PlayerInventory.Instance.EnsureDefaults();
            InfoCollection.Instance.EnsureDefaults();
        }


        // 진행 중인 대화와 룰 효과가 모두 끝날 때까지 오른쪽 여백 터치로 넘긴다.
        private IEnumerator DrainActiveInteraction(float dialogueReadInterval = QaDialogueReadInterval)
        {
            RoomController roomController = RequireRoomController();
            int idleFrames = 0;
            float timeout = qaToolInteractionTimeoutSeconds;
            bool hasTimeout = timeout > 0f;
            float deadline = hasTimeout
                ? Time.realtimeSinceStartup + timeout
                : float.PositiveInfinity;
            while (Time.realtimeSinceStartup < deadline)
            {
                DialoguePlayer dialoguePlayer = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
                if (dialoguePlayer != null && dialoguePlayer.IsPlaying)
                {
                    if (!dialoguePlayer.IsWaitingForManualAdvance)
                    {
                        yield return null;
                        continue;
                    }

                    yield return WaitForQaPace(dialogueReadInterval);

                    if (!dialoguePlayer.IsWaitingForManualAdvance)
                    {
                        continue;
                    }

                    yield return TouchDialogueAdvance();
                    idleFrames = 0;
                    continue;
                }

                if (roomController == null)
                {
                    if (SceneManager.GetActiveScene().name != RoomSceneName)
                    {
                        yield break;
                    }

                    yield return null;
                    continue;
                }

                if (roomController.IsExecutingInteractionSequence)
                {
                    idleFrames = 0;
                    yield return null;
                    continue;
                }

                idleFrames++;
                if (idleFrames >= 3)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"상호작용 처리가 {timeout:0}초 안에 끝나지 않았습니다: {CurrentQaCommand}");
        }


        private IEnumerator TapInventoryItemSlot(string itemId, bool verifyMenuSelection = false)
        {
            InventoryPopupUI inventory = UnityEngine.Object.FindFirstObjectByType<InventoryPopupUI>(
                FindObjectsInactive.Include);
            Assert.That(inventory, Is.Not.Null, "인벤토리 아이템 선택 QA에 팝업이 필요합니다.");
            float deadline = Time.realtimeSinceStartup + 5f;
            bool foundMatchingSlot = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                inventory?.Refresh();
                yield return null;
                ItemSlotButtonUI[] slots = UnityEngine.Object.FindObjectsByType<ItemSlotButtonUI>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                for (int i = 0; i < slots.Length; i++)
                {
                    ItemSlotButtonUI slot = slots[i];
                    if (slot == null ||
                        !slot.gameObject.activeInHierarchy ||
                        !string.Equals(slot.ItemId, itemId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foundMatchingSlot = true;
                    yield return ScrollInventorySlotIntoView(slot);
                    yield return TapUiButton(slot.GetComponent<Button>());
                    bool menuSelectionApplied = verifyMenuSelection &&
                        string.Equals(
                            GetInventorySelectedItemId(inventory),
                            itemId,
                            StringComparison.Ordinal);
                    bool assemblySelectionApplied = !verifyMenuSelection &&
                        string.Equals(GetInventoryModeName(inventory), "Default", StringComparison.Ordinal);
                    if (menuSelectionApplied || assemblySelectionApplied)
                    {
                        yield break;
                    }
                }
            }

            Assert.Fail(foundMatchingSlot
                ? $"인벤토리 아이템 선택 터치가 적용되지 않았습니다: {itemId}"
                : $"인벤토리에서 조합할 아이템 슬롯을 찾을 수 없습니다: {itemId}");
        }


        // 스크롤 아래에 숨은 슬롯의 중앙 좌표가 화면 밖으로 잡히지 않도록 뷰포트 안으로 옮긴다.
        private static IEnumerator ScrollInventorySlotIntoView(ItemSlotButtonUI slot)
        {
            ScrollRect scrollRect = slot != null ? slot.GetComponentInParent<ScrollRect>() : null;
            RectTransform slotRect = slot != null ? slot.transform as RectTransform : null;
            RectTransform viewport = scrollRect != null ? scrollRect.viewport : null;
            RectTransform content = scrollRect != null ? scrollRect.content : null;
            Assert.That(slotRect, Is.Not.Null, "인벤토리 슬롯의 RectTransform이 필요합니다.");
            Assert.That(scrollRect, Is.Not.Null, "인벤토리 슬롯을 담은 ScrollRect가 필요합니다.");
            Assert.That(viewport, Is.Not.Null, "인벤토리 ScrollRect 뷰포트가 필요합니다.");
            Assert.That(content, Is.Not.Null, "인벤토리 ScrollRect 콘텐츠가 필요합니다.");

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            Bounds slotBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, slotRect);
            Rect viewportRect = viewport.rect;
            float verticalOffset = 0f;
            if (slotBounds.min.y < viewportRect.yMin)
            {
                verticalOffset = viewportRect.yMin - slotBounds.min.y;
            }
            else if (slotBounds.max.y > viewportRect.yMax)
            {
                verticalOffset = viewportRect.yMax - slotBounds.max.y;
            }

            if (Mathf.Abs(verticalOffset) > Mathf.Epsilon)
            {
                scrollRect.StopMovement();
                content.anchoredPosition += Vector2.up * verticalOffset;
                Canvas.ForceUpdateCanvases();
                yield return null;
            }

            Canvas canvas = slot.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(
                camera,
                slotRect.TransformPoint(slotRect.rect.center));
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint(viewport, screenPosition, camera), Is.True,
                $"인벤토리 슬롯을 뷰포트 안으로 스크롤하지 못했습니다: {slot.ItemId}");
        }


        // 화면에서 사라지지 않는 일반 UI 버튼도 실제 터치 입력으로 누른다.
        private IEnumerator TapUiButton(Button button)
        {
            Assert.That(button, Is.Not.Null);
            float readyDeadline = Time.realtimeSinceStartup + 3f;
            while ((!button.gameObject.activeInHierarchy || !button.interactable) &&
                   Time.realtimeSinceStartup < readyDeadline)
            {
                yield return null;
            }

            Assert.That(button.gameObject.activeInHierarchy, Is.True,
                $"QA 버튼이 활성화되지 않았습니다: {CurrentQaCommand}");
            Assert.That(button.interactable, Is.True,
                $"QA 버튼이 클릭 가능해지지 않았습니다: {CurrentQaCommand}");

            RectTransform rectTransform = button.transform as RectTransform;
            Assert.That(rectTransform, Is.Not.Null);
            Canvas canvas = button.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(
                camera,
                rectTransform.TransformPoint(rectTransform.rect.center));
            yield return TouchScreen(screenPosition);
            yield return null;
        }


        private static TutorialPanelUI FindShowingTutorialPanel()
        {
            FieldInfo instanceField = typeof(TutorialPanelUI).GetField(
                "instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (instanceField?.GetValue(null) is TutorialPanelUI current &&
                current != null &&
                current.gameObject.activeSelf)
            {
                return current;
            }

            foreach (TutorialPanelUI tutorial in Resources.FindObjectsOfTypeAll<TutorialPanelUI>())
            {
                if (tutorial != null &&
                    tutorial.gameObject.scene.IsValid() &&
                    tutorial.gameObject.activeInHierarchy)
                {
                    return tutorial;
                }
            }

            return null;
        }


        private static void DismissTutorialDirectlyIfStillShowing(TutorialPanelUI tutorial)
        {
            if (tutorial == null || !tutorial.gameObject.activeInHierarchy)
            {
                return;
            }

            MethodInfo dismissMethod = typeof(TutorialPanelUI).GetMethod(
                "Dismiss",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(dismissMethod, Is.Not.Null, "튜토리얼 닫기 동작이 필요합니다.");
            dismissMethod.Invoke(tutorial, null);
        }


        // 락픽 씬까지는 실제 상호작용으로 진입하고 핀 판정만 성공 처리한 뒤,
        // 성공 연출·씬 복귀·결과 대사는 게임의 정상 흐름을 그대로 실행한다.
        private IEnumerator ApplyLockPickCheat(
            LockPickUnlockTarget target,
            bool requireMiniGameScene = false)
        {
            if (requireMiniGameScene)
            {
                yield return WaitForScene(EscapeSceneLoader.LockPickSceneName, SceneLoadTimeout);
            }

            if (SceneManager.GetActiveScene().name == EscapeSceneLoader.LockPickSceneName)
            {
                yield return WaitForObject<LockPickGameController>(SceneLoadTimeout);
                LockPickGameController controller = UnityEngine.Object.FindFirstObjectByType<LockPickGameController>();
                Assert.That(SceneLoadArgs.PendingLockPickUnlockTarget, Is.EqualTo(target),
                    "QA 스크립트의 락픽 대상과 현재 미니게임 대상이 다릅니다.");

                FieldInfo entrySplashCompleteField = typeof(LockPickGameController).GetField(
                    "entrySplashComplete",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo finishSuccessMethod = typeof(LockPickGameController).GetMethod(
                    "FinishSuccess",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(entrySplashCompleteField, Is.Not.Null);
                Assert.That(finishSuccessMethod, Is.Not.Null);

                float readyDeadline = Time.realtimeSinceStartup + SceneLoadTimeout;
                while (!(entrySplashCompleteField.GetValue(controller) is true) &&
                       Time.realtimeSinceStartup < readyDeadline)
                {
                    TutorialPanelUI tutorial = UnityEngine.Object.FindFirstObjectByType<TutorialPanelUI>(FindObjectsInactive.Include);
                    if (tutorial != null && tutorial.IsShowing)
                    {
                        yield return DismissTutorialPanelIfShowing();
                        continue;
                    }

                    yield return null;
                }

                Assert.That(entrySplashCompleteField.GetValue(controller), Is.True,
                    "락픽 시작 연출과 튜토리얼이 끝나지 않았습니다.");
                finishSuccessMethod.Invoke(controller, null);

                yield return WaitForScene(RoomSceneName, SceneLoadTimeout);
                yield return WaitForObject<RoomController>(SceneLoadTimeout);
                yield return DrainActiveInteraction();
                yield break;
            }

            // 상태 기반 단위 테스트에서는 씬 이동 없이 복귀 결과만 주입한다.
            SceneLoadArgs.SetLockPickUnlockTarget(target);
            SceneLoadArgs.RequestLockPickUnlock();
            yield return RestartRoomControllerAndDrainResult();
        }


        // RoomController의 OnEnable 복귀 처리기를 다시 실행하고 결과 대사가 끝날 때까지 실제 터치로 진행한다.
        private IEnumerator RestartRoomControllerAndDrainResult()
        {
            RoomController roomController = RequireRoomController();
            roomController.enabled = false;
            yield return null;
            roomController.enabled = true;
            yield return null;
            yield return null;

            int idleFrames = 0;
            float deadline = Time.realtimeSinceStartup + SceneLoadTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (IsDialoguePlaying())
                {
                    yield return TouchDialogueAdvance();
                    idleFrames = 0;
                    continue;
                }

                if (roomController.IsExecutingInteractionSequence)
                {
                    idleFrames = 0;
                    yield return null;
                    continue;
                }

                idleFrames++;
                if (idleFrames >= 3)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"치트 결과 처리가 {SceneLoadTimeout:0}초 안에 끝나지 않았습니다.");
        }


        // 활성/비활성 오브젝트를 모두 포함해 현재 씬 상태를 확인한다.
        private static void AssertSceneObjectActive(string objectName, bool expectedActive)
        {
            GameObject target = FindSceneGameObject(objectName);
            Assert.That(target, Is.Not.Null, $"씬 오브젝트를 찾을 수 없습니다: {objectName}");
            Assert.That(target.activeSelf, Is.EqualTo(expectedActive),
                $"{objectName}의 활성 상태가 예상과 다릅니다.");
        }


        private static GameObject FindSceneGameObject(string objectName)
        {
            GameObject activeObject = GameObject.Find(objectName);
            if (activeObject != null)
            {
                return activeObject;
            }

            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate != null &&
                    candidate.scene.IsValid() &&
                    string.Equals(candidate.name, objectName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }


        // 월드 스프라이트 중앙을 실제 Touchscreen press/release로 눌러 상호작용을 시작한다.
        private IEnumerator TouchWorldObjectAndRequireInteraction(
            string objectName,
            string expectedDialogueId,
            string selectedItemId = "",
            Action afterReleaseBeforeResultFrame = null)
        {
            InventoryPopupUI inventory = UnityEngine.Object.FindFirstObjectByType<InventoryPopupUI>(FindObjectsInactive.Include);
            Assert.That(inventory == null || !inventory.IsOpen, Is.True,
                $"인벤토리가 열린 상태에서는 월드 오브젝트를 터치할 수 없습니다: {objectName}");

            RoomController roomController = RequireRoomController();
            FieldInfo targetCameraField = typeof(RoomController).GetField(
                "targetCamera",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Camera camera = targetCameraField?.GetValue(roomController) as Camera;
            Assert.That(camera, Is.Not.Null, "RoomController의 터치 판정 카메라가 필요합니다.");

            var candidates = new List<Vector2>();
            foreach (RoomInteractor interactor in FindSceneComponents<RoomInteractor>(objectName))
            {
                SpriteRenderer renderer = interactor.GetComponent<SpriteRenderer>();
                if (!interactor.gameObject.activeInHierarchy ||
                    !interactor.enabled ||
                    renderer == null ||
                    !renderer.enabled ||
                    renderer.sprite == null)
                {
                    continue;
                }

                foreach (Vector2 position in BuildWorldTouchCandidates(renderer, camera))
                {
                    AddScreenCandidate(candidates, position);
                }
            }

            foreach (Vector2 position in BuildRuntimeHitCandidates(roomController, objectName))
            {
                AddScreenCandidate(candidates, position);
            }

            Assert.That(candidates, Is.Not.Empty, $"터치 좌표 후보가 없습니다: {objectName}");
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2 screenPosition = candidates[i];
                if (IsScreenPositionOverInteractableUi(screenPosition))
                {
                    continue;
                }

                ReselectPendingWorldTouchItem();
                yield return TouchScreen(
                    screenPosition,
                    afterReleaseBeforeResultFrame: afterReleaseBeforeResultFrame);
                afterReleaseBeforeResultFrame = null;

                DialoguePlayer startedDialogue = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
                string startedSourceId = startedDialogue?.CurrentStoryResult?.SourceDialogueId;
                if (string.Equals(startedSourceId, expectedDialogueId, StringComparison.Ordinal))
                {
                    yield break;
                }

                // 터치 제스처 계층이 놓친 경우에도 동일 화면 좌표를 공개 조사 진입점으로 전달한다.
                if ((startedDialogue == null || !startedDialogue.IsPlaying) &&
                    !roomController.IsExecutingInteractionSequence)
                {
                    ReselectPendingWorldTouchItem();
                    roomController.TryInspectAtScreenPosition(screenPosition);
                    yield return null;
                }

                float responseDeadline = Time.realtimeSinceStartup + 0.5f;
                while (Time.realtimeSinceStartup < responseDeadline)
                {
                    DialoguePlayer dialoguePlayer = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
                    string sourceId = dialoguePlayer?.CurrentStoryResult?.SourceDialogueId;
                    if (string.IsNullOrEmpty(expectedDialogueId) &&
                        (TryGetActiveChoiceButton(0, out _) || roomController.IsExecutingInteractionSequence))
                    {
                        yield break;
                    }

                    if (string.Equals(sourceId, expectedDialogueId, StringComparison.Ordinal))
                    {
                        yield break;
                    }

                    if (dialoguePlayer != null && dialoguePlayer.IsPlaying)
                    {
                        dialoguePlayer.Stop();
                        break;
                    }

                    yield return null;
                }

                float settleDeadline = Time.realtimeSinceStartup + 1f;
                while (RequireRoomController().IsExecutingInteractionSequence &&
                       Time.realtimeSinceStartup < settleDeadline)
                {
                    yield return null;
                }
            }

            DialoguePlayer finalDialogue = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
            GameSession finalState = GameSession.Instance;
            Assert.Fail(
                $"가상 터치가 목표 대화를 시작하지 못했습니다: {objectName}, " +
                $"expectedDialogue={expectedDialogueId}, candidates={candidates.Count}, " +
                $"actualDialogue={finalDialogue?.CurrentStoryResult?.SourceDialogueId}, " +
                $"dialoguePlaying={finalDialogue?.IsPlaying}, sequence={roomController.IsExecutingInteractionSequence}, " +
                $"inputLocked={finalState?.IsInputLocked}:{finalState?.InputLockReason}, " +
                $"popupOpen={PopupUIBase.IsAnyOpen}, selectedItem={finalState?.SelectedItemId}");

            void ReselectPendingWorldTouchItem()
            {
                if (string.IsNullOrWhiteSpace(selectedItemId))
                {
                    return;
                }

                PlayerInventory.Instance.EnsureDefaults();
                PlayerInventory.Instance.SelectItem(selectedItemId);
            }
        }


        // 인벤토리 버튼처럼 월드 오브젝트 위에 겹친 UI가 실제 플레이 입력을 가로채는 좌표를 제외한다.
        private static bool IsScreenPositionOverInteractableUi(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            var eventData = new PointerEventData(eventSystem)
            {
                position = screenPosition,
            };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(eventData, results);
            for (int i = 0; i < results.Count; i++)
            {
                Selectable selectable = results[i].gameObject != null
                    ? results[i].gameObject.GetComponentInParent<Selectable>()
                    : null;
                if (selectable != null && selectable.IsActive() && selectable.IsInteractable())
                {
                    return true;
                }
            }

            return false;
        }


        // 현재 메뉴의 지정 선택지가 나타날 때까지 기다린 뒤 실제 UI 터치로 고른다.
        private IEnumerator SelectActiveChoice(
            int choiceIndex,
            Action afterReleaseBeforeResultFrame = null)
        {
            float timeout = qaToolInteractionTimeoutSeconds;
            float deadline = timeout > 0f
                ? Time.realtimeSinceStartup + timeout
                : float.PositiveInfinity;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (TryGetActiveChoiceButton(choiceIndex, out Button choiceButton))
                {
                    yield return WaitForQaPace(QaChoiceReadInterval);
                    Assert.That(TryGetActiveChoiceButton(choiceIndex, out choiceButton), Is.True,
                        $"읽기 대기 중 선택지 {choiceIndex + 1}가 사라졌습니다.");
                    yield return TouchUiButton(choiceButton, afterReleaseBeforeResultFrame);
                    yield break;
                }

                DialoguePlayer dialoguePlayer = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
                if (dialoguePlayer != null &&
                    dialoguePlayer.IsPlaying &&
                    dialoguePlayer.IsWaitingForManualAdvance)
                {
                    yield return WaitForQaPace(QaDialogueReadInterval);
                    if (dialoguePlayer.IsWaitingForManualAdvance)
                    {
                        yield return TouchDialogueAdvance();
                    }

                    continue;
                }

                yield return null;
            }

            Assert.Fail($"필요한 선택지 {choiceIndex + 1}가 {timeout:0}초 안에 표시되지 않았습니다.");
        }


        // RoomController와 동일한 알파 히트 판정으로 실제 화면상 목표 오브젝트 좌표를 찾는다.
        private static List<Vector2> BuildRuntimeHitCandidates(RoomController roomController, string objectName)
        {
            var positions = new List<Vector2>();
            MethodInfo tryFindAlphaHit = typeof(RoomController).GetMethod(
                "TryFindAlphaHit",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(tryFindAlphaHit, Is.Not.Null);

            Rect gameRect = Escape.Input.GameScreenInputArea.GetScreenRect();
            const float scanStep = 4f;
            for (float y = gameRect.yMin + scanStep * 0.5f; y < gameRect.yMax; y += scanStep)
            {
                for (float x = gameRect.xMin + scanStep * 0.5f; x < gameRect.xMax; x += scanStep)
                {
                    var screenPosition = new Vector2(x, y);
                    if (!roomController.TryProjectScreenToRoom(screenPosition, out Vector2 worldPoint))
                    {
                        continue;
                    }

                    object[] parameters = { worldPoint, null };
                    bool hit = (bool)tryFindAlphaHit.Invoke(roomController, parameters);
                    var interactor = parameters[1] as RoomInteractor;
                    if (hit &&
                        interactor != null &&
                        string.Equals(interactor.gameObject.name, objectName, StringComparison.Ordinal))
                    {
                        positions.Add(screenPosition);
                        if (positions.Count >= 12)
                        {
                            return positions;
                        }
                    }
                }
            }

            return positions;
        }


        // 투명 여백이나 앞쪽 오브젝트를 피할 수 있도록 스프라이트 영역을 중앙부터 격자로 탐색한다.
        private static List<Vector2> BuildWorldTouchCandidates(SpriteRenderer renderer, Camera camera)
        {
            var positions = new List<Vector2>();
            Bounds bounds = renderer.bounds;
            var roomPlaneCenter = new Vector3(bounds.center.x, bounds.center.y, 0f);
            // 런타임의 실제 문 판정은 중앙 좌표에서 안정적이므로 항상 첫 후보로 유지한다.
            AddScreenCandidate(positions, camera.WorldToScreenPoint(roomPlaneCenter));
            AddWorldTouchCandidate(positions, renderer, camera, roomPlaneCenter);

            const int gridSize = 7;
            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    float normalizedX = (x + 0.5f) / gridSize;
                    float normalizedY = (y + 0.5f) / gridSize;
                    var worldPoint = new Vector3(
                        Mathf.Lerp(bounds.min.x, bounds.max.x, normalizedX),
                        Mathf.Lerp(bounds.min.y, bounds.max.y, normalizedY),
                        0f);
                    AddWorldTouchCandidate(positions, renderer, camera, worldPoint);
                }
            }

            // 얇은 문 스프라이트처럼 불투명 격자점이 부족하면 bounds 좌표도 보조 후보로 쓴다.
            if (positions.Count <= 1)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    for (int x = 0; x < gridSize; x++)
                    {
                        float normalizedX = (x + 0.5f) / gridSize;
                        float normalizedY = (y + 0.5f) / gridSize;
                        var worldPoint = new Vector3(
                            Mathf.Lerp(bounds.min.x, bounds.max.x, normalizedX),
                            Mathf.Lerp(bounds.min.y, bounds.max.y, normalizedY),
                            0f);
                        AddScreenCandidate(positions, camera.WorldToScreenPoint(worldPoint));
                    }
                }
            }

            return positions;
        }


        private static void AddWorldTouchCandidate(
            List<Vector2> positions,
            SpriteRenderer renderer,
            Camera camera,
            Vector3 worldPoint)
        {
            if (!IsOpaqueSpritePoint(renderer, worldPoint))
            {
                return;
            }

            Vector2 screenPosition = camera.WorldToScreenPoint(worldPoint);
            AddScreenCandidate(positions, screenPosition);
        }


        private static void AddScreenCandidate(List<Vector2> positions, Vector2 screenPosition)
        {
            if (!Escape.Input.GameScreenInputArea.Contains(screenPosition))
            {
                return;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                if ((positions[i] - screenPosition).sqrMagnitude < 1f)
                {
                    return;
                }
            }

            positions.Add(screenPosition);
        }


        private static bool IsOpaqueSpritePoint(SpriteRenderer renderer, Vector3 worldPoint)
        {
            Sprite sprite = renderer.sprite;
            if (sprite == null)
            {
                return false;
            }

            Vector3 localPoint = renderer.transform.InverseTransformPoint(worldPoint);
            float pixelX = localPoint.x * sprite.pixelsPerUnit + sprite.pivot.x;
            float pixelY = localPoint.y * sprite.pixelsPerUnit + sprite.pivot.y;
            Rect rect = sprite.rect;
            if (renderer.flipX)
            {
                pixelX = rect.width - pixelX;
            }

            if (renderer.flipY)
            {
                pixelY = rect.height - pixelY;
            }

            if (pixelX < 0f || pixelY < 0f || pixelX >= rect.width || pixelY >= rect.height)
            {
                return false;
            }

            try
            {
                int textureX = Mathf.FloorToInt(rect.x + pixelX);
                int textureY = Mathf.FloorToInt(rect.y + pixelY);
                return sprite.texture.GetPixel(textureX, textureY).a > 0.2f;
            }
            catch (UnityException)
            {
                return true;
            }
        }


        // UGUI 버튼의 실제 화면 좌표를 가상 터치한다.
        private IEnumerator TouchUiButton(
            Button button,
            Action afterReleaseBeforeResultFrame = null)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.gameObject.activeInHierarchy, Is.True);

            RectTransform rectTransform = button.transform as RectTransform;
            Assert.That(rectTransform, Is.Not.Null);
            Canvas canvas = button.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(rectTransform.rect.center));

            for (int attempt = 0;
                 attempt < 3 && button != null && button.gameObject.activeInHierarchy;
                 attempt++)
            {
                yield return null;
                yield return TouchScreen(
                    screenPosition,
                    afterReleaseBeforeResultFrame: afterReleaseBeforeResultFrame);
                afterReleaseBeforeResultFrame = null;

                float deadline = Time.realtimeSinceStartup + 1f;
                while (button != null &&
                       button.gameObject.activeInHierarchy &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }

            if (button != null && button.gameObject.activeInHierarchy)
            {
                yield return null;
            }

            Assert.That(button == null || !button.gameObject.activeInHierarchy, Is.True,
                $"선택지 버튼 터치가 처리되지 않았습니다: {screenPosition}");
        }


        // 한 손가락의 Began/Ended 이벤트를 서로 다른 프레임에 전달한다.
        private IEnumerator TouchScreen(
            Vector2 screenPosition,
            int holdFrames = 1,
            Action afterReleaseBeforeResultFrame = null)
        {
            Assert.That(virtualTouchscreen, Is.Not.Null);
            yield return MoveMouseTo(screenPosition);
            virtualTouchscreen.MakeCurrent();
            int touchId = nextTouchId++;
            InputSystem.QueueStateEvent(virtualTouchscreen, new TouchState
            {
                touchId = touchId,
                phase = UnityEngine.InputSystem.TouchPhase.Began,
                position = screenPosition,
                pressure = 1f,
            });
            // Began을 먼저 한 프레임 처리해야 CursorChaseController가 터치 시작 위치를 기록한다.
            yield return null;

            // 실제 플레이처럼 커서가 터치 지점까지 추적할 시간을 준다.
            for (int i = 0; i < holdFrames; i++)
            {
                InputSystem.QueueStateEvent(virtualTouchscreen, new TouchState
                {
                    touchId = touchId,
                    phase = UnityEngine.InputSystem.TouchPhase.Moved,
                    position = screenPosition,
                    pressure = 1f,
                });
                yield return null;
            }

            InputSystem.QueueStateEvent(virtualTouchscreen, new TouchState
            {
                touchId = touchId,
                phase = UnityEngine.InputSystem.TouchPhase.Ended,
                position = screenPosition,
                pressure = 0f,
            });
            afterReleaseBeforeResultFrame?.Invoke();
            yield return null;
        }


        // 배치 모드의 가상 장치 이벤트는 current 장치 상태에 반영되지 않을 수 있으므로,
        // DialoguePlayer의 개발 빌드 전용 QA 진행 신호로 현재 줄을 넘긴다.
        private IEnumerator TouchDialogueAdvance()
        {
            DialoguePlayer dialoguePlayer = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
            Assert.That(dialoguePlayer, Is.Not.Null);
            dialoguePlayer.RequestQaAdvance();
            yield return null;
        }


        // QA 실행기에서는 실제 커서를 목표 지점까지 천천히 옮겨 다음 입력 위치를 보여 준다.
        private IEnumerator MoveMouseTo(Vector2 screenPosition)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || qaToolMouseMoveSpeedPixelsPerSecond <= 0f)
            {
                yield break;
            }

            if (!qaMousePositionInitialized)
            {
                Rect gameRect = Escape.Input.GameScreenInputArea.GetScreenRect();
                Vector2 reportedPosition = mouse.position.ReadValue();
                qaMousePosition = gameRect.Contains(reportedPosition) && reportedPosition.sqrMagnitude > 1f
                    ? reportedPosition
                    : gameRect.center;
                qaMousePositionInitialized = true;
            }

            Vector2 startPosition = qaMousePosition;
            Vector2 movement = screenPosition - startPosition;
            float directDistance = movement.magnitude;
            if (directDistance <= 0.01f)
            {
                yield break;
            }

            Vector2 perpendicular = new Vector2(-movement.y, movement.x).normalized;
            float movementVariation = 0.8f +
                Mathf.Abs(Mathf.Sin(nextTouchId * 1.618f)) * 0.35f;
            float curveOffset = Mathf.Min(
                directDistance * QaMouseCurveRatio * movementVariation,
                QaMouseCurveMaxOffset);
            float overshootDistance = directDistance >= QaMouseOvershootMinDistance
                ? Mathf.Min(directDistance * QaMouseOvershootRatio, QaMouseOvershootMaxDistance)
                : 0f;
            Vector2 curveEndPosition = screenPosition + movement.normalized * overshootDistance;
            Rect screenRect = Escape.Input.GameScreenInputArea.GetScreenRect();
            curveEndPosition.x = Mathf.Clamp(curveEndPosition.x, screenRect.xMin, screenRect.xMax);
            curveEndPosition.y = Mathf.Clamp(curveEndPosition.y, screenRect.yMin, screenRect.yMax);
            Vector2 controlPoint = (startPosition + curveEndPosition) * 0.5f +
                perpendicular * (curveOffset * qaMouseCurveDirection);
            qaMouseCurveDirection *= -1f;

            var curvePoints = new Vector2[QaMouseCurveSegmentCount + 1];
            var cumulativeDistances = new float[QaMouseCurveSegmentCount + 1];
            curvePoints[0] = startPosition;
            for (int i = 1; i <= QaMouseCurveSegmentCount; i++)
            {
                float progress = i / (float)QaMouseCurveSegmentCount;
                curvePoints[i] = EvaluateQuadraticBezier(
                    startPosition,
                    controlPoint,
                    curveEndPosition,
                    progress);
                cumulativeDistances[i] = cumulativeDistances[i - 1] +
                    Vector2.Distance(curvePoints[i - 1], curvePoints[i]);
            }

            float curveDistance = cumulativeDistances[QaMouseCurveSegmentCount];
            float movementDuration = curveDistance / qaToolMouseMoveSpeedPixelsPerSecond;
            int segmentIndex = 1;
            float movementStartTime = Time.realtimeSinceStartup;
            float movementProgress = 0f;
            while (movementProgress < 1f)
            {
                movementProgress = Mathf.Clamp01(
                    (Time.realtimeSinceStartup - movementStartTime) / movementDuration);
                float travelledDistance = curveDistance * EvaluateHumanApproach(movementProgress);
                while (segmentIndex < QaMouseCurveSegmentCount &&
                       cumulativeDistances[segmentIndex] < travelledDistance)
                {
                    segmentIndex++;
                }

                float segmentStartDistance = cumulativeDistances[segmentIndex - 1];
                float segmentProgress = Mathf.InverseLerp(
                    segmentStartDistance,
                    cumulativeDistances[segmentIndex],
                    travelledDistance);
                Vector2 currentPosition = Vector2.Lerp(
                    curvePoints[segmentIndex - 1],
                    curvePoints[segmentIndex],
                    segmentProgress);
                qaMousePosition = currentPosition;
                mouse.WarpCursorPosition(currentPosition);
                yield return null;
            }

            float correctionDistance = Vector2.Distance(curveEndPosition, screenPosition);
            if (correctionDistance > 0.01f)
            {
                float correctionDuration = Mathf.Clamp(
                    correctionDistance / qaToolMouseMoveSpeedPixelsPerSecond * 8f,
                    0.05f,
                    0.12f);
                float correctionStartTime = Time.realtimeSinceStartup;
                float correctionProgress = 0f;
                while (correctionProgress < 1f)
                {
                    correctionProgress = Mathf.Clamp01(
                        (Time.realtimeSinceStartup - correctionStartTime) / correctionDuration);
                    Vector2 currentPosition = Vector2.Lerp(
                        curveEndPosition,
                        screenPosition,
                        Mathf.SmoothStep(0f, 1f, correctionProgress));
                    qaMousePosition = currentPosition;
                    mouse.WarpCursorPosition(currentPosition);
                    yield return null;
                }
            }

            qaMousePosition = screenPosition;
            mouse.WarpCursorPosition(screenPosition);
        }


        // 목표 거리를 초반에 빠르게 좁힌 뒤 마지막 구간을 정밀하게 감속한다.
        private static float EvaluateHumanApproach(float progress)
        {
            float biasedProgress = 1f - Mathf.Pow(1f - progress, QaMouseApproachBias);
            return EvaluateMinimumJerk(biasedProgress);
        }


        // 사람 손의 출발과 도착처럼 가속과 감속이 이어지는 최소 저크 진행률을 계산한다.
        private static float EvaluateMinimumJerk(float progress)
        {
            float squared = progress * progress;
            float cubed = squared * progress;
            return 10f * cubed - 15f * cubed * progress + 6f * cubed * squared;
        }


        // 세 점으로 정의한 2차 베지어 곡선의 위치를 계산한다.
        private static Vector2 EvaluateQuadraticBezier(
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float progress)
        {
            float inverse = 1f - progress;
            return inverse * inverse * start +
                2f * inverse * progress * control +
                progress * progress * end;
        }


        private static bool TryGetActiveChoiceButton(int index, out Button button)
        {
            button = null;
            DialoguePlayer dialoguePlayer = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
            if (dialoguePlayer == null)
            {
                return false;
            }

            FieldInfo panelField = typeof(DialoguePlayer).GetField(
                "panel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var panel = panelField?.GetValue(dialoguePlayer) as DialoguePopupUI;
            if (panel == null)
            {
                return false;
            }

            FieldInfo field = typeof(DialoguePopupUI).GetField(
                "selectButtons",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var buttons = field?.GetValue(panel) as Button[];
            if (buttons == null || index < 0 || index >= buttons.Length)
            {
                return false;
            }

            button = buttons[index];
            return button != null && button.gameObject.activeInHierarchy && button.interactable;
        }


        private static IEnumerator WaitForScene(string sceneName, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(sceneName),
                $"씬 로드 제한 시간 {timeout:0}초 초과: {sceneName}");
        }


        private static IEnumerator WaitForObject<T>(float timeout) where T : UnityEngine.Object
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (UnityEngine.Object.FindFirstObjectByType<T>() == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(UnityEngine.Object.FindFirstObjectByType<T>(), Is.Not.Null,
                $"씬에서 {typeof(T).Name}을 찾지 못했습니다.");
        }


        private static bool IsDialoguePlaying()
        {
            DialoguePlayer dialoguePlayer = UnityEngine.Object.FindFirstObjectByType<DialoguePlayer>();
            return dialoguePlayer != null && dialoguePlayer.IsPlaying;
        }


        private static RoomController RequireRoomController()
        {
            RoomController roomController = UnityEngine.Object.FindFirstObjectByType<RoomController>();
            Assert.That(roomController, Is.Not.Null, "RoomController가 필요합니다.");
            return roomController;
        }


        private static T FindSceneComponent<T>(string objectName) where T : Component
        {
            T inactiveMatch = null;
            foreach (T candidate in Resources.FindObjectsOfTypeAll<T>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    string.Equals(candidate.gameObject.name, objectName, StringComparison.Ordinal))
                {
                    if (candidate.gameObject.activeInHierarchy)
                    {
                        return candidate;
                    }

                    inactiveMatch ??= candidate;
                }
            }

            return inactiveMatch;
        }


        private static IEnumerable<T> FindSceneComponents<T>(string objectName) where T : Component
        {
            foreach (T candidate in Resources.FindObjectsOfTypeAll<T>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    string.Equals(candidate.gameObject.name, objectName, StringComparison.Ordinal))
                {
                    yield return candidate;
                }
            }
        }
    }
}
#endif
