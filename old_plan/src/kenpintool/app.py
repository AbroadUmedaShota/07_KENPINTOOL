from __future__ import annotations

import os
import sys
from pathlib import Path

from PySide6.QtWidgets import QApplication

from kenpintool.ui.main_window import MainWindow


def run_app() -> int:
    os.environ.setdefault("PYTHONDONTWRITEBYTECODE", "1")
    temp_dir = Path.cwd() / "artifacts" / "temp"
    temp_dir.mkdir(parents=True, exist_ok=True)
    os.environ.setdefault("TMPDIR", str(temp_dir))
    os.environ.setdefault("TEMP", str(temp_dir))
    os.environ.setdefault("TMP", str(temp_dir))

    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    return app.exec()
