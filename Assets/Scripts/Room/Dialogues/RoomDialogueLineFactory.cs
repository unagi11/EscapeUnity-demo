using System;
using Escape.Dialogues;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using Escape.Data;
using Escape.UI;
using UnityEngine;

namespace Escape.Rooms
{
    // TSV 데이터와 게임 상태를 화면 출력용 DialogueLine으로 변환한다.
    internal sealed class RoomDialogueLineFactory
    {
        private const string DialogueChoiceTypePrefix = "SELECT_";

        private readonly TsvTable<Dialogue> dialogueTable;
        private readonly TsvTable<Speaker> speakerTable;
        private readonly TsvTable<Item> itemTable;
        private readonly TsvTable<Info> infoTable;
        private readonly Func<DialoguePopupUI> getDialoguePanel;
        private readonly Func<GameSession> getState;

        // 대사 원본 테이블과 읽기 전용 상태 조회 수단을 연결한다.
        public RoomDialogueLineFactory(
            TsvTable<Dialogue> dialogueTable,
            TsvTable<Speaker> speakerTable,
            TsvTable<Item> itemTable,
            TsvTable<Info> infoTable,
            Func<DialoguePopupUI> getDialoguePanel,
            Func<GameSession> getState)
        {
            this.dialogueTable = dialogueTable;
            this.speakerTable = speakerTable;
            this.itemTable = itemTable;
            this.infoTable = infoTable;
            this.getDialoguePanel = getDialoguePanel;
            this.getState = getState;
        }

        // 선택지 행은 자동 결과 대사를 붙이지 않도록 구분한다.
        private static bool IsDialogueChoice(Dialogue dialogue)
        {
            return dialogue != null &&
                   !string.IsNullOrWhiteSpace(dialogue.type) &&
                   dialogue.type.Trim().StartsWith(DialogueChoiceTypePrefix, StringComparison.OrdinalIgnoreCase);
        }

        // 복합 effect 문자열을 빈 값 없이 정규화한다.
        private static string[] SplitTokens(string value, params char[] separators)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            string[] rawTokens = value.Split(separators, StringSplitOptions.RemoveEmptyEntries);
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

        // 획득한 아이템마다 시스템 안내 대사를 만든다.
        private DialogueLine[] BuildGrantedItemDialogueLines(IReadOnlyList<string> itemIds)
        {
            if (itemIds == null || itemIds.Count == 0)
            {
                return Array.Empty<DialogueLine>();
            }

            var lines = new DialogueLine[itemIds.Count];
            for (int i = 0; i < itemIds.Count; i++)
            {
                string itemName = GetItemName(itemIds[i]);
                lines[i] = CreateItemAcquiredLine(itemName);
            }

            return lines;
        }

