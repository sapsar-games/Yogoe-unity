# 한 폭의 요괴 (Scroll of Yoegoe)

한국 설화 기반 **방치형(유휴) 육성 시뮬레이션** 모바일 게임입니다.
(기획문서 "한폭요괴 1.0" — MVP 3차 + v1.2/v1.3 기물·공양간·나루터 통합 기획 기준)

족자 위에서 요괴들이 스스로 돌아다니며 기물을 사용해 공덕·자원을 생산하고,
공양으로 친밀도·기력을 관리하는 육성 루프입니다. 윷놀이·공양간 요리·상점·나루터 혼령·
출석 윷점 등 서브 콘텐츠를 갖춘 상태로 WebGL 데모가 계속 업데이트되고 있습니다.

## 현재 진행

- [x] BigNumber 무한 자릿수 재화 시스템 (ㄱㄴㄷ...ㅎ → ㄱㄱ,ㄴㄴ 순환 단위)
- [x] 캐릭터 행동 상태머신(걷기·머물기·놀기·기절) · 혼잣말·요구 대사 · 드래그 착석
- [x] 기물 구매·업그레이드·생산(공덕/물/사냥·채집 재료) · 보관 확장 · 개별/일괄 수거
- [x] 공양(상세 화면) · 친밀도·기력 · 랜덤 음식 요구 · 선물꾸러미
- [x] 소환(향으로 빈 슬롯/잠긴 슬롯 개방)
- [x] 윷놀이 — 보드·확률표·대전 AI·특수칸·보물상자·완주 부적·귀환 수거까지 완결
- [x] 공양간 요리 미니게임(5×5 판) · 레시피 76종(음식40·공양물36) · 부적 6종 · 요리책(도감)
- [x] 상점(고가구점) — 진열 로테이션, 공덕으로 리셋
- [x] 북제단·남제단(탭으로 공덕 수거) · 나루터(혼령 접대 → 기억 조각)
- [x] 출석 윷점 (KST 새벽 4시 리셋, 64괘)
- [x] 로컬 세이브 + 오프라인 정산(기력 소모·생산·보관 동일 규칙)
- [x] WebGL 빌드 → GitHub Pages 자동 배포 (push 시 CI)
- [ ] 사운드(목탁·엽전·풍경·나무·가야금) — 음원 미보유
- [ ] 서버 시각(Firebase) 연동 — 연결 지점만 준비(`TrustedTime`), 로컬 시계로 동작 중
- [ ] 광고 SDK 실연동 — 지금은 전부 스텁(즉시 성공 처리)

세부 항목별 ✅/⚠️/❌ 현황은 [`Docs/02_개발진행.md`](Docs/02_개발진행.md), 기획 요약은
[`Docs/00_기획정리.md`](Docs/00_기획정리.md), 미확정 설계 이슈는
[`Docs/05_기획_미확정사항.md`](Docs/05_기획_미확정사항.md)을 참고하세요.

## 요구 환경

- **Unity** `6000.3.11f1` (Unity 6)
- 플랫폼 목표: Android / iOS

## 웹 데모 (GitHub Pages)

`main` push 시 WebGL 자동 빌드·배포.

**플레이:** https://sapsar-games.github.io/Yogoe-unity/

최초 1회 [CI Secrets·Pages 설정](Docs/04_CI_배포.md) 필요. (기존 설계에서 이미
설정을 마쳤고 이번 교체로 영향받지 않으므로 재설정 불필요.)

## 실행 방법 (로컬)

1. Unity Hub에서 이 폴더를 연다.
2. `Assets/Scenes/Main.unity` 를 연다.
3. Play.

시작 재화·스탯: `Assets/Resources/StartingStateSettings.asset`  
화면 크기: `Assets/Resources/ArtScaleSettings.asset`

## 폴더 구조

