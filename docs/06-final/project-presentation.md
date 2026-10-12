# MediCare — Graduation Project Presentation & Defense Deck
**Digital Egypt Pioneers Initiative (DEPI) — Full Stack .NET Web Development**

---

## Slide 1: Title Slide
- **Project Title**: MediCare — Enterprise Clinic Management & Appointment System
- **Initiative**: Digital Egypt Pioneers Initiative (DEPI)
- **Track**: Full Stack .NET Web Development (.NET 8 MVC / SQL Server)
- **Supervision**: Ministry of Communications and Information Technology (MCIT)
- **Presentation Date**: October 2026

---

## Slide 2: The Healthcare Problem in Egypt
- **Fragmented Outpatient Scheduling**: Patients face lengthy clinic wait times and phone-based booking friction.
- **Double Booking & Scheduling Conflicts**: Concurrent booking requests frequently collide, resulting in clinician overbooking.
- **Geographic Information Gap**: Inability to discover verified medical specialists categorized by Egyptian governorates.
- **Disconnected Medical Records**: Prescriptions and consultation histories remain on paper, making allergy checking error-prone.
- **Administrative Blind Spots**: Lack of real-time clinical dashboards and revenue visibility for clinic managers.

---

## Slide 3: The MediCare Solution
- **Connected clinic workflow**: Brings administrators, doctors, and patients into one application.
- **Database-enforced slot protection**: A SQL Server filtered unique index rejects a second booking for the same doctor, date, and time while the existing appointment remains eligible to occupy that slot.
- **Localized for Egypt**: Egyptian governorate search, local clinic wall-clock anchoring (`Africa/Cairo`), and bilingual Arabic/English terms.
- **Appointment reminders**: A background service checks upcoming confirmed visits; delivery channels depend on provider configuration.
- **Enterprise Reporting**: One-click operational and financial exports in CSV, native Excel (.xlsx), and branded PDF.

---

