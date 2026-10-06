#!/usr/bin/env python3
"""구글 시트 전체 → Assets/Resources/*.json 한 번에 가져오기 (npm run sheets).

탭마다 각 export 스크립트를 순서대로 실행한다. 하나가 실패해도(시트 오류 등) 나머지는 계속 가져오고,
끝에 성공/실패를 모아 보여 준다. 실패한 탭의 json 은 건드리지 않는다(각 스크립트가 오류면 안 씀).

레시피는 선호 공양물 검증에 쓰이므로 캐릭터보다 먼저 가져온다.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# (이름, 명령) — 순서 중요: recipes → characters(선호 공양물 id 검증)
STEPS = [
    # 재료 이름이 먼저 — recipes 탭은 그 이름으로 검증한다
    ("ingredients · 재료 이름·설명", ["Tools/export_ingredients.py"]),
    ("recipes · 레시피 조합·요리 설명", ["Tools/export_recipes.py"]),
    ("characters · 캐릭터·선호·대사", ["Tools/export_characters.py"]),
    ("props · 기물 산출·재료 확률", ["Tools/export_props.py"]),
    ("attendance · 출석·윷점 64괘", ["Tools/export_attendance.py"]),
    ("yut_bubbles · 윷 말풍선", ["Tools/export_yut_bubbles.py"]),
    ("charms · 완주 부적 확률", ["Tools/export_charms.py"]),
]


def main() -> int:
    results = []
    for name, args in STEPS:
        print(f"\n▶ {name}", flush=True)
        proc = subprocess.run([sys.executable, *args], cwd=ROOT)
        results.append((name, proc.returncode == 0))

    print("\n──────── 결과 ────────")
    for name, ok in results:
        print(f"  {'✅' if ok else '❌'} {name}")
    failed = [n for n, ok in results if not ok]
    if failed:
        print(f"\n실패 {len(failed)}개 — 위 로그의 시트 오류를 고치고 다시 실행하세요 (실패한 탭의 json 은 그대로).")
        return 1
    print("\n전부 가져왔어요. git diff 로 바뀐 내용을 확인하세요.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
