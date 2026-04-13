"""
roblox-acc-genV2 — 應用程式入口點
"""

import asyncio
import sys
import os

# 確保工作目錄為專案根目錄（PyInstaller 支援）
if getattr(sys, "frozen", False):
    os.chdir(os.path.dirname(sys.executable))
else:
    os.chdir(os.path.dirname(os.path.abspath(__file__)))

# 確保 data/ 目錄存在
os.makedirs("data", exist_ok=True)

from loguru import logger
logger.add("data/roblox_acc.log", rotation="10 MB", retention="7 days", level="DEBUG")

from PySide6.QtWidgets import QApplication
import qasync

from core.settings import get_settings
from core.i18n import init_language
from gui.main_window import MainWindow


def main():
    app = QApplication(sys.argv)
    app.setApplicationName("Roblox Account Manager V2")

    # 從設定讀取語言並初始化
    settings = get_settings()
    lang = settings.get("General", "Language", "en")
    init_language(lang)

    loop = qasync.QEventLoop(app)
    asyncio.set_event_loop(loop)

    window = MainWindow()
    window.show()

    with loop:
        loop.run_forever()


if __name__ == "__main__":
    main()
