using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;

namespace Yoegoe.UI
{
    /// <summary>9-1 소환 연출: 암전 → 빛나는 요괴가 화면 중앙으로 하강.</summary>
    public class SummonCeremony : SingletonBehaviour<SummonCeremony>
    {
        public Font font;
        public CharacterData goraniData;

        public bool TryPlay(CharacterId target = CharacterId.Gorani)
        {
            if (!CharacterSummon.CanSummon(target)) return false;
            if (!isActiveAndEnabled) return false;
            StartCoroutine(PlayRoutine(target));
            return true;
        }

        IEnumerator PlayRoutine(CharacterId target)
        {
            CeremonyGate.Begin();

            var cam = Camera.main;
            Vector3 center = Vector3.zero;
            Vector3 start = new Vector3(0f, 3.5f, 0f);
            if (cam != null)
            {
                float depth = -cam.transform.position.z;
                Vector3 mid = cam.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, depth));
                mid.z = 0f;
                center = mid;
                start = mid + Vector3.up * (cam.orthographicSize * 1.35f);
                start.z = 0f;
            }

            Image dim = CreateDimOverlay();
            float t = 0f;
            const float dimIn = 0.45f;
            while (t < dimIn)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dimIn);
                if (dim != null) dim.color = new Color(0f, 0f, 0.05f, Mathf.Lerp(0f, 0.72f, u));
                yield return null;
            }

            // 소환 절차(향·스폰·초기화·저장)는 CharacterSummon.TrySummon — 여기선 결과 요괴를 연출만
            var agent = CharacterSummon.TrySummon(target, font,
                target == CharacterId.Gorani ? goraniData : null, start);
            if (agent == null)
            {
                DestroyOverlay(dim);
                CeremonyGate.End();
                yield break;
            }

            // 연출 중(CeremonyGate) 맵 AI 정지
            Transform tr = agent.transform;
            Vector3 baseScale = tr.localScale;
            tr.localScale = baseScale * 0.35f;
            PulseGlow(agent, true);

            const float fall = 1.55f;
            t = 0f;
            while (t < fall)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / fall));
                tr.position = Vector3.Lerp(start, center, u);
                float pulse = 0.85f + 0.25f * Mathf.Sin(t * 8f);
                tr.localScale = Vector3.Lerp(baseScale * 0.35f, baseScale * pulse, u);
                yield return null;
            }

            tr.position = center;
            tr.localScale = baseScale;
            agent.ApplyFreshSummon(); // 착지 위치 기준으로 걷기 재시작
            PulseGlow(agent, false);

            t = 0f;
            const float dimOut = 0.5f;
            while (t < dimOut)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dimOut);
                if (dim != null) dim.color = new Color(0f, 0f, 0.05f, Mathf.Lerp(0.72f, 0f, u));
                yield return null;
            }

            DestroyOverlay(dim);
            GameSaveBridge.SaveFromWorld();
            CeremonyGate.End();
        }

        static void PulseGlow(CharacterAgent agent, bool bright)
        {
            if (agent == null) return;

            var sr = agent.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = bright
                    ? Color.Lerp(Color.white, new Color(0.85f, 1f, 1f, 1f), 0.55f)
                    : Color.white;
                return;
            }

            var mr = agent.GetComponent<MeshRenderer>();
            if (mr == null || mr.material == null) return;
            Color c = CharacterSummon.PlaceholderColor;
            if (bright) c = Color.Lerp(c, Color.white, 0.45f);
            if (mr.material.HasProperty("_BaseColor")) mr.material.SetColor("_BaseColor", c);
            if (mr.material.HasProperty("_Color")) mr.material.color = c;
        }

        static Image CreateDimOverlay()
        {
            var go = new GameObject("SummonDim");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 780;
            go.AddComponent<CanvasScaler>();
            var img = go.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0.05f, 0f);
            img.raycastTarget = true; // 연출 중 클릭 차단
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return img;
        }

        static void DestroyOverlay(Image dim)
        {
            if (dim != null) Destroy(dim.gameObject);
        }
    }
}
