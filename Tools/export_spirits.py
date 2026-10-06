#!/usr/bin/env python3
"""나루터 혼령 대사: 대사 시트 탭 spirit_lines ⇄ Assets/Resources/spirit_lines.json

  spirit_lines  kind, type, text_ko, note
                kind: woman(여자) · man(남자) · elder(노인) · child(어린이)
                type: ask(주문 — {dish} 자리에 요리 이름) · thanks(대접받았을 때) · bye(그냥 떠날 때)
                같은 kind·type 이 여러 줄이면 그중 랜덤.

사용법:
  python3 Tools/export_spirits.py            # 시트 → spirit_lines.json   (npm run spirits)
  python3 Tools/export_spirits.py --csv      # Tools/sheets/spirit_lines.csv → spirit_lines.json
  python3 Tools/export_spirits.py --push     # Tools/sheets/spirit_lines.csv → 시트 탭 덮어쓰기 (npm run spirits:push)
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from export_characters import load_config, push_tab, read_csv_text  # noqa: E402
from export_yut_bubbles import fetch_sheet_csv  # noqa: E402
from sheets_config import sheet_id_for  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
TAB = "spirit_lines"
HEADERS = ["kind", "type", "text_ko", "note"]
JSON_PATH = ROOT / "Assets" / "Resources" / "spirit_lines.json"
CSV_PATH = ROOT / "Tools" / "sheets" / "spirit_lines.csv"
KINDS = ["woman", "man", "elder", "child"]   # SpiritKind 순서
TYPES = ["ask", "thanks", "bye"]


def rows_to_json(rows: list[dict]) -> tuple[dict, list[str]]:
    errors, out = [], []
    for r in rows:
        where = f"[{TAB}] {r['_row']}행"
        kind, typ, text = r.get("kind", ""), r.get("type", ""), r.get("text_ko", "")
        if kind not in KINDS:
            errors.append(f"{where}: kind 는 {', '.join(KINDS)} 중 하나 ('{kind}')")
            continue
        if typ not in TYPES:
            errors.append(f"{where}: type 은 {', '.join(TYPES)} 중 하나 ('{typ}')")
            continue
        if not text:
            continue
        if typ == "ask" and "{dish}" not in text:
            errors.append(f"{where}: 주문(ask) 대사에는 {{dish}} 가 있어야 함 ('{text}')")
            continue
        out.append({"kind": kind, "type": typ, "text": text})
    for k in KINDS:
        for t in TYPES:
            if not any(l["kind"] == k and l["type"] == t for l in out):
                print(f"경고: [{TAB}] {k} · {t} 대사가 없음 — 기본 대사를 씀", file=sys.stderr)
    return {"lines": out}, errors


def main() -> int:
    ap = argparse.ArgumentParser(description="혼령 대사 시트 ⇄ spirit_lines.json")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--csv", action="store_true")
    mode.add_argument("--push", action="store_true")
    args = ap.parse_args()
    config = load_config()

    if args.push:
        rows = list(csv.DictReader(CSV_PATH.open(encoding="utf-8")))
        push_tab(config, TAB, HEADERS, rows)
        return 0

    if args.csv:
        text = CSV_PATH.read_text(encoding="utf-8-sig")
    else:
        text = fetch_sheet_csv(sheet_id_for(config, TAB), TAB)
    data, errors = rows_to_json(read_csv_text(text, ["kind", "type"], TAB))
    if errors:
        print("시트 오류 — spirit_lines.json 을 쓰지 않았습니다:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        return 1
    JSON_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {JSON_PATH.relative_to(ROOT)} (대사 {len(data['lines'])})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
