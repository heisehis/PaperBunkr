# Installer polish — implementation plan

Implements: `2026-10-24-installer-polish-design.md` (approved in chat).

1. Add `installer/InstallerBuild.ps1` for project-derived version validation and guarded build
   output paths; call it from `BuildInstaller.ps1`. Require coherent explicit ISS version inputs.
   Verify version derivation, override failures, path guards and PowerShell parsing.
2. Refine `installer/Installer.iss` branding copy and option descriptions, retain existing assets,
   and condition thumbnail task/registration on the actual payload. Compile both payload variants.
3. Add shared Pascal helpers in `installer/InstallerLogic.iss` for numeric release ordering,
   quick-upgrade page selection and bounded profile cleanup. Wire named maintenance dialogs,
   silent rejection, fallback version detection and confirmed uninstall deletion in `Installer.iss`.
   Verify actual Pascal helpers with an install-free native Inno harness.
4. Add `installer/Tests/VerifyInstaller.ps1` and its harness. Compile production installer and
   run focused helper tests without changing the host's installation or registry. Use the
   Avalonia deployment skill for packaging and applicable review-checklist items for accessibility.
5. Review the complete diff and update the roadmap/spec with checks actually performed.
   Leave real wizard, registry integration and destructive uninstall checks explicitly pending
   unless a disposable Windows environment is available.

No app build/test runs in parallel. Installer tests use temporary output directories and never
install the payload or launch the app. Existing staged app changes remain outside this work.
