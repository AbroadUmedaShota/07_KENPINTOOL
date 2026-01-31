from __future__ import annotations

import sqlite3
from datetime import datetime
from pathlib import Path

from kenpintool.domain.models import Decision, DecisionAction, Detection, NgLevel, PageItem


class DatabaseService:
    def __init__(self, db_path: Path) -> None:
        self._db_path = db_path
        self._conn = sqlite3.connect(str(db_path))
        self._conn.row_factory = sqlite3.Row

    def initialize(self) -> None:
        cursor = self._conn.cursor()
        cursor.executescript(
            """
            CREATE TABLE IF NOT EXISTS cases (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                case_name TEXT NOT NULL,
                input_path TEXT NOT NULL,
                ruleset_version TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS pages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                case_id INTEGER NOT NULL,
                page_index INTEGER NOT NULL,
                file_path TEXT NOT NULL,
                file_name TEXT NOT NULL,
                pdf_page_index INTEGER,
                UNIQUE(case_id, page_index)
            );
            CREATE TABLE IF NOT EXISTS detections (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                page_id INTEGER NOT NULL,
                code TEXT NOT NULL,
                level TEXT NOT NULL,
                message TEXT NOT NULL,
                is_qlt_05 INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS decisions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                page_id INTEGER NOT NULL,
                action TEXT NOT NULL,
                timestamp_utc TEXT NOT NULL,
                exception_reason_code TEXT,
                exception_note TEXT,
                override_reason TEXT
            );
            CREATE TABLE IF NOT EXISTS file_integrity (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                case_id INTEGER NOT NULL,
                file_path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                mtime INTEGER NOT NULL,
                UNIQUE(case_id, file_path)
            );
            """
        )
        self._conn.commit()

    def create_case(self, case_name: str, input_path: str, ruleset_version: str) -> int:
        cursor = self._conn.cursor()
        cursor.execute(
            "INSERT INTO cases (case_name, input_path, ruleset_version, created_at) VALUES (?, ?, ?, ?)",
            (case_name, input_path, ruleset_version, datetime.utcnow().isoformat()),
        )
        self._conn.commit()
        return int(cursor.lastrowid)

    def upsert_pages(self, case_id: int, pages: list[PageItem]) -> dict[int, int]:
        cursor = self._conn.cursor()
        id_map: dict[int, int] = {}
        for page in pages:
            cursor.execute(
                """
                INSERT INTO pages (case_id, page_index, file_path, file_name, pdf_page_index)
                VALUES (?, ?, ?, ?, ?)
                ON CONFLICT(case_id, page_index)
                DO UPDATE SET file_path=excluded.file_path, file_name=excluded.file_name, pdf_page_index=excluded.pdf_page_index
                """,
                (
                    case_id,
                    page.index,
                    page.file_path,
                    page.file_name,
                    page.pdf_page_index,
                ),
            )
            page_id = cursor.execute(
                "SELECT id FROM pages WHERE case_id = ? AND page_index = ?",
                (case_id, page.index),
            ).fetchone()
            if page_id:
                id_map[page.index] = int(page_id["id"])
        self._conn.commit()
        return id_map

    def save_detections(self, page_id: int, detections: list[Detection]) -> None:
        cursor = self._conn.cursor()
        cursor.execute("DELETE FROM detections WHERE page_id = ?", (page_id,))
        cursor.executemany(
            "INSERT INTO detections (page_id, code, level, message, is_qlt_05) VALUES (?, ?, ?, ?, ?)",
            [
                (page_id, d.code, d.level.value, d.message, 1 if d.is_qlt_05 else 0)
                for d in detections
            ],
        )
        self._conn.commit()

    def save_decision(self, page_id: int, decision: Decision) -> None:
        cursor = self._conn.cursor()
        cursor.execute("DELETE FROM decisions WHERE page_id = ?", (page_id,))
        cursor.execute(
            """
            INSERT INTO decisions (page_id, action, timestamp_utc, exception_reason_code, exception_note, override_reason)
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            (
                page_id,
                decision.action.value,
                decision.timestamp_utc.isoformat(),
                decision.exception_reason_code,
                decision.exception_note,
                decision.override_reason,
            ),
        )
        self._conn.commit()

    def save_file_hashes(self, records: list[tuple[int, str, str, int, int]]) -> None:
        cursor = self._conn.cursor()
        cursor.executemany(
            """
            INSERT INTO file_integrity (case_id, file_path, sha256, file_size, mtime)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT(case_id, file_path)
            DO UPDATE SET sha256=excluded.sha256, file_size=excluded.file_size, mtime=excluded.mtime
            """,
            records,
        )
        self._conn.commit()

    def load_file_hashes(self) -> list[tuple[str, str]]:
        cursor = self._conn.cursor()
        rows = cursor.execute("SELECT file_path, sha256 FROM file_integrity").fetchall()
        return [(row["file_path"], row["sha256"]) for row in rows]

    def close(self) -> None:
        self._conn.close()
