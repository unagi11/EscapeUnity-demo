using System;
using Escape.Localization;
using Escape.Progress;
using Escape.Data;
using UnityEngine;

namespace Escape.Dialogues
{
    // 대사 출력 시 초상화가 어느 위치에 있는지 나타낸다.
    // TSV 대사를 UI에 출력하기 좋게 가공한 런타임 대사 데이터.
    public sealed class DialogueLine
    {
        public string SpeakerId { get; }
        public string SpeakerName { get; }
        public string Text { get; }
        public Sprite PortraitSprite { get; }
        public Color PortraitTint { get; }
        public float PortraitScale { get; }
        public string Effect { get; }
        public string TypingSfx { get; }
        public string DialogueType { get; }
        public string BackgroundPath { get; }
        public string Bgm { get; }
        public string Shader { get; }
        private readonly Dialogue sourceDialogue;
        private readonly Speaker sourceSpeaker;
        // 이 줄의 표시 조건식(비면 항상 표시). 선택지 줄에서는 선택 시 부여하는 플래그.
        public string Flag { get; }
        // DialogueType이 SELECT_n이면 선택지 줄이다.
        public bool IsChoice =>
            !string.IsNullOrWhiteSpace(DialogueType) &&
            DialogueType.StartsWith("SELECT_", StringComparison.OrdinalIgnoreCase);
        // 간단한 문자열 대사를 기본 중앙 초상화 상태로 만든다.
        public DialogueLine(string speaker, string text)
            : this(speaker, speaker, text, null, Color.white, 1f, string.Empty)
        {
        }

        // TSV 대사와 화자 정보를 합친 출력용 대사 라인을 만든다.
        public DialogueLine(
            string speakerId,
            string speakerName,
            string text,
            Sprite portraitSprite)
            : this(speakerId, speakerName, text, portraitSprite, Color.white, 1f, string.Empty)
        {
        }

        public DialogueLine(
            string speakerId,
            string speakerName,
            string text,
            Sprite portraitSprite,
            string effect)
            : this(speakerId, speakerName, text, portraitSprite, Color.white, 1f, effect)
        {
        }

        public DialogueLine(
            string speakerId,
            string speakerName,
            string text,
            Sprite portraitSprite,
            Color portraitTint,
            float portraitScale,
            string effect,
            string typingSfx = "",
            string dialogueType = "DEFAULT",
            string backgroundPath = "",
            string bgm = "",
            string flag = "",
            string shader = "",
            Dialogue sourceDialogue = null,
            Speaker sourceSpeaker = null)
        {
            SpeakerId = speakerId ?? string.Empty;
            SpeakerName = speakerName ?? string.Empty;
            Text = text ?? string.Empty;
            PortraitSprite = portraitSprite;
            PortraitTint = portraitTint;
            PortraitScale = Mathf.Max(0.01f, portraitScale);
            Effect = effect ?? string.Empty;
            TypingSfx = typingSfx ?? string.Empty;
            DialogueType = string.IsNullOrWhiteSpace(dialogueType) ? "DEFAULT" : dialogueType.Trim();
            BackgroundPath = backgroundPath ?? string.Empty;
            Bgm = bgm ?? string.Empty;
            Shader = shader ?? string.Empty;
            Flag = (flag ?? string.Empty).Trim();
            this.sourceDialogue = sourceDialogue;
            this.sourceSpeaker = sourceSpeaker;
        }

        // 원본 TSV가 있으면 현재 언어로 본문을 다시 해석한다.
        public string ResolveText()
        {
            string text = sourceDialogue != null
                ? LocalizationService.Localized(sourceDialogue, nameof(Dialogue.text), sourceDialogue.text)
                : Text;
            return (text ?? string.Empty)
                .Replace("\\t", "\t")
                .Replace("\\n", "\n");
        }

        // 표시명이 비어 있으면 speakerId를 대신 보여준다.
        public string ResolveSpeakerName()
        {
            if (string.Equals(SpeakerId, GameSession.HeroSpeakerId, StringComparison.Ordinal))
            {
                return GameSession.GetPlayerNameOrDefault();
            }

            string speakerName = sourceSpeaker != null
                ? LocalizationService.Localized(sourceSpeaker, nameof(Speaker.name), sourceSpeaker.name)
                : SpeakerName;
            return string.IsNullOrWhiteSpace(speakerName) ? SpeakerId : speakerName;
        }
    }
}
