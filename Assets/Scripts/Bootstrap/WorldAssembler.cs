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
        /// 씬의 Background_* 를 찾아 걷기/패닝만 연결.
        /// 플레이어 빌드는 씬 Prefab이 있어야 하고, 폴백 생성은 에디터 전용.
        /// </summary>
        static void WireMap(Config cfg)
        {
            var overviewGo = GameObject.Find(OverviewName);
            var playfieldGo = GameObject.Find(PlayfieldName);

#if UNITY_EDITOR
            // 개발 중 씬 미배치 시에만 임시 생성. 출시 빌드에는 포함되지 않음.
            if (overviewGo == null && cfg.overviewBackgroundSprite != null)
            {
                Debug.LogWarning(
                    $"[WorldAssembler] 씬에 {OverviewName} 없음 — 에디터 폴백 생성. " +
                    "메뉴 Yoegoe/Place Map & Props In Main Scene 을 실행하세요.");
                overviewGo = CreateBackgroundGo(OverviewName, cfg.overviewBackgroundSprite,
                    cfg.overviewOffset * Mathf.Max(0.01f, cfg.scale.mapScale),
                    cfg.scale.mapScale, cfg.scale.backgroundSort, z: 1f);
            }

            if (playfieldGo == null && cfg.playfieldSprite != null)
            {
                Debug.LogWarning(
                    $"[WorldAssembler] 씬에 {PlayfieldName} 없음 — 에디터 폴백 생성. " +
                    "메뉴 Yoegoe/Place Map & Props In Main Scene 을 실행하세요.");
                playfieldGo = CreateBackgroundGo(PlayfieldName, cfg.playfieldSprite,
                    Vector2.zero, cfg.scale.mapScale, cfg.scale.backgroundSort + 1, z: 0.9f);
            }
#endif

            // 에디터·플레이어 공통: Prefab이 하나도 없으면 wiring 중단.
            if (overviewGo == null && playfieldGo == null)
            {
                Debug.LogError(
                    "[WorldAssembler] Background_Overview / Background_Playfield 가 씬에 없습니다. " +
                    "메뉴 Yoegoe/Place Map & Props In Main Scene 을 실행하세요.");
                return;
            }

            // Playfield = 캐릭터 걷기 영역.
            var playfieldSr = playfieldGo != null ? playfieldGo.GetComponent<SpriteRenderer>() : null;
            Sprite playfield = playfieldSr != null ? playfieldSr.sprite : null;
            if (playfieldGo != null && playfield != null)
            {
                // Prefab에 Collider2D가 있으면 그대로, 없으면 스프라이트 Physics Shape로 생성.
                var sourceCol = playfieldGo.GetComponent<Collider2D>();
                if (sourceCol == null)
                    sourceCol = BuildPlayfieldWalkCollider(playfieldGo, playfield);

                // Prefab 꼭짓점은 유지하고, 런타임 자식에 복사·팽창한 영역을 WalkArea로 씀.
                const float walkPadWorld = 0.1f;
                var walkCol = EnsureExpandedWalkCollider(playfieldGo, sourceCol, walkPadWorld);
                MapBounds.SetWalkArea(walkCol);

                // Collider도 없으면: 스프라이트 월드 AABB를 마진만큼 넓힌 사각 바운드.
                if (walkCol == null)
                {
                    float scale = playfieldGo.transform.lossyScale.x;
                    if (TryGetSpriteWorldAabb(playfield, scale, out Vector2 walkMin, out Vector2 walkMax))
                    {
                        const float margin = 0.2f;
                        MapBounds.SetBounds(
                            new Vector2(walkMin.x - margin, walkMin.y - margin),
                            new Vector2(walkMax.x + margin, walkMax.y + margin));
                    }
                }
            }

            // Overview 기준으로 카메라 Ortho·드래그 패닝 한도 연결 (스프라이트는 안에서 GO로부터 읽음).
            WireCameraPan(overviewGo, playfieldGo);
        }

