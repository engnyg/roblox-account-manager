"""
主視窗。
對應 C# AccountManager.cs Form + Designer。
"""

from __future__ import annotations

import asyncio
import sys
from typing import Optional

from PySide6.QtCore import Qt, QTimer, QThread, Signal, QObject, Slot
from PySide6.QtWidgets import (
    QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QLineEdit, QPushButton, QLabel, QStatusBar,
    QMenuBar, QMenu, QToolBar, QSplitter,
    QGroupBox, QComboBox, QApplication, QMessageBox,
)
from PySide6.QtGui import QAction
import qasync

from core.account import Account
from core.account_store import load_accounts, save_accounts
from core.settings import get_settings
from core.i18n import tr, get_i18n
from gui.widgets.account_table import AccountTable
from gui.theme.theme_engine import apply_theme, set_background_widget
from gui.theme.background_central_widget import BackgroundCentralWidget


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self._accounts: list[Account] = []
        self._selected: Optional[Account] = None
        self._settings = get_settings()
        self._nexus_server = None
        self._web_server = None

        self._setup_menu()
        self._setup_toolbar()
        self._setup_central()
        self._setup_statusbar()

        self.resize(1100, 700)
        apply_theme()
        self._load_accounts()
        self._start_servers()

        # 啟動時自動開啟 Multi-Roblox
        from manager.multi_roblox import enable_multi_roblox
        enable_multi_roblox()

        # 語言切換時重新整理 UI 文字
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()

    # ------------------------------------------------------------------ #
    # UI Setup
    # ------------------------------------------------------------------ #

    def _setup_menu(self):
        mb = self.menuBar()

        self._menu_file = mb.addMenu("")
        self._act_import = self._add_action(self._menu_file, "", self._import_accounts)
        self._act_export = self._add_action(self._menu_file, "", self._export_cookies)
        self._act_export_info = self._add_action(self._menu_file, "", self._export_account_info)
        self._menu_file.addSeparator()
        self._act_exit = self._add_action(self._menu_file, "", self.close)

        self._menu_accounts = mb.addMenu("")
        self._act_generate = self._add_action(self._menu_accounts, "", self._open_generator)
        self._act_manual_login = self._add_action(self._menu_accounts, "", self._manual_login)
        self._act_utils = self._add_action(self._menu_accounts, "", self._open_account_utils)
        self._act_fields = self._add_action(self._menu_accounts, "", self._open_fields)
        self._menu_accounts.addSeparator()
        self._act_refresh = self._add_action(self._menu_accounts, "", self._refresh_cookies)

        self._menu_game = mb.addMenu("")
        self._act_join = self._add_action(self._menu_game, "", self._join_server)
        self._act_servers = self._add_action(self._menu_game, "", self._open_server_list)
        self._act_find = self._add_action(self._menu_game, "", self._find_player)
        self._menu_game.addSeparator()
        self._act_multi = self._add_action(self._menu_game, "", self._toggle_multi_roblox)
        self._act_fps = self._add_action(self._menu_game, "", self._toggle_fps_unlock)

        self._menu_tools = mb.addMenu("")
        self._act_nexus = self._add_action(self._menu_tools, "", self._open_nexus)
        self._menu_tools.addSeparator()
        self._act_theme = self._add_action(self._menu_tools, "", self._open_theme_editor)
        self._act_settings = self._add_action(self._menu_tools, "", self._open_settings)
        self._act_update = self._add_action(self._menu_tools, "", self._check_updates)

    def _add_action(self, menu: QMenu, text: str, slot) -> QAction:
        action = QAction(text, self)
        action.triggered.connect(slot)
        menu.addAction(action)
        return action

    def _setup_toolbar(self):
        self._toolbar = QToolBar("Main", self)
        self.addToolBar(self._toolbar)

        self._lbl_search = QLabel("")
        self._toolbar.addWidget(self._lbl_search)
        self._search = QLineEdit()
        self._search.setMaximumWidth(200)
        self._search.textChanged.connect(self._on_search)
        self._toolbar.addWidget(self._search)

        self._toolbar.addSeparator()

        self._lbl_place = QLabel("")
        self._toolbar.addWidget(self._lbl_place)
        self._place_id = QLineEdit()
        self._place_id.setMaximumWidth(130)
        self._toolbar.addWidget(self._place_id)

        self._lbl_job = QLabel("")
        self._toolbar.addWidget(self._lbl_job)
        self._job_id = QLineEdit()
        self._job_id.setMaximumWidth(200)
        self._toolbar.addWidget(self._job_id)

        self._join_btn = QPushButton("")
        self._join_btn.clicked.connect(self._join_server)
        self._toolbar.addWidget(self._join_btn)

        # 載入上次的 Place ID
        saved_place = self._settings.get("General", "LastPlaceID", "")
        if saved_place:
            self._place_id.setText(saved_place)
        self._place_id.textChanged.connect(
            lambda text: self._settings.set("General", "LastPlaceID", text.strip())
        )

    def _setup_central(self):
        # BackgroundCentralWidget 本身就是 central widget，
        # 背景畫在它的 paintEvent，子 widget 的 rgba 會透出底層背景
        central = BackgroundCentralWidget()
        self.setCentralWidget(central)
        set_background_widget(central)

        layout = QVBoxLayout(central)
        layout.setContentsMargins(4, 4, 4, 4)

        self._table = AccountTable()
        self._table.account_double_clicked.connect(self._on_double_click)
        self._table.context_menu_requested.connect(self._on_context_menu)
        layout.addWidget(self._table)

    def _setup_statusbar(self):
        self._statusbar = QStatusBar()
        self.setStatusBar(self._statusbar)
        self._status_label = QLabel(tr("Ready"))
        self._statusbar.addWidget(self._status_label)
        self._account_count_label = QLabel("")
        self._statusbar.addPermanentWidget(self._account_count_label)

    # ------------------------------------------------------------------ #
    # Retranslate (called on language change)
    # ------------------------------------------------------------------ #

    def _retranslate_ui(self):
        self.setWindowTitle(tr("Roblox Account Manager V2"))

        # Menus
        self._menu_file.setTitle(tr("File"))
        self._act_import.setText(tr("Import Accounts"))
        self._act_export.setText(tr("Export Cookies"))
        self._act_export_info.setText(tr("Export Account Info"))
        self._act_exit.setText(tr("Exit"))

        self._menu_accounts.setTitle(tr("Accounts"))
        self._act_generate.setText(tr("Generate Accounts"))
        self._act_manual_login.setText(tr("Manual Login"))
        self._act_utils.setText(tr("Account Tools"))
        self._act_fields.setText(tr("Custom Fields"))
        self._act_refresh.setText(tr("Refresh Cookies"))

        self._menu_game.setTitle(tr("Game"))
        self._act_join.setText(tr("Join Server"))
        self._act_servers.setText(tr("Server List"))
        self._act_find.setText(tr("Find Player"))
        self._act_multi.setText(tr("Multi-Roblox"))
        self._act_fps.setText(tr("FPS Unlocker"))

        self._menu_tools.setTitle(tr("Tools"))
        self._act_nexus.setText(tr("Nexus Control"))
        self._act_theme.setText(tr("Theme Editor"))
        self._act_settings.setText(tr("Settings"))
        self._act_update.setText(tr("Check for Updates"))

        # Toolbar
        self._lbl_search.setText(tr("Search: "))
        self._search.setPlaceholderText(tr("Filter accounts..."))
        self._lbl_place.setText(tr("Place ID: "))
        self._place_id.setPlaceholderText(tr("Place ID"))
        self._lbl_job.setText(tr("Job ID: "))
        self._job_id.setPlaceholderText(tr("Job ID (optional)"))
        self._join_btn.setText(tr("Join"))

        # Table headers
        self._table.retranslate()

        # Status
        count = len(self._accounts)
        self._account_count_label.setText(f"{count} {tr('accounts')}")

    # ------------------------------------------------------------------ #
    # Account management
    # ------------------------------------------------------------------ #

    def _load_accounts(self):
        self._accounts = load_accounts()
        self._refresh_table()
        self._status_label.setText(f"{tr('Loaded')} {len(self._accounts)} {tr('accounts')}")

    def _refresh_table(self):
        self._table.set_accounts(self._accounts)
        self._account_count_label.setText(f"{len(self._accounts)} {tr('accounts')}")

    def _save_accounts(self):
        save_accounts(self._accounts)

    def _on_search(self, text: str):
        self._table.filter(text)

    def _on_double_click(self, account: Account):
        self._selected = account
        self._join_server_for(account)

    def _on_context_menu(self, account: Account, pos):
        menu = QMenu(self)
        menu.addAction(tr("Join Server"), lambda: self._join_server_for(account))
        menu.addAction(tr("Server List"), lambda: self._open_server_list_for(account))
        menu.addSeparator()
        menu.addAction(tr("Account Tools"), lambda: self._open_account_utils_for(account))
        menu.addAction(tr("Custom Fields"), lambda: self._open_fields_for(account))
        menu.addAction(tr("Open Browser"), lambda: self._open_browser_for(account))
        menu.addSeparator()
        menu.addAction(tr("Copy Cookie"), lambda: self._copy_cookie(account))
        menu.addSeparator()
        menu.addAction(tr("Remove"), lambda: self._remove_account(account))
        menu.exec(pos)

    # ------------------------------------------------------------------ #
    # Actions
    # ------------------------------------------------------------------ #

    def _join_server(self):
        account = self._table.selected_account()
        if not account:
            self._status_label.setText(tr("No account selected"))
            return
        self._join_server_for(account)

    def _join_server_for(self, account: Account):
        try:
            place_id = int(self._place_id.text().strip())
        except ValueError:
            self._status_label.setText(tr("Enter a valid Place ID"))
            return
        job_id = self._job_id.text().strip()
        from manager.game_launcher import join_server
        ok, msg = join_server(account, place_id, job_id)
        self._status_label.setText(f"{account.username}: {msg}")

    def _import_accounts(self):
        from gui.dialogs.import_dialog import ImportDialog
        dlg = ImportDialog(self)
        dlg.accounts_imported.connect(self._on_accounts_imported)
        dlg.exec()

    def _on_accounts_imported(self, accounts: list[Account]):
        existing_names = {a.username for a in self._accounts}
        new_accounts = [a for a in accounts if a.username not in existing_names]
        self._accounts.extend(new_accounts)
        self._refresh_table()
        self._save_accounts()
        self._status_label.setText(f"{tr('Imported')} {len(new_accounts)} {tr('accounts')}")

    def _export_cookies(self):
        from PySide6.QtWidgets import QFileDialog
        from core.account_store import export_cookie_strings
        path, _ = QFileDialog.getSaveFileName(self, tr("Export Cookies"), "cookies.txt", "Text files (*.txt)")
        if path:
            text = export_cookie_strings(self._accounts)
            with open(path, "w", encoding="utf-8") as f:
                f.write(text)
            self._status_label.setText(f"{tr('Exported to')} {path}")

    def _export_account_info(self):
        from PySide6.QtWidgets import QFileDialog
        from core.account_store import export_account_info
        path, _ = QFileDialog.getSaveFileName(self, tr("Export Account Info"), "accounts_info.txt", "Text files (*.txt)")
        if path:
            text = export_account_info(self._accounts)
            with open(path, "w", encoding="utf-8") as f:
                f.write(text)
            self._status_label.setText(f"{tr('Exported to')} {path}")

    def _open_generator(self):
        from gui.dialogs.generator_dialog import GeneratorDialog
        dlg = GeneratorDialog(self)
        dlg.accounts_generated.connect(self._on_accounts_imported)
        dlg.exec()

    def _manual_login(self):
        self._status_label.setText(tr("Manual Login: browser opening..."))
        self._act_manual_login.setEnabled(False)

        class _Worker(QObject):
            success = Signal(object)
            error = Signal(str)

            def run(self_w):
                from manager.manual_login import run_manual_login
                run_manual_login(
                    on_success=lambda acc: self_w.success.emit(acc),
                    on_error=lambda msg: self_w.error.emit(msg),
                )

        self._ml_worker = _Worker()
        self._ml_thread = QThread(self)
        self._ml_worker.moveToThread(self._ml_thread)
        self._ml_thread.started.connect(self._ml_worker.run)
        self._ml_worker.success.connect(self._on_manual_login_success)
        self._ml_worker.error.connect(self._on_manual_login_error)
        self._ml_worker.success.connect(lambda _: self._ml_thread.quit())
        self._ml_worker.error.connect(lambda _: self._ml_thread.quit())
        self._ml_thread.finished.connect(lambda: self._act_manual_login.setEnabled(True))
        self._ml_thread.start()

    def _on_manual_login_success(self, account: Account):
        self._on_accounts_imported([account])
        self._status_label.setText(f"Manual Login: {account.username}")

    def _on_manual_login_error(self, msg: str):
        self._status_label.setText(f"Manual Login failed: {msg}")

    def _open_account_utils(self):
        account = self._table.selected_account()
        if account:
            self._open_account_utils_for(account)

    def _open_account_utils_for(self, account: Account):
        from gui.dialogs.account_utils_dialog import AccountUtilsDialog
        dlg = AccountUtilsDialog(account, self)
        dlg.exec()
        self._save_accounts()

    def _open_fields(self):
        account = self._table.selected_account()
        if account:
            self._open_fields_for(account)

    def _open_fields_for(self, account: Account):
        from gui.dialogs.account_fields_dialog import AccountFieldsDialog
        dlg = AccountFieldsDialog(account, self)
        if dlg.exec():
            self._save_accounts()
            self._refresh_table()

    def _open_server_list(self):
        account = self._table.selected_account()
        if account:
            self._open_server_list_for(account)

    def _open_server_list_for(self, account: Account):
        from gui.dialogs.server_list_dialog import ServerListDialog
        dlg = ServerListDialog(account, self)
        dlg.join_requested.connect(lambda jid: (self._job_id.setText(jid), self._join_server_for(account)))
        dlg.exec()

    def _find_player(self):
        from PySide6.QtWidgets import QInputDialog
        username, ok = QInputDialog.getText(self, tr("Find Player"), tr("Target username:"))
        if not ok or not username:
            return
        try:
            place_id = int(self._place_id.text().strip())
        except ValueError:
            self._status_label.setText(tr("Enter a Place ID first"))
            return
        from manager.player_finder import find_player_server
        job_id = find_player_server(place_id, username)
        if job_id:
            self._job_id.setText(job_id)
            self._status_label.setText(f"{tr('Found')} {username} {tr('in job')} {job_id}")
        else:
            self._status_label.setText(f"{username} {tr('not found in place')} {place_id}")

    def _refresh_cookies(self):
        from core.cookie_manager import CookieRefreshManager
        mgr = CookieRefreshManager(lambda: self._accounts, self._save_accounts)
        mgr.refresh_all()
        self._status_label.setText(tr("Cookies refreshed"))

    def _toggle_multi_roblox(self):
        from manager.multi_roblox import enable_multi_roblox
        if enable_multi_roblox():
            self._status_label.setText(tr("Multi-Roblox enabled"))
        else:
            self._status_label.setText(tr("Multi-Roblox failed"))

    def _toggle_fps_unlock(self):
        from manager.fps_unlocker import set_fps_cap
        unlocked = self._settings.get_bool("General", "UnlockFPS", False)
        max_fps = self._settings.get_int("General", "MaxFPSValue", 120)
        ok = set_fps_cap(0 if unlocked else max_fps)
        self._status_label.setText(tr("FPS cap updated") if ok else tr("FPS update failed"))

    def _open_nexus(self):
        from gui.dialogs.nexus_dialog import NexusDialog
        dlg = NexusDialog(self._nexus_server, self)
        dlg.exec()

    def _open_theme_editor(self):
        from gui.dialogs.theme_editor_dialog import ThemeEditorDialog
        dlg = ThemeEditorDialog(self)
        dlg.exec()

    def _open_settings(self):
        from gui.dialogs.settings_dialog import SettingsDialog
        dlg = SettingsDialog(self)
        dlg.exec()

    def _check_updates(self):
        from gui.dialogs.updater_dialog import UpdaterDialog
        dlg = UpdaterDialog(self)
        dlg.exec()

    def _open_browser_for(self, account: Account):
        loop = asyncio.get_event_loop()
        from manager.account_browser import open_account_browser
        loop.create_task(open_account_browser(account))

    def _copy_cookie(self, account: Account):
        import pyperclip
        pyperclip.copy(account.security_token)
        self._status_label.setText(f"{tr('Copied cookie for')} {account.username}")

    def _remove_account(self, account: Account):
        self._accounts = [a for a in self._accounts if a is not account]
        self._refresh_table()
        self._save_accounts()
        self._status_label.setText(f"{tr('Removed')} {account.username}")

    # ------------------------------------------------------------------ #
    # Servers
    # ------------------------------------------------------------------ #

    def _start_servers(self):
        settings = self._settings
        loop = asyncio.get_event_loop()

        if settings.get_bool("AccountControl", "Enabled", False):
            from nexus.websocket_server import NexusServer
            port = settings.get_int("AccountControl", "Port", 7964)
            self._nexus_server = NexusServer(port=port)
            loop.create_task(self._nexus_server.start())

        if settings.get_bool("WebServer", "Enabled", False):
            from webapi.web_server import WebApiServer
            port = settings.get_int("WebServer", "Port", 7963)
            password = settings.get("WebServer", "Password", "")
            self._web_server = WebApiServer(
                port=port,
                password=password,
                get_accounts=lambda: self._accounts,
                get_selected=lambda: self._selected,
            )
            loop.create_task(self._web_server.start())

    def closeEvent(self, event):
        self._save_accounts()
        loop = asyncio.get_event_loop()
        if self._nexus_server:
            loop.create_task(self._nexus_server.stop())
        if self._web_server:
            loop.create_task(self._web_server.stop())
        super().closeEvent(event)
