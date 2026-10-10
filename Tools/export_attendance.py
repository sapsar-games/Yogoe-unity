#!/usr/bin/env python3
"""출석 윷점: 구글 시트(2개 탭) ⇄ Assets/Resources/attendance.json

시트 = 윷 말풍선 시트(Tools/yut_bubbles_sheets.config.json 의 sheet_id)에 탭 2개:
  attendance   day, yeopjeon, note                                  (1~7일차 엽전, 행 수 = 순환 일수)
  yut_fortune  gua, name, text_work, text_people, text_heart, note  (64괘 — 부록 B)
               gua: "도·개·걸" (윷점에서 윷=모). text_* 는 내부 태그 1=일 2=사람 3=마음(플레이어 비노출)

사용법:
  python3 Tools/export_attendance.py              # 시트 → attendance.json   (npm run attendance)
  python3 Tools/export_attendance.py --csv        # Tools/sheets/*.csv → attendance.json
  python3 Tools/export_attendance.py --to-csv     # attendance.json → Tools/sheets/*.csv
  python3 Tools/export_attendance.py --push       # attendance.json → CSV + 시트 탭 덮어쓰기
"""

from __future__ import annotations

import argparse
import itertools
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from export_characters import load_config, push_tab, read_csv_text, write_csv  # noqa: E402
from export_yut_bubbles import fetch_sheet_csv  # noqa: E402
from sheets_config import sheet_id_for  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
JSON_PATH = ROOT / "Assets" / "Resources" / "attendance.json"
SHEETS_DIR = ROOT / "Tools" / "sheets"

TAB_DAYS = "attendance"
TAB_GUA = "yut_fortune"
DAY_HEADERS = ["day", "yeopjeon", "note"]
GUA_HEADERS = ["gua", "name", "text_work", "text_people", "text_heart", "note"]
LINE_COLS = ["text_work", "text_people", "text_heart"]

THROWS = "도개걸윷"
ALL_GUA = ["·".join(p) for p in itertools.product(THROWS, repeat=3)]  # 인덱스 = 도0개1걸2윷3 4진수


def rows_to_json(days: list[dict], guas: list[dict]) -> tuple[dict, list[str]]:
    errors: list[str] = []
    rewards = []
    for i, r in enumerate(days, start=1):
        where = f"[{TAB_DAYS}] {r['_row']}행"
        try:
            day = int(r.get("day", ""))
        except ValueError:
            errors.append(f"{where}: day 는 정수")
            continue
        if day != i:
            errors.append(f"{where}: day 는 1부터 순서대로 ({i} 기대, '{day}')")
        try:
            n = int(r.get("yeopjeon", ""))
        except ValueError:
            errors.append(f"{where}: yeopjeon 은 정수 ('{r.get('yeopjeon')}')")
            continue
        if n < 0:
            errors.append(f"{where}: yeopjeon 은 0 이상")
        rewards.append(n)
    if not rewards:
        errors.append(f"[{TAB_DAYS}] 최소 1일치 필요")

    by_gua: dict[str, dict] = {}
    for r in guas:
        where = f"[{TAB_GUA}] {r['_row']}행"
        gua = r.get("gua", "").replace(".", "·").replace(" ", "")
        if gua not in ALL_GUA:
            errors.append(f"{where}: gua 는 '도·개·걸' 형식 ('{r.get('gua')}')")
            continue
        if gua in by_gua:
            errors.append(f"{where}: gua 중복 ({gua})")
            continue
        lines = [r.get(c, "") for c in LINE_COLS]
        if not any(lines):
            errors.append(f"{where}: text_work/people/heart 중 하나는 있어야 함 ({gua})")
        by_gua[gua] = {"gua": gua, "name": r.get("name", ""), "lines": lines}
    missing = [g for g in ALL_GUA if g not in by_gua]
    if missing:
        errors.append(f"[{TAB_GUA}] 빠진 괘 {len(missing)}개: {', '.join(missing[:8])}{' …' if len(missing) > 8 else ''}")

    fortunes = [by_gua.get(g, {"gua": g, "name": "", "lines": ["", "", ""]}) for g in ALL_GUA]
    return {"rewards": rewards, "fortunes": fortunes}, errors


def json_to_rows(data: dict) -> tuple[list[dict], list[dict]]:
    days = [{"day": str(i), "yeopjeon": str(n)} for i, n in enumerate(data.get("rewards", []), start=1)]
    guas = []
    for f in data.get("fortunes", []):
        lines = (f.get("lines") or []) + ["", "", ""]
        guas.append({"gua": f.get("gua", ""), "name": f.get("name", ""),
                     "text_work": lines[0], "text_people": lines[1], "text_heart": lines[2]})
    return days, guas


def main() -> int:
    ap = argparse.ArgumentParser(description="출석 윷점 시트 ⇄ attendance.json")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--csv", action="store_true")
    mode.add_argument("--to-csv", action="store_true")
    mode.add_argument("--push", action="store_true")
    args = ap.parse_args()
    config = load_config()
    tabs = [(TAB_DAYS, DAY_HEADERS, ["day"]), (TAB_GUA, GUA_HEADERS, ["gua"])]
    paths = {t: SHEETS_DIR / f"{t}.csv" for t, _, _ in tabs}

    if args.to_csv or args.push:
        rows = json_to_rows(json.loads(JSON_PATH.read_text(encoding="utf-8")))
        for (tab, headers, _), r in zip(tabs, rows):
            write_csv(paths[tab], headers, r)
        print(f"Wrote Tools/sheets/{{{TAB_DAYS},{TAB_GUA}}}.csv")
        if args.push:
            if not config.get("write_url"):
                print("config 에 write_url 이 없습니다", file=sys.stderr)
                return 1
            for (tab, headers, _), r in zip(tabs, rows):
                push_tab(config, tab, headers, r)
        return 0

    parsed = []
    for tab, _, required in tabs:
        if args.csv:
            text = paths[tab].read_text(encoding="utf-8-sig")
        else:
            if not config.get("sheet_id"):
                print("config 에 sheet_id 가 없습니다.", file=sys.stderr)
                return 1
            text = fetch_sheet_csv(sheet_id_for(config, tab), tab)
        parsed.append(read_csv_text(text, required, tab))

    data, errors = rows_to_json(*parsed)
    if errors:
        print("시트 오류 — attendance.json 을 쓰지 않았습니다:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        return 1
    JSON_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {JSON_PATH.relative_to(ROOT)} (출석 {len(data['rewards'])}일, 괘 {len(data['fortunes'])})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
