using System.Collections;
using UnityEngine;
using Yoegoe.Bootstrap;
using Yoegoe.Characters;
using Yoegoe.Core;
using Yoegoe.Data;
using Yoegoe.Economy;
using Yoegoe.Save;
using Yoegoe.UI;

namespace Yoegoe
{
    /// <summary>
    /// Main 씬 진입점. 카메라·맵·기물·캐릭터·HUD를 조립한다.
    /// Prefab: Assets/Prefabs/Bootstrap/Main.prefab
    /// 화면 크기: ArtScaleSettings.asset / 시작 재화·스탯: StartingStateSettings.asset
    /// 기물 밸런스: Data/Props/*.asset / 비주얼·배치: Prefabs/Props + Main 씬
    /// UI 색·글자: UiStyleSettings.asset
    /// </summary>
    public class Main : MonoBehaviour
    {
        [Header("실제 아트 연결 (없으면 캡슐로 대체 재생)")]
        [Tooltip("옥토끼 CharacterData (Walk Down/Left/Right/Up 스프라이트까지 채운 에셋)를 연결하면 " +
                 "캡슐 대신 실제 스프라이트로 만들고, CharacterAgent.Data도 이 실제 에셋을 그대로 사용한다.")]
        public CharacterData oktoData;
        [Tooltip("삼족오 CharacterData. 비워두면 삼족오는 검정 캡슐로 대체 재생된다.")]
        public CharacterData samjokOData;
        [Tooltip("구미호 CharacterData. 비워두면 구미호는 주황 캡슐로 대체 재생된다.")]
        public CharacterData gumihoData;
        [Tooltip("고라니 CharacterData (소환용). 비워두면 Resources/Characters/Gorani 를 찾는다.")]
        public CharacterData goraniData;

        [Header("화면 크기 (여기 말고 ArtScaleSettings.asset에서 조절)")]
        [Tooltip("비워두면 Resources/ArtScaleSettings 를 자동으로 찾는다. 맵·캐릭터·기물 배율은 그 에셋 하나에서 바꾼다.")]
        public ArtScaleSettings artScale;

        [Header("기물 카탈로그 (배치는 Main 씬 Transform)")]
        [Tooltip("Prefab 참조·FindByPropId·에디터 초기 배치용. 비워두면 Resources/PropLayoutSettings.")]
        public PropLayoutSettings propLayout;

        [Header("맵 배경 (씬 배치 후엔 Background_* Prefab이 소스)")]
        [Tooltip("전체 맵(섬 전경). 에디터 초기 배치에 사용.")]
        public Sprite overviewBackgroundSprite;
        [Tooltip("걷기 가능 플레이필드 레이어. 에디터 초기 배치에 사용.")]
        public Sprite playfieldSprite;
        [Tooltip("전체맵 위치 보정(플레이필드=원점일 때). mapScale=1 기준. 초기 배치 메뉴에만 사용.")]
        public Vector2 overviewOffset = new Vector2(-0.05f, -0.32f);

        [Header("HUD (상단 재화 바 + 하단 슬롯바)")]
        [Tooltip("한글 표시용 폰트. 비워두면 유니티 기본 폰트로 나오는데 한글이 깨질 수 있음 " +
                 "(Assets/Fonts/DOSGothic.ttf 연결 권장 — 프로젝트에 이미 있는 한글 폰트).")]
        public Font hudFont;
        [Tooltip("물 재화 칩에 쓸 아이콘 (Assets/Art/Offerings/Offering_Water 연결 권장).")]
        public Sprite waterIcon;
        [Tooltip("상세화면 하단 급여 바에 나열할 공양물 전체 목록 (Assets/Data/Offerings/*.asset 전부 연결).")]
        public OfferingData[] offerings;

        AppSession session;

        ArtScaleSettings _scale;
        ArtScaleSettings Scale
        {
            get
            {
                if (_scale != null) return _scale;
                if (artScale != null) return _scale = artScale;
                _scale = Resources.Load<ArtScaleSettings>("ArtScaleSettings");
                if (_scale == null)
                    _scale = ScriptableObject.CreateInstance<ArtScaleSettings>();
                return _scale;
            }
        }

        void Awake()
        {
            Time.maximumDeltaTime = 3600f;
            session = new AppSession(hudFont);

            var economyGO = new GameObject("GameEconomy");
            economyGO.AddComponent<GameEconomy>().ApplyStartingState(StartingStateSettings.Get());

            CharacterCatalog.EnsureLoaded();
            // 소환 대상 에셋 (3번째 슬롯 고라니 · 잠긴 4번째 슬롯 구미호)
            CharacterSummon.RegisterData(CharacterId.Gorani, goraniData);
            CharacterSummon.RegisterData(CharacterId.Gumiho, gumihoData);
            offerings = UiAssembler.EnsureOfferingsCatalog(offerings);
            CharacterCatalog.SetOfferings(offerings);
            ShopStock.SetCatalog(offerings);
            ShopStock.EnsureFresh(TrustedTime.UtcNow);

            WorldAssembler.Build(new WorldAssembler.Config
            {
                scale = Scale,
                propLayout = propLayout,
                overviewBackgroundSprite = overviewBackgroundSprite,
                playfieldSprite = playfieldSprite,
                overviewOffset = overviewOffset,
                oktoData = oktoData,
                samjokOData = samjokOData,
                hudFont = hudFont,
            });

            UiAssembler.WireHud(new UiAssembler.Config
            {
                hudFont = hudFont,
                waterIcon = waterIcon,
                offerings = offerings,
                goraniData = goraniData,
            });

            // 인트로 중 HUD가 하늘을 가리지 않게
            if (MapIntro.Instance != null && MapIntro.Instance.IsPrepared && GameHud.Instance != null)
                GameHud.Instance.gameObject.SetActive(false);
        }

        IEnumerator Start()
        {
            yield return null;
            BootSanityCheck.Run(hudFont);
            UiAssembler.ForceCloseOverlayScreens();
            GameSaveBridge.TryLoadSimulateAndApply(hudFont);

            // 로딩 오버레이를 걷히며 Overview 하늘 → 아래로 팬
            session.HideWebGlLoadingOverlay();
            if (MapIntro.Instance != null && MapIntro.Instance.IsPrepared)
                yield return MapIntro.Instance.PlayReveal();

            session.MarkReady();
            session.TryOpenAttendanceIfDue();
        }

        void Update() => session?.Tick();

        void OnApplicationPause(bool pause) => session?.OnPause(pause);

        void OnApplicationFocus(bool hasFocus) => session?.OnFocus(hasFocus);

        void OnApplicationQuit() => session?.OnQuit();
    }
}
