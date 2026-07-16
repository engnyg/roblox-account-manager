"""
Mixer 控制面板對話框。
移植自 MultiRoblox-RAM 的 Mixer：統一管理所有執行中 Roblox 分身的
音量、FPS 上限，並提供一鍵結束全部功能。
"""

from __future__ import annotations

from typing import Callable, TYPE_CHECKING

from PySide6.QtCore import Qt, QTimer
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QTableWidget, QTableWidgetItem,
    QPushButton, QLabel, QHeaderView, QWidget, QSlider, QSpinBox, QMessageBox,
)

from manager.mixer import (
    list_processes_with_volume, set_process_volume, kill_all_roblox,
)
from manager.fps_unlocker import set_fps_cap
from core.i18n import tr, get_i18n
from core.settings import get_settings

if TYPE_CHECKING:
    from core.account import Account


class MixerDialog(QDialog):
    def __init__(self, get_accounts: Callable[[], list["Account"]], parent: QWidget | None = None):
        super().__init__(parent)
        self._get_accounts = get_accounts
        self._settings = get_settings()
        self.resize(640, 420)
        self._setup_ui()
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()

        self._timer = QTimer(self)
        self._timer.timeout.connect(self._refresh)
        self._timer.start(3000)
        self._refresh()

    def _setup_ui(self):
        layout = QVBoxLayout(self)

        top = QHBoxLayout()
        self._lbl_fps = QLabel()
        top.addWidget(self._lbl_fps)
        self._fps_spin = QSpinBox()
        self._fps_spin.setRange(0, 300)
        self._fps_spin.setValue(self._settings.get_int("General", "MaxFPSValue", 120))
        top.addWidget(self._fps_spin)
        self._apply_fps_btn = QPushButton()
        self._apply_fps_btn.clicked.connect(self._apply_fps)
        top.addWidget(self._apply_fps_btn)
        top.addStretch()
        self._kill_all_btn = QPushButton()
        self._kill_all_btn.clicked.connect(self._kill_all)
        top.addWidget(self._kill_all_btn)
        layout.addLayout(top)

        self._table = QTableWidget(0, 4)
        self._table.horizontalHeader().setSectionResizeMode(0, QHeaderView.Stretch)
        self._table.setEditTriggers(QTableWidget.NoEditTriggers)
        self._table.setSelectionBehavior(QTableWidget.SelectRows)
        layout.addWidget(self._table)

        btn_layout = QHBoxLayout()
        btn_layout.addStretch()
        self._close_btn = QPushButton()
        self._close_btn.clicked.connect(self.accept)
        btn_layout.addWidget(self._close_btn)
        layout.addLayout(btn_layout)

    def _retranslate_ui(self):
        self.setWindowTitle(tr("Mixer"))
        self._lbl_fps.setText(tr("FPS Cap:"))
        self._apply_fps_btn.setText(tr("Apply to All"))
        self._kill_all_btn.setText(tr("Kill All Roblox"))
        self._table.setHorizontalHeaderLabels(
            [tr("Account"), tr("PID"), tr("Memory (MB)"), tr("Volume")]
        )
        self._close_btn.setText(tr("Close"))

    def _apply_fps(self):
        fps = self._fps_spin.value()
        ok = set_fps_cap(fps)
        self._settings.set("General", "MaxFPSValue", str(fps))
        if ok:
            QMessageBox.information(self, tr("Mixer"), tr("FPS cap updated"))
        else:
            QMessageBox.warning(self, tr("Mixer"), tr("FPS update failed"))

    def _kill_all(self):
        reply = QMessageBox.question(
            self, tr("Kill All Roblox"),
            tr("This will close every running Roblox window. Continue?"),
        )
        if reply != QMessageBox.Yes:
            return
        count = kill_all_roblox()
        self._status(f"{tr('Killed')} {count} {tr('process(es)')}")
        self._refresh()

    def _status(self, text: str):
        if self.parent() and hasattr(self.parent(), "_status_label"):
            self.parent()._status_label.setText(text)

    def _refresh(self):
        accounts = self._get_accounts()
        tracker_map = {a.browser_tracker_id: a for a in accounts if a.browser_tracker_id}

        entries = list_processes_with_volume()
        self._table.setRowCount(len(entries))
        for row, entry in enumerate(entries):
            proc = entry["process"]
            volume = entry["volume"]
            account = tracker_map.get(proc.tracker_id)
            name = account.username if account else tr("Unknown")

            self._table.setItem(row, 0, QTableWidgetItem(name))
            self._table.setItem(row, 1, QTableWidgetItem(str(proc.pid)))
            self._table.setItem(row, 2, QTableWidgetItem(f"{proc.memory_mb:.0f}"))

            slider = QSlider(Qt.Horizontal)
            slider.setRange(0, 100)
            slider.setValue(int((volume or 0.0) * 100))
            slider.setEnabled(volume is not None)
            slider.valueChanged.connect(
                lambda val, pid=proc.pid: set_process_volume(pid, val / 100.0)
            )
            self._table.setCellWidget(row, 3, slider)

    def closeEvent(self, event):
        self._timer.stop()
        super().closeEvent(event)
