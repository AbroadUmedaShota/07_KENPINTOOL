from __future__ import annotations

import hashlib
from pathlib import Path

from kenpintool.domain.models import PageItem
from kenpintool.services.database import DatabaseService


class FileIntegrityService:
    def record_hashes(self, db: DatabaseService, case_id: int, pages: list[PageItem]) -> None:
        file_paths = sorted({Path(p.file_path) for p in pages})
        records = []
        for path in file_paths:
            if not path.exists():
                continue
            sha = self._hash_file(path)
            stat = path.stat()
            records.append((case_id, str(path), sha, stat.st_size, int(stat.st_mtime)))
        db.save_file_hashes(records)

    def verify_hashes(self, db: DatabaseService) -> bool:
        records = db.load_file_hashes()
        for file_path, expected_hash in records:
            path = Path(file_path)
            if not path.exists():
                return False
            if self._hash_file(path) != expected_hash:
                return False
        return True

    def _hash_file(self, path: Path) -> str:
        sha = hashlib.sha256()
        with path.open("rb") as f:
            for chunk in iter(lambda: f.read(1024 * 1024), b""):
                sha.update(chunk)
        return sha.hexdigest()
