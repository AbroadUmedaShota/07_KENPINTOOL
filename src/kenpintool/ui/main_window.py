import sys
import os
from PySide6.QtWidgets import (QApplication, QMainWindow, QWidget, QHBoxLayout, 
                             QVBoxLayout, QListWidget, QLabel, QPushButton, 
                             QScrollArea, QFrame)
from PySide6.QtGui import QPixmap, QPainter, QPen, QColor, QImage
from PySide6.QtCore import Qt, QRectF

from ..engine.quality import QualityEngine
from ..data.models import PageItem, Detection, Decision

class ImageViewer(QLabel):
    """検知結果をオーバーレイ表示する画像ビューアー。"""
    def __init__(self):
        super().__init__()
        self.setAlignment(Qt.AlignCenter)
        self.item: Optional[PageItem] = None
        self.original_pixmap: Optional[QPixmap] = None

    def set_page_item(self, item: PageItem):
        self.item = item
        if os.path.exists(item.file_path):
            self.original_pixmap = QPixmap(item.file_path)
            self.update_display()
        else:
            self.setText("ファイルが見つかりません")

    def update_display(self):
        if not self.original_pixmap:
            return
            
        # 描画用のコピーを作成
        temp_pixmap = self.original_pixmap.copy()
        painter = QPainter(temp_pixmap)
        
        # 検知結果の描画
        for det in self.item.detections:
            pen = QPen(QColor(255, 0, 0, 180))
            pen.setWidth(5)
            painter.setPen(pen)
            
            # 正規化座標からピクセル座標に変換
            w = temp_pixmap.width()
            h = temp_pixmap.height()
            rect = QRectF(det.x * w, det.y * h, det.width * w, det.height * h)
            painter.drawRect(rect)
            
            # ラベルの描画
            painter.drawText(rect.topLeft(), f"{det.code}: {det.name}")
            
        painter.end()
        
        # スクロールエリアに収まるように縮小表示（簡易版）
        scaled_pixmap = temp_pixmap.scaled(self.size(), Qt.KeepAspectRatio, Qt.SmoothTransformation)
        self.setPixmap(scaled_pixmap)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if self.item:
            self.update_display()

class MainWindow(QMainWindow):
    def __init__(self, input_dir: str):
        super().__init__()
        self.setWindowTitle("自動検品ツール - Python Prototype")
        self.resize(1200, 800)
        
        self.engine = QualityEngine()
        self.input_dir = input_dir
        self.items = []
        
        self.init_ui()
        self.load_files()

    def init_ui(self):
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        main_layout = QHBoxLayout(central_widget)

        # 左ペイン: ファイルリスト
        self.list_widget = QListWidget()
        self.list_widget.setFixedWidth(250)
        self.list_widget.currentRowChanged.connect(self.on_selection_changed)
        main_layout.addWidget(self.list_widget)

        # 中央ペイン: ビューアー
        self.viewer = ImageViewer()
        self.scroll_area = QScrollArea()
        self.scroll_area.setWidgetResizable(True)
        self.scroll_area.setWidget(self.viewer)
        main_layout.addWidget(self.scroll_area, stretch=1)

        # 右ペイン: 操作パネル
        right_panel = QVBoxLayout()
        self.status_label = QLabel("ステータス: 未選択")
        self.status_label.setStyleSheet("font-weight: bold; font-size: 14px;")
        right_panel.addWidget(self.status_label)
        
        self.ok_button = QPushButton("OK (問題なし)")
        self.ok_button.setFixedHeight(50)
        self.ok_button.setStyleSheet("background-color: #90EE90;")
        self.ok_button.clicked.connect(self.on_ok_clicked)
        right_panel.addWidget(self.ok_button)
        
        self.ng_button = QPushButton("NG (不備あり)")
        self.ng_button.setFixedHeight(50)
        self.ng_button.setStyleSheet("background-color: #FFB6C1;")
        self.ng_button.clicked.connect(self.on_ng_clicked)
        right_panel.addWidget(self.ng_button)
        
        right_panel.addStretch()
        main_layout.addLayout(right_panel)

    def load_files(self):
        if not os.path.exists(self.input_dir):
            return
            
        for f in os.listdir(self.input_dir):
            if f.lower().endswith(('.jpg', '.jpeg', '.png')):
                path = os.path.join(self.input_dir, f)
                # 初期ロード時に解析を実行
                dets_raw = self.engine.detect_color_streaks(path)
                dets_raw += self.engine.detect_folded_corners(path)
                
                detections = [Detection(**d) for d in dets_raw]
                item = PageItem(file_path=path, file_name=f, detections=detections)
                self.items.append(item)
                self.list_widget.addItem(f"{item.file_name} [{item.status_text}]")

    def on_selection_changed(self, index):
        if 0 <= index < len(self.items):
            item = self.items[index]
            self.viewer.set_page_item(item)
            self.status_label.setText(f"ステータス: {item.status_text}")

    def on_ok_clicked(self):
        idx = self.list_widget.currentRow()
        if idx >= 0:
            self.items[idx].decision = Decision.OK
            self.update_list_item(idx)
            self.next_page()

    def on_ng_clicked(self):
        idx = self.list_widget.currentRow()
        if idx >= 0:
            self.items[idx].decision = Decision.NG
            self.update_list_item(idx)
            self.next_page()

    def update_list_item(self, index):
        item = self.items[index]
        self.list_widget.item(index).setText(f"{item.file_name} [{item.status_text}]")
        self.status_label.setText(f"ステータス: {item.status_text}")

    def next_page(self):
        curr = self.list_widget.currentRow()
        if curr < self.list_widget.count() - 1:
            self.list_widget.setCurrentRow(curr + 1)

def main():
    app = QApplication(sys.argv)
    input_dir = "samples/inputs"
    window = MainWindow(input_dir)
    window.show()
    sys.exit(app.exec())

if __name__ == "__main__":
    main()
