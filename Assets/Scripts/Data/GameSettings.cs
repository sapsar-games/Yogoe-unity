using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yoegoe.Data
{
    /// <summary>
    /// 게임 전역 수치 — 시트 game_settings 탭(Resources/game_settings.json, npm run settings).
    /// 파일이 없거나 key 가 빠지면 아래 기본값(v1.3 기획서).
    /// </summary>
    public static class GameSettings
    {
        [Serializable] class Row { public string key; public float value; }
        [Serializable] class File { public Row[] settings; }

        public const string ResourcePath = "game_settings";

        static readonly Dictionary<string, float> Defaults = new Dictionary<string, float>
        {
            { "foodStamina", 10f },
            { "offeringStamina", 15f },
            { "offeringIntimacy", 1f },
            { "preferredIntimacy", 5f },
            { "foodRequestMargin", 10f },
            { "requestFulfillBonus", 4f },
            { "guestPerfectStaminaMul", 2f },
            { "guestPerfectIntimacyMul", 2f },
            { "guestCoolStaminaMul", 2f },
            { "guestCoolIntimacyMul", 1f },
            { "spiritPiecesFood", 1f },
            { "spiritPiecesOffering", 2f },
        };

        static Dictionary<string, float> values;

        /// <summary>음식 한 그릇 기력.</summary>
        public static int FoodStamina => Int("foodStamina");
        /// <summary>공양물 기력 (선호 공양물도 같음).</summary>
        public static int OfferingStamina => Int("offeringStamina");
        public static float OfferingIntimacy => Get("offeringIntimacy");
        public static float PreferredIntimacy => Get("preferredIntimacy");
        /// <summary>기력이 (최대 − 이 값) 이하면 음식 요구.</summary>
        public static float FoodRequestMargin => Get("foodRequestMargin");
        /// <summary>음식 요구를 채우면 음식 기력에 더하는 값.</summary>
        public static int RequestFulfillBonus => Int("requestFulfillBonus");
        public static float GuestPerfectStaminaMul => Get("guestPerfectStaminaMul");
        public static float GuestPerfectIntimacyMul => Get("guestPerfectIntimacyMul");
        public static float GuestCoolStaminaMul => Get("guestCoolStaminaMul");
        public static float GuestCoolIntimacyMul => Get("guestCoolIntimacyMul");
        public static int SpiritPiecesFood => Int("spiritPiecesFood");
        public static int SpiritPiecesOffering => Int("spiritPiecesOffering");

        public static float Get(string key)
        {
            Ensure();
            if (values.TryGetValue(key, out var v)) return v;
            return Defaults.TryGetValue(key, out var d) ? d : 0f;
        }

        static int Int(string key) => Mathf.RoundToInt(Get(key));

        static void Ensure()
        {
            if (values != null) return;
            LoadFromJson(Resources.Load<TextAsset>(ResourcePath)?.text);
        }

        /// <summary>game_settings.json 내용으로 다시 채운다 (테스트·핫리로드). 모르는 key·음수는 건너뛴다.</summary>
        public static void LoadFromJson(string json)
        {
            values = new Dictionary<string, float>(Defaults);
            var file = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<File>(json);
            if (file?.settings == null) return;
            foreach (var r in file.settings)
                if (r != null && !string.IsNullOrEmpty(r.key) && Defaults.ContainsKey(r.key) && r.value >= 0f)
                    values[r.key] = r.value;
        }
    }
}
