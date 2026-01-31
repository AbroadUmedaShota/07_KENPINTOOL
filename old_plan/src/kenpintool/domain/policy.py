from __future__ import annotations

import hashlib
from pathlib import Path

from kenpintool.domain.models import DecisionAction, PageItem


class ValidationError(ValueError):
    pass


class DecisionPolicy:
    @staticmethod
    def validate_decision(page: PageItem, action: DecisionAction) -> None:
        if action == DecisionAction.OK and page.has_fatal_detection():
            raise ValidationError("NG-A検出中はOKにできません。")
        if action == DecisionAction.EXCEPTION_APPROVED and not DecisionPolicy.can_request_exception(page):
            raise ValidationError("例外承認はNG-B/NG-Cのみ可能です。")

    @staticmethod
    def can_mark_ok(page: PageItem) -> bool:
        return not page.has_fatal_detection()

    @staticmethod
    def can_mark_rescan(page: PageItem) -> bool:
        return True

    @staticmethod
    def can_request_exception(page: PageItem) -> bool:
        if page.has_fatal_detection():
            return False
        if page.has_qlt_05():
            return False
        return True


def compute_ruleset_hash(path: Path) -> str:
    sha = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            sha.update(chunk)
    return sha.hexdigest()
