#!/usr/bin/env python3
"""공양간 레시피: 구글 시트 탭 recipes ⇄ Assets/Resources/recipes.json (게임이 읽는 정본)

시트 = 윷 말풍선 시트(Tools/yut_bubbles_sheets.config.json 의 sheet_id)의 탭 1개:
  recipes  kind, id, name, ingredient_1, ingredient_2, ingredient_3, description, note
           한 줄 = 조합 1개. 같은 id 를 여러 줄 쓰면 같은 요리의 다른 조합 (예: 고기죽 = 쌀+새고기 / 쌀+멧돼지고기)
           kind: 음식(재료 2) / 공양물(재료 3) · 재료는 한글 이름(쌀, 팥, 물 …)
           description: 요리책 상세 설명 — 요리마다 한 줄에만 (같은 id 의 다른 줄은 비워 둠)

사용법:
  python3 Tools/export_recipes.py            # 시트 → recipes.json   (npm run recipes)
  python3 Tools/export_recipes.py --csv      # Tools/sheets/recipes.csv → recipes.json
  python3 Tools/export_recipes.py --to-csv   # recipes.json → Tools/sheets/recipes.csv
  python3 Tools/export_recipes.py --push     # recipes.json → CSV + 시트 탭 덮어쓰기 (npm run recipes:push)

재료 설명은 별도 탭 ingredients (Tools/export_ingredients.py).
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
from recipes_data import (OLD_INGREDIENT_IDS, ING_KO, ING_ORDER, KIND_INGREDIENTS, KIND_KO, KO_TO_ING, KO_TO_KIND,  # noqa: E402
                          RECIPES_JSON, ROOT, load_recipes)

TAB = "recipes"
ING_COLS = ["ingredient_1", "ingredient_2", "ingredient_3"]
HEADERS = ["kind", "id", "name"] + ING_COLS + ["description", "note"]
CSV_PATH = ROOT / "Tools" / "sheets" / "recipes.csv"


def parse_ingredient(v: str) -> str | None:
    v = v.strip()
    if v in KO_TO_ING:
        return KO_TO_ING[v]
    if v in ING_KO:  # enum 이름도 허용
        return v
    if v in OLD_INGREDIENT_IDS:  # 예전 id
        return OLD_INGREDIENT_IDS[v]
    return None


def rows_to_json(rows: list[dict]) -> tuple[dict, list[str]]:
    errors: list[str] = []
    recipes = []
    by_id: dict[str, tuple[str, str]] = {}
    by_combo: dict[tuple, str] = {}
    desc_by_id: dict[str, str] = {}
    for r in rows:
        where = f"[{TAB}] {r['_row']}행"
        rid, name = r.get("id", ""), r.get("name", "")
        kind = KO_TO_KIND.get(r.get("kind", ""), r.get("kind", ""))
        if kind not in KIND_INGREDIENTS:
            errors.append(f"{where}: kind 는 음식 / 공양물 ('{r.get('kind')}')")
            continue
        if not rid or not rid.replace("_", "").isalnum() or not rid.isascii():
            errors.append(f"{where}: id 는 영문·숫자·_ ('{rid}')")
            continue
        if rid.endswith("_golden"):
            errors.append(f"{where}: id 끝에 _golden 은 황금음식용이라 못 씀 ('{rid}')")
            continue
        if not name:
            errors.append(f"{where}: name 이 비어 있음 ({rid})")
            continue
        ings, bad = [], []
        for c in ING_COLS:
            v = r.get(c, "")
            if not v:
                continue
            ing = parse_ingredient(v)
            (ings if ing else bad).append(ing or v)
        if bad:
            errors.append(f"{where}: 모르는 재료 {bad} (쓸 수 있는 재료: {', '.join(ING_KO.values())})")
            continue
        need = KIND_INGREDIENTS[kind]
        if len(ings) != need:
            errors.append(f"{where}: {KIND_KO[kind]}은 재료 {need}개 ({rid}: {len(ings)}개)")
            continue
        if rid in by_id and by_id[rid] != (name, kind):
            errors.append(f"{where}: 같은 id({rid})인데 이름/종류가 다름 — 한 요리는 이름·종류가 같아야 함")
            continue
        by_id[rid] = (name, kind)
        key = tuple(sorted(ings, key=ING_ORDER.index))
        if key in by_combo:
            errors.append(f"{where}: 같은 조합이 이미 있음 ({' + '.join(ING_KO[i] for i in key)} → {by_combo[key]})")
            continue
        by_combo[key] = rid
        desc = r.get("description", "")
        if desc:
            if rid in desc_by_id and desc_by_id[rid] != desc:
                errors.append(f"{where}: {rid} 설명이 두 줄에 다르게 있음 — 한 줄에만 쓰세요")
                continue
            desc_by_id[rid] = desc
        recipes.append({"id": rid, "name": name, "kind": kind, "ingredients": ings})
    if not recipes:
        errors.append(f"[{TAB}] 레시피가 하나도 없음")
    # 설명은 그 요리의 첫 줄에만 둔다
    placed = set()
    for rec in recipes:
        if rec["id"] in desc_by_id and rec["id"] not in placed:
            rec["description"] = desc_by_id[rec["id"]]
            placed.add(rec["id"])
    return {"recipes": recipes}, errors


def json_to_rows() -> list[dict]:
    rows = []
    for r in load_recipes():
        row = {"kind": KIND_KO[r["kind"]], "id": r["id"], "name": r["name"], "description": r.get("description", "")}
        for c, ing in zip(ING_COLS, r["ingredients"]):
            row[c] = ING_KO[ing]
        rows.append(row)
    return rows


def main() -> int:
    ap = argparse.ArgumentParser(description="레시피 시트 ⇄ recipes.json")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--csv", action="store_true")
    mode.add_argument("--to-csv", action="store_true")
    mode.add_argument("--push", action="store_true")
    args = ap.parse_args()
    config = load_config()

    if args.to_csv or args.push:
        rows = json_to_rows()
        write_csv(CSV_PATH, HEADERS, rows)
        print(f"Wrote {CSV_PATH.relative_to(ROOT)} ({len(rows)}조합)")
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
    data, errors = rows_to_json(read_csv_text(text, ["kind", "id"], TAB))
    if errors:
        print("시트 오류 — recipes.json 을 쓰지 않았습니다:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        return 1
    RECIPES_JSON.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    ids = {r["id"] for r in data["recipes"]}
    print(f"Wrote {RECIPES_JSON.relative_to(ROOT)} (조합 {len(data['recipes'])}, 요리 {len(ids)})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
