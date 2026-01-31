from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
import tempfile


@dataclass(frozen=True)
class RunContext:
    input_path: Path
    output_path: Path
    case_name: str
    ruleset_version: str

    @property
    def audit_log_path(self) -> Path:
        return self.output_path / "audit.jsonl"

    @property
    def csv_path(self) -> Path:
        return self.output_path / "results.csv"

    @property
    def report_path(self) -> Path:
        return self.output_path / "report.pdf"

    @property
    def case_json_path(self) -> Path:
        return self.output_path / "case.json"

    @staticmethod
    def create(input_path: Path, ruleset_version: str) -> "RunContext":
        case_name = input_path.name
        output_path = RunContext._create_output_dir(input_path, case_name)
        return RunContext(
            input_path=input_path,
            output_path=output_path,
            case_name=case_name,
            ruleset_version=ruleset_version,
        )

    @staticmethod
    def _create_output_dir(input_path: Path, case_name: str) -> Path:
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        candidate = input_path / "_kenpintool_output" / f"{case_name}_{timestamp}"
        try:
            candidate.mkdir(parents=True, exist_ok=True)
            return candidate
        except OSError:
            fallback = Path(tempfile.mkdtemp(prefix="kenpintool_"))
            return fallback

    def check_output_permissions(self) -> tuple[bool, str]:
        try:
            test_file = self.output_path / ".perm_check"
            test_file.write_text("ok", encoding="utf-8")
            test_file.unlink(missing_ok=True)
            return True, "ok"
        except Exception as exc:
            return False, f"output not writable: {exc}"
