from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import QObject, QRunnable, QThreadPool, Signal

from datetime import datetime, timezone

from kenpintool.domain.models import Decision, DecisionAction, PageItem
from kenpintool.domain.policy import DecisionPolicy, ValidationError
from kenpintool.services.case_loader import CaseLoader, PageSource
from kenpintool.services.detection_pipeline import DetectionPipeline
from kenpintool.services.image_loader import ImageLoaderService
from kenpintool.services.run_context import RunContext
from kenpintool.services.audit_log import AuditLogWriter
from kenpintool.services.database import DatabaseService
from kenpintool.services.file_integrity import FileIntegrityService
from kenpintool.services.csv_exporter import CsvExporter
from kenpintool.services.report_generator import ReportGenerator, ReportMetadata


class MainViewModel(QObject):
    status_changed = Signal(str)
    image_changed = Signal(object)
    previous_image_changed = Signal(object)
    pages_changed = Signal(list)
    detections_changed = Signal()
    integrity_status_changed = Signal(bool, str)
    analysis_running_changed = Signal(bool)

    def __init__(self) -> None:
        super().__init__()
        self._status_message = "準備完了"
        self._case_loader = CaseLoader()
        self._image_loader = ImageLoaderService()
        self._pipeline = DetectionPipeline()
        self._thread_pool = QThreadPool.globalInstance()
        self._pages: list[PageItem] = []
        self._sources: list[PageSource] = []
        self._current_index = 0
        self._current_task: _ImageLoadTask | None = None
        self._analysis_task: _AnalysisTask | None = None
        self._run_context: RunContext | None = None
        self._audit_log: AuditLogWriter | None = None
        self._database: DatabaseService | None = None
        self._page_id_by_index: dict[int, int] = {}
        self._integrity = FileIntegrityService()
        self._view_context: dict | None = None
        self._compare_mode = False
        self._compare_target = "prev"

    @property
    def status_message(self) -> str:
        return self._status_message

    def set_status(self, message: str) -> None:
        if not message:
            return
        self._status_message = message
        self.status_changed.emit(message)

    def next_page(self) -> None:
        if not self._pages:
            self.set_status("ページがありません。")
            return
        self._current_index = min(self._current_index + 1, len(self._pages) - 1)
        self._load_current_async()

    def prev_page(self) -> None:
        if not self._pages:
            self.set_status("ページがありません。")
            return
        self._current_index = max(self._current_index - 1, 0)
        self._load_current_async()

    def next_issue(self) -> None:
        if not self._pages:
            self.set_status("ページがありません。")
            return
        start = self._current_index
        for offset in range(1, len(self._pages) + 1):
            idx = (start + offset) % len(self._pages)
            page = self._pages[idx]
            if page.decision is None and page.detections:
                self._current_index = idx
                self._load_current_async()
                return
        self.set_status("次のNG候補がありません。")

    def mark_ok(self) -> None:
        page = self.current_page
        if page is None:
            return
        try:
            DecisionPolicy.validate_decision(page, DecisionAction.OK)
        except ValidationError as exc:
            self.set_status(str(exc))
            return
        if self._requires_override_reason(page, DecisionAction.OK):
            self.set_status("AIと異なる判断のため理由入力が必要です。")
            return
        page.decision = Decision(action=DecisionAction.OK, timestamp_utc=datetime.now(timezone.utc))
        self._persist_decision(page)
        self.set_status("OK判定")

    def mark_rescan(self) -> None:
        page = self.current_page
        if page is None:
            return
        try:
            DecisionPolicy.validate_decision(page, DecisionAction.RESCAN)
        except ValidationError as exc:
            self.set_status(str(exc))
            return
        page.decision = Decision(action=DecisionAction.RESCAN, timestamp_utc=datetime.now(timezone.utc))
        self._persist_decision(page)
        self.set_status("再スキャン判定")

    def mark_exception(self, reason_code: str) -> None:
        page = self.current_page
        if page is None:
            return
        try:
            DecisionPolicy.validate_decision(page, DecisionAction.EXCEPTION_APPROVED)
        except ValidationError as exc:
            self.set_status(str(exc))
            return
        page.decision = Decision(
            action=DecisionAction.EXCEPTION_APPROVED,
            timestamp_utc=datetime.now(timezone.utc),
            exception_reason_code=reason_code,
        )
        self._persist_decision(page)
        self.set_status(f"例外承認: {reason_code}")

    def toggle_compare(self) -> None:
        self._compare_mode = not self._compare_mode
        self.set_status("比較モード: ON" if self._compare_mode else "比較モード: OFF")
        self._load_current_async()

    def toggle_compare_target(self) -> None:
        self._compare_target = "next" if self._compare_target == "prev" else "prev"
        self.set_status("比較対象: 前ページ" if self._compare_target == "prev" else "比較対象: 次ページ")
        self._load_current_async()

    @property
    def compare_target(self) -> str:
        return self._compare_target

    def mark_ok_with_reason(self, reason: str) -> None:
        page = self.current_page
        if page is None:
            return
        if not reason.strip():
            self.set_status("理由の入力が必要です。")
            return
        try:
            DecisionPolicy.validate_decision(page, DecisionAction.OK)
        except ValidationError as exc:
            self.set_status(str(exc))
            return
        page.decision = Decision(
            action=DecisionAction.OK,
            timestamp_utc=datetime.now(timezone.utc),
            override_reason=reason,
        )
        self._persist_decision(page)
        self.set_status("OK判定（理由記録）")

    def export_csv(self) -> None:
        if not self._run_context:
            self.set_status("案件未ロードです。")
            return
        if not self._verify_input_integrity():
            self.set_status("入力ファイルの変更を検知しました。出力を中断します。")
            return
        CsvExporter.export(self._run_context.csv_path, self._pages)
        self.set_status(f"CSV出力: {self._run_context.csv_path}")
        if self._audit_log:
            self._audit_log.append("csv_exported", {"path": str(self._run_context.csv_path)})

    def export_report(self) -> None:
        if not self._run_context:
            self.set_status("案件未ロードです。")
            return
        if not self._verify_input_integrity():
            self.set_status("入力ファイルの変更を検知しました。出力を中断します。")
            return
        metadata = ReportMetadata(
            case_name=self._run_context.case_name,
            generated_at=datetime.now(timezone.utc),
            page_count=len(self._pages),
        )
        ReportGenerator.generate(self._run_context.report_path, metadata, self._pages)
        self.set_status(f"レポート出力: {self._run_context.report_path}")
        if self._audit_log:
            self._audit_log.append("report_exported", {"path": str(self._run_context.report_path)})

    def load_case(self, folder: Path) -> None:
        if not folder.exists():
            self.set_status("フォルダが見つかりません。")
            return
        self._sources = self._case_loader.load_pages(folder)
        self._pages = [
            PageItem(index=i + 1, file_path=str(src.file_path), pdf_page_index=src.pdf_page_index)
            for i, src in enumerate(self._sources)
        ]
        self._current_index = 0
        self._initialize_run_context(folder)
        self.pages_changed.emit(self._pages)
        self.set_status(f"読み込み完了: {len(self._pages)}ページ")
        self._load_current_async()
        self._analyze_all_async()
        self._emit_integrity_status()

    def _initialize_run_context(self, folder: Path) -> None:
        self._run_context = RunContext.create(folder, ruleset_version="prototype-py")
        ok, message = self._run_context.check_output_permissions()
        if not ok:
            self.set_status(message)
        self._audit_log = AuditLogWriter(self._run_context.audit_log_path)
        self._database = DatabaseService(self._run_context.output_path / "kenpintool.db")
        self._database.initialize()
        case_id = self._database.create_case(
            self._run_context.case_name,
            str(self._run_context.input_path),
            self._run_context.ruleset_version,
        )
        self._page_id_by_index = self._database.upsert_pages(case_id, self._pages)
        self._integrity.record_hashes(self._database, case_id, self._pages)
        self._audit_log.append(
            "case_opened",
            {
                "case_name": self._run_context.case_name,
                "input_path": str(self._run_context.input_path),
                "ruleset_version": self._run_context.ruleset_version,
                "page_count": len(self._pages),
            },
        )

    def _persist_decision(self, page: PageItem) -> None:
        if page.decision is None:
            return
        page_id = self._page_id_by_index.get(page.index)
        if page_id and self._database:
            self._database.save_decision(page_id, page.decision)
        if self._audit_log:
            self._audit_log.append(
                "decision",
                {
                    "page_index": page.index,
                    "action": page.decision.action.value,
                    "exception_reason_code": page.decision.exception_reason_code,
                    "exception_note": page.decision.exception_note,
                    "override_reason": page.decision.override_reason,
                    "view_context": self._view_context,
                },
            )

    def update_view_context(self, context: dict) -> None:
        self._view_context = context

    def _verify_input_integrity(self) -> bool:
        if not self._database:
            return False
        ok = self._integrity.verify_hashes(self._database)
        self.integrity_status_changed.emit(ok, "検証済")
        return ok

    def _emit_integrity_status(self) -> None:
        if not self._database:
            self.integrity_status_changed.emit(False, "未初期化")
            return
        ok = self._integrity.verify_hashes(self._database)
        self.integrity_status_changed.emit(ok, "検証済")

    def _requires_override_reason(self, page: PageItem, action: DecisionAction) -> bool:
        if action == DecisionAction.OK and page.detections:
            return True
        return False

    def _load_current_async(self) -> None:
        if not self._sources:
            self.image_changed.emit(None)
            self.previous_image_changed.emit(None)
            return
        source = self._sources[self._current_index]
        task = _ImageLoadTask(self._image_loader, source)
        task.emitter.loaded.connect(self._on_image_loaded)
        self._current_task = task
        self._thread_pool.start(task)

        if self._compare_mode and self._sources:
            if self._compare_target == "prev" and self._current_index > 0:
                compare_index = self._current_index - 1
            elif self._compare_target == "next" and self._current_index < len(self._sources) - 1:
                compare_index = self._current_index + 1
            else:
                compare_index = None
        else:
            compare_index = None

        if compare_index is not None:
            prev_source = self._sources[compare_index]
            prev_task = _ImageLoadTask(self._image_loader, prev_source)
            prev_task.emitter.loaded.connect(self._on_previous_image_loaded)
            self._thread_pool.start(prev_task)
        else:
            self.previous_image_changed.emit(None)

    def _on_image_loaded(self, image) -> None:
        self.image_changed.emit(image)

    @property
    def current_page(self) -> PageItem | None:
        if not self._pages:
            return None
        if self._current_index < 0 or self._current_index >= len(self._pages):
            return None
        return self._pages[self._current_index]

    def _on_previous_image_loaded(self, image) -> None:
        self.previous_image_changed.emit(image)

    def _analysis_progress(self, current: int, total: int) -> None:
        self.set_status(f"解析中: {current}/{total}ページ")

    def _analysis_finished(self) -> None:
        self.set_status("解析完了")
        self.detections_changed.emit()
        self.analysis_running_changed.emit(False)

    def _analyze_all_async(self) -> None:
        if not self._pages:
            return
        self.analysis_running_changed.emit(True)
        task = _AnalysisTask(
            self._pipeline,
            self._pages,
            self._page_id_by_index,
            self._audit_log,
            self._database,
        )
        task.emitter.progress.connect(self._analysis_progress)
        task.emitter.finished.connect(self._analysis_finished)
        task.emitter.page_updated.connect(self._on_page_updated)
        self._analysis_task = task
        self._thread_pool.start(task)

    def _on_page_updated(self, page_index: int) -> None:
        if self.current_page and self.current_page.index == page_index:
            self.detections_changed.emit()


