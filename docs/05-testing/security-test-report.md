# MediCare — Security Assessment & Penetration Audit Report

## 1. Security Overview & Methodology
The security verification of the MediCare Clinic Management System was executed using a combination of static code analysis, automated penetration unit/integration tests, and manual parameter tampering simulations following the **OWASP Top 10 (2021)** and **CWE (Common Weakness Enumeration)** standards.

---

## 2. OWASP Top 10 Vulnerability Matrix

| OWASP Category | Vulnerability Evaluated | MediCare Countermeasure Implemented | Verification Result |
|---|---|---|---|
| **A01: Broken Access Control** | IDOR on Medical Records, Prescriptions, Appointments; Privilege Escalation | Explicit server-side ownership validation comparing entity owner IDs against `User.FindFirstValue(ClaimTypes.NameIdentifier)`. Role guards on Admin/Doctor/Patient controllers. | **PASSED (Zero IDOR)** |
| **A02: Cryptographic Failures** | Plaintext credentials, insecure transport, weak hashing | ASP.NET Core Identity PBKDF2 with HMAC-SHA256 and salted hashes; mandatory HTTPS redirection; `HSTS` enabled in production. | **PASSED (Compliant)** |
| **A03: Injection** | SQL Injection, CSV Formula Injection (CWE-1236) | EF Core LINQ parameterized SQL generation throughout; leading formula character escaping (`'`, `=`, `+`, `-`, `@`, `\t`) in CSV export. | **PASSED (Neutralized)** |
| **A04: Insecure Design** | Double booking race condition, premature visit completion | SQL Server filtered unique index `IX_Appointments_Doctor_NoOverlap`; state machine invariant restricting `Completed` status exclusively to clinical encounter creation. | **PASSED (Resilient)** |
| **A05: Security Misconfiguration** | Verbose error leaks, missing security headers, insecure cookies | Exception handler middleware for production; cookies configured with `HttpOnly = true`, `SameSite = Lax`, 24-hour sliding expiration. | **PASSED (Hardened)** |
| **A06: Vulnerable & Outdated Components** | Unpatched third-party packages | All NuGet packages targeting latest .NET 8 compatible versions (EF Core 8.0, Identity 8.0, MailKit 4.8, xUnit 2.9). | **PASSED (Clean)** |
| **A07: Identification & Auth Failures** | Credential stuffing, weak passwords | Strict Identity password policy (≥ 8 chars, uppercase, lowercase, digit, non-alphanumeric); unique email constraint; brute-force protection hooks. | **PASSED (Enforced)** |
| **A08: Software & Data Integrity Failures** | Malicious file uploads (CWE-434), script injection | Strict file extension allow-list (`.jpg`, `.jpeg`, `.png`, `.pdf`), 5 MB limit, server-side GUID renaming, content-disposition validation. | **PASSED (Protected)** |
| **A09: Security Logging & Monitoring Failures** | Silent access breaches, undetected tampering | Detailed security event logging (`_logger.LogWarning("Security IDOR: User {UserId} attempted unauthorized access...")`) on every authorization failure. | **PASSED (Monitored)** |
| **A10: Server-Side Request Forgery (SSRF)** | Arbitrary outbound requests | All internal service communications strictly bounded; no user-controllable URLs passed to HTTP clients. | **PASSED (N/A / Safe)** |

---

## 3. IDOR (Insecure Direct Object Reference) Verification Matrix

Every entity accessible via an ID route parameter was evaluated against cross-user tampering:

