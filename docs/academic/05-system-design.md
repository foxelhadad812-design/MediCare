# Chapter 5: System Design

This chapter presents the architectural and behavioral design models of the **MediCare** clinic management and appointment system. The design models are derived strictly from the verified C# ASP.NET Core source code, Entity Framework Core mappings, database configurations, and business service implementations.

---

## 5.1 Use Case Diagram & Specification

The system accommodates four distinct primary actors whose responsibilities and workflows correspond to specialized portals within the web application:
1. **Patient**: Registers an account, explores medical specialties and doctors, consults an automated triage guidance assistant, reserves time slots with conflict prevention, processes simulated payments, and accesses electronic records and digital prescriptions.
2. **Doctor**: Submits practice credentials for licensing review, configures recurring weekly working schedules and vacation leaves, manages the daily appointment queue, registers patient arrival at reception, conducts clinical encounters, uploads diagnostic attachments, and issues cryptographically verifiable electronic prescriptions.
3. **Pharmacist**: Authenticates under mandatory initial password replacement, scans or enters a 128-bit prescription verification token, reviews medication items, and executes atomic, one-time medication dispensing protected against race conditions.
4. **System Administrator**: Conducts credential vetting for physician registrations, provisions pharmacist accounts, tracks system audit logs, and monitors clinic operational metrics, with emergency CLI access for credential recovery directly on the server host.

---

### Figure 5.1: MediCare System Use Case Diagram

```mermaid
flowchart LR
    %% Actors
    subgraph Actors [Actors]
        Patient["fa:fa-user Patient"]
        Doctor["fa:fa-user-md Doctor"]
        Pharmacist["fa:fa-prescription-bottle-alt Pharmacist"]
        Admin["fa:fa-user-shield System Administrator"]
    end

    %% MediCare System Boundary
    subgraph MediCareSystem [MediCare Clinic Management System]

        %% Patient Use Cases
        subgraph PatientSub [Patient Portal]
            UC_P1([Register & Authenticate])
            UC_P2([Manage Personal Health Profile])
            UC_P3([Search & Filter Doctors])
            UC_P4([Consult AI Triage Assistant])
            UC_P5([Book Appointment Slot])
            UC_P6([Process Simulated Payment])
            UC_P7([Cancel / Reschedule Appointment])
            UC_P8([Attend Telemedicine Consultation])
            UC_P9([View Medical Records & Prescriptions])
        end

        %% Doctor Use Cases
        subgraph DoctorSub [Doctor Clinical Portal]
            UC_D1([Register Practice & Submit Credentials])
            UC_D2([Configure Weekly Working Hours])
            UC_D3([Manage Vacation Leaves])
            UC_D4([View Schedule & Patient Queue])
            UC_D5([Check-in Patient at Reception])
            UC_D6([Conduct Clinical Encounter])
            UC_D7([Upload Diagnostic Attachments])
            UC_D8([Issue Digital Prescription with QR])
            UC_D9([Launch Telehealth Video Room])
        end

        %% Pharmacist Use Cases
        subgraph PharmacistSub [Pharmacy Portal]
            UC_Ph1([Mandatory Password Change on First Login])
            UC_Ph2([Scan / Verify Prescription QR Code])
            UC_Ph3([Inspect Prescription & Physician Details])
            UC_Ph4([Atomic Medication Dispensing])
        end

        %% Admin Use Cases
        subgraph AdminSub [Administration Portal]
            UC_A1([Audit Log Review & Offline CLI Recovery])
            UC_A2([Review & Verify Doctor Licenses])
            UC_A3([Provision Pharmacist Accounts])
            UC_A4([Manage Specialties & Clinic Registry])
            UC_A5([Monitor Clinic Dashboard & Analytics])
        end

    end

    %% Patient Connections
    Patient --> UC_P1
    Patient --> UC_P2
    Patient --> UC_P3
    Patient --> UC_P4
    Patient --> UC_P5
    Patient --> UC_P6
    Patient --> UC_P7
    Patient --> UC_P8
    Patient --> UC_P9

    %% Doctor Connections
    Doctor --> UC_D1
    Doctor --> UC_D2
    Doctor --> UC_D3
    Doctor --> UC_D4
    Doctor --> UC_D5
    Doctor --> UC_D6
    Doctor --> UC_D7
    Doctor --> UC_D8
    Doctor --> UC_D9

    %% Pharmacist Connections
    Pharmacist --> UC_Ph1
    Pharmacist --> UC_Ph2
    Pharmacist --> UC_Ph3
    Pharmacist --> UC_Ph4

    %% Admin Connections
    Admin --> UC_A1
    Admin --> UC_A2
    Admin --> UC_A3
    Admin --> UC_A4
    Admin --> UC_A5

    %% Relationships / Dependencies
    UC_P5 -.->|<<include>>| UC_P6
    UC_D6 -.->|<<include>>| UC_D8
    UC_D6 -.->|<<extend>>| UC_D7
    UC_Ph4 -.->|<<requires>>| UC_Ph2
```

