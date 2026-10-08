using System;
using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.UI;

namespace Yoegoe.Save
{
    /// <summary>
    /// 씬(Agent/Prop/Economy) ↔ GameSaveData 스냅샷.
    /// IO는 GameSaveService, 시간 따라잡기는 OfflineSimulator.
    /// </summary>
    public static class GameSaveBridge
    {
        /// <summary>
        /// 부팅 직후(월드 생성 뒤) 호출.
        /// 세이브 있으면 로드 → 오프라인 시뮬 → 월드 반영.
        /// 없으면 false (기존 StartingState 유지).
        /// </summary>
        /// <param name="bubbleFont">세이브에만 있어 새로 스폰하는 요괴(고라니·구미호)의 말풍선 폰트 — 없으면 한글이 깨진다.</param>
        /// <summary>
        /// 세이브가 있었는데 불러오다 실패했으면 true — 새 게임 상태로 진짜 세이브를 덮어쓰지 않게 저장을 막는다.
        /// </summary>
        public static bool SaveBlockedByLoadFailure { get; private set; }

        public static bool TryLoadSimulateAndApply(Font bubbleFont = null)
        {
            if (!GameSaveService.TryLoad(out var data)) return false;
            try
            {
                LoadAndApply(data, bubbleFont);
                GameSaveService.RecordLoadStatus("ok v" + data.version);
                return true;
            }
            catch (System.Exception e)
            {
                SaveBlockedByLoadFailure = true;
                GameSaveService.RecordLoadStatus("error: " + e.GetType().Name + ": " + e.Message + " @ " + FirstFrame(e.StackTrace));
                Debug.LogError("[GameSaveBridge] 세이브 불러오기 실패 — 덮어쓰지 않도록 저장을 막습니다.\n" + e);
                return false;
            }
        }

        static string FirstFrame(string stack)
        {
            if (string.IsNullOrEmpty(stack)) return "";
            var lines = stack.Split('\n');
            return lines.Length > 0 ? lines[0].Trim() : "";
        }

        static void LoadAndApply(GameSaveData data, Font bubbleFont)
        {
            GameSaveMigration.MigrateToCurrent(data);

            var sim = OfflineSimulator.Simulate(data, TrustedTime.UtcNow);
            if (sim.SimulatedSeconds > 1f)
            {
                Debug.Log($"[GameSaveBridge] 오프라인 따라잡기 {sim.SimulatedSeconds:F0}s " +
                          $"(실제 경과 {sim.ElapsedSeconds:F0}s, 상한 {OfflineSimulator.MaxOfflineSeconds:F0}s)");
            }

            ApplyToWorld(data, bubbleFont);
            // 공덕 더미는 기물에 남겨 두고 버드나무에서 수거한다 (7-4). 예전의 콜드스타트 일괄 스윕은 폐지.
            RefreshAllPropPileLabels();
        }

        /// <summary>기물 더미 표시를 즉시 맞춘다 (일괄 수거·로드 직후).</summary>
        public static void RefreshAllPropPileLabels()
        {
            foreach (var p in UnityEngine.Object.FindObjectsByType<PropSlot>(FindObjectsSortMode.None))
                p?.ForceRefreshPileLabel();
        }

        /// <summary>저장이 필요함만 표시 (수거·요리처럼 자주 일어나는 변경). AppSession이 곧 한 번 몰아서 저장한다.</summary>
        public static void RequestSave() => SaveRequested = true;

        /// <summary><see cref="RequestSave"/> 이후 아직 저장 안 됨.</summary>
        public static bool SaveRequested { get; private set; }

        public static void SaveFromWorld()
        {
            SaveRequested = false;
            // Play 중이 아니거나 Economy 부팅 전이면 OnApplicationQuit 등에서 NRE 남
            if (GameEconomy.Instance == null) return;
            if (SaveBlockedByLoadFailure) return; // 불러오기 실패 — 진짜 세이브를 새 게임으로 덮어쓰지 않는다
            var data = CaptureFromWorld();
            GameSaveService.Save(data);
        }

