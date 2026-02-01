from dataclasses import dataclass, field
from typing import List, Optional
from enum import Enum, auto

class Decision(Enum):
    NONE = auto()
    OK = auto()
    NG = auto()

@dataclass
class Detection:
    code: str
    name: str
    x: float
    y: float
    width: float
    height: float
    confidence: float

@dataclass
class PageItem:
    file_path: str
    file_name: str
    detections: List[Detection] = field(default_factory=list)
    decision: Decision = Decision.NONE
    
    @property
    def status_text(self) -> str:
        if self.decision == Decision.OK: return "OK"
        if self.decision == Decision.NG: return "NG"
        if self.detections: return "疑い"
        return "未確認"
