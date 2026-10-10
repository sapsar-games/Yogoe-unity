using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Yoegoe.UI
{
    /// <summary>
    /// 씬의 모든 글자에 <see cref="UiTextScale"/> 배율을 곱한다.
    /// 글자마다 처음 본 크기를 '원래 크기'로 기억하고, 코드가 크기를 바꾸면 그 값을 새 원래 크기로 삼는다
    /// — 프리팹·코드 어디서 만든 글자든 손대지 않고 따라오게. 새로 생긴 글자는 주기적으로 찾아 적용.
    /// 요괴 말풍선은 크기에 맞춰 배경을 그리므로 여기서 빼고 CharacterAgent 가 직접 배율을 쓴다.
    /// </summary>
    public class UiTextScaler : MonoBehaviour
    {
        public static UiTextScaler Instance { get; private set; }

        const float ScanInterval = 0.3f;
        const int PurgeEveryScans = 30;
        int scans;

        struct UiEntry { public int baseSize, baseMax, baseMin, applied, appliedMax; }
        struct WorldEntry { public float baseSize, applied; }

        readonly Dictionary<Text, UiEntry> ui = new Dictionary<Text, UiEntry>();
        readonly Dictionary<TextMesh, WorldEntry> world = new Dictionary<TextMesh, WorldEntry>();
        float timer;
        float lastUi = -1f, lastWorld = -1f;

        /// <summary>말풍선 등 직접 크기를 관리하는 TextMesh 는 이 이름 끝을 붙인다.</summary>
        public const string ExcludeSuffix = "_Bubble";

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            UiTextScale.Changed += ApplyNow;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UiTextScale.Changed -= ApplyNow;
        }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = ScanInterval;
            ApplyNow();
        }

        public void ApplyNow()
        {
            float su = UiTextScale.Ui, sw = UiTextScale.World;
            bool scaleChanged = !Mathf.Approximately(su, lastUi) || !Mathf.Approximately(sw, lastWorld);
            lastUi = su; lastWorld = sw;

            foreach (var t in FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                ApplyUi(t, su, scaleChanged);
            foreach (var tm in FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!tm.name.EndsWith(ExcludeSuffix)) ApplyWorld(tm, sw, scaleChanged);

            if (scaleChanged || ++scans % PurgeEveryScans == 0) Purge();
        }

        void ApplyUi(Text t, float s, bool force)
        {
            if (t == null) return;
            if (!ui.TryGetValue(t, out var e))
                e = new UiEntry { baseSize = t.fontSize, baseMax = t.resizeTextMaxSize, baseMin = t.resizeTextMinSize, applied = -1, appliedMax = -1 };
            else
            {
                // 코드가 크기를 바꿨으면 그 값을 새 원래 크기로
                if (t.fontSize != e.applied) e.baseSize = t.fontSize;
                if (t.resizeTextMaxSize != e.appliedMax) e.baseMax = t.resizeTextMaxSize;
                if (!force && t.fontSize == e.applied && t.resizeTextMaxSize == e.appliedMax) { ui[t] = e; return; }
            }
            int size = Mathf.Max(1, Mathf.RoundToInt(e.baseSize * s));
            int max = Mathf.Max(1, Mathf.RoundToInt(e.baseMax * s));
            if (t.fontSize != size) t.fontSize = size;
            if (t.resizeTextForBestFit)
            {
                if (t.resizeTextMaxSize != max) t.resizeTextMaxSize = max;
                int min = Mathf.Max(1, Mathf.RoundToInt(e.baseMin * s));
                if (t.resizeTextMinSize != min) t.resizeTextMinSize = Mathf.Min(min, max);
            }
            e.applied = t.fontSize;
            e.appliedMax = t.resizeTextMaxSize;
            ui[t] = e;
        }

        void ApplyWorld(TextMesh tm, float s, bool force)
        {
            if (tm == null) return;
            if (!world.TryGetValue(tm, out var e))
                e = new WorldEntry { baseSize = tm.characterSize, applied = -1f };
            else
            {
                if (!Mathf.Approximately(tm.characterSize, e.applied)) e.baseSize = tm.characterSize;
                else if (!force) return;
            }
            float size = e.baseSize * s;
            if (!Mathf.Approximately(tm.characterSize, size)) tm.characterSize = size;
            e.applied = tm.characterSize;
            world[tm] = e;
        }

        readonly List<Text> deadUi = new List<Text>();
        readonly List<TextMesh> deadWorld = new List<TextMesh>();

        void Purge()
        {
            deadUi.Clear(); deadWorld.Clear();
            foreach (var k in ui.Keys) if (k == null) deadUi.Add(k);
            foreach (var k in world.Keys) if (k == null) deadWorld.Add(k);
            foreach (var k in deadUi) ui.Remove(k);
            foreach (var k in deadWorld) world.Remove(k);
        }
    }
}
