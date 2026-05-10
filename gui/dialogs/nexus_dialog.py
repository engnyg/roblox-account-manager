"""
Nexus 控制面板對話框。
對應 C# Nexus/AccountControl.cs 的 GUI。
"""

from __future__ import annotations

from PySide6.QtCore import Qt, QTimer, QSize
from PySide6.QtGui import QColor, QIcon, QPainter, QBrush
from core.i18n import tr, get_i18n
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QTableWidget, QTableWidgetItem,
    QPushButton, QLineEdit, QLabel, QHeaderView, QWidget,
    QStyledItemDelegate, QStyleOptionViewItem,
)
from PySide6.QtCore import QModelIndex

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


class _ConnectedDelegate(QStyledItemDelegate):
    """在 Connected 列直接绘制图标，回退为彩色圆点。"""

    # 每行的连接状态由外部通过 Qt.UserRole 传入（True/False）
    def paint(self, painter: QPainter, option: QStyleOptionViewItem,
              index: QModelIndex) -> None:
        super().paint(painter, option, index)

        connected = index.data(Qt.UserRole)
        if connected is None:
            return

        icon = _fluent_icon(
            "ACCEPT" if connected else "CANCEL",
            "#a6e3a1" if connected else "#f38ba8",
        )

        painter.save()
        if icon:
            icon.paint(painter, option.rect, Qt.AlignCenter)
        else:
            color = QColor("#a6e3a1" if connected else "#f38ba8")
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


class NexusDialog(QDialog):
    def __init__(self, nexus_server, parent: QWidget | None = None):
        super().__init__(parent)
        self._nexus = nexus_server
        self.resize(640, 420)
        self._setup_ui()
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()

        self._refresh_timer = QTimer(self)
        self._refresh_timer.timeout.connect(self._refresh)
        self._refresh_timer.start(2000)

    def _setup_ui(self):
        layout = QVBoxLayout(self)

        self._table = QTableWidget(0, 3)
        self._table.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self._table.setEditTriggers(QTableWidget.NoEditTriggers)
        self._table.setItemDelegateForColumn(2, _ConnectedDelegate(self._table))
        layout.addWidget(self._table)

        cmd_layout = QHBoxLayout()
        self._lbl_script = QLabel()
        cmd_layout.addWidget(self._lbl_script)
        self._script = QLineEdit()
        cmd_layout.addWidget(self._script)
        self._exec_btn = QPushButton()
        self._exec_btn.clicked.connect(self._execute)
        cmd_layout.addWidget(self._exec_btn)
        layout.addLayout(cmd_layout)

        self._close_btn = QPushButton()
        self._close_btn.clicked.connect(self.reject)
        layout.addWidget(self._close_btn)

    def _retranslate_ui(self):
        self.setWindowTitle(tr("Nexus Account Control"))
        self._table.setHorizontalHeaderLabels([tr("Username"), tr("Job ID"), tr("Connected")])
        self._lbl_script.setText(tr("Script:"))
        self._script.setPlaceholderText(tr("Lua script to execute on selected account..."))
        self._exec_btn.setText(tr("Execute"))
        self._close_btn.setText(tr("Close"))

    def _refresh(self):
        if not self._nexus:
            return
        accounts = self._nexus.get_accounts()
        self._table.setRowCount(len(accounts))
        for row, acc in enumerate(accounts):
            self._table.setItem(row, 0, QTableWidgetItem(acc.username))
            self._table.setItem(row, 1, QTableWidgetItem(acc.in_game_job_id))
            # 将连接状态存入 UserRole，delegate 读取后绘制
            status_item = QTableWidgetItem()
            status_item.setData(Qt.UserRole, acc.is_connected)
            self._table.setItem(row, 2, status_item)

    def _execute(self):
        import asyncio
        from nexus.command import make_message, NexusCommand
        script = self._script.text().strip()
        if not script or not self._nexus:
            return
        msg = make_message(NexusCommand.EXECUTE_SCRIPT, script=script)
        row = self._table.currentRow()
        if row >= 0:
            username = self._table.item(row, 0).text()
            asyncio.ensure_future(self._nexus.send_to(username, msg))
        else:
            asyncio.ensure_future(self._nexus.broadcast(msg))

    def closeEvent(self, event):
        self._refresh_timer.stop()
        super().closeEvent(event)