**Caption (Figure 5.1):** MediCare Use Case Diagram depicting interactions across Patient, Doctor, Pharmacist, and System Administrator actors within the web application boundaries.

**Plain-Language Explanation:**  
This diagram models how the four distinct roles interact with the system modules. Patients manage appointments, payments, and medical history. Doctors manage clinical encounters, schedules, and electronic prescriptions. Pharmacists scan prescription QR tokens and dispense medications once. Administrators verify medical licenses and provision staff accounts.

**How to Explain This in the Discussion:**  
> *"The system establishes role-based separation of concerns across four primary actors. Rather than a generic monolithic user model, each actor has a distinct workflow reflecting clinic operations: the patient books conflict-free slots, the doctor conducts the clinical encounter and issues a signed electronic prescription, the pharmacist validates the token to dispense medication atomically, and the administrator audits and approves provider credentials."*

---

### Detailed Use Case Specifications

The tables below specify the core operational use cases of the MediCare platform.

#### Table 5.1: Use Case Description — UC-P5: Book Appointment Slot
| Field | Details |
| :--- | :--- |
| **Use Case ID** | **UC-P5** |
| **Use Case Name** | Book Appointment Slot |
| **Primary Actor** | Registered Patient |
| **Preconditions** | 1. Patient is authenticated with a valid session.<br>2. Selected doctor is approved (`IsApproved == true`) and has active weekly schedule slots. |
| **Main Success Scenario** | 1. Patient selects a doctor, date, and appointment type (Consultation, Follow-Up, or Telemedicine).<br>2. System validates that the date is between tomorrow and 30 days in advance (`Date <= Today + 30d`).<br>3. System computes available non-overlapping time slots based on the doctor's `WorkingHours`, subtracting approved `DoctorLeaves` and existing bookings (`Status != Cancelled && Status != Rejected`).<br>4. Patient selects an open slot and submits the booking form.<br>5. System verifies in a database transaction that the slot remains unreserved.<br>6. System creates an `Appointment` record with `Status = Confirmed`, sets `PaymentStatus = Unpaid`, and displays payment options. |
| **Alternative / Error Flows** | **4a. Slot Concurrency Conflict:** Another patient booked the same slot milliseconds earlier.<br>System catches filtered unique index violation (`IX_Appointments_Doctor_NoOverlap`), aborts transaction, returns an error message, and prompts the patient to pick another slot.<br>**2a. Date Exceeds Booking Horizon:** Patient requests a date beyond 30 days.<br>System displays validation error: *"Appointments cannot be booked more than 30 days in advance."* |
| **Postconditions** | 1. A new `Appointment` record is persisted in the database.<br>2. Slot is locked against double-booking.<br>3. Automated background reminder job schedules notification 24 hours prior to appointment time. |

---

#### Table 5.2: Use Case Description — UC-D6 & UC-D8: Conduct Clinical Encounter & Issue Prescription
| Field | Details |
| :--- | :--- |
| **Use Case ID** | **UC-D6 & UC-D8** |
| **Use Case Name** | Conduct Clinical Encounter and Issue Digital Prescription |
| **Primary Actor** | Treating Doctor |
| **Preconditions** | 1. Doctor is authenticated.<br>2. Appointment belongs to the logged-in doctor (`Appointment.DoctorId == currentDoctor.Id`).<br>3. Appointment is confirmed or arrived at reception (`Status == Confirmed`). |
| **Main Success Scenario** | 1. Doctor opens the clinical encounter workspace for the patient's appointment.<br>2. Doctor records vital signs, primary diagnosis, symptoms, and clinical examination notes.<br>3. (Optional) Doctor attaches diagnostic lab results or imaging files (validated against allowed MIME magic bytes).<br>4. Doctor adds prescription items specifying medication name, dosage, frequency, and duration in days.<br>5. Doctor submits the completed clinical encounter form.<br>6. System creates a `MedicalRecord` linked to the `Appointment`.<br>7. System generates an associated `Prescription` record with a cryptographically secure random 128-bit verification token (`Convert.ToHexString(RandomNumberGenerator.GetBytes(16))`).<br>8. System renders a local, server-side QR code representing the secure verification URL.<br>9. System updates appointment `Status = Completed`. |
| **Alternative / Error Flows** | **2a. IDOR / Ownership Breach:** A doctor attempts to open an encounter for another doctor's patient.<br>System returns `403 Forbidden` and logs a security alert with user details.<br>**3a. Invalid File Upload:** Doctor attaches an executable or spoofed file.<br>System inspects file header magic bytes, rejects the file, and displays an invalid format warning. |
| **Postconditions** | 1. `MedicalRecord` and `Prescription` records are permanently stored.<br>2. Prescription verification token is indexed uniquely in the database (`IX_Prescriptions_VerificationToken`).<br>3. Patient can immediately view and print the prescription QR code from their portal. |

