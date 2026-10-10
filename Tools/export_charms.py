#!/usr/bin/env python3
"""윷 완주 보상 부적 확률: 구글 시트 탭 charms ⇄ Assets/Resources/charms.json

시트 = 윷 말풍선 시트(Tools/yut_bubbles_sheets.config.json 의 sheet_id)의 탭 1개:
  charms  id, name, weight, seconds, adExtend, note
          한 줄 = 부적 1종. 말 1개가 완주할 때 weight 비율로 1개가 나온다 (예: 전부 1이면 6종 균등).
          weight 0 = 안 나옴. id 는 코드와 연결 — 바꾸지 마세요.
          seconds = 그 부적을 끼고 시작한 판의 제한시간(초), adExtend = 시간이 끝났을 때 광고 연장 가능(TRUE/FALSE).
          나가리는 판 중간에 쓰는 부적이라 seconds·adExtend 를 비워 둔다.

사용법:
  python3 Tools/export_charms.py            # 시트 → charms.json   (npm run charms)
  python3 Tools/export_charms.py --csv      # Tools/sheets/charms.csv → charms.json
  python3 Tools/export_charms.py --to-csv   # charms.json → Tools/sheets/charms.csv
  python3 Tools/export_charms.py --push     # charms.json → CSV + 시트 탭 덮어쓰기 (npm run charms:push)
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

ROOT = Path(__file__).resolve().parents[1]
TAB = "charms"
HEADERS = ["id", "name", "weight", "seconds", "adExtend", "note"]
JSON_PATH = ROOT / "Assets" / "Resources" / "charms.json"
CSV_PATH = ROOT / "Tools" / "sheets" / "charms.csv"
# CookingCharmType (None 제외) — CharmDropRates.All 과 같은 순서
CHARMS = [("PlusFive", "+5초"), ("Diagonal", "대각선"), ("Clairvoyance", "천리안"),
          ("Recycle", "회수"), ("Double", "몰빵"), ("Cancel", "나가리")]
# 시트가 비었을 때의 기본값 (v1.2 기획서) — CookingSession.DefaultLimit / AllowAdExtend 와 같다
DEFAULT_SECONDS = {"PlusFive": 20, "Diagonal": 15, "Clairvoyance": 15, "Recycle": 12, "Double": 10}
DEFAULT_AD = {"PlusFive": True, "Diagonal": True, "Clairvoyance": True, "Recycle": False, "Double": False}
NO_TIME = {"Cancel"}  # 판 중간에 쓰는 부적


def parse_bool(v: str) -> bool | None:
    s = (v or "").strip().lower()
    if s in ("true", "1", "o", "yes"):
        return True
    if s in ("false", "0", "x", "no"):
        return False
    return None


def rows_to_json(rows: list[dict]) -> tuple[dict, list[str]]:
    known = dict(CHARMS)
    errors, seen, out = [], set(), []
    for r in rows:
        where = f"[{TAB}] {r['_row']}행"
        cid = r.get("id", "")
        if cid not in known:
            errors.append(f"{where}: 모르는 id '{cid}' (쓸 수 있는 id: {', '.join(known)})")
            continue
        if cid in seen:
            errors.append(f"{where}: id 중복 ({cid})")
            continue
        seen.add(cid)
        try:
            w = float(r.get("weight", "") or "0")
        except ValueError:
            errors.append(f"{where}: weight 는 숫자 ('{r.get('weight')}')")
            continue
        if w < 0:
            errors.append(f"{where}: weight 는 0 이상")
            continue
        entry = {"id": cid, "name": known[cid], "weight": w}
        sec_raw = (r.get("seconds") or "").strip()
        ad_raw = (r.get("adExtend") or "").strip()
        if cid in NO_TIME:
            if sec_raw or ad_raw:
                errors.append(f"{where}: {known[cid]}은 판 중간에 쓰는 부적 — seconds·adExtend 는 비워 두세요")
        else:
            if sec_raw:
                try:
                    sec = float(sec_raw)
                except ValueError:
                    sec = -1
                if sec <= 0:
                    errors.append(f"{where}: seconds 는 0보다 큰 숫자 ('{sec_raw}')")
                else:
                    entry["seconds"] = sec
            if ad_raw:
                ad = parse_bool(ad_raw)
                if ad is None:
                    errors.append(f"{where}: adExtend 는 TRUE/FALSE ('{ad_raw}')")
                else:
                    entry["adExtend"] = "true" if ad else "false"
        out.append(entry)
    if not any(c["weight"] > 0 for c in out):
        errors.append(f"[{TAB}] weight 가 0보다 큰 부적이 하나는 있어야 함")
    return {"charms": out}, errors


def json_rows() -> list[dict]:
    cur = {}
    if JSON_PATH.exists():
        for c in json.loads(JSON_PATH.read_text(encoding="utf-8")).get("charms", []):
            cur[c["id"]] = c
    def fmt(v):
        return str(int(v)) if float(v).is_integer() else str(v)
    rows = []
    for i, n in CHARMS:
        c = cur.get(i, {})
        row = {"id": i, "name": n, "weight": fmt(c.get("weight", 1)), "seconds": "", "adExtend": ""}
        if i not in NO_TIME:
            row["seconds"] = fmt(c.get("seconds", DEFAULT_SECONDS[i]))
            ad = c.get("adExtend")
            row["adExtend"] = "TRUE" if (ad == "true" if ad is not None else DEFAULT_AD[i]) else "FALSE"
        rows.append(row)
    return rows


def main() -> int:
    ap = argparse.ArgumentParser(description="완주 부적 확률 시트 ⇄ charms.json")
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--csv", action="store_true")
    mode.add_argument("--to-csv", action="store_true")
    mode.add_argument("--push", action="store_true")
    args = ap.parse_args()
    config = load_config()

    if args.to_csv or args.push:
        rows = json_rows()
        write_csv(CSV_PATH, HEADERS, rows)
        print(f"Wrote {CSV_PATH.relative_to(ROOT)} ({len(rows)}종)")
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
    data, errors = rows_to_json(read_csv_text(text, ["id", "weight"], TAB))
    if errors:
        print("시트 오류 — charms.json 을 쓰지 않았습니다:", file=sys.stderr)
        for e in errors:
            print("  " + e, file=sys.stderr)
        return 1
    JSON_PATH.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    total = sum(c["weight"] for c in data["charms"])
    print(f"Wrote {JSON_PATH.relative_to(ROOT)} — " + ", ".join(
        f"{c['name']} {c['weight'] / total * 100:.0f}%" for c in data["charms"] if c["weight"] > 0))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
