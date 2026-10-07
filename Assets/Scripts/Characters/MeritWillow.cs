using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Characters
{
    /// <summary>
    /// 공덕 버드나무 (Docs/00 §6-4 · 기획 7-4).
    /// 공덕 기물(떡절구 등)이 만든 공덕은 기물 안에 저장되지만(세이브·오프라인 정산 그대로)
    /// 화면에는 버드나무에 모인 것으로 보인다. 일하는 동안 가끔 꽃잎이 기물에서 버드나무로 날아와 붙는다.
    /// 탭하면 수거 — 만땅(공덕 기물 보관 합계)이면 분홍으로 변하고, 탭 시 "광고 2배 / 그냥 받기".
    /// 아트 전 임시 그림은 ProceduralSprite로 그린다.
    /// </summary>
    public class MeritWillow : MonoBehaviour
    {
        public static MeritWillow Instance { get; private set; }

        /// <summary>만땅 탭 — UI가 받아서 광고 2배 팝업을 연다(공덕은 이미 일괄 대기분으로 옮겨진 상태).</summary>
        public static event System.Action<Vector3> FullTapped;

        const float WorldHeight = 1.3f;
        const int TexSize = 128;
        const int MaxPetals = 14;
        const float PetalFlySeconds = 0.9f;

        SpriteRenderer body;
        TextMesh label;
        readonly List<SpriteRenderer> attachedPetals = new List<SpriteRenderer>();
        readonly List<(Transform tr, Vector3 from, Vector3 to, float t)> flying = new List<(Transform, Vector3, Vector3, float)>();
        string lastLabel;
        bool lastFull;

        static Sprite treeSprite;
        static Sprite petalSprite;

        public static MeritWillow Create(Vector3 position, int sortingOrder)
        {
            var go = new GameObject("MeritWillow");
            go.transform.position = position;
            var w = go.AddComponent<MeritWillow>();
            if (w.body != null)
                w.body.sortingOrder = sortingOrder;
            return w;
        }

        void Awake()
        {
            Instance = this;
            EnsureBody();
        }

        void EnsureBody()
        {
            if (body == null)
                body = GetComponent<SpriteRenderer>();
            if (body == null)
                body = gameObject.AddComponent<SpriteRenderer>();
            if (body.sprite == null)
                body.sprite = TreeSprite();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (label != null) Destroy(label.gameObject);
        }

        // ---------------- 공덕 합계 (공덕 기물 저장분) ----------------

        static IEnumerable<PropSlot> MeritProps()
        {
            if (PropManager.Instance == null) yield break;
            foreach (var p in PropManager.Instance.All)
                // 제단(collectByTap)은 버드나무를 거치지 않고 제단을 눌러 받는다 (v1.3)
                if (p != null && p.IsBuilt && p.ResourceType == PropResourceType.Merit && !p.CollectsByTap)
                    yield return p;
        }

        public BigNumber StoredMerit
        {
            get
            {
                BigNumber sum = BigNumber.Zero;
                foreach (var p in MeritProps()) sum += p.PendingMerit;
                return sum;
            }
        }

        /// <summary>만땅 = 모든 공덕 기물이 보관 한도에 닿음.</summary>
        public bool IsFull
        {
            get
            {
                bool any = false;
                foreach (var p in MeritProps())
                {
                    any = true;
                    if (!p.IsStorageHalted) return false;
                }
                return any;
            }
        }

        public bool HasMerit => StoredMerit.Mantissa != 0;

        // ---------------- 탭 ----------------

        public bool HitTest(Vector3 world, float padding)
        {
            if (body == null || body.sprite == null) return false;
            var b = body.bounds;
            b.Expand(padding);
            world.z = b.center.z;
            return b.Contains(world);
        }

        /// <summary>탭: 만땅이면 광고 2배 팝업으로, 아니면 바로 지갑으로.</summary>
        public void OnTapped()
        {
            if (!HasMerit || GameEconomy.Instance == null) return;
            BigNumber total = BigNumber.Zero;
            bool full = IsFull;
            foreach (var p in MeritProps())
                if (p.HasPendingMerit) total += p.TakePendingMerit();

            ClearPetals();
            if (full && FullTapped != null)
            {
                GameEconomy.Instance.AddPendingBatchMerit(total);
                FullTapped.Invoke(CanopyCenter());
                return;
            }
            GameEconomy.Instance.AddMerit(total);
            PropSlot.NotifyMeritCollectedAt(CanopyCenter());
        }

        // ---------------- 꽃잎 연출 ----------------

        /// <summary>공덕 기물이 일하는 동안 가끔 호출 — 꽃잎이 기물에서 버드나무로 날아와 붙는다.</summary>
        public void LaunchPetalFrom(Vector3 worldFrom)
        {
            if (flying.Count > 6) return;
            var go = new GameObject("MeritPetal");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PetalSprite();
            sr.sortingOrder = (body != null ? body.sortingOrder : 0) + 2;
            go.transform.position = worldFrom;
            flying.Add((go.transform, worldFrom, RandomCanopyPoint(), 0f));
        }

        void Update()
        {
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var f = flying[i];
                if (f.tr == null) { flying.RemoveAt(i); continue; }
                f.t += Time.deltaTime / PetalFlySeconds;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(f.t));
                var p = Vector3.Lerp(f.from, f.to, u);
                p.y += Mathf.Sin(u * Mathf.PI) * 0.5f; // 포물선
                f.tr.position = p;
                f.tr.Rotate(0f, 0f, 360f * Time.deltaTime);
                if (f.t >= 1f)
                {
                    Destroy(f.tr.gameObject);
                    flying.RemoveAt(i);
                    continue;
                }
                flying[i] = f;
            }

            RefreshVisual();
        }

        void RefreshVisual()
        {
            var stored = StoredMerit;
            bool full = IsFull;
            if (body != null && full != lastFull)
            {
                lastFull = full;
                body.color = full ? new Color(1f, 0.72f, 0.82f, 1f) : Color.white; // 만땅 = 분홍
            }

            // 붙은 꽃잎 수 = 채움 비율
            double cap = 0;
            foreach (var p in MeritProps())
            {
                double c = p.MeritCapacity;
                if (!double.IsInfinity(c)) cap += c;
            }
            float ratio = cap > 0 ? Mathf.Clamp01((float)(stored.ToDouble() / cap)) : (stored.Mantissa != 0 ? 1f : 0f);
            SetAttachedPetals(Mathf.RoundToInt(ratio * MaxPetals));

            EnsureLabel();
            string text = stored.Mantissa == 0 ? "" : (full ? "만땅! " : "") + stored.ToDisplayString();
            if (text != lastLabel)
            {
                lastLabel = text;
                label.text = text;
            }
            label.transform.position = new Vector3(transform.position.x,
                body != null ? body.bounds.max.y + 0.05f : transform.position.y + WorldHeight, transform.position.z);
        }

        void SetAttachedPetals(int count)
        {
            while (attachedPetals.Count < count)
            {
                var go = new GameObject("WillowPetal");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = PetalSprite();
                sr.sortingOrder = (body != null ? body.sortingOrder : 0) + 1;
                go.transform.position = RandomCanopyPoint();
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                attachedPetals.Add(sr);
            }
            while (attachedPetals.Count > count)
            {
                var last = attachedPetals[attachedPetals.Count - 1];
                attachedPetals.RemoveAt(attachedPetals.Count - 1);
                if (last != null) Destroy(last.gameObject);
            }
        }

        void ClearPetals() => SetAttachedPetals(0);

        Vector3 CanopyCenter() =>
            transform.position + new Vector3(0f, WorldHeight * 0.22f, 0f);

        Vector3 RandomCanopyPoint()
        {
            var c = CanopyCenter();
            var r = Random.insideUnitCircle * new Vector2(0.42f, 0.3f);
            return new Vector3(c.x + r.x, c.y + r.y, transform.position.z);
        }

        void EnsureLabel()
        {
            if (label != null) return;
            var go = new GameObject("WillowMeritLabel");
            label = go.AddComponent<TextMesh>();
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.08f;
            label.fontSize = Yoegoe.UI.UiFonts.Size(48);
            label.color = new Color(1f, 0.92f, 0.55f, 1f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) label.font = font;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 1200;
        }

        // ---------------- 임시 그림 ----------------

        static Sprite TreeSprite() =>
            treeSprite != null ? treeSprite
                : (treeSprite = ProceduralSprite.Build("Willow_Temp", TexSize, TexSize / WorldHeight, SampleTree));

        static Sprite PetalSprite() =>
            petalSprite != null ? petalSprite
                : (petalSprite = ProceduralSprite.Build("MeritPetal_Temp", 32, 32 / 0.12f, SamplePetal));

        static readonly Color Trunk = new Color(0.45f, 0.3f, 0.18f, 1f);
        static readonly Color LeafDark = new Color(0.33f, 0.55f, 0.28f, 1f);
        static readonly Color LeafLight = new Color(0.52f, 0.72f, 0.38f, 1f);

        static float Hash(float i)
        {
            float x = Mathf.Sin(i * 127.1f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }

        /// <summary>버드나무: 둥근 수관 + 늘어진 가지 + 줄기.</summary>
        static Color SampleTree(Vector2 p)
        {
            float dx = (p.x - 64f) / 52f, dy = (p.y - 88f) / 28f;
            if (Mathf.Sqrt(dx * dx + dy * dy) <= 1f)
                return dy > 0.25f || Hash(Mathf.Floor(p.x / 9f) + Mathf.Floor(p.y / 9f) * 7f) > 0.6f ? LeafLight : LeafDark;

            float ax = Mathf.Abs(p.x - 64f);
            if (ax < 50f && p.y < 88f)
            {
                float wx = p.x + Mathf.Sin(p.y * 0.09f) * 2.5f;
                float col = Mathf.Floor(wx / 6f);
                bool inStrand = Mathf.Repeat(wx, 6f) < 2f;
                float top = 88f - 28f * Mathf.Sqrt(Mathf.Max(0f, 1f - (ax / 52f) * (ax / 52f)));
                float length = 30f + Hash(col) * 38f;
                if (inStrand && ax > 8f && p.y > top - length && p.y <= top + 2f)
                    return Hash(col + 3f) > 0.5f ? LeafLight : LeafDark;
            }

            if (ax < 6f - (p.y / 80f) * 2f && p.y < 70f) return Trunk;
            return Color.clear;
        }

        static Color SamplePetal(Vector2 p)
        {
            var q = (p - new Vector2(16f, 16f)) / 13f;
            float a = q.x * q.x / 0.35f + q.y * q.y;
            if (a > 1f) return Color.clear;
            return Color.Lerp(new Color(1f, 0.62f, 0.75f, 1f), new Color(1f, 0.88f, 0.92f, 1f), Mathf.Clamp01(q.y + 0.5f));
        }
    }
}
