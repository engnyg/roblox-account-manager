"""
帳號工具對話框。
對應 C# Forms/AccountUtils.cs。
"""

from __future__ import annotations

from typing import TYPE_CHECKING

from PySide6.QtWidgets import (
    QDialog, QVBoxLayout, QFormLayout, QLineEdit, QPushButton,
    QLabel, QGroupBox, QHBoxLayout, QWidget, QMessageBox,
)

from manager.account_utils import change_password, change_email, logout_other_sessions, toggle_block
from core.i18n import tr, get_i18n

if TYPE_CHECKING:
    from core.account import Account


class AccountUtilsDialog(QDialog):
    def __init__(self, account: "Account", parent: QWidget | None = None):
        super().__init__(parent)
        self._account = account
        self.setMinimumWidth(420)
        self._setup_ui()
        get_i18n().language_changed.connect(self._retranslate_ui)
        self._retranslate_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)

        self._pw_group = QGroupBox()
        pw_form = QFormLayout(self._pw_group)
        self._current_pw = QLineEdit()
        self._current_pw.setEchoMode(QLineEdit.Password)
        self._current_pw.setText(self._account.password)
        self._new_pw = QLineEdit()
        self._new_pw.setEchoMode(QLineEdit.Password)
        self._lbl_cur = QLabel()
        self._lbl_new_pw = QLabel()
        pw_form.addRow(self._lbl_cur, self._current_pw)
        pw_form.addRow(self._lbl_new_pw, self._new_pw)
        self._change_pw_btn = QPushButton()
        self._change_pw_btn.clicked.connect(self._change_password)
        pw_form.addRow("", self._change_pw_btn)
        layout.addWidget(self._pw_group)

        self._email_group = QGroupBox()
        email_form = QFormLayout(self._email_group)
        self._email_pw = QLineEdit()
        self._email_pw.setEchoMode(QLineEdit.Password)
        self._new_email = QLineEdit()
        self._lbl_email_pw = QLabel()
        self._lbl_new_email = QLabel()
        email_form.addRow(self._lbl_email_pw, self._email_pw)
        email_form.addRow(self._lbl_new_email, self._new_email)
        self._change_email_btn = QPushButton()
        self._change_email_btn.clicked.connect(self._change_email)
        email_form.addRow("", self._change_email_btn)
        layout.addWidget(self._email_group)

        self._misc_group = QGroupBox()
        misc_layout = QVBoxLayout(self._misc_group)

        self._logout_btn = QPushButton()
        self._logout_btn.clicked.connect(self._logout_others)
        misc_layout.addWidget(self._logout_btn)

        block_layout = QHBoxLayout()
        self._block_user = QLineEdit()
        self._block_btn = QPushButton()
        self._block_btn.clicked.connect(self._toggle_block)
        block_layout.addWidget(self._block_user)
        block_layout.addWidget(self._block_btn)
        misc_layout.addLayout(block_layout)

        layout.addWidget(self._misc_group)

        self._close_btn = QPushButton()
        self._close_btn.clicked.connect(self.accept)
        layout.addWidget(self._close_btn)

    def _retranslate_ui(self):
        self.setWindowTitle(f"{tr('Account Tools')} — {self._account.username}")
        self._pw_group.setTitle(tr("Change Password"))
        self._lbl_cur.setText(tr("Current:"))
        self._lbl_new_pw.setText(tr("New:"))
        self._change_pw_btn.setText(tr("Change Password"))
        self._email_group.setTitle(tr("Change Email"))
        self._lbl_email_pw.setText(tr("Password:"))
        self._lbl_new_email.setText(tr("New email:"))
        self._change_email_btn.setText(tr("Change Email"))
        self._misc_group.setTitle(tr("Other"))
        self._logout_btn.setText(tr("Log out of other sessions"))
        self._block_user.setPlaceholderText(tr("Username to block/unblock"))
        self._block_btn.setText(tr("Toggle Block"))
        self._close_btn.setText(tr("Close"))

    def _change_password(self):
        ok, msg = change_password(self._account, self._current_pw.text(), self._new_pw.text())
        QMessageBox.information(self, tr("Change Password"), msg)

    def _change_email(self):
        ok, msg = change_email(self._account, self._email_pw.text(), self._new_email.text())
        QMessageBox.information(self, tr("Change Email"), msg)

    def _logout_others(self):
        ok, msg = logout_other_sessions(self._account)
        QMessageBox.information(self, tr("Log out of other sessions"), msg)

    def _toggle_block(self):
        username = self._block_user.text().strip()
        if not username:
            return
        ok, msg = toggle_block(self._account, username)
        QMessageBox.information(self, tr("Toggle Block"), msg)
