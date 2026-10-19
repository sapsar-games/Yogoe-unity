using UnityEngine;

namespace Yoegoe.Characters
{
    /// <summary>
    /// 캐릭터 걷기·배회 범위. Main이 Playfield로 SetBounds / SetWalkArea 한다.
    /// IslandOverview는 카메라 패닝만 — 걷기와 무관.
    /// WalkArea(플레이필드 실루엣 콜라이더)가 있으면 사각형 AABB가 아니라 그 안으로 Clamp/RandomPoint.
    /// </summary>
    public static class MapBounds
    {
        public static Vector2 Min = new Vector2(-100f, -100f);
        public static Vector2 Max = new Vector2(100f, 100f);

        /// <summary>플레이필드 실루엣. null이면 Min/Max 사각형만 사용.</summary>
        public static Collider2D WalkArea { get; private set; }

        public static void SetBounds(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
        }

        public static void SetWalkArea(Collider2D area)
        {
            WalkArea = area;
            if (area == null) return;

            Bounds b = area.bounds;
            // RandomPoint 거절 샘플용 AABB (월드). margin은 Main에서 이미 반영된 경우도 있어 그대로 사용.
            Min = new Vector2(b.min.x, b.min.y);
            Max = new Vector2(b.max.x, b.max.y);
        }

        /// <summary>맵 범위 안의 랜덤한 한 점 (z는 호출측 값 유지).</summary>
        public static Vector3 RandomPoint(float z = 0f)
        {
            if (WalkArea != null)
            {
                for (int i = 0; i < 40; i++)
                {
                    float x = Random.Range(Min.x, Max.x);
                    float y = Random.Range(Min.y, Max.y);
                    if (WalkArea.OverlapPoint(new Vector2(x, y)))
                        return new Vector3(x, y, z);
                }

                Vector2 mid = WalkArea.ClosestPoint(new Vector2((Min.x + Max.x) * 0.5f, (Min.y + Max.y) * 0.5f));
                return new Vector3(mid.x, mid.y, z);
            }

            return new Vector3(Random.Range(Min.x, Max.x), Random.Range(Min.y, Max.y), z);
        }

        /// <summary>맵 안인지 (WalkArea 또는 AABB).</summary>
        public static bool Contains(Vector3 world)
        {
            var p = new Vector2(world.x, world.y);
            if (WalkArea != null) return WalkArea.OverlapPoint(p);
            return p.x >= Min.x && p.x <= Max.x && p.y >= Min.y && p.y <= Max.y;
        }

        /// <summary>맵 밖으로 나간 위치를 경계 안으로 밀어넣는다.</summary>
        public static Vector3 Clamp(Vector3 pos)
        {
            if (WalkArea != null)
            {
                var p = new Vector2(pos.x, pos.y);
                if (!WalkArea.OverlapPoint(p))
                {
                    Vector2 c = WalkArea.ClosestPoint(p);
                    pos.x = c.x;
                    pos.y = c.y;
                }
                return pos;
            }

            pos.x = Mathf.Clamp(pos.x, Min.x, Max.x);
            pos.y = Mathf.Clamp(pos.y, Min.y, Max.y);
            return pos;
        }

        /// <summary>
        /// from → to 로 step 만큼 이동하되 맵 밖으로 못 나가게 한다.
        /// 직선이 WalkArea 밖(만·반도 등 비-convex 해안선)으로 막히면, 제자리에 멈추는 대신
        /// 막힌 경계점의 접선 방향으로 미끄러지듯 이동을 시도한다 — 시작·끝점이 둘 다 걸을 수
        /// 있는 영역이어도 그 사이 직선이 해안선을 가로지르는 경우가 흔해서다.
        /// 접선으로도 거의 못 움직이면(코너에 낀 경우) 그제서야 blocked=true.
        /// </summary>
        public static Vector3 MoveClamped(Vector3 from, Vector3 to, float step, out bool blocked)
        {
            if (step <= 0f)
            {
                blocked = false;
                return from;
            }

            Vector3 next = Vector3.MoveTowards(from, to, step);
            Vector3 clamped = Clamp(next);
            float want = (next - from).magnitude;
            float got = (clamped - from).magnitude;
            // 목표 방향으로 거의 못 나갔고, 아직 목표와 멀면 경계에 막힌 것
            float remain = Vector2.Distance(new Vector2(clamped.x, clamped.y), new Vector2(to.x, to.y));
            bool rawBlocked = want > 0.0001f && got < want * 0.25f && remain > 0.08f;

            if (!rawBlocked || WalkArea == null)
            {
                blocked = rawBlocked;
                return clamped;
            }

            Vector2 nextXY = new Vector2(next.x, next.y);
            Vector2 edgePoint = WalkArea.ClosestPoint(nextXY);
            Vector2 outward = nextXY - edgePoint;
            if (outward.sqrMagnitude < 1e-6f)
                outward = new Vector2(from.x, from.y) - edgePoint;
            if (outward.sqrMagnitude < 1e-6f)
            {
                blocked = true;
                return clamped;
            }
            outward.Normalize();
            Vector2 tangent = new Vector2(-outward.y, outward.x);

            Vector3 slidePlus = Clamp(Vector3.MoveTowards(from, from + (Vector3)(tangent * step), step));
            Vector3 slideMinus = Clamp(Vector3.MoveTowards(from, from - (Vector3)(tangent * step), step));
            float gotPlus = (slidePlus - from).magnitude;
            float gotMinus = (slideMinus - from).magnitude;
            float slideGot = Mathf.Max(gotPlus, gotMinus);

            blocked = slideGot < step * 0.25f;
            return blocked ? clamped : (gotPlus >= gotMinus ? slidePlus : slideMinus);
        }
    }
}
