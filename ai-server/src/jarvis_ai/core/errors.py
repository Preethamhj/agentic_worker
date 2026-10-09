class JarvisError(Exception):
    """Base exception class for Jarvis AI server errors."""

    def __init__(self, message: str, code: str = "internal_error") -> None:
        super().__init__(message)
        self.message = message
        self.code = code


class StructuredOutputError(JarvisError):
    """Raised when LLM structured output validation fails after retries."""

    def __init__(self, message: str, raw_output: str | None = None) -> None:
        super().__init__(message, code="structured_output_error")
        self.raw_output = raw_output


class RegistryError(JarvisError):
    """Raised when tool registry validation fails or tool is invalid."""

    def __init__(self, message: str) -> None:
        super().__init__(message, code="registry_error")


class AuthError(JarvisError):
    """Raised when authorization checks fail."""

    def __init__(self, message: str) -> None:
        super().__init__(message, code="auth_error")


class MemoryError(JarvisError):
    """Raised when session memory store operations fail."""

    def __init__(self, message: str) -> None:
        super().__init__(message, code="memory_error")


class NluError(JarvisError):
    """Raised when NLU parsing fails."""

    def __init__(self, message: str) -> None:
        super().__init__(message, code="nlu_error")


class PlanningError(JarvisError):
    """Raised when plan generation fails."""

    def __init__(self, message: str) -> None:
        super().__init__(message, code="planning_error")
