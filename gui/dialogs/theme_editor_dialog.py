"""
主題編輯器對話框 — 視覺化顏色選擇器 + 背景媒體設定。
"""

from __future__ import annotations

import os

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QIcon
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QGridLayout,
    QComboBox, QPushButton, QLabel, QWidget,
    QDialogButtonBox, QScrollArea, QFrame,
    QLineEdit, QDoubleSpinBox, QFileDialog,
    QInputDialog, QMessageBox, QSizePolicy,
    QGroupBox,
)

from gui.theme.default_themes import COLOR_TOKEN_LABELS, BUILTIN_THEME_COLORS
from gui.theme.theme_engine import (
    apply_theme, get_theme_names, get_current_theme,
    get_theme_colors, get_theme_background,
    save_custom_theme, delete_custom_theme,
)


def _parse_color(color_str: str) -> QColor:
    """解析 #rrggbb / rgba(r,g,b,a) 為 QColor。"""
    s = color_str.strip()
    if s.startswith("rgba(") and s.endswith(")"):
        try:
            parts = [x.strip() for x in s[5:-1].split(",")]
            r, g, b, a = int(parts[0]), int(parts[1]), int(parts[2]), int(parts[3])
            return QColor(r, g, b, a)
        except Exception:
            pass
    return QColor(s)


def _color_to_str(color: QColor) -> str:
    """QColor → #rrggbb（不透明）或 rgba(r,g,b,a)（含透明度）。"""
    if color.alpha() < 255:
        return f"rgba({color.red()},{color.green()},{color.blue()},{color.alpha()})"
    return color.name()   # #rrggbb


def _color_button(color: str) -> QPushButton:
    """建立顯示指定顏色的按鈕（正方形色塊，帶棋盤格底圖表示透明度）。"""
    btn = QPushButton()
    btn.setFixedSize(28, 28)
    btn.setToolTip(color)
    _set_btn_color(btn, color)
    return btn


def _set_btn_color(btn: QPushButton, color: str):
    btn.setToolTip(color)
    # 棋盤格底圖讓透明色可視化
    btn.setStyleSheet(
        "QPushButton {"
        " background-color: qlineargradient(x1:0,y1:0,x2:1,y2:1,"
        "  stop:0 #aaa,stop:0.5 #aaa,stop:0.5 #666,stop:1 #666);"  # 棋盤底
        f" background-color: {color};"   # 覆蓋實色/rgba（支援透明）
        " border: 1px solid #888; border-radius: 3px; }"
        "QPushButton:hover { border: 2px solid #fff; }"
    )


