using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediCare.Tests.Integration;

[Trait("Category", "Integration")]
public class CoreClinicalFlowsIntegrationTests : IClassFixture<MediCareWebApplicationFactory>, IAsyncLifetime
{
    private readonly MediCareWebApplicationFactory _factory;

    private const string DoctorUserId = "doc-flow-user-01";
    private const string PatientUserId = "pat-flow-user-01";
    private const string OtherPatientUserId = "pat-other-user-02";
    private const string PharmacistUserId = "pharm-flow-user-01";
    private const string AdminUserId = "admin-flow-user-01";

    private int _doctorId;
    private int _patientId;
    private int _otherPatientId;

    public CoreClinicalFlowsIntegrationTests(MediCareWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        // Anchor test clock to a fixed Monday morning (2026-11-16 09:00:00)
        _factory.Clock.SetNow(new DateTime(2026, 11, 16, 9, 0, 0));

        await _factory.SeedAsync(async db =>
        {
            // Seed Specialization
            var spec = await db.Specializations.FirstOrDefaultAsync(s => s.Name == "Cardiology");
            if (spec == null)
            {
                spec = new Specialization { Name = "Cardiology", Description = "Heart & Cardiovascular Care" };
                db.Specializations.Add(spec);
                await db.SaveChangesAsync();
            }

            // Seed Doctor User & Profile
            var docUser = await db.Users.FirstOrDefaultAsync(u => u.Id == DoctorUserId);
            if (docUser == null)
            {
                docUser = new ApplicationUser
                {
                    Id = DoctorUserId,
                    UserName = "dr.flow@clinic.test",
                    Email = "dr.flow@clinic.test",
                    FullName = "Dr. Hazem El-Masry"
                };
                db.Users.Add(docUser);
                await db.SaveChangesAsync();
            }

            var doc = await db.Doctors.FirstOrDefaultAsync(d => d.UserId == DoctorUserId);
            if (doc == null)
            {
                doc = new Doctor
                {
                    UserId = DoctorUserId,
                    SpecializationId = spec.Id,
                    LicenseNumber = "LIC-FLOW-001",
                    ConsultationFee = 350m,
                    SlotDurationMinutes = 30,
                    IsApproved = true,
                    Governorate = "Cairo",
                    Bio = "Senior Consultant Cardiologist"
                };
                db.Doctors.Add(doc);
                await db.SaveChangesAsync();

                // Add working hours for all days 08:00 - 20:00
                foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
                {
                    db.WorkingHours.Add(new WorkingHours
                    {
                        DoctorId = doc.Id,
                        DayOfWeek = day,
                        StartTime = TimeSpan.FromHours(8),
                        EndTime = TimeSpan.FromHours(20)
                    });
                }
                await db.SaveChangesAsync();
            }
            _doctorId = doc.Id;

            // Seed Patient User & Profile
            var patUser = await db.Users.FirstOrDefaultAsync(u => u.Id == PatientUserId);
            if (patUser == null)
            {
                patUser = new ApplicationUser
                {
                    Id = PatientUserId,
                    UserName = "patient.flow@test.com",
                    Email = "patient.flow@test.com",
                    FullName = "Ali Mahmoud"
                };
                db.Users.Add(patUser);
                await db.SaveChangesAsync();
            }

            var pat = await db.Patients.FirstOrDefaultAsync(p => p.UserId == PatientUserId);
            if (pat == null)
            {
                pat = new Patient
                {
                    UserId = PatientUserId,
                    DateOfBirth = new DateTime(1990, 5, 15),
                    Gender = "Male",
                    BloodGroup = "A+"
                };
                db.Patients.Add(pat);
                await db.SaveChangesAsync();
            }
            _patientId = pat.Id;

            // Seed Other Patient User & Profile (for IDOR testing)
            var otherUser = await db.Users.FirstOrDefaultAsync(u => u.Id == OtherPatientUserId);
            if (otherUser == null)
            {
                otherUser = new ApplicationUser
                {
                    Id = OtherPatientUserId,
                    UserName = "other.patient@test.com",
                    Email = "other.patient@test.com",
                    FullName = "Stranger User"
                };
                db.Users.Add(otherUser);
                await db.SaveChangesAsync();
            }

            var otherPat = await db.Patients.FirstOrDefaultAsync(p => p.UserId == OtherPatientUserId);
            if (otherPat == null)
            {
                otherPat = new Patient
                {
                    UserId = OtherPatientUserId,
                    DateOfBirth = new DateTime(1995, 8, 20),
                    Gender = "Female"
                };
                db.Patients.Add(otherPat);
                await db.SaveChangesAsync();
            }
            _otherPatientId = otherPat.Id;

            // Seed Pharmacist User
            var pharmUser = await db.Users.FirstOrDefaultAsync(u => u.Id == PharmacistUserId);
            if (pharmUser == null)
            {
                pharmUser = new ApplicationUser
                {
                    Id = PharmacistUserId,
                    UserName = "pharmacist.flow@test.com",
                    Email = "pharmacist.flow@test.com",
                    FullName = "Pharmacist Mona"
                };
                db.Users.Add(pharmUser);
                await db.SaveChangesAsync();
            }
        });
    }

