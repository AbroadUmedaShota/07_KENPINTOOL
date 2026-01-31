from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from kenpintool.domain.models import PageItem


@dataclass(frozen=True)
class ReportMetadata:
    case_name: str
    generated_at: datetime
    page_count: int


class ReportGenerator:
    @staticmethod
    def generate(path: Path, metadata: ReportMetadata, pages: list[PageItem]) -> None:
        try:
            from reportlab.lib.pagesizes import A4
            from reportlab.pdfgen import canvas
        except Exception:
            return

        c = canvas.Canvas(str(path), pagesize=A4)
        c.setTitle("KENPINTOOL Report")

        y = 800
        c.drawString(40, y, f"案件名: {metadata.case_name}")
        y -= 20
        c.drawString(40, y, f"生成日時: {metadata.generated_at.isoformat()}")
        y -= 20
        c.drawString(40, y, f"ページ数: {metadata.page_count}")
        y -= 40

        for page in pages:
            if page.decision is None:
                continue
            if y < 80:
                c.showPage()
                y = 800
            c.drawString(
                40,
                y,
                f"{page.index:04d} {page.file_name} {page.decision.action.value}",
            )
            y -= 16
            if page.detections:
                codes = ",".join(d.code for d in page.detections)
                c.drawString(60, y, f"検知: {codes}")
                y -= 16
            if page.decision.override_reason:
                c.drawString(60, y, f"人判断理由: {page.decision.override_reason}")
                y -= 16

        c.save()
