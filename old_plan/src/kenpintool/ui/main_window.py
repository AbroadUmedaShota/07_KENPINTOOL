from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtGui import QAction
from PySide6.QtWidgets import (
    QLabel,
    QMainWindow,
    QListWidget,
    QFileDialog,
    QSplitter,
    QVBoxLayout,
    QWidget,
    QPushButton,
    QInputDialog,
    QMessageBox,
)

from kenpintool.viewmodels.main_viewmodel import MainViewModel
from kenpintool.ui.image_view import ImageView
from kenpintool.domain.exception_reasons import EXCEPTION_REASONS
from kenpintool.domain.decision_reasons import DECISION_REASONS


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("KENPINTOOL Prototype (Python)")

        self._viewmodel = MainViewModel()
        self._viewmodel.status_changed.connect(self.statusBar().showMessage)
        self._viewmodel.image_changed.connect(self._on_image_changed)
        self._viewmodel.previous_image_changed.connect(self._on_previous_image_changed)
        self._viewmodel.pages_changed.connect(self._on_pages_changed)
        self._viewmodel.detections_changed.connect(self._on_detections_changed)
        self._viewmodel.integrity_status_changed.connect(self._on_integrity_status_changed)
        self._viewmodel.analysis_running_changed.connect(self._on_analysis_running_changed)
        self._syncing_views = False
        self._sync_enabled = True
        self._actions: list[QAction] = []
        self._integrity_ok = True
        self._zoom_lock = False

        self.setCentralWidget(self._build_layout())
        self._init_shortcuts()
        self.statusBar().showMessage(self._viewmodel.status_message)

    def _build_layout(self) -> QWidget:
        root = QWidget(self)
        layout = QVBoxLayout(root)
        layout.setContentsMargins(8, 8, 8, 8)

        splitter = QSplitter(Qt.Orientation.Horizontal, root)

        self._page_list = QListWidget(splitter)

        center_panel = QWidget(splitter)
        center_layout = QVBoxLayout(center_panel)
        center_layout.setContentsMargins(0, 0, 0, 0)
        center_split = QSplitter(Qt.Orientation.Vertical, center_panel)
        self._image_view = ImageView(center_split)
        self._previous_image_view = ImageView(center_split)
        self._image_view.view_changed.connect(self._sync_from_current)
        self._previous_image_view.view_changed.connect(self._sync_from_previous)
        center_layout.addWidget(center_split)

        right_panel = QWidget(splitter)
        right_layout = QVBoxLayout(right_panel)
        right_layout.addWidget(QLabel("判定・情報（仮）", right_panel))
        right_layout.addWidget(QLabel("NG-Aは例外承認不可 / QLT-05は再スキャンのみ", right_panel))
        self._integrity_label = QLabel("入力整合: 未確認", right_panel)
        self._integrity_label.setStyleSheet("color: #555; font-weight: bold;")
        right_layout.addWidget(self._integrity_label)
        self._integrity_banner = QLabel("", right_panel)
        self._integrity_banner.setStyleSheet(
            "background-color: #b00020; color: white; padding: 6px; font-weight: bold;"
        )
        self._integrity_banner.setVisible(False)
        right_layout.addWidget(self._integrity_banner)
        self._evidence_list = QListWidget(right_panel)
        right_layout.addWidget(QLabel("Evidence一覧", right_panel))
        right_layout.addWidget(self._evidence_list)

        self._button_ok = QPushButton("OK", right_panel)
        self._button_rescan = QPushButton("再スキャン", right_panel)
        self._button_exception = QPushButton("例外承認（EXC-01）", right_panel)
        self._button_ok.clicked.connect(self._handle_ok_clicked)
        self._button_rescan.clicked.connect(self._viewmodel.mark_rescan)
        self._button_exception.clicked.connect(self._handle_exception_clicked)
        right_layout.addWidget(self._button_ok)
        right_layout.addWidget(self._button_rescan)
        right_layout.addWidget(self._button_exception)
        button_compare = QPushButton("比較モード切替", right_panel)
        button_compare.clicked.connect(self._viewmodel.toggle_compare)
        right_layout.addWidget(button_compare)
        self._button_compare_target = QPushButton("比較対象: 前ページ", right_panel)
        self._button_compare_target.clicked.connect(self._toggle_compare_target)
        right_layout.addWidget(self._button_compare_target)
        self._button_zoom_lock = QPushButton("ズーム保持: OFF", right_panel)
        self._button_zoom_lock.clicked.connect(self._toggle_zoom_lock)
        right_layout.addWidget(self._button_zoom_lock)
        self._button_sync = QPushButton("同期: ON", right_panel)
        self._button_sync.clicked.connect(self._toggle_sync)
        right_layout.addWidget(self._button_sync)
        self._button_export_csv = QPushButton("CSV出力", right_panel)
        self._button_export_report = QPushButton("PDFレポート出力", right_panel)
        self._button_export_csv.clicked.connect(self._export_csv)
        self._button_export_report.clicked.connect(self._export_report)
        right_layout.addWidget(self._button_export_csv)
        right_layout.addWidget(self._button_export_report)

        splitter.setStretchFactor(0, 1)
        splitter.setStretchFactor(1, 3)
        splitter.setStretchFactor(2, 1)

        layout.addWidget(splitter)
        return root

    def _init_shortcuts(self) -> None:
        self._action_open = self._add_action("フォルダを開く", "Ctrl+O", self._open_folder)
        self._add_action("次ページ", "J", self._viewmodel.next_page)
        self._add_action("前ページ", "K", self._viewmodel.prev_page)
        self._add_action("次NG候補", "N", self._viewmodel.next_issue)
        self._add_action("OK判定", "Space", self._handle_ok_clicked)
        self._add_action("再スキャン", "R", self._viewmodel.mark_rescan)
        self._add_action("比較モード切替", "C", self._viewmodel.toggle_compare)
        self._add_action("比較対象切替", "V", self._toggle_compare_target)
        self._add_action("同期切替", "S", self._toggle_sync)
        self._add_action("ズーム保持切替", "L", self._toggle_zoom_lock)
        self._add_action("CSV出力", "Ctrl+S", self._export_csv)
        self._add_action("レポート出力", "Ctrl+P", self._export_report)
        self._add_action("フィット表示", "F", self._fit_view)

    def _add_action(self, label: str, shortcut: str, callback) -> QAction:
        action = QAction(label, self)
        action.setShortcut(shortcut)
        action.triggered.connect(callback)
        self._actions.append(action)
        self.addAction(action)
        return action

    def _open_folder(self) -> None:
        folder = QFileDialog.getExistingDirectory(self, "案件フォルダを選択")
        if folder:
            self._viewmodel.load_case(Path(folder))

    def _fit_view(self) -> None:
        self._image_view.fit_to_view()
        self._previous_image_view.fit_to_view()

    def _on_image_changed(self, image) -> None:
        self._image_view.set_image(image)
        if not self._zoom_lock:
            self._image_view.fit_to_view()
        self._refresh_overlays()

    def _on_previous_image_changed(self, image) -> None:
        self._previous_image_view.set_image(image)
        if not self._zoom_lock:
            self._previous_image_view.fit_to_view()

    def _on_pages_changed(self, pages) -> None:
        self._page_list.clear()
        for page in pages:
            self._page_list.addItem(f"{page.index:04d} {page.file_name}")

    def _on_detections_changed(self) -> None:
        self._refresh_overlays()

    def _refresh_overlays(self) -> None:
        page = self._viewmodel.current_page
        if page is not None:
            self._image_view.set_overlays(page.detections)
            self._refresh_evidence_list(page)
            self._update_decision_buttons(page)

    def _refresh_evidence_list(self, page) -> None:
        self._evidence_list.clear()
        for detection in page.detections:
            for idx, ev in enumerate(detection.evidence, start=1):
                self._evidence_list.addItem(
                    f"{detection.code} #{idx} ({ev.x:.2f},{ev.y:.2f},{ev.width:.2f},{ev.height:.2f})"
                )

    def _update_decision_buttons(self, page) -> None:
        has_fatal = page.has_fatal_detection()
        has_qlt05 = page.has_qlt_05()
        if not self._integrity_ok:
            self._button_ok.setEnabled(False)
            self._button_exception.setEnabled(False)
            return
        self._button_ok.setEnabled(not has_fatal)
        self._button_exception.setEnabled(not has_fatal and not has_qlt05)

    def _handle_ok_clicked(self) -> None:
        page = self._viewmodel.current_page
        if page is None:
            return
        if page.detections:
            reason, ok = self._modal_select_reason()
            if not ok:
                return
            detail, ok = self._modal_input_detail()
            if not ok or not detail:
                QMessageBox.warning(self, "理由必須", "補足の入力が必要です。")
                return
            self._viewmodel.update_view_context(self._image_view.get_view_context())
            self._viewmodel.mark_ok_with_reason(f"{reason} / {detail}")
            return
        self._viewmodel.update_view_context(self._image_view.get_view_context())
        self._viewmodel.mark_ok()

    def _handle_exception_clicked(self) -> None:
        reasons = [f"{code}: {label}" for code, label in EXCEPTION_REASONS]
        reason, ok = self._modal_select_item("例外承認理由", "理由コードを選択してください。", reasons)
        if not ok or not reason:
            return
        code = reason.split(":")[0].strip()
        self._viewmodel.mark_exception(code)

    def _on_integrity_status_changed(self, ok: bool, message: str) -> None:
        text = f"入力整合: {'OK' if ok else 'NG'} ({message})"
        self._integrity_label.setText(text)
        self._integrity_ok = ok
        if ok:
            self._integrity_label.setStyleSheet("color: #2f7d32; font-weight: bold;")
            self._integrity_banner.setVisible(False)
            self._image_view.set_warning(None)
            self._previous_image_view.set_warning(None)
        else:
            self._integrity_label.setStyleSheet("color: #b00020; font-weight: bold;")
            self._integrity_banner.setText("入力ファイルの変更を検知しました。作業を中断してください。")
            self._integrity_banner.setVisible(True)
            self._image_view.set_warning("警告: 入力ファイル不整合")
            self._previous_image_view.set_warning("警告: 入力ファイル不整合")
        self._set_controls_enabled(ok)

    def _on_analysis_running_changed(self, running: bool) -> None:
        self._action_open.setEnabled(not running)

    def _sync_from_current(self) -> None:
        if self._syncing_views:
            return
        if not self._sync_enabled:
            return
        if not self._previous_image_view.has_image():
            return
        self._syncing_views = True
        transform, center = self._image_view.get_view_state()
        self._previous_image_view.set_view_state(transform, center)
        self._syncing_views = False

    def _sync_from_previous(self) -> None:
        if self._syncing_views:
            return
        if not self._sync_enabled:
            return
        if not self._image_view.has_image():
            return
        self._syncing_views = True
        transform, center = self._previous_image_view.get_view_state()
        self._image_view.set_view_state(transform, center)
        self._syncing_views = False

    def _export_csv(self) -> None:
        self._viewmodel.update_view_context(self._image_view.get_view_context())
        self._viewmodel.export_csv()

    def _export_report(self) -> None:
        self._viewmodel.update_view_context(self._image_view.get_view_context())
        self._viewmodel.export_report()

    def _toggle_sync(self) -> None:
        self._sync_enabled = not self._sync_enabled
        self._button_sync.setText("同期: ON" if self._sync_enabled else "同期: OFF")

    def _toggle_compare_target(self) -> None:
        self._viewmodel.toggle_compare_target()
        label = "比較対象: 前ページ" if self._viewmodel.compare_target == "prev" else "比較対象: 次ページ"
        self._button_compare_target.setText(label)

    def _toggle_zoom_lock(self) -> None:
        self._zoom_lock = not self._zoom_lock
        self._button_zoom_lock.setText("ズーム保持: ON" if self._zoom_lock else "ズーム保持: OFF")

    def _modal_select_reason(self) -> tuple[str, bool]:
        reasons = [f"{code}: {label}" for code, label in DECISION_REASONS]
        return self._modal_select_item("理由選択", "理由コードを選択してください。", reasons)

    def _modal_input_detail(self) -> tuple[str, bool]:
        return self._modal_input_text("補足入力", "補足理由を入力してください。")

    def _modal_select_item(self, title: str, label: str, items: list[str]) -> tuple[str, bool]:
        self._set_actions_enabled(False)
        value, ok = QInputDialog.getItem(self, title, label, items, editable=False)
        self._set_actions_enabled(True)
        return value, ok

    def _modal_input_text(self, title: str, label: str) -> tuple[str, bool]:
        self._set_actions_enabled(False)
        value, ok = QInputDialog.getText(self, title, label)
        self._set_actions_enabled(True)
        return value, ok

    def _set_actions_enabled(self, enabled: bool) -> None:
        for action in self._actions:
            action.setEnabled(enabled)

    def _set_controls_enabled(self, enabled: bool) -> None:
        self._button_ok.setEnabled(enabled)
        self._button_rescan.setEnabled(enabled)
        self._button_exception.setEnabled(enabled)
        self._button_export_csv.setEnabled(enabled)
        self._button_export_report.setEnabled(enabled)
