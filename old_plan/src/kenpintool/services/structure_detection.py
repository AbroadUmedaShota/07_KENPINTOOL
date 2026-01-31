from __future__ import annotations

import numpy as np

from kenpintool.services.image_decode import load_cv2_image


class StructureDetectionService:
    def __init__(self, hash_size: int = 8) -> None:
        self._hash_size = hash_size

    def compute_hash(self, file_path: str, pdf_page_index: int | None) -> list[int] | None:
        image = load_cv2_image(file_path, pdf_page_index)
        if image is None:
            return None
        try:
            import cv2
        except Exception:
            return None

        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        resized = cv2.resize(gray, (self._hash_size, self._hash_size), interpolation=cv2.INTER_AREA)
        avg = resized.mean()
        bits = (resized > avg).astype(np.uint8).flatten().tolist()
        return bits

    def compute_similarity(self, hash_a: list[int], hash_b: list[int]) -> float:
        if not hash_a or not hash_b or len(hash_a) != len(hash_b):
            return 0.0
        diff = sum(1 for a, b in zip(hash_a, hash_b) if a != b)
        return 1.0 - (diff / len(hash_a))
