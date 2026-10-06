#!/usr/bin/env python3
"""Assets/Resources/Yut/yut_bubbles.{locale}.json → CSV (+ Google Sheets 쓰기)

사용법:
  # 로컬 CSV만
  python3 Tools/import_yut_bubbles_csv.py
  npm run yut-bubbles:to-csv

  # 시트에 직접 쓰기 (Apps Script 웹 앱 필요 — Tools/YutBubblesSheetsWrite.gs)
  python3 Tools/import_yut_bubbles_csv.py --push
  npm run yut-bubbles:push

config: Tools/yut_bubbles_sheets.config.json
  {
    "sheet_id": "...",
    "tab": "yut_bubbles",
    "locales": ["ko", "en", "zh", "ja"],
    "fallback_locale": "ko",
    "write_url": "https://script.google.com/macros/s/.../exec",
    "write_token": "optional-shared-secret"
  }
"""

from __future__ import annotations

import argparse
import csv
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IN_DIR = ROOT / "Assets" / "Resources" / "Yut"
OUT_DEFAULT = ROOT / "Tools" / "sheets" / "yut_bubbles.csv"
CONFIG_PATH = ROOT / "Tools" / "yut_bubbles_sheets.config.json"
DEFAULT_LOCALES = ["ko", "en", "zh", "ja"]
DEFAULT_FALLBACK = "ko"

# export_yut_bubbles.KNOWN_IDS 와 동기화 — 행 순서 고정용
KNOWN_IDS = [
    "rabbit.yut",
    "rabbit.mo",
    "rabbit.baekdo",
    "rabbit.steps",
    "rabbit.finished",
    "rabbit.cheer",
    "rabbit.urge_finish",
    "candidate.treasure",
    "candidate.offering",
    "candidate.coin",
    "candidate.water",
    "candidate.capture",
    "candidate.stack",
    "candidate.finish",
    "event.captured",
    "event.revived",
    "event.opponent_caught",
    "opponent.throw",
]

DEFAULT_NOTES = {
    "rabbit.yut": "옥토끼 윷",
    "rabbit.mo": "옥토끼 모",
    "rabbit.baekdo": "옥토끼 빽도",
    "rabbit.steps": "옥토끼 도/개/걸 — {result} {steps}",
    "rabbit.finished": "옥토끼 완주 직후(동)",
    "rabbit.cheer": "옥토끼 완주 후 던지기 — {result}",
    "rabbit.urge_finish": "옥토끼 완주 후·남은 말 완주 가능",
    "candidate.treasure": "후보 말 보물칸",
    "candidate.offering": "후보 말 공양물칸",
    "candidate.coin": "후보 말 엽전칸",
    "candidate.water": "후보 말 물 칸",
    "candidate.capture": "후보 말 이무기 잡기",
    "candidate.stack": "후보 말 업기 — {ally}",
    "candidate.finish": "후보 말 완주",
    "event.captured": "잡힘",
    "event.revived": "되살림",
    "event.opponent_caught": "이무기 잡힘",
    "opponent.throw": "이무기 결과 — {result}",
}


def load_config() -> dict:
    if not CONFIG_PATH.exists():
        return {}
    return json.loads(CONFIG_PATH.read_text(encoding="utf-8"))


def load_locale_map(locales: list[str]) -> dict[str, dict[str, str]]:
    by_locale: dict[str, dict[str, str]] = {}
    for loc in locales:
        path = IN_DIR / f"yut_bubbles.{loc}.json"
        if not path.exists():
            continue
        data = json.loads(path.read_text(encoding="utf-8"))
        lines = data.get("lines") or []
        m: dict[str, str] = {}
        for line in lines:
            if not isinstance(line, dict):
                continue
            rid = (line.get("id") or "").strip()
            if not rid:
                continue
            m[rid] = line.get("text") or ""
        by_locale[loc] = m
        print(f"Read {path.relative_to(ROOT)} ({len(m)} lines)")
    return by_locale


