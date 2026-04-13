"""
NopeCHA 驗證碼繞過整合。

Chrome 146+ 限制了 --load-extension 的 MV3 service worker 啟動。
要讓 NopeCHA 正常運作，需要使用已安裝 NopeCHA 的 Chrome profile：
  1. 開啟你常用的 Chrome 瀏覽器
  2. 前往 Chrome Web Store 安裝 NopeCHA
  3. 在帳號生成設定中，指定該 Chrome profile 路徑
     (例如 C:/Users/你的名字/AppData/Local/Google/Chrome/User Data/Default)
"""

from __future__ import annotations

import os
import re
import hashlib

from core.constants import get_resource_path


def validate_api_key(key: str) -> tuple[bool, str]:
    """驗證 NopeCHA API key 格式。"""
    key = key.strip()
    if not key:
        return True, ""  # 空白 = 不使用
    if not re.match(r"^[a-zA-Z0-9_-]+$", key):
        return False, "API key contains invalid characters (only letters, numbers, hyphens, underscores)"
    if len(key) < 10:
        return False, "API key seems too short"
    return True, key


def get_nopecha_extension_path() -> str:
    return get_resource_path("lib/NopeCHA")


def get_nopecha_extension_id() -> str:
    """計算 NopeCHA extension 在 --load-extension 模式下的 extension ID。
    注意：Chrome 146+ 用此方式載入的 extension service worker 不會啟動。
    """
    ext_path = os.path.abspath(get_nopecha_extension_path())
    canon = ext_path.replace("/", os.sep).lower()
    h = hashlib.sha256(canon.encode("utf-8")).digest()
    return "".join(chr(ord("a") + nibble) for byte in h[:16] for nibble in [byte >> 4, byte & 0xF])


def setup_nopecha(tab, api_key: str):
    """在瀏覽器 tab 中設定 NopeCHA API key。
    導航到 nopecha.com/setup，由 content script 把 key 寫入 chrome.storage。
    """
    tab.get(f"https://nopecha.com/setup#{api_key}")
