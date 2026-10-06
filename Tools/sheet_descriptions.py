#!/usr/bin/env python3
"""구글 시트 각 탭 1행 설명(※ …) — 기획자용 안내.

- 변환 스크립트(export_*.py)는 헤더 행을 찾을 때까지 위쪽 줄을 건너뛰므로 설명 줄이 있어도 된다.
- push(json → 시트) 할 때 이 설명이 자동으로 1행에 다시 들어간다.

사용법:
  python3 Tools/sheet_descriptions.py --apply    # 시트 내용은 그대로 두고 1행 설명만 넣기/갱신 (npm run sheets:describe)
"""

from __future__ import annotations

import argparse
import csv
import io
import json
import sys
from pathlib import Path

MARK = "※"

_LINES: dict[str, list[str]] = {
    "charms": [
        "※ 윷 말 완주 보상 부적 확률",
        "- 한 줄 = 부적 1종. 말 1개가 완주할 때 weight 비율로 1개가 나옴 (전부 1이면 6종 균등, 0 = 안 나옴)",
        "- 예: 몰빵 weight 0.5, 나머지 1 → 몰빵 9%, 나머지 각 18%",
        "- 나가리도 소모품 — 가진 개수만큼만 요리 중 쓸 수 있음",
        "- id 는 코드와 연결되니 바꾸지 마세요",
        "- 수정 후: npm run charms (또는 npm run sheets)",
    ],
    "recipes": [
        "※ 공양간 레시피 (요리판에서 실제로 쓰는 조합)",
        "- 한 줄 = 조합 1개. 같은 id 를 여러 줄 쓰면 같은 요리의 다른 조합 (예: 고기죽 = 쌀+새고기 / 쌀+멧돼지고기)",
        "- kind: 음식(재료 2개) / 공양물(재료 3개)",
        "- 재료는 ingredients 탭 name 의 한글 이름 (순서 상관없음, 같은 재료 2번 가능)",
        "- id 는 영문 — 이미 있는 요리의 id 를 바꾸면 그 요리 인벤·도감 기록이 사라짐",
        "- 같은 재료 조합이 두 요리에 있으면 오류",
        "- description = 요리책 상세 설명. 요리마다 한 줄에만 쓰고 같은 id 의 다른 줄은 비워 두세요 (비면 효과만 나옴)",
        "- 수정 후: npm run recipes",
    ],
    "ingredients": [
        "※ 요리책 재료 칸 설명",
        "- 한 줄 = 재료 1개 (재료 13 + 황금쌀·황금꿀 + 황금 재료 6종)",
        "- name = 게임에 보이는 재료 이름 (비우면 기본 이름). description = 요리책에서 재료 칸을 눌렀을 때 상세 설명",
        "- id 는 코드와 연결 — 바꾸지 마세요. 요리 설명은 recipes 탭 description",
        "- 이름을 바꾸면 recipes 탭 재료 칸도 새 이름으로 (직전 기본 이름도 당분간 받아 줌)",
        "- 수정 후: npm run ingredients → npm run recipes (또는 npm run sheets)",
    ],
    "characters": [
        "※ 캐릭터 기본 정보",
        "- 한 줄 = 요괴 1명",
        "- id(Rabbit / SamjokO / Gumiho / Gorani)는 코드와 연결되니 바꾸지 마세요",
        "- endingPropId = 엔딩기물 이름 (없으면 비움)",
        "- note = 메모 (게임에 안 들어감)",
        "- 수정 후: npm run characters",
    ],
    "character_preferences": [
        "※ 요괴별 선호 공양물",
        "- 한 줄 = 선호 1개 (위에서부터 표시 순서)",
        "- offering_id = 공양간 레시피 id (예: sinseollo, hwachae, yakju)",
        "- 줄이 하나도 없는 요괴 = 선호 없음 (상세 화면에 X 표시)",
        "- 수정 후: npm run characters",
    ],
    "character_lines": [
        "※ 요괴 대사",
        "- 한 줄 = 대사 1개. 같은 요괴·같은 type이 여러 줄이면 그중 랜덤",
        "- type",
        "    monologue = 혼잣말",
        "    request_thanks = 음식 요구 완료",
        "    request_gift = 선물꾸러미 줄 때",
        "    golden_find = 황금 재료 수거 ({item} 자리에 황금쌀/황금꿀)",
        "    greeting = 접속 인사 (앱을 켜거나 5분 넘게 비웠다 돌아왔을 때, 놀고 있던 요괴)",
        "- 수정 후: npm run characters",
    ],
    "props": [
        "※ 기물별 산출",
        "- resourceType: Merit(공덕) / Water(물) / Yeopjeon(엽전) / Hunt(사냥 재료) / Gather(채집 재료) / None(없음)",
        "- 자원 기물: cycleMinutes = 1개 만드는 주기(분, 레벨 무관) / baseCapacity = Lv1 보관 (10레벨마다 +1)",
        "- 공덕 기물: 분당 meritPerMinute × levelGrowth^(레벨−1) / meritCapacityMinutes분치 쌓이면 만창",
        "- intimacyBonus(친밀도 보정), upgradable(레벨업 가능) = TRUE / FALSE",
        "- 수정 후: npm run props",
    ],
    "prop_drop_tables": [
        "※ 활터(Hunt) · 약초밭(Gather) 재료 확률",
        "- weight = 확률 % (표마다 합 100 권장)",
        "- ingredient: Rice 쌀 / Namul 산나물 / Fruit 과실 / Chili 고추 / Herb 약재 / RedBean 팥",
        "              Egg 새알 / Oil 기름 / Fish 물고기 / Boar 멧돼지고기 / Bird 새고기 / Honey 꿀",
        "              GoldenRice 황금쌀 / GoldenHoney 황금꿀",
        "- 수정 후: npm run props",
    ],
    "prop_settings": [
        "※ 기물 전역 값",
        "- 기물 구매 비용 = purchaseBaseCost × purchaseCostGrowth^(n−1)  (n = 몇 번째 구매인지)",
        "- 수정 후: npm run props",
    ],
    "attendance": [
        "※ 출석 윷점 일차별 엽전",
        "- 한 줄 = 하루 (1일차부터 순서대로)",
        "- 마지막 날 다음은 1일차로 돌아감",
        "- 수정 후: npm run attendance",
    ],
    "yut_fortune": [
        "※ 옥토끼 윷점 64괘 (기획서 부록 B)",
        "- gua = 도·개·걸 형식 (윷점에서 윷 = 모)",
        "- text_work(일) / text_people(사람) / text_heart(마음) 중 하나가 랜덤으로 나옴 (태그는 플레이어에게 안 보임)",
        "- 64괘가 모두 있어야 함",
        "- 수정 후: npm run attendance",
    ],
    "yut_bubbles": [
        "※ 윷놀이 말풍선",
        "- id는 코드와 연결되니 바꾸지 마세요",
        "- text_ko / text_en / text_zh / text_ja = 언어별 문구",
        "- {result}, {steps} 같은 자리표시는 그대로 두세요",
        "- 수정 후: npm run yut-bubbles",
    ],
}

