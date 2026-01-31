from __future__ import annotations

from pathlib import Path


def get_pdf_page_count(path: Path) -> int:
    try:
        import pypdfium2 as pdfium
    except Exception:
        return 0

    try:
        doc = pdfium.PdfDocument(str(path))
        return len(doc)
    except Exception:
        return 0