---

#### Table 5.3: Use Case Description — UC-Ph4: Atomic Medication Dispensing
| Field | Details |
| :--- | :--- |
| **Use Case ID** | **UC-Ph4** |
| **Use Case Name** | Verify and Dispense Prescription |
| **Primary Actor** | Authenticated Pharmacist |
| **Preconditions** | 1. Pharmacist has logged in (and completed mandatory initial password change if new account).<br>2. Prescription exists and has a valid verification token. |
| **Main Success Scenario** | 1. Pharmacist scans patient's prescription QR code or enters the verification token in the pharmacy portal.<br>2. System looks up prescription using token index `IX_Prescriptions_VerificationToken` (without requiring public login).<br>3. System displays patient name, prescribing doctor credentials, medication items, dosages, and current dispensation status (`IsDispensed`).<br>4. Pharmacist reviews items, prepares medication, enters optional pharmacy notes, and clicks *"Confirm Dispense"*.<br>5. System verifies `IsDispensed == false`, sets `IsDispensed = true`, records `DispensedAt = DateTime.UtcNow` and `DispensedByUserId = currentUserId`.<br>6. System commits the update with optimistic concurrency verification.<br>7. System displays success confirmation and permanently locks the prescription against re-dispensing. |
| **Alternative / Error Flows** | **4a. Prescription Already Dispensed:**<br>System displays a prominent warning badge: *"Prescription was already dispensed on [Date] by Pharmacist [Name]. Re-dispensing is prohibited."* The dispense button is disabled.<br>**5a. Concurrent Dispensing Race Condition:** Two pharmacists attempt to dispense identical token simultaneously.<br>EF Core concurrency token detects conflict (`DbUpdateConcurrencyException`), rolls back the second transaction, and displays an alert. |
| **Postconditions** | 1. Prescription status is permanently marked as dispensed.<br>2. Audit metadata (`DispensedAt`, `DispensedByUserId`, `PharmacyNotes`) is stored for regulatory compliance. |

---

#### Table 5.4: Use Case Description — UC-A2: Doctor Credential Review & Licensing Approval
| Field | Details |
| :--- | :--- |
| **Use Case ID** | **UC-A2** |
| **Use Case Name** | Review and Approve Doctor Registration |
| **Primary Actor** | System Administrator |
| **Preconditions** | 1. Administrator is authenticated (`Role == "Admin"`).<br>2. New doctor has registered with pending approval status (`IsApproved == false`). |
| **Main Success Scenario** | 1. Administrator navigates to the Doctor Approvals dashboard.<br>2. System displays list of pending doctors including full legal name, medical license number, specialization, consultation fee, and biography.<br>3. Administrator reviews credentials against official syndicate standards and selects *"Approve"*.<br>4. System sets `Doctor.IsApproved = true` and updates `UpdatedAt = DateTime.UtcNow`.<br>5. System dispatches an email notification confirming profile activation.<br>6. Doctor immediately becomes visible in public search and patient booking filters. |
| **Alternative / Error Flows** | **3a. Administrator Rejects Registration:**<br>Administrator clicks *"Reject"* and enters rejection reason.<br>System removes the unapproved doctor record, purges the orphaned `ApplicationUser` login account, and logs the administrative rejection event. |
| **Postconditions** | 1. Doctor status is updated in the database.<br>2. Audit entry is recorded in application logs. |

---

### Instructions for Rendering Diagram 5.1
The Mermaid source code is preserved in `docs/academic/diagrams/5.1-use-case.mmd`.
- **Online rendering:** Copy the source into [Mermaid Live Editor](https://mermaid.live) and export as PNG (2000px width) or SVG.
- **Local CLI rendering:** Run `npx @mermaid-js/mermaid-cli -i docs/academic/diagrams/5.1-use-case.mmd -o docs/academic/diagrams/5.1-use-case.png -w 1600`
- **VS Code:** Install the *Markdown Preview Mermaid Support* extension to preview inline.