# 한 칸 안에 줄바꿈으로 여러 줄
DESCRIPTIONS: dict[str, str] = {tab: "\n".join(lines) for tab, lines in _LINES.items()}


def description_row(tab: str, width: int) -> list[str] | None:
    desc = DESCRIPTIONS.get(tab)
    if not desc:
        return None
    return [desc] + [""] * max(0, width - 1)


def strip_description(text: str, required: list[str]) -> str:
    """CSV에서 헤더 행(required 열을 모두 가진 첫 행) 위의 설명 줄들을 떼어낸다."""
    rows = list(csv.reader(io.StringIO(text)))
    for i, row in enumerate(rows):
        cells = {c.strip() for c in row}
        if all(h in cells for h in required):
            out = io.StringIO()
            csv.writer(out, lineterminator="\n").writerows(rows[i:])
            return out.getvalue()
    return text


def main() -> int:
    ap = argparse.ArgumentParser(description="시트 탭 1행 설명 넣기")
    ap.add_argument("--apply", action="store_true", help="시트에 반영")
    args = ap.parse_args()
    if not args.apply:
        for tab, d in DESCRIPTIONS.items():
            print(f"[{tab}] {d}")
        return 0

    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from export_characters import load_config
    from export_yut_bubbles import fetch_sheet_csv
    from import_yut_bubbles_csv import _post_json

    config = load_config()
    sid, url = config.get("sheet_id"), config.get("write_url")
    if not sid or not url:
        print("config 에 sheet_id / write_url 필요", file=sys.stderr)
        return 1

    for tab, desc in DESCRIPTIONS.items():
        rows = list(csv.reader(io.StringIO(fetch_sheet_csv(sid, tab))))
        while rows and rows[0] and rows[0][0].startswith(MARK):
            rows.pop(0)  # 기존 설명 교체
        if not rows:
            print(f"[{tab}] 비어 있음 — 건너뜀")
            continue
        width = max(len(r) for r in rows)
        rows = [r + [""] * (width - len(r)) for r in rows]
        payload = {"tab": tab, "spreadsheetId": sid,
                   "headers": description_row(tab, width), "rows": rows}
        if config.get("write_token"):
            payload["token"] = config["write_token"]
        res = json.loads(_post_json(url, json.dumps(payload, ensure_ascii=False).encode("utf-8")))
        if not res.get("ok"):
            print(f"[{tab}] 실패: {res.get('error')}", file=sys.stderr)
            return 1
        print(f"[{tab}] 설명 넣음 ({len(rows)}행 유지)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
