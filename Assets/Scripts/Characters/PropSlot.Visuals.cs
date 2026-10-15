using System;
using System.Collections.Generic;
using UnityEngine;
using Yoegoe.Cooking;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.UI;

namespace Yoegoe.Characters
{
    /// <summary>점유 아트·자물쇠·이름표·더미/자원 라벨·황금 반짝임. (PropSlot 분할 — 본체는 PropSlot.cs)</summary>
    public partial class PropSlot
    {
        void RefreshGoldenSparkle()
        {
            bool want = IsBuilt && HasGoldenPending && spriteRenderer != null;
            if (want)
            {
                float u = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                spriteRenderer.color = Color.Lerp(Color.white, new Color(1f, 0.86f, 0.35f, 1f), u);
                sparkling = true;
            }
            else if (sparkling)
            {
                sparkling = false;
                if (spriteRenderer != null && IsBuilt) spriteRenderer.color = Color.white;
            }
        }

        /// <summary>기물 스프라이트 윗변 중앙 + pad (연출·드래그 마커 위치).</summary>
        public Vector3 TopAnchorWorld(float pad)
        {
            if (spriteRenderer != null && spriteRenderer.sprite != null)
                return new Vector3(transform.position.x, spriteRenderer.bounds.max.y + pad, transform.position.z);
            return transform.position + Vector3.up * (0.25f + pad);
        }

        /// <summary>더미 UI를 즉시 갱신 (LateUpdate 대기 없이).</summary>
        public void ForceRefreshPileLabel() => RefreshPileLabel();

        /// <summary>
        /// 보관 라벨(***·숫자) 히트 — 라벨이 기물 bounds 밖으로 나와 있어도 그 기물 탭으로 (본체 탭은 MapPointerRouter).
        /// </summary>
        public bool TryGetPileLabelHitScore(Vector3 world, float padding, out float score)
        {
            score = float.MaxValue;
            if (!HasPendingCollectible) return false;
            if (pileLabel == null || !pileLabel.gameObject.activeInHierarchy) return false;

            var mr = pileLabel.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Bounds b = mr.bounds;
                b.Expand(Mathf.Max(0f, padding));
                Vector3 p = world;
                p.z = b.center.z;
                if (!b.Contains(p)) return false;
                score = Vector2.Distance(b.center, p);
                return true;
            }

            // MeshRenderer 없을 때: LowerCenter 기준 대략 박스
            Vector3 c = pileLabel.transform.position;
            float halfW = 0.4f + Mathf.Max(0f, padding);
            float h = 0.85f + Mathf.Max(0f, padding);
            if (Mathf.Abs(world.x - c.x) > halfW) return false;
            if (world.y < c.y - padding || world.y > c.y + h) return false;
            score = Vector2.Distance(new Vector2(c.x, c.y + h * 0.5f), world);
            return true;
        }

        /// <summary>Main이 건립 시 쓸 스프라이트·틴트를 기억.</summary>
        public void SetBuiltAppearance(Sprite sprite, Color tint, Sprite occupiedByOwner = null)
        {
            builtSprite = sprite;
            builtTint = tint;
            occupiedByOwnerSprite = occupiedByOwner != null
                ? occupiedByOwner
                : data != null ? data.occupiedByOwnerSprite : null;
        }

        /// <summary>주인 전용 점유 아트가 있으면 기물 스프라이트를 바꾸고, 캐릭터 본체를 숨긴다.</summary>
        public void RefreshOccupancyVisual()
        {
            bool useOccupied = ShouldShowOwnerOccupationArt();

            if (spriteRenderer != null && IsBuilt)
            {
                Sprite next = useOccupied
                    ? (occupiedByOwnerSprite != null ? occupiedByOwnerSprite : builtSprite)
                    : builtSprite;
                if (next != null) spriteRenderer.sprite = next;
                spriteRenderer.color = Color.white;
                spriteRenderer.enabled = true;
            }

            // 이전 숨김 복구
            if (hiddenOccupantVisual != null && (!useOccupied || hiddenOccupantVisual != Occupant))
            {
                hiddenOccupantVisual.SetSpriteVisible(true);
                hiddenOccupantVisual = null;
            }

            if (useOccupied && Occupant != null)
            {
                Occupant.SetSpriteVisible(false);
                hiddenOccupantVisual = Occupant;
            }
        }

