from __future__ import annotations

from PySide6.QtGui import QColor

from kenpintool.domain.models import NgLevel


def color_for_level(level: NgLevel) -> QColor:
    if level == NgLevel.NG_A:
        return QColor(220, 50, 50)
    if level == NgLevel.NG_B:
        return QColor(240, 140, 0)
    return QColor(0, 120, 215)
