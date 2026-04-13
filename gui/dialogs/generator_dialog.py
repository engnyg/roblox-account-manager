"""
帳號產生器對話框。
對應 C# Forms/（無，這是新增功能）+ 現有 main.py CLI 邏輯的 GUI 版本。
"""

from __future__ import annotations

import asyncio
from typing import Callable, Optional

from PySide6.QtCore import Qt, QThread, Signal, QObject
from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QFormLayout,
    QLineEdit, QCheckBox, QSpinBox, QPushButton,
    QTextEdit, QProgressBar, QLabel, QGroupBox,
    QDialogButtonBox, QWidget,
)

from generator.account_generator import AccountGenerator, GeneratorConfig
from core.roblox_api import validate_password
from core.i18n import tr, get_i18n


class _GeneratorWorker(QObject):
    progress = Signal(str, int)
    finished = Signal(list)  # list[GeneratorResult]
    error = Signal(str)

    def __init__(self, config: GeneratorConfig):
        super().__init__()
        self._config = config

    def run(self):
        try:
            loop = asyncio.new_event_loop()
            gen = AccountGenerator(self._config, lambda msg, pct: self.progress.emit(msg, pct))
            results = loop.run_until_complete(gen.run())
            loop.close()
            self.finished.emit(results)
        except Exception as e:
            self.error.emit(str(e))


