from jarvis_ai.memory.session_store import SessionStore


class Summarizer:

    def __init__(self, store: SessionStore) -> None:
        self.store = store

    def compress_old_messages(self, session_id: str, keep_last_n: int = 10) -> str:
        messages = self.store.get_recent_messages(session_id, limit=keep_last_n * 2)
        if len(messages) <= keep_last_n:
            return ""

        old_messages = messages[:-keep_last_n]
        summary_lines = [f"{m['role']}: {m['content']}" for m in old_messages]
        compressed_summary = "Compressed context:\n" + "\n".join(summary_lines)
        return compressed_summary
