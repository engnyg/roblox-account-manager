"""
BackgroundCentralWidget — 作為主視窗的 central widget，
在自身的 paintEvent 繪製背景（圖片 / GIF / 影片），
使子 widget 的 rgba QSS 背景能真正透出底層內容。
"""

from __future__ import annotations

import os

from PySide6.QtCore import Qt
from PySide6.QtGui import QPainter, QPixmap, QPaintEvent
from PySide6.QtWidgets import QWidget

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
        self._video_sink = None  # QVideoSink（取代 QVideoWidget，避免原生視窗覆蓋子控件）

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
        if self._video_sink:
            self._video_sink = None
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
            from PySide6.QtMultimedia import QMediaPlayer, QAudioOutput, QVideoSink, QVideoFrame
            from PySide6.QtCore import QUrl

            audio = QAudioOutput(self)
            audio.setVolume(0)

            sink = QVideoSink(self)
            sink.videoFrameChanged.connect(self._on_video_frame)
            self._video_sink = sink

            player = QMediaPlayer(self)
            player.setAudioOutput(audio)
            player.setVideoOutput(sink)
            player.setSource(QUrl.fromLocalFile(os.path.abspath(path)))
            player.mediaStatusChanged.connect(
                lambda s: player.play()
                if s == QMediaPlayer.MediaStatus.EndOfMedia else None
            )
            player.play()
            self._player = player
        except (ImportError, Exception):
            pass

    def _on_video_frame(self, frame):
        try:
            img = frame.toImage()
            if not img.isNull():
                self._pixmap = QPixmap.fromImage(img)
                self.update()
        except Exception:
            pass

    # ------------------------------------------------------------------ #
    # Qt events
    # ------------------------------------------------------------------ #

    def resizeEvent(self, event):
        super().resizeEvent(event)

    def paintEvent(self, event: QPaintEvent):
        pixmap: QPixmap | None = None
        if self._movie:
            pixmap = self._movie.currentPixmap()
        elif self._pixmap:
            pixmap = self._pixmap

        if not pixmap or pixmap.isNull():
            # 無背景媒體：正常 QSS 繪製
            super().paintEvent(event)
            return

        # 有背景圖／GIF：
        # 不呼叫 super()，避免 QSS 實色底蓋住圖片。
        # 子 widget 的 rgba QSS 背景會直接疊在這裡畫的圖片上。
        from PySide6.QtGui import QColor
        painter = QPainter(self)

        # 深色底（避免視窗邊緣殘影）
        painter.fillRect(self.rect(), QColor(10, 10, 20))

        # 背景圖，等比例 Cover
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
