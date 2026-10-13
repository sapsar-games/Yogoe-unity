using System.Collections;
using UnityEngine;
using Yoegoe.Core;
using Yoegoe.Debugging;
using Yoegoe.UI;

namespace Yoegoe.Bootstrap
{
    /// <summary>
    /// 인트로: Overview 하늘에서 시작해, 로딩이 끝나면 카메라가 살짝 아래로 내려가며 본편 진입.
    /// </summary>
    public sealed class MapIntro : MonoBehaviour
    {
        public static MapIntro Instance { get; private set; }

        /// <summary>하늘 구도용 ortho (맵 상단이 화면을 채우도록).</summary>
        const float IntroOrtho = 1.65f;
        const float PanSeconds = 1.35f;

        static readonly Color SkyClear = new Color(0.42f, 0.72f, 0.88f);

        float homeOrtho;
        Vector3 homePos;
        bool prepared;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary><see cref="WorldAssembler"/> WireCameraPan 직후 호출.</summary>
        public static void PrepareAfterMapWire()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var intro = cam.GetComponent<MapIntro>();
            if (intro == null) intro = cam.gameObject.AddComponent<MapIntro>();
            intro.Prepare();
        }

        void Prepare()
        {
            var cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            var drag = cam != null ? cam.GetComponent<MapCameraDrag>() : null;
            if (cam == null || drag == null || !drag.BoundsReady) return;

            // 홈 = 맵 가로 맞춤. 부트 직후 aspect가 어긋나도 PlayReveal에서 다시 맞춤.
            homeOrtho = drag.ComputeOrthoToFitWidth();
            homePos = cam.transform.position;
            homePos.x = drag.ContentCenter.x;

            float ortho = Mathf.Clamp(IntroOrtho, drag.minOrtho, Mathf.Max(drag.minOrtho + 0.05f, homeOrtho));
            cam.orthographicSize = ortho;
            drag.RefreshPanLimits();

            Vector3 sky = homePos;
            sky.x = drag.ContentCenter.x;
            sky.y = drag.MaxPanCenter.y;
            cam.transform.position = drag.ClampCenter(sky);

            cam.backgroundColor = SkyClear;
            prepared = true;
        }

        public bool IsPrepared => prepared;

        /// <summary>하늘 → 홈으로 ortho·위치를 보간. HUD는 연출 중 숨김.</summary>
        public IEnumerator PlayReveal()
        {
            if (!prepared)
                yield break;

            CeremonyGate.Begin();
            SetHudVisible(false);

            var cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            var drag = cam != null ? cam.GetComponent<MapCameraDrag>() : null;
            if (cam == null)
            {
                CeremonyGate.End();
                prepared = false;
                yield break;
            }

            Vector3 from = cam.transform.position;
            float fromOrtho = cam.orthographicSize; // Prepare가 둔 하늘 ortho 유지

            // 홈만 현재 화면 비율로 다시 계산 (지금 ortho는 건드리지 않음)
            if (drag != null && drag.BoundsReady)
            {
                homeOrtho = drag.ComputeOrthoToFitWidth();
                homePos.x = drag.ContentCenter.x;
                drag.SetOrthoLimits(drag.minOrtho, homeOrtho);
            }

            float t = 0f;
            while (t < PanSeconds)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / PanSeconds));
                cam.orthographicSize = Mathf.Lerp(fromOrtho, homeOrtho, u);
                if (drag != null) drag.RefreshPanLimits();
                Vector3 p = Vector3.Lerp(from, homePos, u);
                cam.transform.position = drag != null ? drag.ClampCenter(p) : p;
                yield return null;
            }

            if (drag != null)
            {
                drag.FitOrthoToContentWidth();
                cam.transform.position = drag.ClampCenter(homePos);
            }
            else
            {
                cam.orthographicSize = homeOrtho;
                cam.transform.position = homePos;
            }

            SetHudVisible(true);
            CeremonyGate.End();
            prepared = false;
        }

        static void SetHudVisible(bool visible)
        {
            if (GameHud.Instance != null)
                GameHud.Instance.gameObject.SetActive(visible);
        }
    }
}
