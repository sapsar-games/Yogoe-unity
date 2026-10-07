using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Yoegoe.Characters;
using Yoegoe.Data;
using Yoegoe.Debugging;

namespace Yoegoe.Bootstrap
{
    /// <summary>
    /// Main 씬 월드 배선: 카메라·맵·기물·캐릭터.
    /// 맵·기물 셸은 씬 Prefab 인스턴스(Edit 모드에서 배치). 여기서는 바운드·상태만 연결한다.
    /// </summary>
    public static class WorldAssembler
    {
        public const string OverviewName = "Background_Overview";
        public const string PlayfieldName = "Background_Playfield";

        public struct Config
        {
            public ArtScaleSettings scale;
            public PropLayoutSettings propLayout;
            public Sprite overviewBackgroundSprite;
            public Sprite playfieldSprite;
            public Vector2 overviewOffset;
            public CharacterData oktoData;
            public CharacterData samjokOData;
            public Font hudFont;
        }

        public static void Build(Config cfg)
        {
            EnsureCamera(cfg.scale);
            EnsureLight();
            EnsureEventSystem();
            EnsureMapPointerRouter();
            EnsurePropManager();

            WireMap(cfg);
            WireSceneProps(cfg);
            WireMeritWillow(cfg);

            float mapScale = Mathf.Max(0.01f, cfg.scale.mapScale);
            // 캐릭터는 아직 런타임 스폰 (세이브가 좌표 복원)
            CreateCharacter("옥토끼", MapToWorld(new Vector3(-1f, 0.5f, 0), mapScale),
                Color.white, cfg.oktoData, cfg.hudFont);
            CreateCharacter("삼족오", MapToWorld(new Vector3(0f, 0.5f, 0), mapScale),
                Color.black, cfg.samjokOData, cfg.hudFont);
        }

        static Vector3 MapToWorld(Vector3 mapLocal, float mapScale) =>
            new Vector3(mapLocal.x * mapScale, mapLocal.y * mapScale, mapLocal.z);

        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();
        }

        static void EnsureCamera(ArtScaleSettings scale)
        {
            Camera cam;
            if (Camera.main != null)
            {
                cam = Camera.main;
            }
            else
            {
                var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGO.AddComponent<Camera>();
                cam.orthographic = true;
                cam.transform.position = new Vector3(0, 0, -10);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
            }

            cam.orthographic = true;
            cam.orthographicSize = scale.cameraOrthoSize;
        }

        static void EnsureMapPointerRouter()
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (cam.GetComponent<MapCameraDrag>() == null)
                cam.gameObject.AddComponent<MapCameraDrag>();