```
Assets/
  Scripts/
    Core/            BigNumber, TrustedTime, CeremonyGate, SpeechGate
    Data/            Enums, Character/Prop/Offering/Settings Data (ScriptableObject)
    Characters/      CharacterAgent(상태머신·드래그·공양·연출), PropSlot, PropManager, MapPointerRouter
    Bootstrap/       AppSession, WorldAssembler, UiAssembler, MapIntro — Main 씬 조립
    Economy/         GameEconomy, PropStorage/Production, Attendance, ShopStock, SpiritPier(나루터) 등
    Cooking/         공양간 요리 세션·레시피·도감·손님 주문
    Save/            GameSaveService/Bridge/Migration, OfflineSimulator
    UI/              GameHud, DetailScreen, GongyangganScreen, PierScreen, ShopScreen, YutScreen 등 화면 전체
    Minigames/Yut/   윷놀이
    Debugging/       MapCameraDrag
  Resources/         StartingStateSettings.asset, ArtScaleSettings.asset, props.json 등 ← 숫자 조절
  Data/              Characters/, Offerings/, Props/ (.asset)
  Scenes/Main.unity
Docs/
  00_기획정리.md          기획 확정 스펙 요약
  02_개발진행.md          장별 구현 현황(✅/⚠️/❌)
  03_백엔드_설계.md       신뢰 시각(오프라인 정산) 설계
  04_CI_배포.md           WebGL → GitHub Pages CI
  05_기획_미확정사항.md   아직 안 정해진 것
  06_행동룰.md            캐릭터 행동 상태 상세
  07_탭_인터랙션_경우의수.md  탭/드래그 입력 표(3차 기준)
  08_유저인터랙션_v1.3.md    탭/드래그 입력 표(v1.2→v1.3 변경분)
  09_v1.2_시연_역기획.md     v1.2 시연 역기획 노트
  코드정리.md             비개발자용 코드 공부 노트
```

### 윷놀이 미니게임 (`Minigames/Yut/`)

보드 이동·확률표·대전 AI·특수칸·보물상자·완주 부적·귀환 수거까지 동작 중인 완결 콘텐츠.

| 파일 | 역할 |
|------|------|
| `YutMiniGame.cs` + `YutMiniGame.*.cs` | 보드 UI·윷 연출·말/후보·HUD·보상 (part class로 분리) |
| `YutMatch.cs` | 한 판 상태·이동 미리보기 |
| `YutBoardLayout.cs` | 전통 윷판 29발 좌표 + 특수칸 |
| `YutMoveResolver.cs` | 도/개/걸/윷/모/빽도 → 경로 |
| `YutThrowRoller.cs` | 확률표 RNG |
| `YutRewards.cs` | 보물상자·완주 부적 추첨 |
| `YutChallenge.cs` / `YutChallengePresenter.cs` | 이무기 대전 판정·연출 |

말풍선 문구: `Assets/Resources/Yut/yut_bubbles.{locale}.json`  
카탈로그: `Assets/Scripts/Data/YutBubbleCatalog.cs`

## Google Sheets 동기화

기획 수치·대사는 코드가 아니라 구글 시트 두 개(밸런스 시트 · 대사 시트)가 정본입니다.
탭별로 `npm run <이름>` (`characters` / `props` / `attendance` / `yut-bubbles` / `recipes` /
`ingredients` / `charms` / `settings` / `spirits`) 또는 전체 한 번에 `npm run sheets`.
각 명령은 `:csv`(오프라인 변환) · `:push`(시트에 쓰기) 변형도 있습니다. 시트 탭 ↔ 로컬 파일
연결은 `Tools/sheets_config.py` 참고.

**가져오기**는 시트를 `링크 있는 모든 사용자: 뷰어`로 두면 됩니다 (공개 CSV).

**쓰기(`:push`)** 최초 1회:

1. 해당 스프레드시트 → 확장 프로그램 → Apps Script
2. `Tools/YutBubblesSheetsWrite.gs` 전체 붙여넣기 → 저장
3. 배포 → 웹 앱 / 실행: 나 / 액세스: **모든 사용자**
4. `/exec` URL을 config `write_url`에 저장 (코드 수정 후에는 **새 버전**으로 재배포)
5. `npm run yut-bubbles:push`

`write_token`은 선택. 쓸 때만 Apps Script 스크립트 속성 `WRITE_TOKEN`과 같은 임의 비밀을 넣습니다 (배포 URL의 `AKfycb…`가 아님).

## 라이선스

비공개 개발용. (추후 명시)
