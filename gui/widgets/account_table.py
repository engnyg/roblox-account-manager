"""
帳號列表表格（QTableView + QAbstractTableModel）。
對應 C# BrightIdeasSoftware.ObjectListView 帳號列表。
"""

from __future__ import annotations

from typing import Any

from PySide6.QtCore import (
    QAbstractTableModel, QModelIndex, Qt, Signal, QSortFilterProxyModel, QSize,
)
from core.settings import get_settings
from PySide6.QtGui import QColor, QFont, QIcon, QPainter, QBrush
from PySide6.QtWidgets import (
    QTableView, QAbstractItemView, QHeaderView, QMenu, QWidget,
    QStyledItemDelegate, QStyleOptionViewItem,
)

from core.account import Account
from core.i18n import tr

COLUMN_KEYS = [
    ("Launch", "_check"),   # 勾選要同時啟動的帳號
    ("Username", "username"),
    ("Alias", "alias"),
    ("Group", "group"),
    ("Description", "description"),
    ("Last Use", "last_use"),
    ("Valid", "valid"),
]

VALID_COL = next(i for i, (_, k) in enumerate(COLUMN_KEYS) if k == "valid")
CHECK_COL = 0
USERNAME_COL = next(i for i, (_, k) in enumerate(COLUMN_KEYS) if k == "username")

# Lazy icon cache — created after QApplication exists
_icon_cache: dict[str, QIcon | None] = {}


def _fluent_icon(icon_name: str, color: str) -> QIcon | None:
    key = f"{icon_name}@{color}"
    if key not in _icon_cache:
        result = None
        try:
            from qfluentwidgets import FluentIcon as FIF
            from PySide6.QtGui import QColor as _QColor
            raw = getattr(FIF, icon_name).icon(color=_QColor(color))
            pm = raw.pixmap(16, 16)
            if not pm.isNull():
                result = QIcon(pm)
        except Exception:
            pass
        _icon_cache[key] = result
    return _icon_cache[key]


class _ValidDelegate(QStyledItemDelegate):
    """在 Valid 列直接绘制 FluentIcon，回退为彩色圆点。"""

    def paint(self, painter: QPainter, option: QStyleOptionViewItem,
              index: QModelIndex) -> None:
        super().paint(painter, option, index)  # 绘制底色 / 选中高亮

        acc: Account | None = index.data(Qt.UserRole)
        if not isinstance(acc, Account):
            return

        icon = _fluent_icon(
            "ACCEPT" if acc.valid else "CANCEL",
            "#a6e3a1" if acc.valid else "#f38ba8",
        )

        painter.save()
        if icon:
            icon.paint(painter, option.rect, Qt.AlignCenter)
        else:
            # 回退：彩色实心圆点
            color = QColor("#a6e3a1" if acc.valid else "#f38ba8")
            painter.setRenderHint(QPainter.Antialiasing)
            painter.setBrush(QBrush(color))
            painter.setPen(Qt.NoPen)
            r = option.rect
            d = 10
            x = r.x() + (r.width() - d) // 2
            y = r.y() + (r.height() - d) // 2
            painter.drawEllipse(x, y, d, d)
        painter.restore()

    def sizeHint(self, option: QStyleOptionViewItem, index: QModelIndex) -> QSize:
        return QSize(32, super().sizeHint(option, index).height())


