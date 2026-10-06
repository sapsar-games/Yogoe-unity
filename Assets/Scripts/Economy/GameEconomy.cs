using System;
using System.Collections.Generic;
using Yoegoe.Core;
using Yoegoe.Data;
using UnityEngine;

namespace Yoegoe.Economy
{
    /// <summary>
    /// 재화·공양물 인벤토리. 시작값은 StartingStateSettings.asset 에서 적용.
    /// Main이 부팅 시 GameObject 하나에 붙여서 만든다 (씬에 하나만 존재).
    /// </summary>
    public partial class GameEconomy : MonoBehaviour
    {
        public static GameEconomy Instance { get; private set; }

        private void Awake() => BecomeInstance();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 싱글턴 등록. EditMode 테스트는 AddComponent 시 Awake가 안 불리므로
        /// SetUp에서 이 메서드를 직접 호출한다.
        /// </summary>
        public void BecomeInstance()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }
            Instance = this;
        }

        // ---------------- 공덕 (플레이어 지갑 — 수거된 공덕. 기물 더미는 PropSlot.PendingMerit) ----------------
        public BigNumber MeritPile { get; private set; } = BigNumber.Zero;
        public event Action<BigNumber> OnMeritChanged;

        /// <summary>
        /// 일괄 수거 대기분 — 버드나무 만땅 탭 시 여기로 옮겨 '광고 2배 / 그냥 받기' 팝업을 띄운다(7-4).
        /// 팝업을 받지 않고 닫으면 남아서 HUD 일괄 수거 버튼으로 받는다.
        /// 백그라운드 복귀만으로는 채우지 않는다.
        /// </summary>
        public BigNumber PendingBatchMerit { get; private set; } = BigNumber.Zero;
        public event Action OnBatchMeritChanged;
        public bool HasPendingBatchMerit => PendingBatchMerit.Mantissa != 0;

        public void AddMerit(BigNumber amount)
        {
            MeritPile += amount;
            OnMeritChanged?.Invoke(MeritPile);
        }

        public bool TrySpendMerit(BigNumber amount)
        {
            if (amount.Mantissa < 0) return false;
            if (MeritPile < amount) return false;
            MeritPile -= amount;
            OnMeritChanged?.Invoke(MeritPile);
            return true;
        }

        /// <summary>플레이어가 구매로 지은 기물 수 (prebuilt 제외). 다음 구매 n = 이 값 + 1.</summary>
        public int PropsPurchasedCount { get; private set; }
        public event Action OnPropsPurchasedCountChanged;

        public void IncrementPropsPurchasedCount()
        {
            PropsPurchasedCount++;
            OnPropsPurchasedCountChanged?.Invoke();
        }

        public void SetPropsPurchasedCount(int count)
        {
            PropsPurchasedCount = Math.Max(0, count);
            OnPropsPurchasedCountChanged?.Invoke();
        }
        public void AddPendingBatchMerit(BigNumber amount)
        {
            if (amount.Mantissa == 0) return;
            PendingBatchMerit += amount;
            OnBatchMeritChanged?.Invoke();
        }

        /// <summary>일괄 수거 확정 → HUD 공덕으로 이동. multiplier=2 이면 광고/보상권 2배.</summary>
        public bool TryClaimBatchMerit(int multiplier = 1)
        {
            if (!HasPendingBatchMerit) return false;
            if (multiplier < 1) multiplier = 1;
            var claim = PendingBatchMerit * (double)multiplier;
            PendingBatchMerit = BigNumber.Zero;
            OnBatchMeritChanged?.Invoke();
            AddMerit(claim);
            return true;
        }

        // ---------------- 엽전 ----------------
        public int Yeopjeon { get; private set; }
        public event Action<int> OnYeopjeonChanged;
        public void AddYeopjeon(int amount) { Yeopjeon += amount; OnYeopjeonChanged?.Invoke(Yeopjeon); }
        public bool TrySpendYeopjeon(int amount)
        {
            if (amount < 0 || Yeopjeon < amount) return false;
            Yeopjeon -= amount;
            OnYeopjeonChanged?.Invoke(Yeopjeon);
            return true;
        }

        // ---------------- 향 ----------------
        public int Hyang { get; private set; }
        public event Action<int> OnHyangChanged;
        public void AddHyang(int amount) { Hyang += amount; OnHyangChanged?.Invoke(Hyang); }
        public bool TrySpendHyang(int amount)
        {
            if (amount < 0 || Hyang < amount) return false;
            Hyang -= amount;
            OnHyangChanged?.Invoke(Hyang);
            return true;
        }

        // ---------------- 물 (구 정화수) ----------------
        // 공양(기력·기절 회복)과 요리 재료 '물'이 같은 재고. 이름(Water)은 세이브·시트 호환으로 유지.
        public int Water { get; private set; }
        public event Action<int> OnWaterChanged;
        public void AddWater(int amount)
        {
            Water += amount;
            OnWaterChanged?.Invoke(Water);
            OnMaterialsChanged?.Invoke();
        }
        public bool TrySpendWater(int amount)
        {
            if (amount < 0 || Water < amount) return false;
            Water -= amount;
            OnWaterChanged?.Invoke(Water);
            OnMaterialsChanged?.Invoke();
            return true;
        }

        // ---------------- 윷 토큰 ----------------
        /// <summary>기획 2·10장: 30분마다 1개 충전, 최대치에서는 카운트다운 없음(대기 없이 그대로 유지).</summary>
        /// <summary>윷 토큰 1개 충전 시간 — 시트 game_settings.</summary>
        public static TimeSpan YutTokenRegenInterval => TimeSpan.FromMinutes(Yoegoe.Data.GameSettings.YutTokenRegenMinutes);

        public int YutTokenMax { get; private set; } = 5;
        public int YutToken { get; private set; }
        /// <summary>다음 충전 예정 UTC ticks. 0이면 "충전 대기 없음"(가득 찼거나 아직 시작 안 함).</summary>
        public long YutTokenRegenNextUtcTicks { get; private set; }
        public event Action<int> OnYutTokenChanged;

        public void AddYutToken(int amount)
        {
            YutToken = Math.Min(YutTokenMax, YutToken + amount);
            if (YutToken >= YutTokenMax) YutTokenRegenNextUtcTicks = 0;
            OnYutTokenChanged?.Invoke(YutToken);
        }

        /// <summary>윷놀이 보물상자 전용 — 평소 상한(YutTokenMax)을 넘어 hardCap까지 쌓을 수 있다.</summary>
        public void AddYutTokenOverflow(int amount, int hardCap)
        {
            int cap = Math.Max(YutTokenMax, hardCap);
            YutToken = Math.Min(cap, YutToken + amount);
            if (YutToken >= YutTokenMax) YutTokenRegenNextUtcTicks = 0;
            OnYutTokenChanged?.Invoke(YutToken);
        }

        public bool TrySpendYutToken(int amount)
        {
            if (amount < 0 || YutToken < amount) return false;
            bool wasFull = YutToken >= YutTokenMax;
            YutToken -= amount;
            if (wasFull && YutToken < YutTokenMax)
                YutTokenRegenNextUtcTicks = TrustedTime.UtcNow.Add(YutTokenRegenInterval).Ticks;
            OnYutTokenChanged?.Invoke(YutToken);
            return true;
        }

        /// <summary>
        /// 벽시계 기준 30분마다 1개 충전 (기획 2·10장). Main의 Update/포그라운드 복귀 훅에서 호출한다 —
        /// 온라인 중에도, 백그라운드에 있다 돌아왔을 때도 이 한 곳만 거치면 된다.
        /// </summary>
        public void EnsureYutTokenFresh(DateTime utcNow)
        {
            if (YutToken >= YutTokenMax)
            {
                YutTokenRegenNextUtcTicks = 0;
                return;
            }

            if (YutTokenRegenNextUtcTicks <= 0)
            {
                YutTokenRegenNextUtcTicks = utcNow.Add(YutTokenRegenInterval).Ticks;
                return;
            }

            bool changed = false;
            while (YutToken < YutTokenMax && utcNow.Ticks >= YutTokenRegenNextUtcTicks)
            {
                YutToken++;
                changed = true;
                YutTokenRegenNextUtcTicks += YutTokenRegenInterval.Ticks;
            }

            if (YutToken >= YutTokenMax) YutTokenRegenNextUtcTicks = 0;
            if (changed) OnYutTokenChanged?.Invoke(YutToken);
        }

        // ---------------- 공양물 인벤토리 (물 제외) ----------------
        private readonly Dictionary<string, int> OfferingCounts = new Dictionary<string, int>();
        public event Action OnOfferingsChanged;

        public int GetOfferingCount(OfferingData offering)
        {
            if (offering == null) return 0;
            string key = OfferingKey(offering);
            return OfferingCounts.TryGetValue(key, out int n) ? n : 0;
        }

        public void AddOffering(OfferingData offering, int amount)
        {
            if (offering == null || amount == 0) return;
            string key = OfferingKey(offering);
            OfferingCounts.TryGetValue(key, out int cur);
            OfferingCounts[key] = Math.Max(0, cur + amount);
            OnOfferingsChanged?.Invoke();
        }

        public bool TrySpendOffering(OfferingData offering, int amount)
        {
            if (offering == null || amount < 0) return false;
            string key = OfferingKey(offering);
            OfferingCounts.TryGetValue(key, out int cur);
            if (cur < amount) return false;
            OfferingCounts[key] = cur - amount;
            OnOfferingsChanged?.Invoke();
            return true;
        }

        public int GetOfferingCount(string offeringId)
        {
            if (string.IsNullOrEmpty(offeringId)) return 0;
            return OfferingCounts.TryGetValue(offeringId, out int n) ? n : 0;
        }

        /// <summary>세이브용 스냅샷. count&gt;0 만 포함.</summary>
        public void CaptureOfferingCounts(List<KeyValuePair<string, int>> into)
        {
            if (into == null) return;
            into.Clear();
            foreach (var kv in OfferingCounts)
            {
                if (kv.Value > 0) into.Add(kv);
            }
        }

        /// <summary>세이브 복원 — 인벤을 통째로 교체한다.</summary>
        public void ReplaceOfferingCounts(IReadOnlyList<KeyValuePair<string, int>> entries)
        {
            OfferingCounts.Clear();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var kv = entries[i];
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                    OfferingCounts[kv.Key] = kv.Value;
                }
            }
            OnOfferingsChanged?.Invoke();
        }

        private static string OfferingKey(OfferingData offering)
        {
            return !string.IsNullOrEmpty(offering.offeringId) ? offering.offeringId : offering.name;
        }

        // ---------------- 특수 수집품 (황금쌀·황금꿀 — 요리판에서 쌀·꿀 칸으로 쓰여 황금음식이 된다) ----------------
        readonly Dictionary<int, int> SpecialItemCounts = new Dictionary<int, int>();
        public event Action OnSpecialItemsChanged;

        public int GetSpecialItemCount(SpecialItemId id) =>
            SpecialItemCounts.TryGetValue((int)id, out int n) ? n : 0;

        public void AddSpecialItem(SpecialItemId id, int amount)
        {
            if (amount == 0) return;
            SpecialItemCounts.TryGetValue((int)id, out int cur);
            SpecialItemCounts[(int)id] = Math.Max(0, cur + amount);
            OnSpecialItemsChanged?.Invoke();
        }

        /// <summary>세이브용: SpecialItemId 순서 배열.</summary>
        public int[] CaptureSpecialItemCounts()
        {
            var arr = new int[Enum.GetValues(typeof(SpecialItemId)).Length];
            for (int i = 0; i < arr.Length; i++)
                SpecialItemCounts.TryGetValue(i, out arr[i]);
            return arr;
        }

        public void ReplaceSpecialItemCounts(int[] counts)
        {
            SpecialItemCounts.Clear();
            if (counts != null)
                for (int i = 0; i < counts.Length; i++)
                    if (counts[i] > 0) SpecialItemCounts[i] = counts[i];
            OnSpecialItemsChanged?.Invoke();
        }

        /// <summary>StartingStateSettings 기준으로 재화·인벤을 덮어쓴다. Main 부팅 시 1회 호출.</summary>
        public void ApplyStartingState(StartingStateSettings s)
        {
            if (s == null) s = StartingStateSettings.Get();

            MeritPile = s.startingMerit;
            PendingBatchMerit = BigNumber.Zero;
            PropsPurchasedCount = 0;
            Yeopjeon = s.startingYeopjeon;
            Hyang = s.startingHyang;
            Water = s.startingWater;
            YutTokenMax = Mathf.Max(1, s.yutTokenMax);
            YutToken = Mathf.Clamp(s.startingYutToken, 0, YutTokenMax);
            YutTokenRegenNextUtcTicks = YutToken < YutTokenMax
                ? TrustedTime.UtcNow.Add(YutTokenRegenInterval).Ticks
                : 0;

            OfferingCounts.Clear();
            if (s.startingOfferings != null)
            {
                int each = Mathf.Max(0, s.startingOfferingCountEach);
                foreach (var o in s.startingOfferings)
                {
                    if (o == null) continue;
                    if (o.kind == OfferingKind.Water) continue; // 물은 재화 칸
                    AddOffering(o, each);
                }
            }

            SeedStartingMaterials(Yoegoe.Data.GameSettings.StartMaterialEach);
            SpiritPier.Reset(TrustedTime.UtcNow);
            CharmCounts.Clear();
            OnCharmsChanged?.Invoke();

            OnMeritChanged?.Invoke(MeritPile);
            OnBatchMeritChanged?.Invoke();
            OnPropsPurchasedCountChanged?.Invoke();
            OnYeopjeonChanged?.Invoke(Yeopjeon);
            OnHyangChanged?.Invoke(Hyang);
            OnWaterChanged?.Invoke(Water);
            OnYutTokenChanged?.Invoke(YutToken);
            GiftBundle.ResetFromSave(0, false, 0);
            Yoegoe.Cooking.CookingCodex.ResetFromSave(null);
            ShopStock.ResetFromSave("", "", 0);
        }

        /// <summary>세이브 스냅샷으로 재화를 덮어쓴다. 공양물 인벤은 ReplaceOfferingCounts로 별도 복원.</summary>
        public void ApplySaveSnapshot(BigNumber merit, BigNumber pendingBatch,
            int yeopjeon, int hyang, int water, int yutToken, int yutTokenMax,
            int propsPurchasedCount = 0, long yutTokenRegenNextUtcTicks = 0)
        {
            MeritPile = merit;
            PendingBatchMerit = pendingBatch;
            PropsPurchasedCount = Math.Max(0, propsPurchasedCount);
            Yeopjeon = yeopjeon;
            Hyang = hyang;
            Water = water;
            YutTokenMax = Mathf.Max(1, yutTokenMax);
            YutToken = Mathf.Clamp(yutToken, 0, YutTokenMax);
            // 0(미기록)이면 EnsureYutTokenFresh가 다음 틱에 알아서 새 카운트다운을 시작한다.
            YutTokenRegenNextUtcTicks = yutTokenRegenNextUtcTicks;

            OnMeritChanged?.Invoke(MeritPile);
            OnBatchMeritChanged?.Invoke();
            OnPropsPurchasedCountChanged?.Invoke();
            OnYeopjeonChanged?.Invoke(Yeopjeon);
            OnHyangChanged?.Invoke(Hyang);
            OnWaterChanged?.Invoke(Water);
            OnYutTokenChanged?.Invoke(YutToken);
        }
    }
}
