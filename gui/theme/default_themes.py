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

# 深紫色（Dracula 風格）
PURPLE_COLORS: dict[str, str] = {
    "bg_window":    "#282a36",
    "bg_panel":     "#1e1f29",
    "bg_input":     "#1e1f29",
    "bg_button":    "#44475a",
    "bg_hover":     "#6272a4",
    "bg_pressed":   "#7b8ec8",
    "bg_select":    "#6272a4",
    "bg_menubar":   "#1e1f29",
    "bg_statusbar": "#1e1f29",
    "fg_text":      "#f8f8f2",
    "fg_dim":       "#bdc3d2",
    "fg_accent":    "#bd93f9",
    "fg_disabled":  "#6272a4",
    "border":       "#44475a",
    "border_focus": "#bd93f9",
}

# 玫瑰粉（Rose Pine 風格）
ROSE_COLORS: dict[str, str] = {
    "bg_window":    "#191724",
    "bg_panel":     "#12101e",
    "bg_input":     "#12101e",
    "bg_button":    "#26233a",
    "bg_hover":     "#403d52",
    "bg_pressed":   "#524f67",
    "bg_select":    "#403d52",
    "bg_menubar":   "#12101e",
    "bg_statusbar": "#12101e",
    "fg_text":      "#e0def4",
    "fg_dim":       "#908caa",
    "fg_accent":    "#eb6f92",
    "fg_disabled":  "#6e6a86",
    "border":       "#403d52",
    "border_focus": "#eb6f92",
}

# 翡翠綠（Everforest 風格）
GREEN_COLORS: dict[str, str] = {
    "bg_window":    "#2d353b",
    "bg_panel":     "#232a2e",
    "bg_input":     "#232a2e",
    "bg_button":    "#3d484d",
    "bg_hover":     "#475258",
    "bg_pressed":   "#56635f",
    "bg_select":    "#475258",
    "bg_menubar":   "#232a2e",
    "bg_statusbar": "#232a2e",
    "fg_text":      "#d3c6aa",
    "fg_dim":       "#9da9a0",
    "fg_accent":    "#a7c080",
    "fg_disabled":  "#5c6a72",
    "border":       "#475258",
    "border_focus": "#a7c080",
}

# 海洋藍（Nord 風格）
NORD_COLORS: dict[str, str] = {
    "bg_window":    "#2e3440",
    "bg_panel":     "#242933",
    "bg_input":     "#242933",
    "bg_button":    "#3b4252",
    "bg_hover":     "#434c5e",
    "bg_pressed":   "#4c566a",
    "bg_select":    "#434c5e",
    "bg_menubar":   "#242933",
    "bg_statusbar": "#242933",
    "fg_text":      "#eceff4",
    "fg_dim":       "#d8dee9",
    "fg_accent":    "#88c0d0",
    "fg_disabled":  "#616e88",
    "border":       "#434c5e",
    "border_focus": "#88c0d0",
}

# 琥珀橘（Monokai 風格）
MONOKAI_COLORS: dict[str, str] = {
    "bg_window":    "#272822",
    "bg_panel":     "#1e1f1c",
    "bg_input":     "#1e1f1c",
    "bg_button":    "#3e3d32",
    "bg_hover":     "#49483e",
    "bg_pressed":   "#75715e",
    "bg_select":    "#49483e",
    "bg_menubar":   "#1e1f1c",
    "bg_statusbar": "#1e1f1c",
    "fg_text":      "#f8f8f2",
    "fg_dim":       "#cfcfc2",
    "fg_accent":    "#fd971f",
    "fg_disabled":  "#75715e",
    "border":       "#49483e",
    "border_focus": "#fd971f",
}

# 內建主題顏色對應表
BUILTIN_THEME_COLORS: dict[str, dict[str, str]] = {
    "Dark":    DARK_COLORS,
    "Light":   LIGHT_COLORS,
    "Purple":  PURPLE_COLORS,
    "Rose":    ROSE_COLORS,
    "Green":   GREEN_COLORS,
    "Nord":    NORD_COLORS,
    "Monokai": MONOKAI_COLORS,
}

# ------------------------------------------------------------------ #
# iOS 26 液態玻璃（Liquid Glass）— 手寫 QSS，使用 rgba 透明層
# 搭配背景圖片效果最佳
# ------------------------------------------------------------------ #

