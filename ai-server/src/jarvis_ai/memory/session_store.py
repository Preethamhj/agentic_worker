import os
import sqlite3
from typing import Any

from jarvis_ai.schemas.models import ReportResponseModel


class SessionStore:

    def __init__(self, db_path: str | None = None) -> None:
        if db_path is None:
            db_dir = os.path.abspath(
                os.path.join(os.path.dirname(__file__), "../../../../logs")
            )
            os.makedirs(db_dir, exist_ok=True)
            db_path = os.path.join(db_dir, "session_memory.db")
        self.db_path = db_path
        self._init_db()

    def _get_connection(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self.db_path)
        conn.row_factory = sqlite3.Row
        return conn

    def _init_db(self) -> None:
        conn = self._get_connection()
        try:
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS sessions (
                    session_id TEXT PRIMARY KEY,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
                """
            )
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(session_id) REFERENCES sessions(session_id)
                );
                """
            )
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS task_summaries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    task_id TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(session_id) REFERENCES sessions(session_id)
                );
                """
            )
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS report_idempotency (
                    idempotency_key TEXT PRIMARY KEY,
                    response_json TEXT NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );
                """
            )
            conn.commit()
        finally:
            conn.close()

    def ensure_session(self, session_id: str) -> None:
        conn = self._get_connection()
        try:
            conn.execute(
                "INSERT OR IGNORE INTO sessions (session_id) VALUES (?);", (session_id,)
            )
            conn.commit()
        finally:
            conn.close()

    def add_message(self, session_id: str, role: str, content: str) -> None:
        self.ensure_session(session_id)
        conn = self._get_connection()
        try:
            conn.execute(
                "INSERT INTO messages (session_id, role, content) VALUES (?, ?, ?);",
                (session_id, role, content),
            )
            conn.commit()
        finally:
            conn.close()

    def add_task_summary(self, session_id: str, task_id: str, summary: str) -> None:
        self.ensure_session(session_id)
        conn = self._get_connection()
        try:
            conn.execute(
                "INSERT INTO task_summaries (session_id, task_id, summary) VALUES (?, ?, ?);",
                (session_id, task_id, summary),
            )
            conn.commit()
        finally:
            conn.close()

    def get_report_response(self, idempotency_key: str) -> ReportResponseModel | None:
        conn = self._get_connection()
        try:
            row = conn.execute(
                "SELECT response_json FROM report_idempotency WHERE idempotency_key = ?;",
                (idempotency_key,),
            ).fetchone()
            if row is None:
                return None
            return ReportResponseModel.model_validate_json(row["response_json"])
        finally:
            conn.close()

    def add_report_response_if_absent(
        self,
        idempotency_key: str,
        session_id: str,
        task_id: str,
        summary: str,
        response: ReportResponseModel,
    ) -> ReportResponseModel:
        conn = self._get_connection()
        try:
            conn.execute("BEGIN IMMEDIATE;")
            row = conn.execute(
                "SELECT response_json FROM report_idempotency WHERE idempotency_key = ?;",
                (idempotency_key,),
            ).fetchone()
            if row is not None:
                stored_response = ReportResponseModel.model_validate_json(row["response_json"])
                conn.commit()
                return stored_response

            conn.execute(
                "INSERT OR IGNORE INTO sessions (session_id) VALUES (?);",
                (session_id,),
            )
            conn.execute(
                "INSERT INTO task_summaries (session_id, task_id, summary) VALUES (?, ?, ?);",
                (session_id, task_id, summary),
            )
            conn.execute(
                """
                INSERT INTO report_idempotency (idempotency_key, response_json)
                VALUES (?, ?);
                """,
                (idempotency_key, response.model_dump_json()),
            )
            conn.commit()
            return response
        except Exception:
            conn.rollback()
            raise
        finally:
            conn.close()

    def get_recent_messages(self, session_id: str, limit: int = 10) -> list[dict[str, Any]]:
        conn = self._get_connection()
        try:
            cursor = conn.execute(
                "SELECT role, content, created_at FROM messages WHERE session_id = ? ORDER BY id DESC LIMIT ?;",
                (session_id, limit),
            )
            rows = cursor.fetchall()
            messages = [{"role": row["role"], "content": row["content"]} for row in reversed(rows)]
            return messages
        finally:
            conn.close()

    def get_task_summaries(self, session_id: str) -> list[dict[str, Any]]:
        conn = self._get_connection()
        try:
            cursor = conn.execute(
                "SELECT task_id, summary, created_at FROM task_summaries WHERE session_id = ? ORDER BY id ASC;",
                (session_id,),
            )
            rows = cursor.fetchall()
            return [{"task_id": row["task_id"], "summary": row["summary"]} for row in rows]
        finally:
            conn.close()
