from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path


class NgLevel(str, Enum):
    NG_A = "NG-A"
    NG_B = "NG-B"
    NG_C = "NG-C"


class DecisionAction(str, Enum):
    OK = "OK"
    RESCAN = "RESCAN"
    EXCEPTION_APPROVED = "EXCEPTION_APPROVED"


@dataclass(frozen=True)
class Detection:
    code: str
    level: NgLevel
    message: str
    is_qlt_05: bool = False
    suggested_action: DecisionAction | None = None
    evidence: list["EvidenceRegion"] = field(default_factory=list)


@dataclass(frozen=True)
class Decision:
    action: DecisionAction
    timestamp_utc: datetime
    exception_reason_code: str | None = None
    exception_note: str | None = None
    override_reason: str | None = None


@dataclass(frozen=True)
class EvidenceRegion:
    x: float
    y: float
    width: float
    height: float


@dataclass
class PageItem:
    index: int
    file_path: str
    detections: list[Detection] = field(default_factory=list)
    decision: Decision | None = None
    pdf_page_index: int | None = None
    file_name: str | None = None

    def __post_init__(self) -> None:
        if not self.file_name:
            self.file_name = Path(self.file_path).name

    def has_fatal_detection(self) -> bool:
        return any(d.level == NgLevel.NG_A for d in self.detections)

    def has_qlt_05(self) -> bool:
        return any(d.is_qlt_05 for d in self.detections)


 
