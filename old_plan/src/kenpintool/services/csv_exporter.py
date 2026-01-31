from __future__ import annotations

import csv
from pathlib import Path

from kenpintool.domain.models import PageItem


class CsvExporter:
    @staticmethod
    def export(path: Path, pages: list[PageItem]) -> None:
        with path.open("w", encoding="utf-8-sig", newline="") as f:
            writer = csv.writer(f)
            writer.writerow(
                [
                    "page_index",
                    "file_name",
                    "pdf_page_index",
                    "decision",
                    "detection_codes",
                    "detection_levels",
                    "suggested_actions",
                    "ai_human_mismatch",
                    "decision_reason",
                    "evidence_count",
                    "override_reason",
                    "ng_codes",
                ]
            )
            for page in pages:
                decision = page.decision.action.value if page.decision else ""
                ng_codes = ",".join(sorted({d.code for d in page.detections}))
                detection_codes = ",".join(d.code for d in page.detections)
                detection_levels = ",".join(d.level.value for d in page.detections)
                suggested_actions = ",".join(
                    d.suggested_action.value if d.suggested_action else ""
                    for d in page.detections
                )
                ai_human_mismatch = bool(page.detections) and decision == "OK"
                decision_reason = (
                    page.decision.exception_reason_code if page.decision else ""
                )
                evidence_count = sum(len(d.evidence) for d in page.detections)
                override_reason = page.decision.override_reason if page.decision else ""
                writer.writerow(
                    [
                        page.index,
                        page.file_name,
                        page.pdf_page_index if page.pdf_page_index is not None else "",
                        decision,
                        detection_codes,
                        detection_levels,
                        suggested_actions,
                        "1" if ai_human_mismatch else "0",
                        decision_reason,
                        evidence_count,
                        override_reason,
                        ng_codes,
                    ]
                )
