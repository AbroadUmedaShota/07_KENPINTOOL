import tempfile
from pathlib import Path

from kenpintool.domain.models import PageItem
from kenpintool.services.database import DatabaseService
from kenpintool.services.file_integrity import FileIntegrityService


def test_file_integrity_detects_change() -> None:
    with tempfile.TemporaryDirectory() as tmpdir:
        base = Path(tmpdir)
        file_path = base / "sample.txt"
        file_path.write_text("original", encoding="utf-8")

        db = DatabaseService(base / "test.db")
        db.initialize()
        case_id = db.create_case("case", str(base), "ruleset")

        pages = [PageItem(index=1, file_path=str(file_path))]
        integrity = FileIntegrityService()
        integrity.record_hashes(db, case_id, pages)

        assert integrity.verify_hashes(db)

        file_path.write_text("modified", encoding="utf-8")
        assert not integrity.verify_hashes(db)
