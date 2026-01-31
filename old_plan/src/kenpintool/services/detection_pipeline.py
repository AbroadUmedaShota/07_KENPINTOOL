from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable

from kenpintool.domain.models import DecisionAction, Detection, EvidenceRegion, NgLevel, PageItem
from kenpintool.services.dummy_detection import DummyDetectionService
from kenpintool.services.quality_detection import QualityDetectionService
from kenpintool.services.structure_detection import StructureDetectionService


@dataclass
class DetectionResult:
    page_index: int
    detections: list[Detection]


class DetectionPipeline:
    def __init__(self) -> None:
        self._dummy = DummyDetectionService()
        self._quality = QualityDetectionService()
        self._structure = StructureDetectionService()
        self._page_hashes: list[tuple[int, list[int]]] = []
        self._duplicate_threshold = 0.95

    def analyze_pages(self, pages: Iterable[PageItem]) -> Iterable[DetectionResult]:
        for page in pages:
            detections = self._analyze_page(page)
            yield DetectionResult(page.index, detections)

    def _analyze_page(self, page: PageItem) -> list[Detection]:
        detections: list[Detection] = []

        detections.extend(self._dummy.detect_from_filename(page.file_path))
        detections.extend(self._quality.detect_qlt_05(page.file_path, page.pdf_page_index))

        current_hash = self._structure.compute_hash(page.file_path, page.pdf_page_index)
        if current_hash:
            match_index = 0
            match_similarity = 0.0
            for page_index, existing_hash in self._page_hashes:
                similarity = self._structure.compute_similarity(current_hash, existing_hash)
                if similarity >= self._duplicate_threshold and similarity > match_similarity:
                    match_similarity = similarity
                    match_index = page_index
            self._page_hashes.append((page.index, current_hash))
            if match_index > 0:
                detections.append(
                    Detection(
                        code="STR-02",
                        level=NgLevel.NG_A,
                        message=f"ページ{match_index:03d}と重複",
                        suggested_action=DecisionAction.RESCAN,
                        evidence=[EvidenceRegion(0.0, 0.0, 1.0, 1.0)],
                    )
                )

        return detections
