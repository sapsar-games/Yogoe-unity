using System;
using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Core;

namespace Yoegoe.Economy
{
    /// <summary>12장 고가구점 재고: 2시간 랜덤 공양 2칸 + 향.</summary>
    public static class ShopStock
    {
        // 시트 game_settings
        public static int OfferingPriceYeopjeon => Yoegoe.Data.GameSettings.ShopOfferingPrice;
        public static int HyangPriceYeopjeon => Yoegoe.Data.GameSettings.ShopHyangPrice;
        /// <summary>진열 3시간마다 랜덤 교체 (3차 기획).</summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(3);

        public enum Side { Left, Right }
        public enum BuyFail { None, NoStock, NotEnoughYeopjeon, NotEnoughMerit }

        /// <summary>진열 리셋 비용 = 떡절구 현재 분당 산출 × 이 분 (12장 '5분치 공덕').</summary>
        public static float RerollMeritMinutes => Yoegoe.Data.GameSettings.ShopRerollMeritMinutes;

        public static string LeftOfferingId { get; private set; }
        public static string RightOfferingId { get; private set; }
        public static long NextRefreshUtcTicks { get; private set; }

        static OfferingData[] catalog;

        public static void SetCatalog(OfferingData[] offerings) => catalog = offerings;

        public static void CaptureToSave(out string leftId, out string rightId, out long nextTicks)
        {
            leftId = LeftOfferingId ?? "";
            rightId = RightOfferingId ?? "";
            nextTicks = NextRefreshUtcTicks;
        }

        public static void ResetFromSave(string leftId, string rightId, long nextTicks)
        {
            LeftOfferingId = leftId;
            RightOfferingId = rightId;
            NextRefreshUtcTicks = nextTicks;
            EnsureFresh(TrustedTime.UtcNow);
        }

        public static void EnsureFresh(DateTime utcNow)
        {
            bool missing = string.IsNullOrEmpty(LeftOfferingId) || string.IsNullOrEmpty(RightOfferingId);
            bool expired = NextRefreshUtcTicks <= 0 || utcNow.Ticks >= NextRefreshUtcTicks;
            if (!missing && !expired) return;
            RerollBoth(utcNow);
        }

        public static void ForceReroll(DateTime utcNow) => RerollBoth(utcNow);

        static void RerollBoth(DateTime utcNow)
        {
            LeftOfferingId = PickRandomOfferingId(exclude: null);
            RightOfferingId = PickRandomOfferingId(exclude: LeftOfferingId);
            // 후보가 1개뿐이면 양쪽 동일 허용
            if (string.IsNullOrEmpty(RightOfferingId))
                RightOfferingId = LeftOfferingId;
            NextRefreshUtcTicks = utcNow.Add(RefreshInterval).Ticks;
        }

        static string PickRandomOfferingId(string exclude)
        {
            var list = BuildCandidates(exclude);
            if (list.Count == 0)
            {
                list = BuildCandidates(null);
                if (list.Count == 0) return "";
            }
            return list[UnityEngine.Random.Range(0, list.Count)].offeringId;
        }

        static List<OfferingData> BuildCandidates(string excludeId)
        {
            var list = new List<OfferingData>();
            // 3차: 진열은 공양물 24종에서 (음식·물 제외)
            var poolSrc = OfferingCatalog.RandomPool;
            if (poolSrc.Count > 0)
            {
                foreach (var o in poolSrc)
                {
                    if (o == null) continue;
                    if (!string.IsNullOrEmpty(excludeId)
                        && string.Equals(o.offeringId, excludeId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    list.Add(o);
                }
                return list;
            }
            var src = catalog;
            if (src == null || src.Length == 0)
            {
                // CharacterCatalog 테이블 폴백은 id만으로는 전체 목록이 없어 catalog 필수
                return list;
            }
            for (int i = 0; i < src.Length; i++)
            {
                var o = src[i];
                if (o == null) continue;
                if (o.kind == OfferingKind.Water) continue;
                if (string.IsNullOrEmpty(o.offeringId)) continue;
                if (!string.IsNullOrEmpty(excludeId)
                    && string.Equals(o.offeringId, excludeId, StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(o);
            }
            return list;
        }

        public static OfferingData GetOffering(Side side)
        {
            string id = side == Side.Left ? LeftOfferingId : RightOfferingId;
            if (string.IsNullOrEmpty(id)) return null;
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Length; i++)
                {
                    var o = catalog[i];
                    if (o == null) continue;
                    if (string.Equals(o.offeringId, id, StringComparison.OrdinalIgnoreCase))
                        return o;
                }
            }
            return OfferingCatalog.Find(id) ?? CharacterCatalog.FindOffering(id);
        }

        public static bool TryBuyOffering(Side side, out BuyFail fail)
        {
            fail = BuyFail.None;
            var o = GetOffering(side);
            if (o == null)
            {
                fail = BuyFail.NoStock;
                return false;
            }
            if (!GameEconomy.Instance.TrySpendYeopjeon(OfferingPriceYeopjeon))
            {
                fail = BuyFail.NotEnoughYeopjeon;
                return false;
            }
            GameEconomy.Instance.AddOffering(o, 1);
            return true;
        }

        public static bool TryBuyHyang(out BuyFail fail)
        {
            fail = BuyFail.None;
            if (!GameEconomy.Instance.TrySpendYeopjeon(HyangPriceYeopjeon))
            {
                fail = BuyFail.NotEnoughYeopjeon;
                return false;
            }
            GameEconomy.Instance.AddHyang(1);
            return true;
        }

        /// <summary>진열 리셋 비용 = 분당 산출 × 5 (소수는 올림).</summary>
        public static BigNumber RerollCost(double meritPerMinute) =>
            meritPerMinute > 0 ? (BigNumber)Math.Ceiling(meritPerMinute * RerollMeritMinutes) : BigNumber.Zero;

        /// <summary>
        /// 떡절구 현재 분당 산출 — 앉은 요괴·친밀도와 무관하게 레벨 기준(보정 전).
        /// 공덕 기물(엔딩기물)이 여럿이면 가장 높은 값(엔딩기물은 떡절구 레벨을 따른다).
        /// </summary>
        public static double MortarMeritPerMinute()
        {
            double best = 0;
            if (PropManager.Instance == null) return best;
            foreach (var p in PropManager.Instance.All)
                if (p != null && p.IsBuilt && p.ResourceType == PropResourceType.Merit)
                    best = Math.Max(best, p.GetBaseProductionThisLevel());
            return best;
        }

        public static BigNumber GetRerollCost() => RerollCost(MortarMeritPerMinute());

        /// <summary>5분치 공덕을 내고 좌우 공양물을 새로 뽑는다(3시간 타이머도 다시 시작).</summary>
        public static bool TryRerollWithMerit(DateTime utcNow, out BuyFail fail)
        {
            fail = BuyFail.None;
            var cost = GetRerollCost();
            if (cost.Mantissa == 0)
            {
                fail = BuyFail.NoStock; // 떡절구가 없으면 리셋 불가
                return false;
            }
            if (!GameEconomy.Instance.TrySpendMerit(cost))
            {
                fail = BuyFail.NotEnoughMerit;
                return false;
            }
            ForceReroll(utcNow);
            return true;
        }
    }
}
