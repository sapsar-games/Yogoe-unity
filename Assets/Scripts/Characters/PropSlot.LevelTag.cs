using UnityEngine;
using Yoegoe.Data;
using Yoegoe.Economy;

namespace Yoegoe.Characters
{
    // v1.3 레벨업 딱지 — 지금 공덕으로 한 단계 올릴 수 있는 기물 왼쪽 위 바깥에 금색 ▲ 동그라미.
    // 누르면 그 기물 창 (MapPointerRouter 가 HitLevelTag 로 먼저 본다). 떡절구(공덕)는 기물 창이 없어 제외.
    public partial class PropSlot
    {
        SpriteRenderer levelTag;
        float levelTagCheckTimer;
        static Sprite s_levelTagSprite;

        /// <summary>딱지가 떠 있는지.</summary>
        public bool ShowsLevelTag => levelTag != null && levelTag.gameObject.activeSelf;

        bool WantsLevelTag()
        {
            if (!IsBuilt || data == null || !AcceptsWorkers) return false;
            if (ResourceType == PropResourceType.Merit && !CollectsByTap) return false; // 떡절구 제외, 제단은 포함
            if (!PropEconomy.CanUpgrade(this)) return false;
            var eco = GameEconomy.Instance;
            return eco != null && eco.MeritPile >= PropEconomy.GetUpgradeCost(this);
        }

        void RefreshLevelTag()
        {
            levelTagCheckTimer -= Time.unscaledDeltaTime;
            if (levelTagCheckTimer > 0f) { PlaceLevelTag(); return; }
            levelTagCheckTimer = 0.5f;
            bool want = WantsLevelTag();
            if (!want)
            {
                if (levelTag != null && levelTag.gameObject.activeSelf) levelTag.gameObject.SetActive(false);
                return;
            }
            if (levelTag == null)
            {
                var go = new GameObject(name + "_LevelTag");
                levelTag = go.AddComponent<SpriteRenderer>();
                levelTag.sprite = LevelTagSprite();
                levelTag.sortingOrder = 1150;
                go.transform.localScale = Vector3.one * 0.5f;
            }
            if (!levelTag.gameObject.activeSelf) levelTag.gameObject.SetActive(true);
            PlaceLevelTag();
        }

        void PlaceLevelTag()
        {
            if (levelTag == null || !levelTag.gameObject.activeSelf) return;
            Vector3 p;
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                var b = spriteRenderer.bounds;
                p = new Vector3(b.min.x - 0.05f, b.max.y + 0.02f, transform.position.z);
            }
            else p = transform.position + new Vector3(-0.4f, 0.4f, 0f);
            levelTag.transform.position = p;
        }

        /// <summary>딱지를 눌렀는지 — 보이는 동그라미보다 넓게 잡는다.</summary>
        public bool HitLevelTag(Vector3 world, float pad)
        {
            if (!ShowsLevelTag) return false;
            var b = levelTag.bounds;
            b.Expand(new Vector3(pad * 2f, pad * 2f, 0f));
            return world.x >= b.min.x && world.x <= b.max.x && world.y >= b.min.y && world.y <= b.max.y;
        }

        void DestroyLevelTag()
        {
            if (levelTag == null) return;
            if (Application.isPlaying) Destroy(levelTag.gameObject);
            else DestroyImmediate(levelTag.gameObject);
            levelTag = null;
        }

        /// <summary>금색 동그라미 안에 짙은 ▲ (코드로 그린 임시 그림).</summary>
        static Sprite LevelTagSprite()
        {
            if (s_levelTagSprite != null) return s_levelTagSprite;
            const int N = 16;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color[N * N];
            var gold = new Color(0.95f, 0.75f, 0.25f, 1f);
            var edge = new Color(0.45f, 0.3f, 0.1f, 1f);
            var tri = new Color(0.3f, 0.18f, 0.06f, 1f);
            float c = (N - 1) / 2f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                Color col = new Color(0, 0, 0, 0);
                if (d <= c + 0.3f) col = d >= c - 1f ? edge : gold;
                // ▲: y 4..11, 위로 갈수록 좁게
                if (y >= 4 && y <= 11 && Mathf.Abs(x - c) <= (11 - y) * 0.55f + 0.3f) col = tri;
                px[y * N + x] = col;
            }
            tex.SetPixels(px);
            tex.Apply();
            s_levelTagSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 16f);
            return s_levelTagSprite;
        }
    }
}
