"""
多語言支援模組。
支援語言：English (en) / 繁體中文 (zh)
"""

from __future__ import annotations

from PySide6.QtCore import QObject, Signal

TRANSLATIONS: dict[str, dict[str, str]] = {
    "en": {},  # English is the key itself, no lookup needed
    "zh": {
        # === Main Window ===
        "Roblox Account Manager V2": "Roblox 帳號管理器 V2",
        "File": "檔案",
        "Import Accounts": "匯入帳號",
        "Export Cookies": "匯出 Cookies",
        "Export Account Info": "匯出帳號資訊",
        "Exit": "離開",
        "Accounts": "帳號",
        "Generate Accounts": "產生帳號",
        "Manual Login": "手動登入",
        "Manual Login: browser opening...": "手動登入：正在開啟瀏覽器...",
        "Account Tools": "帳號工具",
        "Custom Fields": "自訂欄位",
        "Refresh Cookies": "刷新 Cookies",
        "Game": "遊戲",
        "Join Server": "加入伺服器",
        "Server List": "伺服器列表",
        "Find Player": "尋找玩家",
        "Multi-Roblox": "多開 Roblox",
        "FPS Unlocker": "FPS 解鎖",
        "Tools": "工具",
        "Nexus Control": "Nexus 控制",
        "Theme Editor": "主題編輯器",
        "Settings": "設定",
        "Check for Updates": "檢查更新",
        "Search: ": "搜尋：",
        "Filter accounts...": "篩選帳號...",
        "Place ID: ": "Place ID：",
        "Place ID": "Place ID",
        "Job ID (optional)": "Job ID（可選）",
        "Job ID: ": "Job ID：",
        "Join": "加入",
        "Ready": "就緒",
        "accounts": "個帳號",
        "No account selected": "未選擇帳號",
        "Enter a valid Place ID": "請輸入有效的 Place ID",
        "Enter a Place ID first": "請先輸入 Place ID",
        "Multi-Roblox enabled": "多開 Roblox 已啟用",
        "Multi-Roblox failed": "多開 Roblox 失敗",
        "FPS cap updated": "FPS 已更新",
        "FPS update failed": "FPS 更新失敗",
        "Cookies refreshed": "Cookies 已刷新",

        # Context menu
        "Open Browser": "開啟瀏覽器",
        "Copy Cookie": "複製 Cookie",
        "Remove": "移除",

        # === Account Table ===
        "Username": "使用者名稱",
        "Alias": "別名",
        "Group": "群組",
        "Description": "描述",
        "Last Use": "上次使用",
        "Valid": "有效",

        # === Generator Dialog ===
        "Account Generator": "帳號產生器",
        "Generation Settings": "產生設定",
        "Password:": "密碼：",
        "Leave blank for random password": "留空使用隨機密碼（每個帳號獨立）",
        "Name prefix:": "名稱前綴：",
        "Leave blank for random username": "留空使用隨機使用者名稱",
        "Count:": "數量：",
        "Follow users:": "追蹤使用者：",
        "username1, username2 (optional)": "username1, username2（可選）",
        "Proxies:": "代理伺服器：",
        "http://ip:port, ... (optional)": "http://ip:port, ...（可選）",
        "Captcha key:": "驗證碼金鑰：",
        "NopeCHA API key (optional)": "NopeCHA API 金鑰（可選）",
        "Chrome profile with NopeCHA installed (optional)": "已安裝 NopeCHA 的 Chrome profile 路徑（可選）",
        "Chrome profile:": "Chrome profile：",
        "Options": "選項",
        "Email verification": "郵件驗證",
        "Avatar customization": "頭像隨機化",
        "Scrambled username (faster)": "亂碼使用者名稱（較快）",
        "Incognito mode": "無痕模式",
        "Ready": "就緒",
        "Start": "開始",
        "Close": "關閉",
        "Starting generation...": "開始產生帳號...",
        "Password cannot be empty.": "密碼不可為空。",

        # === Settings Dialog ===
        "Settings": "設定",
        "General": "一般",
        "Theme:": "主題：",
        "Join delay (s):": "加入延遲（秒）：",
        "Auto cookie refresh:": "自動刷新 Cookie：",
        "Auto close last process:": "自動關閉上一個進程：",
        "Show presence:": "顯示在線狀態：",
        "Unlock FPS:": "解鎖 FPS：",
        "Max FPS:": "最大 FPS：",
        "Language:": "語言：",
        "Web API": "Web API",
        "Enable Web API:": "啟用 Web API：",
        "Password:": "密碼：",
        "Port:": "埠號：",
        "Nexus": "Nexus",
        "Enable Nexus:": "啟用 Nexus：",

        # === Import Dialog ===
        "Import Accounts": "匯入帳號",
        "Import Mode": "匯入模式",
        "Cookie strings (.ROBLOSECURITY, one per line)": "Cookie 字串（.ROBLOSECURITY，每行一個）",
        "cookies.json file": "cookies.json 檔案",
        "Paste cookie strings here (one per line)...": "在此貼上 Cookie 字串（每行一個）...",
        "No file selected": "未選擇檔案",
        "Browse...": "瀏覽...",
        "Select cookies.json": "選擇 cookies.json",
        "JSON files (*.json);;All files (*)": "JSON 檔案 (*.json);;所有檔案 (*)",

        # === Server List Dialog ===
        "Server List": "伺服器列表",
        "Place ID:": "Place ID：",
        "e.g. 4924922222": "例如 4924922222",
        "Fetch Servers": "取得伺服器",
        "Job ID": "Job ID",
        "Players": "玩家數",
        "Max": "上限",
        "Ping": "延遲",
        "Join Selected": "加入選取",

        # === Account Utils Dialog ===
        "Account Tools": "帳號工具",
        "Change Password": "更改密碼",
        "Current:": "目前密碼：",
        "New:": "新密碼：",
        "Change Email": "更改電子郵件",
        "New email:": "新電子郵件：",
        "Other": "其他",
        "Log out of other sessions": "登出其他 Session",
        "Username to block/unblock": "封鎖/解封的使用者名稱",
        "Toggle Block": "切換封鎖",

        # === Account Fields Dialog ===
        "Fields": "自訂欄位",
        "Field Name": "欄位名稱",
        "Value": "值",
        "Add Field": "新增欄位",
        "Delete Selected": "刪除選取",
        "Save": "儲存",
        "Add Field": "新增欄位",

        # === Nexus Dialog ===
        "Nexus Account Control": "Nexus 帳號控制",
        "Connected": "已連線",
        "Script:": "腳本：",
        "Lua script to execute on selected account...": "在選取帳號上執行的 Lua 腳本...",
        "Execute": "執行",

        # === Theme Editor Dialog ===
        "Theme Editor": "主題編輯器",
        "Theme:": "主題：",
        "Preview": "預覽",
        "Save as Custom": "另存自訂",
        "Save Theme": "儲存主題",
        "Theme name:": "主題名稱：",
        "Save as New Theme": "另存新主題",
        "Delete": "刪除",
        "Color Settings": "顏色設定",
        "Background (Image / GIF / MP4)": "背景（圖片 / GIF / MP4）",
        "File:": "檔案：",
        "Leave blank = no background": "留空 = 無背景",
        "Browse...": "瀏覽…",
        "Opacity:": "不透明度：",
        "Theme name:": "主題名稱：",
        "is a built-in theme, please choose another name.": "是內建主題，請換一個名稱。",
        "Saved": "已儲存",
        "Theme \"%s\" saved and applied.": "主題「%s」已儲存並套用。",
        "Confirm Delete": "確認刪除",
        "Delete theme \"%s\"?": "確定要刪除主題「%s」嗎？",
        "Pick Color — %s": "選擇顏色 — %s",
        # Color token labels
        "Window Background": "視窗背景",
        "Panel Background": "面板背景",
        "Input Background": "輸入框背景",
        "Button Background": "按鈕背景",
        "Button Hover": "按鈕懸停",
        "Button Pressed": "按鈕按下",
        "Selection Background": "選取背景",
        "Menu Bar Background": "選單欄背景",
        "Status Bar Background": "狀態欄背景",
        "Primary Text": "主要文字",
        "Secondary Text": "次要文字",
        "Accent Color": "強調色",
        "Disabled Text": "停用文字",
        "Border": "邊框",
        "Focus Border": "焦點邊框",

        # === Updater Dialog ===
        "Update Checker": "更新檢查",
        "Could not check for updates.": "無法檢查更新。",
        "You are up to date.": "您已是最新版本。",

        # === Find Player ===
        "Find Player": "尋找玩家",
        "Target username:": "目標使用者名稱：",
        "not found in place": "不在 place",

        # === Messages ===
        "Imported": "已匯入",
        "accounts": "個帳號",
        "Exported to": "已匯出至",
        "Removed": "已移除",
        "Copied cookie for": "已複製 Cookie：",
        "Loaded": "已載入",
        "in job": "在伺服器",
        "not found in place": "不在 place 中",
        "Blocked": "已封鎖",
        "Unblocked": "已解封",
        "Failed to block": "封鎖失敗",
        "Failed to unblock": "解封失敗",
    },
}

LANGUAGE_NAMES = {
    "en": "English",
    "zh": "繁體中文",
}


class I18nManager(QObject):
    language_changed = Signal(str)

    def __init__(self):
        super().__init__()
        self._lang = "en"

    def set_language(self, lang: str):
        if lang in TRANSLATIONS and lang != self._lang:
            self._lang = lang
            self.language_changed.emit(lang)

    def current_language(self) -> str:
        return self._lang

    def tr(self, key: str) -> str:
        if self._lang == "en":
            return key
        return TRANSLATIONS[self._lang].get(key, key)


_manager: I18nManager | None = None


def get_i18n() -> I18nManager:
    global _manager
    if _manager is None:
        _manager = I18nManager()
    return _manager


def tr(key: str) -> str:
    """全域翻譯函式。"""
    return get_i18n().tr(key)


def init_language(lang: str):
    """從設定初始化語言。"""
    get_i18n()._lang = lang if lang in TRANSLATIONS else "en"
