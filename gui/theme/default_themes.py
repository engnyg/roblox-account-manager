"""
預設 QSS 主題 — 基於顏色 Token 系統。
"""

from __future__ import annotations

# ------------------------------------------------------------------ #
# 顏色 Token 標籤（中文顯示用）
# ------------------------------------------------------------------ #

COLOR_TOKEN_LABELS: dict[str, str] = {
    "bg_window":     "視窗背景",
    "bg_panel":      "面板背景",
    "bg_input":      "輸入框背景",
    "bg_button":     "按鈕背景",
    "bg_hover":      "按鈕懸停",
    "bg_pressed":    "按鈕按下",
    "bg_select":     "選取背景",
    "bg_menubar":    "選單欄背景",
    "bg_statusbar":  "狀態欄背景",
    "fg_text":       "主要文字",
    "fg_dim":        "次要文字",
    "fg_accent":     "強調色",
    "fg_disabled":   "停用文字",
    "border":        "邊框",
    "border_focus":  "焦點邊框",
}

# ------------------------------------------------------------------ #
# 預設顏色
# ------------------------------------------------------------------ #

DARK_COLORS: dict[str, str] = {
    "bg_window":    "#1e1e2e",
    "bg_panel":     "#181825",
    "bg_input":     "#181825",
    "bg_button":    "#313244",
    "bg_hover":     "#45475a",
    "bg_pressed":   "#585b70",
    "bg_select":    "#45475a",
    "bg_menubar":   "#181825",
    "bg_statusbar": "#181825",
    "fg_text":      "#cdd6f4",
    "fg_dim":       "#a6adc8",
    "fg_accent":    "#89b4fa",
    "fg_disabled":  "#6c7086",
    "border":       "#45475a",
    "border_focus": "#89b4fa",
}

LIGHT_COLORS: dict[str, str] = {
    "bg_window":    "#eff1f5",
    "bg_panel":     "#dce0e8",
    "bg_input":     "#dce0e8",
    "bg_button":    "#dce0e8",
    "bg_hover":     "#ccd0da",
    "bg_pressed":   "#acb0be",
    "bg_select":    "#8caaee",
    "bg_menubar":   "#dce0e8",
    "bg_statusbar": "#dce0e8",
    "fg_text":      "#4c4f69",
    "fg_dim":       "#6c6f85",
    "fg_accent":    "#1e66f5",
    "fg_disabled":  "#9ca0b0",
    "border":       "#ccd0da",
    "border_focus": "#1e66f5",
}

# 內建主題顏色對應表
BUILTIN_THEME_COLORS: dict[str, dict[str, str]] = {
    "Dark":  DARK_COLORS,
    "Light": LIGHT_COLORS,
}

# ------------------------------------------------------------------ #
# QSS 模板（使用 .format(**colors) 填入）
# ------------------------------------------------------------------ #

QSS_TEMPLATE = """\
QMainWindow, QDialog, QWidget {{
    background-color: {bg_window};
    color: {fg_text};
    font-family: "Segoe UI", sans-serif;
    font-size: 10pt;
}}

QMenuBar {{
    background-color: {bg_menubar};
    color: {fg_text};
    border-bottom: 1px solid {border};
}}
QMenuBar::item:selected {{ background-color: {bg_button}; }}

QMenu {{
    background-color: {bg_window};
    border: 1px solid {border};
    color: {fg_text};
}}
QMenu::item:selected {{ background-color: {bg_button}; }}

QTableView, QTreeView, QListView {{
    background-color: {bg_panel};
    alternate-background-color: {bg_window};
    color: {fg_text};
    gridline-color: {border};
    border: 1px solid {border};
    selection-background-color: {bg_select};
    selection-color: {fg_text};
}}
QHeaderView::section {{
    background-color: {bg_button};
    color: {fg_text};
    border: none;
    padding: 4px;
}}

QPushButton {{
    background-color: {bg_button};
    color: {fg_text};
    border: 1px solid {border};
    border-radius: 4px;
    padding: 4px 12px;
    min-height: 22px;
}}
QPushButton:hover    {{ background-color: {bg_hover}; }}
QPushButton:pressed  {{ background-color: {bg_pressed}; }}
QPushButton:disabled {{ color: {fg_disabled}; }}

QLineEdit, QTextEdit, QPlainTextEdit {{
    background-color: {bg_input};
    color: {fg_text};
    border: 1px solid {border};
    border-radius: 4px;
    padding: 3px;
}}
QLineEdit:focus {{ border-color: {border_focus}; }}

QComboBox {{
    background-color: {bg_button};
    color: {fg_text};
    border: 1px solid {border};
    border-radius: 4px;
    padding: 3px 6px;
}}
QComboBox::drop-down {{ border: none; }}
QComboBox QAbstractItemView {{ background-color: {bg_window}; color: {fg_text}; }}

QCheckBox {{ color: {fg_text}; }}
QCheckBox::indicator {{
    width: 14px; height: 14px;
    border: 1px solid {border};
    border-radius: 2px;
    background: {bg_input};
}}
QCheckBox::indicator:checked {{ background: {fg_accent}; }}

QTabWidget::pane {{ border: 1px solid {border}; }}
QTabBar::tab {{
    background: {bg_button};
    color: {fg_text};
    padding: 6px 16px;
    border: 1px solid {border};
}}
QTabBar::tab:selected {{ background: {bg_hover}; color: {fg_accent}; }}

QScrollBar:vertical {{
    background: {bg_panel};
    width: 10px;
    border: none;
}}
QScrollBar::handle:vertical {{
    background: {bg_hover};
    border-radius: 5px;
    min-height: 20px;
}}
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{ height: 0; }}

QStatusBar {{ background-color: {bg_statusbar}; color: {fg_dim}; }}

QGroupBox {{
    color: {fg_accent};
    border: 1px solid {border};
    border-radius: 4px;
    margin-top: 8px;
    padding-top: 8px;
}}
QGroupBox::title {{ subcontrol-origin: margin; left: 8px; }}

QSplitter::handle {{ background: {bg_button}; }}

QProgressBar {{
    background-color: {bg_panel};
    border: 1px solid {border};
    border-radius: 4px;
    text-align: center;
    color: {fg_text};
}}
QProgressBar::chunk {{ background-color: {fg_accent}; border-radius: 3px; }}

QToolTip {{ background-color: {bg_button}; color: {fg_text}; border: 1px solid {border}; }}

QToolBar {{ background-color: {bg_menubar}; border-bottom: 1px solid {border}; spacing: 4px; }}
QToolBar QLabel {{ color: {fg_text}; }}

QSpinBox {{
    background-color: {bg_input};
    color: {fg_text};
    border: 1px solid {border};
    border-radius: 4px;
    padding: 2px 4px;
}}
"""


def colors_to_qss(colors: dict[str, str]) -> str:
    """將顏色 token dict 轉換為 QSS 字串。"""
    return QSS_TEMPLATE.format(**colors)


# ------------------------------------------------------------------ #
# THEMES dict（向後相容）
# ------------------------------------------------------------------ #

THEMES: dict[str, str] = {
    name: colors_to_qss(colors)
    for name, colors in BUILTIN_THEME_COLORS.items()
}
