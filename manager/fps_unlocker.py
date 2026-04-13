"""
FPS 解鎖器。
透過修改 Roblox ClientAppSettings.json 來解鎖 FPS。
對應 C# Classes/ClientSettingsPatcher.cs。
"""

from __future__ import annotations

import json
import os
import sys

from loguru import logger


def _get_settings_path() -> str | None:
    if sys.platform != "win32":
        return None
    local_app = os.environ.get("LocalAppData", "")
    versions_dir = os.path.join(local_app, "Roblox", "Versions")
    if not os.path.isdir(versions_dir):
        return None
    for ver in sorted(os.listdir(versions_dir), reverse=True):
        path = os.path.join(versions_dir, ver, "ClientSettings", "ClientAppSettings.json")
        if os.path.exists(path):
            return path
    # Create in latest version
    for ver in sorted(os.listdir(versions_dir), reverse=True):
        base = os.path.join(versions_dir, ver, "ClientSettings")
        os.makedirs(base, exist_ok=True)
        return os.path.join(base, "ClientAppSettings.json")
    return None


def set_fps_cap(fps: int) -> bool:
    """設定 FPS 上限（0 = 無限制）。"""
    path = _get_settings_path()
    if not path:
        logger.warning("Could not find Roblox ClientAppSettings.json")
        return False
    try:
        try:
            with open(path, "r", encoding="utf-8") as f:
                settings = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            settings = {}

        if fps <= 0 or fps >= 10000:
            settings.pop("DFIntTaskSchedulerTargetFps", None)
        else:
            settings["DFIntTaskSchedulerTargetFps"] = fps

        with open(path, "w", encoding="utf-8") as f:
            json.dump(settings, f, indent=2)

        logger.info(f"FPS cap set to {fps}")
        return True
    except Exception as e:
        logger.error(f"fps_unlocker: {e}")
        return False


def unlock_fps(max_fps: int = 0) -> bool:
    """解鎖 FPS（預設無上限）。"""
    return set_fps_cap(max_fps)
