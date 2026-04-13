"""
自訂欄位對話框。
對應 C# Forms/AccountFields.cs。
"""

from __future__ import annotations

from typing import TYPE_CHECKING

from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QTableWidget, QTableWidgetItem,
    QPushButton, QLineEdit, QWidget, QInputDialog,
)
from PySide6.QtCore import Qt
from core.i18n import tr, get_i18n

if TYPE_CHECKING:
    from core.account import Account


class AccountFieldsDialog(QDialog):
    def __init__(self, account: "Account", parent: QWidget | None = None):
        super().__init__(parent)
        self._account = account
        self.resize(480, 360)
        self._setup_ui()
        self._load()
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)

        self._table = QTableWidget(0, 2)
        self._table.setColumnWidth(0, 180)
        self._table.setColumnWidth(1, 260)
        layout.addWidget(self._table)

        btn_layout = QHBoxLayout()
        self._add_btn = QPushButton()
        self._add_btn.clicked.connect(self._add_field)
        self._del_btn = QPushButton()
        self._del_btn.clicked.connect(self._delete_field)
        self._save_btn = QPushButton()
        self._save_btn.clicked.connect(self._save)
        self._close_btn = QPushButton()
        self._close_btn.clicked.connect(self.reject)
        btn_layout.addWidget(self._add_btn)
        btn_layout.addWidget(self._del_btn)
        btn_layout.addWidget(self._save_btn)
        btn_layout.addWidget(self._close_btn)
        layout.addLayout(btn_layout)

    def _retranslate_ui(self):
        self.setWindowTitle(f"{tr('Fields')} — {self._account.username}")
        self._table.setHorizontalHeaderLabels([tr("Field Name"), tr("Value")])
        self._add_btn.setText(tr("Add Field"))
        self._del_btn.setText(tr("Delete Selected"))
        self._save_btn.setText(tr("Save"))
        self._close_btn.setText(tr("Close"))

    def _load(self):
        self._table.setRowCount(0)
        for row, (name, value) in enumerate(self._account.fields.items()):
            self._table.insertRow(row)
            self._table.setItem(row, 0, QTableWidgetItem(name))
            self._table.setItem(row, 1, QTableWidgetItem(value))

    def _add_field(self):
        name, ok = QInputDialog.getText(self, tr("Add Field"), tr("Field name:"))
        if ok and name:
            row = self._table.rowCount()
            self._table.insertRow(row)
            self._table.setItem(row, 0, QTableWidgetItem(name))
            self._table.setItem(row, 1, QTableWidgetItem(""))

    def _delete_field(self):
        row = self._table.currentRow()
        if row >= 0:
            self._table.removeRow(row)

    def _save(self):
        self._account.fields = {}
        for row in range(self._table.rowCount()):
            name_item = self._table.item(row, 0)
            val_item = self._table.item(row, 1)
            if name_item:
                name = name_item.text().strip()
                value = val_item.text() if val_item else ""
                if name:
                    self._account.fields[name] = value
        self.accept()
