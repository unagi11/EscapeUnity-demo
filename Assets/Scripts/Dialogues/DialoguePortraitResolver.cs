using System;
using Escape.Data;
using UnityEngine;

namespace Escape.Dialogues
{
    // Dialogue TSV의 speaker_id와 face 값을 Resources/Portraits의 Sprite로 해석한다.
    public static class DialoguePortraitResolver
    {
        private const string PortraitResourceRoot = "Portraits/";
        private const string DefaultFace = "default";

        public static Sprite Load(Dialogue dialogue)
        {
            return Load(dialogue, null);
        }

        public static Sprite Load(Dialogue dialogue, TsvTable<Speaker> speakerTable)
        {
            string resourcePath = ResolveResourcePath(dialogue, speakerTable);
            return LoadSprite(resourcePath);
        }

        public static Color ResolveTint(Speaker speaker, Color fallback)
        {
            string color = (speaker != null ? speaker.color : string.Empty) ?? string.Empty;
            color = color.Trim();
            if (string.IsNullOrWhiteSpace(color))
            {
                return fallback;
            }

            if (!color.StartsWith("#", StringComparison.Ordinal))
            {
                color = "#" + color;
            }

            return ColorUtility.TryParseHtmlString(color, out Color parsed) ? parsed : fallback;
        }

        public static float ResolveScale(Speaker speaker, float fallback)
        {
            string scale = (speaker != null ? speaker.scale : string.Empty) ?? string.Empty;
            return float.TryParse(scale.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? Mathf.Max(0.01f, parsed)
                : fallback;
        }

        private static string ResolveResourcePath(Dialogue dialogue, TsvTable<Speaker> speakerTable)
        {
            if (dialogue == null)
            {
                return string.Empty;
            }

            string speakerId = NormalizeToken(dialogue.speaker_id);
            if (string.IsNullOrWhiteSpace(speakerId))
            {
                return string.Empty;
            }

            return ResolveSpeakerPortraitPath(speakerId, dialogue.face, speakerTable);
        }

        // speaker.tsv의 짧은 기본 키와 face를 `Portraits/{key}_{face}` 경로로 조합한다.
        private static string ResolveSpeakerPortraitPath(
            string speakerId,
            string face,
            TsvTable<Speaker> speakerTable)
        {
            string portraitKey = speakerId;
            if (speakerTable != null && speakerTable.TryGet(speakerId, out Speaker speaker))
            {
                string configuredKey = (speaker != null ? speaker.path : string.Empty)?.Trim() ?? string.Empty;
                if (string.Equals(configuredKey, "none", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(configuredKey))
                {
                    portraitKey = NormalizePortraitKey(configuredKey);
                }
            }

            face = NormalizeToken(face);
            if (string.IsNullOrWhiteSpace(face))
            {
                face = DefaultFace;
            }

            return string.IsNullOrWhiteSpace(portraitKey)
                ? string.Empty
                : PortraitResourceRoot + portraitKey + "_" + face;
        }

        // 기존 전체 경로도 짧은 기본 키로 정규화해 데이터 이전을 안전하게 처리한다.
        private static string NormalizePortraitKey(string value)
        {
            string key = (value ?? string.Empty).Trim().Replace('\\', '/');
            if (key.StartsWith(PortraitResourceRoot, StringComparison.Ordinal))
            {
                key = key.Substring(PortraitResourceRoot.Length);
            }

            const string defaultSuffix = "_default";
            if (key.EndsWith(defaultSuffix, StringComparison.Ordinal))
            {
                key = key.Substring(0, key.Length - defaultSuffix.Length);
            }

            return key.Trim('/');
        }

        private static Sprite LoadSprite(string resourcePath)
        {
            resourcePath = NormalizeToken(resourcePath);
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return null;
            }

            Sprite sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null)
            {
                return sprite;
            }

            Sprite[] sprites = Resources.LoadAll<Sprite>(resourcePath);
            return sprites != null && sprites.Length > 0 ? sprites[0] : null;
        }

        private static string NormalizeToken(string value)
        {
            value = (value ?? string.Empty).Trim();
            return string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
        }
    }
}
