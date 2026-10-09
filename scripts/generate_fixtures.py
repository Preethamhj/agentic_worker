import json
import os

examples_dir = r"C:\Users\ASUS\Desktop\agentic worker\contracts\examples"
os.makedirs(examples_dir, exist_ok=True)

fixtures = {
    "PlanRequest": {
        "valid1": {
            "schema_version": 1,
            "session_id": "sess-001",
            "text": "create folder test in documents",
            "locale": "en-US"
        },
        "invalid1": {
            "schema_version": 2, # bad schema version
            "session_id": "sess-001",
            "text": "create folder test",
            "locale": "en-US"
        },
        "invalid2": {
            "schema_version": 1,
            "session_id": "sess-001",
            "text": "create folder test",
            "locale": "en-US",
            "extra_field": "not_allowed" # additional property
        }
    },
    "Intent": {
        "valid1": {
            "schema_version": 1,
            "intent_id": "intent-001",
            "kind": "command",
            "action": "fs.create_folder",
            "confidence": 0.95,
            "entities": {"path": "test"},
            "risk": {"level": "medium", "reasons": ["creates directory"]},
            "source": "rules"
        },
        "invalid1": {
            "schema_version": 1,
            "intent_id": "intent-001",
            "kind": "invalid_kind", # invalid enum
            "action": "fs.create_folder",
            "confidence": 0.95,
            "entities": {},
            "risk": {"level": "medium", "reasons": []},
            "source": "rules"
        },
        "invalid2": {
            "schema_version": 1,
            "intent_id": "intent-001",
            "kind": "command",
            "action": "fs.create_folder",
            "confidence": 1.5, # confidence > 1
            "entities": {},
            "risk": {"level": "medium", "reasons": []},
            "source": "rules"
        }
    },
    "ToolCall": {
        "valid1": {
            "schema_version": 1,
            "tool_id": "fs.create_folder",
            "args": {"path": "C:\\Users\\ASUS\\Documents\\Jarvis\\test"}
        },
        "invalid1": {
            "schema_version": 1,
            "tool_id": 123, # bad type
            "args": {}
        },
        "invalid2": {
            "schema_version": 1,
            "tool_id": "fs.create_folder",
            "args": {},
            "unknown": True
        }
    },
    "VerificationPlan": {
        "valid1": {
            "schema_version": 1,
            "checks": [{"type": "dir_exists", "params": {"path": "C:\\test"}}],
            "watch_scopes": ["C:\\test"],
            "window_seconds": 30
        },
        "invalid1": {
            "schema_version": 1,
            "checks": [{"type": "unknown_check", "params": {}}], # bad enum
            "watch_scopes": [],
            "window_seconds": 30
        },
        "invalid2": {
            "schema_version": 1,
            "checks": [],
            "watch_scopes": [],
            "window_seconds": "30" # bad type
        }
    },
    "PlanStep": {
        "valid1": {
            "schema_version": 1,
            "step_id": "step-001",
            "order": 1,
            "description": "Create test folder",
            "depends_on": [],
            "tool_call": {
                "schema_version": 1,
                "tool_id": "fs.create_folder",
                "args": {"path": "C:\\test"}
            },
            "verification_plan": {
                "schema_version": 1,
                "checks": [{"type": "dir_exists", "params": {"path": "C:\\test"}}],
                "watch_scopes": ["C:\\test"],
                "window_seconds": 30
            },
            "advisory_risk": "medium"
        },
        "invalid1": {
            "schema_version": 1,
            "step_id": "step-001",
            "order": "1", # bad type
            "description": "Create test folder",
            "depends_on": [],
            "tool_call": {
                "schema_version": 1,
                "tool_id": "fs.create_folder",
                "args": {"path": "C:\\test"}
            },
            "verification_plan": {
                "schema_version": 1,
                "checks": [],
                "watch_scopes": [],
                "window_seconds": 30
            },
            "advisory_risk": "medium"
        },
        "invalid2": {
            "schema_version": 1,
            "step_id": "step-001",
            "order": 1,
            "description": "Create test folder",
            "depends_on": [],
            "tool_call": {
                "schema_version": 1,
                "tool_id": "fs.create_folder",
                "args": {"path": "C:\\test"}
            },
            "verification_plan": {
                "schema_version": 1,
                "checks": [],
                "watch_scopes": [],
                "window_seconds": 30
            },
            "advisory_risk": "extreme" # bad enum
        }
    },
    "PlanResponse": {
        "valid1": {
            "schema_version": 1,
            "trace_id": "trace-001",
            "status": "ok",
            "intent": {
                "schema_version": 1,
                "intent_id": "intent-001",
                "kind": "command",
                "action": "fs.create_folder",
                "confidence": 0.9,
                "entities": {},
                "risk": {"level": "medium", "reasons": []},
                "source": "rules"
            },
            "steps": [],
            "message": "Plan generated successfully"
        },
        "invalid1": {
            "schema_version": 1,
            "trace_id": "trace-001",
            "status": "invalid_status", # bad enum
            "intent": {
                "schema_version": 1,
                "intent_id": "intent-001",
                "kind": "command",
                "action": "fs.create_folder",
                "confidence": 0.9,
                "entities": {},
                "risk": {"level": "medium", "reasons": []},
                "source": "rules"
            },
            "steps": [],
            "message": "msg"
        },
        "invalid2": {
            "schema_version": 1,
            "trace_id": "trace-001",
            "status": "ok",
            "intent": {
                "schema_version": 1,
                "intent_id": "intent-001",
                "kind": "command",
                "action": "fs.create_folder",
                "confidence": 0.9,
                "entities": {},
                "risk": {"level": "medium", "reasons": []},
                "source": "rules"
            },
            "steps": "not_an_array", # bad type
            "message": "msg"
        }
    },
    "AuthorizationRecord": {
        "valid1": {
            "schema_version": 1,
            "auth_id": "auth-001",
            "task_id": "task-001",
            "step_id": "step-001",
            "tool_id": "fs.create_folder",
            "canonical_args": {"path": "C:\\test"},
            "expected": {"exists": True},
            "allowed_effects": ["create:C:\\test"],
            "watch_scopes": ["C:\\test"],
            "risk": "medium",
            "approved_by": "user",
            "issued_at": "2026-10-08T10:00:00Z",
            "expires_at": "2026-10-08T10:05:00Z",
            "nonce": "nonce-12345",
            "hmac": "abc123def456"
        },
        "invalid1": {
            "schema_version": 1,
            "auth_id": "auth-001",
            "task_id": "task-001",
            "step_id": "step-001",
            "tool_id": "fs.create_folder",
            "canonical_args": {"path": "C:\\test"},
            "expected": {"exists": True},
            "allowed_effects": ["create:C:\\test"],
            "watch_scopes": ["C:\\test"],
            "risk": "medium",
            "approved_by": "nobody", # bad enum
            "issued_at": "2026-10-08T10:00:00Z",
            "expires_at": "2026-10-08T10:05:00Z",
            "nonce": "nonce-12345",
            "hmac": "abc123def456"
        },
        "invalid2": {
            "schema_version": 1,
            "auth_id": "auth-001",
            "task_id": "task-001",
            "step_id": "step-001",
            "tool_id": "fs.create_folder",
            "canonical_args": {"path": "C:\\test"},
            "expected": {"exists": True},
            "allowed_effects": ["create:C:\\test"],
            "watch_scopes": ["C:\\test"],
            "risk": "medium",
            "approved_by": "user",
            "issued_at": "2026-10-08T10:00:00Z",
            "expires_at": "2026-10-08T10:05:00Z",
            "nonce": "nonce-12345"
            # missing required field 'hmac'
        }
    },
    "ExecutionReceipt": {
        "valid1": {
            "schema_version": 1,
            "step_id": "step-001",
            "auth_id": "auth-001",
            "started_at": "2026-10-08T10:00:01Z",
            "completed_at": "2026-10-08T10:00:02Z",
            "exit_code": 0,
            "reported_success": True,
            "stdout": "",
            "stderr": ""
        },
        "invalid1": {
            "schema_version": 1,
            "step_id": "step-001",
            "auth_id": "auth-001",
            "started_at": "2026-10-08T10:00:01Z",
            "completed_at": "2026-10-08T10:00:02Z",
            "exit_code": "0", # bad type
            "reported_success": True,
            "stdout": "",
            "stderr": ""
        },
        "invalid2": {
            "schema_version": 1,
            "step_id": "step-001",
            "auth_id": "auth-001",
            "started_at": "2026-10-08T10:00:01Z",
            "completed_at": "2026-10-08T10:00:02Z",
            "exit_code": 0,
            "reported_success": True,
            "stdout": "",
            "stderr": "",
            "unauthorized": "field" # additional property
        }
    },
    "Evidence": {
        "valid1": {
            "schema_version": 1,
            "evidence_id": "ev-001",
            "step_id": "step-001",
            "check": {"type": "dir_exists", "params": {"path": "C:\\test"}},
            "collector": "FileCollector",
            "collected_at": "2026-10-08T10:00:03Z",
            "observed": {"exists": True},
            "expected": {"exists": True},
            "match": True,
            "nonce": "nonce-12345",
            "record_sha256": "sha256hash"
        },
        "invalid1": {
            "schema_version": 1,
            "evidence_id": "ev-001",
            "step_id": "step-001",
            "check": {"type": "dir_exists", "params": {"path": "C:\\test"}},
            "collector": "FileCollector",
            "collected_at": "2026-10-08T10:00:03Z",
            "observed": {"exists": True},
            "expected": {"exists": True},
            "match": "true", # bad type (string instead of bool)
            "nonce": "nonce-12345",
            "record_sha256": "sha256hash"
        },
        "invalid2": {
            "schema_version": 1,
            "evidence_id": "ev-001",
            "step_id": "step-001",
            "check": {"type": "dir_exists", "params": {"path": "C:\\test"}},
            "collector": "FileCollector",
            "collected_at": "2026-10-08T10:00:03Z",
            "observed": {"exists": True},
            "expected": {"exists": True},
            "match": True,
            "nonce": "nonce-12345"
            # missing record_sha256
        }
    },
    "VerificationResult": {
        "valid1": {
            "schema_version": 1,
            "step_id": "step-001",
            "status": "VERIFIED",
            "stages": [{"stage": 1, "name": "Trigger validation", "passed": True, "detail": "OK"}],
            "evidence": [],
            "side_effects": [],
            "reasons": ["All checks passed"],
            "duration_ms": 45.2
        },
        "invalid1": {
            "schema_version": 1,
            "step_id": "step-001",
            "status": "SUCCESS", # bad enum (must be VERIFIED, FAILED, ESCALATE, PARTIAL)
            "stages": [],
            "evidence": [],
            "side_effects": [],
            "reasons": [],
            "duration_ms": 10
        },
        "invalid2": {
            "schema_version": 1,
            "step_id": "step-001",
            "status": "VERIFIED",
            "stages": [],
            "evidence": [],
            "side_effects": [],
            "reasons": [],
            "duration_ms": "fast" # bad type
        }
    },
    "ReportRequest": {
        "valid1": {
            "schema_version": 1,
            "session_id": "sess-001",
            "task_id": "task-001",
            "steps": [{"step_id": "step-001", "status": "completed", "verification_status": "VERIFIED", "summary": "Created folder"}]
        },
        "invalid1": {
            "schema_version": 1,
            "session_id": "sess-001",
            "task_id": "task-001",
            "steps": [{"step_id": "step-001", "status": "completed", "verification_status": "VERIFIED"}] # missing summary
        },
        "invalid2": {
            "schema_version": 1,
            "session_id": "sess-001",
            "task_id": "task-001",
            "steps": [],
            "extra": 123
        }
    },
    "ReportResponse": {
        "valid1": {
            "schema_version": 1,
            "message": "Task completed successfully",
            "memory_updated": True
        },
        "invalid1": {
            "schema_version": 1,
            "message": "Task completed successfully",
            "memory_updated": "yes" # bad type
        },
        "invalid2": {
            "schema_version": 1,
            "message": "Task completed successfully"
            # missing memory_updated
        }
    }
}

for schema_name, data in fixtures.items():
    for name, content in data.items():
        filename = f"{schema_name}.{name}.json"
        path = os.path.join(examples_dir, filename)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(content, f, indent=2)

print("Generated all fixture files successfully.")
