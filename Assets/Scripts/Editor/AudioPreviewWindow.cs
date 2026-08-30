using System;
using Escape.Audio;
using UnityEditor;
using UnityEngine;

namespace Escape.Editor
{
    // Play Mode에서 게임 오디오로 BGM/SFX를 들어보는 간단한 프리뷰 창(재생 전용).
    public sealed class AudioPreviewWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Escape/Audio/BGM & SFX Preview";
        private const int SfxColumns = 3;

        private Vector2 scroll;
        private string status = "대기 중";

        [MenuItem(MenuPath)]
        private static void Open()
        {
            AudioPreviewWindow window = GetWindow<AudioPreviewWindow>("Audio Preview");
            window.minSize = new Vector2(360f, 480f);
            window.Show();
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("BGM & SFX Preview", EditorStyles.boldLabel);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Play Mode에서만 소리가 납니다.", MessageType.Info);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                DrawVolume();
                DrawBgm();
                DrawSfx();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void DrawVolume()
        {
            Header("Volume");
            Slider("Master", SoundPlayer.MasterVolume, SoundPlayer.SetMasterVolume);
            Slider("BGM", SoundPlayer.BgmVolume, SoundPlayer.SetBgmVolume);
            Slider("SFX", SoundPlayer.SfxVolume, SoundPlayer.SetSfxVolume);
        }

        private void DrawBgm()
        {
            Header("BGM");
            string current = ChipSynthPlayer.Instance != null ? ChipSynthPlayer.Instance.CurrentSongId : null;
            EditorGUILayout.LabelField("Now", string.IsNullOrEmpty(current) ? "<none>" : current);

            if (GUILayout.Button("Stop"))
            {
                SoundPlayer.StopBgm();
                status = "BGM 정지";
            }

            var songs = ChipSongLibrary.ListSongs();
            for (int i = 0; i < songs.Count; i++)
            {
                ChipSongEntry entry = songs[i];
                string prefix = string.Equals(current, entry.Id, StringComparison.Ordinal) ? "▶ " : string.Empty;
                if (GUILayout.Button($"{prefix}{entry.Title}  [{entry.Id}]"))
                {
                    SoundPlayer.PlayBgm(entry.Id);
                    status = $"BGM: {entry.Id}";
                }
            }
        }

        private void DrawSfx()
        {
            Header("SFX");
            var ids = ChipSfxLibrary.BuiltInIds;
            for (int i = 0; i < ids.Count; i += SfxColumns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < SfxColumns; c++)
                    {
                        int index = i + c;
                        if (index >= ids.Count)
                        {
                            GUILayout.FlexibleSpace();
                            continue;
                        }

                        string id = ids[index];
                        if (GUILayout.Button(id, GUILayout.MinWidth(100f)))
                        {
                            SoundPlayer.PlaySfx(id);
                            status = $"SFX: {id}";
                        }
                    }
                }
            }
        }

        private static void Slider(string label, float current, Action<float> setter)
        {
            float next = EditorGUILayout.Slider(label, current, 0f, 1f);
            if (!Mathf.Approximately(current, next))
            {
                setter(next);
            }
        }

        private static void Header(string title)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
