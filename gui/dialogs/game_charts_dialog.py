"""
遊戲探索排行榜對話框。
移植自 MultiRoblox-RAM 的 Game Discovery 功能。
"""

from __future__ import annotations

from PySide6.QtCore import Qt, QThread, Signal, QObject
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QTreeWidget, QTreeWidgetItem,
    QPushButton, QLabel, QHeaderView, QWidget,
)

from manager.game_charts import fetch_charts
from core.i18n import tr, get_i18n


class _ChartsWorker(QObject):
    finished = Signal(list)
    error = Signal(str)

    def run(self):
        try:
            charts = fetch_charts()
            self.finished.emit(charts)
        except Exception as e:
            self.error.emit(str(e))


class GameChartsDialog(QDialog):
    place_selected = Signal(int)  # place_id

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.resize(560, 520)
        self._setup_ui()
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()
        self._fetch()

    def _setup_ui(self):
        layout = QVBoxLayout(self)

        top = QHBoxLayout()
        self._status_lbl = QLabel()
        top.addWidget(self._status_lbl)
        top.addStretch()
        self._refresh_btn = QPushButton()
        self._refresh_btn.clicked.connect(self._fetch)
        top.addWidget(self._refresh_btn)
        layout.addLayout(top)

        self._tree = QTreeWidget()
        self._tree.setHeaderHidden(False)
        self._tree.itemDoubleClicked.connect(self._on_double_click)
        layout.addWidget(self._tree)

        btn_layout = QHBoxLayout()
        btn_layout.addStretch()
        self._use_btn = QPushButton()
        self._use_btn.clicked.connect(self._use_selected)
        self._close_btn = QPushButton()
        self._close_btn.clicked.connect(self.reject)
        btn_layout.addWidget(self._use_btn)
        btn_layout.addWidget(self._close_btn)
        layout.addLayout(btn_layout)

    def _retranslate_ui(self):
        self.setWindowTitle(tr("Game Charts"))
        self._tree.setHeaderLabels([tr("Game"), tr("Playing"), tr("Place ID")])
        self._tree.header().setSectionResizeMode(0, QHeaderView.Stretch)
        self._refresh_btn.setText(tr("Refresh"))
        self._use_btn.setText(tr("Use Selected"))
        self._close_btn.setText(tr("Close"))
        self._status_lbl.setText(tr("Loading charts..."))

    def _fetch(self):
        self._refresh_btn.setEnabled(False)
        self._tree.clear()
        self._status_lbl.setText(tr("Loading charts..."))

        self._worker = _ChartsWorker()
        self._thread = QThread(self)
        self._worker.moveToThread(self._thread)
        self._thread.started.connect(self._worker.run)
        self._worker.finished.connect(self._on_charts)
        self._worker.error.connect(self._on_error)
        self._worker.finished.connect(lambda *_: self._thread.quit())
        self._worker.error.connect(lambda *_: self._thread.quit())
        self._thread.start()

    def _on_charts(self, charts: list[dict]):
        self._refresh_btn.setEnabled(True)
        if not charts:
            self._status_lbl.setText(tr("Failed to load charts"))
            return
        self._status_lbl.setText("")
        for sort in charts:
            top_item = QTreeWidgetItem([sort["name"], "", ""])
            self._tree.addTopLevelItem(top_item)
            for game in sort["games"]:
                child = QTreeWidgetItem([
                    game["name"], str(game["playing"]), str(game["placeId"]),
                ])
                child.setData(0, Qt.UserRole, game["placeId"])
                top_item.addChild(child)
        self._tree.expandAll()

    def _on_error(self, msg: str):
        self._refresh_btn.setEnabled(True)
        self._status_lbl.setText(f"{tr('Failed to load charts')}: {msg}")

    def _on_double_click(self, item: QTreeWidgetItem, _col: int):
        self._emit_place(item)

    def _use_selected(self):
        item = self._tree.currentItem()
        if item:
            self._emit_place(item)

    def _emit_place(self, item: QTreeWidgetItem):
        place_id = item.data(0, Qt.UserRole)
        if place_id:
            self.place_selected.emit(int(place_id))
            self.accept()
