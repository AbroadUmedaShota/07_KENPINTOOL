from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from kenpintool.services.pdf_utils import get_pdf_page_count


SUPPORTED_IMAGE_EXTS = {".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp"}
SUPPORTED_PDF_EXTS = {".pdf"}


@dataclass(frozen=True)
class PageSource:
    file_path: Path
    pdf_page_index: int | None = None


class CaseLoader:
    def load_pages(self, input_path: Path) -> list[PageSource]:
        pages: list[PageSource] = []
        for path in sorted(input_path.rglob("*")):
            if not path.is_file():
                continue
            ext = path.suffix.lower()
            if ext in SUPPORTED_IMAGE_EXTS:
                pages.append(PageSource(file_path=path))
            elif ext in SUPPORTED_PDF_EXTS:
                page_count = get_pdf_page_count(path)
                for idx in range(page_count):
                    pages.append(PageSource(file_path=path, pdf_page_index=idx))
        return pages
