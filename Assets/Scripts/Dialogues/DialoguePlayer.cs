using System;
using Escape.SceneFlow;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.Audio;
using Escape.Data;
using Escape.Rooms;
using Escape.UI;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Escape.Dialogues
{
    // 대사 팝업과 타이핑 효과, 초상화 상태를 제어한다.
    [MovedFrom(true, "Escape.Managers", null, "DialogueManager")]
    public sealed class DialoguePlayer : MonoBehaviour
    {
        private const string TimingCheckSuccessFlag = "TIMING_CHECK_SUCC";
        private const string TimingCheckFailureFlag = "TIMING_CHECK_FAIL";
        private const string DefaultTypingSfx = "dialogue_type";
        private const string BlackFadeOnEffect = "검정ON";
        private const string BlackFadeOffEffect = "검정OFF";
        private const string SlowBlackFadeOnEffect = "느린검정ON";
        private const string SlowBlackFadeOffEffect = "느린검정OFF";
        private const string InstantBlackOnEffect = "즉시검정ON";
        private const string InstantBlackOffEffect = "즉시검정OFF";
        private const string WhiteFadeOnEffect = "하양ON";
        private const string WhiteFadeOffEffect = "하양OFF";
        private const string VectorBlurEffect = "백터블러ON";
        private const string UpperRightVectorBlurEffect = "우상백터블러ON";
        private const string VectorBlurAlias = "벡터블러ON";
        private const string UpperRightVectorBlurAlias = "우상벡터블러ON";
        private const string VectorBlurOffEffect = "백터블러OFF";
        private const string VectorBlurOffAlias = "벡터블러OFF";
        private const string FullscreenNoiseEffect = "전체노이즈";
        private const string FullscreenNoiseAlias = "전체화면노이즈";
        private const string FullscreenNoiseOnEffect = "전체노이즈ON";
        private const string FullscreenNoiseOnAlias = "전체화면노이즈ON";
        private const string FullscreenNoiseOffEffect = "전체노이즈OFF";
        private const string FullscreenNoiseOffAlias = "전체화면노이즈OFF";
        private const string BgmEffectPrefix = "BGM=";
        private const string BgmOffEffect = "BGM_OFF";
        private const string BgmNoneEffect = "none";
        private const string BgmSegmentLoopEffect = "BGM구간반복";
        private const string BgmSegmentLoopPrefix = BgmSegmentLoopEffect + "=";
        private const string BgmSegmentLoopOffEffect = BgmSegmentLoopEffect + "OFF";
        private const int DefaultBgmSegmentLoopMilliseconds = 100;
        private const string RoomMoveEffectPrefix = "방이동=";
        private const string RoomMoveEffectAliasPrefix = "ROOM=";
        private const string SceneObjectHideEffectPrefix = "숨김=";
        private const string SceneObjectShowEffectPrefix = "보임=";
        private const string ItemAcquireEffectPrefix = "아이템획득=";
        private const string ItemRemoveEffectPrefix = "아이템제거=";
        private const string InfoAcquireEffectPrefix = "정보획득=";
        private const string AutoAdvanceEffect = "자동";
        private const string EffectWaitPrefix = "대기=";
        private const string YeonSpeakerId = "yeon";
        private const string HiddenYeonSpeakerId = "yeon_hidden";

        private enum DialogueEffectTiming
        {
            Before,
            During,
            After,
        }

        public static DialoguePlayer Instance { get; private set; }

        [SerializeField] private DialoguePopupUI panel;
        [SerializeField, FormerlySerializedAs("roomManager")] private MonoBehaviour worldEffectsSource;
        [SerializeField] private TimingCheckPopupUI timingCheckPopup;
        [SerializeField] private EndingCreditRollUI endingCreditRollUI;
        [SerializeField] private RoomPostEffectController roomPostEffectController;
        [SerializeField] private SpriteRenderer backgroundRenderer;
        [SerializeField] private Color inactiveYeonPortraitTint = new(0.19f, 0.19f, 0.22f, 1f);
        [SerializeField, Min(0f)] private float charactersPerSecond = 36f;
        [SerializeField, Min(1f)] private float fastForwardMultiplier = 8f;
        [SerializeField] private bool playTypingSound = true;
        [SerializeField, HideInInspector] private bool hideOnAwake = true;
        [SerializeField, HideInInspector, Min(0.01f)] private float portraitBounceDuration = 0.22f;
        [SerializeField, HideInInspector, Min(0f)] private float portraitBounceWorldHeight = 0.14f;
        [SerializeField, HideInInspector, Min(0f)] private float portraitBounceUiHeight = 36f;
        [SerializeField, HideInInspector, Min(0.01f)] private float portraitFadeDuration = 0.28f;
        [SerializeField, HideInInspector, Min(0.01f)] private float portraitSlideDuration = 0.45f;
        [SerializeField, HideInInspector, Min(0.01f)] private float portraitDramaticSlideDuration = 0.18f;
        [SerializeField, HideInInspector, Min(0f)] private float portraitSlideWorldDistance = 2.4f;
        [SerializeField, HideInInspector, Min(0f)] private float portraitSlideUiDistance = 260f;
        [SerializeField, HideInInspector, Min(0f)] private float portraitDramaticShakeWorldAmount = 0.08f;
        [SerializeField, HideInInspector, Min(0f)] private float portraitDramaticShakeUiAmount = 12f;
        [SerializeField, HideInInspector, Min(1f)] private float portraitDramaticShakeFrequency = 36f;

        private DialogueLine[] lines = System.Array.Empty<DialogueLine>();
        private int lineIndex = -1;
        private CancellationTokenSource typeCts;
        private CancellationTokenSource effectCts;
        private CancellationTokenSource autoAdvanceCts;
        private CancellationTokenSource storyCts;
        private string fullText = string.Empty;
        private string visibleText = string.Empty;
        private string lineTypingSfx = string.Empty;
        private float lineCharactersPerSecond;
        private Sprite currentPortraitSprite;
        private string currentPortraitSpeakerId = string.Empty;
        private Color currentPortraitTint = Color.white;
        private bool isTyping;
        private bool isEffectPlaying;
        private int activeLineEffectCount;
        private bool isStoryEffectPlaying;
        private bool isAutoAdvancingEffectOnlyLine;
        private int playStartedFrame = -1;
        private int advanceInputConsumedFrame = -1;
        private Transform effectTarget;
        private Vector3 effectOriginalLocalPosition;
        private Vector3 effectOriginalLocalScale;
        private bool hasCloseupState;
        private readonly List<InlineSfxCue> lineSfxCues = new();
        private readonly List<InlineWaitCue> lineWaitCues = new();
        private readonly List<InlineShakeCue> lineShakeCues = new();
        private readonly List<SpriteAlphaState> effectSpriteAlphaStates = new();
        private readonly List<GraphicAlphaState> effectGraphicAlphaStates = new();
        private bool hidePanelOnComplete = true;
        private IDialogueRoomEffects roomEffects;
        private IDialogueScreenEffectPlayer screenEffectPlayer;

        // 한 dialogue 재생 동안 유지하는 상태(선택으로 획득한 플래그, 결과, 선택 대기 여부).
        private readonly HashSet<string> activeStoryFlags = new(StringComparer.Ordinal);
        private string storyDialogueId = string.Empty;
        private GameSession storyState;
        private DialogueStoryResult storyResult;
        private bool waitingForChoice;
        private bool storyEndRequested;

        public bool IsPlaying => lineIndex >= 0;
        // 현재 줄의 텍스트와 연출이 끝나 실제 진행 입력을 받을 수 있는지 반환한다.
        public bool IsWaitingForManualAdvance =>
            IsPlaying &&
            !waitingForChoice &&
            !isTyping &&
            !isEffectPlaying &&
            !isStoryEffectPlaying &&
            lineIndex < lines.Length &&
            lines[lineIndex] != null &&
            !string.IsNullOrWhiteSpace(visibleText) &&
            !ContainsEffectToken(lines[lineIndex].Effect, AutoAdvanceEffect);
        // 재생이 끝난 뒤 호출부가 선택 플래그·게임오버 요청을 읽는다.
        public DialogueStoryResult CurrentStoryResult => storyResult;
        public bool ConsumedAdvanceInputThisFrame => advanceInputConsumedFrame == Time.frameCount;
        public Sprite CurrentPortraitSprite => currentPortraitSprite;
        public event System.Action<Sprite> PortraitSpriteChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            ResolveWorldEffects();

            if (panel == null)
            {
                panel = FindFirstObjectByType<DialoguePopupUI>(FindObjectsInactive.Include);
            }

            if (hideOnAwake)
            {
                panel?.Hide();
            }

            LocalizationService.Ensure().LanguageChanged += RefreshCurrentLocalization;
        }

        private void OnDestroy()
        {
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= RefreshCurrentLocalization;
            }

            CancelType();
            CancelAutoAdvance();
            CancelEffect();
            storyCts?.Cancel();
            storyCts?.Dispose();
            storyCts = null;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        // 진행 중인 타이핑/자동진행/연출 UniTask를 취소·정리한다(코루틴의 StopCoroutine 대체).
        private void CancelType()
        {
            typeCts?.Cancel();
            typeCts?.Dispose();
            typeCts = null;
        }

        private void CancelAutoAdvance()
        {
            autoAdvanceCts?.Cancel();
            autoAdvanceCts?.Dispose();
            autoAdvanceCts = null;
        }

        private void CancelEffect()
        {
            effectCts?.Cancel();
            effectCts?.Dispose();
            effectCts = null;
        }
        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (IsPlaying && Time.frameCount != playStartedFrame && WasStorySkipPressed())
            {
                SkipCurrentStoryForDevelopment();
                return;
            }
#endif

            // 진행 자체는 RunStoryAsync의 대기 루프가 처리한다. 여기서는 이 프레임의 진행 입력이
            // 대화에 소비됐음을 표시해, RoomController가 같은 클릭을 방 조사로 중복 처리하지 않게 한다.
            if (IsPlaying && !waitingForChoice && Time.frameCount != playStartedFrame && WasAdvancePressed())
            {
                advanceInputConsumedFrame = Time.frameCount;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // F1 개발 단축키로 현재 story의 남은 대사·선택지·연출을 취소하고 완료 상태로 넘긴다.
        private void SkipCurrentStoryForDevelopment()
        {
            storyCts?.Cancel();
            storyCts?.Dispose();
            storyCts = null;
            CompleteStory();
        }
#endif

        // 언어가 바뀌면 현재 본문·화자명 또는 선택지를 효과 재실행 없이 갱신한다.
        private void RefreshCurrentLocalization()
        {
            if (panel == null || lineIndex < 0 || lineIndex >= lines.Length)
            {
                return;
            }

            if (waitingForChoice)
            {
                var choiceLines = new List<DialogueLine>();
                int cursor = lineIndex;
                while (cursor < lines.Length && lines[cursor] != null && lines[cursor].IsChoice)
                {
                    choiceLines.Add(lines[cursor]);
                    cursor++;
                }

                choiceLines.Sort((left, right) =>
                    ParseChoiceOrder(left.DialogueType).CompareTo(ParseChoiceOrder(right.DialogueType)));
                var labels = new string[choiceLines.Count];
                for (int i = 0; i < choiceLines.Count; i++)
                {
                    labels[i] = choiceLines[i].ResolveText();
                }

                panel.RefreshChoiceLabels(labels);
                return;
            }

            DialogueLine line = lines[lineIndex];
            if (line == null)
            {
                return;
            }

            if (isTyping)
            {
                CancelType();
                isTyping = false;
            }

            fullText = ExtractInlineControlTags(
                line.ResolveText(),
                lineSfxCues,
                lineWaitCues,
                lineShakeCues,
                out string typingSfxOverride,
                out int typingMillisecondsOverride);
            lineTypingSfx = string.IsNullOrWhiteSpace(typingSfxOverride)
                ? line.TypingSfx
                : typingSfxOverride;
            lineCharactersPerSecond = typingMillisecondsOverride > 0
                ? 1000f / typingMillisecondsOverride
                : charactersPerSecond;
            visibleText = StripRichTextTags(fullText);
            panel.SetSpeaker(line.ResolveSpeakerName());
            panel.SetBody(fullText);
            panel.SetBodyType(line.DialogueType);
            panel.SetBodyVisibleCharacters(int.MaxValue);
        }

        public void Configure(DialoguePopupUI dialoguePanel)
        {
            panel = dialoguePanel;
        }

        // Room 씬이 제공하는 방 상태와 화면 효과 구현을 연결한다.
        internal void Configure(
            DialoguePopupUI dialoguePanel,
            IDialogueRoomEffects roomEffects,
            IDialogueScreenEffectPlayer screenEffectPlayer)
        {
            panel = dialoguePanel;
            this.roomEffects = roomEffects;
            this.screenEffectPlayer = screenEffectPlayer;
        }

        // 기존 직렬화 참조에서 방 상태 effect 구현을 복원한다.
        private void ResolveWorldEffects()
        {
            roomEffects ??= worldEffectsSource as IDialogueRoomEffects;
        }

        public void Play(string speaker, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Stop();
                return;
            }

            Play(new[] { new DialogueLine(speaker, text) });
        }

        public void Play(DialogueLine[] dialogueLines)
        {
            Play(dialogueLines, true);
        }

        // 연속 대사가 끝났을 때 팝업 루트를 닫을지 선택해 재생한다.
        public void Play(DialogueLine[] dialogueLines, bool hidePanelWhenComplete)
        {
            Play(dialogueLines, hidePanelWhenComplete, string.Empty);
        }

        // 한 dialogue의 전체 라인을 받아 조건 스킵·선택지 분기·게임오버 판정을 내부에서 처리한다.
        // dialogueId는 영속 플래그 조건을 판정할 때 사용한다.
        public void Play(DialogueLine[] dialogueLines, bool hidePanelWhenComplete, string dialogueId)
        {
            lines = dialogueLines ?? System.Array.Empty<DialogueLine>();
            hidePanelOnComplete = hidePanelWhenComplete;
            storyDialogueId = dialogueId ?? string.Empty;
            storyState = PlayerInventory.Instance != null ? PlayerInventory.Instance.State : null;
            activeStoryFlags.Clear();
            storyResult = new DialogueStoryResult(storyDialogueId);
            waitingForChoice = false;
            storyEndRequested = false;
            if (lines.Length == 0)
            {
                Stop();
                return;
            }

            lineIndex = 0;
            playStartedFrame = Time.frameCount;
            // 스토리당 배경 초기화는 여기서 한 번만. 이후 라인들은 배경을 유지/변경한다.
            ClearBackground();

            storyCts?.Cancel();
            storyCts?.Dispose();
            storyCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            RunStoryAsync(storyCts.Token).Forget();
        }

        // 전체 대화를 종료하고 패널과 포트레이트를 정리한다.
        public void Stop()
        {
            storyCts?.Cancel();
            storyCts?.Dispose();
            storyCts = null;
            SoundPlayer.StopStoryBgm();
            ResetPlaybackState();
            ClearPortrait();
            ClearBackground();
            panel?.Hide();
        }

        // 선택지 지점에서는 대화를 닫지 않고 다음 흐름을 기다리는 상태로 전환한다.
        private void PauseForChoice()
        {
            ResetPlaybackState();
            panel?.HideChoices();
        }

        // 현재 줄 재생에 사용한 타이핑·효과 상태만 정리한다.
        private void ResetPlaybackState()
        {
            CancelType();
            CancelAutoAdvance();

            StopEffect(true);

            isTyping = false;
            isAutoAdvancingEffectOnlyLine = false;
            lineIndex = -1;
            lines = System.Array.Empty<DialogueLine>();
            lineSfxCues.Clear();
            lineWaitCues.Clear();
            lineShakeCues.Clear();
            lineTypingSfx = string.Empty;
            lineCharactersPerSecond = 0f;
            storyEndRequested = false;
            panel?.StopBodyShake();
            panel?.SetWaitingForInput(false);
            hidePanelOnComplete = true;
        }

        // 한 dialogue의 전체 라인을 for문으로 순회한다. 조건 불충족 줄은 건너뛰고,
        // 선택지 묶음은 선택될 때까지, 일반 줄은 타이핑·연출이 끝나고 진행 입력이 올 때까지 await 한다.
        private async UniTaskVoid RunStoryAsync(CancellationToken ct)
        {
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    lineIndex = i; // IsPlaying 유지 및 진행 위치 표시
                    DialogueLine line = lines[i];

                    if (line != null && line.IsChoice)
                    {
                        int resumeIndex = await ShowChoicesAsync(i, ct);
                        i = resumeIndex - 1;
                        continue;
                    }

                    if (!IsLineFlagSatisfied(line))
                    {
                        continue;
                    }

                    await PlayLineAsync(line, ct);
                    if (storyEndRequested)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return; // Stop() 또는 새 Play()로 취소됨
            }

            CompleteStory();
        }

        // 스토리 종료 처리. 독립 재생이면 완전히 닫고, 이어지는 재생이면 선택 대기 상태로 둔다.
        private void CompleteStory()
        {
            if (hidePanelOnComplete)
            {
                Stop();
            }
            else
            {
                // 호출부가 패널을 이어서 관리해도 끝난 스토리의 BGM 오버라이드는 즉시 해제한다.
                SoundPlayer.StopStoryBgm();
                PauseForChoice();
            }
        }

        // 한 줄을 준비한 뒤 대사전, 대사중, 대사후 효과를 순서대로 재생한다.
        private async UniTask PlayLineAsync(DialogueLine line, CancellationToken ct)
        {
            PrepareLine(line);
            isAutoAdvancingEffectOnlyLine = true;
            try
            {
                string beforeEffects = SelectEffectsByTiming(line.Effect, DialogueEffectTiming.Before);
                StartLineEffect(beforeEffects, string.Empty);
                StartStoryEffects(beforeEffects, ct);
                await WaitEffectsCompleteAsync(ct);

                string duringEffects = SelectEffectsByTiming(line.Effect, DialogueEffectTiming.During);
                StartLineEffect(duringEffects, line.Shader);
                StartStoryEffects(duringEffects, ct);
                StartLineTyping();
                await WaitTypingAndEffectsCompleteAsync(ct);

                string afterEffects = SelectEffectsByTiming(line.Effect, DialogueEffectTiming.After);
                StartLineEffect(afterEffects, string.Empty);
                StartStoryEffects(afterEffects, ct);
                await WaitEffectsCompleteAsync(ct);
            }
            finally
            {
                isAutoAdvancingEffectOnlyLine = false;
            }

            await WaitLineAdvanceAsync(line, ct);
        }

        // 배경과 초상화, 본문 데이터를 설정하되 타이핑과 효과는 아직 시작하지 않는다.
        private void PrepareLine(DialogueLine line)
        {
            if (storyResult != null && LineRequestsGameOver(line))
            {
                storyResult.RequestGameOver();
            }

            CancelType();
            CancelAutoAdvance();
            StopEffect(true);

            ApplyBackground(line.BackgroundPath);
            ApplyLineBgm(line.Bgm);
            fullText = ExtractInlineControlTags(
                line.ResolveText(),
                lineSfxCues,
                lineWaitCues,
                lineShakeCues,
                out string typingSfxOverride,
                out int typingMillisecondsOverride);
            lineTypingSfx = string.IsNullOrWhiteSpace(typingSfxOverride)
                ? line.TypingSfx
                : typingSfxOverride;
            lineCharactersPerSecond = typingMillisecondsOverride > 0
                ? 1000f / typingMillisecondsOverride
                : charactersPerSecond;
            visibleText = StripRichTextTags(fullText);
            panel?.Show(line.ResolveSpeakerName(), fullText, line.DialogueType);
            panel?.SetBodyVisibleCharacters(0);
            panel?.SetWaitingForInput(false);
            panel?.ShowPortrait(line.PortraitSprite, line.PortraitTint, line.PortraitScale);
            ApplyPersistentCloseup();
            if (line.PortraitSprite != null)
            {
                currentPortraitSpeakerId = line.SpeakerId;
                currentPortraitTint = line.PortraitTint;
                SetCurrentPortrait(line.PortraitSprite);
            }

            ApplyActiveSpeakerPortraitTint(line.SpeakerId);
        }

        // 준비된 본문 타이핑과 첫 글자 위치의 인라인 SFX를 시작한다.
        private void StartLineTyping()
        {
            panel?.SetWaitingForInput(false);
            PlayInlineSfxCues(0, 0);
            PlayInlineShakeCues(0, 0);

            // 개발용 Ctrl 빠른 진행 중에는 타이핑 애니메이션과 인라인 대기를 생략한다.
            if (IsFastForwardActive())
            {
                CompleteInlineWaitCues();
                FinishTyping();
                return;
            }

            // 즉시표시(cps<=0)거나 본문이 없으면 타이핑을 건너뛴다.
            if (lineCharactersPerSecond <= 0f || string.IsNullOrEmpty(visibleText))
            {
                FinishTyping();
                return;
            }

            typeCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            TypeLine(typeCts.Token).Forget();
        }

        // 타이핑과 대사중 효과가 모두 끝날 때까지 기다리며, 진행 입력은 타이핑 완료에만 사용한다.
        private async UniTask WaitTypingAndEffectsCompleteAsync(CancellationToken ct)
        {
            // 이 줄을 시작시킨 클릭을 그대로 소비하지 않도록 한 프레임 양보한다.
            await UniTask.Yield(PlayerLoopTiming.Update, ct);

            while (isTyping || isEffectPlaying || isStoryEffectPlaying)
            {
                ct.ThrowIfCancellationRequested();

                if (isTyping)
                {
                    if (IsFastForwardActive())
                    {
                        SkipTypingToEnd();
                    }
                    else if (WasAdvancePressed())
                    {
                        advanceInputConsumedFrame = Time.frameCount;
                        SkipTypingToEnd();
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    continue;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        // 대사전·대사후처럼 타이핑과 분리된 효과가 끝날 때까지 기다린다.
        private async UniTask WaitEffectsCompleteAsync(CancellationToken ct)
        {
            while (isEffectPlaying || isStoryEffectPlaying)
            {
                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        // 효과가 모두 끝난 뒤 일반 본문은 진행 입력을 기다리고, 빈 본문이나 `자동` 줄은 자동 진행한다.
        private async UniTask WaitLineAdvanceAsync(DialogueLine line, CancellationToken ct)
        {
            bool autoAdvance = line != null &&
                               (string.IsNullOrWhiteSpace(visibleText) ||
                                ContainsEffectToken(line.Effect, AutoAdvanceEffect));
            if (autoAdvance || IsFastForwardActive())
            {
                return;
            }

            panel?.SetWaitingForInput(true);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (WasAdvancePressed())
                {
                    advanceInputConsumedFrame = Time.frameCount;
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        // 줄의 flag 조건이 만족되는지 확인한다(이번 스토리에서 선택으로 획득했거나 영속 플래그로 보유).
        private bool IsLineFlagSatisfied(DialogueLine line)
        {
            string flagExpression = line != null ? (line.Flag ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(flagExpression))
            {
                return true;
            }

            string[] tokens = SplitFlagTokens(flagExpression);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (activeStoryFlags.Contains(token))
                {
                    continue;
                }

                if (storyState != null && storyState.HasDialogueFlag(storyDialogueId, token))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        // startIndex부터 이어지는 선택지 줄을 모아 패널에 띄우고, 선택될 때까지 await 한다.
        // 선택된 flag를 이번 스토리에 반영하고, 선택지 묶음 다음 줄 인덱스를 돌려준다.
        private async UniTask<int> ShowChoicesAsync(int startIndex, CancellationToken ct)
        {
            CancelType();
            CancelAutoAdvance();

            var choiceLines = new List<DialogueLine>();
            int cursor = startIndex;
            while (cursor < lines.Length && lines[cursor] != null && lines[cursor].IsChoice)
            {
                choiceLines.Add(lines[cursor]);
                cursor++;
            }

            int resumeIndex = cursor;
            choiceLines.Sort((left, right) =>
                ParseChoiceOrder(left.DialogueType).CompareTo(ParseChoiceOrder(right.DialogueType)));

            var labels = new string[choiceLines.Count];
            for (int i = 0; i < choiceLines.Count; i++)
            {
                labels[i] = choiceLines[i].ResolveText();
            }

            var selection = new UniTaskCompletionSource<int>();
            waitingForChoice = true;
            panel?.SetVisible(true);
            panel?.SetWaitingForInput(false);
            panel?.ShowChoices(labels, index => selection.TrySetResult(index));

            int selected;
            try
            {
                selected = await selection.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                waitingForChoice = false;
            }

            if (selected >= 0 && selected < choiceLines.Count)
            {
                string flag = (choiceLines[selected].Flag ?? string.Empty).Trim();
                AddStoryFlag(flag);
            }

            return resumeIndex;
        }

        private static int ParseChoiceOrder(string type)
        {
            string value = (type ?? string.Empty).Trim();
            const string prefix = "SELECT_";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return int.MaxValue;
            }

            return int.TryParse(value.Substring(prefix.Length), out int order) ? order : int.MaxValue;
        }

        // 대사 텍스트에 배드엔딩/게임오버 표식이 있으면 게임오버를 요청한다.
        private static bool LineRequestsGameOver(DialogueLine line)
        {
            string text = line != null ? line.ResolveText() : string.Empty;
            return text.IndexOf("BAD ENDING", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("GAME OVER", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 복합 flag 표기(+ , | &)를 개별 토큰으로 나눈다.
        private static string[] SplitFlagTokens(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return System.Array.Empty<string>();
            }

            string[] rawTokens = value.Split(new[] { '+', ',', '|', '&' }, StringSplitOptions.RemoveEmptyEntries);
            var tokens = new List<string>();
            for (int i = 0; i < rawTokens.Length; i++)
            {
                string token = rawTokens[i].Trim();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    tokens.Add(token);
                }
            }

            return tokens.ToArray();
        }

        // bg_path가 비어 있으면 현재 배경을 유지하고, none이면 배경을 즉시 숨긴다.
        private void ApplyBackground(string resourcePath)
        {
            string normalizedPath = (resourcePath ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return;
            }

            if (string.Equals(normalizedPath, "none", StringComparison.OrdinalIgnoreCase))
            {
                ClearBackground();
                return;
            }

            const string resourcesPrefix = "Resources/";
            if (normalizedPath.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath.Substring(resourcesPrefix.Length);
            }

            int extensionIndex = normalizedPath.LastIndexOf('.');
            if (extensionIndex > normalizedPath.LastIndexOf('/'))
            {
                normalizedPath = normalizedPath.Substring(0, extensionIndex);
            }

            normalizedPath = normalizedPath.Trim('/');
            Sprite sprite = Resources.Load<Sprite>(normalizedPath);
            if (sprite == null)
            {
                Sprite[] sprites = Resources.LoadAll<Sprite>(normalizedPath);
                sprite = sprites != null && sprites.Length > 0 ? sprites[0] : null;
            }

            if (sprite == null)
            {
                Debug.LogWarning($"Dialogue background sprite not found at Resources/{normalizedPath}.");
                ClearBackground();
                return;
            }

            if (backgroundRenderer != null)
            {
                backgroundRenderer.sprite = sprite;
                backgroundRenderer.enabled = true;
            }
        }

        // 대화 UI를 완전히 닫을 때 유지 중인 배경 상태를 정리한다.
        public void ClearBackground()
        {
            if (backgroundRenderer == null)
            {
                return;
            }

            backgroundRenderer.sprite = null;
            backgroundRenderer.enabled = false;
        }

        private async UniTaskVoid TypeLine(CancellationToken ct)
        {
            isTyping = true;
            float visibleCharacters = 0f;
            int previousVisibleCount = 0;

            try
            {
                while (visibleCharacters < visibleText.Length)
                {
                    visibleCharacters += GetCurrentTypingSpeed() * Time.deltaTime;
                    int targetCount = Mathf.Clamp(Mathf.FloorToInt(visibleCharacters), 0, visibleText.Length);
                    int waitCueIndex = FindInlineWaitCue(previousVisibleCount, targetCount);
                    int count = waitCueIndex >= 0
                        ? lineWaitCues[waitCueIndex].VisibleIndex
                        : targetCount;

                    if (count > previousVisibleCount)
                    {
                        panel?.SetBodyVisibleCharacters(count);
                        PlayInlineSfxCues(previousVisibleCount, count);
                        PlayInlineShakeCues(previousVisibleCount, count);
                        PlayTypingSounds(previousVisibleCount, count);
                        previousVisibleCount = count;
                    }

                    if (waitCueIndex >= 0 && previousVisibleCount == lineWaitCues[waitCueIndex].VisibleIndex)
                    {
                        await PlayInlineWaitCue(waitCueIndex, ct);
                        continue;
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            FinishTyping();
        }

        private void PlayTypingSounds(int previousVisibleCount, int nextVisibleCount)
        {
            if (!playTypingSound || nextVisibleCount <= previousVisibleCount)
            {
                return;
            }

            int startIndex = Mathf.Clamp(previousVisibleCount, 0, visibleText.Length);
            int endIndex = Mathf.Clamp(nextVisibleCount, 0, visibleText.Length);
            for (int i = startIndex; i < endIndex; i++)
            {
                if (ShouldPlayTypingSound(visibleText[i]))
                {
                    string typingSfx = string.IsNullOrWhiteSpace(lineTypingSfx)
                        ? DefaultTypingSfx
                        : lineTypingSfx;
                    SoundPlayer.PlaySfx(typingSfx, false);

                    return;
                }
            }
        }

        private static bool ShouldPlayTypingSound(char character)
        {
            return character != ' ' &&
                   character != '\t' &&
                   character != '\n' &&
                   character != '\r' &&
                   !char.IsWhiteSpace(character);
        }

        private static string StripRichTextTags(string text)
        {
            text ??= string.Empty;
            var builder = new StringBuilder(text.Length);
            bool isInsideTag = false;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '<')
                {
                    isInsideTag = true;
                    continue;
                }

                if (character == '>' && isInsideTag)
                {
                    isInsideTag = false;
                    continue;
                }

                if (!isInsideTag)
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private void FinishTyping()
        {
            typeCts?.Dispose();
            typeCts = null;

            isTyping = false;
            PlayInlineSfxCues(0, int.MaxValue);
            PlayInlineShakeCues(0, int.MaxValue);
            panel?.SetBodyVisibleCharacters(int.MaxValue);
            panel?.SetWaitingForInput(IsPlaying && !isEffectPlaying && !isAutoAdvancingEffectOnlyLine);
        }

        // 타이핑 도중 진행 입력을 받으면 현재 줄 전체를 즉시 보여주고 다음 입력을 기다린다.
        private void SkipTypingToEnd()
        {
            if (!isTyping)
            {
                return;
            }

            CancelType();
            CompleteInlineWaitCues();
            FinishTyping();
        }

        private void CompleteInlineWaitCues()
        {
            for (int i = 0; i < lineWaitCues.Count; i++)
            {
                InlineWaitCue cue = lineWaitCues[i];
                cue.Played = true;
                lineWaitCues[i] = cue;
            }
        }

        // 글자 출력은 timeScale을 따르며, Ctrl 입력 시 추가로 빠르게 처리한다.
        private float GetCurrentTypingSpeed()
        {
            return lineCharactersPerSecond * GetCurrentFastForwardMultiplier();
        }

        private float GetCurrentFastForwardMultiplier()
        {
            return IsFastForwardActive() ? fastForwardMultiplier : 1f;
        }

        // 본문 안의 제어 태그를 제거하고, 해당 지점의 효과음·대기·흔들림 큐를 기록한다.
        private static string ExtractInlineControlTags(
            string text,
            List<InlineSfxCue> sfxCues,
            List<InlineWaitCue> waitCues,
            List<InlineShakeCue> shakeCues,
            out string typingSfxOverride,
            out int typingMillisecondsOverride)
        {
            sfxCues.Clear();
            waitCues.Clear();
            shakeCues.Clear();
            typingSfxOverride = string.Empty;
            typingMillisecondsOverride = 0;

            text ??= string.Empty;
            var builder = new StringBuilder(text.Length);
            int visibleIndex = 0;
            int cueOrder = 0;
            bool isInsideRichTextTag = false;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '<' && TryReadSfxTag(text, i, out string sfxId, out int tagEndIndex))
                {
                    sfxCues.Add(new InlineSfxCue(visibleIndex, cueOrder++, sfxId));
                    i = tagEndIndex;
                    continue;
                }

                if (text[i] == '<' && TryReadWaitTag(text, i, out float waitSeconds, out tagEndIndex))
                {
                    waitCues.Add(new InlineWaitCue(visibleIndex, cueOrder++, waitSeconds));
                    i = tagEndIndex;
                    continue;
                }

                if (text[i] == '<' && TryReadTypingSfxTag(text, i, out string typingSfxId, out tagEndIndex))
                {
                    typingSfxOverride = typingSfxId;
                    i = tagEndIndex;
                    continue;
                }

                if (text[i] == '<' && TryReadTypingMillisecondsTag(text, i, out int milliseconds, out tagEndIndex))
                {
                    typingMillisecondsOverride = milliseconds;
                    i = tagEndIndex;
                    continue;
                }

                if (text[i] == '<' && TryReadMarkerTag(text, i, "shake", out tagEndIndex))
                {
                    shakeCues.Add(new InlineShakeCue(visibleIndex, cueOrder++));
                    i = tagEndIndex;
                    continue;
                }

                char character = text[i];
                builder.Append(character);

                if (character == '<')
                {
                    isInsideRichTextTag = true;
                    continue;
                }

                if (character == '>' && isInsideRichTextTag)
                {
                    isInsideRichTextTag = false;
                    continue;
                }

                if (!isInsideRichTextTag)
                {
                    visibleIndex++;
                }
            }

            return builder.ToString();
        }

        // 현재 타이핑 구간 안에 있는 다음 대기 큐를 찾는다.
        private int FindInlineWaitCue(int previousVisibleCount, int nextVisibleCount)
        {
            for (int i = 0; i < lineWaitCues.Count; i++)
            {
                InlineWaitCue cue = lineWaitCues[i];
                if (!cue.Played &&
                    cue.VisibleIndex >= previousVisibleCount &&
                    cue.VisibleIndex <= nextVisibleCount)
                {
                    return i;
                }
            }

            return -1;
        }

        // <ms=...>로 지정된 시간만큼 타이핑을 잠시 멈춘다.
        private async UniTask PlayInlineWaitCue(int cueIndex, CancellationToken ct)
        {
            InlineWaitCue cue = lineWaitCues[cueIndex];
            cue.Played = true;
            lineWaitCues[cueIndex] = cue;

            float elapsed = 0f;
            while (elapsed < cue.Seconds)
            {
                elapsed += Time.deltaTime * GetCurrentFastForwardMultiplier();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        // 현재 타이핑 구간에 도달한 inline 효과음을 재생한다.
        private void PlayInlineSfxCues(int previousVisibleCount, int nextVisibleCount)
        {
            for (int i = 0; i < lineSfxCues.Count; i++)
            {
                InlineSfxCue cue = lineSfxCues[i];
                if (cue.Played ||
                    cue.VisibleIndex < previousVisibleCount ||
                    cue.VisibleIndex > nextVisibleCount ||
                    HasEarlierUnplayedWaitCue(cue.VisibleIndex, cue.Order))
                {
                    continue;
                }

                SoundPlayer.PlaySfx(cue.SfxId);
                cue.Played = true;
                lineSfxCues[i] = cue;
            }
        }

        // 현재 타이핑 구간에 도달한 <shake> 큐로 활성 대사창을 흔든다.
        private void PlayInlineShakeCues(int previousVisibleCount, int nextVisibleCount)
        {
            for (int i = 0; i < lineShakeCues.Count; i++)
            {
                InlineShakeCue cue = lineShakeCues[i];
                if (cue.Played ||
                    cue.VisibleIndex < previousVisibleCount ||
                    cue.VisibleIndex > nextVisibleCount ||
                    HasEarlierUnplayedWaitCue(cue.VisibleIndex, cue.Order))
                {
                    continue;
                }

                panel?.PlayBodyShake();
                cue.Played = true;
                lineShakeCues[i] = cue;
            }
        }

        // 같은 출력 위치에서는 TSV에 먼저 적힌 대기 태그가 다른 인라인 큐보다 먼저 처리되게 한다.
        private bool HasEarlierUnplayedWaitCue(int visibleIndex, int order)
        {
            for (int i = 0; i < lineWaitCues.Count; i++)
            {
                InlineWaitCue waitCue = lineWaitCues[i];
                if (!waitCue.Played &&
                    waitCue.VisibleIndex == visibleIndex &&
                    waitCue.Order < order)
                {
                    return true;
                }
            }

            return false;
        }

        // <shake> 또는 <shake/> 형식의 값 없는 인라인 제어 태그인지 확인한다.
        private static bool TryReadMarkerTag(
            string text,
            int startIndex,
            string expectedTagName,
            out int tagEndIndex)
        {
            tagEndIndex = -1;
            int closeIndex = text.IndexOf('>', startIndex + 1);
            if (closeIndex < 0)
            {
                return false;
            }

            string tag = text.Substring(startIndex + 1, closeIndex - startIndex - 1).Trim();
            if (tag.EndsWith("/", StringComparison.Ordinal))
            {
                tag = tag.Substring(0, tag.Length - 1).TrimEnd();
            }

            if (!string.Equals(tag, expectedTagName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            tagEndIndex = closeIndex;
            return true;
        }

        // <sfx=id> 또는 <sfx="id"> 형식인지 확인한다.
        private static bool TryReadSfxTag(
            string text,
            int startIndex,
            out string sfxId,
            out int tagEndIndex)
        {
            return TryReadValueTag(text, startIndex, "sfx", out sfxId, out tagEndIndex);
        }

        // <type_sfx=id> 또는 <type_sfx="id"> 형식인지 확인한다.
        private static bool TryReadTypingSfxTag(
            string text,
            int startIndex,
            out string sfxId,
            out int tagEndIndex)
        {
            return TryReadValueTag(text, startIndex, "type_sfx", out sfxId, out tagEndIndex);
        }

        // <type_ms=100> 형식의 줄별 글자 출력 간격을 밀리초 단위로 읽는다.
        private static bool TryReadTypingMillisecondsTag(
            string text,
            int startIndex,
            out int milliseconds,
            out int tagEndIndex)
        {
            milliseconds = 0;
            if (!TryReadValueTag(text, startIndex, "type_ms", out string value, out tagEndIndex))
            {
                return false;
            }

            return int.TryParse(value, out milliseconds) && milliseconds > 0;
        }

        // 값 하나를 받는 인라인 제어 태그를 공통으로 해석한다.
        private static bool TryReadValueTag(
            string text,
            int startIndex,
            string expectedTagName,
            out string value,
            out int tagEndIndex)
        {
            value = string.Empty;
            tagEndIndex = -1;

            int closeIndex = text.IndexOf('>', startIndex + 1);
            if (closeIndex < 0)
            {
                return false;
            }

            string tag = text.Substring(startIndex + 1, closeIndex - startIndex - 1).Trim();
            int equalsIndex = tag.IndexOf('=');
            if (equalsIndex <= 0 ||
                !string.Equals(tag.Substring(0, equalsIndex).Trim(), expectedTagName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            value = tag.Substring(equalsIndex + 1).Trim().Trim('"', '\'');
            tagEndIndex = closeIndex;
            return !string.IsNullOrWhiteSpace(value);
        }

        // <ms=100> 형식인지 확인하고 초 단위 대기 시간으로 변환한다.
        private static bool TryReadWaitTag(
            string text,
            int startIndex,
            out float seconds,
            out int tagEndIndex)
        {
            seconds = 0f;
            tagEndIndex = -1;

            int closeIndex = text.IndexOf('>', startIndex + 1);
            if (closeIndex < 0)
            {
                return false;
            }

            string tag = text.Substring(startIndex + 1, closeIndex - startIndex - 1).Trim();
            int equalsIndex = tag.IndexOf('=');
            if (equalsIndex <= 0 ||
                !string.Equals(tag.Substring(0, equalsIndex).Trim(), "ms", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string value = tag.Substring(equalsIndex + 1).Trim().Trim('"', '\'');
            if (!int.TryParse(value, out int milliseconds) || milliseconds <= 0)
            {
                return false;
            }

            seconds = milliseconds / 1000f;
            tagEndIndex = closeIndex;
            return true;
        }

        private void StartLineEffect(string effect, string shaderProfile)
        {
            StopEffect(true);

            string effectName = (effect ?? string.Empty).Trim();
            bool waitPostEffectTransition = TryApplyShaderProfile(shaderProfile);
            if (string.IsNullOrEmpty(effectName))
            {
                if (waitPostEffectTransition)
                {
                    effectCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
                    WaitPostEffectTransition(effectCts.Token).Forget();
                }

                return;
            }

            var portraitEffectNames = new List<string>();
            bool playFullscreenNoise = false;
            float roomAnimationWaitSeconds = 0f;
            float effectWaitSeconds = 0f;
            var roomAnimationWaitTargets = new List<AsepriteRoomAnimator>();
            string[] effectNames = SplitEffectTokens(effectName);
            var blackFadeEffectModes = new List<int>();
            var redFadeEffectModes = new List<int>();
            var greenFadeEffectModes = new List<int>();
            ClearPortraitBeforeBlockingEffects(effectNames);
            for (int i = 0; i < effectNames.Length; i++)
            {
                string token = effectNames[i].Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (DialogueStoryEffectParser.Parse(token) != DialogueStoryEffect.None)
                {
                    continue;
                }

                if (TryUnlockAchievementEffect(token))
                {
                    continue;
                }

                if (TryApplyBgmEffect(token))
                {
                    continue;
                }

                if (TryApplyRoomMoveEffect(token))
                {
                    continue;
                }

                if (TryApplySceneObjectVisibilityEffect(token))
                {
                    continue;
                }

                if (TryApplyItemInventoryEffect(token))
                {
                    continue;
                }

                if (TryApplyInfoInventoryEffect(token))
                {
                    continue;
                }

                if (token == AutoAdvanceEffect)
                {
                    continue;
                }

                if (TryParseEffectWaitToken(token, out float parsedWaitSeconds))
                {
                    effectWaitSeconds = Mathf.Max(effectWaitSeconds, parsedWaitSeconds);
                    continue;
                }

                if (token == "띠리링")
                {
                    SoundPlayer.PlaySfx(token);
                    continue;
                }

                if (token == "bang" || token == "boom" || token == "쾅")
                {
                    SoundPlayer.PlaySfx(token);
                    continue;
                }

                if (TryPlayRoomAnimationEffect(token, out float animationWaitSeconds, roomAnimationWaitTargets))
                {
                    roomAnimationWaitSeconds = Mathf.Max(roomAnimationWaitSeconds, animationWaitSeconds);
                    continue;
                }

                if (token == "블러ON")
                {
                    roomPostEffectController?.SetBlurOverride(true);
                    continue;
                }

                if (token == "블러OFF")
                {
                    roomPostEffectController?.SetBlurOverride(false);
                    continue;
                }

                if (token == VectorBlurEffect || token == VectorBlurAlias)
                {
                    roomPostEffectController?.SetVectorBlurOverride(new Vector2(-1f, 1f));
                    continue;
                }

                if (token == UpperRightVectorBlurEffect || token == UpperRightVectorBlurAlias)
                {
                    roomPostEffectController?.SetVectorBlurOverride(new Vector2(1f, 1f));
                    continue;
                }

                if (token == VectorBlurOffEffect || token == VectorBlurOffAlias)
                {
                    roomPostEffectController?.SetBlurOverride(false);
                    continue;
                }

                if (token == FullscreenNoiseEffect || token == FullscreenNoiseAlias)
                {
                    playFullscreenNoise = true;
                    continue;
                }

                if (token == FullscreenNoiseOnEffect || token == FullscreenNoiseOnAlias)
                {
                    SceneTransitionFadeUI.ShowFullscreenNoise();
                    continue;
                }

                if (token == FullscreenNoiseOffEffect || token == FullscreenNoiseOffAlias)
                {
                    SceneTransitionFadeUI.HideFullscreenNoise();
                    continue;
                }

                if (token == "WARNINGON" || token == "HP50ON")
                {
                    roomPostEffectController?.SetWarnOverride(true);
                    waitPostEffectTransition |= roomPostEffectController != null;
                    continue;
                }

                if (token == "WARNINGOFF" || token == "HP50OFF")
                {
                    roomPostEffectController?.SetWarnOverride(false);
                    waitPostEffectTransition |= roomPostEffectController != null;
                    continue;
                }

                if (token == "DANGERON" || token == "HP20ON")
                {
                    roomPostEffectController?.SetDangerOverride(true);
                    waitPostEffectTransition |= roomPostEffectController != null;
                    continue;
                }

                if (token == "DANGEROFF" || token == "HP20OFF")
                {
                    roomPostEffectController?.SetDangerOverride(false);
                    waitPostEffectTransition |= roomPostEffectController != null;
                    continue;
                }

                if (token == InstantBlackOnEffect)
                {
                    // 페이드 없이 즉시 검정으로 덮는다. (검정ON의 즉시 버전)
                    // 독립 검정 오버레이를 사용하므로 대사 패널은 숨기지 않는다.
                    screenEffectPlayer?.ApplyImmediate(
                        new DialogueScreenEffectCue(
                            DialogueScreenEffectKind.Blackout,
                            DialogueScreenEffectState.Show,
                            DialogueScreenEffectSpeed.Instant));
                    roomPostEffectController?.SetBlurOverride(false);
                    continue;
                }

                if (token == InstantBlackOffEffect)
                {
                    // 페이드 없이 즉시 검정을 걷어낸다. (검정OFF의 즉시 버전)
                    screenEffectPlayer?.ApplyImmediate(
                        new DialogueScreenEffectCue(
                            DialogueScreenEffectKind.Blackout,
                            DialogueScreenEffectState.Hide,
                            DialogueScreenEffectSpeed.Instant));
                    continue;
                }

                if (token == BlackFadeOnEffect || token == BlackFadeOffEffect)
                {
                    blackFadeEffectModes.Add(token == BlackFadeOnEffect ? 1 : 2);
                    continue;
                }

                if (token == SlowBlackFadeOnEffect || token == SlowBlackFadeOffEffect)
                {
                    // 검정ON/OFF의 느린 버전. 실제 지속시간은 화면 효과 시스템이 결정한다.
                    blackFadeEffectModes.Add(token == SlowBlackFadeOnEffect ? 3 : 4);
                    continue;
                }

                if (token == WhiteFadeOnEffect || token == WhiteFadeOffEffect)
                {
                    blackFadeEffectModes.Add(token == WhiteFadeOnEffect ? 5 : 6);
                    continue;
                }

                if (token == DialogueEffectTokens.RedFadeOn || token == DialogueEffectTokens.RedFadeOff)
                {
                    redFadeEffectModes.Add(token == DialogueEffectTokens.RedFadeOn ? 1 : 2);
                    continue;
                }

                if (token == DialogueEffectTokens.GreenFadeOn || token == DialogueEffectTokens.GreenFadeOff)
                {
                    greenFadeEffectModes.Add(token == DialogueEffectTokens.GreenFadeOn ? 1 : 2);
                    continue;
                }

                if (token == "즉시퇴장")
                {
                    continue;
                }

                if (IsPortraitEffectToken(token))
                {
                    portraitEffectNames.Add(token);
                }
            }

            Transform portrait = panel != null ? panel.CurrentPortraitTransform : null;
            if (portrait == null)
            {
                portraitEffectNames.Clear();
            }

            bool waitForRoomAnimation = roomAnimationWaitSeconds > 0f || roomAnimationWaitTargets.Count > 0;
            int effectCount = portraitEffectNames.Count;
            effectCount += redFadeEffectModes.Count;
            effectCount += greenFadeEffectModes.Count;
            effectCount += blackFadeEffectModes.Count;
            effectCount += playFullscreenNoise ? 1 : 0;
            effectCount += effectWaitSeconds > 0f ? 1 : 0;
            effectCount += waitForRoomAnimation ? 1 : 0;
            effectCount += waitPostEffectTransition ? 1 : 0;
            if (effectCount == 0)
            {
                return;
            }

            effectCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            CancellationToken effectToken = effectCts.Token;
            activeLineEffectCount = effectCount;
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);

            if (portrait != null)
            {
                effectTarget = portrait;
                effectOriginalLocalPosition = portrait.localPosition;
                effectOriginalLocalScale = portrait.localScale;
                CapturePortraitAlpha(portrait);

                for (int i = 0; i < portraitEffectNames.Count; i++)
                {
                    StartPortraitEffect(portraitEffectNames[i], portrait, effectToken);
                }
            }

            for (int i = 0; i < redFadeEffectModes.Count; i++)
            {
                PlayRedFadeEffect(redFadeEffectModes[i] == 1, effectToken).Forget();
            }

            for (int i = 0; i < greenFadeEffectModes.Count; i++)
            {
                PlayGreenFadeEffect(greenFadeEffectModes[i] == 1, effectToken).Forget();
            }

            for (int i = 0; i < blackFadeEffectModes.Count; i++)
            {
                StartBlackFadeEffect(blackFadeEffectModes[i], effectToken);
            }

            if (playFullscreenNoise)
            {
                PlayFullscreenNoiseEffect(effectToken).Forget();
            }

            if (effectWaitSeconds > 0f)
            {
                WaitLineEffect(effectWaitSeconds, effectToken).Forget();
            }

            if (waitForRoomAnimation)
            {
                WaitRoomAnimationEffect(
                    roomAnimationWaitSeconds,
                    CopyRoomAnimationWaitTargets(roomAnimationWaitTargets),
                    effectToken).Forget();
            }

            if (waitPostEffectTransition)
            {
                WaitPostEffectTransition(effectToken).Forget();
            }
        }

        // 같은 타이밍에 지정된 초상화 효과를 모두 동일한 완료 묶음 안에서 시작한다.
        private void StartPortraitEffect(string effectName, Transform portrait, CancellationToken ct)
        {
            switch (effectName)
            {
                case "통통":
                    BouncePortraitTwice(portrait, ct).Forget();
                    break;
                case "페이드등장":
                    FadePortrait(portrait, 0f, 1f, portraitFadeDuration, false, ct).Forget();
                    break;
                case "페이드퇴장":
                    FadePortrait(portrait, 1f, 0f, portraitFadeDuration, true, ct).Forget();
                    break;
                case "좌등장":
                    SlidePortrait(portrait, -1f, true, false, ct).Forget();
                    break;
                case "좌퇴장":
                    SlidePortrait(portrait, -1f, false, false, ct).Forget();
                    break;
                case "우등장":
                    SlidePortrait(portrait, 1f, true, false, ct).Forget();
                    break;
                case "우퇴장":
                    SlidePortrait(portrait, 1f, false, false, ct).Forget();
                    break;
                case "클로즈업":
                    PlayPortraitCloseup(false);
                    break;
                case "클로즈복귀":
                    PlayPortraitCloseup(true);
                    break;
            }
        }

        // 초상화 전용 토큰만 병렬 실행 목록에 넣어 알 수 없는 토큰이 대기를 막지 않게 한다.
        private static bool IsPortraitEffectToken(string token)
        {
            return token == "통통" || token == "페이드등장" || token == "페이드퇴장" ||
                token == "좌등장" || token == "좌퇴장" || token == "우등장" || token == "우퇴장" ||
                token == "클로즈업" || token == "클로즈복귀";
        }

        // 검정·흰색 페이드 모드를 현재 타이밍의 병렬 효과 묶음에서 시작한다.
        private void StartBlackFadeEffect(int mode, CancellationToken ct)
        {
            switch (mode)
            {
                case 1:
                    PlayBlackFadeEffect(true, ct).Forget();
                    break;
                case 2:
                    PlayBlackFadeEffect(false, ct).Forget();
                    break;
                case 3:
                    PlaySlowBlackFadeEffect(true, ct).Forget();
                    break;
                case 4:
                    PlaySlowBlackFadeEffect(false, ct).Forget();
                    break;
                case 5:
                    PlayWhiteFadeEffect(true, ct).Forget();
                    break;
                case 6:
                    PlayWhiteFadeEffect(false, ct).Forget();
                    break;
            }
        }

        // TSV shader 컬럼의 프로필 이름을 RoomPostEffectController에 전달한다.
        private bool TryApplyShaderProfile(string shaderProfile)
        {
            if (string.IsNullOrWhiteSpace(shaderProfile) || roomPostEffectController == null)
            {
                return false;
            }

            return roomPostEffectController.SetStoryProfile(shaderProfile);
        }

        // `대기=5000` 형식의 effect를 실시간 밀리초 대기로 해석한다.
        private static bool TryParseEffectWaitToken(string token, out float seconds)
        {
            seconds = 0f;
            string value = (token ?? string.Empty).Trim();
            if (!value.StartsWith(EffectWaitPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string millisecondsText = value.Substring(EffectWaitPrefix.Length).Trim();
            if (!int.TryParse(millisecondsText, out int milliseconds) || milliseconds <= 0)
            {
                Debug.LogWarning($"Dialogue effect wait requires positive milliseconds: {value}");
                return true;
            }

            seconds = milliseconds / 1000f;
            return true;
        }

        // `업적달성=ID` effect를 기존 업적 저장 및 알림 경로로 전달한다.
        private static bool TryUnlockAchievementEffect(string token)
        {
            const string prefix = "업적달성=";
            if (string.IsNullOrWhiteSpace(token) ||
                !token.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string achievementId = token.Substring(prefix.Length).Trim();
            if (string.IsNullOrEmpty(achievementId))
            {
                Debug.LogWarning("Achievement dialogue effect requires an ID after '업적달성='.");
                return true;
            }

            AchievementProgress.Unlock(achievementId);
            return true;
        }

        // TSV effect의 아이템 획득·제거 토큰을 실제 소지품 상태에 반영한다.
        private static bool TryApplyItemInventoryEffect(string token)
        {
            if (!TryParseItemInventoryEffectToken(token, out bool acquire, out string itemId))
            {
                return false;
            }

            if (string.IsNullOrEmpty(itemId))
            {
                Debug.LogWarning($"Dialogue item effect requires an item ID: '{token}'.");
                return true;
            }

            PlayerInventory inventory = PlayerInventory.Instance;
            if (inventory == null)
            {
                Debug.LogError($"Dialogue item effect requires an PlayerInventory: '{token}'.");
                return true;
            }

            if (acquire)
            {
                inventory.AddItem(itemId);
            }
            else
            {
                inventory.ConsumeItem(itemId);
            }

            return true;
        }

        // 아이템 effect를 안내 대사 생성과 실제 상태 변경에서 함께 쓸 수 있도록 해석한다.
        internal static bool TryParseItemInventoryEffectToken(string token, out bool acquire, out string itemId)
        {
            acquire = false;
            itemId = string.Empty;
            string value = (token ?? string.Empty).Trim();
            if (TryStripEffectTimingSuffix(value, DialogueEffectTokens.BeforeSuffix, out string strippedToken) ||
                TryStripEffectTimingSuffix(value, DialogueEffectTokens.DuringSuffix, out strippedToken) ||
                TryStripEffectTimingSuffix(value, DialogueEffectTokens.AfterSuffix, out strippedToken))
            {
                value = strippedToken;
            }

            if (value.StartsWith(ItemAcquireEffectPrefix, StringComparison.Ordinal))
            {
                acquire = true;
                itemId = value.Substring(ItemAcquireEffectPrefix.Length).Trim();
                return true;
            }

            if (value.StartsWith(ItemRemoveEffectPrefix, StringComparison.Ordinal))
            {
                itemId = value.Substring(ItemRemoveEffectPrefix.Length).Trim();
                return true;
            }

            return false;
        }

        // TSV effect의 정보 획득 토큰을 실제 정보 보유 상태에 반영한다.
        private static bool TryApplyInfoInventoryEffect(string token)
        {
            string value = (token ?? string.Empty).Trim();
            if (!value.StartsWith(InfoAcquireEffectPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string infoId = value.Substring(InfoAcquireEffectPrefix.Length).Trim();
            if (string.IsNullOrEmpty(infoId))
            {
                Debug.LogWarning($"Dialogue info effect requires an info ID: '{value}'.");
                return true;
            }

            InfoCollection infoCollection = InfoCollection.Instance;
            if (infoCollection == null)
            {
                Debug.LogError($"Dialogue info effect requires an InfoCollection: '{value}'.");
                return true;
            }

            infoCollection.AddInfo(infoId);
            return true;
        }

        // 현재 방에 생성된 Aseprite 태그 애니메이션을 dialogue effect 토큰으로 재생한다.
        private bool TryPlayRoomAnimationEffect(
            string token,
            out float waitSeconds,
            ICollection<AsepriteRoomAnimator> waitTargets)
        {
            waitSeconds = 0f;
            if (!TryParseRoomAnimationEffectToken(
                    token,
                    out string animationName,
                    out bool hasPlaybackOverride,
                    out AsepriteSpritePlaybackMode playbackOverride))
            {
                return false;
            }

            Transform room = roomEffects?.CurrentRoom;
            if (room == null)
            {
                return false;
            }

            var animators = room.GetComponentsInChildren<AsepriteRoomAnimator>(true);
            var played = false;
            for (int i = 0; i < animators.Length; i++)
            {
                AsepriteRoomAnimator animator = animators[i];
                if (animator == null || !animator.isActiveAndEnabled)
                {
                    continue;
                }

                if (animator.TryPlay(animationName, hasPlaybackOverride, playbackOverride, out float durationSeconds))
                {
                    waitSeconds = Mathf.Max(waitSeconds, durationSeconds);
                    if (durationSeconds > 0f && animator.IsPlaying)
                    {
                        waitTargets?.Add(animator);
                    }

                    played = true;
                }
            }

            return played;
        }

        private static bool TryParseRoomAnimationEffectToken(
            string token,
            out string animationName,
            out bool hasPlaybackOverride,
            out AsepriteSpritePlaybackMode playbackOverride)
        {
            animationName = (token ?? string.Empty).Trim();
            hasPlaybackOverride = false;
            playbackOverride = AsepriteSpritePlaybackMode.Once;
            if (string.IsNullOrEmpty(animationName))
            {
                return false;
            }

            const string onceAliasPrefix = "애니한번=";
            const string loopAliasPrefix = "애니루프=";
            const string prefix = "anim=";
            if (animationName.StartsWith(onceAliasPrefix, StringComparison.Ordinal))
            {
                playbackOverride = AsepriteSpritePlaybackMode.Once;
                hasPlaybackOverride = true;
                animationName = animationName.Substring(onceAliasPrefix.Length).Trim();
            }
            else if (animationName.StartsWith(loopAliasPrefix, StringComparison.Ordinal))
            {
                playbackOverride = AsepriteSpritePlaybackMode.Loop;
                hasPlaybackOverride = true;
                animationName = animationName.Substring(loopAliasPrefix.Length).Trim();
            }
            else if (animationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                animationName = animationName.Substring(prefix.Length).Trim();
            }

            int colonIndex = animationName.IndexOf(':');
            if (colonIndex >= 0)
            {
                string mode = animationName.Substring(0, colonIndex).Trim();
                if (string.Equals(mode, "loop", StringComparison.OrdinalIgnoreCase))
                {
                    playbackOverride = AsepriteSpritePlaybackMode.Loop;
                    hasPlaybackOverride = true;
                    animationName = animationName.Substring(colonIndex + 1).Trim();
                }
                else if (string.Equals(mode, "once", StringComparison.OrdinalIgnoreCase))
                {
                    playbackOverride = AsepriteSpritePlaybackMode.Once;
                    hasPlaybackOverride = true;
                    animationName = animationName.Substring(colonIndex + 1).Trim();
                }
                else
                {
                    string suffixMode = animationName.Substring(colonIndex + 1).Trim();
                    if (string.Equals(suffixMode, "loop", StringComparison.OrdinalIgnoreCase))
                    {
                        playbackOverride = AsepriteSpritePlaybackMode.Loop;
                        hasPlaybackOverride = true;
                        animationName = animationName.Substring(0, colonIndex).Trim();
                    }
                    else if (string.Equals(suffixMode, "once", StringComparison.OrdinalIgnoreCase))
                    {
                        playbackOverride = AsepriteSpritePlaybackMode.Once;
                        hasPlaybackOverride = true;
                        animationName = animationName.Substring(0, colonIndex).Trim();
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return !string.IsNullOrEmpty(animationName);
        }

        // 대사 effect 대기도 게임의 timeScale을 따라 진행한다.
        private async UniTaskVoid WaitLineEffect(float waitSeconds, CancellationToken ct)
        {
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);

            try
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(waitSeconds),
                    ignoreTimeScale: false,
                    cancellationToken: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            FinishEffect(false);
        }

        private async UniTaskVoid WaitRoomAnimationEffect(
            float waitSeconds,
            IReadOnlyList<AsepriteRoomAnimator> waitTargets,
            CancellationToken ct)
        {
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);

            try
            {
                if (waitTargets != null && waitTargets.Count > 0)
                {
                    while (IsAnyRoomAnimationPlaying(waitTargets))
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                }
                else
                {
                    var elapsed = 0f;
                    while (elapsed < waitSeconds)
                    {
                        ct.ThrowIfCancellationRequested();
                        elapsed += Time.deltaTime;
                        await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            FinishEffect(false);
        }

        private static IReadOnlyList<AsepriteRoomAnimator> CopyRoomAnimationWaitTargets(
            IReadOnlyList<AsepriteRoomAnimator> waitTargets)
        {
            if (waitTargets == null || waitTargets.Count == 0)
            {
                return Array.Empty<AsepriteRoomAnimator>();
            }

            return new List<AsepriteRoomAnimator>(waitTargets);
        }

        private static bool IsAnyRoomAnimationPlaying(IReadOnlyList<AsepriteRoomAnimator> waitTargets)
        {
            for (int i = 0; i < waitTargets.Count; i++)
            {
                AsepriteRoomAnimator animator = waitTargets[i];
                if (animator != null && animator.isActiveAndEnabled && animator.IsPlaying)
                {
                    return true;
                }
            }

            return false;
        }

        // WARNINGON/OFF, DANGERON/OFF처럼 내부 보간이 있는 효과가 끝날 때까지 대사 진행을 막는다.
        private async UniTaskVoid WaitPostEffectTransition(CancellationToken ct)
        {
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);

            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                while (roomPostEffectController != null && roomPostEffectController.IsTransitioning)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            FinishEffect(false);
        }

        // TOPUI 노이즈 UniTask를 대사 effect 대기 상태와 연결한다.
        private async UniTaskVoid PlayFullscreenNoiseEffect(CancellationToken ct)
        {
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);

            try
            {
                await SceneTransitionFadeUI.PlayFullscreenNoiseAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            FinishEffect(false);
        }

        // 대사 effect의 상태 변경과 스토리 흐름 제어를 처리한다.
        private void StartStoryEffects(string effect, CancellationToken ct)
        {
            if (!ContainsStoryEffect(effect))
            {
                return;
            }

            PlayStoryEffectsAsync(effect, ct).Forget();
        }

        private async UniTaskVoid PlayStoryEffectsAsync(string effect, CancellationToken ct)
        {
            isStoryEffectPlaying = true;
            try
            {
                bool requiresTimingCheck = false;
                string[] tokens = SplitEffectTokens(effect);
                for (int i = 0; i < tokens.Length; i++)
                {
                    switch (DialogueStoryEffectParser.Parse(tokens[i]))
                    {
                        case DialogueStoryEffect.HealthMinusOne:
                            if (storyState != null && storyState.ApplyDamage(1))
                            {
                                storyResult?.RequestGameOver();
                            }
                            break;
                        case DialogueStoryEffect.HealthPlusOne:
                            storyState?.Heal(1);
                            break;
                        case DialogueStoryEffect.TimingCheck:
                            requiresTimingCheck = true;
                            break;
                        case DialogueStoryEffect.ReturnTitle:
                            GameSession.Instance?.SetInputLocked(false);
                            GameSession.Instance?.ResetState();
                            SaveDataPopupUI.ClearPendingLoad();
                            EscapeSceneLoader.LoadTitle();
                            return;
                        case DialogueStoryEffect.EndingCredits:
                            await PlayEndingCreditsAndReturnTitle(ct);
                            return;
                        case DialogueStoryEffect.EndStory:
                            storyEndRequested = true;
                            return;
                        case DialogueStoryEffect.FlashlightOn:
                            if (roomEffects == null)
                            {
                                Debug.LogError("손전등ON 효과에는 IDialogueRoomEffects 구현이 필요합니다.", this);
                                break;
                            }

                            panel?.SetVisible(false);
                            try
                            {
                                await roomEffects.PlayFlashlightTurnOnEffectAsync(ct);
                            }
                            finally
                            {
                                panel?.SetVisible(true);
                            }
                            break;
                    }
                }

                if (!requiresTimingCheck)
                {
                    return;
                }

                if (timingCheckPopup == null)
                {
                    Debug.LogError("Timing check effect requires a TimingCheckPopupUI reference.", this);
                    AddStoryFlag(TimingCheckFailureFlag);
                    return;
                }

                bool succeeded;
                panel?.SetWaitingForInput(false);
                panel?.SetVisible(false);
                try
                {
                    succeeded = await timingCheckPopup.ShowAsync(ct);
                }
                finally
                {
                    panel?.SetVisible(true);
                }

                AddStoryFlag(succeeded ? TimingCheckSuccessFlag : TimingCheckFailureFlag);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 스토리 종료 시 진행 중인 타이밍 체크도 함께 취소한다.
            }
            finally
            {
                isStoryEffectPlaying = false;
            }
        }

        // 엔딩 전용 크레딧을 대사 UI와 분리해 재생하고, 종료 후 타이틀로 돌아간다.
        private async UniTask PlayEndingCreditsAndReturnTitle(CancellationToken ct)
        {
            panel?.Hide();
            if (endingCreditRollUI != null)
            {
                Sprite endingBackground = backgroundRenderer != null && backgroundRenderer.enabled
                    ? backgroundRenderer.sprite
                    : null;
                await endingCreditRollUI.PlayAsync(ct, endingBackground);
            }
            else
            {
                Debug.LogError("Ending credits require an EndingCreditRollUI scene reference.", this);
            }

            GameSession.Instance?.SetInputLocked(false);
            GameSession.Instance?.ResetState();
            SaveDataPopupUI.ClearPendingLoad();
            EscapeSceneLoader.LoadTitle();
        }

        private static bool ContainsStoryEffect(string effect)
        {
            string[] tokens = SplitEffectTokens(effect);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (DialogueStoryEffectParser.Parse(tokens[i]) != DialogueStoryEffect.None)
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] SplitEffectTokens(string effect)
        {
            return (effect ?? string.Empty)
                .Split(new[] { '+', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // 예약된 타이밍 접미사를 제거하고 지정 단계에 해당하는 효과만 다시 조합한다.
        private static string SelectEffectsByTiming(string effect, DialogueEffectTiming requestedTiming)
        {
            string[] tokens = SplitEffectTokens(effect);
            var selectedTokens = new List<string>(tokens.Length);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                DialogueEffectTiming timing = DialogueEffectTiming.During;
                if (TryStripEffectTimingSuffix(token, DialogueEffectTokens.BeforeSuffix, out string strippedToken))
                {
                    timing = DialogueEffectTiming.Before;
                    token = strippedToken;
                }
                else if (TryStripEffectTimingSuffix(token, DialogueEffectTokens.DuringSuffix, out strippedToken))
                {
                    timing = DialogueEffectTiming.During;
                    token = strippedToken;
                }
                else if (TryStripEffectTimingSuffix(token, DialogueEffectTokens.AfterSuffix, out strippedToken))
                {
                    timing = DialogueEffectTiming.After;
                    token = strippedToken;
                }

                if (timing == requestedTiming && !string.IsNullOrEmpty(token))
                {
                    selectedTokens.Add(token);
                }
            }

            return string.Join("+", selectedTokens);
        }

        private static bool TryStripEffectTimingSuffix(string token, string suffix, out string strippedToken)
        {
            strippedToken = token;
            if (!token.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            strippedToken = token.Substring(0, token.Length - suffix.Length).Trim();
            return true;
        }

        // 타이밍 접미사 유무와 관계없이 effect에 지정 제어 토큰이 포함됐는지 확인한다.
        private static bool ContainsEffectToken(string effect, string expectedToken)
        {
            string[] tokens = SplitEffectTokens(effect);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (TryStripEffectTimingSuffix(token, DialogueEffectTokens.BeforeSuffix, out string strippedToken) ||
                    TryStripEffectTimingSuffix(token, DialogueEffectTokens.DuringSuffix, out strippedToken) ||
                    TryStripEffectTimingSuffix(token, DialogueEffectTokens.AfterSuffix, out strippedToken))
                {
                    token = strippedToken;
                }

                if (string.Equals(token, expectedToken, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // 대사 배경이 방을 가린 동안 실제 플레이 방을 소리와 입장 연출 없이 전환한다.
        private bool TryApplyRoomMoveEffect(string token)
        {
            string value = (token ?? string.Empty).Trim();
            string destinationName;
            if (value.StartsWith(RoomMoveEffectPrefix, StringComparison.Ordinal))
            {
                destinationName = value.Substring(RoomMoveEffectPrefix.Length).Trim();
            }
            else if (value.StartsWith(RoomMoveEffectAliasPrefix, StringComparison.OrdinalIgnoreCase))
            {
                destinationName = value.Substring(RoomMoveEffectAliasPrefix.Length).Trim();
            }
            else
            {
                return false;
            }

            if (!TryParseDialogueRoomType(destinationName, out RoomType destination))
            {
                Debug.LogWarning($"Unknown dialogue room move destination: '{destinationName}'.", this);
                return true;
            }

            if (roomEffects == null)
            {
                Debug.LogError("Dialogue room move effect requires IDialogueRoomEffects.", this);
                return true;
            }

            roomEffects.MoveToRoomFromDialogue(destination);
            return true;
        }

        // TSV 작성용 한국어 방 이름과 RoomType enum 이름을 모두 허용한다.
        private static bool TryParseDialogueRoomType(string value, out RoomType roomType)
        {
            string normalized = (value ?? string.Empty).Trim();
            roomType = normalized switch
            {
                "거실" => RoomType.LivingRoom,
                "침실" => RoomType.BedRoom,
                "부엌" => RoomType.KitchenRoom,
                "현관" => RoomType.EntranceRoom,
                "다용도실" => RoomType.UtilityRoom,
                _ => RoomType.None,
            };

            if (roomType != RoomType.None)
            {
                return true;
            }

            return Enum.TryParse(normalized, true, out roomType) && roomType != RoomType.None;
        }

        // TSV effect에서 정확한 씬 오브젝트 이름을 받아 활성 상태와 저장 상태를 함께 변경한다.
        private bool TryApplySceneObjectVisibilityEffect(string token)
        {
            string value = (token ?? string.Empty).Trim();
            bool active;
            string objectName;
            if (value.StartsWith(SceneObjectHideEffectPrefix, StringComparison.Ordinal))
            {
                active = false;
                objectName = value.Substring(SceneObjectHideEffectPrefix.Length).Trim();
            }
            else if (value.StartsWith(SceneObjectShowEffectPrefix, StringComparison.Ordinal))
            {
                active = true;
                objectName = value.Substring(SceneObjectShowEffectPrefix.Length).Trim();
            }
            else
            {
                return false;
            }

            if (string.IsNullOrEmpty(objectName))
            {
                Debug.LogWarning($"Dialogue object visibility effect has no object name: '{value}'.", this);
                return true;
            }

            if (roomEffects == null)
            {
                Debug.LogError("Dialogue object visibility effect requires IDialogueRoomEffects.", this);
                return true;
            }

            if (!roomEffects.SetSceneObjectActive(objectName, active))
            {
                Debug.LogWarning($"Dialogue object visibility target was not found: '{objectName}'.", this);
            }

            return true;
        }

        // TSV effect의 BGM 토큰을 즉시 적용한다.
        private static bool TryApplyBgmEffect(string token)
        {
            string value = (token ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (string.Equals(value, BgmSegmentLoopOffEffect, StringComparison.OrdinalIgnoreCase))
            {
                SoundPlayer.StopBgmSegmentLoop();
                return true;
            }

            if (string.Equals(value, BgmSegmentLoopEffect, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, BgmSegmentLoopEffect + "ON", StringComparison.OrdinalIgnoreCase))
            {
                SoundPlayer.StartBgmSegmentLoop(DefaultBgmSegmentLoopMilliseconds);
                return true;
            }

            if (value.StartsWith(BgmSegmentLoopPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string millisecondsText = value.Substring(BgmSegmentLoopPrefix.Length).Trim();
                if (!int.TryParse(millisecondsText, out int milliseconds) || milliseconds <= 0)
                {
                    Debug.LogWarning($"BGM 구간 반복 길이는 양의 정수(ms)여야 합니다: {value}");
                    return true;
                }

                SoundPlayer.StartBgmSegmentLoop(milliseconds);
                return true;
            }

            if (IsBgmStopToken(value))
            {
                SoundPlayer.SilenceStoryBgm();
                return true;
            }

            if (!value.StartsWith(BgmEffectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string songId = value.Substring(BgmEffectPrefix.Length).Trim();
            if (IsBgmStopToken(songId))
            {
                SoundPlayer.SilenceStoryBgm();
                return true;
            }

            if (!string.IsNullOrWhiteSpace(songId))
            {
                SoundPlayer.PlayStoryBgm(songId);
            }

            return true;
        }

        // TSV bgm 열의 값으로 스토리 레이어 BGM을 즉시 교체한다.
        private static void ApplyLineBgm(string bgm)
        {
            string value = (bgm ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (string.Equals(value, BgmOffEffect, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "BGM정지", StringComparison.Ordinal))
            {
                SoundPlayer.SilenceStoryBgm();
                return;
            }

            if (value.StartsWith(BgmEffectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(BgmEffectPrefix.Length).Trim();
            }

            if (IsBgmStopToken(value))
            {
                SoundPlayer.SilenceStoryBgm();
                return;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                SoundPlayer.PlayStoryBgm(value);
            }
        }

        private static bool IsBgmStopToken(string value)
        {
            return string.Equals(value, BgmOffEffect, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, BgmNoneEffect, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "BGM정지", StringComparison.Ordinal);
        }

        private void AddStoryFlag(string flag)
        {
            if (string.IsNullOrWhiteSpace(flag))
            {
                return;
            }

            flag = flag.Trim();
            activeStoryFlags.Add(flag);
            storyResult?.AddFlag(flag);
        }

        // 검정 화면 전환을 재생한다.
        private UniTaskVoid PlayBlackFadeEffect(bool fadeToBlack, CancellationToken ct)
        {
            return PlayScreenEffect(
                new DialogueScreenEffectCue(
                    DialogueScreenEffectKind.Blackout,
                    ResolveScreenEffectState(fadeToBlack)),
                fadeToBlack,
                ct);
        }

        // 타격 장면용 반투명 붉은 오버레이를 전환한다.
        private UniTaskVoid PlayRedFadeEffect(bool fadeOn, CancellationToken ct)
        {
            return PlayScreenEffect(
                new DialogueScreenEffectCue(
                    DialogueScreenEffectKind.DangerOverlay,
                    ResolveScreenEffectState(fadeOn)),
                false,
                ct);
        }

        // 희망 회복용 반투명 초록 오버레이를 전환한다.
        private UniTaskVoid PlayGreenFadeEffect(bool fadeOn, CancellationToken ct)
        {
            return PlayScreenEffect(
                new DialogueScreenEffectCue(
                    DialogueScreenEffectKind.RecoveryOverlay,
                    ResolveScreenEffectState(fadeOn)),
                false,
                ct);
        }

        // 장면 전환용 흰색 오버레이를 전환한다.
        private UniTaskVoid PlayWhiteFadeEffect(bool fadeOn, CancellationToken ct)
        {
            return PlayScreenEffect(
                new DialogueScreenEffectCue(
                    DialogueScreenEffectKind.Whiteout,
                    ResolveScreenEffectState(fadeOn)),
                fadeOn,
                ct);
        }

        // 검정 화면 전환을 화면 효과 시스템의 느린 속도로 재생한다.
        private UniTaskVoid PlaySlowBlackFadeEffect(bool fadeToBlack, CancellationToken ct)
        {
            return PlayScreenEffect(
                new DialogueScreenEffectCue(
                    DialogueScreenEffectKind.Blackout,
                    ResolveScreenEffectState(fadeToBlack),
                    DialogueScreenEffectSpeed.Slow),
                fadeToBlack,
                ct);
        }

        // 색상과 지속시간 세부사항을 화면 효과 시스템에 위임하고 대사 대기 상태만 관리한다.
        private async UniTaskVoid PlayScreenEffect(
            DialogueScreenEffectCue cue,
            bool disableBlurOnShow,
            CancellationToken ct)
        {
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);

            try
            {
                if (screenEffectPlayer != null)
                {
                    await screenEffectPlayer.PlayAsync(cue, ct);
                    if (disableBlurOnShow && cue.State == DialogueScreenEffectState.Show)
                    {
                        roomPostEffectController?.SetBlurOverride(false);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            if (!ct.IsCancellationRequested)
            {
                FinishEffect(false);
            }
        }

        // 기존 bool 토큰 상태를 명시적인 화면 효과 상태로 변환한다.
        private static DialogueScreenEffectState ResolveScreenEffectState(bool show)
        {
            return show
                ? DialogueScreenEffectState.Show
                : DialogueScreenEffectState.Hide;
        }

        private async UniTaskVoid BouncePortraitTwice(Transform portrait, CancellationToken ct)
        {
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);

            float height = UsesUiSpace(portrait) ? portraitBounceUiHeight : portraitBounceWorldHeight;
            const int jumpCount = 2;
            for (int jump = 0; jump < jumpCount; jump++)
            {
                SoundPlayer.PlayPortraitBounceSfx();
                float elapsed = 0f;
                while (elapsed < portraitBounceDuration)
                {
                    if (portrait == null)
                    {
                        FinishEffect(false);
                        return;
                    }

                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / portraitBounceDuration);
                    float offset = Mathf.Sin(t * Mathf.PI) * height;
                    portrait.localPosition = effectOriginalLocalPosition + Vector3.up * offset;
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }

            FinishEffect(true);
        }

        // Default/Closeup 렌더러를 전환해 즉시 클로즈업하거나 기본 화면으로 복원한다.
        private void PlayPortraitCloseup(bool restore)
        {
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);

            hasCloseupState = !restore;
            panel?.SetPortraitCloseup(hasCloseupState);

            FinishEffect(false);
        }

        // 새 대사에서 포트레이트가 다시 설정돼도 진행 중인 클로즈업 표시를 유지한다.
        private void ApplyPersistentCloseup()
        {
            if (panel == null || panel.CurrentPortraitTransform == null)
            {
                return;
            }

            panel.SetPortraitCloseup(hasCloseupState);
        }

        private async UniTaskVoid SlidePortrait(Transform portrait, float direction, bool entering, bool dramatic, CancellationToken ct)
        {
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);

            float distance = UsesUiSpace(portrait) ? portraitSlideUiDistance : portraitSlideWorldDistance;
            Vector3 sideOffset = Vector3.right * direction * distance;
            Vector3 start = entering ? effectOriginalLocalPosition + sideOffset : effectOriginalLocalPosition;
            Vector3 end = entering ? effectOriginalLocalPosition : effectOriginalLocalPosition + sideOffset;
            float duration = dramatic ? portraitDramaticSlideDuration : portraitSlideDuration;

            portrait.localPosition = start;
            float shakeAmount = UsesUiSpace(portrait) ? portraitDramaticShakeUiAmount : portraitDramaticShakeWorldAmount;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (portrait == null)
                {
                    FinishEffect(false);
                    return;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = dramatic ? EaseOutBack(t) : SmoothStep(t);
                Vector3 basePosition = Vector3.LerpUnclamped(start, end, eased);

                if (dramatic)
                {
                    float fade = 1f - t;
                    float x = Mathf.Sin(elapsed * portraitDramaticShakeFrequency) * shakeAmount * fade;
                    float y = Mathf.Sin(elapsed * portraitDramaticShakeFrequency * 1.73f) * shakeAmount * 0.45f * fade;
                    basePosition += new Vector3(x, y, 0f);
                }

                portrait.localPosition = basePosition;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            portrait.localPosition = end;
            portrait.localScale = effectOriginalLocalScale;
            if (!entering)
            {
                panel?.ClearPortraits();
            }

            FinishEffect(entering);
        }

        private async UniTaskVoid FadePortrait(Transform portrait, float from, float to, float duration, bool clearOnComplete, CancellationToken ct)
        {
            isEffectPlaying = true;
            panel?.SetWaitingForInput(false);
            SetPortraitAlphaFactor(from);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (portrait == null)
                {
                    FinishEffect(false);
                    return;
                }

                elapsed += Time.deltaTime;
                float t = SmoothStep(Mathf.Clamp01(elapsed / duration));
                SetPortraitAlphaFactor(Mathf.Lerp(from, to, t));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            SetPortraitAlphaFactor(to);
            if (clearOnComplete)
            {
                panel?.ClearPortraits();
            }

            FinishEffect(!clearOnComplete);
        }

        private void FinishEffect(bool resetPosition)
        {
            if (resetPosition && effectTarget != null)
            {
                effectTarget.localPosition = effectOriginalLocalPosition;
                effectTarget.localScale = effectOriginalLocalScale;
                RestorePortraitAlpha();
            }

            if (activeLineEffectCount > 0)
            {
                activeLineEffectCount--;
            }

            if (activeLineEffectCount > 0)
            {
                return;
            }

            effectCts?.Dispose();
            effectCts = null;
            effectTarget = null;
            ClearPortraitAlphaStates();
            isEffectPlaying = false;
            panel?.SetWaitingForInput(IsPlaying && !isTyping && !isAutoAdvancingEffectOnlyLine);
        }

        private void StopEffect(bool resetPosition)
        {
            CancelEffect();
            activeLineEffectCount = 0;

            if (resetPosition && effectTarget != null)
            {
                effectTarget.localPosition = effectOriginalLocalPosition;
                effectTarget.localScale = effectOriginalLocalScale;
                RestorePortraitAlpha();
            }

            effectTarget = null;
            ClearPortraitAlphaStates();
            isEffectPlaying = false;
        }

        private static bool UsesUiSpace(Transform target)
        {
            if (target == null || target.GetComponent<SpriteRenderer>() != null)
            {
                return false;
            }

            return target is RectTransform || target.parent is RectTransform;
        }

        private static float SmoothStep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.70158f;
            t -= 1f;
            return 1f + t * t * ((overshoot + 1f) * t + overshoot);
        }

        private void CapturePortraitAlpha(Transform target)
        {
            ClearPortraitAlphaStates();
            if (target == null)
            {
                return;
            }

            var spriteRenderers = target.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                SpriteRenderer renderer = spriteRenderers[i];
                if (renderer != null)
                {
                    effectSpriteAlphaStates.Add(new SpriteAlphaState(renderer, renderer.color.a));
                }
            }

            var graphics = target.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                UnityEngine.UI.Graphic graphic = graphics[i];
                if (graphic != null)
                {
                    effectGraphicAlphaStates.Add(new GraphicAlphaState(graphic, graphic.color.a));
                }
            }
        }

        private void SetPortraitAlphaFactor(float alphaFactor)
        {
            alphaFactor = Mathf.Clamp01(alphaFactor);

            for (int i = 0; i < effectSpriteAlphaStates.Count; i++)
            {
                SpriteRenderer renderer = effectSpriteAlphaStates[i].Renderer;
                if (renderer == null)
                {
                    continue;
                }

                Color color = renderer.color;
                color.a = effectSpriteAlphaStates[i].BaseAlpha * alphaFactor;
                renderer.color = color;
            }

            for (int i = 0; i < effectGraphicAlphaStates.Count; i++)
            {
                UnityEngine.UI.Graphic graphic = effectGraphicAlphaStates[i].Graphic;
                if (graphic == null)
                {
                    continue;
                }

                Color color = graphic.color;
                color.a = effectGraphicAlphaStates[i].BaseAlpha * alphaFactor;
                graphic.color = color;
            }
        }

        private void RestorePortraitAlpha()
        {
            SetPortraitAlphaFactor(1f);
        }

        private void ClearPortraitAlphaStates()
        {
            effectSpriteAlphaStates.Clear();
            effectGraphicAlphaStates.Clear();
        }

        private readonly struct SpriteAlphaState
        {
            public readonly SpriteRenderer Renderer;
            public readonly float BaseAlpha;

            public SpriteAlphaState(SpriteRenderer renderer, float baseAlpha)
            {
                Renderer = renderer;
                BaseAlpha = baseAlpha;
            }
        }

        private readonly struct GraphicAlphaState
        {
            public readonly UnityEngine.UI.Graphic Graphic;
            public readonly float BaseAlpha;

            public GraphicAlphaState(UnityEngine.UI.Graphic graphic, float baseAlpha)
            {
                Graphic = graphic;
                BaseAlpha = baseAlpha;
            }
        }

        private struct InlineSfxCue
        {
            public readonly int VisibleIndex;
            public readonly int Order;
            public readonly string SfxId;
            public bool Played;

            public InlineSfxCue(int visibleIndex, int order, string sfxId)
            {
                VisibleIndex = visibleIndex;
                Order = order;
                SfxId = sfxId ?? string.Empty;
                Played = false;
            }
        }

        private struct InlineWaitCue
        {
            public readonly int VisibleIndex;
            public readonly int Order;
            public readonly float Seconds;
            public bool Played;

            public InlineWaitCue(int visibleIndex, int order, float seconds)
            {
                VisibleIndex = visibleIndex;
                Order = order;
                Seconds = Mathf.Max(0f, seconds);
                Played = false;
            }
        }

        private struct InlineShakeCue
        {
            public readonly int VisibleIndex;
            public readonly int Order;
            public bool Played;

            public InlineShakeCue(int visibleIndex, int order)
            {
                VisibleIndex = visibleIndex;
                Order = order;
                Played = false;
            }
        }

        private void SetCurrentPortrait(Sprite sprite)
        {
            if (currentPortraitSprite == sprite)
            {
                return;
            }

            currentPortraitSprite = sprite;
            PortraitSpriteChanged?.Invoke(currentPortraitSprite);
        }

        // 연이 포트레이트는 다른 화자나 지문의 차례에 검정 계열로 낮추고, 연이 대사에서 원래 색으로 복구한다.
        private void ApplyActiveSpeakerPortraitTint(string activeSpeakerId)
        {
            if (currentPortraitSprite == null || !IsYeonSpeaker(currentPortraitSpeakerId))
            {
                return;
            }

            Color tint = IsYeonSpeaker(activeSpeakerId)
                ? currentPortraitTint
                : inactiveYeonPortraitTint;
            panel?.SetPortraitTint(tint);
        }

        private static bool IsYeonSpeaker(string speakerId)
        {
            return string.Equals(speakerId, YeonSpeakerId, StringComparison.Ordinal) ||
                string.Equals(speakerId, HiddenYeonSpeakerId, StringComparison.Ordinal);
        }

        // 대화 UI를 완전히 닫을 때 유지 중인 포트레이트 상태를 정리한다.
        public void ClearPortrait()
        {
            hasCloseupState = false;
            currentPortraitSpeakerId = string.Empty;
            currentPortraitTint = Color.white;
            SetCurrentPortrait(null);
            panel?.ClearPortraits();
        }

        // 검정ON/OFF처럼 즉시 return하는 연출과 함께 있어도 초상화를 먼저 지운다.
        private void ClearPortraitBeforeBlockingEffects(string[] effectNames)
        {
            if (effectNames == null)
            {
                return;
            }

            for (int i = 0; i < effectNames.Length; i++)
            {
                if (effectNames[i].Trim() == "즉시퇴장")
                {
                    ClearPortrait();
                    return;
                }
            }
        }

        private static bool WasAdvancePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (Escape.Input.GameScreenInputArea.Contains(Mouse.current.position.ReadValue()))
                {
                    return true;
                }
            }

            if (Keyboard.current != null &&
                (Keyboard.current.spaceKey.wasPressedThisFrame ||
                 Keyboard.current.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                if (Escape.Input.GameScreenInputArea.Contains(Touchscreen.current.primaryTouch.position.ReadValue()))
                {
                    return true;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                return true;
            }

            if (Input.GetMouseButtonUp(0))
            {
                return Escape.Input.GameScreenInputArea.Contains(Input.mousePosition);
            }
#endif
            return false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // F1은 일반 진행 입력과 분리해 현재 story 전체를 건너뛴다.
        private static bool WasStorySkipPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F1))
            {
                return true;
            }
#endif
            return false;
        }
#endif

        // 개발용 Ctrl을 누르는 동안 현재 줄을 즉시 표시하고 다음 줄 대기를 생략한다.
        private bool IsFastForwardActive()
        {
            return IsFastForwardHeld();
        }

        private static bool IsFastForwardHeld()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                (Keyboard.current.leftCtrlKey.isPressed ||
                 Keyboard.current.rightCtrlKey.isPressed))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                return true;
            }
#endif
#endif
            return false;
        }
    }

}