| Endpoint | Target Resource | Legitimate Owner | Unauthorized Attacker Role | Tampering Vector | Server Response | Logged Audit | Status |
|---|---|---|---|---|---|---|---|
| `/MedicalRecords/Details/{id}` | Clinical Record #42 | Patient A (ID: 10) | Patient B (ID: 11) | Change URL ID to 42 | **HTTP 403 Forbidden** | `Security IDOR: User {UserId} attempted unauthorized access...` | **PASSED** |
| `/MedicalRecords/Details/{id}` | Clinical Record #42 | Doctor A (Attending) | Doctor B (Unrelated) | Change URL ID to 42 | **HTTP 403 Forbidden** | Logged as cross-doctor IDOR attempt | **PASSED** |
| `/MedicalRecords/DownloadAttachment/{id}` | Diagnostic PDF | Patient A (Owner) | Patient B (Attacker) | Change URL ID to 42 | **HTTP 403 Forbidden** | Access blocked before file disk read | **PASSED** |
| `/Prescriptions/Print/{id}` | Prescription #50 | Patient A (Recipient) | Patient B (Attacker) | Change URL ID to 50 | **HTTP 403 Forbidden** | Logged as IDOR attempt | **PASSED** |
| `/Prescriptions/Print/{id}` | Prescription #50 | Doctor A (Issuer) | Doctor B (Unrelated) | Change URL ID to 50 | **HTTP 403 Forbidden** | Logged as IDOR attempt | **PASSED** |
| `/Appointments/Details/{id}` | Appointment #88 | Patient A | Patient B | Change URL ID to 88 | **HTTP 403 Forbidden** | Logged as unauthorized appointment access | **PASSED** |
| `/Appointments/Cancel/{id}` | Appointment #88 | Patient A | Patient B | POST form tampering | **HTTP 403 Forbidden** | Rejection before state transition | **PASSED** |
| `/Doctor/Appointments` | Schedule Feed | Doctor A | Doctor B | Route parameter tamper | **Strict Claims Binding** | Queries strictly by authenticated user's Doctor ID | **PASSED** |
| `/Admin/ApproveDoctor/{id}` | Syndicate Approval | Admin | Doctor or Patient | Direct POST request | **Redirect to AccessDenied** | Blocked at MVC Authorization Filter | **PASSED** |

---

## 4. Cross-Site Request Forgery (CSRF) Audit

All state-changing MVC actions were inspected for anti-CSRF token verification:

* `AccountController`: `[ValidateAntiForgeryToken]` on `Login`, `RegisterPatient`, `RegisterDoctor`, `Logout`.
* `AdminController`: `[ValidateAntiForgeryToken]` on `ApproveDoctor`, `RejectDoctor`.
* `DoctorController`: `[ValidateAntiForgeryToken]` on `UpdateProfile`, `SetSchedule`, `AddLeave`, `CancelLeave`.
* `AppointmentsController`: `[ValidateAntiForgeryToken]` on `Book`, `Cancel`, `ConfirmAppointment`, `RejectAppointment`.
* `MedicalRecordsController`: `[ValidateAntiForgeryToken]` on `Create` (Encounter submission).

**Audit Result:** 100% of state-changing `POST` endpoints enforce Anti-Forgery tokens. Malicious cross-site form submissions receive HTTP 400 Bad Request.

---

## 5. CSV Formula Injection (CWE-1236) Defense
Spreadsheet applications (Microsoft Excel, LibreOffice Calc) interpret cell strings beginning with `=`, `+`, `-`, `@`, or `\t` as executable formulas, creating Remote Code Execution (RCE) or data exfiltration risks for administrators opening exported reports.

### Implementation Defense
In `AdminService.EscapeCsv`:
```csharp
private static string EscapeCsv(string? value)
{
    if (string.IsNullOrEmpty(value)) return "";

    // Mitigate CSV Formula Injection (CWE-1236)
    if (value.StartsWith("=") || value.StartsWith("+") || value.StartsWith("-") || value.StartsWith("@") || value.StartsWith("\t"))
    {
        value = "'" + value;
    }

    if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
    return value;
}
```

### Verification Test
Automated unit test `ExportAppointmentsCsvAsync_NeutralizesFormulaInjectionCharacters` verified that malicious payloads such as `=cmd|' /C calc'!A0` and `@AttackerDoc` are safely prefixed with a single quote (`'`), neutralizing spreadsheet formula execution.

---

## 6. File Upload & Path Traversal Security (CWE-434, CWE-22)
1. **Extension Whitelisting:** Permitted extensions are strictly limited to `.jpg`, `.jpeg`, `.png`, and `.pdf`. Executable extensions (`.exe`, `.dll`, `.bat`, `.sh`, `.php`, `.asp`, `.aspx`) are rejected.
2. **Size Enforcement:** Maximum allowable upload size is capped at 5 MB (`5 * 1024 * 1024` bytes).
3. **Randomized Renaming:** Uploaded files are immediately renamed to cryptographically random GUIDs (`Guid.NewGuid().ToString("N") + extension`), preventing attackers from overwriting existing system files or executing known filenames.
4. **Path Traversal Guard:** The file storage service combines the sanitized GUID filename with the absolute `WebRootPath` directory using `Path.Combine`, preventing `../` directory navigation.
