using Escape.Platform;
using UnityEngine;

namespace Escape.Storage
{
    // Steam Cloud를 우선 사용하되 실패하거나 비활성 상태면 로컬 저장소로 전환한다.
    public sealed class PlatformSaveDataStore : ISaveDataStore
    {
        private const string LocalFallbackMarkerPrefix = "escape.save.local-fallback.";
        private const string PendingDeleteMarkerPrefix = "escape.save.pending-delete.";

        private readonly ISaveDataStore local = new PlayerPrefsSaveDataStore();

        // Cloud 기록이 가능하면 로컬에도 복사하고, 실패하면 로컬 데이터를 권위 데이터로 표시한다.
        public bool Write(string key, string value)
        {
            ClearPendingDeleteMarker(key);

            PlatformServiceHost platformHost = PlatformServiceHost.Instance;
            if (platformHost?.IsInitialized == true && platformHost.SaveData(key, value))
            {
                local.Write(key, value);
                ClearLocalFallbackMarker(key);
                return true;
            }

            if (!local.Write(key, value))
            {
                return false;
            }

            SetLocalFallbackMarker(key);
            if (platformHost?.IsInitialized == true)
            {
                Debug.LogWarning($"[Save] 플랫폼 저장 실패로 로컬 저장소를 사용합니다: {key}");
            }

            return true;
        }

        // 로컬 폴백 데이터가 있으면 우선하고, 아니면 Cloud에서 읽어 로컬에도 캐시한다.
        public bool TryRead(string key, out string value)
        {
            if (HasPendingDeleteMarker(key))
            {
                TryDeletePendingCloudData(key);
                value = string.Empty;
                return false;
            }

            if (HasLocalFallbackMarker(key) && local.TryRead(key, out value))
            {
                return true;
            }

            PlatformServiceHost platformHost = PlatformServiceHost.Instance;
            if (platformHost?.IsInitialized == true && platformHost.TryLoadData(key, out value))
            {
                local.Write(key, value);
                ClearLocalFallbackMarker(key);
                return true;
            }

            return local.TryRead(key, out value);
        }

        // 로컬 슬롯을 지우고, 플랫폼 연결이 없으면 Cloud 삭제를 다음 읽기까지 보류한다.
        public bool Delete(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !local.Delete(key))
            {
                return false;
            }

            ClearLocalFallbackMarker(key);
            PlatformServiceHost platformHost = PlatformServiceHost.Instance;
            if (platformHost?.IsInitialized == true && platformHost.DeleteData(key))
            {
                ClearPendingDeleteMarker(key);
                return true;
            }

            SetPendingDeleteMarker(key);
            return true;
        }

        // 로컬 저장이 최신임을 슬롯 키별로 기록한다.
        private static void SetLocalFallbackMarker(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            PlayerPrefs.SetInt(GetLocalFallbackMarkerKey(key), 1);
            PlayerPrefs.Save();
        }

        // Cloud 저장 성공 후에는 해당 슬롯의 로컬 우선 표시를 해제한다.
        private static void ClearLocalFallbackMarker(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string markerKey = GetLocalFallbackMarkerKey(key);
            if (!PlayerPrefs.HasKey(markerKey))
            {
                return;
            }

            PlayerPrefs.DeleteKey(markerKey);
            PlayerPrefs.Save();
        }

        // 실제 세이브 키와 충돌하지 않는 로컬 우선 표시 키를 만든다.
        private static string GetLocalFallbackMarkerKey(string key)
        {
            return $"{LocalFallbackMarkerPrefix}{key}";
        }

        // 해당 슬롯이 Cloud보다 최신인 로컬 폴백 데이터를 갖는지 확인한다.
        private static bool HasLocalFallbackMarker(string key)
        {
            return !string.IsNullOrWhiteSpace(key) &&
                PlayerPrefs.GetInt(GetLocalFallbackMarkerKey(key), 0) == 1;
        }

        // 보류 중인 Cloud 삭제를 플랫폼 연결이 가능해진 시점에 다시 시도한다.
        private static void TryDeletePendingCloudData(string key)
        {
            PlatformServiceHost platformHost = PlatformServiceHost.Instance;
            if (platformHost?.IsInitialized == true && platformHost.DeleteData(key))
            {
                ClearPendingDeleteMarker(key);
            }
        }

        // Cloud 삭제 보류 상태를 슬롯별로 기록한다.
        private static void SetPendingDeleteMarker(string key)
        {
            PlayerPrefs.SetInt(GetPendingDeleteMarkerKey(key), 1);
            PlayerPrefs.Save();
        }

        // Cloud 삭제가 끝났거나 새 저장으로 대체되면 보류 상태를 해제한다.
        private static void ClearPendingDeleteMarker(string key)
        {
            string markerKey = GetPendingDeleteMarkerKey(key);
            if (!PlayerPrefs.HasKey(markerKey))
            {
                return;
            }

            PlayerPrefs.DeleteKey(markerKey);
            PlayerPrefs.Save();
        }

        // 실제 세이브 키와 충돌하지 않는 Cloud 삭제 보류 키를 만든다.
        private static string GetPendingDeleteMarkerKey(string key)
        {
            return $"{PendingDeleteMarkerPrefix}{key}";
        }

        // 해당 슬롯에 처리되지 않은 Cloud 삭제 요청이 있는지 확인한다.
        private static bool HasPendingDeleteMarker(string key)
        {
            return !string.IsNullOrWhiteSpace(key) &&
                PlayerPrefs.GetInt(GetPendingDeleteMarkerKey(key), 0) == 1;
        }
    }
}