        public static GameSaveData CaptureFromWorld()
        {
            if (GameEconomy.Instance == null)
                throw new System.InvalidOperationException(
                    "[GameSaveBridge] GameEconomy.Instance 가 없습니다. SaveFromWorld는 Play 중에만 호출하세요.");

            var data = new GameSaveData
            {
                version = GameSaveMigration.CurrentVersion,
                savedAtUtcTicks = TrustedTime.UtcNow.Ticks,
                economy = new EconomySave
                {
                    merit = BigNumberSave.From(GameEconomy.Instance.MeritPile),
                    pendingBatchMerit = BigNumberSave.From(GameEconomy.Instance.PendingBatchMerit),
                    yeopjeon = GameEconomy.Instance.Yeopjeon,
                    hyang = GameEconomy.Instance.Hyang,
                    water = GameEconomy.Instance.Water,
                    yutToken = GameEconomy.Instance.YutToken,
                    yutTokenMax = GameEconomy.Instance.YutTokenMax,
                    yutTokenRegenNextUtcTicks = GameEconomy.Instance.YutTokenRegenNextUtcTicks,
                    propsPurchasedCount = GameEconomy.Instance.PropsPurchasedCount,
                    giftMissStreak = 0,
                    giftFirstGrantDone = false,
                    adRewardTickets = 0
                }
            };
            GiftBundle.CaptureToSave(out data.economy.giftMissStreak, out data.economy.giftFirstGrantDone, out data.economy.adRewardTickets);
            ShopStock.CaptureToSave(out data.economy.shopLeftOfferingId, out data.economy.shopRightOfferingId, out data.economy.shopNextRefreshUtcTicks);
            data.economy.offerings = CaptureOfferings(GameEconomy.Instance);
            data.economy.materials = GameEconomy.Instance.CaptureMaterialCounts();
            data.economy.specialItems = GameEconomy.Instance.CaptureSpecialItemCounts();
            data.economy.charms = GameEconomy.Instance.CaptureCharmCounts();
            Attendance.CaptureToSave(out data.economy.attendanceNextDayIndex, out data.economy.attendanceLastHandledDayKey);
            UnityEngine.Debug.Log($"[DIAG] CaptureFromWorld attendance: next={data.economy.attendanceNextDayIndex} last={data.economy.attendanceLastHandledDayKey} (live Attendance.NextDayIndex={Attendance.NextDayIndex} Attendance.LastHandledDayKey={Attendance.LastHandledDayKey})");
            data.economy.codexDiscovered = Yoegoe.Cooking.CookingCodex.CaptureToSave();
            data.economy.lockedSlotUnlocked = CharacterSummon.LockedSlotUnlocked;

            // Props
            var props = UnityEngine.Object.FindObjectsByType<PropSlot>(FindObjectsSortMode.None);
            data.props = new PropSave[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                string id = p.data != null ? p.data.propId : p.name;
                p.CaptureStorage(out int stored, out float cycleProgress, out bool judged, out int[] ingredients);
                data.props[i] = new PropSave
                {
                    storedResources = stored,
                    cycleProgressSeconds = cycleProgress,
                    overflowJudged = judged,
                    pendingIngredients = ingredients,
                    destinationId = p.DestinationId ?? "",
                    propId = id,
                    level = p.level,
                    isBuilt = p.IsBuilt,
                    pendingMerit = BigNumberSave.From(p.PendingMerit),
                    isEndingProp = p.data != null && p.data.isEndingProp,
                    ownerCharacterId = p.data != null ? p.data.owner.ToString() : ""
                };
            }

            // Agents
            var agents = CharacterAgent.All;
            data.agents = new AgentSave[agents.Count];
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                string cid = a.Data != null ? a.Data.id.ToString() : a.name;
                string propId = FindOccupiedPropId(a);

                data.agents[i] = new AgentSave
                {
                    characterId = cid,
                    intimacy = a.Stats.Intimacy,
                    stamina = a.Stats.Stamina,
                    state = a.Stats.State,
                    stateTimer = a.Stats.StateTimer,
                    posX = a.transform.position.x,
                    posY = a.transform.position.y,
                    occupiedPropId = propId,
                    revealedPreferredOfferingIds = a.Stats.RevealedPreferredOfferingIds.ToArray(),
                    goldenBuffEndsUtcTicks = a.Stats.GoldenBuffEndsUtcTicks
                };
            }

