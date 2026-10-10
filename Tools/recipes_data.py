"""공양간 레시피 공용 데이터 — Assets/Resources/recipes.json (정본: 시트 recipes 탭, npm run recipes).

다른 도구(export_characters 선호 검증 등)는 여기서 레시피를 읽는다.
"""

from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RECIPES_JSON = ROOT / "Assets" / "Resources" / "recipes.json"

INGREDIENTS_JSON = ROOT / "Assets" / "Resources" / "ingredients.json"

# CookingIngredientId 순서 (enum 이름, 기본 한글 이름). 실제 이름은 시트 ingredients 탭 name → ingredients.json.
DEFAULT_INGREDIENTS = [
    ("Water", "물"), ("Chili", "고추"), ("Rice", "쌀"), ("Grain", "잡곡"), ("Fruit", "과실"),
    ("Namul", "산나물"), ("Herb", "약재"), ("Honey", "꿀"), ("Boar", "멧돼지고기"), ("Bird", "새고기"),
    ("Seafood", "해산물"), ("Egg", "새알"), ("Oil", "기름"),
]


# 바뀐 재료 id (예전 id → 지금 id). 시트에 예전 id 가 남아 있어도 받아 준다.
OLD_INGREDIENT_IDS = {"RedBean": "Grain", "Fish": "Seafood"}
# 바뀌기 전 기본 이름도 레시피 재료 칸에서 받아 준다.
OLD_INGREDIENT_NAMES = {"팥": "Grain", "물고기": "Seafood"}


def current_id(iid: str) -> str:
    return OLD_INGREDIENT_IDS.get(iid, iid)


def ingredient_names() -> dict[str, str]:
    """id → 지금 이름 (ingredients.json 의 name, 없으면 기본 이름)."""
    names = dict(DEFAULT_INGREDIENTS)
    if INGREDIENTS_JSON.exists():
        for e in json.loads(INGREDIENTS_JSON.read_text(encoding="utf-8")).get("ingredients", []):
            iid = current_id(e.get("id", ""))
            if iid in names and e.get("name"):
                names[iid] = e["name"]
    return names


ING_KO = ingredient_names()
INGREDIENTS = list(ING_KO.items())
ING_ORDER = [i for i, _ in INGREDIENTS]
# 레시피 시트는 지금 이름으로 쓴다. 이름을 바꾼 직전 이름(기본 이름)도 받아 준다.
KO_TO_ING = dict(OLD_INGREDIENT_NAMES)
KO_TO_ING.update({ko: i for i, ko in DEFAULT_INGREDIENTS})
KO_TO_ING.update({ko: i for i, ko in INGREDIENTS})

KIND_KO = {"Food": "음식", "Offering": "공양물"}
KO_TO_KIND = {v: k for k, v in KIND_KO.items()}
KIND_INGREDIENTS = {"Food": 2, "Offering": 3}  # 19장: 음식 = 재료 2, 공양물 = 재료 3


def load_recipes() -> list[dict]:
    return json.loads(RECIPES_JSON.read_text(encoding="utf-8")).get("recipes", [])


def products() -> list[dict]:
    """결과물 id 순서대로 (같은 id의 조합은 combos 에 모음)."""
    order, info = [], {}
    for r in load_recipes():
        pid = r["id"]
        if pid not in info:
            order.append(pid)
            info[pid] = {"id": pid, "name": r["name"], "kind": r["kind"], "combos": []}
        info[pid]["combos"].append(sorted(r["ingredients"], key=ING_ORDER.index))
    return [info[p] for p in order]


def combo_text(ingredients: list[str]) -> str:
    return " + ".join(ING_KO[i] for i in sorted(ingredients, key=ING_ORDER.index))
