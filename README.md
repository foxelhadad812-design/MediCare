# MediCare — Clinic Management & Appointment System

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12-239120?logo=csharp)
![EF Core](https://img.shields.io/badge/EF%20Core-8.0-512BD4)
![SQL Server](https://img.shields.io/badge/SQL%20Server-2022-CC292B?logo=microsoftsqlserver)
![Tests](https://img.shields.io/badge/tests-275%20passed%20%7C%200%20failed-brightgreen)
![DEPI Compliant](https://img.shields.io/badge/DEPI-100%25%20Audited%20%26%20Compliant-blue)
![Architecture](https://img.shields.io/badge/Architecture-3--Tier%20N--Tier-orange)

## Project Overview
**MediCare** is an enterprise-grade Clinic Management and Appointment System developed as a graduation project for the **Digital Egypt Pioneers Initiative (DEPI) - .NET Full Stack Track** under the auspices of the Ministry of Communications and Information Technology (MCIT).

The system streamlines Egyptian outpatient clinic operations by offering dynamic, conflict-free appointment scheduling with SQL Server concurrency protection, role-based portals for patients, clinicians, clinic administrators, and licensed pharmacists, real-time push notifications, digital prescription issuance with standardized print views, offline-safe server-side QR verification, clinical history and allergy tracking, automated 24-hour background reminders, and multi-format administrative analytics.

---

## Technical Stack
- **Framework & Runtime:** ASP.NET Core MVC (.NET 8 LTS, C# 12)
- **Data Access & ORM:** Entity Framework Core 8, Microsoft SQL Server 2022 / LocalDB
- **Authentication & Security:** ASP.NET Core Identity 8, Role-Based Access Control, Anti-CSRF (`[ValidateAntiForgeryToken]`), Per-IP Rate Limiting, BOLA/IDOR Defense, CSV Formula Injection Mitigation (CWE-1236), OWASP Security Headers & CSP Report-Only
- **Digital Prescriptions & Pharmacy:** Server-side QR Code Generation (`QRCoder`), Optimistic Concurrency Tokens (`WHERE IsDispensed = 0`), Dedicated Pharmacist Portal
- **Real-Time Communication:** ASP.NET Core SignalR (Strongly-Typed Hubs)
- **Background Processing:** Hosted Background Services (`BackgroundService`, `IServiceScopeFactory`)
- **Email & Messaging:** MailKit (SMTP via `IEmailService`), SendGrid adapter, Twilio SMS adapter, Mock SMS (`ISmsService`)
- **Frontend & UI:** Bootstrap 5, FullCalendar 6, Chart.js, Leaflet, Bootstrap Icons, Self-Hosted Assets (`wwwroot/lib/`), CSS Print Media Queries
- **Validation & Testing:** FluentValidation, xUnit, Moq, FluentAssertions, SQL Server LocalDB Concurrency Integration Tests
- **Reporting:** Multi-format exports (Sanitized CSV, Native OpenXML `.xlsx` Excel, Standard PDF)
- **CI/CD & Hosting:** GitHub Actions, Microsoft Azure App Service, Azure SQL Database

---

## Implementation Status across Sprints

### Sprint 1: Foundation Phase
| Component | Status | Architectural Notes |
|---|:---:|---|
| **Solution Architecture** | &#10003; Complete | 3-Project N-Tier (`MediCare.Web` -> `MediCare.Services` -> `MediCare.Data`). Controllers inject services only. |
| **Data Entities & Schema** | &#10003; Complete | 11 domain entities + Identity. Automatic `CreatedAt`/`UpdatedAt` audit timestamps. No soft delete. |
| **Concurrency Safeguards** | &#10003; Complete | SQL Server Filtered Unique Index on `Appointments(DoctorId, AppointmentDate, StartTime) WHERE [Status] IN (1, 2)`. |
| **Database Migrations** | &#10003; Complete | Applied `InitialCreate` and `FinalGapClosureSchema` on SQL Server LocalDB (`(localdb)\mssqllocaldb`). |
| **Identity & Authentication** | &#10003; Complete | Roles (`Admin`, `Doctor`, `Patient`). Patient registration active immediately; Doctor registration requires approval (`IsApproved = false`). |
| **Database Seeder** | &#10003; Complete | `DbInitializer` seeds 1 Admin, 5 Specializations, 5 Doctors across Egyptian governorates with working hours, 5 Patients, past clinical encounters, prescriptions, and upcoming visits. |
| **Doctor Directory** | &#10003; Complete | Public list with search, governorate filtering, specialization, max fee, available day filters, pagination, and detailed doctor schedule profiles. |

### Sprint 2: Booking Engine Phase
| Component | Status | Architectural Notes |
|---|:---:|---|
| **Doctor Schedule Management** | &#10003; Complete | Doctor portal for weekly `WorkingHours` and `DoctorLeaves` with conflict detection warning if active appointments exist. |
| **Slot Calculation Engine** | &#10003; Complete | Pure, deterministic slot calculation engine in `MediCare.Services`. Excludes leaves, existing bookings, enforces 2h lead time, 30d advance booking window, and clinic local time (`Africa/Cairo`). |
| **Interactive Booking Flow** | &#10003; Complete | FullCalendar 6.1 interactive UI, slot selection modal, `AppointmentFactory`, booking review, and conflict pre-checking via `/api/appointments/check-conflict`. |
| **State Machine & Lifecycle** | &#10003; Complete | Full lifecycle transitions (`Pending` -> `Confirmed`/`Rejected`, `Cancelled` with 2h rule, `Completed`, `NoShow`) with ownership enforcement (403 IDOR prevention). |
| **Real-Time Push Notifications** | &#10003; Complete | Strongly-typed SignalR `AppointmentHub` (`IAppointmentNotificationClient`), persist-to-DB first architecture, unread counter badge, bell dropdown, and live toast popups. |

### Sprint 3: Clinical Encounters, Prescriptions & Admin Analytics
| Component | Status | Architectural Notes |
|---|:---:|---|
| **Clinical Encounter Documentation** | &#10003; Complete | Doctor digital encounter chart (`/MedicalRecords/Create/{appointmentId}`) with diagnosis, symptoms, visit notes, and diagnostic file upload (JPG/PNG/PDF &le; 5 MB) stored under `wwwroot/uploads/records/` with GUID safe names. |
| **Completion Rule Invariant** | &#10003; Complete | Appointment transitions to `Completed` **only** upon documenting an encounter for a `Confirmed` appointment whose scheduled start time has elapsed; updates `PaymentStatus = Paid` atomically in a single EF Core transaction. |
| **Itemized Digital Prescriptions** | &#10003; Complete | Prescriptions linked to encounter, doctor, and patient with dynamic multi-medication repeater (Medication, Dosage, Frequency, Duration Days, Instructions). |
| **Standardized Print View** | &#10003; Complete | Dedicated `/Prescriptions/Print/{id}` view with `@media print` CSS rules, clinic branding, doctor license metadata, patient age calculation, Rx body, and physician signature block. |
| **Admin Doctor Approvals** | &#10003; Complete | Admin portal (`/Admin/Approvals`) to review credentials, approve doctors (`IsApproved = true`), or decline with explanatory note; transactional emails sent via MailKit. |
| **Admin Dashboard & Analytics** | &#10003; Complete | Operational metrics ribbon (Total Visits, Active/Pending Doctors, Completed Rate, Paid Revenue, Pending Revenue), Chart.js monthly volume bar chart, and specializations distribution doughnut chart. |
| **RFC 4180 CSV Export** | &#10003; Complete | Full appointments CSV export (`/Admin/ExportAppointmentsCsv`) with UTF-8 BOM preamble for Excel compatibility and CSV Formula Injection mitigation (CWE-1236). |

### Sprint 4: Final Gap Closure & Compliance Hardening
| Component | Status | Architectural Notes |
|---|:---:|---|
| **Patient Profile & Allergies** | &#10003; Complete | Patient portal (`/Account/Profile`) to manage personal information, emergency contacts, recorded drug allergies, and chronic medical history with FluentValidation. |
| **Governorates Location Filter** | &#10003; Complete | Server-side bidirectional English/Arabic governorate filtering in `IDoctorRepository` across all 27 Egyptian governorates. |
| **Atomic Rescheduling** | &#10003; Complete | Atomic self-service appointment rescheduling (`/Appointments/Reschedule/{id}`) enforcing 2-hour lead time, working hours, doctor leaves, patient conflict checks, and DB index isolation. |
| **Admin Patient Management** | &#10003; Complete | Admin patient directory (`/Admin/Patients`) with multi-field search and instant account lockout/unlock management. |
| **Specializations CRUD** | &#10003; Complete | Clinical departments directory (`/Admin/Specializations`) with create, edit, and delete actions guarded against deleting active doctor specialties. |
| **Executive Analytics & Cohorts** | &#10003; Complete | Top 5 performing clinicians league table and patient demographic cohorts (`<18`, `18-35`, `36-50`, `50+`) on Admin dashboard and reports views. |
| **Multi-Format Export Subsystem** | &#10003; Complete | Native Microsoft Excel OpenXML (`.xlsx`) and branded PDF report generation alongside sanitized CSV exports. |
| **24-Hour Reminder Background Worker** | &#10003; Complete | Hosted `AppointmentReminderBackgroundService` scanning confirmed appointments in `[Now + 23h, Now + 25h]`, dispatching Email, SMS, and in-app SignalR alerts with `ReminderSent` flag tracking. |
| **Automated Testing Suite** | &#10003; Complete | **157 passing automated tests** (148 Unit Tests + 9 SQL Server LocalDB Integration Tests) with 0 regressions, 0 warnings, 0 errors. |

---

## Local Development & Setup Guide

### 1. Prerequisites
- **.NET 8 SDK (LTS)**: Check version using `dotnet --version`
- **SQL Server**: Microsoft SQL Server 2022 or SQL Server Express / LocalDB (`MSSQLLocalDB`)
- **Git**

### 2. Clone and Checkout
```bash
git clone https://github.com/foxelhadad812-design/MediCare.git
cd MediCare
git checkout feature/sprint-1-foundation
```

### 3. Environment & Secrets Configuration

MediCare differentiates security posture across runtime environments via standard `ASPNETCORE_ENVIRONMENT` (`Development` vs `Production`).

#### Environment Differentiators:
- **Development Mode (`ASPNETCORE_ENVIRONMENT=Development`):**
  - Cookie security allows `CookieSecurePolicy.SameAsRequest` to facilitate local development over HTTP without cookie drops.
  - Test Pharmacist account (`pharmacist@medicare.com`) is optionally seeded only if `Seed:PharmacistPassword` is provided in user-secrets.
  - Fallback developer passwords (`Seed:DefaultPassword`) are permitted for testing dummy patients and doctors.
- **Production Mode (`ASPNETCORE_ENVIRONMENT=Production`):**
  - Cookies enforce `CookieSecurePolicy.Always` with `Secure`, `HttpOnly`, and `SameSite=Strict/Lax`.
  - Administrator password **must** be provided via environment variable or Azure Key Vault and **strictly enforced to be at least 16 characters long**. Startup fails immediately if missing or weak.
  - Automatic seeding of the Pharmacist account is **completely disabled**. Pharmacist accounts must be provisioned individually by the clinic Administrator via the Admin Panel.
  - HTTP Strict Transport Security (HSTS) is enforced (365 days, preload, subdomains).

#### Managing Secrets via `dotnet user-secrets` (Development):
To ensure zero credentials are committed to source control, configure secrets locally:
```bash
cd src/MediCare.Web
dotnet user-secrets init

# Local database connection string (LocalDB):
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\mssqllocaldb;Database=MediCareDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"

# Configure seeded account passwords:
dotnet user-secrets set "Seed:AdminPassword" "<YourStrongAdminPassword16CharsMin!>"
dotnet user-secrets set "Seed:PharmacistPassword" "<YourStrongPharmacistPassword!>"
dotnet user-secrets set "Seed:DefaultPassword" "<YourSecureDefaultPassword>"

# Optional: Configure trusted reverse proxy IPs (if running behind IIS, Nginx, or Azure Application Gateway):
dotnet user-secrets set "ForwardedHeaders:KnownProxies:0" "127.0.0.1"
```

#### Production Environment Variables:
In containerized or cloud environments (Azure App Service / Docker / Linux), configure via environment variables:
```bash
ConnectionStrings__DefaultConnection="Server=...;Database=...;"
Seed__AdminPassword="<StrongEnterpriseAdminPasswordAtLeast16Chars>"
ForwardedHeaders__KnownProxies__0="<ReverseProxyInternalIP>"
```

---

### 4. Apply Database Migrations
Run the EF Core database update from the repository root:
```bash
dotnet ef database update --project src/MediCare.Data --startup-project src/MediCare.Web
```

---

### 5. Run the Application
```bash
dotnet run --project src/MediCare.Web
```
Open your browser and navigate to the local endpoint (`https://localhost:7001` or `http://localhost:5000`).
The database initializer automatically seeds demo specializations, doctors, and users upon first startup.

#### Emergency CLI Account Recovery Command:
If an administrator or staff account requires an immediate lockout reset directly on the server console (without web UI access):
```bash
dotnet run --project src/MediCare.Web -- --recovery-unlock-user admin@medicare.com
```
This utility resets failed access attempts, clears the lockout timestamp, and exits immediately before starting the web server.

---

### 6. Run Automated Tests

Execute the comprehensive automated test suite (**275 passing tests** across unit, calculation engine, DST timezone, clinical encounter, admin metrics, rate limiting, and SQL Server concurrency suites):

```bash
# Run the entire test suite (including SQL Server LocalDB integration tests)
dotnet test MediCare.sln

# Run unit tests only (isolated in-memory and mock tests)
dotnet test MediCare.sln --filter "Category!=Integration"

# Run SQL Server filtered unique index and concurrency integration tests
dotnet test MediCare.sln --filter "Category=Integration"
```

> **Note on Integration Tests:** The integration tests verify the filtered unique index concurrency guarantees and optimistic concurrency token (`WHERE IsDispensed = 0`) against Microsoft SQL Server LocalDB (`(localdb)\mssqllocaldb;Database=MediCare_IntegrationTests`) or the container configured via `MEDICARE_TEST_CONNECTION_STRING`.

---

## System Roles & Access Control

| Role | Core Responsibilities & Capabilities | Lockout Policy |
|---|---|---|
| **Admin** | Clinic administration, doctor approval workflows, patient status & lockout toggles, pharmacist account creation, executive analytics, and RFC 4180 / Excel / PDF exports. | **Exempt from account lockout** to eliminate Denial of Service (DoS) risks; protected via IP-based rate limiting (5 req/min) and failed login audit warnings. |
| **Doctor** | Schedule & working hours management, leaves calendar, appointment confirmations/rejections, digital encounter charting, itemized digital prescriptions with QR code. | Standard lockout (5 failed attempts locks for 15 minutes). Admin or CLI can unlock. |
| **Patient** | Doctor search by specialty and 27 governorates, interactive booking calendar, atomic rescheduling, medical history/allergy profile, digital prescription view & download. | Standard lockout (5 failed attempts locks for 15 minutes). Admin or CLI can unlock. |
| **Pharmacist** | Verification and atomic dispensing of digital prescriptions via token scanning (`/Prescriptions/Verify`, `/Prescriptions/Dispense`). | Standard lockout (5 failed attempts locks for 15 minutes). Admin or CLI can unlock. |

---

## Pre-Seeded Demo Accounts

Passwords for seeded accounts are populated dynamically from your configured `Seed:AdminPassword` / `Seed:DefaultPassword` / `Seed:PharmacistPassword` user-secrets (or environment variables in production). **Zero hardcoded credentials exist in source code.**

| Account Role | Email Address | Display Name / Clinical Specialty | Provisioning Behavior |
|---|---|---|---|
| **Administrator** | `admin@medicare.com` | System Administrator | Seeded on first boot. Requires &ge; 16 chars in Production. |
| **Pharmacist** | `pharmacist@medicare.com` | Licensed Pharmacist | Seeded in Development only if secret configured. Must be created in Admin Panel in Production. |
| **Doctor** | `ahmed.mahmoud@medicare.com` | Dr. Ahmed Mahmoud (Cardiology) | Seeded with demo clinic schedule across Cairo. |
| **Doctor** | `sara.alsayed@medicare.com` | Dr. Sara Al-Sayed (Dermatology) | Seeded with demo clinic schedule across Giza. |
| **Doctor** | `youssef.nabil@medicare.com` | Dr. Youssef Nabil (Pediatrics) | Seeded with demo clinic schedule across Alexandria. |
| **Doctor** | `mona.mansour@medicare.com` | Dr. Mona Mansour (Orthopedics) | Seeded with demo clinic schedule. |
| **Doctor** | `tarek.ezzat@medicare.com` | Dr. Tarek Ezzat (General Internal Medicine) | Seeded with demo clinic schedule. |
| **Patient** | `khaled.omar@medicare.com` | Khaled Omar | Pre-seeded with encounter & prescription history. |
| **Patient** | `nourhan.ali@medicare.com` | Nourhan Ali | Pre-seeded with upcoming appointment. |
| **Patient** | `mostafa.hassan@medicare.com` | Mostafa Hassan | Pre-seeded patient record. |
| **Patient** | `dina.fathy@medicare.com` | Dina Fathy | Pre-seeded patient record. |
| **Patient** | `mohamed.selim@medicare.com` | Mohamed Selim | Pre-seeded patient record. |

---

## Security Architecture & Engineering Trade-offs

1. **Admin Lockout Exemption vs IP Rate Limiting:**
   - *Rationale:* Enabling Identity account lockout on the sole clinic Administrator exposes the clinic to a trivial Denial-of-Service (DoS) attack where any anonymous attacker could lock the administrator out by submitting 5 wrong passwords.
   - *Defense-in-Depth:* Instead of account-level lockout, the login endpoint enforces a per-IP Fixed Window Rate Limiter (maximum 5 POST attempts per minute per IP). In addition, every failed administrator login triggers a high-severity `SECURITY ALERT` warning log (without logging passwords). In case of an emergency, the CLI switch `--recovery-unlock-user` can restore access on the host.

2. **Self-Hosted Vendor Assets & Data Privacy:**
   - *Privacy Protection:* To prevent leakage of patient health data and prescription verification tokens to third-party endpoints, external services and CDNs have been eliminated:
     - Digital prescription QR codes are generated 100% server-side via `QRCoder` as inline PNG Data URIs (removing `api.qrserver.com`).
     - Chatbot avatar badges render locally generated SVG/HTML initials badges (removing `ui-avatars.com`).
     - Frontend vendor libraries (SignalR 8.0.7, Chart.js 4.4.1, FullCalendar 6.1.15, Leaflet 1.9.4 with local marker icons, Canvas Confetti 1.9.3, Bootstrap Icons 1.11.3) are self-hosted in `wwwroot/lib/`.
     - Typography fonts (Cairo & Inter) are self-hosted in `wwwroot/lib/fonts/` (removing `fonts.googleapis.com` and `fonts.gstatic.com`).
   - *External Network Boundaries:* External HTTP calls are strictly limited to tile loading (`*.tile.openstreetmap.org`) and optional telemedicine rooms (`meet.jit.si`).

3. **Content-Security-Policy (CSP) in Report-Only Mode:**
   - *Policy:* `Content-Security-Policy-Report-Only` and `Permissions-Policy` headers are emitted on every HTTP response.
   - *Trade-off:* Because legacy Razor views contain server-rendered inline JavaScript for dynamic calendar, chart, and modal event bindings, CSP is kept in Report-Only mode during auditing to avoid breaking frontend interactive charts without requiring complete nonce injection or bundle refactoring.

4. **Atomic Concurrency in Prescription Dispensing:**
   - *Protection:* To prevent race conditions and double-dispensing in busy pharmacy settings, `Prescription` employs an optimistic concurrency token (`[ConcurrencyCheck] public bool IsDispensed`).
   - *Database Invariant:* Dispensing queries issue an atomic `UPDATE ... SET IsDispensed = 1 WHERE Id = @id AND IsDispensed = 0`. Competing parallel requests result in 0 rows affected, throwing a `DbUpdateConcurrencyException` and guaranteeing that a prescription can never be dispensed twice. Verified with automated SQL Server LocalDB concurrency tests.

5. **Mandatory Staff Password Change Enforcement:**
   - *Security Requirement:* When the clinic Administrator creates a staff account (such as a licensed pharmacist), the account is created with a temporary password and assigned the `MustChangePassword` claim.
   - *Enforcement Mechanism:* `MustChangePasswordMiddleware` intercepts authenticated requests across the application. Any staff user holding the `MustChangePassword` claim is strictly redirected to `/Account/ChangePassword` before accessing any clinical records, appointments, or prescriptions. Upon successful password update, the claim is removed and the user's security cookie is refreshed.

---

## Repository & Documentation Structure

All project documentation follows the official DEPI guidelines and is organized as follows:

```
MediCare-docs/
├── .github/workflows/ci.yml                   # GitHub Actions CI build & test workflow
├── MediCare.sln                               # Visual Studio / .NET 8 solution
├── README.md                                  # Repository overview and setup guide
├── src/
│   ├── MediCare.Data/                         # Entities, DbContext, configurations, migrations, UoW
│   ├── MediCare.Services/                     # Service contracts, implementations, DTOs, FluentValidation
│   └── MediCare.Web/                          # ASP.NET Core MVC, Identity, controllers, Razor views
├── tests/
│   └── MediCare.Tests/                        # xUnit tests with FluentAssertions and Moq
└── docs/
    ├── 01-planning/                           # Phase 1: Planning and Management (Deadline: 16 Oct 2026)
    │   ├── project-proposal.md                # System overview, problem, scope, architecture & milestones
    │   ├── project-plan.md                    # 9-week timeline, Gantt chart, resource allocation & MVP rules
    │   ├── task-assignment.md                 # RACI matrix and solo engineering role coverage
    │   ├── risk-assessment.md                 # Risk matrix (RSK-01 to RSK-08) and mitigation strategies
    │   └── kpis.md                            # Quantifiable engineering KPIs and verification methods
    ├── 02-literature-review/                  # Phase 1: Literature Review (Deadline: 16 Oct 2026)
    │   └── literature-review.md               # Market analysis, system comparison & evaluator placeholders
    ├── 03-requirements/                       # Phase 1: Requirements Gathering (Deadline: 16 Oct 2026)
    │   ├── stakeholders-and-user-stories.md   # Stakeholders matrix and Given/When/Then user stories (US-01..08)
    │   ├── functional-requirements.md         # Numbered functional specifications (FR-01..28)
    │   └── non-functional-requirements.md     # Performance, security, reliability and usability NFRs
    ├── 04-design/                             # Phase 2: System Analysis and Design (Deadline: 6 Nov 2026)
    │   ├── README.md                          # Phase 2 documentation index
    │   ├── problem-statement-and-objectives.md# Clinical problems, 6 design goals & boundaries
    │   ├── use-case-diagram-and-descriptions.md # Use case model (UC-01..18) and detailed narratives
    │   ├── software-architecture.md           # 3-Project N-Tier design, request flows & patterns
    │   ├── database/                          # Relational data models and schemas
    │   │   ├── er-diagram.md                  # Mermaid ERD with cardinalities and keys
    │   │   └── logical-and-physical-schema.md # Data dictionary, filtered index & seed data
    │   ├── data-flow/                         # Process and data modeling
    │   │   └── dfd-context-and-level-1.md     # Context DFD, Level 1, and Level 2 booking flow
    │   ├── behavior/                          # UML dynamic behavior models
    │   │   ├── sequence-diagrams.md           # Sequence diagrams for 7 core workflows
    │   │   ├── activity-diagrams.md           # Activity diagrams for booking, visit & leaves
    │   │   ├── state-diagram.md               # Appointment and doctor approval state machines
    │   │   └── class-diagram.md               # Object-oriented class models across all tiers
    │   ├── ui-ux/                             # UI/UX specifications and guidelines
    │   │   ├── wireframes-spec.md             # Screen-by-screen layouts & sitemap for Figma
    │   │   └── ui-ux-guidelines.md            # WCAG 2.1 AA palette, typography & print CSS
    │   ├── deployment/                        # Infrastructure and hosting models
    │   │   ├── technology-stack.md            # Detailed technology inventory and rationale
    │   │   ├── deployment-and-component-diagrams.md # Cloud topology & component diagrams
    │   │   └── deployment-strategy.md         # CI/CD, user-secrets, Azure & fallback plan
    │   ├── api/                               # Internal JSON API and OpenAPI 3.0
    │   │   ├── api-documentation.md           # Internal calendar & notification JSON contracts
    │   │   └── openapi.yaml                   # OpenAPI 3.0 specification for internal API
    │   └── testing/                           # Quality assurance planning
    │       └── testing-and-validation-plan.md # Test pyramid, 10-thread test & TC-01..24 matrix
    ├── 05-testing/                            # Phase 4: Testing & Quality Assurance
    │   ├── test-strategy.md                   # Multi-tier testing methodology & quality metrics
    │   ├── test-plan.md                       # Test environment setup, scope, & criteria
    │   ├── test-cases.md                      # Detailed test specifications (TC-01..30)
    │   ├── test-execution-report.md           # 157 automated test runs & pass evidence
    │   ├── security-test-report.md            # IDOR, CSRF, CSV Injection security audit
    │   └── final-qa-summary.md                # Verification sign-off & readiness metrics
    └── 06-final/                              # Phase 4: Final Deliverables & User Manual
        ├── user-manual.md                     # Comprehensive operations guide (Admin, Doctor, Patient)
        ├── technical-documentation.md         # Full architectural specification, ERD, and security controls
        └── project-presentation.md            # DEPI graduation defense slide deck outline
```

---

## Key Milestone Dates (DEPI 2026)

| Milestone Phase | Deliverables Included | Official Deadline | Status |
|---|---|---|:---:|
| **Phase 1: Planning & Requirements** | Proposal, Plan, Tasks, Risks, KPIs, Literature Review, User Stories, FR/NFR | **16 Oct 2026** | &#10003; Documented |
| **Phase 2: System Analysis & Design** | Architecture, ERD, Schema, DFDs, UML Diagrams, Wireframes, API Spec | **6 Nov 2026** | &#10003; Documented |
| **Phase 3: Implementation & Deployment** | Sprint 1 Scaffolding & Directory; Sprint 2 Booking Engine & SignalR; Sprint 3 Clinical Records, Prescriptions & Admin Analytics | **30 Nov 2026** | &#10003; Complete |
| **Phase 4: Testing, Manual & Defense** | Automated Test Suites (157 Tests), Security Audit, User Manual, Technical Docs, Presentation Deck | **4 Dec 2026** | &#10003; Complete & Audited |
