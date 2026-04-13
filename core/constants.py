import os
import sys

APP_NAME = "Roblox Account Manager"
APP_VERSION_FILE = "version.txt"

# Roblox API base URLs
ROBLOX_BASE = "https://www.roblox.com"
AUTH_API = "https://auth.roblox.com"
USERS_API = "https://users.roblox.com"
FRIENDS_API = "https://friends.roblox.com"
AVATAR_API = "https://avatar.roblox.com"
ACCOUNT_API = "https://accountsettings.roblox.com"
GAMES_API = "https://games.roblox.com"
PRESENCE_API = "https://presence.roblox.com"
ECONOMY_API = "https://economy.roblox.com"

# Local server
WEB_API_PORT = 7963
NEXUS_PORT = 7964

# File paths
DATA_DIR = "data"
ACCOUNTS_FILE = os.path.join(DATA_DIR, "accounts.txt")
COOKIES_FILE = os.path.join(DATA_DIR, "cookies.json")
ANALYTICS_FILE = os.path.join(DATA_DIR, "analytics.txt")
ENCRYPTED_ACCOUNTS_FILE = os.path.join(DATA_DIR, "accounts.dat")
SETTINGS_FILE = "config.ini"

# RAM binary header (matches C# RAMHeader: "Roblox Account Manager created by ic3w0lf22 @ github.com .......")
RAM_HEADER = bytes([
    82, 111, 98, 108, 111, 120, 32, 65, 99, 99, 111, 117, 110, 116, 32, 77,
    97, 110, 97, 103, 101, 114, 32, 99, 114, 101, 97, 116, 101, 100, 32, 98,
    121, 32, 105, 99, 51, 119, 48, 108, 102, 50, 50, 32, 64, 32, 103, 105,
    116, 104, 117, 98, 46, 99, 111, 109, 32, 46, 46, 46, 46, 46, 46, 46
])

# Follow privacy options
FOLLOW_PRIVACY = {
    0: "All",
    1: "Followers",
    2: "Following",
    3: "Friends",
    4: "NoOne",
}


def get_resource_path(relative_path: str) -> str:
    """Get absolute path to resource (works for dev and PyInstaller)."""
    try:
        base_path = sys._MEIPASS
    except AttributeError:
        base_path = os.path.abspath(".")
    return os.path.join(base_path, relative_path)


def find_ungoogled_chromium() -> str | None:
    """尋找 Ungoogled Chromium 執行檔。
    依序搜尋：V2 lib/、相鄰舊版資料夾 lib/。
    """
    search_roots = [
        get_resource_path("lib"),
        os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                     "..", "roblox-acc-gen", "Roblox-Account-Gen", "lib"),
    ]
    for root in search_roots:
        root = os.path.normpath(root)
        if not os.path.isdir(root):
            continue
        for entry in os.listdir(root):
            if entry.startswith("ungoogled-chromium") and os.path.isdir(os.path.join(root, entry)):
                exe = os.path.join(root, entry, "chrome.exe")
                if os.path.isfile(exe):
                    return exe
    return None
