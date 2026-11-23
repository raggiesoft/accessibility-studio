# Headless CLI (`a11y-studio-cli`) & Enterprise MSI / Group Policy Guide

## 1. Headless CI/CD Audits (`a11y-studio-cli`)

`a11y-studio-cli` runs identically on Windows 11 (`x64` / `ARM64`), Linux build agents, and macOS runners:

```bash
# Scan a compiled static site or web repository and fail CI on Critical/Serious violations
a11y-studio-cli scan ./dist \
  --profile Wcag22AA_Section508 \
  --fail-on Serious \
  --sarif audit-results.sarif \
  --html accessibility-report.html \
  --csv findings.csv \
  --json snapshot.json

# Export the standalone offline HTML5 Help manual bundle
a11y-studio-cli export-help ./Help
```

### Exit Codes

- **`0`**: Audit passed with zero violations at or above `--fail-on`.
- **`1`**: Invalid CLI arguments or target path not found.
- **`2`**: Audit detected blocking WCAG 2.2 / Section 508 violations.

---

## 2. Silent Enterprise MSI Installation & Group Policy

When deployed via our Windows Installer (`.msi`) package, IT administrators can set fleet-wide policy defaults silently:

```bat
msiexec /i raggiesoft-accessibility-studio-setup.msi /qn ^
  ADD_CLI_TO_PATH=1 ^
  COMPLIANCE_PROFILE="Wcag22AA_Section508" ^
  BATCH_THROTTLE_MS="1500"
```

### Registry Policy Keys

1. **Enforced Machine Policy (Highest Precedence):**
   `HKLM\Software\Policies\RaggieSoft\AccessibilityStudio`
2. **Per-User Preference Fallback:**
   `HKCU\Software\RaggieSoft\AccessibilityStudio`

Supported values:
- `ComplianceProfile` (`REG_SZ`): `Wcag22AA_Section508`, `Wcag22LevelA`, `AdaTitleIIWeb`, `StrictEnterpriseAll`
- `BatchThrottleMs` (`REG_DWORD`): `0`–`30000`
