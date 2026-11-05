# Security policy

## Threat model summary

Jarvis is a local-first Windows assistant. Its planner emits validated tool IDs and arguments, policy and user approval govern execution, each execution requires a signed authorization, and an independent verifier evaluates operating-system evidence. The AI server and Ollama are expected to run locally; Jarvis does not intentionally send tasks to cloud services.

The design is not a security boundary against a hostile process running as the same Windows user. Filesystem snapshots are non-recursive and have documented coverage limits; reminder payloads and scheduled tasks can be changed by same-user processes after verification. Review [docs/threat-model.md](docs/threat-model.md) and [docs/DESIGN.md](docs/DESIGN.md) for details.

## Reporting a vulnerability

Please do not publish exploit details, credentials, personal data, or sensitive logs in a public issue. Use GitHub's private vulnerability reporting for this repository when available. Otherwise, contact the repository maintainers privately through GitHub and include:

- affected version or commit and Windows/.NET environment;
- concise impact and reproduction conditions, avoiding real user files or scheduled tasks;
- any proposed mitigation.

Do not include secrets or unrelated user data. We will acknowledge reports and coordinate disclosure after a fix or mitigation is available.
