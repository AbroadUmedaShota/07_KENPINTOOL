from __future__ import annotations

from pathlib import Path

import numpy as np


def load_cv2_image(file_path: str, pdf_page_index: int | None) -> "np.ndarray | None":
    path = Path(file_path)
    if pdf_page_index is not None:
        return _load_pdf_page(path, pdf_page_index)
    return _load_raster(path)


def _load_raster(path: Path) -> "np.ndarray | None":
    try:
        import cv2
    except Exception:
        return None
    data = cv2.imdecode(np.fromfile(str(path), dtype=np.uint8), cv2.IMREAD_COLOR)
    return data


def _load_pdf_page(path: Path, page_index: int) -> "np.ndarray | None":
    try:
        import pypdfium2 as pdfium
    except Exception:
        return None

    try:
        doc = pdfium.PdfDocument(str(path))
        if page_index < 0 or page_index >= len(doc):
            return None
        page = doc[page_index]
        bitmap = page.render(scale=1.0)
        pil_image = bitmap.to_pil().convert("RGB")
        return np.array(pil_image)
    except Exception:
        return None
