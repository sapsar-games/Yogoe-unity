#!/usr/bin/env python3
"""캐릭터 기획 데이터: 구글 시트(3개 탭) ⇄ Assets/Resources/characters.json

시트 = 윷 말풍선 시트(Tools/yut_bubbles_sheets.config.json 의 sheet_id)에 탭 3개:
  characters             id, displayName, endingPropId, detailDescription, note
  character_preferences  character_id, offering_id, offering_name, note   (한 줄 = 선호 1개, 순서 = 표시 순서)
  character_lines        character_id, type, text_ko, note              (한 줄 = 대사 1개)
                         type: monologue(혼잣말) · request_thanks(음식 요구 완료) · request_gift(선물꾸러미 줄 때)
                               · golden_find(황금 재료 수거 — {item} 자리에 황금쌀/황금꿀)
                               · greeting(앱을 켜거나 오래 비웠다 돌아왔을 때, 놀고 있던 요괴의 인사)

사용법:
  python3 Tools/export_characters.py              # 시트 → characters.json   (npm run characters)
  python3 Tools/export_characters.py --csv        # Tools/sheets/*.csv → characters.json
  python3 Tools/export_characters.py --to-csv     # characters.json → Tools/sheets/*.csv
  python3 Tools/export_characters.py --push       # characters.json → CSV + 시트 탭 덮어쓰기 (npm run characters:push)

시트 쓰기는 윷 말풍선과 같은 Apps Script 웹 앱(write_url)을 쓴다 — Tools/YutBubblesSheetsWrite.gs.
"""

from __future__ import annotations

import argparse
import csv
import io
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from export_yut_bubbles import fetch_sheet_csv  # noqa: E402
from sheets_config import sheet_id_for  # noqa: E402
from import_yut_bubbles_csv import _post_json  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
JSON_PATH = ROOT / "Assets" / "Resources" / "characters.json"
SHEETS_DIR = ROOT / "Tools" / "sheets"
CONFIG_PATH = ROOT / "Tools" / "yut_bubbles_sheets.config.json"
ENUMS_PATH = ROOT / "Assets" / "Scripts" / "Data" / "Enums.cs"
OFFERING_ASSETS = ROOT / "Assets" / "Data" / "Offerings"

TAB_CHARACTERS = "characters"
TAB_PREFS = "character_preferences"
TAB_LINES = "character_lines"

CHAR_HEADERS = ["id", "displayName", "endingPropId", "detailDescription", "note"]
PREF_HEADERS = ["character_id", "offering_id", "offering_name", "note"]
LINE_HEADERS = ["character_id", "type", "text_ko", "note"]

# 시트 type → JSON 필드
LINE_TYPES = {
    "monologue": "monologueLines",
    "request_thanks": "requestThanksLines",
    "request_gift": "requestGiftLines",
    "golden_find": "goldenFindLines",
    "greeting": "greetingLines",
    # v1.2 시연 대사 세트
    "go_hunt": "goHuntLines", "go_gather": "goGatherLines", "go_spring": "goSpringLines", "go_altar": "goAltarLines",
    "work_hunt": "workHuntLines", "work_gather": "workGatherLines", "work_spring": "workSpringLines",
    "work_altar": "workAltarLines",
    "full": "fullLines", "full_idle": "fullIdleLines", "tired": "tiredLines", "home": "homeLines",
    "hungry": "hungryLines", "fed": "fedLines", "gold": "goldLines", "offer": "offerLines",
}


# ---------------- 검증용 기준 데이터 ----------------

def known_character_ids() -> list[str]:
    m = re.search(r"enum\s+CharacterId\s*\{([^}]*)\}", ENUMS_PATH.read_text(encoding="utf-8"))
    return [x.strip() for x in m.group(1).split(",") if x.strip()] if m else []


def known_offering_ids() -> dict[str, str]:
    """공양간 레시피(recipes.json) + 공양물 에셋 offeringId → 이름."""
    from recipes_data import load_recipes
    ids: dict[str, str] = {}
    for r in load_recipes():  # recipes.json (시트 recipes 탭)
        ids.setdefault(r["id"], r["name"])
    for p in OFFERING_ASSETS.glob("*.asset"):
        text = p.read_text(encoding="utf-8")
        oid = re.search(r"\n\s*offeringId:\s*(\S+)", text)
        name = re.search(r"\n\s*displayName:\s*(.+)", text)
        if oid:
            ids.setdefault(oid.group(1), name.group(1).strip() if name else oid.group(1))
    return ids


# ---------------- CSV 읽기/쓰기 ----------------

def read_csv_text(text: str, required: list[str], tab: str) -> list[dict[str, str]]:
    from sheet_descriptions import strip_description
    text = strip_description(text, required)  # 1행 ※설명 줄 건너뛰기
    reader = csv.DictReader(io.StringIO(text))
    fields = [h.strip() for h in (reader.fieldnames or [])]
    missing = [h for h in required if h not in fields]
    if missing:
        raise RuntimeError(f"[{tab}] 헤더 누락: {', '.join(missing)}")
    rows = []
    for i, row in enumerate(reader, start=2):
        cleaned = {k.strip(): (v or "").strip() for k, v in row.items() if k}
        if not any(cleaned.get(h) for h in required):
            continue  # 빈 줄
        cleaned["_row"] = str(i)
        rows.append(cleaned)
    return rows


