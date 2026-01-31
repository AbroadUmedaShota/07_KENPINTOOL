from __future__ import annotations

from typing import Iterable

import numpy as np

from kenpintool.domain.models import DecisionAction, Detection, EvidenceRegion, NgLevel
from kenpintool.services.image_decode import load_cv2_image


class QualityDetectionService:
    def __init__(self) -> None:
        self._min_line_length_ratio = 0.8
        self._max_line_gap = 5

    def detect_qlt_05(self, file_path: str, pdf_page_index: int | None) -> list[Detection]:
        image = load_cv2_image(file_path, pdf_page_index)
        if image is None:
            return []
        try:
            import cv2
        except Exception:
            return []

        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        edges = cv2.Canny(gray, 50, 150, apertureSize=3)
        min_length = int(min(gray.shape[0], gray.shape[1]) * self._min_line_length_ratio)
        lines = cv2.HoughLinesP(
            edges,
            1,
            np.pi / 180,
            threshold=150,
            minLineLength=min_length,
            maxLineGap=self._max_line_gap,
        )

        if lines is None:
            return []

        detections: list[Detection] = []
        if len(lines) > 0:
            detections.append(
                Detection(
                    code="QLT-05",
                    level=NgLevel.NG_A,
                    message="線状ノイズの疑い",
                    is_qlt_05=True,
                    suggested_action=DecisionAction.RESCAN,
                    evidence=[EvidenceRegion(0.0, 0.0, 1.0, 1.0)],
                )
            )
        return detections