class _ImageLoadEmitter(QObject):
    loaded = Signal(object)


class _ImageLoadTask(QRunnable):
    def __init__(self, loader: ImageLoaderService, source: PageSource) -> None:
        super().__init__()
        self._loader = loader
        self._source = source
        self.emitter = _ImageLoadEmitter()

    def run(self) -> None:
        result = self._loader.load(self._source)
        image = result.image if result else None
        self.emitter.loaded.emit(image)


class _AnalysisEmitter(QObject):
    progress = Signal(int, int)
    finished = Signal()
    page_updated = Signal(int)


class _AnalysisTask(QRunnable):
    def __init__(
        self,
        pipeline: DetectionPipeline,
        pages: list[PageItem],
        page_id_by_index: dict[int, int],
        audit_log: AuditLogWriter | None,
        database: DatabaseService | None,
    ) -> None:
        super().__init__()
        self._pipeline = pipeline
        self._pages = pages
        self.emitter = _AnalysisEmitter()
        self._page_id_by_index = page_id_by_index
        self._audit_log = audit_log
        self._database = database

    def run(self) -> None:
        total = len(self._pages)
        for idx, result in enumerate(self._pipeline.analyze_pages(self._pages), start=1):
            page = self._pages[result.page_index - 1]
            page.detections = result.detections
            page_id = self._page_id_by_index.get(page.index)
            if page_id and self._database:
                self._database.save_detections(page_id, result.detections)
            if self._audit_log:
                self._audit_log.append(
                    "detections",
                    {
                        "page_index": page.index,
                        "codes": [d.code for d in result.detections],
                    },
                )
            self.emitter.page_updated.emit(page.index)
            self.emitter.progress.emit(idx, total)
        self.emitter.finished.emit()