## Slide 4: System Architecture & Technology Stack
- **Backend Architecture**: Three project layers (`MediCare.Web` → `MediCare.Services` → `MediCare.Data`).
- **Framework**: .NET 8 (C# 12) & ASP.NET Core MVC.
- **ORM & Database**: Entity Framework Core 8 with Microsoft SQL Server.
- **Real-Time Communication**: SignalR WebSockets for instant notification toasts and slot availability updates.
- **Frontend**: Razor Views, Bootstrap 5, Bootstrap Icons, FullCalendar 6, Chart.js.
- **Validation & Reliability**: FluentValidation, Repository & Unit of Work patterns, Result pattern.
- **Testing**: xUnit, FluentAssertions, Moq, Microsoft SQL Server LocalDB integration tests.

---

## Slide 5: Database Engineering & Concurrency Defense
- **11 Relational Entities**: `ApplicationUser`, `Doctor`, `Patient`, `Appointment`, `MedicalRecord`, `Prescription`, `PrescriptionItem`, `WorkingHours`, `DoctorLeave`, `Notification`, `Specialization`.
- **Filtered Unique Index**:
  ```sql
  CREATE UNIQUE INDEX IX_Appointments_Doctor_Date_Time_Active
  ON Appointments (DoctorId, AppointmentDate, StartTime)
  WHERE Status <> 3 AND Status <> 4; -- Exclude Cancelled and Rejected
  ```
- **Concurrency verification**: The recorded test report describes ten simultaneous booking attempts, with one accepted and nine rejected by the slot constraint. Rerun this scenario against the current code before presenting it as a current result.

---

## Slide 6: Clinician (Doctor) Capabilities
- **Schedule Management**: Intuitive working hours configuration per weekday.
- **Leave Declaration**: Temporary leave windows automatically block the calendar and protect patient bookings.
- **Consultation Execution**: Patient attendance tracking (Confirmed → Completed / No-Show / Cancelled).
- **Electronic Health Records (EHR)**: Detailed diagnostic recording, symptoms, and treatment plans.
- **Digital Prescriptions**: Structured formulation of medications, dosages, frequency, and instructions.

---

## Slide 7: Patient Experience & Care Continuum
- **Governorate Discovery**: Search approved doctors across all 27 Egyptian governorates with specialization filtering.
- **Calendar Booking**: Dynamic time slots rendered using doctor availability rules.
- **Atomic Rescheduling**: Reschedule appointments online up to 2 hours prior to visit time with automatic conflict prevention.
- **Clinical Profile & Allergies**: Patients record known drug allergies and chronic medical history, immediately highlighted to attending physicians.
- **Real-Time Updates**: Instant alerts for appointment approvals, schedule changes, and doctor notes.

---

## Slide 8: Administrator Operations & Analytics
- **Medical Licensing Gate**: Strict approval/rejection workflow before newly registered doctors become bookable.
- **Patient Registry & Security**: Comprehensive patient audit with instant account lockout controls.
- **Department Management**: Complete CRUD operations for medical specializations with linked-doctor safety guards.
- **Executive Analytics Dashboard**:
  - Total visits, revenue collected (EGP), accounts receivable.
  - Monthly status trends & department visit shares.
  - Top 5 performing clinicians league table.
  - Patient demographics (gender & age bracket cohorts).
- **Multi-Format Export**: Native Excel (.xlsx), PDF reports, and CSV.

---

## Slide 9: Automated Background Services & External Adapters
- **24-Hour Reminder Background Worker**:
  - `AppointmentReminderBackgroundService : BackgroundService` executes every 15 minutes.
  - Identifies confirmed visits scheduled in the upcoming 24-hour window.
  - Dispatches personalized Email (MailKit / SendGrid), SMS (Twilio), and in-app SignalR notifications.
  - Updates `ReminderSent = true` atomically.
- **Provider Adapters**: Configurable MailKit SMTP, SendGrid, Twilio, and mock simulation fallback modes.

---

## Slide 10: Quality Assurance & DEPI Compliance Audit
- **Testing record**: The repository includes unit, controller, security, and SQL Server integration tests. The earlier run report is dated 7 October 2026, but its total does not match the suite breakdown; rerun and save the result before quoting a number.
- **Security controls to demonstrate**: role checks, ownership validation for clinical records, anti-forgery protection on MVC forms, and CSV export sanitization.
- **Deployment status**: An Azure deployment workflow is included. A live deployment URL and successful cloud run should only be presented once verified.

---

## Slide 11: Live Demonstration Script & Flow
1. **Admin Persona**: Log in as Administrator → Review operational dashboard → Approve pending doctor credentials → Manage clinical specializations.
2. **Doctor Persona**: Log in with a configured development sample account, such as the seeded cardiology doctor → Review working hours → View upcoming consultations.
3. **Patient Persona**: Log in as Patient → Search doctors in Alexandria / Cardiology → Book slot → View profile & update drug allergies → Reschedule appointment.
4. **Doctor Persona**: Conduct consultation → Create Medical Record & Digital Prescription.
5. **Admin Persona**: Generate and download native Excel (.xlsx) and branded PDF reports.

---

## Slide 12: Future Roadmap
- **Telemedicine Integration**: WebRTC video consultations directly in browser.
- **Payment Gateways**: Direct online payments via Egyptian payment providers (Paymob, Fawry).
- **Cross-Platform Mobile App**: .NET MAUI companion mobile applications for iOS and Android.
- **AI clinic guide**: Optional OpenAI Responses API support suggests a broad specialty using structured output, while a local rules-based fallback remains available. It does not diagnose, triage, or recommend treatment; real medical data must not be entered.

---

## Slide 13: Conclusion & Acknowledgments
- MediCare demonstrates a working clinic workflow prototype with documented design choices, role-based access, and database-level booking protection.
- Thank the DEPI instructors and reviewers who provided project guidance.
- **Open for Questions & Defense**.
