import unittest
from datetime import datetime

from kenpintool.domain.models import (
    Decision,
    DecisionAction,
    Detection,
    NgLevel,
    PageItem,
)
from kenpintool.domain.policy import DecisionPolicy, ValidationError


class DecisionPolicyTests(unittest.TestCase):
    def test_ok_blocked_by_ng_a(self) -> None:
        page = PageItem(
            index=1,
            file_path="dummy",
            detections=[Detection(code="STR-01", level=NgLevel.NG_A, message="fatal")],
        )
        self.assertFalse(DecisionPolicy.can_mark_ok(page))
        with self.assertRaises(ValidationError):
            DecisionPolicy.validate_decision(page, DecisionAction.OK)

    def test_exception_blocked_by_ng_a(self) -> None:
        page = PageItem(
            index=1,
            file_path="dummy",
            detections=[Detection(code="STR-01", level=NgLevel.NG_A, message="fatal")],
        )
        self.assertFalse(DecisionPolicy.can_request_exception(page))
        with self.assertRaises(ValidationError):
            DecisionPolicy.validate_decision(page, DecisionAction.EXCEPTION_APPROVED)

    def test_exception_blocked_by_qlt_05(self) -> None:
        page = PageItem(
            index=1,
            file_path="dummy",
            detections=[Detection(code="QLT-05", level=NgLevel.NG_A, message="noise", is_qlt_05=True)],
        )
        self.assertFalse(DecisionPolicy.can_request_exception(page))
        with self.assertRaises(ValidationError):
            DecisionPolicy.validate_decision(page, DecisionAction.EXCEPTION_APPROVED)

    def test_ok_allowed_without_ng_a(self) -> None:
        page = PageItem(
            index=1,
            file_path="dummy",
            detections=[Detection(code="QLT-02", level=NgLevel.NG_B, message="minor")],
        )
        self.assertTrue(DecisionPolicy.can_mark_ok(page))

    def test_rescan_always_allowed(self) -> None:
        page = PageItem(index=1, file_path="dummy")
        self.assertTrue(DecisionPolicy.can_mark_rescan(page))


if __name__ == "__main__":
    unittest.main()
