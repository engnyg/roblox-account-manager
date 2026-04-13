"""
主題編輯器對話框。
對應 C# Forms/ThemeEditor.cs。
"""

from __future__ import annotations

from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QComboBox, QTextEdit,
    QPushButton, QLabel, QWidget, QDialogButtonBox,
)
from PySide6.QtGui import QFont

from gui.theme.theme_engine import apply_theme, get_theme_names, get_current_theme
from gui.theme.default_themes import THEMES


class ThemeEditorDialog(QDialog):
    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.setWindowTitle("Theme Editor")
        self.resize(680, 520)
        self._setup_ui()
        self._load_theme(get_current_theme())

    def _setup_ui(self):
        layout = QVBoxLayout(self)

        top = QHBoxLayout()
        top.addWidget(QLabel("Theme:"))
        self._theme_select = QComboBox()
        self._theme_select.addItems(get_theme_names())
        self._theme_select.currentTextChanged.connect(self._load_theme)
        top.addWidget(self._theme_select)
        top.addStretch()
        layout.addLayout(top)

        self._editor = QTextEdit()
        font = QFont("Consolas", 10)
        self._editor.setFont(font)
        layout.addWidget(self._editor)

        btn_layout = QHBoxLayout()
        preview_btn = QPushButton("Preview")
        preview_btn.clicked.connect(self._preview)
        save_btn = QPushButton("Save as Custom")
        save_btn.clicked.connect(self._save_custom)
        btn_layout.addWidget(preview_btn)
        btn_layout.addWidget(save_btn)
        layout.addLayout(btn_layout)

        buttons = QDialogButtonBox(QDialogButtonBox.Close)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def _load_theme(self, name: str):
        self._editor.setPlainText(THEMES.get(name, ""))

    def _preview(self):
        qss = self._editor.toPlainText()
        from PySide6.QtWidgets import QApplication
        app = QApplication.instance()
        if app:
            app.setStyleSheet(qss)

    def _save_custom(self):
        from PySide6.QtWidgets import QInputDialog
        name, ok = QInputDialog.getText(self, "Save Theme", "Theme name:")
        if ok and name:
            from gui.theme.theme_engine import register_custom_theme
            register_custom_theme(name, self._editor.toPlainText())
            if self._theme_select.findText(name) < 0:
                self._theme_select.addItem(name)
