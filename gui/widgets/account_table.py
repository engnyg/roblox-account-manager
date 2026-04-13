"""
帳號列表表格（QTableView + QAbstractTableModel）。
對應 C# BrightIdeasSoftware.ObjectListView 帳號列表。
"""

from __future__ import annotations

from typing import Any

from PySide6.QtCore import (
    QAbstractTableModel, QModelIndex, Qt, Signal, QSortFilterProxyModel,
)
from PySide6.QtGui import QColor, QFont
from PySide6.QtWidgets import (
    QTableView, QAbstractItemView, QHeaderView, QMenu, QWidget,
)

from core.account import Account
from core.i18n import tr

COLUMN_KEYS = [
    ("Username", "username"),
    ("Alias", "alias"),
    ("Group", "group"),
    ("Description", "description"),
    ("Last Use", "last_use"),
    ("Valid", "valid"),
]


class AccountTableModel(QAbstractTableModel):
    def __init__(self, accounts: list[Account] | None = None, parent=None):
        super().__init__(parent)
        self._accounts: list[Account] = accounts or []

    def set_accounts(self, accounts: list[Account]):
        self.beginResetModel()
        self._accounts = accounts
        self.endResetModel()

    def get_account(self, row: int) -> Account | None:
        if 0 <= row < len(self._accounts):
            return self._accounts[row]
        return None

    def rowCount(self, parent=QModelIndex()) -> int:
        return len(self._accounts)

    def columnCount(self, parent=QModelIndex()) -> int:
        return len(COLUMN_KEYS)

    def headerData(self, section: int, orientation: Qt.Orientation, role=Qt.DisplayRole) -> Any:
        if orientation == Qt.Horizontal and role == Qt.DisplayRole:
            return tr(COLUMN_KEYS[section][0])
        return None

    def data(self, index: QModelIndex, role=Qt.DisplayRole) -> Any:
        if not index.isValid():
            return None
        acc = self._accounts[index.row()]
        col_name = COLUMN_KEYS[index.column()][1]

        if role == Qt.DisplayRole:
            val = getattr(acc, col_name, "")
            if col_name == "last_use" and val:
                return val.strftime("%Y-%m-%d %H:%M")
            if col_name == "valid":
                return "✓" if val else "✗"
            return str(val) if val else ""

        if role == Qt.ForegroundRole:
            if col_name == "valid":
                return QColor("#a6e3a1") if acc.valid else QColor("#f38ba8")
            return None

        if role == Qt.UserRole:
            return acc

        return None

    def flags(self, index: QModelIndex) -> Qt.ItemFlags:
        return Qt.ItemIsEnabled | Qt.ItemIsSelectable

    def retranslate(self):
        """語言切換時重整表頭。"""
        self.headerDataChanged.emit(Qt.Horizontal, 0, self.columnCount() - 1)


class AccountTable(QTableView):
    account_double_clicked = Signal(Account)
    context_menu_requested = Signal(Account, object)  # Account, QPoint

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self._model = AccountTableModel()
        self._proxy = QSortFilterProxyModel(self)
        self._proxy.setSourceModel(self._model)
        self._proxy.setFilterCaseSensitivity(Qt.CaseInsensitive)
        self._proxy.setFilterKeyColumn(-1)  # 搜尋所有欄位
        self.setModel(self._proxy)

        self.setAlternatingRowColors(True)
        self.setSelectionBehavior(QAbstractItemView.SelectRows)
        self.setSelectionMode(QAbstractItemView.ExtendedSelection)
        self.setSortingEnabled(True)
        self.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.verticalHeader().setVisible(False)
        self.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        for i in range(1, len(COLUMN_KEYS)):
            self.horizontalHeader().setSectionResizeMode(i, QHeaderView.ResizeToContents)

        self.doubleClicked.connect(self._on_double_click)
        self.setContextMenuPolicy(Qt.CustomContextMenu)
        self.customContextMenuRequested.connect(self._on_context_menu)

    def set_accounts(self, accounts: list[Account]):
        self._model.set_accounts(accounts)

    def filter(self, text: str):
        self._proxy.setFilterFixedString(text)

    def selected_accounts(self) -> list[Account]:
        result = []
        for idx in self.selectedIndexes():
            if idx.column() == 0:
                source_idx = self._proxy.mapToSource(idx)
                acc = self._model.get_account(source_idx.row())
                if acc:
                    result.append(acc)
        return result

    def selected_account(self) -> Account | None:
        accounts = self.selected_accounts()
        return accounts[0] if accounts else None

    def _on_double_click(self, index: QModelIndex):
        source_idx = self._proxy.mapToSource(index)
        acc = self._model.get_account(source_idx.row())
        if acc:
            self.account_double_clicked.emit(acc)

    def retranslate(self):
        self._model.retranslate()

    def _on_context_menu(self, pos):
        acc = self.selected_account()
        if acc:
            self.context_menu_requested.emit(acc, self.viewport().mapToGlobal(pos))
