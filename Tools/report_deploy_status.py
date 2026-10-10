#!/usr/bin/env python3
"""배포 상태를 시트 deploy_status 탭 맨 위에 한 줄 적는다 (최근 30줄 보관).

시트 메뉴 「한폭요괴 → 게임에 반영」 → GitHub Actions(sheets-deploy · build-and-deploy)가 단계마다 부른다.

사용법:
  python3 Tools/report_deploy_status.py --result "배포 완료" --detail "..." --link URL
  python3 Tools/report_deploy_status.py --result "시트 오류" --log pull.log    # pull_all_sheets 로그에서 오류 줄 뽑기
"""

from __future__ import annotations

import argparse
import csv
import io
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from export_characters import load_config, push_tab  # noqa: E402
from export_yut_bubbles import fetch_sheet_csv  # noqa: E402

TAB = "deploy_status"
HEADERS = ["time", "result", "detail", "link"]
KEEP = 30
KST = timezone(timedelta(hours=9))


def errors_from_log(path: Path) -> str:
    """pull_all_sheets 로그 → '❌ recipes · …' 줄과 '[tab] n행: …' 오류 줄 (최대 8줄)."""
    if not path.exists():
        return ""
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    failed = [l.strip() for l in lines if l.strip().startswith("❌")]
    errs = [l.strip() for l in lines if re.match(r"^\s*\[[a-z_]+\] ", l)]
    out = failed + errs[:8]
    if len(errs) > 8:
        out.append(f"… 외 {len(errs) - 8}줄")
    return "\n".join(out)


def existing_rows(config: dict) -> list[dict]:
    try:
        text = fetch_sheet_csv(config["sheet_id"], TAB)
    except Exception:
        return []
    rows = list(csv.reader(io.StringIO(text)))
    # 1행 ※설명 줄 건너뛰고 헤더 찾기. 탭이 아직 없으면 시트가 다른 탭을 돌려주므로 헤더로 확인한다.
    while rows and rows[0] and rows[0][0].startswith("※"):
        rows.pop(0)
    if not rows or [h.strip() for h in rows[0][:4]] != HEADERS:
        return []
    return [dict(zip(HEADERS, r + [""] * 4)) for r in rows[1:] if any(c.strip() for c in r)]


def main() -> int:
    ap = argparse.ArgumentParser(description="deploy_status 탭에 한 줄 적기")
    ap.add_argument("--result", required=True)
    ap.add_argument("--detail", default="")
    ap.add_argument("--link", default="")
    ap.add_argument("--log", default="")
    args = ap.parse_args()
    config = load_config()
    if not config.get("write_url") or not config.get("sheet_id"):
        print("config 에 sheet_id / write_url 이 없어 상태를 적지 못했습니다", file=sys.stderr)
        return 0  # 상태 기록 실패로 배포를 막지 않는다

    detail = args.detail
    if args.log:
        found = errors_from_log(Path(args.log))
        detail = (detail + "\n" + found).strip() if found else detail
    row = {"time": datetime.now(KST).strftime("%m-%d %H:%M"), "result": args.result,
           "detail": detail, "link": args.link}
    rows = [row] + existing_rows(config)[: KEEP - 1]
    try:
        push_tab(config, TAB, HEADERS, rows)
    except Exception as e:  # 상태 기록 실패로 배포를 막지 않는다
        print(f"경고: 상태 기록 실패 ({e})", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
