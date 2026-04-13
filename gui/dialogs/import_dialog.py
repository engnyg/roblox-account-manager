"""
匯入帳號對話框。
對應 C# Forms/ImportForm.cs。
"""

from __future__ import annotations

from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QLabel, QTextEdit,
    QPushButton, QFileDialog, QWidget, QDialogButtonBox,
    QRadioButton, QButtonGroup, QGroupBox,
)
from PySide6.QtCore import Signal

from core.account_store import (
    import_from_cookies_json, import_from_cookie_strings,
)
from core.account import Account
from core.i18n import tr, get_i18n


class ImportDialog(QDialog):
    accounts_imported = Signal(list)  # list[Account]

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.setMinimumWidth(500)
        self._setup_ui()
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)

        self._mode_group_box = QGroupBox()
        mode_layout = QVBoxLayout(self._mode_group_box)
        self._mode_group = QButtonGroup(self)

        self._mode_cookie = QRadioButton()
        self._mode_json = QRadioButton()
        self._mode_cookie.setChecked(True)
        self._mode_group.addButton(self._mode_cookie, 0)
        self._mode_group.addButton(self._mode_json, 1)

        mode_layout.addWidget(self._mode_cookie)
        mode_layout.addWidget(self._mode_json)
        layout.addWidget(self._mode_group_box)

        self._text = QTextEdit()
        layout.addWidget(self._text)

        file_layout = QHBoxLayout()
        self._file_path = QLabel()
        self._browse_btn = QPushButton()
        self._browse_btn.clicked.connect(self._browse)
        file_layout.addWidget(self._file_path)
        file_layout.addWidget(self._browse_btn)
        layout.addLayout(file_layout)

        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self._import)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

        self._mode_group.idToggled.connect(self._update_mode)
        self._update_mode(0, True)

    def _retranslate_ui(self):
        self.setWindowTitle(tr("Import Accounts"))
        self._mode_group_box.setTitle(tr("Import Mode"))
        self._mode_cookie.setText(tr("Cookie strings (.ROBLOSECURITY, one per line)"))
        self._mode_json.setText(tr("cookies.json file"))
        self._text.setPlaceholderText(tr("Paste cookie strings here (one per line)..."))
        if self._file_path.text() in ("No file selected", "未選擇檔案", ""):
            self._file_path.setText(tr("No file selected"))
        self._browse_btn.setText(tr("Browse..."))

    def _update_mode(self, btn_id: int, checked: bool):
        if not checked:
            return
        is_cookie = self._mode_cookie.isChecked()
        self._text.setEnabled(is_cookie)
        self._browse_btn.setEnabled(not is_cookie)

    def _browse(self):
        path, _ = QFileDialog.getOpenFileName(self, tr("Select cookies.json"), "", tr("JSON files (*.json);;All files (*)"))
        if path:
            self._file_path.setText(path)

    def _import(self):
        accounts: list[Account] = []

        if self._mode_cookie.isChecked():
            raw = self._text.toPlainText().strip()
            if not raw:
                self.reject()
                return
            lines = raw.splitlines()
            accounts = import_from_cookie_strings(lines)
        else:
            path = self._file_path.text()
            if path and path != "No file selected":
                accounts = import_from_cookies_json(path)

        if accounts:
            self.accounts_imported.emit(accounts)
        self.accept()
