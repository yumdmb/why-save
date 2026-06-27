# Contributing

Thanks for considering a contribution to Why Save.

## Before You Start

- Search existing issues and pull requests to avoid duplicate work.
- For larger changes, open an issue first so the design can be discussed.
- Keep contributions focused. Small, reviewable pull requests are easier to merge.

## Development Setup

Requirements:

- Windows
- .NET 8 SDK
- WiX v4, only if you are building the MSI

Build and test:

```powershell
dotnet build WhySave.sln
dotnet test WhySave.sln
```

Build the MSI:

```powershell
dotnet tool install --global wix --version 4.*
powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1
```

## Pull Request Guidelines

- Include tests for behavior changes where practical.
- Keep privacy-sensitive behavior local-first by default.
- Do not add telemetry, analytics, or outbound data flow without explicit discussion.
- Avoid logging decrypted reason text, notes, full URLs, encryption keys, or other sensitive user data.
- Update documentation when user-facing behavior changes.
- Make sure `dotnet test WhySave.sln` passes before requesting review.

## Code Style

- Follow the existing C# and WPF patterns in the repository.
- Prefer small, explicit services over broad abstractions.
- Keep UI changes consistent with the existing Windows tray app experience.
- Keep storage and crypto changes conservative and well tested.
