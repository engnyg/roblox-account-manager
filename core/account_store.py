"""
帳號載入 / 儲存 / 加密 / 匯入匯出。
支援三種格式：
  1. 加密二進位 (accounts.dat) — 主要格式
  2. 純文字 (accounts.txt)     — 備份 / 相容舊版
  3. JSON cookies (cookies.json) — 產生器輸出格式
"""

from __future__ import annotations

import json
import os
import threading
from datetime import datetime
from typing import Optional

from loguru import logger

from core.account import Account
from core.constants import (
    ACCOUNTS_FILE, COOKIES_FILE, ENCRYPTED_ACCOUNTS_FILE, DATA_DIR,
)

_lock = threading.Lock()


def _ensure_data_dir():
    os.makedirs(DATA_DIR, exist_ok=True)


# ------------------------------------------------------------------ #
# Encrypted save / load
# ------------------------------------------------------------------ #

def save_accounts(accounts: list[Account], password: Optional[str] = None) -> bool:
    """Serialise and save accounts. Uses encryption when password is provided."""
    _ensure_data_dir()
    data = json.dumps([a.to_dict() for a in accounts], ensure_ascii=False, indent=2)

    with _lock:
        if password:
            try:
                from core.cryptography import encrypt
                blob = encrypt(data, password.encode())
                with open(ENCRYPTED_ACCOUNTS_FILE, "wb") as f:
                    f.write(blob)
                logger.debug(f"Saved {len(accounts)} accounts (encrypted)")
                return True
            except Exception as e:
                logger.error(f"Encryption failed: {e}")
                return False
        else:
            # Plain JSON fallback
            with open(ENCRYPTED_ACCOUNTS_FILE + ".json", "w", encoding="utf-8") as f:
                f.write(data)
            logger.debug(f"Saved {len(accounts)} accounts (plain JSON)")
            return True


def load_accounts(password: Optional[str] = None) -> list[Account]:
    """Load accounts from disk. Tries encrypted → plain JSON → cookies.json."""
    accounts: list[Account] = []

    # 1. Encrypted binary
    if os.path.exists(ENCRYPTED_ACCOUNTS_FILE) and password:
        try:
            from core.cryptography import decrypt
            with open(ENCRYPTED_ACCOUNTS_FILE, "rb") as f:
                blob = f.read()
            data = decrypt(blob, password.encode())
            accounts = [Account.from_dict(d) for d in json.loads(data)]
            logger.info(f"Loaded {len(accounts)} accounts (encrypted)")
            return accounts
        except Exception as e:
            logger.warning(f"Could not decrypt accounts.dat: {e}")

    # 2. Plain JSON fallback
    plain_path = ENCRYPTED_ACCOUNTS_FILE + ".json"
    if os.path.exists(plain_path):
        try:
            with open(plain_path, "r", encoding="utf-8") as f:
                accounts = [Account.from_dict(d) for d in json.load(f)]
            logger.info(f"Loaded {len(accounts)} accounts (plain JSON)")
            return accounts
        except Exception as e:
            logger.warning(f"Could not load plain JSON: {e}")

    # 3. Generator cookies.json
    if os.path.exists(COOKIES_FILE):
        accounts = import_from_cookies_json(COOKIES_FILE)
        if accounts:
            logger.info(f"Imported {len(accounts)} accounts from cookies.json")

    return accounts


# ------------------------------------------------------------------ #
# Generator output helpers
# ------------------------------------------------------------------ #

def append_account_txt(account: Account):
    """Append one account line to accounts.txt (plain-text backup)."""
    _ensure_data_dir()
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    parts = [
        f"Username: {account.username}",
        f"Password: {account.password}",
    ]
    if account.email:
        parts.append(f"Email: {account.email}")
    if account.email_password:
        parts.append(f"Email Password: {account.email_password}")
    line = ", ".join(parts) + f" (Created at {timestamp})\n"
    with _lock:
        with open(ACCOUNTS_FILE, "a", encoding="utf-8") as f:
            f.write(line)


def append_cookies_json(account_data: dict):
    """Append account data to cookies.json (generator output)."""
    _ensure_data_dir()
    with _lock:
        try:
            with open(COOKIES_FILE, "r", encoding="utf-8") as f:
                existing = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            existing = []
        existing.append(account_data)
        with open(COOKIES_FILE, "w", encoding="utf-8") as f:
            json.dump(existing, f, indent=4)


def import_from_cookies_json(path: str) -> list[Account]:
    """Convert cookies.json format into Account objects."""
    accounts = []
    try:
        with open(path, "r", encoding="utf-8") as f:
            raw = json.load(f)
        for entry in raw:
            cookie = ""
            for c in entry.get("cookies", []):
                if c.get("name") == ".ROBLOSECURITY":
                    cookie = c["value"]
                    break
            if not cookie:
                continue
            a = Account(
                security_token=cookie,
                username=entry.get("username", ""),
                password=entry.get("password", ""),
            )
            a.valid = bool(a.username)
            accounts.append(a)
    except Exception as e:
        logger.error(f"import_from_cookies_json: {e}")
    return accounts


def import_from_cookie_strings(lines: list[str]) -> list[Account]:
    """Import raw .ROBLOSECURITY cookie strings (one per line)."""
    accounts = []
    for line in lines:
        token = line.strip()
        if not token:
            continue
        a = Account(security_token=token)
        if a.fetch_info():
            accounts.append(a)
        else:
            logger.warning(f"Could not fetch info for token (len={len(token)})")
    return accounts


# ------------------------------------------------------------------ #
# Export
# ------------------------------------------------------------------ #

def export_cookie_strings(accounts: list[Account]) -> str:
    """Export .ROBLOSECURITY cookies, one per line."""
    return "\n".join(a.security_token for a in accounts if a.security_token)


def export_account_info(accounts: list[Account]) -> str:
    """Export accounts in readable format:
    Username: x, Password: x, Email: x, Email Password: x (Created at yyyy-mm-dd HH:MM:SS)
    """
    lines = []
    for a in accounts:
        parts = [f"Username: {a.username}", f"Password: {a.password}"]
        if a.email:
            parts.append(f"Email: {a.email}")
        if a.email_password:
            parts.append(f"Email Password: {a.email_password}")
        ts = a.last_use.strftime("%Y-%m-%d %H:%M:%S") if a.last_use else datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        lines.append(", ".join(parts) + f" (Created at {ts})")
    return "\n".join(lines)
