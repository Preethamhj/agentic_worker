# ADR-0005: Resolve Packaged Notepad

- Windows 11 `notepad.exe` can be an alias that activates a packaged application.
- The resulting process image is `Notepad\Notepad.exe` inside WindowsApps.
- Package resolution is limited to app id `notepad` and executable `notepad.exe`.
- Only exact `Microsoft.WindowsNotepad_<version>_<arch>__8wekyb3d8bbwe` package names qualify.
- The only accepted package executable is `Notepad\Notepad.exe`.
- The WindowsApps root is injectable for deterministic tests and defaults to `%ProgramFiles%\WindowsApps`.
- The highest package version matching the current process architecture is preferred.
- System32 resolution remains the fallback when no eligible package is available.
- `PackageRootFolder` in HKCU is user-writable and is treated only as untrusted metadata; the candidate is reconstructed beneath WindowsApps and must pass containment, exact-layout, publisher, traversal, and reparse-point checks.
- An invalid or unavailable package candidate falls back to System32.
- Residual risk: WindowsApps is protected by TrustedInstaller ACLs, but administrators or compromised servicing can alter trusted package files.
