using System;
using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.UI;

namespace Yoegoe.Characters
{
    /// <summary>생산·공덕 더미·자원 보관·수거·황금 재료 발견 (규칙은 PropProduction). (PropSlot 분할 — 본체는 PropSlot.cs)</summary>
    public partial class PropSlot
    {
        /// <summary>이 요괴가 자기 엔딩기물에 앉았는지 (주인 배율 대상).</summary>
        public bool IsOwnerOnEndingProp(CharacterAgent agent) =>
            data != null && data.isEndingProp && agent != null && agent.Data != null && data.owner == agent.Data.id;

        /// <summary>앉은 요괴 기준 분당 공덕 (공덕 기물 아니면 0).</summary>
        public double MeritPerMinuteFor(CharacterAgent agent) =>
            !IsBuilt || data == null || agent == null ? 0
                : PropProduction.MeritPerMinute(ProductionConfig, level, agent.Stats.Intimacy, IsOwnerOnEndingProp(agent));

        /// <summary>세이브 로드용. 더미만 덮어쓴다.</summary>
        public void SetPendingMeritFromSave(BigNumber amount)
        {
            PendingMerit = amount;
        }

        /// <summary>7-1: 머물기 중 생산분을 기물 더미에 적립. HUD 지갑으로는 바로 안 들어간다. 만창에서 멈춘다.</summary>
        public void AddToMeritPile(BigNumber amount)
        {
            if (!IsBuilt || amount.Mantissa == 0) return;
            PendingMerit += amount;
            double cap = MeritCapacity;
            if (!double.IsInfinity(cap) && PendingMerit.ToDouble() > cap)
                PendingMerit = (BigNumber)cap;
        }

        /// <summary>
        /// 앉은 요괴가 dt초 머무는 동안의 생산 (규칙은 <see cref="PropProduction.Produce"/> — 오프라인과 동일).
        /// 반환 = 실제로 일한 초(이만큼만 기력이 닳는다). 만창이면 0.
        /// speed = 생산 속도 배율(황금음식 버프 2) — 같은 시간에 speed배 생산하고, 기력은 실제 시간만큼만 닳는다.
        /// </summary>
        public float ProduceWhileStaying(float dt, float intimacy, bool ownerOnEndingProp, float speed = 1f)
        {
            if (!IsBuilt || data == null || dt <= 0f) return dt;
            if (speed <= 0f) speed = 1f;
            float producedSeconds = PropProduction.Produce(ProductionConfig, level, intimacy, ownerOnEndingProp,
                PendingMerit.ToDouble(), ref storage, dt * speed, () => UnityEngine.Random.value,
                code =>
                {
                    pendingIngredients.Add(code);
                    if (PropCatalog.IsSpecialCode(code) && Occupant != null) goldenFinder = Occupant;
                },
                out double meritAdded, DestinationId);
            float worked = producedSeconds / speed;

            if (meritAdded > 0)
            {
                AddToMeritPile(meritAdded);
                // 가끔 공덕꽃잎이 버드나무로 날아가 붙는다 (연출) — 제단은 버드나무를 거치지 않으므로 없음
                petalTimer += worked;
                if (!CollectsByTap && petalTimer >= nextPetalAt && worked < 60f)
                {
                    petalTimer = 0f;
                    nextPetalAt = UnityEngine.Random.Range(15f, 35f);
                    if (MeritWillow.Instance != null)
                        MeritWillow.Instance.LaunchPetalFrom(TopAnchorWorld(0.05f));
                }
            }
            return worked;
        }

        /// <summary>버드나무 등 기물 밖에서 공덕을 수거했을 때 같은 꽃잎 연출을 띄운다.</summary>
        public static void NotifyMeritCollectedAt(Vector3 worldPos) => MeritCollectedAtWorld?.Invoke(worldPos);

        /// <summary>탭 수거 — 공덕 더미 또는 자원 보관을 지갑으로.</summary>
        public bool TryCollect()
        {
            if (HasPendingResources) return TryCollectResources();
            return TryCollectMerit();
        }