    public Task DisposeAsync()
    {
        _factory.Clock.Reset();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Flow1_AppointmentBooking_E2E()
    {
        // 1. Guest views public doctors catalog
        var guestClient = _factory.CreateAnonymousClient();
        var doctorsPage = await guestClient.GetAsync("/Doctors");
        doctorsPage.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Candidate slot inspection via public calendar API
        var targetDate = new DateTime(2026, 11, 18); // Wednesday
        var slotsResponse = await guestClient.GetAsync($"/api/calendar/slots?doctorId={_doctorId}&date={targetDate:yyyy-MM-dd}");
        slotsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Authenticated Patient submits booking form
        var patientClient = _factory.CreateAuthenticatedClient(PatientUserId, "Patient", fullName: "Ali Mahmoud");

        var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["doctorId"] = _doctorId.ToString(),
            ["appointmentDate"] = targetDate.ToString("yyyy-MM-dd"),
            ["startTime"] = "11:00:00",
            ["notes"] = "Experiencing chest discomfort after exercise",
            ["type"] = "Consultation"
        });

        var bookResponse = await patientClient.PostAsync("/Appointments/Book", formContent);

        // Assert redirect to MyAppointments
        bookResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        bookResponse.Headers.Location?.ToString().Should().Contain("MyAppointments");

        // Verify state in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.DoctorId == _doctorId && a.PatientId == _patientId && a.AppointmentDate.Date == targetDate.Date);