            if (YutScreen.Instance != null)
                data.yutMatch = YutScreen.Instance.CaptureForSave();

            data.pier = SpiritPier.CaptureToSave();
            return data;
        }

        public static void ApplyToWorld(GameSaveData data, Font bubbleFont = null)
        {
            if (data == null) return;

            // Economy — StartingState를 덮어쓴다
            ApplyEconomy(data.economy);
            // 나루터 — 오프라인 동안 온 혼령은 AppSession.Tick 의 SpiritPier.Advance 가 따라잡는다
            SpiritPier.ResetFromSave(data.pier, TrustedTime.UtcNow);

            // Props — 점유 초기화 후 더미·레벨 반영
            var props = UnityEngine.Object.FindObjectsByType<PropSlot>(FindObjectsSortMode.None);
            foreach (var p in props)
                p.ClearOccupantForSaveRestore();

            if (data.props != null)
            {
                foreach (var ps in data.props)
                {
                    if (ps == null || string.IsNullOrEmpty(ps.propId)) continue;
                    foreach (var p in props)
                    {
                        string id = p.data != null ? p.data.propId : p.name;
                        if (id != ps.propId) continue;
                        p.ApplySaveBuiltState(ps.isBuilt, ps.level);
                        p.SetPendingMeritFromSave(ps.pendingMerit.ToBigNumber());
                        p.RestoreStorage(ps.storedResources, ps.cycleProgressSeconds, ps.overflowJudged, ps.pendingIngredients);
                        p.DestinationId = string.IsNullOrEmpty(ps.destinationId) ? null : ps.destinationId;
                        if (string.IsNullOrEmpty(p.DestinationId))
                            p.ApplyFixedDestination();
                        break;
                    }
                }
            }

            // Agents — 세이브에만 있는 고라니 등 먼저 스폰
            EnsureMissingAgentsFromSave(data.agents, bubbleFont);

            // Agents + 기물 점유 복원
            if (data.agents != null)
            {
                foreach (var ags in data.agents)
                {
                    if (ags == null) continue;
                    foreach (var a in CharacterAgent.All)
                    {
                        if (a == null || a.Data == null) continue;
                        string cid = a.Data.id.ToString();
                        if (cid != ags.characterId) continue;

                        PropSlot occupy = null;
                        if (!string.IsNullOrEmpty(ags.occupiedPropId))
                        {
                            foreach (var p in props)
                            {
                                string id = p.data != null ? p.data.propId : p.name;
                                if (id == ags.occupiedPropId) { occupy = p; break; }
                            }
                        }

                        a.ApplySaveSnapshot(
                            ags.intimacy,
                            ags.stamina,
                            ags.state,
                            ags.stateTimer,
                            new Vector3(ags.posX, ags.posY, a.transform.position.z),
                            occupy);
                        a.Stats.SetRevealedPreferences(ags.revealedPreferredOfferingIds);
                        a.Stats.GoldenBuffEndsUtcTicks = ags.goldenBuffEndsUtcTicks;
                        break;
                    }
                }
            }

            // 진행 중이던 윷놀이 매치 — 있으면 조용히 복원(화면은 유저가 윷놀이를 열 때 이어짐).
            if (YutScreen.Instance != null)
                YutScreen.Instance.ApplyFromSave(data.yutMatch);
        }

