from jarvis_ai.schemas.models import ReportRequestModel, ReportResponseModel


class ResponseGenerator:

    def generate_report_response(self, request: ReportRequestModel) -> ReportResponseModel:
        all_verified = True
        failed_steps: list[str] = []
        verified_steps: list[str] = []

        for step in request.steps:
            if step.verification_status == "VERIFIED":
                verified_steps.append(step.step_id)
            else:
                all_verified = False
                failed_steps.append(f"{step.step_id} ({step.verification_status})")

        if all_verified and len(request.steps) > 0:
            msg = f"Task '{request.task_id}' completed and verified successfully across {len(verified_steps)} step(s)."
        elif not all_verified:
            msg = f"Task '{request.task_id}' failed verification. Unverified/Failed steps: {', '.join(failed_steps)}."
        else:
            msg = f"Task '{request.task_id}' processed with no steps."

        return ReportResponseModel(
            message=msg,
            memory_updated=True,
        )
