# ADR-0005: Resolve Packaged Notepad

- Windows 11 `notepad.exe` can be an alias that activates a packaged application.
- The resulting process image is `Notepad\Notepad.exe` inside WindowsApps.
- Package resolution is limited to app id `notepad` and executable `notepad.exe`.
- Only exact `Microsoft.WindowsNotepad_<version>_<arch>__<publisher>` package names qualify.
- The only accepted package executable is `Notepad\Notepad.exe`.
- The WindowsApps root is injectable for deterministic tests and defaults to `%ProgramFiles%\WindowsApps`.
- The highest package version matching the current process architecture is preferred.
- System32 resolution remains the fallback when no eligible package is available.
- WindowsApps is protected by TrustedInstaller ACLs, limiting package replacement by ordinary users.
- Residual risk: administrators or compromised servicing can alter the trusted package or its registry metadata.