        /// <summary>콜드스타트 시 세이브에 있는 소환 요괴(고라니·구미호)를 월드에 스폰 (향 소모 없음).</summary>
        private static void EnsureMissingAgentsFromSave(AgentSave[] agents, Font bubbleFont)
        {
            if (agents == null) return;
            foreach (var ags in agents)
            {
                if (ags == null || string.IsNullOrEmpty(ags.characterId)) continue;
                CharacterId id;
                if (ags.characterId == nameof(CharacterId.Gorani)) id = CharacterId.Gorani;
                else if (ags.characterId == nameof(CharacterId.Gumiho)) id = CharacterId.Gumiho;
                else continue;
                if (CharacterSummon.IsPresent(id)) continue;
                CharacterSummon.SpawnForSaveRestore(id, bubbleFont, new Vector3(ags.posX, ags.posY, 0f));
            }
        }

        private static void ApplyEconomy(EconomySave e)
        {
            if (e == null) return;
            GameEconomy.Instance.ApplySaveSnapshot(
                e.merit.ToBigNumber(),
                e.pendingBatchMerit != null ? e.pendingBatchMerit.ToBigNumber() : BigNumber.Zero,
                e.yeopjeon,
                e.hyang,
                e.water,
                e.yutToken,
                e.yutTokenMax,
                e.propsPurchasedCount,
                e.yutTokenRegenNextUtcTicks);
            ApplyOfferings(GameEconomy.Instance, e.offerings);
            GameEconomy.Instance.ReplaceMaterialCounts(e.materials);
            GameEconomy.Instance.ReplaceSpecialItemCounts(e.specialItems);
            GameEconomy.Instance.ReplaceCharmCounts(e.charms);
            GiftBundle.ResetFromSave(e.giftMissStreak, e.giftFirstGrantDone, e.adRewardTickets);
            ShopStock.ResetFromSave(e.shopLeftOfferingId, e.shopRightOfferingId, e.shopNextRefreshUtcTicks);
            Attendance.ResetFromSave(e.attendanceNextDayIndex, e.attendanceLastHandledDayKey);
            UnityEngine.Debug.Log($"[DIAG] ApplyEconomy attendance: from save next={e.attendanceNextDayIndex} last={e.attendanceLastHandledDayKey} → live now Attendance.LastHandledDayKey={Attendance.LastHandledDayKey}");
            CharacterSummon.ResetFromSave(e.lockedSlotUnlocked);
            Yoegoe.Cooking.CookingCodex.ResetFromSave(e.codexDiscovered);
        }

        static OfferingCountSave[] CaptureOfferings(GameEconomy eco)
        {
            var buf = new List<KeyValuePair<string, int>>(8);
            eco.CaptureOfferingCounts(buf);
            if (buf.Count == 0) return Array.Empty<OfferingCountSave>();
            var arr = new OfferingCountSave[buf.Count];
            for (int i = 0; i < buf.Count; i++)
            {
                arr[i] = new OfferingCountSave
                {
                    offeringId = buf[i].Key,
                    count = buf[i].Value
                };
            }
            return arr;
        }

        static void ApplyOfferings(GameEconomy eco, OfferingCountSave[] offerings)
        {
            var buf = new List<KeyValuePair<string, int>>();
            if (offerings != null)
                foreach (var o in offerings)
                    if (o != null && !string.IsNullOrEmpty(o.offeringId) && o.count > 0)
                        buf.Add(new KeyValuePair<string, int>(o.offeringId, o.count));
            eco.ReplaceOfferingCounts(buf);
        }

        private static string FindOccupiedPropId(CharacterAgent agent)
        {
            foreach (var p in UnityEngine.Object.FindObjectsByType<PropSlot>(FindObjectsSortMode.None))
            {
                if (p.Occupant == agent)
                {
                    return p.data != null ? p.data.propId : p.name;
                }
            }
            return "";
        }
    }
}