#if UNITY_EDITOR
        /// <summary>에디터 전용: 씬에 배경 Prefab이 없을 때 임시 스프라이트 GO 생성.</summary>
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
#endif


        /// <summary>
        /// 맵 배경 크기에 맞춰 카메라 Ortho·패닝 한도를 연결한다.
        /// 가로는 화면에 딱 맞추고, 세로가 길면 위아래만 드래그로 본다.
        /// </summary>
        static void WireCameraPan(GameObject overviewGo, GameObject playfieldGo)
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return;

            // 1) 패닝 콘텐츠 = Overview 우선, 없으면 Playfield.
            //    SpriteRenderer.bounds = 스케일 반영된 월드 AABB (양옆 틈 계산 기준).
            float panW = 0f, panH = 0f;
            Vector2 panCenter = Vector2.zero;

            var overviewSr = overviewGo != null ? overviewGo.GetComponent<SpriteRenderer>() : null;
            var playfieldSr = playfieldGo != null ? playfieldGo.GetComponent<SpriteRenderer>() : null;

            if (overviewSr != null && overviewSr.sprite != null)
            {
                Bounds b = overviewSr.bounds;
                panW = b.size.x;
                panH = b.size.y;
                panCenter = new Vector2(b.center.x, b.center.y);
            }
            else if (playfieldSr != null && playfieldSr.sprite != null)
            {
                Bounds b = playfieldSr.bounds;
                panW = b.size.x;
                panH = b.size.y;
                panCenter = new Vector2(b.center.x, b.center.y);
            }
            else return;

            var drag = cam.GetComponent<MapCameraDrag>();
            if (drag == null) drag = cam.gameObject.AddComponent<MapCameraDrag>();

            // 2) 맵 반폭·반높이 등록 후, 가로 = 화면 가로로 Ortho 맞춤 (세로만 패닝).
            drag.SetContentRect(panCenter, panW * 0.5f, panH * 0.5f);
            drag.FitOrthoToContentWidth();

            // 3) 터치/드래그 입력이 drag를 쓰도록 연결 + 인트로가 홈 ortho/위치를 기억.
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

        const string WalkAreaChildName = "__WalkArea";

        /// <summary>
        /// Prefab/소스 폴리곤은 그대로 두고, 자식 GO에 바깥으로 pad만큼 넓힌 복사본을 만든다.
        /// Build가 여러 번 호출돼도 소스 기준으로 다시 써서 점점 커지지 않는다.
        /// </summary>
        static Collider2D EnsureExpandedWalkCollider(
            GameObject fieldGO, Collider2D source, float padWorld)
        {
            if (fieldGO == null || source == null) return null;

            var poly = source as PolygonCollider2D;
            if (poly == null) return source;

            Transform childT = fieldGO.transform.Find(WalkAreaChildName);
            GameObject childGo = childT != null
                ? childT.gameObject
                : new GameObject(WalkAreaChildName);
            if (childT == null)
                childGo.transform.SetParent(fieldGO.transform, false);

            var expanded = childGo.GetComponent<PolygonCollider2D>();
            if (expanded == null)
                expanded = childGo.AddComponent<PolygonCollider2D>();
            expanded.isTrigger = true;

            float sx = Mathf.Max(0.01f, Mathf.Abs(fieldGO.transform.lossyScale.x));
            float padLocal = Mathf.Max(0f, padWorld) / sx;

            expanded.pathCount = poly.pathCount;
            for (int p = 0; p < poly.pathCount; p++)
            {
                Vector2[] path = poly.GetPath(p);
                if (path == null || path.Length == 0)
                {
                    expanded.SetPath(p, path);
                    continue;
                }

                Vector2 center = Vector2.zero;
                for (int i = 0; i < path.Length; i++)
                    center += path[i];
                center /= path.Length;

                for (int i = 0; i < path.Length; i++)
                {
                    Vector2 d = path[i] - center;
                    float len = d.magnitude;
                    if (len > 1e-5f)
                        path[i] = center + d * ((len + padLocal) / len);
                }
                expanded.SetPath(p, path);
            }

            return expanded;
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
                SpawnMissingFromLayout(cfg, slots);
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
            slot.ApplyFixedDestination();

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

        /// <summary>
        /// 씬에 아직 없는 기물(v1.3 북제단·남제단 등)은 PropLayoutSettings 배치대로 만든다 — 씬을 다시 굽지 않아도 나오게.
        /// 크기는 씬에 있는 기물과 맞춘다.
        /// </summary>
        static void SpawnMissingFromLayout(Config cfg, PropSlot[] sceneSlots)
        {
            var layout = cfg.propLayout != null ? cfg.propLayout : PropLayoutSettings.Get();
            if (layout?.placements == null) return;
            var have = new System.Collections.Generic.HashSet<string>();
            Vector3 sceneScale = Vector3.one;
            foreach (var s in sceneSlots)
            {
                if (s == null) continue;
                if (s.data != null) have.Add(s.data.propId);
                sceneScale = s.transform.localScale;
            }
            float mapScale = Mathf.Max(0.01f, cfg.scale.mapScale);
            foreach (var place in layout.placements)
            {
                if (place?.data == null || place.prefab == null || have.Contains(place.data.propId)) continue;
                var slot = Object.Instantiate(place.prefab);
                slot.transform.position = MapToWorld(place.position, mapScale);
                slot.transform.localScale = sceneScale;
                if (slot.data == null) slot.data = place.data;
                ConfigureProp(slot, cfg.scale);
                slot.gameObject.name = "Prop_" + (!string.IsNullOrEmpty(slot.data.displayName) ? slot.data.displayName : slot.data.propId);
                have.Add(place.data.propId);
            }
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
