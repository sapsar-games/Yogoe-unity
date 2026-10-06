#!/usr/bin/env python3
"""기물 밸런스: 구글 시트(3개 탭) ⇄ Assets/Resources/props.json

시트 = 윷 말풍선 시트(Tools/yut_bubbles_sheets.config.json 의 sheet_id)에 탭 3개:
  props             propId, displayName, resourceType, cycleMinutes, baseCapacity, meritPerMinute, levelGrowth,
                    meritCapacityMinutes, intimacyBonus, ownerMultiplier, upgradable, upgradeBaseCost,
                    upgradeCostMultiplier, note
                    resourceType: Merit(공덕) · Water(물) · Yeopjeon(엽전) · Hunt(사냥 재료) · Gather(채집 재료) · None
  prop_drop_tables  table, ingredient, name, weight, note     (weight: 확률 %, ingredient: 재료 id)
                    table = Hunt / Gather (지금 게임의 활터·약초밭 표, GoldenRice / GoldenHoney 가능)
                          또는 destinations 탭의 목적지 id (v1.2 — 보내기 팝업이 생기면 게임이 쓴다)
  destinations      id, prop, name, rarity, minIntimacy, goldenChance, note
                    v1.2 사냥터·채집터 목적지. prop = Hunt / Gather, minIntimacy = 입장 친밀도,
                    goldenChance = 재료가 하나 나올 때 황금 버전으로 바뀔 확률 %
  prop_settings     key, value, note                         (purchaseBaseCost, purchaseCostGrowth)

  props 의 propId 는 기물 에셋과 연결된다. 에셋이 아직 없는 기물(v1.2 북제단·남제단 등)은
  경고만 하고 값은 저장한다 — 기물 에셋이 생기면 그때부터 게임에 적용.

사용법:
  python3 Tools/export_props.py              # 시트 → props.json   (npm run props)
  python3 Tools/export_props.py --csv        # Tools/sheets/*.csv → props.json
  python3 Tools/export_props.py --to-csv     # props.json → Tools/sheets/*.csv
  python3 Tools/export_props.py --push       # props.json → CSV + 시트 탭 덮어쓰기 (npm run props:push)
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from export_characters import load_config, push_tab, read_csv_text, write_csv  # noqa: E402
from export_yut_bubbles import fetch_sheet_csv  # noqa: E402
from recipes_data import current_id  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
JSON_PATH = ROOT / "Assets" / "Resources" / "props.json"
SHEETS_DIR = ROOT / "Tools" / "sheets"
PROP_ASSETS = ROOT / "Assets" / "Data" / "Props"
ECONOMY_CS = ROOT / "Assets" / "Scripts" / "Cooking" / "CookingTypes.cs"  # CookingIngredientId
ENUMS_CS = ROOT / "Assets" / "Scripts" / "Data" / "Enums.cs"

TAB_PROPS = "props"
TAB_DROPS = "prop_drop_tables"
TAB_SETTINGS = "prop_settings"
TAB_DEST = "destinations"

PROP_HEADERS = ["propId", "displayName", "resourceType", "cycleMinutes", "baseCapacity", "meritPerMinute",
                "levelGrowth", "meritCapacityMinutes", "intimacyBonus", "ownerMultiplier", "upgradable",
                "upgradeBaseCost", "upgradeCostMultiplier", "note"]
DROP_HEADERS = ["table", "ingredient", "name", "weight", "note"]
SETTING_HEADERS = ["key", "value", "note"]
DEST_HEADERS = ["id", "prop", "name", "rarity", "minIntimacy", "goldenChance", "note"]

RESOURCE_TYPES = ["Merit", "Water", "Yeopjeon", "Hunt", "Gather", "None"]
DROP_TABLES = ["Hunt", "Gather"]
SETTING_KEYS = {"purchaseBaseCost": 300.0, "purchaseCostGrowth": 1.35}
FLOAT_COLS = ["cycleMinutes", "meritPerMinute", "levelGrowth", "meritCapacityMinutes", "ownerMultiplier",
              "upgradeBaseCost", "upgradeCostMultiplier"]


def known_prop_ids() -> set[str]:
    ids = set()
    for p in PROP_ASSETS.glob("*.asset"):
        m = re.search(r"\n\s*propId:\s*(.+)", p.read_text(encoding="utf-8"))
        if m:
            ids.add(m.group(1).strip().strip('"'))
    return ids


def ingredient_names() -> list[str]:
    """요리 재료(CookingIngredientId) + 특수 수집품(SpecialItemId: 황금쌀·황금꿀)."""
    names = []
    m = re.search(r"enum\s+CookingIngredientId\s*\{([^}]*)\}", ECONOMY_CS.read_text(encoding="utf-8"))
    for line in (m.group(1) if m else "").splitlines():
        name = line.split("//")[0].split("=")[0].strip().rstrip(",").strip()
        if name and name != "Count":
            names.append(name)
    m = re.search(r"enum\s+SpecialItemId\s*\{([^}]*)\}", ENUMS_CS.read_text(encoding="utf-8"))
    names += [x.strip() for x in (m.group(1) if m else "").split(",") if x.strip()]
    return names


def to_bool(v: str) -> bool | None:
    s = (v or "").strip().lower()
    if s in ("true", "1", "yes", "y", "o"):
        return True
    if s in ("false", "0", "no", "n", "x", ""):
        return False
    return None


def to_num(v: str) -> float | None:
    s = (v or "").strip().replace(",", "")
    if s == "":
        return 0.0
    try:
        return float(s)
    except ValueError:
        return None


def parse_destinations(rows: list[dict], errors: list[str]) -> list[dict]:
    out, seen = [], set()
    for r in rows:
        where = f"[{TAB_DEST}] {r['_row']}행"
        did = r.get("id", "")
        if did in seen or did in DROP_TABLES:
            errors.append(f"{where}: id 중복 ({did})")
            continue
        seen.add(did)
        prop = r.get("prop", "")
        if prop not in DROP_TABLES:
            errors.append(f"{where}: prop 은 {', '.join(DROP_TABLES)} 중 하나 ('{prop}')")
            continue
        mi, gc = to_num(r.get("minIntimacy", "")), to_num(r.get("goldenChance", ""))
        if mi is None or mi < 0:
            errors.append(f"{where}: minIntimacy 는 0 이상 숫자 ('{r.get('minIntimacy')}')")
            continue
        if gc is None or not 0 <= gc <= 100:
            errors.append(f"{where}: goldenChance 는 0~100 ('{r.get('goldenChance')}')")
            continue
        out.append({"id": did, "prop": prop, "name": r.get("name", ""), "rarity": r.get("rarity", ""),
                    "minIntimacy": mi, "goldenChance": gc})
    return out


def rows_to_json(props: list[dict], drops: list[dict], settings: list[dict],
                 dests: list[dict] | None = None) -> tuple[dict, list[str]]:
    errors: list[str] = []
    valid_props = known_prop_ids()
    out_dests = parse_destinations(dests or [], errors)
    dest_ids = {d["id"] for d in out_dests}
    ingredients = ingredient_names()

    out_props, seen = [], set()
    for r in props:
        where = f"[{TAB_PROPS}] {r['_row']}행"
        pid = r.get("propId", "")
        if pid in seen:
            errors.append(f"{where}: propId 중복 ({pid})")
            continue
        seen.add(pid)
        if valid_props and pid not in valid_props:
            print(f"경고: {where}: 기물 에셋이 아직 없는 propId '{pid}' — 값은 저장, 에셋이 생기면 적용"
                  f" (지금 에셋: {', '.join(sorted(valid_props))})", file=sys.stderr)
        rtype = r.get("resourceType", "")
        if rtype not in RESOURCE_TYPES:
            errors.append(f"{where}: resourceType 은 {', '.join(RESOURCE_TYPES)} 중 하나 ('{rtype}')")
        entry = {"propId": pid, "displayName": r.get("displayName", ""), "resourceType": rtype}
        for col in FLOAT_COLS:
            n = to_num(r.get(col, ""))
            if n is None:
                errors.append(f"{where}: {col} 숫자 아님 ('{r.get(col)}')")
                n = 0.0
            entry[col] = n
        cap = to_num(r.get("baseCapacity", ""))
        if cap is None or cap != int(cap):
            errors.append(f"{where}: baseCapacity 는 정수 ('{r.get('baseCapacity')}')")
            cap = 0
        entry["baseCapacity"] = int(cap)
        for col in ("intimacyBonus", "upgradable"):
            b = to_bool(r.get(col, ""))
            if b is None:
                errors.append(f"{where}: {col} 는 TRUE/FALSE ('{r.get(col)}')")
                b = False
            entry[col] = b
        if rtype in ("Water", "Yeopjeon", "Hunt", "Gather"):
            if entry["cycleMinutes"] <= 0:
                errors.append(f"{where}: 자원 기물은 cycleMinutes > 0 필요")
            if entry["baseCapacity"] <= 0:
                errors.append(f"{where}: 자원 기물은 baseCapacity > 0 필요")
        if rtype == "Merit" and entry["meritPerMinute"] <= 0:
            errors.append(f"{where}: 공덕 기물은 meritPerMinute > 0 필요")
        out_props.append(entry)

    out_drops = []
    totals: dict[str, float] = {}
    for r in drops:
        where = f"[{TAB_DROPS}] {r['_row']}행"
        table, ing = r.get("table", ""), current_id(r.get("ingredient", ""))
        if table not in DROP_TABLES and table not in dest_ids:
            errors.append(f"{where}: table 은 {', '.join(DROP_TABLES)} 또는 destinations 탭의 목적지 id ('{table}')")
            continue
        if table in dest_ids and ing.startswith("Golden"):
            errors.append(f"{where}: 목적지 표에는 황금 재료를 쓰지 않음 — destinations 탭 goldenChance 로")
            continue
        if ing not in ingredients:
            errors.append(f"{where}: 알 수 없는 ingredient '{ing}' (가능: {', '.join(ingredients)})")
            continue
        w = to_num(r.get("weight", ""))
        if w is None or w <= 0:
            errors.append(f"{where}: weight 는 0보다 큰 숫자 ('{r.get('weight')}')")
            continue
        totals[table] = totals.get(table, 0.0) + w
        out_drops.append({"table": table, "ingredient": ing, "name": r.get("name", ""), "weight": w})
    for table, total in totals.items():
        if abs(total - 100.0) > 0.01:
            print(f"경고: [{TAB_DROPS}] {table} 확률 합이 {total:g}% (100이 아니면 비율로 환산됨)", file=sys.stderr)

    out_settings = dict(SETTING_KEYS)
    for r in settings:
        where = f"[{TAB_SETTINGS}] {r['_row']}행"
        key = r.get("key", "")
        if key not in SETTING_KEYS:
            errors.append(f"{where}: 알 수 없는 key '{key}' (가능: {', '.join(SETTING_KEYS)})")
            continue
        n = to_num(r.get("value", ""))
        if n is None or n <= 0:
            errors.append(f"{where}: value 는 0보다 큰 숫자 ('{r.get('value')}')")
            continue
        out_settings[key] = n

    for d in out_dests:
        if d["id"] not in totals:
            errors.append(f"[{TAB_DEST}] 목적지 '{d['id']}' 의 재료 확률이 {TAB_DROPS} 탭에 없음")

    return {"props": out_props, "dropTables": out_drops, "settings": out_settings,
            "destinations": out_dests}, errors


def fmt(v) -> str:
    if isinstance(v, bool):
        return "TRUE" if v else "FALSE"
    if isinstance(v, float) and v == int(v):
        return str(int(v))
    return str(v)


def json_to_rows(data: dict) -> tuple[list[dict], list[dict], list[dict]]:
    props = [{h: fmt(p.get(h, "")) for h in PROP_HEADERS if h != "note"} for p in data.get("props", [])]
    drops = [{h: fmt(d.get(h, "")) for h in DROP_HEADERS if h != "note"} for d in data.get("dropTables", [])]
    settings = [{"key": k, "value": fmt(v)} for k, v in (data.get("settings") or {}).items()]
    dests = [{h: fmt(d.get(h, "")) for h in DEST_HEADERS if h != "note"} for d in data.get("destinations", [])]
    return props, drops, settings, dests


def main() -> int:
    ap = argparse.ArgumentParser(description="기물 밸런스 시트 ⇄ props.json")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--csv", action="store_true", help="Tools/sheets/*.csv 에서 읽기")
    mode.add_argument("--to-csv", action="store_true", help="props.json → CSV")
    mode.add_argument("--push", action="store_true", help="props.json → CSV + 시트 탭 덮어쓰기")
    args = ap.parse_args()
    config = load_config()

    tabs = [(TAB_PROPS, PROP_HEADERS, ["propId"]),
            (TAB_DROPS, DROP_HEADERS, ["table", "ingredient"]),
            (TAB_SETTINGS, SETTING_HEADERS, ["key"]),
            (TAB_DEST, DEST_HEADERS, ["id"])]
    paths = {t: SHEETS_DIR / f"{t}.csv" for t, _, _ in tabs}

    if args.to_csv or args.push:
        rows = json_to_rows(json.loads(JSON_PATH.read_text(encoding="utf-8")))
        for (tab, headers, _), r in zip(tabs, rows):
            write_csv(paths[tab], headers, r)
        print(f"Wrote Tools/sheets/{{{TAB_PROPS},{TAB_DROPS},{TAB_SETTINGS},{TAB_DEST}}}.csv")
        if args.push:
            if not config.get("write_url"):
                print("config 에 write_url 이 없습니다 — Tools/YutBubblesSheetsWrite.gs 참고", file=sys.stderr)
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
            text = fetch_sheet_csv(config["sheet_id"], tab)
        parsed.append(read_csv_text(text, required, tab))

    data, errors = rows_to_json(*parsed)
    if errors:
        print("시트 오류 — props.json 을 쓰지 않았습니다:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        return 1

    JSON_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {JSON_PATH.relative_to(ROOT)} (기물 {len(data['props'])}, 재료 확률 {len(data['dropTables'])},"
          f" 목적지 {len(data['destinations'])})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
