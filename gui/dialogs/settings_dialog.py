"""
設定對話框。
對應 C# Forms/SettingsForm.cs。
"""

from __future__ import annotations

from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QTabWidget, QWidget, QFormLayout,
    QLabel, QLineEdit, QCheckBox, QSpinBox, QComboBox,
    QDialogButtonBox, QGroupBox, QPushButton,
)

from core.settings import get_settings
from core.i18n import tr, get_i18n, LANGUAGE_NAMES, init_language
from gui.theme.theme_engine import get_theme_names, apply_theme


class SettingsDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setMinimumWidth(480)
        self._settings = get_settings()
        self._setup_ui()
        self._load()
        self._retranslate_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        self._tabs = QTabWidget()

        # --- General tab ---
        gen_tab = QWidget()
        gen_form = QFormLayout(gen_tab)

        theme_row = QWidget()
        theme_hl = QHBoxLayout(theme_row)
        theme_hl.setContentsMargins(0, 0, 0, 0)
        self._theme = QComboBox()
        self._theme.addItems(get_theme_names())
        theme_hl.addWidget(self._theme, 1)
        self._btn_theme_editor = QPushButton("編輯…")
        self._btn_theme_editor.setFixedWidth(56)
        self._btn_theme_editor.clicked.connect(self._open_theme_editor)
        theme_hl.addWidget(self._btn_theme_editor)
        self._lbl_theme = QLabel()
        gen_form.addRow(self._lbl_theme, theme_row)

        self._language = QComboBox()
        for code, name in LANGUAGE_NAMES.items():
            self._language.addItem(name, code)
        self._lbl_lang = QLabel()
        gen_form.addRow(self._lbl_lang, self._language)

        self._join_delay = QSpinBox()
        self._join_delay.setRange(0, 60)
        self._lbl_delay = QLabel()
        gen_form.addRow(self._lbl_delay, self._join_delay)

        self._auto_refresh = QCheckBox()
        self._lbl_refresh = QLabel()
        gen_form.addRow(self._lbl_refresh, self._auto_refresh)

        self._auto_close = QCheckBox()
        self._lbl_close = QLabel()
        gen_form.addRow(self._lbl_close, self._auto_close)

        self._show_presence = QCheckBox()
        self._lbl_presence = QLabel()
        gen_form.addRow(self._lbl_presence, self._show_presence)

        self._unlock_fps = QCheckBox()
        self._lbl_fps = QLabel()
        gen_form.addRow(self._lbl_fps, self._unlock_fps)

        self._max_fps = QSpinBox()
        self._max_fps.setRange(30, 10000)
        self._lbl_maxfps = QLabel()
        gen_form.addRow(self._lbl_maxfps, self._max_fps)

        self._tabs.addTab(gen_tab, "")

        # --- Web API tab ---
        ws_tab = QWidget()
        ws_form = QFormLayout(ws_tab)

        self._ws_enabled = QCheckBox()
        self._lbl_ws_en = QLabel()
        ws_form.addRow(self._lbl_ws_en, self._ws_enabled)

        self._ws_password = QLineEdit()
        self._ws_password.setEchoMode(QLineEdit.Password)
        self._lbl_ws_pw = QLabel()
        ws_form.addRow(self._lbl_ws_pw, self._ws_password)

        self._ws_port = QSpinBox()
        self._ws_port.setRange(1024, 65535)
        self._lbl_ws_port = QLabel()
        ws_form.addRow(self._lbl_ws_port, self._ws_port)

        self._tabs.addTab(ws_tab, "")

        # --- Nexus tab ---
        nexus_tab = QWidget()
        nexus_form = QFormLayout(nexus_tab)

        self._nexus_enabled = QCheckBox()
        self._lbl_nx_en = QLabel()
        nexus_form.addRow(self._lbl_nx_en, self._nexus_enabled)

        self._nexus_port = QSpinBox()
        self._nexus_port.setRange(1024, 65535)
        self._lbl_nx_port = QLabel()
        nexus_form.addRow(self._lbl_nx_port, self._nexus_port)

        self._tabs.addTab(nexus_tab, "")

        layout.addWidget(self._tabs)

        self._buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        self._buttons.accepted.connect(self._save)
        self._buttons.rejected.connect(self.reject)
        layout.addWidget(self._buttons)

    def _retranslate_ui(self):
        self.setWindowTitle(tr("Settings"))
        self._tabs.setTabText(0, tr("General"))
        self._tabs.setTabText(1, tr("Web API"))
        self._tabs.setTabText(2, tr("Nexus"))

        # General
        self._lbl_theme.setText(tr("Theme:"))
        self._lbl_lang.setText(tr("Language:"))
        self._lbl_delay.setText(tr("Join delay (s):"))
        self._lbl_refresh.setText(tr("Auto cookie refresh:"))
        self._lbl_close.setText(tr("Auto close last process:"))
        self._lbl_presence.setText(tr("Show presence:"))
        self._lbl_fps.setText(tr("Unlock FPS:"))
        self._lbl_maxfps.setText(tr("Max FPS:"))

        # Web API
        self._lbl_ws_en.setText(tr("Enable Web API:"))
        self._lbl_ws_pw.setText(tr("Password:"))
        self._lbl_ws_port.setText(tr("Port:"))

        # Nexus
        self._lbl_nx_en.setText(tr("Enable Nexus:"))
        self._lbl_nx_port.setText(tr("Port:"))

    def _load(self):
        s = self._settings

        theme = s.get("General", "Theme", "Dark")
        idx = self._theme.findText(theme)
        if idx >= 0:
            self._theme.setCurrentIndex(idx)

        lang = s.get("General", "Language", "en")
        for i in range(self._language.count()):
            if self._language.itemData(i) == lang:
                self._language.setCurrentIndex(i)
                break

        self._join_delay.setValue(s.get_int("General", "AccountJoinDelay", 8))
        self._auto_refresh.setChecked(s.get_bool("General", "AutoCookieRefresh", True))
        self._auto_close.setChecked(s.get_bool("General", "AutoCloseLastProcess", True))
        self._show_presence.setChecked(s.get_bool("General", "ShowPresence", True))
        self._unlock_fps.setChecked(s.get_bool("General", "UnlockFPS", False))
        self._max_fps.setValue(s.get_int("General", "MaxFPSValue", 120))

        self._ws_enabled.setChecked(s.get_bool("WebServer", "Enabled", False))
        self._ws_password.setText(s.get("WebServer", "Password", ""))
        self._ws_port.setValue(s.get_int("WebServer", "Port", 7963))

        self._nexus_enabled.setChecked(s.get_bool("AccountControl", "Enabled", False))
        self._nexus_port.setValue(s.get_int("AccountControl", "Port", 7964))

    def _save(self):
        s = self._settings

        new_theme = self._theme.currentText()
        s.set("General", "Theme", new_theme)

        new_lang = self._language.currentData()
        s.set("General", "Language", new_lang)

        s.set("General", "AccountJoinDelay", str(self._join_delay.value()))
        s.set("General", "AutoCookieRefresh", str(self._auto_refresh.isChecked()).lower())
        s.set("General", "AutoCloseLastProcess", str(self._auto_close.isChecked()).lower())
        s.set("General", "ShowPresence", str(self._show_presence.isChecked()).lower())
        s.set("General", "UnlockFPS", str(self._unlock_fps.isChecked()).lower())
        s.set("General", "MaxFPSValue", str(self._max_fps.value()))

        s.set("WebServer", "Enabled", str(self._ws_enabled.isChecked()).lower())
        s.set("WebServer", "Password", self._ws_password.text())
        s.set("WebServer", "Port", str(self._ws_port.value()))

        s.set("AccountControl", "Enabled", str(self._nexus_enabled.isChecked()).lower())
        s.set("AccountControl", "Port", str(self._nexus_port.value()))

        # 套用主題與語言
        apply_theme(new_theme)
        get_i18n().set_language(new_lang)

        self.accept()

    def _open_theme_editor(self):
        from gui.dialogs.theme_editor_dialog import ThemeEditorDialog
        dlg = ThemeEditorDialog(self)
        dlg.exec()
        # 編輯器關閉後刷新主題下拉（可能新增了自訂主題）
        current = self._theme.currentText()
        self._theme.clear()
        self._theme.addItems(get_theme_names())
        idx = self._theme.findText(current)
        if idx >= 0:
            self._theme.setCurrentIndex(idx)