def load_existing_notes(csv_path: Path) -> dict[str, str]:
    if not csv_path.exists():
        return {}
    notes: dict[str, str] = {}
    with csv_path.open(encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if not reader.fieldnames or "id" not in reader.fieldnames:
            return {}
        for row in reader:
            rid = (row.get("id") or "").strip()
            if not rid:
                continue
            note = (row.get("note") or "").strip()
            if note:
                notes[rid] = note
    return notes


def ordered_ids(by_locale: dict[str, dict[str, str]]) -> list[str]:
    seen: set[str] = set()
    ordered: list[str] = []
    for rid in KNOWN_IDS:
        ordered.append(rid)
        seen.add(rid)
    extras: list[str] = []
    for m in by_locale.values():
        for rid in m:
            if rid not in seen:
                extras.append(rid)
                seen.add(rid)
    extras.sort()
    return ordered + extras


def build_rows(
    by_locale: dict[str, dict[str, str]],
    locales: list[str],
    existing_notes: dict[str, str],
) -> list[dict[str, str]]:
    ids = ordered_ids(by_locale)
    present: set[str] = set()
    for m in by_locale.values():
        present.update(m.keys())

    rows: list[dict[str, str]] = []
    for rid in ids:
        if rid not in present and rid not in KNOWN_IDS:
            continue
        row: dict[str, str] = {
            "id": rid,
            "note": existing_notes.get(rid) or DEFAULT_NOTES.get(rid) or "",
        }
        for loc in locales:
            row[f"text_{loc}"] = (by_locale.get(loc) or {}).get(rid) or ""
        rows.append(row)
    return rows


def fieldnames_for(locales: list[str]) -> list[str]:
    return ["id", "note"] + [f"text_{loc}" for loc in locales]


def write_csv(path: Path, rows: list[dict[str, str]], locales: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fieldnames = fieldnames_for(locales)
    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames, quoting=csv.QUOTE_MINIMAL)
        writer.writeheader()
        for row in rows:
            writer.writerow({k: row.get(k, "") for k in fieldnames})


def _http_error_hint(code: int, body: str) -> str:
    low = body.lower()
    if code in (401, 403) or "페이지를 찾을 수 없음" in body or "can't open" in low:
        return (
            f"시트 쓰기 HTTP {code}: 웹 앱 접근이 막혀 있습니다.\n"
            "Apps Script → 배포 → 배포 관리 → 연필(수정):\n"
            "  · 실행 계정: 나\n"
            "  · 액세스 권한: 모든 사용자 (Anyone)  ← '나만'이면 Python이 401 납니다\n"
            "저장 후 새 /exec URL을 write_url 에 넣고 다시: npm run yut-bubbles:push\n"
            "참고: write_token 에는 URL 배포 ID를 넣지 마세요. "
            "스크립트 속성 WRITE_TOKEN 과 같은 임의 비밀만 (없으면 빈 문자열)."
        )
    snippet = body.replace("\n", " ")[:200]
    return f"시트 쓰기 HTTP {code}: {snippet}"


def _ssl_context():
    import ssl

    try:
        import certifi

        return ssl.create_default_context(cafile=certifi.where())
    except Exception:
        return ssl.create_default_context()


def _post_json(url: str, data: bytes, max_redirects: int = 5) -> str:
    """Apps Script ContentService: POST /exec → 302 → GET echo URL 로 결과 JSON 수신."""

    class _NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args, **kwargs):  # noqa: ANN002, ANN003
            return None

    https = urllib.request.HTTPSHandler(context=_ssl_context())
    opener = urllib.request.build_opener(_NoRedirect, https)
    req = urllib.request.Request(
        url,
        data=data,
        headers={
            "Content-Type": "application/json; charset=utf-8",
            "User-Agent": "YogoeYutBubblesPush/1.0",
        },
        method="POST",
    )
    try:
        with opener.open(req, timeout=60) as res:
            return res.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        if e.code not in (301, 302, 303, 307, 308):
            body = e.read().decode("utf-8", errors="replace")
            raise RuntimeError(_http_error_hint(e.code, body)) from e
        loc = e.headers.get("Location")
        if not loc:
            body = e.read().decode("utf-8", errors="replace")
            raise RuntimeError(_http_error_hint(e.code, body)) from e
        echo = urllib.request.urljoin(url, loc)
        # 결과는 echo URL 을 GET 해야 함 (POST 재전송하면 405)
        get_req = urllib.request.Request(
            echo,
            headers={"User-Agent": "YogoeYutBubblesPush/1.0"},
            method="GET",
        )
        with opener.open(get_req, timeout=60) as res:
            return res.read().decode("utf-8")



