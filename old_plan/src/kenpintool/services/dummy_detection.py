from __future__ import annotations

from kenpintool.domain.models import Detection, EvidenceRegion, NgLevel


class DummyDetectionService:
    def detect_from_filename(self, file_path: str) -> list[Detection]:
        name = file_path.lower()
        detections: list[Detection] = []
        if "str-01s" in name:
            detections.append(
                Detection(
                    code="STR-01S",
                    level=NgLevel.NG_C,
                    message="並び抜けの疑い",
                    evidence=[EvidenceRegion(0.0, 0.0, 1.0, 0.1)],
                )
            )
        if "str-03s" in name:
            detections.append(
                Detection(
                    code="STR-03S",
                    level=NgLevel.NG_C,
                    message="並び入替の疑い",
                    evidence=[EvidenceRegion(0.0, 0.9, 1.0, 0.1)],
                )
            )
        return detections
