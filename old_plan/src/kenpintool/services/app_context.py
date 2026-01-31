from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class AppContext:
    input_path: Path
    output_path: Path
    ruleset_version: str
