using System;
using System.IO;
using UnityEngine;
using Yoegoe.Core;

namespace Yoegoe.Save
{
    /// <summary>
    /// 세이브 파일 읽기/쓰기만 담당. 오프라인 시뮬·씬 반영은 각각 다른 클래스.
    /// WebGL은 persistentDataPath 제약이 있어 PlayerPrefs(JSON 문자열)로 저장한다.
    /// </summary>
    public static class GameSaveService
    {
        public const string PrefsKey = "Yoegoe.GameSave.v1";
        public const string FileName = "games_save_v1.json";

        public static string FilePath =>
            Path.Combine(Application.persistentDataPath, FileName);
        public static void Save(GameSaveData data)
        {
            if (data == null) return;
            data.savedAtUtcTicks = TrustedTime.UtcNow.Ticks;
            string json = JsonUtility.ToJson(data, prettyPrint: true);

#if UNITY_WEBGL && !UNITY_EDITOR
            PlayerPrefs.SetString(PrefsKey, json);
            PlayerPrefs.Save();
            try { YogoeSyncFilesystem(); } catch { /* ignore */ }
#else
            try
            {
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GameSaveService] 파일 저장 실패, PlayerPrefs로 폴백: " + e.Message);
            }
            PlayerPrefs.SetString(PrefsKey, json);
            PlayerPrefs.Save();
#endif
        }

        /// <summary>마지막 불러오기 결과 — 진단용 (PlayerPrefs "Yoegoe.LoadStatus"). nokey · parsefail · ok · error: …</summary>
        public const string LoadStatusKey = "Yoegoe.LoadStatus";
        /// <summary>불러오기 직전 원본 세이브 백업 (불러오다 실패해도 되살릴 수 있게).</summary>
        public const string BackupKey = "Yoegoe.GameSave.backup";

        public static void RecordLoadStatus(string status)
        {
            try
            {
                PlayerPrefs.SetString(LoadStatusKey, System.DateTime.UtcNow.ToString("u") + " " + status);
                PlayerPrefs.Save();
            }
            catch { /* 진단 실패는 무시 */ }
            Debug.Log("[GameSaveService] 불러오기: " + status);
        }

        public static bool TryLoad(out GameSaveData data)
        {
            data = null;

#if UNITY_WEBGL && !UNITY_EDITOR
            if (!PlayerPrefs.HasKey(PrefsKey)) { RecordLoadStatus("nokey"); return false; }
            string raw = PlayerPrefs.GetString(PrefsKey);
            PlayerPrefs.SetString(BackupKey, raw);
            if (!TryParse(raw, out data)) { RecordLoadStatus("parsefail len=" + (raw?.Length ?? 0)); return false; }
            return true;
#else
            // 파일·Prefs 둘 다 있으면 savedAtUtcTicks가 더 최신인 쪽을 쓴다.
            // (예전엔 파일만 있으면 Prefs를 무시해서, 파일만 낡은 출석 키(0)일 때 재수령되던 구멍)
            GameSaveData fromFile = null;
            GameSaveData fromPrefs = null;

            if (File.Exists(FilePath))
            {
                try
                {
                    TryParse(File.ReadAllText(FilePath), out fromFile);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[GameSaveService] 파일 로드 실패: " + e.Message);
                }
            }
            if (PlayerPrefs.HasKey(PrefsKey))
                TryParse(PlayerPrefs.GetString(PrefsKey), out fromPrefs);

            if (fromFile == null && fromPrefs == null) return false;
            if (fromFile == null) { data = fromPrefs; return true; }
            if (fromPrefs == null) { data = fromFile; return true; }

            data = fromPrefs.savedAtUtcTicks >= fromFile.savedAtUtcTicks ? fromPrefs : fromFile;
            return true;
#endif
        }

        static bool TryParse(string json, out GameSaveData data)
        {
            data = null;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                data = JsonUtility.FromJson<GameSaveData>(json);
                return data != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GameSaveService] JSON 파싱 실패: " + e.Message);
                data = null;
                return false;
            }
        }

        public static void DeleteSave()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
#if !UNITY_WEBGL || UNITY_EDITOR
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch { /* ignore */ }
#endif
            Debug.Log("[GameSaveService] 세이브 삭제됨: " + PrefsKey);
        }

        /// <summary>세이브 + PlayerPrefs 전부 삭제. WebGL IndexedDB 잔여 대비.</summary>
        public static void ClearAllLocalData()
        {
            DeleteSave();
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
#if UNITY_WEBGL && !UNITY_EDITOR
            try { YogoeSyncFilesystem(); } catch { /* ignore */ }
#endif
            Debug.Log("[GameSaveService] 로컬 데이터 전체 삭제");
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void YogoeSyncFilesystem();
#endif
    }
}
