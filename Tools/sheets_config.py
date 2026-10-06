"""기획 시트 주소 — 탭마다 어느 스프레드시트에 있는지.

Tools/yut_bubbles_sheets.config.json
  sheet_id           밸런스 시트 (기물·레시피·부적·전역 수치·출석·선호 · deploy_status)
  dialogue_sheet_id  대사·텍스트 시트 (아래 DIALOGUE_TABS). 비어 있으면 sheet_id 하나에 다 있다고 본다.
"""

from __future__ import annotations

# 대사·텍스트 시트에 있는 탭
DIALOGUE_TABS = {
    "characters", "character_lines",
    "yut_bubbles", "yut_fortune", "OPENING", "okto",
}


def sheet_id_for(config: dict, tab: str) -> str:
    if tab in DIALOGUE_TABS and config.get("dialogue_sheet_id"):
        return config["dialogue_sheet_id"]
    return config.get("sheet_id") or ""