def push_to_sheet(
    write_url: str,
    tab: str,
    locales: list[str],
    rows: list[dict[str, str]],
    write_token: str = "",
    spreadsheet_id: str = "",
) -> dict:
    headers = fieldnames_for(locales)
    from sheet_descriptions import description_row
    body = [[row.get(h, "") for h in headers] for row in rows]
    desc = description_row(tab, len(headers))  # 1행 ※설명 유지
    payload = {
        "tab": tab,
        "headers": desc or headers,
        "rows": ([headers] + body) if desc else body,
    }
    if spreadsheet_id:
        payload["spreadsheetId"] = spreadsheet_id
    if write_token:
        # URL 경로의 배포 ID를 토큰으로 오인하는 경우 무시
        deploy_key = ""
        if "/macros/s/" in write_url:
            deploy_key = write_url.split("/macros/s/", 1)[1].split("/", 1)[0]
        if write_token != deploy_key:
            payload["token"] = write_token
        else:
            print(
                "경고: write_token 이 배포 URL ID와 같습니다. 무시합니다 "
                "(WRITE_TOKEN 스크립트 속성을 쓸 때만 별도 비밀을 넣으세요).",
                file=sys.stderr,
            )

    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    try:
        raw = _post_json(write_url, data)
    except urllib.error.URLError as e:
        raise RuntimeError(f"시트 쓰기 연결 실패: {e}") from e

    try:
        result = json.loads(raw)
    except json.JSONDecodeError as e:
        raise RuntimeError(
            "시트 응답이 JSON이 아님.\n"
            f"응답 앞부분: {raw[:200]}"
        ) from e
    if not result.get("ok"):
        raise RuntimeError(f"시트 쓰기 거부: {result.get('error') or result}")
    return result


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Merge yut_bubbles.*.json into CSV and optionally push to Google Sheets"
    )
    parser.add_argument("--out", type=Path, default=OUT_DEFAULT, help="출력 CSV 경로")
    parser.add_argument("--stdout", action="store_true", help="파일 대신 stdout으로 출력")
    parser.add_argument(
        "--push",
        action="store_true",
        help="Apps Script write_url 로 시트 탭에 직접 쓰기",
    )
    parser.add_argument(
        "--locales",
        help="쉼표 구분 locale (기본: config 또는 ko,en,zh,ja)",
    )
    args = parser.parse_args()

    config = load_config()
    if args.locales:
        locales = [s.strip() for s in args.locales.split(",") if s.strip()]
    else:
        locales = list(config.get("locales") or DEFAULT_LOCALES)
    fallback = config.get("fallback_locale") or DEFAULT_FALLBACK
    if fallback not in locales:
        locales.insert(0, fallback)

    by_locale = load_locale_map(locales)
    if not by_locale:
        print(f"JSON 없음: {IN_DIR / 'yut_bubbles.*.json'}", file=sys.stderr)
        return 1

    out_path = args.out
    existing_notes = load_existing_notes(out_path if not args.stdout else OUT_DEFAULT)
    rows = build_rows(by_locale, locales, existing_notes)

    if args.stdout and not args.push:
        fieldnames = fieldnames_for(locales)
        writer = csv.DictWriter(sys.stdout, fieldnames=fieldnames, quoting=csv.QUOTE_MINIMAL)
        writer.writeheader()
        for row in rows:
            writer.writerow({k: row.get(k, "") for k in fieldnames})
    else:
        if not args.stdout:
            write_csv(out_path, rows, locales)
            print(f"Wrote {out_path.relative_to(ROOT)} ({len(rows)} rows, locales={','.join(locales)})")

    if args.push:
        write_url = (config.get("write_url") or "").strip()
        if not write_url:
            print(
                "시트 쓰기 실패: config 에 write_url 이 없습니다.\n"
                "1) Tools/YutBubblesSheetsWrite.gs 를 스프레드시트 Apps Script에 넣고 웹 앱으로 배포\n"
                "2) Tools/yut_bubbles_sheets.config.json 에 \"write_url\": \"https://script.google.com/.../exec\" 추가\n"
                "자세한 안내는 Tools/YutBubblesSheetsWrite.gs 상단 주석.",
                file=sys.stderr,
            )
            return 1
        tab = config.get("tab") or "yut_bubbles"
        token = (config.get("write_token") or "").strip()
        try:
            result = push_to_sheet(
                write_url,
                tab,
                locales,
                rows,
                token,
                spreadsheet_id=(config.get("dialogue_sheet_id") or config.get("sheet_id") or "").strip(),
            )
        except RuntimeError as e:
            print(str(e), file=sys.stderr)
            return 1
        print(
            f"Pushed to '{result.get('spreadsheetName', '?')}' "
            f"({result.get('spreadsheetId')}) "
            f"tab '{result.get('tab', tab)}' "
            f"({result.get('rows', len(rows))} rows × {result.get('columns', '?')} cols)"
        )
        names = result.get("sheetNames")
        if names:
            print(f"tabs: {', '.join(names)}")
    elif not args.stdout:
        sheet_id = config.get("sheet_id")
        if sheet_id:
            print(
                f"시트 직접 쓰기: npm run yut-bubbles:push  "
                f"(write_url 설정 필요 — Tools/YutBubblesSheetsWrite.gs)"
            )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
