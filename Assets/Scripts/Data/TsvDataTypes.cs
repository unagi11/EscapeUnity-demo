using System;
using UnityEngine.Scripting;

namespace Escape.Data
{
    // item.tsv의 아이템 행 데이터를 보관한다.
    [Serializable]
    [Preserve]
    public sealed class Item
    {
        public string id;
        public string icon_idx;
        public string name;
        public string name_ko;
        public string name_en;
        public string name_ja;
        public string desc;
        public string desc_ko;
        public string desc_en;
        public string desc_ja;
        public string com_item_id_0;
        public string com_item_id_1;
        public string com_item_id_2;
        public string decom_item_id_0;
        public string decom_item_id_1;
        public string start_item;
    }

    // info.tsv의 정보 행 데이터를 보관한다.
    [Serializable]
    [Preserve]
    public sealed class Info
    {
        public string id;
        public string icon_idx;
        public string name;
        public string name_ko;
        public string name_en;
        public string name_ja;
        public string desc;
        public string desc_ko;
        public string desc_en;
        public string desc_ja;
        public string start_info;
    }

    // achievement.tsv의 업적 행 데이터를 보관한다.
    [Serializable]
    [Preserve]
    public sealed class Achievement
    {
        public string id;
        public string icon_achv_idx;
        public string hidden;
        public string name;
        public string name_ko;
        public string name_en;
        public string name_ja;
        public string desc;
        public string desc_ko;
        public string desc_en;
        public string desc_ja;
    }

    // speaker.tsv의 화자와 초상화 설정을 보관한다.
    [Serializable]
    [Preserve]
    public sealed class Speaker
    {
        public string id;
        public string name;
        public string name_ko;
        public string name_en;
        public string name_ja;
        public string path;
        public string color;
        public string scale;
        public string typing_sfx;
    }

    // nameban.tsv의 금지 이름 규칙을 보관한다.
    [Serializable]
    [Preserve]
    public sealed class NameBan
    {
        public string id;
        public string value;
        public string match_type;
        public string enabled;
    }

    // dialogue TSV의 대사와 연출 토큰을 보관한다.
    [Serializable]
    [Preserve]
    public sealed class Dialogue
    {
        public string id;
        public string speaker_id;
        public string face;
        public string type;
        public string bg_path;
        public string flag;
        public string text;
        public string text_ko;
        public string effect;
        public string shader;
        public string bgm;
        public string text_en;
        public string text_ja;
    }

    // ui_text.tsv의 다국어 UI 문구를 보관한다.
    [Serializable]
    [Preserve]
    public sealed class UiText
    {
        public string id;
        public string text;
        public string text_ko;
        public string text_en;
        public string text_ja;
    }

    [Serializable]
    [Preserve]
    // tutorial.tsv의 다국어 문구, 배치 영역, 가이드 이미지를 보관한다.
    public sealed class Tutorial
    {
        public string id;
        public string text;
        public string text_ko;
        public string text_en;
        public string text_ja;
        public string text_rect;
        public string guide_image;
    }
}
