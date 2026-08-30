using System;
using Escape.Dialogues;
using Escape.Localization;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.Data;
using Escape.UI;
using UnityEngine;

namespace Escape.Rooms
{
    // Room 대사 묶음의 구성, 재생 대기, 후속 게임오버 대사와 패널 수명주기를 관리한다.
    internal sealed class RoomDialogueStoryPlayer
    {
        private const string LogPrefix = "[RoomDialogueStoryPlayer]";
        private const string HealthGameOverDialogueId = "health_game_over_yeon";

        private readonly TsvTable<Dialogue> dialogueTable;
        private readonly RoomDialogueLineFactory lineFactory;
        private readonly Func<DialoguePlayer> getDialoguePlayer;
        private readonly Func<DialoguePopupUI> getDialoguePanel;
        private readonly AdventurePanelVisibilityController adventurePanel;
        private int playDepth;

        public bool IsPlaying => playDepth > 0;

        // 대사 데이터와 UI 재생 의존성을 연결한다.
        public RoomDialogueStoryPlayer(
            TsvTable<Dialogue> dialogueTable,
            RoomDialogueLineFactory lineFactory,
            Func<DialoguePlayer> getDialoguePlayer,
            Func<DialoguePopupUI> getDialoguePanel,
            AdventurePanelVisibilityController adventurePanel)
        {
            this.dialogueTable = dialogueTable;
            this.lineFactory = lineFactory;
            this.getDialoguePlayer = getDialoguePlayer;
            this.getDialoguePanel = getDialoguePanel;
            this.adventurePanel = adventurePanel;
        }

        // TSV 대사를 순서대로 재생하고 선택지에서 획득한 flag 결과를 반환한다.
        public async UniTask<DialogueStoryResult> PlayAsync(
            string dialogueId,
            string sourceName,
            CancellationToken cancellationToken,
            IReadOnlyList<DialogueLine> appendedLines = null)
        {
            playDepth++;
            adventurePanel.PushHidden();
            var result = new DialogueStoryResult(dialogueId);
            try
            {
                if (string.IsNullOrWhiteSpace(dialogueId))
                {
                    bool playedAppendedOnly = PlayLines(appendedLines, false);
                    await WaitForDialogue(playedAppendedOnly, cancellationToken);
                    HidePanel();
                    return result;
                }

                if (dialogueTable == null || !dialogueTable.TryGetRows(dialogueId, out var dialogues))
                {
                    Debug.LogWarning($"Dialogue id not found: {dialogueId} ({sourceName})");
                    bool playedFallback = PlayLines(appendedLines, false);
                    await WaitForDialogue(playedFallback, cancellationToken);
                    HidePanel();
                    return result;
                }

                Debug.Log($"{LogPrefix} Dialogue start: {dialogueId} (rows={dialogues.Count})");
                var storyLines = new List<DialogueLine>(dialogues.Count);
                for (int i = 0; i < dialogues.Count; i++)
                {
                    Dialogue dialogue = dialogues[i];
                    storyLines.Add(lineFactory.CreateStoryLine(dialogue));
                    lineFactory.AppendEffectResultLines(storyLines, dialogue);
                }

                if (appendedLines != null && appendedLines.Count > 0)
                {
                    storyLines.AddRange(appendedLines);
                }

                bool playedStory = PlayLines(storyLines, false, dialogueId);
                await WaitForDialogue(playedStory, cancellationToken);

                DialoguePlayer dialoguePlayer = getDialoguePlayer?.Invoke();
                if (dialoguePlayer != null && dialoguePlayer.CurrentStoryResult != null)
                {
                    result = dialoguePlayer.CurrentStoryResult;
                }

                if (result.RequestsGameOver)
                {
                    await PlayAsync(
                        HealthGameOverDialogueId,
                        sourceName,
                        cancellationToken);
                }

                Debug.Log($"{LogPrefix} Dialogue end: {dialogueId} (flags={string.Join(",", result.Flags)})");
                HidePanel();
                return result;
            }
            finally
            {
                adventurePanel.PopHidden();
                playDepth = Mathf.Max(0, playDepth - 1);
            }
        }

        // 현지화 키 하나를 짧은 시스템 대사로 재생한다.
        public async UniTask PlaySingleLineAsync(string textId, CancellationToken cancellationToken)
        {
            var lines = new[] { new DialogueLine(string.Empty, LocalizationService.Text(textId)) };
            bool playedDialogue = PlayLines(lines, false);
            await WaitForDialogue(playedDialogue, cancellationToken);
            HidePanel();
        }

        // 대화 묶음 종료 시 패널과 유지 중인 초상화·배경을 함께 정리한다.
        public void HidePanel()
        {
            DialoguePlayer dialoguePlayer = getDialoguePlayer?.Invoke();
            dialoguePlayer?.ClearPortrait();
            dialoguePlayer?.ClearBackground();
            getDialoguePanel?.Invoke()?.Hide();
        }

        // 대사 목록을 DialoguePlayer가 소비할 배열로 복사해 재생한다.
        private bool PlayLines(
            IReadOnlyList<DialogueLine> lines,
            bool hidePanelWhenComplete = true,
            string dialogueId = "")
        {
            DialoguePlayer dialoguePlayer = getDialoguePlayer?.Invoke();
            if (lines == null || lines.Count == 0 || dialoguePlayer == null)
            {
                return false;
            }

            var dialogueLines = new DialogueLine[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                dialogueLines[i] = lines[i];
            }

            dialoguePlayer.Play(dialogueLines, hidePanelWhenComplete, dialogueId);
            return true;
        }

        // DialoguePlayer의 현재 재생이 완전히 끝날 때까지 기다린다.
        private async UniTask WaitForDialogue(bool playedDialogue, CancellationToken cancellationToken)
        {
            DialoguePlayer dialoguePlayer = getDialoguePlayer?.Invoke();
            while (playedDialogue && dialoguePlayer != null && dialoguePlayer.IsPlaying)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }
    }
}