LIQUID_GLASS_QSS = """
/* ── 基底 ── */
QMainWindow, QDialog {
    background-color: rgba(10, 10, 20, 210);
    color: #ffffff;
    font-family: "Segoe UI", "SF Pro Display", sans-serif;
    font-size: 10pt;
}
QWidget {
    color: #ffffff;
    font-family: "Segoe UI", "SF Pro Display", sans-serif;
    font-size: 10pt;
}

/* ── 選單欄 ── */
QMenuBar {
    background-color: rgba(255, 255, 255, 12);
    color: #ffffff;
    border-bottom: 1px solid rgba(255, 255, 255, 30);
    padding: 2px 4px;
}
QMenuBar::item { border-radius: 6px; padding: 4px 8px; }
QMenuBar::item:selected { background-color: rgba(255, 255, 255, 25); }

QMenu {
    background-color: rgba(20, 20, 35, 220);
    border: 1px solid rgba(255, 255, 255, 35);
    border-radius: 12px;
    color: #ffffff;
    padding: 4px;
}
QMenu::item { padding: 6px 20px; border-radius: 6px; }
QMenu::item:selected { background-color: rgba(0, 122, 255, 160); }
QMenu::separator { height: 1px; background: rgba(255,255,255,20); margin: 3px 8px; }

/* ── 工具列 ── */
QToolBar {
    background-color: rgba(255, 255, 255, 10);
    border-bottom: 1px solid rgba(255, 255, 255, 18);
    spacing: 6px;
    padding: 2px 6px;
}
QToolBar QLabel { color: rgba(255, 255, 255, 200); }

/* ── 表格 / 清單 ── */
QTableView, QTreeView, QListView {
    background-color: rgba(255, 255, 255, 10);
    alternate-background-color: rgba(255, 255, 255, 5);
    color: #ffffff;
    gridline-color: rgba(255, 255, 255, 18);
    border: 1px solid rgba(255, 255, 255, 30);
    border-radius: 12px;
    selection-background-color: rgba(0, 122, 255, 140);
    selection-color: #ffffff;
}
QHeaderView::section {
    background-color: rgba(255, 255, 255, 18);
    color: rgba(255, 255, 255, 220);
    border: none;
    border-bottom: 1px solid rgba(255, 255, 255, 25);
    padding: 5px 8px;
    font-weight: 600;
}
QTableView::item { padding: 3px 6px; }
QTableView::item:selected { background-color: rgba(0, 122, 255, 140); border-radius: 4px; }

/* ── 按鈕 ── */
QPushButton {
    background-color: rgba(255, 255, 255, 18);
    color: #ffffff;
    border: 1px solid rgba(255, 255, 255, 40);
    border-radius: 10px;
    padding: 5px 14px;
    min-height: 24px;
    font-weight: 500;
}
QPushButton:hover {
    background-color: rgba(255, 255, 255, 32);
    border-color: rgba(255, 255, 255, 60);
}
QPushButton:pressed {
    background-color: rgba(0, 122, 255, 160);
    border-color: rgba(0, 122, 255, 200);
}
QPushButton:disabled { color: rgba(255, 255, 255, 70); border-color: rgba(255,255,255,20); }

/* ── 輸入框 ── */
QLineEdit, QTextEdit, QPlainTextEdit {
    background-color: rgba(255, 255, 255, 12);
    color: #ffffff;
    border: 1px solid rgba(255, 255, 255, 30);
    border-radius: 10px;
    padding: 5px 8px;
    selection-background-color: rgba(0, 122, 255, 140);
}
QLineEdit:focus, QTextEdit:focus {
    border: 1px solid rgba(0, 122, 255, 200);
    background-color: rgba(255, 255, 255, 18);
}

/* ── 下拉選單 ── */
QComboBox {
    background-color: rgba(255, 255, 255, 18);
    color: #ffffff;
    border: 1px solid rgba(255, 255, 255, 35);
    border-radius: 10px;
    padding: 4px 8px;
    min-height: 24px;
}
QComboBox:hover { background-color: rgba(255, 255, 255, 28); }
QComboBox::drop-down { border: none; width: 20px; }
QComboBox::down-arrow { image: none; border-left: 4px solid transparent; border-right: 4px solid transparent; border-top: 5px solid rgba(255,255,255,180); margin-right: 6px; }
QComboBox QAbstractItemView {
    background-color: rgba(20, 20, 35, 230);
    color: #ffffff;
    border: 1px solid rgba(255, 255, 255, 35);
    border-radius: 10px;
    selection-background-color: rgba(0, 122, 255, 140);
    outline: none;
}

/* ── 核取方塊 ── */
QCheckBox { color: #ffffff; spacing: 6px; }
QCheckBox::indicator {
    width: 16px; height: 16px;
    border: 1.5px solid rgba(255, 255, 255, 50);
    border-radius: 5px;
    background-color: rgba(255, 255, 255, 12);
}
QCheckBox::indicator:checked {
    background-color: rgba(0, 122, 255, 200);
    border-color: rgba(0, 122, 255, 220);
}

/* ── 頁籤 ── */
QTabWidget::pane {
    border: 1px solid rgba(255, 255, 255, 25);
    border-radius: 12px;
    background-color: rgba(255, 255, 255, 8);
}
QTabBar::tab {
    background: rgba(255, 255, 255, 12);
    color: rgba(255, 255, 255, 180);
    padding: 7px 18px;
    border: 1px solid rgba(255, 255, 255, 20);
    border-radius: 8px;
    margin: 2px 2px 0 2px;
}
QTabBar::tab:selected {
    background: rgba(0, 122, 255, 160);
    color: #ffffff;
    border-color: rgba(0, 122, 255, 200);
}
QTabBar::tab:hover:!selected { background: rgba(255, 255, 255, 22); }

/* ── 捲軸 ── */
QScrollBar:vertical {
    background: transparent;
    width: 8px;
    border: none;
    margin: 2px;
}
QScrollBar::handle:vertical {
    background: rgba(255, 255, 255, 50);
    border-radius: 4px;
    min-height: 24px;
}
QScrollBar::handle:vertical:hover { background: rgba(255, 255, 255, 80); }
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0; }
QScrollBar:horizontal {
    background: transparent;
    height: 8px;
    border: none;
    margin: 2px;
}
QScrollBar::handle:horizontal {
    background: rgba(255, 255, 255, 50);
    border-radius: 4px;
    min-width: 24px;
}
QScrollBar::add-line:horizontal, QScrollBar::sub-line:horizontal { width: 0; }

/* ── 狀態列 ── */
QStatusBar {
    background-color: rgba(255, 255, 255, 8);
    color: rgba(255, 255, 255, 160);
    border-top: 1px solid rgba(255, 255, 255, 18);
}

/* ── 群組框 ── */
QGroupBox {
    color: rgba(255, 255, 255, 220);
    border: 1px solid rgba(255, 255, 255, 28);
    border-radius: 12px;
    margin-top: 10px;
    padding-top: 10px;
    background-color: rgba(255, 255, 255, 6);
}
QGroupBox::title {
    subcontrol-origin: margin;
    left: 12px;
    padding: 0 4px;
    color: rgba(0, 200, 255, 230);
    font-weight: 600;
}

/* ── 分割器 ── */
QSplitter::handle { background: rgba(255, 255, 255, 20); }

/* ── 進度條 ── */
QProgressBar {
    background-color: rgba(255, 255, 255, 12);
    border: 1px solid rgba(255, 255, 255, 25);
    border-radius: 6px;
    text-align: center;
    color: #ffffff;
    min-height: 10px;
}
QProgressBar::chunk {
    background-color: qlineargradient(x1:0, y1:0, x2:1, y2:0,
        stop:0 rgba(0, 122, 255, 220), stop:1 rgba(0, 200, 255, 220));
    border-radius: 5px;
}

/* ── 數字輸入 ── */
QSpinBox, QDoubleSpinBox {
    background-color: rgba(255, 255, 255, 12);
    color: #ffffff;
    border: 1px solid rgba(255, 255, 255, 30);
    border-radius: 10px;
    padding: 3px 6px;
}
QSpinBox:focus, QDoubleSpinBox:focus { border-color: rgba(0, 122, 255, 200); }

/* ── 提示框 ── */
QToolTip {
    background-color: rgba(20, 20, 35, 230);
    color: #ffffff;
    border: 1px solid rgba(255, 255, 255, 40);
    border-radius: 8px;
    padding: 4px 8px;
}

/* ── 標籤 ── */
QLabel { color: #ffffff; background: transparent; }
"""

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

# 液態玻璃主題（手寫 QSS，不走 token 系統）
THEMES["Liquid Glass"] = LIQUID_GLASS_QSS
