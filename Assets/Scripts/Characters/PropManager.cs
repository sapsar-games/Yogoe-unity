using System.Collections.Generic;
using UnityEngine;

namespace Yoegoe.Characters
{
    /// <summary>씬 안의 모든 PropSlot을 등록해두고, 걷기 목적지 후보를 골라주는 매니저.</summary>
    public class PropManager : MonoBehaviour
    {
        public static PropManager Instance { get; private set; }
        private readonly List<PropSlot> allProps = new List<PropSlot>();

        /// <summary>등록된 기물 목록 (HUD 등에서 FindObjectsByType 대신 사용).</summary>
        public List<PropSlot> All => allProps;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // 씬에 미리 배치된 기물은 PropManager보다 먼저 OnEnable될 수 있어 여기서 재등록한다.
            foreach (var slot in FindObjectsByType<PropSlot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Register(slot);
        }

        public void Register(PropSlot slot)
        {
            if (!allProps.Contains(slot)) allProps.Add(slot);
        }

        public void Unregister(PropSlot slot) => allProps.Remove(slot);

        /// <summary>
        /// 6-2 걷기 목적지 후보 선정.
        /// 룰 기준: Docs/06_행동룰.md
        /// 비어있고, 직전 기물이 아니고, 다른 요괴의 엔딩 기물이 아니고, 만창이 아닌(가서 일할 수 있는) 것 중 랜덤.
        /// 후보가 없으면 null (호출측에서 30초 방황 후 재추첨).
        /// </summary>
        public PropSlot GetRandomAvailableProp(CharacterAgent requester, PropSlot exclude,
            System.Func<PropSlot, bool> filter = null)
        {
            var candidates = new List<PropSlot>();
            for (int i = 0; i < allProps.Count; i++)
            {
                var p = allProps[i];
                if (p == null) continue;
                if (!p.IsBuilt) continue;
                if (p.IsOccupied) continue;
                if (p.IsReserved) continue;
                if (p == exclude) continue;
                if (p.IsStorageHalted) continue; // 가도 일을 못 함
                if (!p.AcceptsWorkers) continue; // 화덕 등 — 요리 전용, 일할 곳 아님
                if (!p.CanBeUsedBy(requester)) continue;
                if (filter != null && !filter(p)) continue;
                candidates.Add(p);
            }

            if (candidates.Count == 0) return null;
            return candidates[Random.Range(0, candidates.Count)];
        }

        /// <summary>드래그 드롭용: worldPos가 기물 스프라이트 bounds 안(또는 maxRadius 이내)인 가장 가까운 기물.
        /// maxRadius=0이면 PNG 크기(스프라이트 AABB) 안에 있을 때만 매칭.
        /// allowOccupied=true면 점유된 기물도 반환(옆에 내려놓기용).
        /// allowEndingRefuse=true면 타 요괴 엔딩 기물도 반환(거절 연출용).</summary>
        public PropSlot FindNearestDropTarget(
            CharacterAgent requester,
            Vector3 worldPos,
            float maxRadius,
            bool allowOccupied = false,
            bool allowEndingRefuse = false)
        {
            PropSlot best = null;
            float bestDist = float.MaxValue;
            float limit = Mathf.Max(0f, maxRadius);
            foreach (var p in allProps)
            {
                if (p == null) continue;
                if (!p.IsBuilt) continue;

                bool endingRefuse = p.IsForbiddenEndingFor(requester);
                if (endingRefuse)
                {
                    if (!allowEndingRefuse) continue;
                }
                else
                {
                    if (!p.CanBeUsedBy(requester)) continue;
                    if (!allowOccupied && p.IsOccupied) continue;
                }

                float d = DistanceToPropSurface(p, worldPos);
                if (d <= limit && d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>탭/수거용: worldPos에 가장 가까운 기물.</summary>
        public PropSlot FindNearestProp(Vector3 worldPos, float maxRadius)
        {
            PropSlot best = null;
            float bestDist = maxRadius;
            foreach (var p in allProps)
            {
                if (p == null) continue;
                float d = DistanceToPropSurface(p, worldPos);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>미건립(자물쇠)만 — 구매 탭용.</summary>
        public PropSlot FindNearestUnbuiltProp(Vector3 worldPos, float maxRadius)
        {
            PropSlot best = null;
            float bestDist = Mathf.Max(0f, maxRadius);
            foreach (var p in allProps)
            {
                if (p == null || p.IsBuilt) continue;
                float d = DistanceToPropSurface(p, worldPos);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }
        /// <summary>bounds 표면까지 거리(안이면 0). 큰 기물 가장자리 드롭도 잡힘.</summary>
        private static float DistanceToPropSurface(PropSlot prop, Vector3 worldPos)
        {
            Bounds b = GetPropBounds(prop);
            Vector3 p = worldPos;
            p.z = b.center.z;
            if (b.Contains(p)) return 0f;
            Vector3 closest = b.ClosestPoint(p);
            return Vector2.Distance(closest, p);
        }

        /// <summary>
        /// 탭/드롭 히트용 AABB. TextMesh(자물쇠 글자) MeshRenderer는 bounds가 거대해서 제외한다.
        /// </summary>
        private static Bounds GetPropBounds(PropSlot prop)
        {
            var srs = prop.GetComponentsInChildren<SpriteRenderer>(true);
            Bounds? union = null;
            for (int i = 0; i < srs.Length; i++)
            {
                var sr = srs[i];
                if (sr == null || !sr.enabled || sr.sprite == null) continue;
                // 더미/이펙트용 작은 오버레이는 히트에서 제외하고 본 기물만
                if (sr.name.IndexOf("Pile", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (union == null) union = sr.bounds;
                else
                {
                    var u = union.Value;
                    u.Encapsulate(sr.bounds);
                    union = u;
                }
            }
            if (union != null) return union.Value;
            return new Bounds(prop.transform.position, Vector3.one * 0.6f);
        }
    }
}
