import tempfile
from pathlib import Path

from kenpintool.services.audit_log import AuditLogWriter, verify_audit_chain


def test_audit_chain_verification() -> None:
    with tempfile.TemporaryDirectory() as tmpdir:
        path = Path(tmpdir) / "audit.jsonl"
        writer = AuditLogWriter(path)
        writer.append("event", {"a": 1})
        writer.append("event", {"a": 2})

        errors = verify_audit_chain(path)
        assert errors == []

        # Tamper with the file
        lines = path.read_text(encoding="utf-8").splitlines()
        lines[0] = lines[0].replace('"a": 1', '"a": 9')
        path.write_text("\n".join(lines) + "\n", encoding="utf-8")

        errors = verify_audit_chain(path)
        assert errors