        appointment.Should().NotBeNull();
        appointment!.Status.Should().Be(AppointmentStatus.Pending);
        appointment.PaymentStatus.Should().Be(PaymentStatus.Unpaid);
        appointment.StartTime.Should().Be(TimeSpan.FromHours(11));
    }

    [Fact]
    public async Task Flow2_CancellationAndRescheduling_E2E()
    {
        // Setup existing appointment for patient
        int apptId = 0;
        var apptDate = new DateTime(2026, 11, 20); // Friday (well ahead of clock)
        await _factory.SeedAsync(async db =>
        {
            var appt = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = apptDate,
                StartTime = TimeSpan.FromHours(14),
                EndTime = TimeSpan.FromHours(14.5),
                Status = AppointmentStatus.Confirmed,
                PaymentStatus = PaymentStatus.Unpaid,
                ConsultationFee = 350m
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            apptId = appt.Id;
        });

        var patientClient = _factory.CreateAuthenticatedClient(PatientUserId, "Patient");
        var attackerClient = _factory.CreateAuthenticatedClient(OtherPatientUserId, "Patient");

        // 1. Attacker attempts to cancel someone else's appointment -> IDOR defense
        var attackerCancelResponse = await attackerClient.PostAsync($"/Appointments/Cancel/{apptId}", new FormUrlEncodedContent(new Dictionary<string, string>()));
        // In AppointmentsController, unauthorized cancel results in Error message in TempData and redirect
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var unchangedAppt = await db.Appointments.FindAsync(apptId);
            unchangedAppt!.Status.Should().Be(AppointmentStatus.Confirmed, "Attacker must not be able to cancel another patient's appointment");
        }

        // 2. Legitimate patient reschedules appointment
        var newDate = new DateTime(2026, 11, 23); // Next Monday
        var rescheduleContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["appointmentId"] = apptId.ToString(),
            ["newAppointmentDate"] = newDate.ToString("yyyy-MM-dd"),
            ["newStartTime"] = "16:00:00",
            ["reason"] = "Work schedule conflict"
        });

        var rescheduleResponse = await patientClient.PostAsync("/Appointments/Reschedule", rescheduleContent);
        rescheduleResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updatedAppt = await db.Appointments.FindAsync(apptId);
            updatedAppt!.AppointmentDate.Date.Should().Be(newDate.Date);
            updatedAppt.StartTime.Should().Be(TimeSpan.FromHours(16));
        }

        // 3. Legitimate patient cancels appointment
        var cancelResponse = await patientClient.PostAsync($"/Appointments/Cancel/{apptId}", new FormUrlEncodedContent(new Dictionary<string, string>()));
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cancelledAppt = await db.Appointments.FindAsync(apptId);
            cancelledAppt!.Status.Should().Be(AppointmentStatus.Cancelled);
        }
    }

    [Fact]
    public async Task Flow3_DoctorEncounterAndPrescription_E2E()
    {
        // Setup appointment ready for encounter (start time earlier than clock)
        int apptId = 0;
        var apptDate = new DateTime(2026, 11, 16); // Monday
        var apptTime = TimeSpan.FromHours(8.5);    // 08:30 (Clock is at 09:00)

        await _factory.SeedAsync(async db =>
        {
            var appt = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = apptDate,
                StartTime = apptTime,
                EndTime = TimeSpan.FromHours(9),
                Status = AppointmentStatus.Confirmed,
                PaymentStatus = PaymentStatus.Paid,
                ConsultationFee = 350m
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            apptId = appt.Id;
        });

        var doctorClient = _factory.CreateAuthenticatedClient(DoctorUserId, "Doctor");

        // 1. Doctor opens encounter page
        var encounterPageResponse = await doctorClient.GetAsync($"/MedicalRecords/Create/{apptId}");
        encounterPageResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Doctor documents consultation & issues prescription
        var encounterForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Encounter.AppointmentId"] = apptId.ToString(),
            ["Encounter.Diagnosis"] = "Mild Essential Hypertension",
            ["Encounter.TreatmentPlan"] = "Dietary DASH modification, low sodium, and follow-up in 4 weeks",
            ["Encounter.BloodPressure"] = "135/85",
            ["Encounter.HeartRate"] = "76",
            ["Encounter.Symptoms"] = "Occasional morning headaches",
            ["Encounter.PrescriptionNotes"] = "Take with water after breakfast",
            ["Encounter.PrescriptionItems[0].MedicationName"] = "Amlodipine 5mg",
            ["Encounter.PrescriptionItems[0].Dosage"] = "1 tablet daily",
            ["Encounter.PrescriptionItems[0].Frequency"] = "Once daily morning",
            ["Encounter.PrescriptionItems[0].DurationDays"] = "30"
        });

        var createResponse = await doctorClient.PostAsync("/MedicalRecords/Create", encounterForm);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // 3. Verify in database: MedicalRecord and Prescription generated
        int prescriptionId = 0;
        string verificationToken = string.Empty;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var record = await db.MedicalRecords
                .Include(r => r.Prescription)
                    .ThenInclude(p => p!.Items)
                .FirstOrDefaultAsync(r => r.AppointmentId == apptId);

            record.Should().NotBeNull();
            record!.Diagnosis.Should().Be("Mild Essential Hypertension");
            record.Prescription.Should().NotBeNull();
            record.Prescription!.VerificationToken.Should().HaveLength(32);
            record.Prescription.Items.Should().ContainSingle(i => i.MedicationName == "Amlodipine 5mg");

            prescriptionId = record.Prescription.Id;
            verificationToken = record.Prescription.VerificationToken;

            var updatedAppt = await db.Appointments.FindAsync(apptId);
            updatedAppt!.Status.Should().Be(AppointmentStatus.Completed);
        }

        // 4. Patient views digital prescription printable view
        var patientClient = _factory.CreateAuthenticatedClient(PatientUserId, "Patient");
        var printResponse = await patientClient.GetAsync($"/Prescriptions/Print/{prescriptionId}");
        printResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Flow4_AppointmentPayment_E2E()
    {
        // Setup confirmed unpaid appointment
        int apptId = 0;
        await _factory.SeedAsync(async db =>
        {
            var appt = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = new DateTime(2026, 11, 25),
                StartTime = TimeSpan.FromHours(10),
                EndTime = TimeSpan.FromHours(10.5),
                Status = AppointmentStatus.Confirmed,
                PaymentStatus = PaymentStatus.Unpaid,
                ConsultationFee = 300m
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            apptId = appt.Id;
        });

        var patientClient = _factory.CreateAuthenticatedClient(PatientUserId, "Patient");
        var attackerClient = _factory.CreateAuthenticatedClient(OtherPatientUserId, "Patient");

        // 1. Patient attempts to view receipt before payment -> redirected
        var prePaymentReceiptResponse = await patientClient.GetAsync($"/Payment/Receipt/{apptId}");
        prePaymentReceiptResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // 2. Attacker attempts payment on another patient's appointment -> Forbidden
        var attackerCheckout = new PaymentCheckoutRequestDto
        {
            AppointmentId = apptId,
            CardNumber = "4242424242424242",
            CardHolderName = "Attacker User",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "999"
        };
        var attackerResponse = await attackerClient.PostAsJsonAsync("/api/payment/checkout", attackerCheckout);
        attackerResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Legitimate patient pays with promo code DEPI2026 (20% discount)
        var legitimateCheckout = new PaymentCheckoutRequestDto
        {
            AppointmentId = apptId,
            CardNumber = "4242424242424242",
            CardHolderName = "Ali Mahmoud",
            ExpiryMonth = "12",
            ExpiryYear = "2029",
            Cvv = "123",
            PromoCode = "DEPI2026"
        };

        var checkoutResponse = await patientClient.PostAsJsonAsync("/api/payment/checkout", legitimateCheckout);
        checkoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Verify payment state in DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var paidAppt = await db.Appointments.FindAsync(apptId);
            paidAppt!.PaymentStatus.Should().Be(PaymentStatus.Paid);
        }

        // 5. Patient views completed receipt HTML
        var receiptHtmlResponse = await patientClient.GetAsync($"/Payment/Receipt/{apptId}");
        receiptHtmlResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Flow5_PrescriptionVerificationAndDispensing_E2E()
    {
        // Setup appointment with prescription ready for dispensing
        string token = string.Empty;
        int prescriptionId = 0;

        await _factory.SeedAsync(async db =>
        {
            var appt = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = new DateTime(2026, 11, 16),
                StartTime = TimeSpan.FromHours(8),
                EndTime = TimeSpan.FromHours(8.5),
                Status = AppointmentStatus.Completed,
                PaymentStatus = PaymentStatus.Paid,
                ConsultationFee = 350m
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();

            var medRec = new MedicalRecord
            {
                AppointmentId = appt.Id,
                DoctorId = _doctorId,
                PatientId = _patientId,
                Diagnosis = "Type 2 Diabetes"
            };
            db.MedicalRecords.Add(medRec);
            await db.SaveChangesAsync();

            token = Guid.NewGuid().ToString("N"); // 32 characters
            var presc = new Prescription
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                MedicalRecordId = medRec.Id,
                VerificationToken = token,
                PrescriptionDate = DateTime.UtcNow,
                IsDispensed = false,
                Items = new List<PrescriptionItem>
                {
                    new() { MedicationName = "Metformin 500mg", Dosage = "1 tab", Frequency = "Twice daily", DurationDays = 30 }
                }
            };
            db.Prescriptions.Add(presc);
            await db.SaveChangesAsync();
            prescriptionId = presc.Id;
        });

        // 1. Public anonymous guest verifies prescription via QR link
        var guestClient = _factory.CreateAnonymousClient();
        var verifyResponse = await guestClient.GetAsync($"/Prescriptions/Verify?token={token}");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Non-pharmacist (Patient user) attempts to access dispensing portal -> Forbidden
        var patientClient = _factory.CreateAuthenticatedClient(PatientUserId, "Patient");
        var patientDispenseResponse = await patientClient.GetAsync($"/Prescriptions/Dispense?token={token}");
        patientDispenseResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Licensed Pharmacist accesses dispensing portal
        var pharmacistClient = _factory.CreateAuthenticatedClient(PharmacistUserId, "Pharmacist", fullName: "Mona Pharmacist");
        var pharmacistGet = await pharmacistClient.GetAsync($"/Prescriptions/Dispense?token={token}");
        pharmacistGet.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Pharmacist dispenses prescription
        var dispenseForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["pharmacyNotes"] = "Dispensed 1 box of Metformin 500mg, patient advised on hypoglycemia symptoms."
        });

        var dispensePostResponse = await pharmacistClient.PostAsync("/Prescriptions/Dispense", dispenseForm);
        dispensePostResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // 5. Verify state in DB: IsDispensed == true
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dispensedPrescription = await db.Prescriptions.FindAsync(prescriptionId);
            dispensedPrescription!.IsDispensed.Should().BeTrue();
            dispensedPrescription.DispensedAt.Should().NotBeNull();
            dispensedPrescription.PharmacyNotes.Should().Contain("Metformin");
        }

        // 6. Pharmacist attempts to dispense again -> fails (double-dispense defense)
        var doubleDispenseResponse = await pharmacistClient.PostAsync("/Prescriptions/Dispense", dispenseForm);
        doubleDispenseResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // Check verification page shows already dispensed
        var reVerifyResponse = await guestClient.GetAsync($"/Prescriptions/Verify?token={token}");
        reVerifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
