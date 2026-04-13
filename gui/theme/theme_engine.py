"""
QSS 主題引擎。
對應 C# Forms/ThemeEditor.cs 的 LoadTheme / ApplyTheme。
"""

from __future__ import annotations

from PySide6.QtWidgets import QApplication

from gui.theme.default_themes import THEMES
from core.settings import get_settings


def apply_theme(theme_name: str | None = None):
    """套用主題到整個 QApplication。"""
    settings = get_settings()
    name = theme_name or settings.get("General", "Theme", "Dark")
    qss = THEMES.get(name, THEMES["Dark"])
    app = QApplication.instance()
    if app:
        app.setStyleSheet(qss)
    settings.set("General", "Theme", name)


def get_current_theme() -> str:
    return get_settings().get("General", "Theme", "Dark")


def get_theme_names() -> list[str]:
    return list(THEMES.keys())


def register_custom_theme(name: str, qss: str):
    """允許使用者新增自訂主題。"""
    THEMES[name] = qss
