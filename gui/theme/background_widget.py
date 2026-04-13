"""
背景媒體層 — 支援靜態圖片、GIF 動圖、MP4 影片。
"""

from __future__ import annotations

import os

from PySide6.QtCore import Qt
from PySide6.QtGui import QPainter, QPixmap, QPaintEvent
from PySide6.QtWidgets import QWidget

IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".bmp", ".webp"}
GIF_EXTS   = {".gif"}
VIDEO_EXTS = {".mp4", ".avi", ".mov", ".mkv", ".wmv"}


class BackgroundWidget(QWidget):
    """透明滑鼠事件、置於最底層的背景渲染 Widget。"""

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.setAttribute(Qt.WA_TransparentForMouseEvents)
        self.setAttribute(Qt.WA_NoSystemBackground)
        self._opacity: float = 1.0
        self._pixmap:  QPixmap | None = None
        self._movie    = None   # QMovie
        self._player   = None   # QMediaPlayer
        self._video_widget = None  # QVideoWidget

    # ------------------------------------------------------------------ #
    # Public API
    # ------------------------------------------------------------------ #

    def set_source(self, path: str, opacity: float = 0.5):
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
    # Internal setup
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

            vw = QVideoWidget(self)
            vw.setGeometry(self.rect())
            vw.lower()
            # 讓影片透明度生效：把 video widget 放在繪圖層下面，
            # 改用 opacity effect 控制整體透明度
            from PySide6.QtWidgets import QGraphicsOpacityEffect
            effect = QGraphicsOpacityEffect(vw)
            effect.setOpacity(self._opacity)
            vw.setGraphicsEffect(effect)
            vw.show()
            self._video_widget = vw

            audio = QAudioOutput(self)
            audio.setVolume(0)  # 背景靜音

            player = QMediaPlayer(self)
            player.setAudioOutput(audio)
            player.setVideoOutput(vw)
            player.setSource(QUrl.fromLocalFile(os.path.abspath(path)))
            # 結束後循環
            player.mediaStatusChanged.connect(
                lambda status: player.play()
                if status == QMediaPlayer.MediaStatus.EndOfMedia else None
            )
            player.play()
            self._player = player
        except (ImportError, Exception):
            # Multimedia 模組不可用時靜默略過
            pass

    # ------------------------------------------------------------------ #
    # Qt events
    # ------------------------------------------------------------------ #

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if self._video_widget:
            self._video_widget.setGeometry(self.rect())

    def paintEvent(self, event: QPaintEvent):
        # 影片由 QVideoWidget 自行渲染，不需要 paintEvent
        if self._video_widget:
            return

        pixmap: QPixmap | None = None
        if self._movie:
            pixmap = self._movie.currentPixmap()
        elif self._pixmap:
            pixmap = self._pixmap

        if not pixmap or pixmap.isNull():
            return

        painter = QPainter(self)
        painter.setOpacity(self._opacity)
        rect = self.rect()
        # 等比例縮放填滿（cover）
        scaled = pixmap.scaled(
            rect.size(),
            Qt.KeepAspectRatioByExpanding,
            Qt.SmoothTransformation,
        )
        x = (rect.width()  - scaled.width())  // 2
        y = (rect.height() - scaled.height()) // 2
        painter.drawPixmap(x, y, scaled)
