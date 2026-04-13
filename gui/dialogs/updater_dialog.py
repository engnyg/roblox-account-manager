"""
更新檢查對話框。
移植自 lib/lib.py checkUpdate()。
"""

from __future__ import annotations

import httpx

from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QLabel, QPushButton, QWidget,
)
from core.i18n import tr, get_i18n


def check_update() -> tuple[str, str]:
    """
    回傳 (current_version, latest_version)。
    """
    try:
        with open("version.txt", encoding="utf-8") as f:
            current = f.read().strip()
    except FileNotFoundError:
        current = "unknown"

    try:
        r = httpx.get(
            "https://api.github.com/repos/qing762/roblox-auto-signup/releases/latest",
            timeout=10,
        )
        latest = r.json().get("tag_name", "unknown")
    except Exception:
        latest = "unknown"

    return current, latest


class UpdaterDialog(QDialog):
    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.setMinimumWidth(360)
        self._current, self._latest = check_update()
        self._setup_ui()
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        self._status = QLabel()
        layout.addWidget(self._status)
        self._close_btn = QPushButton()
        self._close_btn.clicked.connect(self.accept)
        layout.addWidget(self._close_btn)

    def _retranslate_ui(self):
        self.setWindowTitle(tr("Update Checker"))
        self._close_btn.setText(tr("Close"))
        current, latest = self._current, self._latest
        if current == "unknown" or latest == "unknown":
            self._status.setText(tr("Could not check for updates."))
        elif current < latest:
            self._status.setText(f"Update available!\n\nCurrent: {current}\nLatest: {latest}")
        else:
            self._status.setText(f"{tr('You are up to date.')} ({current})")