class AccountTableModel(QAbstractTableModel):
    checked_changed = Signal()

    def __init__(self, accounts: list[Account] | None = None, parent=None):
        super().__init__(parent)
        self._accounts: list[Account] = accounts or []
        # 勾選狀態以用戶名為鍵（重新載入帳號、重開程式後仍保留）
        raw = get_settings().get("Launch", "Checked", "")
        self._checked: set[str] = {n for n in raw.split(",") if n}

    # -- 勾選 --
    def is_checked(self, acc: Account) -> bool:
        return acc.username in self._checked

    def _save_checked(self):
        try:
            get_settings().set("Launch", "Checked", ",".join(sorted(self._checked)))
        except Exception:
            pass

    def set_checked_many(self, accounts: list[Account], checked: bool):
        changed = False
        for acc in accounts:
            if checked and acc.username not in self._checked:
                self._checked.add(acc.username)
                changed = True
            elif not checked and acc.username in self._checked:
                self._checked.discard(acc.username)
                changed = True
        if changed:
            self._save_checked()
            if self._accounts:
                self.dataChanged.emit(
                    self.index(0, CHECK_COL), self.index(len(self._accounts) - 1, CHECK_COL),
                    [Qt.CheckStateRole],
                )
            self.checked_changed.emit()

    def checked_accounts(self) -> list[Account]:
        return [a for a in self._accounts if a.username in self._checked]

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

        if col_name == "_check":
            if role == Qt.CheckStateRole:
                return Qt.Checked if self.is_checked(acc) else Qt.Unchecked
            if role == Qt.UserRole:
                return acc
            return None

        if role == Qt.DisplayRole:
            val = getattr(acc, col_name, "")
            if col_name == "last_use" and val:
                return val.strftime("%Y-%m-%d %H:%M")
            if col_name == "valid":
                return ""   # delegate 负责绘制
            return str(val) if val else ""

        if role == Qt.UserRole:
            return acc

        return None

    def setData(self, index: QModelIndex, value, role=Qt.EditRole) -> bool:
        if index.isValid() and index.column() == CHECK_COL and role == Qt.CheckStateRole:
            acc = self._accounts[index.row()]
            self.set_checked_many([acc], Qt.CheckState(value) == Qt.Checked)
            return True
        return False

    def flags(self, index: QModelIndex) -> Qt.ItemFlags:
        f = Qt.ItemIsEnabled | Qt.ItemIsSelectable
        if index.column() == CHECK_COL:
            f |= Qt.ItemIsUserCheckable
        return f

    def retranslate(self):
        """語言切換時重整表頭。"""
        self.headerDataChanged.emit(Qt.Horizontal, 0, self.columnCount() - 1)


class _AccountFilterProxy(QSortFilterProxyModel):
    """在文字搜尋之外，額外支援依 Group 篩選。"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self._group_filter = ""  # 空字串 = 不限群組

    def set_group_filter(self, group: str):
        self._group_filter = group or ""
        self.invalidateFilter()

    def lessThan(self, left: QModelIndex, right: QModelIndex) -> bool:
        if left.column() == CHECK_COL:
            m: AccountTableModel = self.sourceModel()
            la, ra = m.get_account(left.row()), m.get_account(right.row())
            return bool(la and m.is_checked(la)) < bool(ra and m.is_checked(ra))
        return super().lessThan(left, right)

    def filterAcceptsRow(self, source_row: int, source_parent: QModelIndex) -> bool:
        if not super().filterAcceptsRow(source_row, source_parent):
            return False
        if self._group_filter:
            model: AccountTableModel = self.sourceModel()
            acc = model.get_account(source_row)
            if not acc or acc.group != self._group_filter:
                return False
        return True


class AccountTable(QTableView):
    checked_changed = Signal()
    account_double_clicked = Signal(Account)
    context_menu_requested = Signal(Account, object)  # Account, QPoint

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self._model = AccountTableModel()
        self._proxy = _AccountFilterProxy(self)
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
        for i in range(len(COLUMN_KEYS)):
            self.horizontalHeader().setSectionResizeMode(
                i, QHeaderView.Stretch if i == USERNAME_COL else QHeaderView.ResizeToContents
            )
        self._model.checked_changed.connect(self.checked_changed)

        # 用 delegate 直接绘制 Valid 列图标，绕开 DecorationRole
        self.setItemDelegateForColumn(VALID_COL, _ValidDelegate(self))

        self.doubleClicked.connect(self._on_double_click)
        self.setContextMenuPolicy(Qt.CustomContextMenu)
        self.customContextMenuRequested.connect(self._on_context_menu)

    def set_accounts(self, accounts: list[Account]):
        self._model.set_accounts(accounts)

    def filter(self, text: str):
        self._proxy.setFilterFixedString(text)

    def set_group_filter(self, group: str):
        self._proxy.set_group_filter(group)

    def accounts_in_group(self, group: str) -> list[Account]:
        if not group:
            return list(self._model._accounts)
        return [a for a in self._model._accounts if a.group == group]

    def selected_accounts(self) -> list[Account]:
        result = []
        sm = self.selectionModel()
        if sm is None:
            return result
        for idx in sm.selectedRows():
            source_idx = self._proxy.mapToSource(idx)
            acc = self._model.get_account(source_idx.row())
            if acc:
                result.append(acc)
        return result

    # -- 勾選 --
    def visible_accounts(self) -> list[Account]:
        """目前（套用搜尋 / 群組篩選後）看得到的帳號。"""
        out = []
        for r in range(self._proxy.rowCount()):
            acc = self._model.get_account(self._proxy.mapToSource(self._proxy.index(r, 0)).row())
            if acc:
                out.append(acc)
        return out

    def checked_accounts(self) -> list[Account]:
        return self._model.checked_accounts()

    def set_checked(self, accounts: list[Account], checked: bool):
        self._model.set_checked_many(accounts, checked)

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
