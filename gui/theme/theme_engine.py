"""
QSS 主題引擎 — 支援顏色 Token 自訂與背景媒體。
"""

from __future__ import annotations

import json
import os
import copy
from typing import Optional

from PySide6.QtWidgets import QApplication

from gui.theme.default_themes import (
    THEMES, BUILTIN_THEME_COLORS, colors_to_qss,
    DARK_COLORS, COLOR_TOKEN_LABELS,
)
from core.settings import get_settings

CUSTOM_THEMES_FILE = os.path.join("data", "custom_themes.json")

# ------------------------------------------------------------------ #
# 自訂主題顏色快取  { name: {colors: {...}, background: {path, opacity}} }
# ------------------------------------------------------------------ #
_custom_theme_data: dict[str, dict] = {}

# 背景 Widget 的弱參考（由 main_window 設定）
_bg_widget = None   # BackgroundWidget instance


def _load_custom_themes():
    global _custom_theme_data
    if not os.path.isfile(CUSTOM_THEMES_FILE):
        return
    try:
        with open(CUSTOM_THEMES_FILE, "r", encoding="utf-8") as f:
            data = json.load(f)
        for name, info in data.items():
            _custom_theme_data[name] = info
            colors = info.get("colors", {})
            if colors:
                THEMES[name] = colors_to_qss({**DARK_COLORS, **colors})
    except Exception:
        pass


_load_custom_themes()


# ------------------------------------------------------------------ #
# Public API
# ------------------------------------------------------------------ #

def set_background_widget(widget):
    """由 MainWindow 呼叫，登記背景 Widget。"""
    global _bg_widget
    _bg_widget = widget


def apply_theme(theme_name: str | None = None):
    """套用主題（QSS + 背景）到整個 QApplication。"""
    settings = get_settings()
    name = theme_name or settings.get("General", "Theme", "Dark")
    qss = THEMES.get(name, THEMES["Dark"])
    app = QApplication.instance()
    if app:
        app.setStyleSheet(qss)
    settings.set("General", "Theme", name)

    # 套用背景
    if _bg_widget is not None:
        bg_info = get_theme_background(name)
        path    = bg_info.get("path", "")
        opacity = bg_info.get("opacity", 0.5)
        if path and os.path.isfile(path):
            _bg_widget.set_source(path, opacity)
        else:
            _bg_widget.clear()


def get_current_theme() -> str:
    return get_settings().get("General", "Theme", "Dark")


def get_theme_names() -> list[str]:
    return list(THEMES.keys())


def get_theme_colors(name: str) -> dict[str, str]:
    """取得主題的顏色 token dict（自訂 > 內建 > Dark fallback）。"""
    if name in _custom_theme_data and "colors" in _custom_theme_data[name]:
        base = copy.deepcopy(DARK_COLORS)
        base.update(_custom_theme_data[name]["colors"])
        return base
    return copy.deepcopy(BUILTIN_THEME_COLORS.get(name, DARK_COLORS))


def get_theme_background(name: str) -> dict:
    """取得主題背景設定 {path, opacity}。"""
    if name in _custom_theme_data:
        return _custom_theme_data[name].get("background", {})
    return {}


def save_custom_theme(
    name: str,
    colors: dict[str, str],
    background_path: str = "",
    background_opacity: float = 0.5,
):
    """儲存自訂主題（顏色 + 背景）到 JSON 檔案，並即時套用。"""
    os.makedirs("data", exist_ok=True)

    # 只儲存與 DARK_COLORS 不同的顏色（節省空間）
    diff_colors = {k: v for k, v in colors.items() if v != DARK_COLORS.get(k)}

    _custom_theme_data[name] = {
        "colors":     diff_colors,
        "background": {
            "path":    background_path,
            "opacity": background_opacity,
        },
    }
    THEMES[name] = colors_to_qss({**DARK_COLORS, **diff_colors})

    try:
        with open(CUSTOM_THEMES_FILE, "w", encoding="utf-8") as f:
            json.dump(_custom_theme_data, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


def delete_custom_theme(name: str):
    """刪除自訂主題（不能刪除內建主題）。"""
    if name in BUILTIN_THEME_COLORS:
        return
    _custom_theme_data.pop(name, None)
    THEMES.pop(name, None)
    try:
        with open(CUSTOM_THEMES_FILE, "w", encoding="utf-8") as f:
            json.dump(_custom_theme_data, f, ensure_ascii=False, indent=2)
    except Exception:
        pass


def register_custom_theme(name: str, qss: str):
    """向後相容：直接用 QSS 字串新增主題。"""
    THEMES[name] = qss
