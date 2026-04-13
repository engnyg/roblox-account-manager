"""
Nexus 控制面板對話框。
對應 C# Nexus/AccountControl.cs 的 GUI。
"""

from __future__ import annotations

from PySide6.QtCore import Qt, QTimer
from core.i18n import tr, get_i18n
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QTableWidget, QTableWidgetItem,
    QPushButton, QLineEdit, QLabel, QHeaderView, QWidget,
)


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
            status = "✓" if acc.is_connected else "✗"
            self._table.setItem(row, 2, QTableWidgetItem(status))

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
