using UnityEngine;

namespace Yoegoe.Debugging
{
    /// <summary>
    /// 맵 패닝·줌. 입력은 MapPointerRouter가 소유하고, 여기서는 카메라만 움직인다.
    /// </summary>
    public class MapCameraDrag : MonoBehaviour
    {
        private Camera cam;
        private Vector2 panCenter;
        private float contentHalfW;
        private float contentHalfH;
        private bool boundsSet;

        private Vector2 minCenter;
        private Vector2 maxCenter;
        private float lastAspect = -1f;

        [Tooltip("최대 확대(ortho 최소).")]
        public float minOrtho = 1.4f;

        [Tooltip("최대 축소(ortho 최대). 맵 가로 맞춤 값으로 잠근다 — 그보다 멀어지면 양옆 틈이 생긴다.")]
        public float maxOrtho = 8f;

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        /// <summary>
        /// 줌 아웃이 맵 가로를 넘지 못하게 잠근다. (양옆 카메라 배경색 틈 방지)
        /// 인트로처럼 줌인된 상태는 그대로 둔다.
        /// </summary>
        private void LateUpdate()
        {
            if (!boundsSet) return;
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || !cam.orthographic) return;

            float fit = ComputeOrthoToFitWidth();
            maxOrtho = Mathf.Max(minOrtho + 0.01f, fit);

            float aspect = cam.aspect;
            bool aspectChanged = lastAspect > 0f && Mathf.Abs(aspect - lastAspect) > 0.001f;
            lastAspect = aspect;

            // 가로 맞춤보다 멀리 나간 경우만 끌어당김
            if (cam.orthographicSize > fit + 0.001f)
            {
                cam.orthographicSize = fit;
                RefreshPanLimits();
                Vector3 pos = cam.transform.position;
                pos.x = panCenter.x;
                cam.transform.position = ClampCenter(pos);
            }
            else if (aspectChanged && cam.orthographicSize >= fit - 0.05f)
            {
                // 이미 가로 맞춤 홈인데 Game 뷰 비율만 바뀐 경우
                FitOrthoToContentWidth();
            }
        }

        /// <summary>화면 픽셀 델타만큼 카메라를 이동(맵이 손가락을 따라오는 방향).</summary>
        public void ApplyScreenDelta(Vector2 screenDelta)
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;
            if (screenDelta.sqrMagnitude <= 0f) return;

            float worldPerPixel = (cam.orthographicSize * 2f) / Mathf.Max(1, Screen.height);
            Vector3 worldDelta = new Vector3(-screenDelta.x * worldPerPixel, -screenDelta.y * worldPerPixel, 0f);
            Vector3 newPos = cam.transform.position + worldDelta;
            cam.transform.position = ClampCenter(newPos);
        }

        /// <summary>
        /// 핀치/휠 줌. ratio&gt;1 이면 축소(멀리), &lt;1 이면 확대(가까이).
        /// screenPivot 아래 월드 좌표가 줌 전후에 유지된다.
        /// </summary>
        public void ZoomByRatio(Vector2 screenPivot, float ratio)
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || !cam.orthographic) return;
            if (ratio <= 0.0001f || Mathf.Approximately(ratio, 1f)) return;

            float oldOrtho = cam.orthographicSize;
            float newOrtho = Mathf.Clamp(oldOrtho * ratio, minOrtho, maxOrtho);
            if (Mathf.Approximately(newOrtho, oldOrtho)) return;

            float depth = Mathf.Abs(cam.transform.position.z);
            Vector3 before = cam.ScreenToWorldPoint(new Vector3(screenPivot.x, screenPivot.y, depth));
            before.z = 0f;

            cam.orthographicSize = newOrtho;
            RefreshPanLimits();

            Vector3 after = cam.ScreenToWorldPoint(new Vector3(screenPivot.x, screenPivot.y, depth));
            after.z = 0f;
            Vector3 pos = cam.transform.position + (before - after);
            cam.transform.position = ClampCenter(pos);
        }

        public bool BoundsReady => boundsSet;
        public Vector2 ContentCenter => panCenter;
        public float ContentHalfWidth => contentHalfW;
        public Vector2 MinPanCenter => minCenter;
        public Vector2 MaxPanCenter => maxCenter;

        /// <summary>맵 콘텐츠 반폭·반높이(월드)와 중심. 줌에 따라 패닝 한도를 다시 계산한다.</summary>
        public void SetContentRect(Vector2 center, float halfWidth, float halfHeight)
        {
            if (cam == null) cam = GetComponent<Camera>();
            panCenter = center;
            contentHalfW = Mathf.Max(0f, halfWidth);
            contentHalfH = Mathf.Max(0f, halfHeight);
            boundsSet = true;
            RefreshPanLimits();
            if (cam != null)
                cam.transform.position = ClampCenter(cam.transform.position);
        }

        /// <summary>
        /// 맵 가로 = 카메라가 보는 가로가 되도록 하는 Ortho.
        /// cam.aspect 사용 (실제 렌더 비율과 동일해야 양옆 틈이 안 남음).
        /// </summary>
        public float ComputeOrthoToFitWidth()
        {
            if (cam == null) cam = GetComponent<Camera>();
            float aspect = cam != null ? cam.aspect : (float)Screen.width / Mathf.Max(1, Screen.height);
            return Mathf.Max(0.1f, contentHalfW / Mathf.Max(0.01f, aspect));
        }

        /// <summary>맵 가로에 Ortho를 맞추고 X를 맵 중심에 둔다. 세로가 길면 위아래만 패닝.</summary>
        public void FitOrthoToContentWidth()
        {
            if (!boundsSet) return;
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null || !cam.orthographic) return;

            float ortho = ComputeOrthoToFitWidth();
            // max를 맞춤값 그대로 — 1.05 여유 주면 살짝만 줄여도 양옆 파란 틈이 보임
            SetOrthoLimits(minOrtho, ortho);
            cam.orthographicSize = ortho;
            lastAspect = cam.aspect;
            RefreshPanLimits();

            Vector3 pos = cam.transform.position;
            pos.x = panCenter.x;
            cam.transform.position = ClampCenter(pos);
        }

        public void SetOrthoLimits(float min, float max)
        {
            minOrtho = Mathf.Max(0.5f, min);
            maxOrtho = Mathf.Max(minOrtho + 0.1f, max);
            if (cam == null) cam = GetComponent<Camera>();
            if (cam != null)
                cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minOrtho, maxOrtho);
            RefreshPanLimits();
        }

        /// <summary>ortho 변경 후 패닝 한도 재계산 (인트로 등).</summary>
        public void RefreshPanLimits()
        {
            if (!boundsSet || cam == null) return;
            float camH = cam.orthographicSize * 2f;
            float camW = camH * cam.aspect;
            float halfExtraW = Mathf.Max(0f, contentHalfW - camW * 0.5f);
            float halfExtraH = Mathf.Max(0f, contentHalfH - camH * 0.5f);
            minCenter = new Vector2(panCenter.x - halfExtraW, panCenter.y - halfExtraH);
            maxCenter = new Vector2(panCenter.x + halfExtraW, panCenter.y + halfExtraH);
        }

        public Vector3 ClampCenter(Vector3 pos)
        {
            if (!boundsSet) return pos;
            pos.x = Mathf.Clamp(pos.x, minCenter.x, maxCenter.x);
            pos.y = Mathf.Clamp(pos.y, minCenter.y, maxCenter.y);
            return pos;
        }
    }
}
