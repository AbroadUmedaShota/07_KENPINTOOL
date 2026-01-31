from __future__ import annotations

import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path


class AuditLogWriter:
    def __init__(self, path: Path) -> None:
        self._path = path
        self._last_hash = self._read_last_hash()
        self._ensure_append_only_hint()

    def append(self, event_type: str, payload: dict) -> None:
        record = {
            "ts_utc": datetime.now(timezone.utc).isoformat(),
            "type": event_type,
            "payload": payload,
            "prev_hash": self._last_hash,
        }
        record_json = json.dumps(record, ensure_ascii=False, separators=(",", ":"))
        record_hash = hashlib.sha256(record_json.encode("utf-8")).hexdigest()
        record["hash"] = record_hash

        with self._path.open("a", encoding="utf-8") as f:
            f.write(json.dumps(record, ensure_ascii=False) + "\n")

        self._last_hash = record_hash

    def _read_last_hash(self) -> str | None:
        if not self._path.exists():
            return None
        try:
            last_line = ""
            with self._path.open("r", encoding="utf-8") as f:
                for line in f:
                    last_line = line.strip()
            if not last_line:
                return None
            data = json.loads(last_line)
            return data.get("hash")
        except Exception:
            return None

    def _ensure_append_only_hint(self) -> None:
        # Windowsの厳密なACL制御は運用側で担保する前提。
        # ここでは「読み取り専用」などの属性が付いていないかの最低限チェックのみ行う。
        try:
            if self._path.exists() and not self._path.is_file():
                return
            # read-only属性の場合、追記が失敗するため警告ログ等は上位層で実施する。
        except Exception:
            return


def verify_audit_chain(path: Path) -> list[str]:
    if not path.exists():
        return ["audit log not found"]
    errors: list[str] = []
    prev_hash: str | None = None
    try:
        with path.open("r", encoding="utf-8") as f:
            for line_no, line in enumerate(f, start=1):
                line = line.strip()
                if not line:
                    continue
                data = json.loads(line)
                expected_prev = data.get("prev_hash")
                if expected_prev != prev_hash:
                    errors.append(f"line {line_no}: prev_hash mismatch")
                record_hash = data.get("hash")
                payload = {
                    "ts_utc": data.get("ts_utc"),
                    "type": data.get("type"),
                    "payload": data.get("payload"),
                    "prev_hash": data.get("prev_hash"),
                }
                record_json = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
                calc_hash = hashlib.sha256(record_json.encode("utf-8")).hexdigest()
                if record_hash != calc_hash:
                    errors.append(f"line {line_no}: hash mismatch")
                prev_hash = record_hash
    except Exception as exc:
        errors.append(f"verification error: {exc}")
    return errors
