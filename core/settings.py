import configparser
import os
from core.constants import SETTINGS_FILE


_defaults = {
    "General": {
        "Language": "en",
        "CheckForUpdates": "true",
        "AccountJoinDelay": "8",
        "AsyncJoin": "false",
        "SavePasswords": "true",
        "ServerRegionFormat": "<city>, <countryCode>",
        "MaxRecentGames": "8",
        "ShuffleJobID": "false",
        "ShuffleChoosesLowestServer": "false",
        "ShufflePageCount": "5",
        "IPApiLink": "http://ip-api.com/json/<ip>",
        "AutoCookieRefresh": "true",
        "AutoCloseLastProcess": "true",
        "ShowPresence": "true",
        "PresenceUpdateRate": "5",
        "UnlockFPS": "false",
        "MaxFPSValue": "120",
        "Theme": "Dark",
    },
    "WebServer": {
        "Enabled": "false",
        "Password": "",
        "Port": "7963",
    },
    "AccountControl": {
        "Enabled": "false",
        "Port": "7964",
    },
    "Watcher": {
        "Enabled": "false",
        "VerifyDataModel": "true",
        "IgnoreExistingProcesses": "true",
        "CloseIfMemoryLow": "false",
        "MemoryLowValue": "200",
        "CloseIfWindowTitle": "false",
        "ExpectedWindowTitle": "Roblox",
        "RememberWindowPositions": "false",
    },
    "Generator": {
        "DefaultPassword": "Qing762.chy",
        "EnableAnalytics": "true",
        "CaptchaApiKey": "",
        "UseProxy": "false",
    },
}


class Settings:
    def __init__(self):
        self._config = configparser.ConfigParser()
        self._path = SETTINGS_FILE
        self._load()

    def _load(self):
        for section, values in _defaults.items():
            if not self._config.has_section(section):
                self._config.add_section(section)
            for key, val in values.items():
                if not self._config.has_option(section, key):
                    self._config.set(section, key, val)

        if os.path.exists(self._path):
            self._config.read(self._path, encoding="utf-8")

        self.save()

    def save(self):
        with open(self._path, "w", encoding="utf-8") as f:
            self._config.write(f)

    def get(self, section: str, key: str, fallback: str = "") -> str:
        return self._config.get(section, key, fallback=fallback)

    def get_bool(self, section: str, key: str, fallback: bool = False) -> bool:
        return self._config.getboolean(section, key, fallback=fallback)

    def get_int(self, section: str, key: str, fallback: int = 0) -> int:
        return self._config.getint(section, key, fallback=fallback)

    def get_float(self, section: str, key: str, fallback: float = 0.0) -> float:
        return self._config.getfloat(section, key, fallback=fallback)

    def set(self, section: str, key: str, value: str):
        if not self._config.has_section(section):
            self._config.add_section(section)
        self._config.set(section, key, str(value))
        self.save()


# Singleton
_instance: Settings | None = None


def get_settings() -> Settings:
    global _instance
    if _instance is None:
        _instance = Settings()
    return _instance
