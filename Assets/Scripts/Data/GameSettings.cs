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
            { "cookBaseSeconds", 15f },
            { "cookAdExtendSeconds", 15f },
            { "cookFoodSeconds", 1.5f },
            { "cookOfferingSeconds", 2.1f },
            { "cookSteamSeconds", 0.5f },
            { "staminaDrainMinutes", 10f },
            { "faintHours", 18f },
            { "requestIntervalMinMinutes", 3f },
            { "requestIntervalMaxMinutes", 5f },
            { "requestShowSeconds", 60f },
            { "goldenBuffMinutes", 5f },
            { "goldenSpeedMul", 2f },
            { "shopOfferingPrice", 10f },
            { "shopHyangPrice", 20f },
            { "shopRerollMeritMinutes", 5f },
            { "yutTokenMax", 5f },
            { "yutTokenRegenMinutes", 30f },
            { "yutTokenBuyCost", 10f },
            { "yutTokenBuyAmount", 5f },
            { "chestOfferingWeight", 50f },
            { "chestAdTicketWeight", 30f },
            { "chestHyangWeight", 5f },
            { "chestYeopjeonWeight", 15f },
            { "chestYeopjeonAmount", 3f },
            { "giftChance", 0.2f },
            { "giftPityMisses", 4f },
            { "startMerit", 1000f },
            { "startYeopjeon", 100f },
            { "startHyang", 2f },
            { "startWater", 0f },
            { "startYutToken", 5f },
            { "startMaterialEach", 5f },
            { "startIntimacy", 50f },
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

        // 요리판
        public static float CookBaseSeconds => Get("cookBaseSeconds");
        public static float CookAdExtendSeconds => Get("cookAdExtendSeconds");
        public static float CookFoodSeconds => Get("cookFoodSeconds");
        public static float CookOfferingSeconds => Get("cookOfferingSeconds");
        public static float CookSteamSeconds => Get("cookSteamSeconds");
        // 기력 · 행동
        /// <summary>일하는 동안 초당 기력 소모 (= 1 / (staminaDrainMinutes × 60)).</summary>
        public static float StaminaDrainPerSecond => 1f / Mathf.Max(1f, Get("staminaDrainMinutes") * 60f);
        public static float FaintThresholdSeconds => Get("faintHours") * 3600f;
        public static float RequestIntervalMinSeconds => Get("requestIntervalMinMinutes") * 60f;
        public static float RequestIntervalMaxSeconds => Mathf.Max(RequestIntervalMinSeconds, Get("requestIntervalMaxMinutes") * 60f);
        public static float RequestShowSeconds => Get("requestShowSeconds");
        // 황금 요리
        public static float GoldenBuffSeconds => Get("goldenBuffMinutes") * 60f;
        public static float GoldenSpeedMul => Get("goldenSpeedMul");
        // 상점 · 윷
        public static int ShopOfferingPrice => Int("shopOfferingPrice");
        public static int ShopHyangPrice => Int("shopHyangPrice");
        public static float ShopRerollMeritMinutes => Get("shopRerollMeritMinutes");
        public static int YutTokenMax => Mathf.Max(1, Int("yutTokenMax"));
        public static float YutTokenRegenMinutes => Mathf.Max(1f, Get("yutTokenRegenMinutes"));
        public static int YutTokenBuyCost => Int("yutTokenBuyCost");
        public static int YutTokenBuyAmount => Int("yutTokenBuyAmount");
        public static float ChestOfferingWeight => Get("chestOfferingWeight");
        public static float ChestAdTicketWeight => Get("chestAdTicketWeight");
        public static float ChestHyangWeight => Get("chestHyangWeight");
        public static float ChestYeopjeonWeight => Get("chestYeopjeonWeight");
        public static int ChestYeopjeonAmount => Int("chestYeopjeonAmount");
        public static float GiftChance => Get("giftChance");
        public static int GiftPityMisses => Int("giftPityMisses");
        // 새 게임 시작
        public static int StartMaterialEach => Int("startMaterialEach");

        /// <summary>시작 상태 에셋 사본에 시트 값을 덮어쓴다 (새 게임에만 쓰인다).</summary>
        public static void ApplyStart(StartingStateSettings s)
        {
            if (s == null) return;
            s.startingMerit = Int("startMerit");
            s.startingYeopjeon = Int("startYeopjeon");
            s.startingHyang = Int("startHyang");
            s.startingWater = Int("startWater");
            s.startingYutToken = Int("startYutToken");
            s.yutTokenMax = YutTokenMax;
            s.startingIntimacy = Get("startIntimacy");
        }

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
