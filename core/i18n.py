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
        "Anti-AFK": "防掛機",
        "Anti-AFK enabled": "防掛機已啟用",
        "Anti-AFK disabled": "防掛機已停用",
        "Game Charts": "遊戲排行榜",
        "Tools": "工具",
        "Nexus Control": "Nexus 控制",
        "Mixer": "混音台",
        "Theme Editor": "主題編輯器",
        "Settings": "設定",
        "Check for Updates": "檢查更新",
        "Search: ": "搜尋：",
        "Filter accounts...": "篩選帳號...",
        "Group: ": "群組：",
        "All Groups": "所有群組",
        "Launch Group": "啟動群組",
        "No accounts in this group": "此群組沒有帳號",
        "Launching group": "正在啟動群組",
        "Group launched": "群組已啟動",
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
        "Copy Username": "複製使用者名稱",
        "Copy Cookie": "複製 Cookie",
        "Remove": "移除",
        "Set Alias": "設定別名",
        "Set Group": "設定群組",
        "Set Description": "設定描述",
        "Join Private Server": "加入私人伺服器",
        # Set Alias / Group / Description dialogs
        "Alias for": "別名：",
        "Group for": "群組：",
        "Description for": "描述：",

        # VIP toolbar
        "Private Server: ": "私人伺服器：",
        "Paste Roblox private server link...": "貼上 Roblox 私人伺服器連結...",
        "Get Job ID": "取得 Job ID",
        "Invalid private server link": "無效的私人伺服器連結",
        "Resolving job ID...": "正在取得 Job ID...",
        "Failed": "失敗",

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

        # === Mixer Dialog ===
        "FPS Cap:": "FPS 上限：",
        "Apply to All": "套用至全部",
        "Kill All Roblox": "結束所有 Roblox",
        "This will close every running Roblox window. Continue?": "這將關閉所有正在執行的 Roblox 視窗，確定要繼續嗎？",
        "Account": "帳號",
        "PID": "PID",
        "Memory (MB)": "記憶體 (MB)",
        "Volume": "音量",
        "CPU %": "CPU %",
        "Launch": "啟動",
        "Launch Checked": "啟動已勾選",
        "No accounts checked": "沒有勾選任何帳號",
        "A launch is already in progress": "已有啟動作業進行中",
        "Check All Visible": "勾選目前顯示的帳號",
        "Uncheck All": "取消全部勾選",
        "Check Selected": "勾選選取的帳號",
        "Uncheck Selected": "取消勾選選取的帳號",
        "Window size:": "視窗大小：",
        "Window size": "視窗大小",
        "Tile in a grid": "自動排成格狀",
        "Apply on launch": "啟動時套用",
        "Apply to Running Windows": "套用到執行中的視窗",
        "Some windows could not be resized (not created yet, or in fullscreen mode).": "部分視窗無法調整（尚未建立，或處於全螢幕模式）。",
        "Limit": "限制",
        "Limits": "資源限制",
        "CPU limit:": "CPU 上限：",
        "RAM limit:": "RAM 上限：",
        "Off": "不限制",
        "Cleared": "已清除",
        "Apply to Selected": "套用到選取",
        "Apply to All Instances": "套用到全部",
        "Clear Limits": "清除限制",
        "Select at least one instance first": "請先選取至少一個實例",
        "No running Roblox instances": "沒有執行中的 Roblox",
        "Auto-apply these limits to newly launched instances": "新啟動的實例自動套用這組限制",
        "CPU limit is a percentage of the whole machine (all cores). RAM limit caps the working set (min 256 MB); Roblox pages out instead of crashing, but may stutter.":
            "CPU 上限是佔整台電腦（所有核心）的百分比。RAM 上限是限制工作集（最小 256 MB）：Roblox 會把多的頁面換出到分頁檔而不會崩潰，但可能變卡。",
        "Killed": "已結束",
        "process(es)": "個進程",

        # === Game Charts Dialog ===
        "Game": "遊戲",
        "Playing": "遊玩中",
        "Refresh": "重新整理",
        "Use Selected": "使用選取",
        "Loading charts...": "載入排行榜中...",
        "Failed to load charts": "載入排行榜失敗",
        "Unknown": "未知",

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
        "Copied username for": "已複製使用者名稱：",
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
