#!/usr/bin/env python3
"""요리책 재료 칸 설명: 구글 시트 탭 ingredients ⇄ Assets/Resources/ingredients.json

시트 = 윷 말풍선 시트(Tools/yut_bubbles_sheets.config.json 의 sheet_id)의 탭 1개:
  ingredients  id, name, description, note
               한 줄 = 재료 1개 (재료 13 + 황금쌀·황금꿀 + v1.2 황금 재료 6종).
               name = 게임에 보이는 재료 이름 (비우면 기본 이름), description = 요리책 설명.
               id 는 코드와 연결 — 바꾸지 마세요. 이름을 바꾸면 recipes 탭 재료 칸도 새 이름으로.
               (요리 설명은 recipes 탭 description)

사용법:
  python3 Tools/export_ingredients.py            # 시트 → ingredients.json   (npm run ingredients)
  python3 Tools/export_ingredients.py --csv      # Tools/sheets/ingredients.csv → ingredients.json
  python3 Tools/export_ingredients.py --to-csv   # ingredients.json → Tools/sheets/ingredients.csv
  python3 Tools/export_ingredients.py --push     # ingredients.json → CSV + 시트 탭 덮어쓰기 (npm run ingredients:push)
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from export_characters import load_config, push_tab, read_csv_text, write_csv  # noqa: E402
from export_yut_bubbles import fetch_sheet_csv  # noqa: E402
from sheets_config import sheet_id_for  # noqa: E402
from recipes_data import DEFAULT_INGREDIENTS, ROOT, current_id  # noqa: E402

TAB = "ingredients"
HEADERS = ["id", "name", "description", "note"]
JSON_PATH = ROOT / "Assets" / "Resources" / "ingredients.json"
CSV_PATH = ROOT / "Tools" / "sheets" / "ingredients.csv"
# 재료 칸 = CookingIngredientId 순서 + 특수 수집품 (CodexScreen 재료 칸 순서와 같다)
CELLS = DEFAULT_INGREDIENTS + [("GoldenRice", "황금쌀"), ("GoldenHoney", "황금꿀")]
# v1.2 황금 재료 6종 — 시트에 먼저 들어온 id. 게임 쪽 황금 개편 전까지는 설명만 보관한다.
CELLS += [("GoldenNamul", "황금 산나물"), ("GoldenHerb", "황금 약재"), ("GoldenChili", "황금 고추"),
          ("GoldenFish", "황금 해산물"), ("GoldenBird", "황금 새고기"), ("GoldenEgg", "황금 새알")]


def rows_to_json(rows: list[dict]) -> tuple[dict, list[str]]:
    known = dict(CELLS)
    errors, seen, out = [], set(), []
    for r in rows:
        where = f"[{TAB}] {r['_row']}행"
        iid = current_id(r.get("id", ""))
        if iid not in known:
            errors.append(f"{where}: 모르는 id '{iid}' (코드의 재료 id만)")
            continue
        if iid in seen:
            errors.append(f"{where}: id 중복 ({iid})")
            continue
        seen.add(iid)
        name = (r.get("name") or "").strip() or known[iid]
        out.append({"id": iid, "name": name, "description": r.get("description", "")})
    return {"ingredients": out}, errors


def json_rows() -> list[dict]:
    desc, names = {}, {}
    if JSON_PATH.exists():
        for e in json.loads(JSON_PATH.read_text(encoding="utf-8")).get("ingredients", []):
            desc[e["id"]] = e.get("description", "")
            names[e["id"]] = e.get("name", "")
    return [{"id": i, "name": names.get(i) or n, "description": desc.get(i, "")} for i, n in CELLS]


def main() -> int:
    ap = argparse.ArgumentParser(description="재료 설명 시트 ⇄ ingredients.json")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--csv", action="store_true")
    mode.add_argument("--to-csv", action="store_true")
    mode.add_argument("--push", action="store_true")
    args = ap.parse_args()
    config = load_config()

    if args.to_csv or args.push:
        rows = json_rows()
        write_csv(CSV_PATH, HEADERS, rows)
        print(f"Wrote {CSV_PATH.relative_to(ROOT)} ({len(rows)}칸)")
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
        text = fetch_sheet_csv(sheet_id_for(config, TAB), TAB)
    data, errors = rows_to_json(read_csv_text(text, ["id"], TAB))
    if errors:
        print("시트 오류 — ingredients.json 을 쓰지 않았습니다:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        return 1
    JSON_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {JSON_PATH.relative_to(ROOT)} (재료 {len(data['ingredients'])})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
