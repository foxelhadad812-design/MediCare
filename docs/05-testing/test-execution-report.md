# MediCare — Automated Test Execution Report

## 1. Recorded Test Run

The October 7, 2026 figures below are historical and unreconciled. A fresh run was performed against the current source on October 11, 2026.

### Current verification — October 11, 2026

| Run | Result | Notes |
|---|---:|---|
| Release build | Passed | 0 warnings, 0 errors |
| Isolated tests (`Category!=Integration`) | **503 passed, 0 failed, 0 skipped** | Result file: `TestResults/medicare-unit-final-2026-10-11.trx` |
| `CoreClinicalFlowsIntegrationTests` | **8 passed, 0 failed** | Result file: `TestResults/medicare-web-integration-final-2026-10-11.trx`. The test host now uses ephemeral Data Protection keys instead of a protected user-profile folder. |
| SQL Server / LocalDB integration | Not verified | LocalDB could not create an automatic instance in this environment. Run on a machine with SQL Server LocalDB or a configured `MEDICARE_TEST_CONNECTION_STRING`. |
| Azure deployment | **Not deployed** | [Latest public Azure workflow run](https://github.com/foxelhadad812-design/MediCare/actions/runs/38074994561) built and published successfully, but the deploy step failed because no Azure credentials were available. The configured fallback host `medicare-depi-app.azurewebsites.net` also did not resolve. See deployment issue in the bug log. |

The unit run includes two tests for Responses API structured-output parsing and provider-error fallback. No API key was used, so a live OpenAI request was not verified. Do not cite the historical concurrency result below as a current run.

### Historical record — October 7, 2026

The original report stated **131 passed, 0 failed, 0 skipped**, but its detailed suite counts did not reconcile and no result file accompanied it. It is retained only as an unverified historical claim.

| Metric | Result | Status |
|---|---|---|
| Total and passed tests | 131 stated in the original summary | Historical only; breakdown does not reconcile |
| Failed and skipped tests | 0 stated in the original summary | Historical record only |
| Execution time | 4.97 seconds stated in the original summary | Historical record only |
| Build warnings and errors | 0 stated in the original summary | Historical record only |

---

## 2. Test Areas Listed in the Original Report

| Test suite | Area covered |
|---|---|
| `AdminServiceTests` | Admin metrics, approvals, CSV export |
| `AppointmentServiceTests` | Booking lifecycle, cancellation, conflicts |
| `AuthServiceTests` | Registration, roles, login validation |
| `ChatbotServiceTests` | Keyword matching and fallback responses |
| `ClinicClockTests` | Clinic time handling |
| `DoctorServiceTests` | Directory filters and doctor details |
| `FileStorageServiceTests` | Attachment validation and safe storage |
| `MedicalRecordServiceTests` | Encounters, prescriptions, attachments |
| `NotificationServiceTests` | In-app notifications and SignalR |
| `PaymentServiceTests` | Simulated checkout and payment state |
| `PrescriptionServiceTests` | Prescription access and ownership |
| `SlotEngineServiceTests` | Slot generation, leaves, and past slots |
| `ValidationTests` | FluentValidation rules |
| Controller tests | MVC and API endpoint behavior |
| Repository/integration tests | SQL Server queries, indexes, and booking conflicts |

## 3. Concurrency Stress Test Verification (KPI-01)
* **Target Slot:** Tuesday, 2026-11-17 at 10:00:00 (Doctor ID: 1)
* **Thread Count:** 10 asynchronous concurrent tasks firing simultaneously
* **Historical claimed database result (not re-run on October 11, 2026):**
  - Exactly **1** thread returned `Result.Success` (HTTP 200/201).
  - Exactly **9** threads returned `Result.Failure` with error message indicating slot conflict (HTTP 409).
  - Database table `Appointments` contained exactly **1** record for Doctor 1 at that date/time.
  - Zero deadlocks or orphan records detected.
  - **KPI-01 Compliance:** **0.0% Double-Booking Confirmed.**

---

## 4. Database Filtered Index Verification
* **Index Name:** `IX_Appointments_Doctor_NoOverlap`
* **Columns:** `(DoctorId, AppointmentDate, StartTime)`
* **Filter Expression:** `[Status] <> 3 AND [Status] <> 4` (`Status != Cancelled AND Status != Rejected`)
* **Rebooking Verification:**
  - Appointment cancelled -> status updated to `3`.
  - Same patient or new patient books the identical slot -> booking succeeds without constraint violation.
  - Slot rebooking capability verified under real SQL Server engine.