def write_csv(path: Path, headers: list[str], rows: list[dict[str, str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=headers)
        w.writeheader()
        for r in rows:
            w.writerow({h: r.get(h, "") for h in headers})


# ---------------- 시트 행 ⇄ JSON ----------------

def rows_to_json(chars: list[dict], prefs: list[dict], lines: list[dict]) -> tuple[dict, list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    valid_ids = known_character_ids()
    offering_ids = known_offering_ids()

    out: list[dict] = []
    by_id: dict[str, dict] = {}
    for r in chars:
        cid = r.get("id", "")
        if not cid:
            errors.append(f"[{TAB_CHARACTERS}] {r['_row']}행: id 비어 있음")
            continue
        if cid in by_id:
            errors.append(f"[{TAB_CHARACTERS}] {r['_row']}행: id 중복 ({cid})")
            continue
        if valid_ids and cid not in valid_ids:
            errors.append(f"[{TAB_CHARACTERS}] {r['_row']}행: 알 수 없는 id '{cid}' (가능: {', '.join(valid_ids)})")
        entry = {
            "id": cid,
            "displayName": r.get("displayName", ""),
            "preferredOfferings": [],
            "endingPropId": r.get("endingPropId", ""),
            "detailDescription": r.get("detailDescription", ""),
        }
        for field in LINE_TYPES.values():
            entry[field] = []
        by_id[cid] = entry
        out.append(entry)

    for r in prefs:
        cid, oid = r.get("character_id", ""), r.get("offering_id", "")
        where = f"[{TAB_PREFS}] {r['_row']}행"
        if cid not in by_id:
            errors.append(f"{where}: characters 탭에 없는 캐릭터 '{cid}'")
            continue
        if not oid:
            errors.append(f"{where}: offering_id 비어 있음")
            continue
        if oid not in offering_ids:
            warnings.append(f"{where}: 공양간 레시피/공양물 에셋에 없는 id '{oid}' — 게임에서 못 얻을 수 있음")
        name = r.get("offering_name") or offering_ids.get(oid, oid)
        if any(p["id"] == oid for p in by_id[cid]["preferredOfferings"]):
            errors.append(f"{where}: {cid} 선호 중복 ({oid})")
            continue
        by_id[cid]["preferredOfferings"].append({"id": oid, "name": name})

    for r in lines:
        cid, typ, text = r.get("character_id", ""), r.get("type", ""), r.get("text_ko", "")
        where = f"[{TAB_LINES}] {r['_row']}행"
        if cid not in by_id:
            errors.append(f"{where}: characters 탭에 없는 캐릭터 '{cid}'")
            continue
        if typ not in LINE_TYPES:
            errors.append(f"{where}: type 은 {', '.join(LINE_TYPES)} 중 하나 ('{typ}')")
            continue
        if not text:
            continue
        by_id[cid][LINE_TYPES[typ]].append(text)

    for w in warnings:
        print("경고: " + w, file=sys.stderr)
    return {"characters": out}, errors


def json_to_rows(data: dict) -> tuple[list[dict], list[dict], list[dict]]:
    chars, prefs, lines = [], [], []
    for c in data.get("characters", []):
        chars.append({h: c.get(h, "") for h in CHAR_HEADERS if h != "note"})
        for p in c.get("preferredOfferings") or []:
            prefs.append({"character_id": c["id"], "offering_id": p.get("id", ""), "offering_name": p.get("name", "")})
        for typ, field in LINE_TYPES.items():
            for t in c.get(field) or []:
                lines.append({"character_id": c["id"], "type": typ, "text_ko": t})
    return chars, prefs, lines


def dump_json(data: dict) -> str:
    return json.dumps(data, ensure_ascii=False, indent=2) + "\n"


# ---------------- main ----------------

def load_config() -> dict:
    return json.loads(CONFIG_PATH.read_text(encoding="utf-8")) if CONFIG_PATH.exists() else {}


# push 때 시트의 note(메모)를 살리기 위한 행 식별 열 — JSON에는 note가 없어서 시트에서 가져온다
NOTE_KEYS: dict[str, list[str]] = {
    "characters": ["id"],
    "character_preferences": ["character_id", "offering_id"],
    "character_lines": ["character_id", "type", "text_ko"],
    "props": ["propId"],
    "prop_drop_tables": ["table", "ingredient"],
    "prop_settings": ["key"],
    "destinations": ["id"],
    "charms": ["id"],
    "game_settings": ["key"],
    "ingredients": ["id"],
    "attendance": ["day"],
    "yut_fortune": ["gua"],
}


def keep_sheet_notes(config: dict, tab: str, rows: list[dict]) -> None:
    """시트에 적혀 있던 note를 같은 행(식별 열 일치)에 다시 채운다. 못 읽으면 그냥 넘어간다."""
    keys = NOTE_KEYS.get(tab)
    if not keys or not config.get("sheet_id"):
        return
    try:
        existing = read_csv_text(fetch_sheet_csv(sheet_id_for(config, tab), tab), keys, tab)
    except Exception as e:  # 탭이 아직 없거나 네트워크 문제
        print(f"경고: [{tab}] 기존 note를 못 읽었습니다 ({e})", file=sys.stderr)
        return
    notes = {tuple(r.get(k, "") for k in keys): r.get("note", "") for r in existing if r.get("note")}
    for r in rows:
        if not r.get("note"):
            r["note"] = notes.get(tuple(r.get(k, "") for k in keys), "")


def push_tab(config: dict, tab: str, headers: list[str], rows: list[dict]) -> None:
    from sheet_descriptions import description_row
    if "note" in headers:
        keep_sheet_notes(config, tab, rows)
    body = [[r.get(h, "") for h in headers] for r in rows]
    desc = description_row(tab, len(headers))
    payload = {"tab": tab, "headers": desc or headers, "rows": ([headers] + body) if desc else body}
    if sheet_id_for(config, tab):
        payload["spreadsheetId"] = sheet_id_for(config, tab)
    if config.get("write_token"):
        payload["token"] = config["write_token"]
    raw = _post_json(config["write_url"], json.dumps(payload, ensure_ascii=False).encode("utf-8"))
    result = json.loads(raw)
    if not result.get("ok"):
        raise RuntimeError(f"[{tab}] 시트 쓰기 거부: {result.get('error') or result}")
    print(f"시트 쓰기: {result.get('spreadsheetName')} / {tab} ({result.get('rows')}행)")


def main() -> int:
    ap = argparse.ArgumentParser(description="캐릭터 기획 시트 ⇄ characters.json")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--csv", action="store_true", help="Tools/sheets/*.csv 에서 읽기")
    mode.add_argument("--to-csv", action="store_true", help="characters.json → CSV")
    mode.add_argument("--push", action="store_true", help="characters.json → CSV + 시트 탭 덮어쓰기")
    args = ap.parse_args()
    config = load_config()

    csv_paths = {
        TAB_CHARACTERS: SHEETS_DIR / f"{TAB_CHARACTERS}.csv",
        TAB_PREFS: SHEETS_DIR / f"{TAB_PREFS}.csv",
        TAB_LINES: SHEETS_DIR / f"{TAB_LINES}.csv",
    }

    if args.to_csv or args.push:
        chars, prefs, lines = json_to_rows(json.loads(JSON_PATH.read_text(encoding="utf-8")))
        write_csv(csv_paths[TAB_CHARACTERS], CHAR_HEADERS, chars)
        write_csv(csv_paths[TAB_PREFS], PREF_HEADERS, prefs)
        write_csv(csv_paths[TAB_LINES], LINE_HEADERS, lines)
        print(f"Wrote Tools/sheets/{{{TAB_CHARACTERS},{TAB_PREFS},{TAB_LINES}}}.csv")
        if args.push:
            if not config.get("write_url"):
                print("config 에 write_url 이 없습니다 — Tools/YutBubblesSheetsWrite.gs 참고", file=sys.stderr)
                return 1
            push_tab(config, TAB_CHARACTERS, CHAR_HEADERS, chars)
            push_tab(config, TAB_PREFS, PREF_HEADERS, prefs)
            push_tab(config, TAB_LINES, LINE_HEADERS, lines)
        return 0

    texts: dict[str, str] = {}
    for tab, path in csv_paths.items():
        if args.csv:
            texts[tab] = path.read_text(encoding="utf-8-sig")
        else:
            if not config.get("sheet_id"):
                print("config 에 sheet_id 가 없습니다.", file=sys.stderr)
                return 1
            texts[tab] = fetch_sheet_csv(sheet_id_for(config, tab), tab)

    chars = read_csv_text(texts[TAB_CHARACTERS], ["id"], TAB_CHARACTERS)
    prefs = read_csv_text(texts[TAB_PREFS], ["character_id", "offering_id"], TAB_PREFS)
    lines = read_csv_text(texts[TAB_LINES], ["character_id", "type", "text_ko"], TAB_LINES)
    data, errors = rows_to_json(chars, prefs, lines)
    if errors:
        print("시트 오류 — characters.json 을 쓰지 않았습니다:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        return 1

    JSON_PATH.write_text(dump_json(data), encoding="utf-8")
    n_pref = sum(len(c["preferredOfferings"]) for c in data["characters"])
    n_line = sum(len(c[f]) for c in data["characters"] for f in LINE_TYPES.values())
    print(f"Wrote {JSON_PATH.relative_to(ROOT)} (캐릭터 {len(data['characters'])}, 선호 {n_pref}, 대사 {n_line})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