        private bool ShouldShowOwnerOccupationArt()
        {
            if (!IsOccupied || Occupant == null || Occupant.Data == null) return false;
            Sprite art = occupiedByOwnerSprite != null
                ? occupiedByOwnerSprite
                : (data != null ? data.occupiedByOwnerSprite : null);
            if (art == null) return false;
            if (data == null) return false;
            if (!data.hasUniqueEndingAnimation) return false;
            return data.isEndingProp && data.owner == Occupant.Data.id;
        }

        private void ApplyBuiltVisual()
        {
            if (lockLabel != null) lockLabel.gameObject.SetActive(false);

            if (meshRenderer != null && !(meshRenderer is SpriteRenderer))
            {
                meshRenderer.enabled = true;
                if (meshRenderer.material != null)
                    meshRenderer.material.color = builtTint;
            }

            RefreshOccupancyVisual();
        }

        private void ApplyLockVisual()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(0.35f, 0.35f, 0.4f, 0.55f);
                if (builtSprite != null) spriteRenderer.sprite = builtSprite;
            }
            if (meshRenderer != null && !(meshRenderer is SpriteRenderer))
            {
                meshRenderer.enabled = true;
                if (meshRenderer.material != null)
                    meshRenderer.material.color = new Color(0.25f, 0.25f, 0.3f, 0.8f);
            }
            EnsureLockLabel();
            lockLabel.gameObject.SetActive(true);
        }

        private void RefreshLockVisual()
        {
            if (IsBuilt)
            {
                if (lockLabel != null && lockLabel.gameObject.activeSelf)
                    lockLabel.gameObject.SetActive(false);
                return;
            }
            EnsureLockLabel();
            if (!lockLabel.gameObject.activeSelf)
                lockLabel.gameObject.SetActive(true);
            lockLabel.transform.position = transform.position + Vector3.up * 0.55f;
        }

        private void EnsureLockLabel()
        {
            if (lockLabel != null) return;
            var go = new GameObject(name + "_Lock");
            lockLabel = go.AddComponent<TextMesh>();
            lockLabel.anchor = TextAnchor.MiddleCenter;
            lockLabel.alignment = TextAlignment.Center;
            lockLabel.characterSize = 0.07f;
            lockLabel.fontSize = UiFonts.Size(42);
            lockLabel.color = new Color(0.9f, 0.85f, 0.7f, 1f);
            if (sharedPileFont == null)
                sharedPileFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (sharedPileFont != null) lockLabel.font = sharedPileFont;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 480;
        }

        /// <summary>건립된 기물 위 displayName. 진한 회색 배경 + 흰 글자.</summary>
        void RefreshNameLabel()
        {
            if (!IsBuilt)
            {
                if (nameLabel != null && nameLabel.gameObject.activeSelf)
                    nameLabel.gameObject.SetActive(false);
                lastNameLabelText = null;
                return;
            }

            EnsureNameLabel();
            if (!nameLabel.gameObject.activeSelf)
                nameLabel.gameObject.SetActive(true);

            string want = DisplayName;
            if (want != lastNameLabelText)
            {
                lastNameLabelText = want;
                nameLabel.text = want;
            }
            nameLabel.transform.position = GetNameLabelWorldPos();
            FitNameLabelBg();
            EnsureNameLabelSorting();
        }

        private void RefreshPileLabel()
        {
            if (IsResourceProp)
            {
                RefreshResourceLabel();
                return;
            }

            if (!HasPendingMerit)
            {
                if (pileLabel != null && pileLabel.gameObject.activeSelf)
                    pileLabel.gameObject.SetActive(false);
                lastPileAmount = null;
                lastPileRounded = int.MinValue;
                lastPileDisplayKey = double.NaN;
                return;
            }

            EnsurePileLabel();
            if (!pileLabel.gameObject.activeSelf)
                pileLabel.gameObject.SetActive(true);

            // 표시 문자열이 실제로 바뀔 때만 TextMesh 갱신 (매 프레임 할당 → GC 스파이크 방지)
            // 만창이면 숫자 대신 "가득"
            if (IsStorageHalted)
            {
                if (lastPileAmount != "가득")
                {
                    lastPileAmount = "가득";
                    lastPileRounded = int.MinValue;
                    lastPileDisplayKey = double.NaN;
                    pileLabel.text = lastPileAmount;
                }
            }
            else
            {
                double v = Math.Abs(PendingMerit.ToDouble());
                if (v < 1000d)
                {
                    int rounded = Mathf.RoundToInt((float)v);
                    if (rounded != lastPileRounded)
                    {
                        lastPileRounded = rounded;
                        lastPileDisplayKey = double.NaN;
                        lastPileAmount = rounded.ToString();
                        pileLabel.text = lastPileAmount;
                    }
                }
                else
                {
                    // 큰 수는 표시 단위가 바뀔 때만
                    double key = Math.Round(PendingMerit.Mantissa, 2) * 1000 + PendingMerit.Exponent;
                    if (key != lastPileDisplayKey)
                    {
                        lastPileDisplayKey = key;
                        lastPileRounded = int.MinValue;
                        lastPileAmount = PendingMerit.ToDisplayString();
                        pileLabel.text = lastPileAmount;
                    }
                }
            }

            pileLabel.transform.position = GetPileLabelWorldPos();
            FitPileLabelBg();
            EnsurePileLabelSorting();
        }

        /// <summary>자원 기물 라벨: "물 3/6" · 만창이면 붉게.</summary>
        void RefreshResourceLabel()
        {
            if (!HasPendingResources)
            {
                if (pileLabel != null && pileLabel.gameObject.activeSelf)
                    pileLabel.gameObject.SetActive(false);
                lastResourceStored = -1;
                return;
            }

            EnsurePileLabel();
            if (!pileLabel.gameObject.activeSelf)
                pileLabel.gameObject.SetActive(true);

            int stored = storage.Stored, cap = ResourceCapacity;
            bool halted = IsStorageHalted;
            if (stored != lastResourceStored || cap != lastResourceCapacity || halted != lastResourceHalted)
            {
                lastResourceStored = stored;
                lastResourceCapacity = cap;
                lastResourceHalted = halted;
                pileLabel.text = ResourceLabel(ResourceType) + " " + stored + "/" + cap;
                // 흰 배경 위: 평소 진한 초록, 만창은 붉은 경고
                pileLabel.color = halted
                    ? new Color(0.75f, 0.2f, 0.15f, 1f)
                    : new Color(0.12f, 0.45f, 0.22f, 1f);
            }
            pileLabel.transform.position = GetPileLabelWorldPos();
            FitPileLabelBg();
            EnsurePileLabelSorting();
        }

        static string ResourceLabel(PropResourceType type)
        {
            switch (type)
            {
                case PropResourceType.Water: return "물";
                default: return "재료";
            }
        }

        float GetPropTopY()
        {
            float topY = transform.position.y + 0.85f;
            if (spriteRenderer != null && spriteRenderer.enabled && spriteRenderer.sprite != null)
                topY = spriteRenderer.bounds.max.y;
            return topY;
        }

        Vector3 GetNameLabelWorldPos()
        {
            // 스프라이트에 더 가깝게 — 공덕 숫자보다 아래
            return new Vector3(transform.position.x, GetPropTopY(), transform.position.z);
        }

        Vector3 GetPileLabelWorldPos()
        {
            // 엔딩 점유 아트처럼 키가 큰 기물도 숫자게 스프라이트 위로 뜨게 (이름표 위)
            return new Vector3(transform.position.x, GetPropTopY() + 0.20f, transform.position.z);
        }

        void EnsureNameLabelSorting()
        {
            if (nameLabelBg != null) nameLabelBg.sortingOrder = 1185;
            if (nameLabel == null) return;
            var mr = nameLabel.GetComponent<MeshRenderer>();
            if (mr == null) return;
            mr.sortingOrder = 1190;
        }

        void FitNameLabelBg()
        {
            if (nameLabel == null || nameLabelBg == null) return;
            var mr = nameLabel.GetComponent<MeshRenderer>();
            if (mr == null) return;
            // TextMesh bounds는 월드 — 부모 스케일 1이므로 localScale에 그대로 패딩
            var size = mr.bounds.size;
            const float padX = 0.06f;
            const float padY = 0.03f;
            nameLabelBg.transform.localScale = new Vector3(
                Mathf.Max(0.12f, size.x + padX),
                Mathf.Max(0.08f, size.y + padY),
                1f);
        }

        static Sprite NameBgSprite()
        {
            if (sharedNameBgSprite != null) return sharedNameBgSprite;
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var px = new Color[64];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply(false, false);
            sharedNameBgSprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
            return sharedNameBgSprite;
        }

        void EnsurePileLabelSorting()
        {
            if (pileLabelBg != null) pileLabelBg.sortingOrder = 1195;
            if (pileLabel == null) return;
            var mr = pileLabel.GetComponent<MeshRenderer>();
            if (mr == null) return;
            // 기물·캐릭터(수백대)보다 항상 앞에. TextMesh는 sortingOrder가 먹히도록 명시.
            mr.sortingOrder = 1200;
        }

        void FitPileLabelBg()
        {
            if (pileLabel == null || pileLabelBg == null) return;
            var mr = pileLabel.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var size = mr.bounds.size;
            const float padX = 0.08f;
            const float padY = 0.04f;
            pileLabelBg.transform.localScale = new Vector3(
                Mathf.Max(0.14f, size.x + padX),
                Mathf.Max(0.1f, size.y + padY),
                1f);
        }

        /// <summary>
        /// 라벨 오브젝트 생성 — 흰 배경 + 진한 초록 글자.
        /// </summary>
        private void EnsurePileLabel()
        {
            if (pileLabel != null) return;

            var go = new GameObject("MeritPile");
            // 부모 스케일(기물 propScale)에 숫자가 찌그러지지 않게 월드에 독립
            go.transform.SetParent(null, false);

            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(go.transform, false);
            pileLabelBg = bgGo.AddComponent<SpriteRenderer>();
            pileLabelBg.sprite = NameBgSprite();
            pileLabelBg.color = new Color(1f, 1f, 1f, 0.95f);

            pileLabel = go.AddComponent<TextMesh>();
            pileLabel.anchor = TextAnchor.MiddleCenter;
            pileLabel.alignment = TextAlignment.Center;
            pileLabel.characterSize = 0.045f;
            pileLabel.fontSize = UiFonts.Size(18);
            pileLabel.color = new Color(0.12f, 0.45f, 0.22f, 1f);
            if (sharedPileFont == null)
                sharedPileFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (sharedPileFont != null) pileLabel.font = sharedPileFont;
            EnsurePileLabelSorting();
        }

        void EnsureNameLabel()
        {
            if (nameLabel != null) return;

            var go = new GameObject("PropName");
            go.transform.SetParent(null, false);

            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(go.transform, false);
            nameLabelBg = bgGo.AddComponent<SpriteRenderer>();
            nameLabelBg.sprite = NameBgSprite();
            nameLabelBg.color = new Color(0.18f, 0.18f, 0.2f, 0.92f);

            nameLabel = go.AddComponent<TextMesh>();
            nameLabel.anchor = TextAnchor.MiddleCenter;
            nameLabel.alignment = TextAlignment.Center;
            nameLabel.characterSize = 0.045f;
            nameLabel.fontSize = UiFonts.Size(18);
            nameLabel.color = Color.white;
            if (sharedPileFont == null)
                sharedPileFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (sharedPileFont != null) nameLabel.font = sharedPileFont;
            EnsureNameLabelSorting();
        }
    }
}