        // 아이템 변화와 탈출의지 변화 effect 뒤에 공용 시스템 대사를 자동으로 붙인다.
        public void AppendEffectResultLines(List<DialogueLine> storyLines, Dialogue sourceDialogue)
        {
            if (storyLines == null || sourceDialogue == null || IsDialogueChoice(sourceDialogue))
            {
                return;
            }

            string[] effectTokens = SplitTokens(sourceDialogue.effect, '+', ',', '|');
            for (int i = 0; i < effectTokens.Length; i++)
            {
                DialogueStoryEffect storyEffect = DialogueStoryEffectParser.Parse(effectTokens[i]);
                if (storyEffect == DialogueStoryEffect.HealthMinusOne ||
                    storyEffect == DialogueStoryEffect.HealthPlusOne)
                {
                    if (!ShouldAppendEscapeWillChangeLine(storyEffect))
                    {
                        continue;
                    }

                    storyLines.Add(CreateEscapeWillChangedLine(storyEffect, sourceDialogue.flag));
                    continue;
                }

                if (!DialoguePlayer.TryParseItemInventoryEffectToken(
                        effectTokens[i],
                        out bool acquire,
                        out string itemId) ||
                    string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                string itemName = GetItemName(itemId);
                DialogueLine resultLine = acquire
                    ? CreateItemAcquiredLine(itemName, sourceDialogue.flag)
                    : CreateItemUsedLine(itemName, sourceDialogue.flag);
                storyLines.Add(resultLine);
            }
        }

        // 회복 대사는 풀피에도 보여 주고, 더 감소할 수 없는 경우에만 감소 대사를 생략한다.
        private bool ShouldAppendEscapeWillChangeLine(DialogueStoryEffect storyEffect)
        {
            GameSession state = getState();
            if (state == null)
            {
                return false;
            }

            return storyEffect == DialogueStoryEffect.HealthMinusOne
                ? state.CurrentHealth > 0
                : true;
        }

        // 원본 사건 묘사에서는 탈출의지 토큰만 빼서 자동 각성대사에서 한 번 실행되게 한다.
        private static string RemoveEscapeWillChangeEffects(string effect)
        {
            string[] effectTokens = SplitTokens(effect, '+', ',', '|');
            var remainingTokens = new List<string>(effectTokens.Length);
            for (int i = 0; i < effectTokens.Length; i++)
            {
                DialogueStoryEffect storyEffect = DialogueStoryEffectParser.Parse(effectTokens[i]);
                if (storyEffect != DialogueStoryEffect.HealthMinusOne &&
                    storyEffect != DialogueStoryEffect.HealthPlusOne)
                {
                    remainingTokens.Add(effectTokens[i]);
                }
            }

            return string.Join("+", remainingTokens);
        }

        // 탈출의지 변화에 대응하는 공용 각성대사를 만들고 대사후에 수치를 변경한다.
        private DialogueLine CreateEscapeWillChangedLine(
            DialogueStoryEffect storyEffect,
            string flagOverride)
        {
            bool decreased = storyEffect == DialogueStoryEffect.HealthMinusOne;
            string dialogueId = decreased ? "escape_will_decreased" : "escape_will_increased";
            string fadeOn = decreased
                ? DialogueEffectTokens.RedFadeOn
                : DialogueEffectTokens.GreenFadeOn;
            string changeWill = decreased
                ? DialogueEffectTokens.EscapeWillDecreaseOne
                : DialogueEffectTokens.EscapeWillIncreaseOne;
            string fadeOff = decreased
                ? DialogueEffectTokens.RedFadeOff
                : DialogueEffectTokens.GreenFadeOff;
            string effect = DialogueEffectTokens.Sequence(
                DialogueEffectTokens.Before(fadeOn),
                DialogueEffectTokens.After(changeWill),
                DialogueEffectTokens.After(fadeOff));
            Dialogue dialogue = GetRequiredDialogue(dialogueId);

            return CreateDialogueLine(
                dialogue,
                UnescapeDialogueText(GetDialogueText(dialogue)),
                false,
                flagOverride,
                effect);
        }

        // 획득한 정보마다 시스템 안내 대사를 만든다.
        private DialogueLine[] BuildGrantedInfoDialogueLines(IReadOnlyList<string> infoIds)
        {
            if (infoIds == null || infoIds.Count == 0)
            {
                return Array.Empty<DialogueLine>();
            }

            var lines = new DialogueLine[infoIds.Count];
            for (int i = 0; i < infoIds.Count; i++)
            {
                string infoName = GetInfoName(infoIds[i]);
                lines[i] = CreateInfoAcquiredLine(infoName);
            }

            return lines;
        }

        // 실제 소비된 아이템 이름으로 소모 안내 대사를 만든다.
        private DialogueLine[] BuildConsumedItemDialogueLines(IReadOnlyList<string> itemIds)
        {
            if (itemIds == null || itemIds.Count == 0)
            {
                return Array.Empty<DialogueLine>();
            }

            var lines = new DialogueLine[itemIds.Count];
            for (int i = 0; i < itemIds.Count; i++)
            {
                lines[i] = CreateItemUsedLine(GetItemName(itemIds[i]));
            }

            return lines;
        }

        // 공용 item_used 템플릿으로 아이템 제거 안내 행을 만든다.
        private DialogueLine CreateItemUsedLine(string itemName, string flagOverride = null)
        {
            return CreateSystemResultLine("item_used", FormatItemReference(itemName), flagOverride);
        }

        // 아이템 사용·획득과 정보 획득 순서로 Action 결과 대사를 만든다.
        public DialogueLine[] BuildActionResultDialogueLines(
            IReadOnlyList<string> consumedItemIds,
            IReadOnlyList<string> grantedItemIds,
            IReadOnlyList<string> grantedInfoIds)
        {
            var lines = new List<DialogueLine>(BuildConsumedItemDialogueLines(consumedItemIds));
            lines.AddRange(BuildGrantedItemDialogueLines(grantedItemIds));
            lines.AddRange(BuildGrantedInfoDialogueLines(grantedInfoIds));
            return lines.ToArray();
        }

        // 공용 item_acquired 템플릿으로 아이템 획득 안내 행을 만든다.
        private DialogueLine CreateItemAcquiredLine(string itemName, string flagOverride = null)
        {
            return CreateSystemResultLine("item_acquired", itemName, flagOverride);
        }

        // 공용 info_acquired 템플릿으로 정보 획득 안내 행을 만든다.
        private DialogueLine CreateInfoAcquiredLine(string infoName)
        {
            return CreateSystemResultLine("info_acquired", infoName);
        }

        // 공용 TSV 템플릿의 첫 자리표시자에 결과 이름을 채운다.
        private DialogueLine CreateSystemResultLine(
            string dialogueId,
            string value,
            string flagOverride = null)
        {
            Dialogue dialogue = GetRequiredDialogue(dialogueId);
            string template = UnescapeDialogueText(GetDialogueText(dialogue));
            string text = string.Format(template, value);
            return CreateDialogueLine(dialogue, text, false, flagOverride);
        }

        // 아이템 ID에 대응하는 현지화 이름을 반환한다.
        private string GetItemName(string itemId)
        {
            if (itemTable != null && itemTable.TryGet(itemId, out Item item))
            {
                return LocalizationService.Localized(item, nameof(Item.name), item.name);
            }

            return itemId ?? string.Empty;
        }

        // 정보 ID에 대응하는 현지화 이름을 반환한다.
        private string GetInfoName(string infoId)
        {
            if (infoTable != null && infoTable.TryGet(infoId, out Info info))
            {
                return LocalizationService.Localized(info, nameof(Info.name), info.name);
            }

            return infoId ?? string.Empty;
        }

        // 시스템 대사 안의 아이템 이름에 공용 강조 색을 적용한다.
        private static string FormatItemReference(string itemName)
        {
            return $"<color=#D8E88F>[{itemName}]</color>";
        }

        // 공용 시스템 대사 템플릿을 가져오고 누락 시 데이터 오류를 즉시 알린다.
        private Dialogue GetRequiredDialogue(string dialogueId)
        {
            if (dialogueTable != null && dialogueTable.TryGet(dialogueId, out Dialogue dialogue))
            {
                return dialogue;
            }

            throw new InvalidOperationException($"Required dialogue template not found: {dialogueId}");
        }

        // TSV의 Dialogue/Speaker 데이터를 실제 UI 출력용 DialogueLine으로 합친다.
        public DialogueLine CreateStoryLine(Dialogue dialogue)
        {
            return CreateDialogueLine(
                dialogue,
                UnescapeDialogueText(GetDialogueText(dialogue)),
                true,
                effectOverride: RemoveEscapeWillChangeEffects(dialogue.effect));
        }

        // 한 Dialogue 행에 화자·초상화·효과 메타데이터를 결합한다.
        private DialogueLine CreateDialogueLine(
            Dialogue dialogue,
            string text,
            bool useSourceLocalization = false,
            string flagOverride = null,
            string effectOverride = null)
        {
            string speakerId = dialogue.speaker_id;
            Speaker speaker = null;
            if (speakerTable != null)
            {
                speakerTable.TryGet(speakerId, out speaker);
            }

            DialoguePopupUI dialoguePanel = getDialoguePanel();
            return new DialogueLine(
                speakerId,
                GetSpeakerName(speaker),
                text,
                DialoguePortraitResolver.Load(dialogue, speakerTable),
                DialoguePortraitResolver.ResolveTint(
                    speaker,
                    dialoguePanel != null
                        ? dialoguePanel.DefaultPortraitTint
                        : Color.white),
                DialoguePortraitResolver.ResolveScale(
                    speaker,
                    dialoguePanel != null
                        ? dialoguePanel.DefaultPortraitScale
                        : 1f),
                effectOverride ?? dialogue.effect,
                speaker != null ? speaker.typing_sfx : string.Empty,
                dialogue.type,
                dialogue.bg_path,
                dialogue.bgm,
                flagOverride ?? dialogue.flag,
                dialogue.shader,
                useSourceLocalization ? dialogue : null,
                useSourceLocalization ? speaker : null);
        }

        // 화자 데이터의 현지화 이름을 반환한다.
        private static string GetSpeakerName(Speaker speaker)
        {
            if (speaker == null)
            {
                return string.Empty;
            }

            return LocalizationService.Localized(speaker, nameof(Speaker.name), speaker.name);
        }

        // 대사 데이터의 현지화 본문을 반환한다.
        private static string GetDialogueText(Dialogue dialogue)
        {
            if (dialogue == null)
            {
                return string.Empty;
            }

            return LocalizationService.Localized(dialogue, nameof(Dialogue.text), dialogue.text);
        }

        // TSV 안의 \n, \t 표기를 실제 줄바꿈과 탭으로 바꾼다.
        private static string UnescapeDialogueText(string text)
        {
            return (text ?? string.Empty)
                .Replace("\\t", "\t")
                .Replace("\\n", "\n");
        }
    }
}
