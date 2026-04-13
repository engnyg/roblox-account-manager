"""
帳號工具操作。
對應 C# Forms/AccountUtils.cs。
"""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from core.account import Account


def set_privacy(account: "Account", follow: int, message: int, chat: int) -> dict[str, bool]:
    """批次設定隱私選項。"""
    return {
        "follow": account.set_follow_privacy(follow),
    }


def change_password(account: "Account", current: str, new: str) -> tuple[bool, str]:
    return account.change_password(current, new)


def change_email(account: "Account", password: str, new_email: str) -> tuple[bool, str]:
    return account.change_email(password, new_email)


def logout_other_sessions(account: "Account") -> tuple[bool, str]:
    return account.logout_other_sessions()


def unblock_everyone(account: "Account") -> tuple[bool, str]:
    return account.unblock_everyone() if hasattr(account, "unblock_everyone") else (False, "Not implemented")


def toggle_block(account: "Account", username: str) -> tuple[bool, str]:
    from core.roblox_api import get_user_id
    uid = get_user_id(username)
    if not uid:
        return False, f"User {username} not found"
    ok, blocked_data = account.get_blocked_list()
    if not ok:
        return False, "Could not fetch block list"
    blocked_ids = {u.get("userId") for u in blocked_data.get("blockedUsers", [])}
    if uid in blocked_ids:
        result = account.block_user(uid, unblock=True)
        return result, "Unblocked" if result else "Failed to unblock"
    else:
        result = account.block_user(uid)
        return result, "Blocked" if result else "Failed to block"
