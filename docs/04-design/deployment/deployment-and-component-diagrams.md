# Deployment & Component Diagrams: MediCare

This document specifies the structural composition of MediCare's software components and their physical distribution across runtime hardware and cloud environments.

---

## 1. High-Level Component Diagram

The Component Diagram details the internal modular structure of MediCare, the boundaries of its 3-tier projects, and their dependencies on external frameworks and third-party systems.

```mermaid
flowchart TD
    subgraph UI_Tier["Presentation Tier (MediCare.Web)"]
        MVC["MVC Controllers\n(Account, Doctors, Appointments,\nMedicalRecords, Admin)"]
        Razor["Razor View Engine\n(Bootstrap 5, CSS Print Views)"]
        FullCal["FullCalendar.js Client\n(Slot Picker Widget)"]
        Charts["Chart.js Analytics Client\n(Admin Visualizations)"]
        Hub["AppointmentHub\n(SignalR WebSocket Endpoint)"]
        Filters["Action Filters\n(IDOR Ownership, Anti-CSRF)"]
        Middleware["Global Exception\nHandling Middleware"]
    end

    subgraph Service_Tier["Business Logic Tier (MediCare.Services)"]
        SlotEng["SlotEngineService\n(Dynamic 30-min Slot Calculator)"]
        ApptSvc["AppointmentService\n(State Transitions & Concurrency Pre-check)"]
        RecSvc["MedicalRecordService\n(Consultation Notes & Attachments)"]
        PrescSvc["PrescriptionService\n(Medication Item Repeater)"]
        DocSvc["DoctorService\n(Hours & Leave Management)"]
        Validators["FluentValidation Rules\n(Booking, Leaves, Hours)"]
        Factories["Pattern Factories\n(AppointmentFactory, NotificationFactory)"]
        EmailGw["EmailService (MailKit)\n(SMTP Wrapper)"]
        MockSms["MockSmsService\n(SMS Gateway Stub)"]
    end

    subgraph Data_Tier["Data Access Tier (MediCare.Data)"]
        UoW["Unit of Work (IUnitOfWork)\n(Transaction Coordinator)"]
        Repos["Repositories (IRepository<T>,\nIAppointmentRepo, IDoctorRepo)"]
        DbContext["ApplicationDbContext\n(EF Core 8 Context)"]
        Entities["Domain Entity Models\n(Appointments, Records, Leaves)"]
        Configs["Fluent API Configurations\n(Filtered Unique Indexes)"]
        DbSeed["DbInitializer\n(Automated Data Seeder)"]
    end

    subgraph External_Systems["External Infrastructure & Cloud Gateways"]
        SQLDB[("Microsoft SQL Server / Azure SQL\n(Relational Tables & Indexes)")]
        DiskStorage[("File System (wwwroot/uploads)\n(Diagnostic Scans & PDFs)")]
        SMTPRelay["External SMTP Relay Server\n(Mailtrap / SendGrid / Gmail)"]
        ClientBrowser["Client Web Browser\n(Desktop & Mobile Devices)"]
    end

    %% Client Interactions
    ClientBrowser <-->|"HTTPS (HTML, CSS, JS, JSON)"| MVC
    ClientBrowser <-->|"WSS (WebSocket Real-Time Push)"| Hub
    MVC --> Razor
    Razor --> FullCal
    Razor --> Charts
    MVC --> Filters
    MVC --> Middleware

    %% Web -> Services
    MVC -->|"Calls Service Interfaces"| SlotEng
    MVC -->|"Calls Service Interfaces"| ApptSvc
    MVC -->|"Calls Service Interfaces"| RecSvc
    MVC -->|"Calls Service Interfaces"| PrescSvc
    MVC -->|"Calls Service Interfaces"| DocSvc

    %% Services Internal
    ApptSvc --> SlotEng
    ApptSvc --> Validators
    ApptSvc --> Factories
    ApptSvc --> EmailGw
    ApptSvc --> MockSms
    RecSvc --> PrescSvc

    %% Services -> Data
    SlotEng --> UoW
    ApptSvc --> UoW
    RecSvc --> UoW
    DocSvc --> UoW
    UoW --> Repos
    Repos --> DbContext
    DbContext --> Entities
    DbContext --> Configs
    DbSeed --> DbContext

    %% Data & Services -> External
    DbContext <-->|"T-SQL / TCP Port 1433"| SQLDB
    RecSvc -->|"Saves File Stream"| DiskStorage
    EmailGw -->|"SMTP / TLS Port 587"| SMTPRelay
```

---

## 2. Cloud Deployment Diagram

The Deployment Diagram maps the physical runtime distribution of MediCare's software artifacts across client devices, Microsoft Azure cloud resources, and external services.

```mermaid
flowchart TD
    subgraph ClientTier["Client Tier (End-User Devices)"]
        nodeBrowser["Modern Web Browser\n(Chrome, Firefox, Safari, Edge)\n- HTML5 / CSS3 / JavaScript\n- FullCalendar.js, Chart.js\n- SignalR WebSocket Client"]
    end

    subgraph AzureCloud["Microsoft Azure Cloud (West Europe / East US)"]
        subgraph AppServicePlan["Azure App Service Plan (B1 Basic / F1 Free Tier)"]
            subgraph WebApp["MediCare Web Application Container"]
                AppRuntime[".NET 8.0 ASP.NET Core Runtime\n- Kestrel Web Server\n- In-Process SignalR Hub\n- MediCare.Web.dll\n- MediCare.Services.dll\n- MediCare.Data.dll"]
                LocalDisk[("App Service Virtual Storage\nwwwroot/uploads/\n- Diagnostic Scans (.jpg, .png)\n- Medical Reports (.pdf)")]
            end
        end

        subgraph AzureDataTier["Azure Managed Data Services"]
            AzureSQL[("Azure SQL Database\n(Serverless / Basic 5 DTU)\n- Relational Tables\n- Filtered Unique Index\n- Automated Point-in-Time Backups")]
        end
    end

    subgraph ExternalServices["External Communication Relays"]
        SMTPServer["External SMTP Service\n(Mailtrap / Gmail SMTP / SendGrid)\n- Secure TLS Transmission\n- Port 587 / 465"]
    end

    %% Network Connections
    nodeBrowser <-->|"HTTPS (Port 443)\nTLS 1.3 Encrypted"| AppRuntime
    nodeBrowser <-->|"WSS / WebSockets (Port 443)\nReal-Time SignalR Stream"| AppRuntime
    AppRuntime <-->|"Local File I/O Stream"| LocalDisk
    AppRuntime <-->|"T-SQL over Encrypted TDS (Port 1433)\nAzure Connection String"| AzureSQL
    AppRuntime -->|"SMTP / TLS Encrypted (Port 587)\nMailKit Transmission"| SMTPServer
```

---

## 3. Network & Security Boundaries

1. **Client-to-Application Encryption:**  
   All HTTP traffic is automatically upgraded to **HTTPS (TLS 1.3)** via Azure App Service URL redirection. Cleartext HTTP requests on port 80 are permanently disabled.
2. **Database Isolation:**  
   Azure SQL Database enforces firewall rules restricting incoming connections exclusively to Azure App Service instances (`Allow Azure services and resources to access this server = YES`) and authorized administrator IP addresses. Port 1433 is shielded from the public internet.
3. **Application Secret Isolation:**  
   Database connection strings, SMTP passwords, and Identity security tokens are injected at runtime via Azure App Service **Application Settings (Environment Variables)** and are never written to physical disk or configuration files.
