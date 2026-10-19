using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    public partial class PropPanel
    {
        /// <summary>"보낼 요괴" 칸에 올라올 후보 전체 (옥토끼 제외 — 옥토끼는 떡절구 전용, v1.3).</summary>
        static IEnumerable<CharacterAgent> Workers()
        {
            foreach (var a in CharacterAgent.All)
                if (a != null && a.Data != null && a.Data.id != CharacterId.Rabbit) yield return a; // 옥토끼는 떡절구만 (v1.3)
        }

        /// <summary>지금 고른 목적지의 destinations 시트 데이터 (이름·친밀도·드랍률 등).</summary>
        PropCatalog.Destination Dest => PropCatalog.FindDestination(selectedDest);

        /// <summary>못 보내는 이유 (null = 보낼 수 있음). refuse = 친밀도 부족(누르면 거절 대사).</summary>
        string WhyNot(CharacterAgent a, out bool refuse)
        {
            refuse = false;
            var s = a.Stats;
            var busy = a.CurrentProp;
            var d = Dest;

            bool fainted = s.State == ActionState.Fainted;
            bool workingElsewhere = s.State == ActionState.Staying && busy != null && busy != prop;
            bool exhausted = s.Stamina < 1f;
            bool endingLocked = prop.data != null && prop.data.isEndingProp && prop.data.owner != a.Data.id;
            bool intimacyTooLow = d != null && s.Intimacy + 0.001f < d.minIntimacy;

            if (fainted) return "기절했어요\n물로 깨워 주세요";
            if (workingElsewhere) return busy.DisplayName + "에서\n일하는 중";
            if (exhausted) return "지쳐서 쉬는 중\n먹여 주세요";
            if (endingLocked) return "이 기물은\n못 써요";
            if (intimacyTooLow)
            {
                refuse = true;
                return $"친밀도 {Mathf.FloorToInt(s.Intimacy)}/{d.minIntimacy:0}\n누르면 물어봐요";
            }
            return null;
        }

        /// <summary>친밀도가 모자란 요괴 카드를 눌렀을 때 — 거절 말풍선을 2.6초 띄우고 선택은 취소한다.</summary>
        void Refuse(CharacterAgent a)
        {
            refusing = a;
            refuseUntil = Time.unscaledTime + 2.6f;
            selectedAgent = null;
            a.ShowTempSpeech(GameSettingsText.Refuse);
        }

        /// <summary>[보내기] 버튼. 고른 요괴를 고른 목적지의 실제 기물로 걷게 한다 (CharacterAgent.WalkToProp).</summary>
        void Send()
        {
            if (prop == null || selectedAgent == null) return;
            var a = selectedAgent;
            if (WhyNot(a, out _) != null) { Rebuild(); return; }
            // 고른 목적지(selectedDest)와 propId가 같은 실제 기물로 걸어간다 —
            // 드래그로 건드린 기물(prop)이 아니라 고른 목적지가 진짜 자리다.
            var seatProp = ResolveSeatProp(prop, selectedDest);
            if (seatProp == null || seatProp.IsOccupied || seatProp.IsStorageHalted) { Rebuild(); return; }
            if (seatProp.HasDestinations) seatProp.DestinationId = selectedDest;
            if (a.WalkToProp(seatProp))
            {
                a.SayCatalogLine(e => e.GoLinesFor(seatProp.ResourceType));
                GameSaveBridge.RequestSave();
                Close(); // 걸어가는 중이라 도착 전까진 IsOccupied가 안 바뀌어 화면이 그대로 보이므로, 보내면 바로 닫는다
                return;
            }
            selectedAgent = null;
            Rebuild();
        }

        /// <summary>목적지 id(destinations 탭)와 fixedDestinationId가 같은 실제 기물 —
        /// 없으면(목적지 선택 전 등) 드래그로 건드린 기물 그대로. propId가 아니라 fixedDestinationId로
        /// 맞춰야 한다 — propId는 한글 이름(예: 멧돼지 앞산), destId는 영문 id(예: BoarHill)라 서로 다르다.</summary>
        static PropSlot ResolveSeatProp(PropSlot touched, string destId)
        {
            if (string.IsNullOrEmpty(destId)) return touched;
            if (touched != null && touched.data != null && touched.data.fixedDestinationId == destId) return touched;
            if (PropManager.Instance == null) return touched;
            foreach (var p in PropManager.Instance.All)
                if (p != null && p.data != null && p.data.fixedDestinationId == destId) return p;
            return touched;
        }

        /// <summary>[불러들이기] 버튼. 일하던 요괴를 내려오게 하고(EnterPlaying) 점유를 비운다.</summary>
        void Recall()
        {
            var a = prop != null ? prop.Occupant : null;
            if (a == null) return;
            a.EnterPlaying();
            a.SayCatalogLine(e => e.homeLines);
            GameSaveBridge.RequestSave();
            Rebuild();
        }

        /// <summary>[받기] 버튼. 쌓인 보관함/공덕을 수거한다.</summary>
        void Take()
        {
            if (prop != null && prop.HasPendingCollectible && prop.TryCollect()) GameSaveBridge.RequestSave();
            Rebuild();
        }

        /// <summary>[레벨업] 버튼. 비용을 내고 레벨을 올린다 (실패하면 그냥 다시 그리기만).</summary>
        void LevelUp()
        {
            if (prop == null) return;
            if (!PropEconomy.TryUpgrade(prop)) { Rebuild(); return; }
            PropUpgradeFx.SpawnWorld(prop.transform.position, "+1 급", font);
            levelNote = prop.ResourceType == PropResourceType.Merit
                ? $"레벨업! 보관 공덕 {((BigNumber)prop.MeritCapacity).ToDisplayString()}"
                : prop.IsResourceProp && prop.level % 10 == 0
                ? "레벨업! 보관함의 총량이 +1 되었어요"
                : $"{prop.DisplayName} Lv{prop.level}";
            GameSaveBridge.SaveFromWorld();
            Rebuild();
        }
    }
}
