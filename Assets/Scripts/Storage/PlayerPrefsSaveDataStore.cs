using UnityEngine;

namespace Escape.Storage
{
    // Steam을 사용할 수 없을 때 PlayerPrefs에 세이브 문자열을 보관한다.
    public sealed class PlayerPrefsSaveDataStore : ISaveDataStore
    {
        // 지정 키의 값을 PlayerPrefs에 즉시 기록한다.
        public bool Write(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || value == null)
            {
                return false;
            }

            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
            return true;
        }

        // 지정 키의 값을 PlayerPrefs에서 읽는다.
        public bool TryRead(string key, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(key) || !PlayerPrefs.HasKey(key))
            {
                return false;
            }

            value = PlayerPrefs.GetString(key, string.Empty);
            return !string.IsNullOrWhiteSpace(value);
        }

        // 지정 키의 로컬 세이브 문자열을 즉시 삭제한다.
        public bool Delete(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return true;
        }
    }
}
