"""
DrissionPage 瀏覽器自動化工具。
移植自 lib/lib.py downloadUngoogledChromium() / returnUngoogledChromiumPath()。
"""

from __future__ import annotations

import os
import platform
import shutil

import httpx
from DrissionPage import SessionPage

from core.constants import get_resource_path
from loguru import logger


def _get_available_versions() -> list[str]:
    system = platform.system()
    if system != "Windows":
        return []
    try:
        page = SessionPage()
        page.get("https://ungoogled-software.github.io/ungoogled-chromium-binaries/releases/windows/64bit/")
        raw = [x.ele("@tag()=a").text for x in page.eles("@tag()=li") if x.ele("@tag()=a")]
        return [
            v for v in raw
            if v and "." in v and v.split(".")[0].isdigit() and int(v.split(".")[0]) <= 136
        ]
    except Exception as e:
        logger.warning(f"Could not fetch Ungoogled Chromium versions: {e}")
        return []


def get_ungoogled_chromium_path() -> str | None:
    """回傳已安裝的 Ungoogled Chromium 路徑（或 None）。"""
    if platform.system() != "Windows":
        return None
    versions = _get_available_versions()
    if not versions:
        return None
    path = get_resource_path(f"lib/ungoogled-chromium_{versions[0]}.1_windows_x64")
    return path if os.path.isdir(path) else None


def download_ungoogled_chromium(prompt_callback=None) -> str:
    """
    下載並解壓 Ungoogled Chromium。
    prompt_callback(msg) -> bool: 向使用者詢問是否繼續（True=繼續）。
    """
    system = platform.system()
    if system != "Windows":
        return f"{system} not supported for automated install"

    versions = _get_available_versions()
    if not versions:
        return "No compatible versions found"

    version = versions[0]
    base_dir = get_resource_path(f"lib/ungoogled-chromium_{version}.1_windows_x64")

    if os.path.isdir(base_dir):
        return "already_installed"

    if prompt_callback:
        if not prompt_callback(f"Would you like to install Ungoogled Chromium {version}?"):
            return "cancelled"

    zip_path = base_dir + ".zip"
    url = f"https://github.com/ungoogled-software/ungoogled-chromium-windows/releases/download/{version}.1/ungoogled-chromium_{version}.1_windows_x64.zip"

    if not os.path.exists(zip_path):
        logger.info(f"Downloading Ungoogled Chromium {version}...")
        try:
            with httpx.stream("GET", url, follow_redirects=True, timeout=300) as r:
                r.raise_for_status()
                with open(zip_path, "wb") as f:
                    for chunk in r.iter_bytes(chunk_size=65536):
                        f.write(chunk)
        except Exception as e:
            return f"Download failed: {e}"

    # Extract
    try:
        from zipfile import ZipFile
        temp_dir = base_dir + "_temp"
        with ZipFile(zip_path, "r") as z:
            z.extractall(temp_dir)

        items = os.listdir(temp_dir)
        if len(items) == 1 and os.path.isdir(os.path.join(temp_dir, items[0])):
            shutil.move(os.path.join(temp_dir, items[0]), base_dir)
            os.rmdir(temp_dir)
        else:
            os.makedirs(base_dir, exist_ok=True)
            for item in items:
                shutil.move(os.path.join(temp_dir, item), os.path.join(base_dir, item))
            os.rmdir(temp_dir)

        os.remove(zip_path)
        logger.success("Ungoogled Chromium installed")
        return "success"
    except Exception as e:
        try:
            os.remove(zip_path)
        except Exception:
            pass
        return f"Extraction failed: {e}"