class GeneratorDialog(QDialog):
    accounts_generated = Signal(list)

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.setMinimumWidth(520)
        self._thread: Optional[QThread] = None
        self._worker: Optional[_GeneratorWorker] = None
        self._setup_ui()
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)

        # --- Settings ---
        self._form_group = QGroupBox()
        form = QFormLayout(self._form_group)

        self._password = QLineEdit()
        self._password.setEchoMode(QLineEdit.Password)
        self._lbl_pw = QLabel()
        form.addRow(self._lbl_pw, self._password)

        self._name_format = QLineEdit()
        self._lbl_name = QLabel()
        form.addRow(self._lbl_name, self._name_format)

        self._count = QSpinBox()
        self._count.setRange(1, 100)
        self._count.setValue(1)
        self._lbl_count = QLabel()
        form.addRow(self._lbl_count, self._count)

        self._follow = QLineEdit()
        self._lbl_follow = QLabel()
        form.addRow(self._lbl_follow, self._follow)

        self._proxies = QLineEdit()
        self._lbl_proxies = QLabel()
        form.addRow(self._lbl_proxies, self._proxies)

        self._captcha_key = QLineEdit()
        self._lbl_captcha = QLabel()
        form.addRow(self._lbl_captcha, self._captcha_key)

        self._chrome_profile = QLineEdit()
        self._lbl_profile = QLabel()
        form.addRow(self._lbl_profile, self._chrome_profile)

        # Checkboxes
        self._options_group = QGroupBox()
        opt_layout = QVBoxLayout(self._options_group)

        self._verification = QCheckBox()
        self._verification.setChecked(True)
        self._customization = QCheckBox()
        self._customization.setChecked(True)
        self._scrambled = QCheckBox()
        self._scrambled.setChecked(True)
        self._incognito = QCheckBox()
        self._incognito.setChecked(True)

        opt_layout.addWidget(self._verification)
        opt_layout.addWidget(self._customization)
        opt_layout.addWidget(self._scrambled)
        opt_layout.addWidget(self._incognito)

        layout.addWidget(self._form_group)
        layout.addWidget(self._options_group)

        # --- Progress ---
        self._progress_bar = QProgressBar()
        self._progress_bar.setRange(0, 100)
        self._progress_label = QLabel()
        layout.addWidget(self._progress_label)
        layout.addWidget(self._progress_bar)

        # --- Log ---
        self._log = QTextEdit()
        self._log.setReadOnly(True)
        self._log.setMaximumHeight(120)
        layout.addWidget(self._log)

        # --- Buttons ---
        btn_layout = QHBoxLayout()
        self._start_btn = QPushButton()
        self._start_btn.clicked.connect(self._start)
        self._cancel_btn = QPushButton()
        self._cancel_btn.clicked.connect(self.reject)
        btn_layout.addWidget(self._start_btn)
        btn_layout.addWidget(self._cancel_btn)
        layout.addLayout(btn_layout)

    def _retranslate_ui(self):
        self.setWindowTitle(tr("Account Generator"))
        self._form_group.setTitle(tr("Generation Settings"))
        self._lbl_pw.setText(tr("Password:"))
        self._password.setPlaceholderText(tr("Leave blank for random password"))
        self._name_format.setPlaceholderText(tr("Leave blank for random username"))
        self._lbl_name.setText(tr("Name prefix:"))
        self._lbl_count.setText(tr("Count:"))
        self._follow.setPlaceholderText(tr("username1, username2 (optional)"))
        self._lbl_follow.setText(tr("Follow users:"))
        self._proxies.setPlaceholderText(tr("http://ip:port, ... (optional)"))
        self._lbl_proxies.setText(tr("Proxies:"))
        self._captcha_key.setPlaceholderText(tr("NopeCHA API key (optional)"))
        self._lbl_captcha.setText(tr("Captcha key:"))
        self._chrome_profile.setPlaceholderText(tr("Chrome profile with NopeCHA installed (optional)"))
        self._lbl_profile.setText(tr("Chrome profile:"))
        self._options_group.setTitle(tr("Options"))
        self._verification.setText(tr("Email verification"))
        self._customization.setText(tr("Avatar customization"))
        self._scrambled.setText(tr("Scrambled username (faster)"))
        self._incognito.setText(tr("Incognito mode"))
        self._progress_label.setText(tr("Ready"))
        self._start_btn.setText(tr("Start"))
        self._cancel_btn.setText(tr("Close"))

    def _start(self):
        if self._thread and self._thread.isRunning():
            return

        password = self._password.text().strip()
        # 空白 = 每個帳號產生獨立隨機密碼（在 AccountGenerator 內處理）

        follow_raw = self._follow.text().strip()
        follow_users = [u.strip() for u in follow_raw.split(",") if u.strip()] if follow_raw else []

        proxy_raw = self._proxies.text().strip()
        proxies_raw = [p.strip() for p in proxy_raw.split(",") if p.strip()] if proxy_raw else []

        captcha_key = self._captcha_key.text().strip()
        chrome_profile = self._chrome_profile.text().strip() or None

        # Read version
        try:
            with open("version.txt", encoding="utf-8") as f:
                version = f.read().strip()
        except Exception:
            version = "unknown"

        config = GeneratorConfig(
            password=password,
            verification=self._verification.isChecked(),
            name_format=self._name_format.text().strip() or None,
            scrambled_username=self._scrambled.isChecked(),
            customization=self._customization.isChecked(),
            follow_users=follow_users,
            proxies=proxies_raw,
            captcha_api_key=captcha_key,
            browser_user_data_dir=chrome_profile,
            incognito=self._incognito.isChecked(),
            count=self._count.value(),
            version=version,
        )

        self._worker = _GeneratorWorker(config)
        self._thread = QThread(self)
        self._worker.moveToThread(self._thread)
        self._thread.started.connect(self._worker.run)
        self._worker.progress.connect(self._on_progress)
        self._worker.finished.connect(self._on_finished)
        self._worker.error.connect(self._on_error)
        self._thread.start()

        self._start_btn.setEnabled(False)
        self._log.append(tr("Starting generation..."))

    def _on_progress(self, msg: str, pct: int):
        self._progress_label.setText(msg)
        self._progress_bar.setValue(pct)
        self._log.append(msg)

    def _on_finished(self, results):
        if self._thread:
            self._thread.quit()
            self._thread.wait()
        self._start_btn.setEnabled(True)
        successful = [r for r in results if r.success]
        self._log.append(f"\nDone: {len(successful)}/{len(results)} accounts generated.")
        self._progress_bar.setValue(100)
        accounts = [r.account for r in successful if r.account]
        if accounts:
            self.accounts_generated.emit(accounts)

    def _on_error(self, msg: str):
        if self._thread:
            self._thread.quit()
            self._thread.wait()
        self._start_btn.setEnabled(True)
        self._log.append(f"ERROR: {msg}")
