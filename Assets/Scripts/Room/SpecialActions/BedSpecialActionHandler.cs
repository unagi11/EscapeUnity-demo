using System;
using Escape.Localization;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.UI;
using UnityEngine;

namespace Escape.Rooms
{
    // 침대 선택지 구성과 선택 이후의 기능 흐름을 담당한다.
    internal sealed class BedSpecialActionHandler : IRoomSpecialActionHandler
    {
        private const string BedMenuPromptTid = "bed_menu_prompt";
        private const string BedMenuSleepTalkTid = "bed_menu_sleep_talk";
        private const string BedMenuCancelTid = "bed_menu_cancel";
        private const string BedSleepTalkChoice = "BED_SLEEP_TALK";
        private const string BedCancelChoice = "BED_CANCEL";
        private static readonly string[] BedSleepTalkDialogueIds =
        {
                "bed_sleep_talk_forever",
                "bed_sleep_talk_destroy",
                "bed_sleep_talk_nothing_inside",
                "bed_sleep_talk_protect",
            };

        private static readonly InteractionSpecialAction[] SupportedActions =
        {
                InteractionSpecialAction.OpenBedInteractionMenu,
        };

        private readonly DialoguePopupUI dialoguePanel;
        private readonly Func<string, RoomInteractor, CancellationToken, UniTask<DialogueStoryResult>> playDialogueStory;

        public IReadOnlyList<InteractionSpecialAction> Actions => SupportedActions;

        // 침대 흐름에 필요한 UI, 상태, 공용 실행 함수만 주입받는다.
        public BedSpecialActionHandler(
            DialoguePopupUI dialoguePanel,
            Func<string, RoomInteractor, CancellationToken, UniTask<DialogueStoryResult>> playDialogueStory)
        {
            this.dialoguePanel = dialoguePanel;
            this.playDialogueStory = playDialogueStory;
        }

        // 침대 선택 메뉴를 열고 선택 결과를 실행한다.
        public UniTask ExecuteAsync(
            InteractionSpecialAction action,
            RoomInteractor interactable,
            CancellationToken cancellationToken)
        {
            return OpenBedInteractionMenu(interactable, cancellationToken);
        }

        // 실제 선택 버튼 수에 맞춰 침대 메뉴를 나눠 보여준다.
        private async UniTask OpenBedInteractionMenu(
            RoomInteractor interactable,
            CancellationToken cancellationToken)
        {
            int choiceCapacity = dialoguePanel != null ? dialoguePanel.ChoiceCapacity : 0;
            if (choiceCapacity <= 0)
            {
                Debug.LogWarning("DialoguePopupUI SelectPanel requires at least one choice button.");
                return;
            }

            var choices = new List<DialogueChoice>();
            if (choiceCapacity >= 2)
            {
                choices.Add(new DialogueChoice(
                    BedSleepTalkChoice,
                    LocalizationService.Text(BedMenuSleepTalkTid),
                    1));
            }

            choices.Add(new DialogueChoice(
                BedCancelChoice,
                LocalizationService.Text(BedMenuCancelTid),
                choices.Count + 1));

            dialoguePanel.Show(string.Empty, LocalizationService.Text(BedMenuPromptTid));
            DialogueChoice selected = await ShowDialogueChoices(
                "bed_menu_demo",
                choices,
                cancellationToken);

            if (selected?.Flag == BedSleepTalkChoice)
            {
                await playDialogueStory(
                    GetRandomBedSleepTalkDialogueId(),
                    interactable,
                    cancellationToken);
            }
            else
            {
                dialoguePanel.Hide();
            }
        }

        // 버튼 선택이 끝날 때까지 기다려 선택된 행동을 반환한다.
        private async UniTask<DialogueChoice> ShowDialogueChoices(
            string sourceDialogueId,
            IReadOnlyList<DialogueChoice> choices,
            CancellationToken cancellationToken)
        {
            if (dialoguePanel == null)
            {
                Debug.LogWarning("BedSpecialActionHandler requires a DialoguePopupUI.");
                return null;
            }

            choices ??= Array.Empty<DialogueChoice>();
            if (choices.Count > dialoguePanel.ChoiceCapacity)
            {
                Debug.LogWarning(
                    $"DialoguePopupUI SelectPanel does not have enough buttons: " +
                    $"{sourceDialogueId} choices={choices.Count}, capacity={dialoguePanel.ChoiceCapacity}");
                return null;
            }

            bool selected = false;
            DialogueChoice selectedChoice = null;
            var labels = new string[choices.Count];
            for (int i = 0; i < choices.Count; i++)
            {
                labels[i] = choices[i].Label;
            }

            dialoguePanel.SetVisible(true);
            dialoguePanel.ShowChoices(labels, index =>
            {
                if (index >= 0 && index < choices.Count)
                {
                    selectedChoice = choices[index];
                    selected = true;
                }
            });

            while (!selected)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            return selectedChoice;
        }

        // 준비된 잠꼬대 문장 중 하나를 같은 확률로 고른다.
        private static string GetRandomBedSleepTalkDialogueId()
        {
            int index = UnityEngine.Random.Range(0, BedSleepTalkDialogueIds.Length);
            return BedSleepTalkDialogueIds[index];
        }
        // 침대 메뉴에 표시할 선택지의 값과 순서를 보관한다.
        private sealed class DialogueChoice
        {
            public readonly string Flag;
            public readonly string Label;
            public readonly int Order;

            public DialogueChoice(string flag, string label, int order)
            {
                Flag = flag ?? string.Empty;
                Label = label ?? string.Empty;
                Order = order;
            }
        }
    }
}
