"""
Mixer 控制面板對話框。
移植自 MultiRoblox-RAM 的 Mixer：統一管理所有執行中 Roblox 分身的
音量、FPS 上限，並提供一鍵結束全部功能；另外加上每個實例的 CPU / RAM 限制
（見 manager/resource_limits.py）。
"""

from __future__ import annotations

from typing import Callable, TYPE_CHECKING

import psutil
from PySide6.QtCore import Qt, QTimer
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QTableWidget, QTableWidgetItem,
    QPushButton, QLabel, QHeaderView, QWidget, QSlider, QSpinBox, QMessageBox,
    QCheckBox,
)

from manager.mixer import (
    list_processes_with_volume, set_process_volume, kill_all_roblox,
)
from manager.fps_unlocker import set_fps_cap
from manager import window_layout as wl
from manager.resource_limits import (
    Limits, LimitError, MAX_MEMORY_MB,
    apply_limits, clamp_limits, default_limits, auto_apply_enabled,
    get_applied, save_defaults,
)
from core.i18n import tr, get_i18n
from core.settings import get_settings

if TYPE_CHECKING:
    from core.account import Account


class MixerDialog(QDialog):
    def __init__(self, get_accounts: Callable[[], list["Account"]], parent: QWidget | None = None):
        super().__init__(parent)
        self._get_accounts = get_accounts
        self._settings = get_settings()
        self.resize(820, 520)
        self._procs: dict[int, psutil.Process] = {}  # 供 cpu_percent 連續取樣
        self._ncpu = psutil.cpu_count(logical=True) or 1
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

        # ---- CPU / RAM 限制 ----
        lim = QHBoxLayout()
        self._lbl_cpu = QLabel()
        lim.addWidget(self._lbl_cpu)
        self._cpu_spin = QSpinBox()
        self._cpu_spin.setRange(0, 100)
        self._cpu_spin.setSuffix(" %")
        d = default_limits()
        self._cpu_spin.setValue(d.cpu_percent)
        lim.addWidget(self._cpu_spin)
        self._lbl_ram = QLabel()
        lim.addWidget(self._lbl_ram)
        self._ram_spin = QSpinBox()
        self._ram_spin.setRange(0, MAX_MEMORY_MB)
        self._ram_spin.setSingleStep(128)
        self._ram_spin.setSuffix(" MB")
        self._ram_spin.setValue(d.memory_mb)
        lim.addWidget(self._ram_spin)
        self._apply_sel_btn = QPushButton()
        self._apply_sel_btn.clicked.connect(lambda: self._apply_limit_values(selected_only=True))
        lim.addWidget(self._apply_sel_btn)
        self._apply_all_btn = QPushButton()
        self._apply_all_btn.clicked.connect(lambda: self._apply_limit_values(selected_only=False))
        lim.addWidget(self._apply_all_btn)
        self._clear_btn = QPushButton()
        self._clear_btn.clicked.connect(self._clear_limits)
        lim.addWidget(self._clear_btn)
        lim.addStretch()
        layout.addLayout(lim)

        self._auto_chk = QCheckBox()
        self._auto_chk.setChecked(auto_apply_enabled())
        self._auto_chk.toggled.connect(lambda _=None: self._save_default_limits())
        layout.addWidget(self._auto_chk)
        self._lbl_hint = QLabel()
        self._lbl_hint.setWordWrap(True)
        layout.addWidget(self._lbl_hint)

        # ---- 視窗大小 ----
        win = QHBoxLayout()
        ws = wl.load_settings()
        self._lbl_win = QLabel()
        win.addWidget(self._lbl_win)
        self._win_w = QSpinBox()
        self._win_w.setRange(wl.MIN_SIZE, wl.MAX_SIZE)
        self._win_w.setSingleStep(50)
        self._win_w.setValue(ws.width)
        win.addWidget(self._win_w)
        self._lbl_x = QLabel("×")
        win.addWidget(self._lbl_x)
        self._win_h = QSpinBox()
        self._win_h.setRange(wl.MIN_SIZE, wl.MAX_SIZE)
        self._win_h.setSingleStep(50)
        self._win_h.setValue(ws.height)
        win.addWidget(self._win_h)
        self._tile_chk = QCheckBox()
        self._tile_chk.setChecked(ws.tile)
        win.addWidget(self._tile_chk)
        self._win_launch_chk = QCheckBox()
        self._win_launch_chk.setChecked(ws.apply_on_launch)
        win.addWidget(self._win_launch_chk)
        self._win_apply_btn = QPushButton()
        self._win_apply_btn.clicked.connect(self._apply_windows)
        win.addWidget(self._win_apply_btn)
        win.addStretch()
        layout.addLayout(win)
        for w in (self._win_w, self._win_h):
            w.editingFinished.connect(self._save_window_settings)
        for c in (self._tile_chk, self._win_launch_chk):
            c.toggled.connect(lambda _=None: self._save_window_settings())

        self._table = QTableWidget(0, 6)
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
            [tr("Account"), tr("PID"), tr("Memory (MB)"), tr("CPU %"), tr("Limit"), tr("Volume")]
        )
        self._lbl_win.setText(tr("Window size:"))
        self._tile_chk.setText(tr("Tile in a grid"))
        self._win_launch_chk.setText(tr("Apply on launch"))
        self._win_apply_btn.setText(tr("Apply to Running Windows"))
        self._lbl_cpu.setText(tr("CPU limit:"))
        self._lbl_ram.setText(tr("RAM limit:"))
        self._cpu_spin.setSpecialValueText(tr("Off"))
        self._ram_spin.setSpecialValueText(tr("Off"))
        self._apply_sel_btn.setText(tr("Apply to Selected"))
        self._apply_all_btn.setText(tr("Apply to All Instances"))
        self._clear_btn.setText(tr("Clear Limits"))
        self._auto_chk.setText(tr("Auto-apply these limits to newly launched instances"))
        self._lbl_hint.setText(tr(
            "CPU limit is a percentage of the whole machine (all cores). "
            "RAM limit caps the working set (min 256 MB); Roblox pages out instead of crashing, but may stutter."
        ))
        self._close_btn.setText(tr("Close"))

    def _apply_fps(self):
        fps = self._fps_spin.value()
        ok = set_fps_cap(fps)
        self._settings.set("General", "MaxFPSValue", str(fps))
        if ok:
            QMessageBox.information(self, tr("Mixer"), tr("FPS cap updated"))
        else:
            QMessageBox.warning(self, tr("Mixer"), tr("FPS update failed"))

    # ---- 視窗大小 ----
    def _window_settings(self) -> "wl.WindowSettings":
        w, h = wl.clamp_size(self._win_w.value(), self._win_h.value())
        return wl.WindowSettings(
            apply_on_launch=self._win_launch_chk.isChecked(), width=w, height=h,
            tile=self._tile_chk.isChecked(),
        )

    def _save_window_settings(self):
        try:
            wl.save_settings(self._window_settings())
        except Exception:
            pass

    def _apply_windows(self):
        self._save_window_settings()
        pids = self._selected_pids() or self._all_pids()
        if not pids:
            QMessageBox.information(self, tr("Mixer"), tr("No running Roblox instances"))
            return
        ok, total = wl.arrange_all(pids, self._window_settings())
        self._status(f"{tr('Window size')}: {ok}/{total}")
        if ok < total:
            QMessageBox.warning(
                self, tr("Mixer"),
                tr("Some windows could not be resized (not created yet, or in fullscreen mode)."),
            )

    # ---- 限制 ----
    def _current_limits(self) -> Limits:
        return clamp_limits(self._cpu_spin.value(), self._ram_spin.value())

    def _save_default_limits(self):
        try:
            save_defaults(self._current_limits(), self._auto_chk.isChecked())
        except Exception:
            pass

    def _selected_pids(self) -> list[int]:
        pids = []
        for idx in self._table.selectionModel().selectedRows():
            item = self._table.item(idx.row(), 1)
            if item and item.text().isdigit():
                pids.append(int(item.text()))
        return pids

    def _all_pids(self) -> list[int]:
        pids = []
        for row in range(self._table.rowCount()):
            item = self._table.item(row, 1)
            if item and item.text().isdigit():
                pids.append(int(item.text()))
        return pids

    def _run_limits(self, pids: list[int], limits: Limits):
        ok, errors = 0, []
        for pid in pids:
            try:
                apply_limits(pid, limits)
                ok += 1
            except LimitError as e:
                errors.append(f"{pid}: {e}")
        label = limits.describe() or tr("Cleared")
        self._status(f"{tr('Limits')}: {label} → {ok}/{len(pids)}")
        if errors:
            QMessageBox.warning(self, tr("Mixer"), "\n".join(errors))
        self._refresh()

    def _apply_limit_values(self, selected_only: bool):
        pids = self._selected_pids() if selected_only else self._all_pids()
        if not pids:
            QMessageBox.information(
                self, tr("Mixer"),
                tr("Select at least one instance first") if selected_only else tr("No running Roblox instances"),
            )
            return
        limits = self._current_limits()
        # 把修正後的值回填（例如 RAM 100 → 256）
        self._cpu_spin.setValue(limits.cpu_percent)
        self._ram_spin.setValue(limits.memory_mb)
        self._save_default_limits()
        self._run_limits(pids, limits)

    def _clear_limits(self):
        pids = self._selected_pids() or self._all_pids()
        if pids:
            self._run_limits(pids, Limits())

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
        selected = set(self._selected_pids())

        entries = list_processes_with_volume()
        live = {e["process"].pid for e in entries}
        for pid in list(self._procs):
            if pid not in live:
                del self._procs[pid]

        self._table.setRowCount(len(entries))
        for row, entry in enumerate(entries):
            proc = entry["process"]
            volume = entry["volume"]
            account = tracker_map.get(proc.tracker_id)
            name = account.username if account else tr("Unknown")

            cpu_txt = "-"
            try:
                sp = self._procs.get(proc.pid)
                if sp is None:
                    sp = psutil.Process(proc.pid)
                    sp.cpu_percent(None)  # 第一次取樣只是建立基準
                    self._procs[proc.pid] = sp
                else:
                    cpu_txt = f"{sp.cpu_percent(None) / self._ncpu:.1f}"
            except psutil.Error:
                pass
            applied = get_applied(proc.pid)

            self._table.setItem(row, 0, QTableWidgetItem(name))
            self._table.setItem(row, 1, QTableWidgetItem(str(proc.pid)))
            self._table.setItem(row, 2, QTableWidgetItem(f"{proc.memory_mb:.0f}"))
            self._table.setItem(row, 3, QTableWidgetItem(cpu_txt))
            self._table.setItem(row, 4, QTableWidgetItem(applied.describe() or "-"))

            slider = QSlider(Qt.Horizontal)
            slider.setRange(0, 100)
            slider.setValue(int((volume or 0.0) * 100))
            slider.setEnabled(volume is not None)
            slider.valueChanged.connect(
                lambda val, pid=proc.pid: set_process_volume(pid, val / 100.0)
            )
            self._table.setCellWidget(row, 5, slider)
            if proc.pid in selected:
                self._table.selectRow(row)

    def closeEvent(self, event):
        self._timer.stop()
        super().closeEvent(event)
