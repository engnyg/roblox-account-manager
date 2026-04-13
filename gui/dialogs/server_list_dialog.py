"""
伺服器列表對話框。
對應 C# Forms/ServerList.cs。
"""

from __future__ import annotations

from typing import Optional, TYPE_CHECKING

from PySide6.QtCore import Qt, QThread, Signal, QObject
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QTableWidget, QTableWidgetItem,
    QLineEdit, QPushButton, QLabel, QHeaderView, QWidget,
)

from manager.server_browser import fetch_servers, format_server_display
from core.i18n import tr, get_i18n

if TYPE_CHECKING:
    from core.account import Account


class _ServerFetchWorker(QObject):
    finished = Signal(list)
    error = Signal(str)

    def __init__(self, place_id: int):
        super().__init__()
        self._place_id = place_id

    def run(self):
        try:
            servers = fetch_servers(self._place_id)
            self.finished.emit(servers)
        except Exception as e:
            self.error.emit(str(e))


class ServerListDialog(QDialog):
    join_requested = Signal(str)  # job_id

    def __init__(self, account: "Account", parent: QWidget | None = None):
        super().__init__(parent)
        self._account = account
        self.resize(680, 480)
        self._setup_ui()
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)

        top = QHBoxLayout()
        self._lbl_place = QLabel()
        top.addWidget(self._lbl_place)
        self._place_id = QLineEdit()
        top.addWidget(self._place_id)
        self._fetch_btn = QPushButton()
        self._fetch_btn.clicked.connect(self._fetch)
        top.addWidget(self._fetch_btn)
        layout.addLayout(top)

        self._table = QTableWidget(0, 4)
        self._table.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self._table.setEditTriggers(QTableWidget.NoEditTriggers)
        self._table.setSelectionBehavior(QTableWidget.SelectRows)
        layout.addWidget(self._table)

        btn_layout = QHBoxLayout()
        self._join_btn = QPushButton()
        self._join_btn.clicked.connect(self._join)
        self._close_btn = QPushButton()
        self._close_btn.clicked.connect(self.reject)
        btn_layout.addWidget(self._join_btn)
        btn_layout.addWidget(self._close_btn)
        layout.addLayout(btn_layout)

    def _retranslate_ui(self):
        self.setWindowTitle(tr("Server List"))
        self._lbl_place.setText(tr("Place ID:"))
        self._place_id.setPlaceholderText(tr("e.g. 4924922222"))
        self._fetch_btn.setText(tr("Fetch Servers"))
        self._table.setHorizontalHeaderLabels([tr("Job ID"), tr("Players"), tr("Max"), tr("Ping")])
        self._join_btn.setText(tr("Join Selected"))
        self._close_btn.setText(tr("Close"))

    def _fetch(self):
        try:
            place_id = int(self._place_id.text().strip())
        except ValueError:
            return

        self._fetch_btn.setEnabled(False)
        self._table.setRowCount(0)

        self._worker = _ServerFetchWorker(place_id)
        self._thread = QThread(self)
        self._worker.moveToThread(self._thread)
        self._thread.started.connect(self._worker.run)
        self._worker.finished.connect(self._on_servers)
        self._worker.error.connect(lambda e: self._fetch_btn.setEnabled(True))
        self._thread.start()

    def _on_servers(self, servers: list[dict]):
        if self._thread:
            self._thread.quit()
        self._fetch_btn.setEnabled(True)
        self._table.setRowCount(len(servers))
        for row, srv in enumerate(servers):
            d = format_server_display(srv)
            self._table.setItem(row, 0, QTableWidgetItem(d["id"]))
            self._table.setItem(row, 1, QTableWidgetItem(str(d["playing"])))
            self._table.setItem(row, 2, QTableWidgetItem(str(d["maxPlayers"])))
            self._table.setItem(row, 3, QTableWidgetItem(str(d["ping"])))

    def _join(self):
        row = self._table.currentRow()
        if row < 0:
            return
        job_id = self._table.item(row, 0).text()
        self.join_requested.emit(job_id)
        self.accept()
