# MediCare — Clinic Management & Appointment System

## Project Overview
**MediCare** is an enterprise-grade Clinic Management and Appointment System developed as a graduation project for the **Digital Egypt Pioneers Initiative (DEPI) - .NET Full Stack Track**.

The system streamlines clinic workflows by offering dynamic, conflict-free appointment scheduling, role-based portals for patients, doctors, and clinic administrators, real-time push notifications, digital prescription issuance with print formatting, and clinical history tracking.

---

## Technical Stack
- **Framework & Runtime:** ASP.NET Core MVC (.NET 8)
- **Data Access & ORM:** Entity Framework Core, Microsoft SQL Server
- **Authentication & Security:** ASP.NET Core Identity, Role-Based Access Control, Anti-CSRF, BOLA/IDOR Defense
- **Real-Time Communication:** ASP.NET Core SignalR (Strongly-Typed Hubs)
- **Email & Messaging:** MailKit (SMTP via `IEmailService`), Mock SMS (`ISmsService`)
- **Frontend & UI:** Bootstrap 5, FullCalendar.js, Chart.js, jQuery, CSS Print Media Queries
- **Validation & Testing:** FluentValidation, xUnit, Moq, FluentAssertions
- **CI/CD & Hosting:** GitHub Actions, Microsoft Azure App Service, Azure SQL Database

---

## Repository & Documentation Structure

All project documentation follows the official DEPI guidelines and is organized as follows:

```
MediCare-docs/
├── README.md                                  # Repository overview and documentation index
├── docs/
│   ├── 01-planning/                           # Phase 1: Planning and Management (Deadline: 16 Oct 2026)
│   │   ├── project-proposal.md                # System overview, problem, scope, architecture & milestones
│   │   ├── project-plan.md                    # 9-week timeline, Gantt chart, resource allocation & MVP rules
│   │   ├── task-assignment.md                 # RACI matrix and solo engineering role coverage
│   │   ├── risk-assessment.md                 # Risk matrix (RSK-01 to RSK-08) and mitigation strategies
│   │   └── kpis.md                            # Quantifiable engineering KPIs and verification methods
│   ├── 02-literature-review/                  # Phase 1: Literature Review (Deadline: 16 Oct 2026)
│   │   └── literature-review.md               # Market analysis, system comparison & evaluator placeholders
│   ├── 03-requirements/                       # Phase 1: Requirements Gathering (Deadline: 16 Oct 2026)
│   │   ├── stakeholders-and-user-stories.md   # Stakeholders matrix and Given/When/Then user stories (US-01..08)
│   │   ├── functional-requirements.md         # Numbered functional specifications (FR-01..28)
│   │   └── non-functional-requirements.md     # Performance, security, reliability and usability NFRs
│   ├── 04-design/                             # Phase 2: System Analysis and Design (Deadline: 6 Nov 2026)
│   │   ├── README.md                          # Phase 2 documentation index
│   │   ├── problem-statement-and-objectives.md# Clinical problems, 6 design goals & boundaries
│   │   ├── use-case-diagram-and-descriptions.md # Use case model (UC-01..18) and detailed narratives
│   │   ├── software-architecture.md           # 3-Project N-Tier design, request flows & patterns
│   │   ├── database/                          # Relational data models and schemas
│   │   │   ├── er-diagram.md                  # Mermaid ERD with cardinalities and keys
│   │   │   └── logical-and-physical-schema.md # Data dictionary, filtered index & seed data
│   │   ├── data-flow/                         # Process and data modeling
│   │   │   └── dfd-context-and-level-1.md     # Context DFD, Level 1, and Level 2 booking flow
│   │   ├── behavior/                          # UML dynamic behavior models
│   │   │   ├── sequence-diagrams.md           # Sequence diagrams for 7 core workflows
│   │   │   ├── activity-diagrams.md           # Activity diagrams for booking, visit & leaves
│   │   │   ├── state-diagram.md               # Appointment and doctor approval state machines
│   │   │   └── class-diagram.md               # Object-oriented class models across all tiers
│   │   ├── ui-ux/                             # UI/UX specifications and guidelines
│   │   │   ├── wireframes-spec.md             # Screen-by-screen layouts & sitemap for Figma
│   │   │   └── ui-ux-guidelines.md            # WCAG 2.1 AA palette, typography & print CSS
│   │   ├── deployment/                        # Infrastructure and hosting models
│   │   │   ├── technology-stack.md            # Detailed technology inventory and rationale
│   │   │   ├── deployment-and-component-diagrams.md # Cloud topology & component diagrams
│   │   │   └── deployment-strategy.md         # CI/CD, user-secrets, Azure & fallback plan
│   │   ├── api/                               # Internal JSON API and OpenAPI 3.0
│   │   │   ├── api-documentation.md           # Internal calendar & notification JSON contracts
│   │   │   └── openapi.yaml                   # OpenAPI 3.0 specification for internal API
│   │   └── testing/                           # Quality assurance planning
│   │       └── testing-and-validation-plan.md # Test pyramid, 10-thread test & TC-01..24 matrix
│   ├── 05-testing/                            # Phase 4: Testing & Quality Assurance (Deadline: 4 Dec 2026)
│   │   └── .gitkeep
│   └── 06-final/                              # Phase 4: Final Deliverables & User Manual (Deadline: 4 Dec 2026)
│       └── .gitkeep
```

> **Note on Implementation Code:**  
> In accordance with the project architecture, production source code will be placed under `/src` (`MediCare.Web`, `MediCare.Services`, `MediCare.Data`) and automated test suites under `/tests` (`MediCare.Tests.Unit`, `MediCare.Tests.Integration`) during Phase 3 (Implementation).

---

## Key Milestone Dates (DEPI 2026)

| Milestone Phase | Deliverables Included | Official Deadline |
|---|---|---|
| **Phase 1: Planning & Requirements** | Proposal, Plan, Tasks, Risks, KPIs, Literature Review, User Stories, FR/NFR | **16 Oct 2026** |
| **Phase 2: System Analysis & Design** | Architecture, ERD, Schema, DFDs, UML Diagrams, Wireframes, API Spec | **6 Nov 2026** |
| **Phase 3: Implementation & Deployment** | Source Code, Migrations, Seed Data, CI/CD, Azure Live Deployment, README | **30 Nov 2026** |
| **Phase 4: Testing, Manual & Defense** | Automated Test Suites, Bug Reports, User Manual, Slides, Presentation | **4 Dec 2026** |
