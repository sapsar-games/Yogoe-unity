#!/usr/bin/env python3
"""게임 전역 수치: 구글 시트 탭 game_settings ⇄ Assets/Resources/game_settings.json

시트 = 윷 말풍선 시트(Tools/yut_bubbles_sheets.config.json 의 sheet_id)의 탭 1개:
  game_settings  key, value, note
                 한 줄 = 값 하나. key 는 코드와 연결 — 바꾸지 마세요. 비우거나 줄을 지우면 코드 기본값(v1.3).

사용법:
  python3 Tools/export_settings.py            # 시트 → game_settings.json   (npm run settings)
  python3 Tools/export_settings.py --csv      # Tools/sheets/game_settings.csv → game_settings.json
  python3 Tools/export_settings.py --to-csv   # game_settings.json → Tools/sheets/game_settings.csv
  python3 Tools/export_settings.py --push     # game_settings.json → CSV + 시트 탭 덮어쓰기 (npm run settings:push)
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from export_characters import load_config, push_tab, read_csv_text, write_csv  # noqa: E402
from export_yut_bubbles import fetch_sheet_csv  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
TAB = "game_settings"
HEADERS = ["key", "value", "note"]
JSON_PATH = ROOT / "Assets" / "Resources" / "game_settings.json"
CSV_PATH = ROOT / "Tools" / "sheets" / "game_settings.csv"

# key → (기본값 v1.3, 설명). GameSettings.cs 의 기본값과 같다.
KEYS: dict[str, tuple[float, str]] = {
    "foodStamina": (10, "음식 한 그릇 기력"),
    "offeringStamina": (15, "공양물 기력 (선호 공양물도 같음)"),
    "offeringIntimacy": (1, "공양물 친밀도"),
    "preferredIntimacy": (5, "선호 공양물 친밀도"),
    "foodRequestMargin": (10, "기력이 (최대 기력 − 이 값) 이하면 음식 요구"),
    "requestFulfillBonus": (4, "음식 요구를 채우면 음식 기력에 더하는 값 (v1.3 그릇 말풍선으로 바뀌면 없어짐)"),
    "guestPerfectStaminaMul": (2, "주문 요괴 · 김 오를 때 기력 = 공양물 기력 × 이 값"),
    "guestPerfectIntimacyMul": (2, "주문 요괴 · 김 오를 때 친밀도 = 선호 친밀도 × 이 값"),
    "guestCoolStaminaMul": (2, "주문 요괴 · 식은 뒤 기력 = 공양물 기력 × 이 값"),
    "guestCoolIntimacyMul": (1, "주문 요괴 · 식은 뒤 친밀도 = 선호 친밀도 × 이 값"),
    "spiritPiecesFood": (1, "혼령에게 음식을 주면 받는 기억 조각 (나루터)"),
    "spiritPiecesOffering": (2, "혼령에게 공양물을 주면 받는 기억 조각 (나루터)"),
}


def fmt(v: float) -> str:
    return str(int(v)) if float(v).is_integer() else str(v)


def derived_note(key: str, values: dict[str, float]) -> str:
    """주문 요괴 배수 줄에는 계산된 값을 같이 적는다."""
    st, pi = values["offeringStamina"], values["preferredIntimacy"]
    calc = {
        "guestPerfectStaminaMul": f"→ 기력 +{fmt(st * values['guestPerfectStaminaMul'])}",
        "guestPerfectIntimacyMul": f"→ 친밀도 +{fmt(pi * values['guestPerfectIntimacyMul'])}",
        "guestCoolStaminaMul": f"→ 기력 +{fmt(st * values['guestCoolStaminaMul'])}",
        "guestCoolIntimacyMul": f"→ 친밀도 +{fmt(pi * values['guestCoolIntimacyMul'])}",
        "requestFulfillBonus": f"→ 요구 채우면 기력 +{fmt(values['foodStamina'] + values['requestFulfillBonus'])}",
    }.get(key)
    return f"{KEYS[key][1]} {calc}" if calc else KEYS[key][1]


def rows_to_json(rows: list[dict]) -> tuple[dict, list[str]]:
    errors, out = [], {k: v for k, (v, _) in KEYS.items()}
    seen = set()
    for r in rows:
        where = f"[{TAB}] {r['_row']}행"
        key = r.get("key", "")
        if key not in KEYS:
            errors.append(f"{where}: 모르는 key '{key}' (쓸 수 있는 key: {', '.join(KEYS)})")
            continue
        if key in seen:
            errors.append(f"{where}: key 중복 ({key})")
            continue
        seen.add(key)
        raw = (r.get("value") or "").strip().replace(",", "")
        if not raw:
            continue  # 비우면 기본값
        try:
            v = float(raw)
        except ValueError:
            errors.append(f"{where}: value 는 숫자 ('{raw}')")
            continue
        if v < 0:
            errors.append(f"{where}: value 는 0 이상")
            continue
        out[key] = v
    return {"settings": [{"key": k, "value": v} for k, v in out.items()]}, errors


def json_rows() -> list[dict]:
    values = {k: v for k, (v, _) in KEYS.items()}
    if JSON_PATH.exists():
        for e in json.loads(JSON_PATH.read_text(encoding="utf-8")).get("settings", []):
            if e.get("key") in values:
                values[e["key"]] = e["value"]
    return [{"key": k, "value": fmt(values[k]), "note": derived_note(k, values)} for k in KEYS]


def main() -> int:
    ap = argparse.ArgumentParser(description="게임 전역 수치 시트 ⇄ game_settings.json")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--csv", action="store_true")
    mode.add_argument("--to-csv", action="store_true")
    mode.add_argument("--push", action="store_true")
    args = ap.parse_args()
    config = load_config()

    if args.to_csv or args.push:
        rows = json_rows()
        write_csv(CSV_PATH, HEADERS, rows)
        print(f"Wrote {CSV_PATH.relative_to(ROOT)} ({len(rows)}줄)")
        if args.push:
            if not config.get("write_url"):
                print("config 에 write_url 이 없습니다", file=sys.stderr)
                return 1
            push_tab(config, TAB, HEADERS, rows)
        return 0

    if args.csv:
        text = CSV_PATH.read_text(encoding="utf-8-sig")
    else:
        if not config.get("sheet_id"):
            print("config 에 sheet_id 가 없습니다.", file=sys.stderr)
            return 1
        text = fetch_sheet_csv(config["sheet_id"], TAB)
    data, errors = rows_to_json(read_csv_text(text, ["key"], TAB))
    if errors:
        print("시트 오류 — game_settings.json 을 쓰지 않았습니다:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        return 1
    JSON_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {JSON_PATH.relative_to(ROOT)} ({len(data['settings'])}개)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