class ThemeEditorDialog(QDialog):
    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.setWindowTitle("主題編輯器")
        self.resize(560, 640)
        self._color_btns: dict[str, QPushButton] = {}
        self._current_colors: dict[str, str] = {}
        self._setup_ui()
        self._load_theme(get_current_theme())

    # ------------------------------------------------------------------ #
    # UI 建構
    # ------------------------------------------------------------------ #

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setSpacing(8)

        # ── 主題選擇列 ──────────────────────────────────────────────── #
        top = QHBoxLayout()
        top.addWidget(QLabel("主題："))
        self._theme_select = QComboBox()
        self._theme_select.addItems(get_theme_names())
        self._theme_select.currentTextChanged.connect(self._load_theme)
        top.addWidget(self._theme_select, 1)

        self._btn_new = QPushButton("另存新主題")
        self._btn_new.clicked.connect(self._save_as)
        top.addWidget(self._btn_new)

        self._btn_delete = QPushButton("刪除")
        self._btn_delete.clicked.connect(self._delete_theme)
        top.addWidget(self._btn_delete)
        layout.addLayout(top)

        # ── 顏色 Token 列表 ─────────────────────────────────────────── #
        color_group = QGroupBox("顏色設定")
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.NoFrame)

        inner = QWidget()
        grid = QGridLayout(inner)
        grid.setColumnStretch(1, 1)
        grid.setHorizontalSpacing(10)
        grid.setVerticalSpacing(6)

        for row, (token, label) in enumerate(COLOR_TOKEN_LABELS.items()):
            lbl = QLabel(label)
            lbl.setMinimumWidth(110)
            grid.addWidget(lbl, row, 0)

            btn = _color_button("#000000")
            btn.clicked.connect(lambda checked, t=token: self._pick_color(t))
            self._color_btns[token] = btn
            grid.addWidget(btn, row, 1, Qt.AlignLeft)

            hex_lbl = QLabel("#000000")
            hex_lbl.setObjectName(f"hex_{token}")
            grid.addWidget(hex_lbl, row, 2)

        scroll.setWidget(inner)
        color_group_layout = QVBoxLayout(color_group)
        color_group_layout.addWidget(scroll)
        layout.addWidget(color_group, 1)

        # ── 背景設定 ────────────────────────────────────────────────── #
        bg_group = QGroupBox("背景（圖片 / GIF / MP4）")
        bg_form = QGridLayout(bg_group)

        bg_form.addWidget(QLabel("檔案："), 0, 0)
        self._bg_path = QLineEdit()
        self._bg_path.setPlaceholderText("留空 = 無背景")
        bg_form.addWidget(self._bg_path, 0, 1)

        browse_btn = QPushButton("瀏覽…")
        browse_btn.setFixedWidth(64)
        browse_btn.clicked.connect(self._browse_background)
        bg_form.addWidget(browse_btn, 0, 2)

        bg_form.addWidget(QLabel("不透明度："), 1, 0)
        self._bg_opacity = QDoubleSpinBox()
        self._bg_opacity.setRange(0.05, 1.0)
        self._bg_opacity.setSingleStep(0.05)
        self._bg_opacity.setValue(0.5)
        self._bg_opacity.setDecimals(2)
        bg_form.addWidget(self._bg_opacity, 1, 1)

        layout.addWidget(bg_group)

        # ── 操作按鈕 ────────────────────────────────────────────────── #
        btn_row = QHBoxLayout()
        preview_btn = QPushButton("預覽")
        preview_btn.clicked.connect(self._preview)
        btn_row.addWidget(preview_btn)
        btn_row.addStretch()
        layout.addLayout(btn_row)

        buttons = QDialogButtonBox(QDialogButtonBox.Close)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    # ------------------------------------------------------------------ #
    # Slots
    # ------------------------------------------------------------------ #

    def _load_theme(self, name: str):
        if self._theme_select.currentText() != name:
            idx = self._theme_select.findText(name)
            if idx >= 0:
                self._theme_select.setCurrentIndex(idx)

        self._current_colors = get_theme_colors(name)
        self._refresh_color_buttons()

        # 背景
        bg = get_theme_background(name)
        self._bg_path.setText(bg.get("path", ""))
        self._bg_opacity.setValue(bg.get("opacity", 0.5))

        # 內建主題不能刪除
        self._btn_delete.setEnabled(name not in BUILTIN_THEME_COLORS)

    def _refresh_color_buttons(self):
        for token, btn in self._color_btns.items():
            color = self._current_colors.get(token, "#000000")
            _set_btn_color(btn, color)
            # 更新旁邊的 hex label
            lbl: QLabel | None = self.findChild(QLabel, f"hex_{token}")
            if lbl:
                lbl.setText(color)

    def _pick_color(self, token: str):
        from PySide6.QtWidgets import QColorDialog
        current = _parse_color(self._current_colors.get(token, "#000000"))
        color = QColorDialog.getColor(
            current,
            self,
            f"選擇顏色 — {COLOR_TOKEN_LABELS.get(token, token)}",
            QColorDialog.ColorDialogOption.ShowAlphaChannel,
        )
        if color.isValid():
            color_str = _color_to_str(color)
            self._current_colors[token] = color_str
            _set_btn_color(self._color_btns[token], color_str)
            lbl: QLabel | None = self.findChild(QLabel, f"hex_{token}")
            if lbl:
                lbl.setText(color_str)

    def _browse_background(self):
        path, _ = QFileDialog.getOpenFileName(
            self,
            "選擇背景檔案",
            "",
            "媒體檔案 (*.png *.jpg *.jpeg *.bmp *.webp *.gif *.mp4 *.avi *.mov *.mkv *.wmv)",
        )
        if path:
            self._bg_path.setText(path)

    def _preview(self):
        """即時預覽目前設定（不儲存）。"""
        from PySide6.QtWidgets import QApplication
        from gui.theme.default_themes import DARK_COLORS, colors_to_qss
        from gui.theme import theme_engine

        qss = colors_to_qss({**DARK_COLORS, **self._current_colors})
        app = QApplication.instance()
        if app:
            app.setStyleSheet(qss)

        # 背景預覽
        bg_widget = theme_engine._bg_widget
        if bg_widget is not None:
            path    = self._bg_path.text().strip()
            opacity = self._bg_opacity.value()
            if path and os.path.isfile(path):
                bg_widget.set_source(path, opacity)
            else:
                bg_widget.clear()

    def _save_as(self):
        name, ok = QInputDialog.getText(self, "另存新主題", "主題名稱：")
        if not ok or not name.strip():
            return
        name = name.strip()
        if name in BUILTIN_THEME_COLORS:
            QMessageBox.warning(self, "錯誤", f"「{name}」是內建主題，請換一個名稱。")
            return
        save_custom_theme(
            name,
            self._current_colors,
            self._bg_path.text().strip(),
            self._bg_opacity.value(),
        )
        if self._theme_select.findText(name) < 0:
            self._theme_select.addItem(name)
        self._theme_select.setCurrentText(name)
        apply_theme(name)
        QMessageBox.information(self, "已儲存", f"主題「{name}」已儲存並套用。")

    def _delete_theme(self):
        name = self._theme_select.currentText()
        if name in BUILTIN_THEME_COLORS:
            return
        reply = QMessageBox.question(
            self, "確認刪除", f"確定要刪除主題「{name}」嗎？",
            QMessageBox.Yes | QMessageBox.No,
        )
        if reply == QMessageBox.Yes:
            delete_custom_theme(name)
            idx = self._theme_select.findText(name)
            if idx >= 0:
                self._theme_select.removeItem(idx)
            apply_theme("Dark")
