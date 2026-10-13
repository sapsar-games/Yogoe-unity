using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Characters
{
    /// <summary>
    /// collectByTap=false 공덕 기물(떡절구 등) 일괄 수거.
    /// 제단은 기물 탭으로 직접 받고, 여기는 HUD 일괄 수거 버튼에서 호출한다.
    /// </summary>
    public static class MeritBatchCollect
    {
        /// <summary>만땅 수거 — UI가 광고 2배 팝업을 연다(공덕은 이미 일괄 대기분으로 옮겨진 상태).</summary>
        public static event System.Action<Vector3> FullRequested;

        static IEnumerable<PropSlot> BatchProps()
        {
            if (PropManager.Instance == null) yield break;
            foreach (var p in PropManager.Instance.All)
                if (p != null && p.IsBuilt && p.ResourceType == PropResourceType.Merit && !p.CollectsByTap)
                    yield return p;
        }

        public static BigNumber StoredMerit
        {
            get
            {
                BigNumber sum = BigNumber.Zero;
                foreach (var p in BatchProps()) sum += p.PendingMerit;
                return sum;
            }
        }

        /// <summary>만땅 = 일괄 대상 공덕 기물이 모두 보관 한도에 닿음.</summary>
        public static bool IsFull
        {
            get
            {
                bool any = false;
                foreach (var p in BatchProps())
                {
                    any = true;
                    if (!p.IsStorageHalted) return false;
                }
                return any;
            }
        }

        public static bool HasMerit => StoredMerit.Mantissa != 0;

        /// <summary>
        /// 일괄 수거. 만땅이면 대기분으로 옮기고 FullRequested.
        /// 기물에 쌓인 게 없어도 PendingBatchMerit이 있으면 팝업만 다시 연다.
        /// </summary>
        public static void Collect(Vector3 fxOrigin)
        {
            if (GameEconomy.Instance == null) return;
            if (!HasMerit)
            {
                if (GameEconomy.Instance.HasPendingBatchMerit) FullRequested?.Invoke(fxOrigin);
                return;
            }

            BigNumber total = BigNumber.Zero;
            bool full = IsFull;
            foreach (var p in BatchProps())
                if (p.HasPendingMerit) total += p.TakePendingMerit();

            if (full && FullRequested != null)
            {
                GameEconomy.Instance.AddPendingBatchMerit(total);
                FullRequested.Invoke(fxOrigin);
                return;
            }

            GameEconomy.Instance.AddMerit(total);
            PropSlot.NotifyMeritCollectedAt(fxOrigin);
        }
    }
}
