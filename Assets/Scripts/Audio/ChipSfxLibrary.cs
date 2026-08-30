using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Escape.Audio
{
    // SFX 정의(ChipSfxDefinition)를 내장 기본값 + StreamingAssets/ChipSfx JSON 오버라이드로 제공한다.
    // JSON 파일이 있으면 같은 id의 내장값을 대체하므로, 사용자가 노트를 파일로 조정할 수 있다.
    public static class ChipSfxLibrary
    {
        private const string SfxDirectoryName = "ChipSfx";

        // 키패드 각 자리 톤 주파수(과거 ChipSfxPlayer 값 그대로).
        private static readonly double[] KeypadFrequencies =
        {
            1975.53, 2093.00, 2349.32, 2637.02, 2793.83, 3135.96,
            3520.00, 3951.07, 4186.01, 4698.64, 5274.04, 5587.65,
        };

        private static readonly object loadLock = new();
        private static readonly Dictionary<string, ChipSfxDefinition> builtIns = BuildBuiltIns();
        private static readonly List<string> builtInIds = new(builtIns.Keys);
        private static Dictionary<string, ChipSfxDefinition> overrides = new(StringComparer.Ordinal);
        private static bool loaded;

        public static string SfxDirectoryPath => Path.Combine(Application.streamingAssetsPath, SfxDirectoryName);

        // AudioPreview 등에서 순회할 내장 SFX id 목록.
        public static IReadOnlyList<string> BuiltInIds => builtInIds;

        // JSON 오버라이드를 다시 읽는다.
        public static void Reload()
        {
            lock (loadLock)
            {
                loaded = false;
                LoadOverrides();
            }
        }

        // id에 해당하는 정의를 돌려준다(JSON 오버라이드 우선, 없으면 내장값).
        public static bool TryGetDefinition(string id, out ChipSfxDefinition definition)
        {
            EnsureLoaded();
            id = (id ?? string.Empty).Trim();
            if (overrides.TryGetValue(id, out definition) && definition != null)
            {
                return true;
            }

            return builtIns.TryGetValue(id, out definition) && definition != null;
        }

        // 키패드 자리(key)에 대한 단일 피에조 톤 정의를 만든다.
        public static ChipSfxDefinition BuildKeypadTone(char key)
        {
            int index = Mathf.Clamp(KeypadClipIndex(key), 0, KeypadFrequencies.Length - 1);
            double frequency = KeypadFrequencies[index];
            return Def($"keypad_{key}", $"DoorLockKeypad{key}", 0.075,
                Piezo(0.000, 0.075, frequency, 0.52f));
        }

        // 편집용으로 정의를 깊은 복사한다(JsonUtility 라운드트립).
        public static ChipSfxDefinition Clone(ChipSfxDefinition definition)
        {
            return definition == null
                ? null
                : JsonUtility.FromJson<ChipSfxDefinition>(JsonUtility.ToJson(definition));
        }

        // 내장 기본값 정의를 복사해 돌려준다(오버라이드 무시).
        public static ChipSfxDefinition GetBuiltIn(string id)
        {
            builtIns.TryGetValue((id ?? string.Empty).Trim(), out ChipSfxDefinition definition);
            return Clone(definition);
        }

        // 해당 id에 JSON 오버라이드가 있는지 확인한다.
        public static bool HasOverride(string id)
        {
            EnsureLoaded();
            return overrides.ContainsKey((id ?? string.Empty).Trim());
        }

        // GUI에서 편집한 정의를 오버라이드 JSON으로 저장하고 다시 읽는다.
        public static void SaveOverride(ChipSfxDefinition definition)
        {
            string id = definition != null ? (definition.id ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            Directory.CreateDirectory(SfxDirectoryPath);
            File.WriteAllText(Path.Combine(SfxDirectoryPath, $"{id}.json"), JsonUtility.ToJson(definition, true), Encoding.UTF8);
            Reload();
        }

        // 오버라이드 JSON을 지워 내장 기본값으로 되돌린다.
        public static void DeleteOverride(string id)
        {
            id = (id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            string path = Path.Combine(SfxDirectoryPath, $"{id}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Reload();
        }

        // 내장 정의를 모두 JSON으로 내보낸다. 반환값은 저장한 파일 수.
        public static int ExportBuiltIns()
        {
            string directory = SfxDirectoryPath;
            Directory.CreateDirectory(directory);

            int count = 0;
            foreach (string id in builtInIds)
            {
                if (!builtIns.TryGetValue(id, out ChipSfxDefinition definition) || definition == null)
                {
                    continue;
                }

                string path = Path.Combine(directory, $"{id}.json");
                File.WriteAllText(path, JsonUtility.ToJson(definition, true), Encoding.UTF8);
                count++;
            }

            return count;
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            lock (loadLock)
            {
                if (!loaded)
                {
                    LoadOverrides();
                }
            }
        }

        private static void LoadOverrides()
        {
            var next = new Dictionary<string, ChipSfxDefinition>(StringComparer.Ordinal);
            string directory = SfxDirectoryPath;
            if (Directory.Exists(directory))
            {
                foreach (string path in Directory.GetFiles(directory, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(path, Encoding.UTF8);
                        var definition = JsonUtility.FromJson<ChipSfxDefinition>(json);
                        string id = definition != null ? (definition.id ?? string.Empty).Trim() : string.Empty;
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            Debug.LogWarning($"Chip sfx json has no id: {path}");
                            continue;
                        }

                        next[id] = definition;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to load chip sfx json: {path}\n{ex}");
                    }
                }
            }

            overrides = next;
            loaded = true;
        }

        private static int KeypadClipIndex(char key)
        {
            return key switch
            {
                '1' => 0,
                '2' => 1,
                '3' => 2,
                '4' => 3,
                '5' => 4,
                '6' => 5,
                '7' => 6,
                '8' => 7,
                '9' => 8,
                '*' => 9,
                '0' => 10,
                '#' => 11,
                _ => 4,
            };
        }

        // ── 내장 정의(과거 하드코딩 합성값을 세그먼트 데이터로 옮긴 것) ──
        private static Dictionary<string, ChipSfxDefinition> BuildBuiltIns()
        {
            var map = new Dictionary<string, ChipSfxDefinition>(StringComparer.Ordinal);

            Add(map, Def("click", "InteractClick", 0.055,
                Sq(0.000, 0.014, 1320.0, 0.42f),
                Sq(0.021, 0.018, 520.0, 0.34f)));

            // 밤에 멀리 들리는 벽시계 초침: 작은 틱과 낮은 톡 사이에 조용한 여백을 둔다.
            Add(map, Def("clock_tick", "ClockTickQuietNight", 0.92,
                Noise(0.000, 0.008, 0.012f, 2301),
                Sq(0.000, 0.015, 2140.0, 0.070f),
                Bell(0.006, 0.030, 1560.0, 0.020f),
                Noise(0.520, 0.010, 0.016f, 2309),
                Sq(0.520, 0.018, 980.0, 0.060f),
                Bell(0.528, 0.038, 720.0, 0.020f)));

            Add(map, Def("move", "MoveTididididi", 0.359,
                Sq(0.000, 0.055, 1567.98, 0.55f),
                Sq(0.073, 0.055, 1396.91, 0.55f),
                Sq(0.146, 0.055, 1174.66, 0.55f),
                Sq(0.219, 0.055, 987.77, 0.55f),
                Sq(0.292, 0.055, 783.99, 0.55f)));

            Add(map, Def("light_switch", "ElectricLightBeep", 0.095,
                Elec(0.000, 0.045, 2240.0, 0.34f),
                Noise(0.006, 0.055, 0.055f, 43),
                Elec(0.052, 0.020, 3100.0, 0.11f)));

            Add(map, Def("whistle", "WhistleBeep", 0.12,
                Sweep(0.000, 0.055, 1760.0, 2637.02, 0.34f),
                Noise(0.047, 0.040, 0.035f, 43),
                Sweep(0.060, 0.030, 2637.02, 2093.0, 0.12f)));

            Add(map, Def("dialogue_type", "DialogueTypeTok", 0.032,
                Sq(0.000, 0.018, 1046.50, 0.22f),
                Bell(0.002, 0.026, 783.99, 0.18f)));

            Add(map, Def("yeon_dialogue_type", "YeonDialogueTypePyu", 0.034,
                Sweep(0.000, 0.034, 1174.66, 1567.98, 0.42f)));

            Add(map, Def("typewriter_type", "TypewriterKeyClack", 0.045,
                Noise(0.000, 0.018, 0.20f, 911),
                Sq(0.000, 0.012, 880.0, 0.12f),
                Noise(0.018, 0.027, 0.06f, 373)));

            // 리듬 분리수거 입력음: 짧은 종이/플라스틱 접촉감의 "착" 클릭.
            Add(map, Def("recycle_chak", "RecycleChak", 0.072,
                Noise(0.000, 0.014, 0.16f, 2901),
                FNoise(0.006, 0.040, 0.10f, 0.070f, 2907),
                Sq(0.004, 0.018, 740.0, 0.10f),
                Sq(0.018, 0.026, 370.0, 0.08f),
                Noise(0.036, 0.018, 0.035f, 2917)));

            // 리듬 분리수거 정타음: 성공 판정이 또렷하게 느껴지는 짧은 상승 벨.
            Add(map, Def("rythm_recycle_hit", "RythmRecycleHit", 0.115,
                Noise(0.000, 0.010, 0.055f, 3011),
                SqSweep(0.000, 0.060, 740.0, 1174.66, 0.13f),
                Bell(0.018, 0.090, 1567.98, 0.12f),
                Bell(0.046, 0.060, 2093.00, 0.075f)));

            // 락픽을 들어 올릴 때 나는 가볍고 날렵한 금속 마찰음.
            Add(map, Def("lockpick_pick_up", "LockPickPickUp", 0.13,
                FNoise(0.000, 0.055, 0.055f, 0.18f, 4011),
                SqSweep(0.006, 0.085, 520.0, 1174.66, 0.17f),
                Bell(0.052, 0.065, 1760.00, 0.055f)));

            // 락픽을 내려놓을 때의 낮고 짧은 금속 접촉음.
            Add(map, Def("lockpick_pick_down", "LockPickPickDown", 0.12,
                Noise(0.000, 0.018, 0.095f, 4021),
                SqSweep(0.000, 0.075, 740.0, 293.66, 0.16f),
                Bell(0.018, 0.075, 440.00, 0.045f)));

            // 핀이 기준선에 맞아 고정되는 또렷한 딸깍음.
            Add(map, Def("lockpick_pin_set", "LockPickPinSet", 0.16,
                Noise(0.000, 0.012, 0.10f, 4031),
                Sq(0.000, 0.025, 1318.51, 0.19f),
                Bell(0.018, 0.125, 2093.00, 0.12f),
                Bell(0.046, 0.090, 2637.02, 0.055f)));

            // 자물쇠 해제 성공: 기계식 걸쇠음 뒤 D단조를 밝게 마무리하는 짧은 팡파레.
            Add(map, Def("lockpick_success", "LockPickSuccess", 0.82,
                Noise(0.000, 0.035, 0.16f, 4041),
                SqSweep(0.000, 0.090, 180.0, 95.0, 0.22f),
                Sq(0.100, 0.095, 587.33, 0.16f),
                Bell(0.100, 0.240, 1174.66, 0.11f),
                Sq(0.235, 0.095, 698.46, 0.17f),
                Bell(0.235, 0.280, 1396.91, 0.12f),
                Sq(0.370, 0.180, 880.00, 0.18f),
                Bell(0.370, 0.390, 1760.00, 0.14f)));

            Add(map, Def("portrait_bounce", "PortraitBounceTtong", 0.105,
                SqSweep(0.000, 0.105, 880.0, 1567.98, 0.5f)));

            Add(map, Def("question_bell", "QuestionTtiriring", 0.62,
                Bell(0.000, 0.28, 1318.51, 0.32f),
                Bell(0.105, 0.30, 1661.22, 0.30f),
                Bell(0.215, 0.38, 2093.00, 0.34f)));

            // 불길한 의문 강조음: 다섯 음의 깨끗한 하강 벨과 약한 저역 여운으로 불안을 남긴다.
            Add(map, Def("question_ominous", "QuestionOminous", 1.25,
                Bell(0.000, 0.32, 659.25, 0.28f),
                Bell(0.150, 0.34, 523.25, 0.27f),
                Bell(0.300, 0.38, 415.30, 0.27f),
                Bell(0.460, 0.44, 349.23, 0.28f),
                Bell(0.630, 0.56, 277.18, 0.30f),
                Sweep(0.650, 0.46, 123.47, 61.74, 0.06f)));

            // 현관 초인종: 길게 울리는 딩-동 두 음으로, 인트로의 정적을 끊는 용도.
            Add(map, Def("doorbell_long", "DoorbellDingDongLong", 1.78,
                Bell(0.000, 0.74, 1567.98, 0.34f),
                Bell(0.000, 0.82, 3135.96, 0.08f),
                Bell(0.820, 0.78, 1174.66, 0.32f),
                Bell(0.820, 0.86, 2349.32, 0.07f)));

            // 아이템 획득 미니 팡파레: 짧게 상승하되 대사를 가리지 않도록 잔향을 줄인다.
            Add(map, Def("item_acquire", "ItemAcquireMiniFanfare", 0.34,
                Sq(0.000, 0.055, 1046.50, 0.16f),
                Sq(0.060, 0.055, 1318.51, 0.17f),
                Sq(0.120, 0.060, 1567.98, 0.18f),
                Sq(0.185, 0.100, 2093.00, 0.16f),
                Bell(0.190, 0.120, 2093.00, 0.045f)));

            // 도전과제 달성 알림: 아이템 획득보다 조금 더 반짝이는 짧은 상승 팡파레.
            Add(map, Def("achievement_unlock", "AchievementUnlockFanfare", 0.42,
                Sq(0.000, 0.045, 1174.66, 0.14f),
                Sq(0.052, 0.045, 1567.98, 0.15f),
                Sq(0.104, 0.052, 1975.53, 0.16f),
                Bell(0.150, 0.180, 2637.02, 0.10f),
                Bell(0.220, 0.150, 3135.96, 0.07f)));

            // 아이템 사용 확인음: 짧은 조작 클릭 뒤 위로 닫히는 확인 톤.
            Add(map, Def("item_use", "ItemUseConfirm", 0.16,
                Noise(0.000, 0.018, 0.08f, 641),
                Sq(0.000, 0.030, 740.0, 0.20f),
                Sq(0.044, 0.046, 1174.66, 0.22f),
                Bell(0.076, 0.070, 1567.98, 0.08f)));

            // 낮고 둔탁한 피격음: 저역 하강 스윕 + 저음 버즈 + 짧은 임팩트 노이즈.
            Add(map, Def("hit", "PlayerHitLow", 0.10,
                Noise(0.000, 0.030, 0.06f, 137),
                Sweep(0.000, 0.080, 520.0, 120.0, 0.34f),
                Sq(0.012, 0.050, 196.00, 0.12f)));

            // 억눌린 비명: 처음 치솟은 음이 길게 이어지다가 무너지도록 배음과 숨 노이즈를 겹친다.
            Add(map, Def("scream", "MuffledScream", 1.55,
                Sweep(0.000, 0.130, 680.0, 1040.0, 0.22f),
                SqSweep(0.085, 1.380, 1040.0, 420.0, 0.16f),
                Sweep(0.020, 1.400, 1760.0, 880.0, 0.09f),
                Sweep(0.040, 1.300, 2860.0, 1500.0, 0.05f),
                FNoise(0.000, 1.480, 0.05f, 0.065f, 2311)));

            Add(map, BuildHeartDrop());
            Add(map, BuildDeath());

            Add(map, Def("heal", "PlayerHealPpiyop", 0.24,
                Sweep(0.000, 0.145, 720.0, 1650.0, 0.30f),
                Sq(0.138, 0.050, 2093.00, 0.15f),
                Bell(0.155, 0.075, 2637.02, 0.10f)));

            // Space Shooter 자동 발사: 반복되어도 거슬리지 않게 짧은 고역 펄스만 남긴다.
            Add(map, Def("space_shoot", "SpaceShooterPulse", 0.052,
                Sq(0.000, 0.026, 1760.0, 0.22f),
                Sweep(0.006, 0.030, 2400.0, 1600.0, 0.08f)));

            // Space Shooter 적 격파: 작은 노이즈 팝 뒤 점수가 오른 느낌의 짧은 상승음.
            Add(map, Def("space_enemy_hit", "SpaceShooterEnemyPop", 0.135,
                Noise(0.000, 0.026, 0.11f, 2201),
                Sweep(0.010, 0.075, 420.0, 980.0, 0.18f),
                Sq(0.074, 0.036, 1567.98, 0.12f)));

            Add(map, Def("keypad", "DoorLockKeypad5", 0.075,
                Piezo(0.000, 0.075, 2793.83, 0.52f)));

            Add(map, Def("keypad_success", "KeypadSuccess", 0.29,
                Piezo(0.000, 0.040, 2637.02, 0.50f),
                Piezo(0.047, 0.040, 3135.96, 0.52f),
                Piezo(0.094, 0.040, 3520.00, 0.52f),
                Piezo(0.141, 0.040, 3951.07, 0.54f),
                Piezo(0.188, 0.060, 4698.64, 0.56f)));

            Add(map, Def("keypad_fail", "KeypadFail", 0.22,
                Piezo(0.000, 0.075, 1975.53, 0.48f),
                Piezo(0.105, 0.085, 1567.98, 0.46f)));

            // 도어락 연속 오답 경고음: 하강하는 전자음을 여덟 번 반복해 "삐용" 박자를 만든다.
            Add(map, Def("keypad_alarm", "KeypadAlarmPpiyong8", 1.36,
                Sweep(0.000, 0.120, 3520.00, 1760.00, 0.70f),
                Sweep(0.165, 0.120, 3520.00, 1760.00, 0.70f),
                Sweep(0.330, 0.120, 3520.00, 1760.00, 0.70f),
                Sweep(0.495, 0.120, 3520.00, 1760.00, 0.70f),
                Sweep(0.660, 0.120, 3520.00, 1760.00, 0.70f),
                Sweep(0.825, 0.120, 3520.00, 1760.00, 0.70f),
                Sweep(0.990, 0.120, 3520.00, 1760.00, 0.70f),
                Sweep(1.155, 0.150, 3520.00, 1760.00, 0.72f)));

            // 엔딩 암전용 충격음: 짧은 노이즈 임팩트와 저역 하강으로 문이 쾅 닫히는 느낌을 낸다.
            Add(map, Def("bang", "EndingBang", 0.42,
                Noise(0.000, 0.055, 0.42f, 2017),
                Sweep(0.000, 0.220, 180.0, 38.0, 0.42f),
                Sq(0.035, 0.120, 82.41, 0.20f),
                FNoise(0.070, 0.300, 0.16f, 0.180f, 2027)));

            // 냄비 끓기: 부드러운 스팀 바닥 + 불규칙하게 올라오는 저음 보글 방울.
            Add(map, Def("pot_simmer", "PotSimmer", 0.90,
                FNoise(0.000, 0.900, 0.08f, 0.030f, 1301),
                Sweep(0.100, 0.050, 150.0, 210.0, 0.10f),
                Sweep(0.300, 0.045, 120.0, 180.0, 0.09f),
                Sweep(0.520, 0.055, 170.0, 240.0, 0.11f),
                Sweep(0.720, 0.045, 130.0, 190.0, 0.08f)));

            // CRT 켜짐: 저음 쿵 + 고역 플라이백 휘파람 + 잦아드는 정전기.
            Add(map, Def("tv_on", "TvOn", 0.38,
                Noise(0.000, 0.040, 0.22f, 1601),
                Sweep(0.000, 0.180, 60.0, 40.0, 0.28f),
                Sweep(0.040, 0.300, 8200.0, 7800.0, 0.05f),
                FNoise(0.040, 0.280, 0.10f, 0.45f, 1607)));

            // CRT 꺼짐: 고역 휘파람이 아래로 붕괴 + 마지막 정전기 틱(수렴점).
            Add(map, Def("tv_off", "TvOff", 0.32,
                Sweep(0.000, 0.200, 8000.0, 400.0, 0.09f),
                Sweep(0.000, 0.220, 1000.0, 60.0, 0.24f),
                Noise(0.215, 0.030, 0.14f, 1709)));

            // 이불/천 스침: 부드러운 마찰 히스 두 번(서로 겹치게), 톤 성분 없이.
            Add(map, Def("bedding_rustle", "BeddingRustle", 0.60,
                FNoise(0.000, 0.300, 0.16f, 0.120f, 1801),
                FNoise(0.240, 0.320, 0.13f, 0.100f, 1811)));

            // 쥐 찍찍 소리: 아주 짧은 고음 상승 스윕 두 번과 얇은 노이즈 꼬리.
            Add(map, Def("mouse_squeak", "MouseSqueak", 0.18,
                Sweep(0.000, 0.045, 3200.0, 5600.0, 0.22f),
                Noise(0.030, 0.020, 0.035f, 1901),
                Sweep(0.070, 0.050, 3600.0, 6200.0, 0.20f),
                Noise(0.108, 0.030, 0.030f, 1907)));

            return map;
        }

        // 낮게 가라앉는 불길한 사망음: 단조풍 저음 하강 + 저역 doom 스윕/럼블.
        private static ChipSfxDefinition BuildDeath()
        {
            return new ChipSfxDefinition
            {
                id = "death",
                clipName = "PlayerDeathDoom",
                durationSeconds = 1.05,
                segments = new[]
                {
                    Sq(0.000, 0.13, 220.00, 0.28f),   // A3
                    Sq(0.140, 0.13, 174.61, 0.28f),   // F3
                    Sq(0.280, 0.13, 146.83, 0.28f),   // D3
                    Sq(0.420, 0.16, 110.00, 0.30f),   // A2
                    Sweep(0.560, 0.45, 130.0, 45.0, 0.30f),
                    FNoise(0.560, 0.45, 0.06f, 0.05f, 428),
                },
            };
        }

        // 마음이 쿵 내려앉는 감정 충격음: 짧은 임팩트 뒤 저역이 빠르게 가라앉는다.
        private static ChipSfxDefinition BuildHeartDrop()
        {
            return new ChipSfxDefinition
            {
                id = "heart_drop",
                clipName = "HeartDropShock",
                durationSeconds = 0.34,
                segments = new[]
                {
                    Noise(0.000, 0.026, 0.18f, 2207),
                    Sweep(0.000, 0.130, 220.0, 48.0, 0.30f),
                    Sq(0.018, 0.085, 82.41, 0.14f),
                    Sweep(0.045, 0.160, 640.0, 92.0, 0.13f),
                    FNoise(0.080, 0.220, 0.055f, 0.060f, 2213),
                },
            };
        }

        private static void Add(Dictionary<string, ChipSfxDefinition> map, ChipSfxDefinition definition)
        {
            map[definition.id] = definition;
        }

        private static ChipSfxDefinition Def(string id, string clipName, double duration, params ChipSfxSegment[] segments)
        {
            return new ChipSfxDefinition
            {
                id = id,
                clipName = clipName,
                durationSeconds = duration,
                segments = segments,
            };
        }

        private static ChipSfxSegment Sq(double start, double duration, double frequency, float volume)
            => new() { type = "square", start = start, duration = duration, frequency = frequency, volume = volume };

        private static ChipSfxSegment SqSweep(double start, double duration, double startFrequency, double endFrequency, float volume)
            => new() { type = "squareSweep", start = start, duration = duration, startFrequency = startFrequency, endFrequency = endFrequency, volume = volume };

        private static ChipSfxSegment Sweep(double start, double duration, double startFrequency, double endFrequency, float volume)
            => new() { type = "sweep", start = start, duration = duration, startFrequency = startFrequency, endFrequency = endFrequency, volume = volume };

        private static ChipSfxSegment Bell(double start, double duration, double frequency, float volume)
            => new() { type = "bell", start = start, duration = duration, frequency = frequency, volume = volume };

        private static ChipSfxSegment Elec(double start, double duration, double frequency, float volume)
            => new() { type = "electric", start = start, duration = duration, frequency = frequency, volume = volume };

        private static ChipSfxSegment Piezo(double start, double duration, double frequency, float volume)
            => new() { type = "piezo", start = start, duration = duration, frequency = frequency, volume = volume };

        private static ChipSfxSegment Noise(double start, double duration, float volume, long seed)
            => new() { type = "noise", start = start, duration = duration, volume = volume, seed = seed };

        private static ChipSfxSegment FNoise(double start, double duration, float volume, float response, long seed)
            => new() { type = "filteredNoise", start = start, duration = duration, volume = volume, response = response, seed = seed };
    }
}
