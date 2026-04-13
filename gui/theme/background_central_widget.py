"""
BackgroundCentralWidget — 作為主視窗的 central widget，
在自身的 paintEvent 繪製背景（圖片 / GIF / 影片），
使子 widget 的 rgba QSS 背景能真正透出底層內容。
"""

from __future__ import annotations

import os

from PySide6.QtCore import Qt
from PySide6.QtGui import QPainter, QPixmap, QPaintEvent
from PySide6.QtStyle import QStyleOption
from PySide6.QtWidgets import QWidget, QStyle

IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".bmp", ".webp"}
GIF_EXTS   = {".gif"}
VIDEO_EXTS = {".mp4", ".avi", ".mov", ".mkv", ".wmv"}


class BackgroundCentralWidget(QWidget):
    """
    Central widget 本體，負責在 paintEvent 繪製背景媒體。
    子 widget（表格、按鈕…）使用 rgba QSS 時，
    Qt 的合成系統會自動讓它們透出此處繪製的背景。
    """

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        # 允許 QSS 繪製此 widget 自身的背景色（用於無背景時的主題色）
        self.setAttribute(Qt.WA_StyledBackground, True)
        self._opacity: float = 0.6
        self._pixmap:  QPixmap | None = None
        self._movie    = None   # QMovie
        self._player   = None   # QMediaPlayer
        self._video_widget = None  # QVideoWidget

    # ------------------------------------------------------------------ #
    # Public API
    # ------------------------------------------------------------------ #

    def set_source(self, path: str, opacity: float = 0.6):
        self._opacity = max(0.0, min(1.0, opacity))
        self._clear_media()
        if not path or not os.path.isfile(path):
            self.update()
            return
        ext = os.path.splitext(path)[1].lower()
        if ext in GIF_EXTS:
            self._setup_gif(path)
        elif ext in VIDEO_EXTS:
            self._setup_video(path)
        elif ext in IMAGE_EXTS:
            self._setup_image(path)
        self.update()

    def clear(self):
        self._clear_media()
        self.update()

    # ------------------------------------------------------------------ #
    # Internal
    # ------------------------------------------------------------------ #

    def _clear_media(self):
        if self._movie:
            self._movie.stop()
            self._movie = None
        if self._player:
            self._player.stop()
            self._player = None
        if self._video_widget:
            self._video_widget.hide()
            self._video_widget.deleteLater()
            self._video_widget = None
        self._pixmap = None

    def _setup_image(self, path: str):
        self._pixmap = QPixmap(path)

    def _setup_gif(self, path: str):
        from PySide6.QtGui import QMovie
        movie = QMovie(path)
        movie.frameChanged.connect(lambda _: self.update())
        movie.start()
        self._movie = movie

    def _setup_video(self, path: str):
        try:
            from PySide6.QtMultimedia import QMediaPlayer, QAudioOutput
            from PySide6.QtMultimediaWidgets import QVideoWidget
            from PySide6.QtCore import QUrl
            from PySide6.QtWidgets import QGraphicsOpacityEffect

            vw = QVideoWidget(self)
            vw.setGeometry(self.rect())
            vw.lower()

            eff = QGraphicsOpacityEffect(vw)
            eff.setOpacity(self._opacity)
            vw.setGraphicsEffect(eff)
            vw.show()
            self._video_widget = vw

            audio = QAudioOutput(self)
            audio.setVolume(0)

            player = QMediaPlayer(self)
            player.setAudioOutput(audio)
            player.setVideoOutput(vw)
            player.setSource(QUrl.fromLocalFile(os.path.abspath(path)))
            player.mediaStatusChanged.connect(
                lambda s: player.play()
                if s == QMediaPlayer.MediaStatus.EndOfMedia else None
            )
            player.play()
            self._player = player
        except (ImportError, Exception):
            pass

    # ------------------------------------------------------------------ #
    # Qt events
    # ------------------------------------------------------------------ #

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if self._video_widget:
            self._video_widget.setGeometry(self.rect())
            self._video_widget.lower()

    def paintEvent(self, event: QPaintEvent):
        painter = QPainter(self)

        # 1. 先讓 QSS 繪製主題背景色（無背景圖時的 fallback）
        opt = QStyleOption()
        opt.initFrom(self)
        self.style().drawPrimitive(QStyle.PrimitiveElement.PE_Widget, opt, painter, self)

        # 2. 疊加背景媒體
        if self._video_widget:
            # 影片由 QVideoWidget 自行渲染
            return

        pixmap: QPixmap | None = None
        if self._movie:
            pixmap = self._movie.currentPixmap()
        elif self._pixmap:
            pixmap = self._pixmap

        if not pixmap or pixmap.isNull():
            return

        painter.setOpacity(self._opacity)
        rect = self.rect()
        scaled = pixmap.scaled(
            rect.size(),
            Qt.KeepAspectRatioByExpanding,
            Qt.SmoothTransformation,
        )
        x = (rect.width()  - scaled.width())  // 2
        y = (rect.height() - scaled.height()) // 2
        painter.drawPixmap(x, y, scaled)