        bool TryCollectResources()
        {
            if (!HasPendingResources || GameEconomy.Instance == null) return false;
            var type = ResourceType;
            int n = PropStorage.TakeAll(ref storage);
            SpecialItemId? golden = null;
            switch (type)
            {
                case PropResourceType.Water: GameEconomy.Instance.AddWater(n); break;
                default:
                    foreach (var code in pendingIngredients)
                    {
                        if (PropCatalog.IsSpecialCode(code))
                        {
                            golden = PropCatalog.SpecialOf(code);
                            GameEconomy.Instance.AddSpecialItem(golden.Value, 1);
                        }
                        else
                            GameEconomy.Instance.AddMaterial((CookingIngredientId)code, 1);
                    }
                    break;
            }
            pendingIngredients.Clear();
            if (golden.HasValue) AnnounceGoldenFind(golden.Value);
            ForceRefreshPileLabel();
            ResourcesCollected?.Invoke(this, type, n);
            return true;
        }

        /// <summary>"내가 황금꿀을 발견했어!" — 캐낸 요괴(없으면 지금 앉은 요괴 → 아무 요괴)가 말한다.</summary>
        void AnnounceGoldenFind(SpecialItemId item)
        {
            var speaker = goldenFinder != null ? goldenFinder : Occupant;
            if (speaker == null && CharacterAgent.All.Count > 0) speaker = CharacterAgent.All[0];
            goldenFinder = null;
            if (speaker == null) return;

            CharacterCatalog.Entry entry = null;
            if (speaker.Data != null) CharacterCatalog.TryGet(speaker.Data.id, out entry);
            string line = CharacterCatalog.PickLine(entry?.goldenFindLines, "내가 {item}을 발견했어!");
            speaker.ShowTempSpeech(line.Replace("{item}", SpecialItemName(item)));
        }

        public static string SpecialItemName(SpecialItemId item) =>
            item == SpecialItemId.GoldenHoney ? "황금꿀" : "황금쌀";

        /// <summary>세이브용 자원 보관 스냅샷.</summary>
        public void CaptureStorage(out int stored, out float cycleProgress, out bool overflowJudged, out int[] ingredients)
        {
            stored = storage.Stored;
            cycleProgress = storage.CycleProgressSeconds;
            overflowJudged = storage.OverflowJudged;
            ingredients = pendingIngredients.ToArray();
        }

        /// <summary>세이브 로드용 자원 보관 복원.</summary>
        public void RestoreStorage(int stored, float cycleProgress, bool overflowJudged, int[] ingredients)
        {
            storage = new PropStorage.State
            {
                Stored = Mathf.Max(0, stored),
                CycleProgressSeconds = Mathf.Max(0f, cycleProgress),
                OverflowJudged = overflowJudged
            };
            pendingIngredients.Clear();
            if (ingredients != null) pendingIngredients.AddRange(ingredients);
        }

        public bool TryCollectMerit()
        {
            if (!HasPendingMerit) return false;
            var collected = TakePendingMerit();
            GameEconomy.Instance.AddMerit(collected);
            MeritCollectedAtWorld?.Invoke(TopAnchorWorld(0.15f));
            return true;
        }

        /// <summary>더미만 비워 반환 (일괄 수거용 — HUD에 바로 넣지 않음).</summary>
        public BigNumber TakePendingMerit()
        {
            var collected = PendingMerit;
            PendingMerit = BigNumber.Zero;
            ForceRefreshPileLabel();
            return collected;
        }

        /// <summary>
        /// 더미 표시 단계 0(빈) ~ 5.
        /// 경계 = 기물 분당 기본생산 × 1·3·10·20·30분 (7-2).
        /// </summary>
        public int GetPileStage()
        {
            if (!HasPendingMerit) return 0;
            double perMin = GetBaseProductionThisLevel();
            if (perMin <= 0) return 1;

            double pile = Math.Abs(PendingMerit.ToDouble());
            int[] minuteMarks = { 1, 3, 10, 20, 30 };
            int stage = 1;
            for (int i = 0; i < minuteMarks.Length; i++)
            {
                if (pile >= perMin * minuteMarks[i]) stage = i + 1;
                else break;
            }
            return stage;
        }

        /// <summary>레벨 기준 분당 공덕(보정 전). 공덕 기물이 아니면 0.</summary>
        public double GetBaseProductionThisLevel() =>
            IsBuilt && data != null ? PropProduction.BaseMeritPerMinute(ProductionConfig, level) : 0;
    }
}