            var router = cam.GetComponent<MapPointerRouter>();
            if (router == null) router = cam.gameObject.AddComponent<MapPointerRouter>();
            router.targetCamera = cam;
            router.mapDrag = cam.GetComponent<MapCameraDrag>();
            router.propDropRadius = 0.15f;
            router.lockTapRadius = 0.28f;
        }

        static void EnsureLight()
        {
            if (Object.FindAnyObjectByType<Light>() != null) return;
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        static void EnsurePropManager()
        {
            if (PropManager.Instance != null) return;
            if (Object.FindAnyObjectByType<PropManager>(FindObjectsInactive.Include) != null) return;
            Debug.LogWarning(
                "[WorldAssembler] 씬에 PropManager Prefab 인스턴스 없음 — 런타임 폴백 생성. " +
                "메뉴 Yoegoe/Bake Bootstrap Prefabs (Main · PropManager) 또는 " +
                "Yoegoe/Place Map & Props In Main Scene 을 실행하세요.");
            var go = new GameObject("PropManager");
            go.AddComponent<PropManager>();
        }

        /// <summary>
        /// 씬의 Background_* 를 찾아 걷기/패닝만 연결. 없으면 Main 스프라이트로 폴백 생성.
        /// </summary>
        static void WireMap(Config cfg)
        {
            var overviewGo = GameObject.Find(OverviewName);
            var playfieldGo = GameObject.Find(PlayfieldName);

            if (overviewGo == null && cfg.overviewBackgroundSprite != null)
            {
                Debug.LogWarning(
                    $"[WorldAssembler] 씬에 {OverviewName} 없음 — 런타임 폴백 생성. " +
                    "메뉴 Yoegoe/Place Map & Props In Main Scene 을 실행하세요.");
                overviewGo = CreateBackgroundGo(OverviewName, cfg.overviewBackgroundSprite,
                    cfg.overviewOffset * Mathf.Max(0.01f, cfg.scale.mapScale),
                    cfg.scale.mapScale, cfg.scale.backgroundSort, z: 1f);
            }

            if (playfieldGo == null && cfg.playfieldSprite != null)
            {
                Debug.LogWarning(
                    $"[WorldAssembler] 씬에 {PlayfieldName} 없음 — 런타임 폴백 생성. " +
                    "메뉴 Yoegoe/Place Map & Props In Main Scene 을 실행하세요.");
                playfieldGo = CreateBackgroundGo(PlayfieldName, cfg.playfieldSprite,
                    Vector2.zero, cfg.scale.mapScale, cfg.scale.backgroundSort + 1, z: 0.9f);
            }

            var overviewSr = overviewGo != null ? overviewGo.GetComponent<SpriteRenderer>() : null;
            var playfieldSr = playfieldGo != null ? playfieldGo.GetComponent<SpriteRenderer>() : null;
            Sprite overview = overviewSr != null ? overviewSr.sprite : cfg.overviewBackgroundSprite;
            Sprite playfield = playfieldSr != null ? playfieldSr.sprite : cfg.playfieldSprite;

            if (playfieldGo != null && playfield != null)
            {
                var walkCol = playfieldGo.GetComponent<Collider2D>();
                if (walkCol == null)
                    walkCol = BuildPlayfieldWalkCollider(playfieldGo, playfield);

                MapBounds.SetWalkArea(walkCol);
                if (walkCol == null)
                {
                    float scale = playfieldGo.transform.lossyScale.x;
                    if (TryGetSpriteWorldAabb(playfield, scale, out Vector2 walkMin, out Vector2 walkMax))
                    {
                        const float margin = 0.35f;
                        MapBounds.SetBounds(
                            new Vector2(walkMin.x + margin, walkMin.y + margin),
                            new Vector2(walkMax.x - margin, walkMax.y - margin));
                    }
                }
            }

            WireCameraPan(cfg, overviewGo, overview, playfieldGo, playfield);
        }

        static GameObject CreateBackgroundGo(
            string name, Sprite sprite, Vector2 posXy, float mapScale, int sort, float z)
        {
            float scale = Mathf.Max(0.01f, mapScale);
            var go = new GameObject(name);
            go.transform.position = new Vector3(posXy.x, posXy.y, z);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sort;
            return go;
        }

        static void WireCameraPan(
            Config cfg,
            GameObject overviewGo, Sprite overview,
            GameObject playfieldGo, Sprite playfield)
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return;

            float panW = 0f, panH = 0f;
            Vector2 panCenter = Vector2.zero;

            if (overviewGo != null && overview != null)
            {
                float sx = overviewGo.transform.lossyScale.x;
                float sy = overviewGo.transform.lossyScale.y;
                panW = overview.bounds.size.x * sx;
                panH = overview.bounds.size.y * sy;
                panCenter = new Vector2(overviewGo.transform.position.x, overviewGo.transform.position.y);
            }
            else if (playfieldGo != null && playfield != null)
            {
                float sx = playfieldGo.transform.lossyScale.x;
                float sy = playfieldGo.transform.lossyScale.y;
                panW = playfield.bounds.size.x * sx;
                panH = playfield.bounds.size.y * sy;
                panCenter = new Vector2(playfieldGo.transform.position.x, playfieldGo.transform.position.y);
            }
            else return;

            float camHeight = cam.orthographicSize * 2f;
            float camWidth = camHeight * cam.aspect;
            var drag = cam.GetComponent<MapCameraDrag>();
            if (drag == null) drag = cam.gameObject.AddComponent<MapCameraDrag>();

            float halfExtraW = Mathf.Max(0f, panW / 2f - camWidth / 2f);
            float halfExtraH = Mathf.Max(0f, panH / 2f - camHeight / 2f);

            if (halfExtraW <= 0.01f && halfExtraH <= 0.01f)
            {
                float orthoByW = (panW / 1.2f) / (2f * Mathf.Max(0.01f, cam.aspect));
                float orthoByH = (panH / 1.2f) / 2f;
                float newOrtho = Mathf.Min(orthoByW, orthoByH);
                if (newOrtho > 0.1f && newOrtho < cam.orthographicSize)
                {
                    cam.orthographicSize = newOrtho;
                    camHeight = cam.orthographicSize * 2f;
                    camWidth = camHeight * cam.aspect;
                }
            }

            drag.SetContentRect(panCenter, panW * 0.5f, panH * 0.5f);
            drag.SetOrthoLimits(1.4f, Mathf.Max(cam.orthographicSize * 1.05f, cam.orthographicSize));

            var router = cam.GetComponent<MapPointerRouter>();
            if (router != null) router.mapDrag = drag;

            MapIntro.PrepareAfterMapWire();
        }

        static Collider2D BuildPlayfieldWalkCollider(GameObject fieldGO, Sprite sprite)
        {
            if (fieldGO == null || sprite == null) return null;

            int shapeCount = sprite.GetPhysicsShapeCount();
            if (shapeCount <= 0) return null;

            var col = fieldGO.AddComponent<PolygonCollider2D>();
            col.isTrigger = true;
            col.pathCount = shapeCount;

            var path = new List<Vector2>(64);
            for (int i = 0; i < shapeCount; i++)
            {
                path.Clear();
                sprite.GetPhysicsShape(i, path);
                col.SetPath(i, path);
            }
            return col;
        }

        static bool TryGetSpriteWorldAabb(Sprite sprite, float scale, out Vector2 min, out Vector2 max)
        {
            min = default;
            max = default;
            if (sprite == null || scale <= 0f) return false;

            var verts = sprite.vertices;
            if (verts != null && verts.Length > 0)
            {
                float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
                float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector2 v = verts[i] * scale;
                    if (v.x < minX) minX = v.x;
                    if (v.y < minY) minY = v.y;
                    if (v.x > maxX) maxX = v.x;
                    if (v.y > maxY) maxY = v.y;
                }
                if (minX < maxX && minY < maxY)
                {
                    min = new Vector2(minX, minY);
                    max = new Vector2(maxX, maxY);
                    return true;
                }
            }

            Bounds b = sprite.bounds;
            float hx = b.extents.x * scale;
            float hy = b.extents.y * scale;
            if (hx <= 0f || hy <= 0f) return false;
            min = new Vector2(-hx, -hy);
            max = new Vector2(hx, hy);
            return true;
        }

        /// <summary>씬 PropSlot만 사용. 없으면 PropLayoutSettings Prefab으로 폴백 스폰.</summary>
        static void WireSceneProps(Config cfg)
        {
            var slots = Object.FindObjectsByType<PropSlot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (slots != null && slots.Length > 0)
            {
                for (int i = 0; i < slots.Length; i++)
                    ConfigureProp(slots[i], cfg.scale);
                return;
            }

            Debug.LogWarning(
                "[WorldAssembler] 씬에 PropSlot이 없습니다 — PropLayoutSettings로 폴백 스폰. " +
                "메뉴 Yoegoe/Place Map & Props In Main Scene 을 실행하세요.");
            SpawnPropsFromLayoutFallback(cfg);
        }

        static void ConfigureProp(PropSlot slot, ArtScaleSettings scale)
        {
            if (slot == null) return;
            if (slot.data != null)
                slot.data = PropCatalog.RuntimeCopy(slot.data); // 시트 값은 사본에만

            var sr = slot.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sortingOrder = scale.SortOrderForProp(slot.transform.position.y);

            Sprite sprite = sr != null && sr.sprite != null
                ? sr.sprite
                : (slot.data != null ? slot.data.icon : null);
            Sprite occupied = slot.data != null ? slot.data.occupiedByOwnerSprite : null;
            slot.SetBuiltAppearance(sprite, Color.white, occupied);
            bool prebuilt = slot.data != null && slot.data.isPrebuilt;
            slot.ConfigureBuiltState(prebuilt);
        }

        static void SpawnPropsFromLayoutFallback(Config cfg)
        {
            var layout = cfg.propLayout != null ? cfg.propLayout : PropLayoutSettings.Get();
            if (layout?.placements == null || layout.placements.Length == 0)
            {
                Debug.LogError("[WorldAssembler] PropLayoutSettings 배치가 비어 있습니다.");
                return;
            }

            float mapScale = Mathf.Max(0.01f, cfg.scale.mapScale);
            for (int i = 0; i < layout.placements.Length; i++)
            {
                var place = layout.placements[i];
                if (place?.data == null || place.prefab == null) continue;

                var slot = Object.Instantiate(place.prefab);
                slot.transform.position = MapToWorld(place.position, mapScale);
                if (slot.data == null) slot.data = place.data;
                ConfigureProp(slot, cfg.scale);
                slot.gameObject.name = "Prop_" + (!string.IsNullOrEmpty(slot.data.displayName)
                    ? slot.data.displayName
                    : slot.data.propId);
            }
        }

        static void WireMeritWillow(Config cfg)
        {
            if (MeritWillow.Instance != null) return;

            var existing = Object.FindAnyObjectByType<MeritWillow>(FindObjectsInactive.Include);
            if (existing != null) return;

            var layout = cfg.propLayout != null ? cfg.propLayout : PropLayoutSettings.Get();
            float mapScale = Mathf.Max(0.01f, cfg.scale.mapScale);
            Vector3 pos = layout != null
                ? MapToWorld(layout.willowPosition, mapScale)
                : Vector3.zero;
            MeritWillow.Create(pos, cfg.scale.SortOrderForProp(pos.y));
        }

        static void CreateCharacter(string name, Vector3 pos, Color color, CharacterData realData, Font hudFont)
        {
            if (realData != null)
            {
                CharacterSpawner.Spawn(CharacterCatalog.RuntimeCopy(realData), pos, color, hudFont);
                return;
            }

            var data = ScriptableObject.CreateInstance<CharacterData>();
            data.id = ResolveCharacterIdByName(name);
            data.displayName = name;
            data.startingIntimacy = 50f;
            data.startingStamina = 70f;
            CharacterCatalog.ApplyTo(data);
            CharacterSpawner.Spawn(data, pos, color, hudFont);
        }

        static CharacterId ResolveCharacterIdByName(string name)
        {
            if (name == "옥토끼") return CharacterId.Rabbit;
            if (name == "삼족오") return CharacterId.SamjokO;
            if (name == "구미호") return CharacterId.Gumiho;
            if (name == "고라니") return CharacterId.Gorani;
            return CharacterId.Rabbit;
        }
    }
}
