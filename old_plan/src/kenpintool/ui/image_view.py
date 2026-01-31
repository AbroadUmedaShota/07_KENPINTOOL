from __future__ import annotations

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QBrush, QImage, QPen, QPixmap, QTransform
from PySide6.QtWidgets import (
    QGraphicsPixmapItem,
    QGraphicsRectItem,
    QGraphicsScene,
    QGraphicsTextItem,
    QGraphicsView,
)

from kenpintool.domain.models import Detection
from kenpintool.ui.overlay_palette import color_for_level


class ImageView(QGraphicsView):
    view_changed = Signal()

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self._scene = QGraphicsScene(self)
        self.setScene(self._scene)
        self._pixmap_item = QGraphicsPixmapItem()
        self._scene.addItem(self._pixmap_item)
        self._overlay_items: list[QGraphicsRectItem] = []
        self._warning_rect: QGraphicsRectItem | None = None
        self._warning_text: QGraphicsTextItem | None = None
        self.setDragMode(QGraphicsView.DragMode.ScrollHandDrag)
        self.setRenderHints(self.renderHints())
        self._fit_on_next = True
        self._current_image_size: tuple[int, int] | None = None

    def set_image(self, image: QImage | None) -> None:
        if image is None or image.isNull():
            self._pixmap_item.setPixmap(QPixmap())
            self._current_image_size = None
            self._clear_warning()
            self.view_changed.emit()
            return
        self._pixmap_item.setPixmap(QPixmap.fromImage(image))
        self._scene.setSceneRect(self._pixmap_item.boundingRect())
        self._current_image_size = (image.width(), image.height())
        if self._fit_on_next:
            self.fitInView(self._pixmap_item, Qt.AspectRatioMode.KeepAspectRatio)
            self._fit_on_next = False
        self._update_warning_geometry()
        self.view_changed.emit()

    def set_overlays(self, detections: list[Detection]) -> None:
        for item in self._overlay_items:
            self._scene.removeItem(item)
        self._overlay_items.clear()
        if not self._current_image_size:
            return
        width, height = self._current_image_size
        for detection in detections:
            color = color_for_level(detection.level)
            pen = QPen(color)
            pen.setWidth(2)
            brush = QBrush(Qt.BrushStyle.NoBrush)
            for ev in detection.evidence:
                rect = QGraphicsRectItem(
                    ev.x * width,
                    ev.y * height,
                    ev.width * width,
                    ev.height * height,
                )
                rect.setPen(pen)
                rect.setBrush(brush)
                self._scene.addItem(rect)
                self._overlay_items.append(rect)

    def set_warning(self, text: str | None) -> None:
        if not text:
            self._clear_warning()
            return
        if self._warning_rect is None:
            self._warning_rect = QGraphicsRectItem()
            self._warning_rect.setBrush(QBrush(Qt.GlobalColor.red))
            self._warning_rect.setPen(QPen(Qt.GlobalColor.red))
            self._warning_rect.setOpacity(0.6)
            self._scene.addItem(self._warning_rect)
        if self._warning_text is None:
            self._warning_text = QGraphicsTextItem()
            self._warning_text.setDefaultTextColor(Qt.GlobalColor.white)
            font = self._warning_text.font()
            font.setPointSize(16)
            font.setBold(True)
            self._warning_text.setFont(font)
            self._scene.addItem(self._warning_text)
        self._warning_text.setPlainText(text)
        self._update_warning_geometry()

    def _update_warning_geometry(self) -> None:
        if not self._warning_rect or not self._warning_text:
            return
        if not self._current_image_size:
            return
        width, height = self._current_image_size
        banner_height = max(40, int(height * 0.12))
        self._warning_rect.setRect(0, 0, width, banner_height)
        self._warning_text.setPos(10, 5)

    def _clear_warning(self) -> None:
        if self._warning_rect:
            self._scene.removeItem(self._warning_rect)
            self._warning_rect = None
        if self._warning_text:
            self._scene.removeItem(self._warning_text)
            self._warning_text = None

    def wheelEvent(self, event) -> None:
        factor = 1.1 if event.angleDelta().y() > 0 else 1 / 1.1
        self.scale(factor, factor)
        self.view_changed.emit()

    def fit_to_view(self) -> None:
        self._fit_on_next = True
        pixmap = self._pixmap_item.pixmap()
        if not pixmap.isNull():
            self.fitInView(self._pixmap_item, Qt.AspectRatioMode.KeepAspectRatio)
        self.view_changed.emit()

    def scrollContentsBy(self, dx: int, dy: int) -> None:
        super().scrollContentsBy(dx, dy)
        self.view_changed.emit()

    def get_view_state(self) -> tuple[QTransform, object]:
        return self.transform(), self.mapToScene(self.viewport().rect()).boundingRect().center()

    def set_view_state(self, transform: QTransform, center) -> None:
        self.setTransform(transform)
        self.centerOn(center)

    def has_image(self) -> bool:
        return self._current_image_size is not None

    def get_view_context(self) -> dict:
        if not self._current_image_size:
            return {"zoom": 1.0, "view_box": None}
        width, height = self._current_image_size
        rect = self.mapToScene(self.viewport().rect()).boundingRect()
        view_box = {
            "x": max(0.0, rect.x() / width),
            "y": max(0.0, rect.y() / height),
            "width": min(1.0, rect.width() / width),
            "height": min(1.0, rect.height() / height),
        }
        zoom = self.transform().m11()
        return {"zoom": zoom, "view_box": view_box}
