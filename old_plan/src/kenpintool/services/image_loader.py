from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from collections import OrderedDict

import numpy as np
from PySide6.QtGui import QImage

from kenpintool.services.case_loader import PageSource


@dataclass(frozen=True)
class LoadedImage:
    image: QImage
    width: int
    height: int


class ImageLoaderService:
    def __init__(self, max_cache_items: int = 3) -> None:
        self._cache: "OrderedDict[str, LoadedImage]" = OrderedDict()
        self._max_cache_items = max_cache_items

    def load(self, source: PageSource) -> LoadedImage | None:
        key = self._cache_key(source)
        cached = self._cache_get(key)
        if cached is not None:
            return cached

        path = source.file_path
        if source.pdf_page_index is not None:
            loaded = self._load_pdf_page(path, source.pdf_page_index)
        else:
            loaded = self._load_raster(path)

        if loaded is not None:
            self._cache_set(key, loaded)
        return loaded

    def clear_cache(self, keep_key: str | None = None) -> None:
        if keep_key and keep_key in self._cache:
            item = self._cache[keep_key]
            self._cache.clear()
            self._cache[keep_key] = item
            return
        self._cache.clear()

    def _cache_key(self, source: PageSource) -> str:
        page = source.pdf_page_index if source.pdf_page_index is not None else ""
        return f"{source.file_path}|{page}"

    def _cache_get(self, key: str) -> LoadedImage | None:
        if key in self._cache:
            self._cache.move_to_end(key)
            return self._cache[key]
        return None

    def _cache_set(self, key: str, value: LoadedImage) -> None:
        self._cache[key] = value
        self._cache.move_to_end(key)
        while len(self._cache) > self._max_cache_items:
            self._cache.popitem(last=False)

    def _load_raster(self, path: Path) -> LoadedImage | None:
        try:
            import cv2
        except Exception:
            return None

        data = cv2.imdecode(np.fromfile(str(path), dtype=np.uint8), cv2.IMREAD_COLOR)
        if data is None:
            return None
        rgb = cv2.cvtColor(data, cv2.COLOR_BGR2RGB)
        h, w, _ = rgb.shape
        image = QImage(rgb.data, w, h, w * 3, QImage.Format.Format_RGB888).copy()
        return LoadedImage(image=image, width=w, height=h)

    def _load_pdf_page(self, path: Path, page_index: int) -> LoadedImage | None:
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
            pil_image = bitmap.to_pil()
            rgb = pil_image.convert("RGB")
            w, h = rgb.size
            data = np.array(rgb)
            image = QImage(data.data, w, h, w * 3, QImage.Format.Format_RGB888).copy()
            return LoadedImage(image=image, width=w, height=h)
        except Exception:
            return None
