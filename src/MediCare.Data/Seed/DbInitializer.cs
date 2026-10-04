using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediCare.Data.Seed;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetService<ILogger<ApplicationDbContext>>();

        // Auto-migrate database if pending
        if ((await context.Database.GetPendingMigrationsAsync()).Any())
        {
            await context.Database.MigrateAsync();
        }

        // Avoid re-seeding if data already exists
        if (await context.Specializations.AnyAsync() && await context.Users.AnyAsync())
        {
            return;
        }

        var defaultPassword = configuration["Seed:DefaultPassword"] ?? "P@ssword123!";
        var adminPassword = configuration["Seed:AdminPassword"] ?? defaultPassword;

        // 1. Seed Roles
        string[] roles = { "Admin", "Doctor", "Patient" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // 2. Seed Administrator
        const string adminEmail = "admin@medicare.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "System Administrator",
                PhoneNumber = "+201000000001",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }

        // 3. Seed 5 Specializations
        var specs = new List<Specialization>
        {
            new() { Name = "Cardiology", Description = "Cardiovascular health, coronary diseases, heart failure, and hypertension management." },
            new() { Name = "Dermatology", Description = "Skin disorders, pediatric dermatology, cosmetic treatments, and rash care." },
            new() { Name = "Pediatrics", Description = "Comprehensive medical care and developmental tracking for infants, children, and adolescents." },
            new() { Name = "Orthopedics", Description = "Musculoskeletal injuries, joints, sports medicine, spine care, and fracture treatment." },
            new() { Name = "General Internal Medicine", Description = "Adult primary care, wellness examinations, chronic disease prevention, and diagnostics." }
        };

        if (!await context.Specializations.AnyAsync())
        {
            await context.Specializations.AddRangeAsync(specs);
            await context.SaveChangesAsync();
        }

        var cardiology = await context.Specializations.FirstAsync(s => s.Name == "Cardiology");
        var dermatology = await context.Specializations.FirstAsync(s => s.Name == "Dermatology");
        var pediatrics = await context.Specializations.FirstAsync(s => s.Name == "Pediatrics");
        var orthopedics = await context.Specializations.FirstAsync(s => s.Name == "Orthopedics");
        var internalMed = await context.Specializations.FirstAsync(s => s.Name == "General Internal Medicine");

        // 4. Seed 5 Approved Doctors
        var doctorsData = new[]
        {
            new {
                Email = "ahmed.mahmoud@medicare.com",
                FullName = "Dr. Ahmed Mahmoud",
                Phone = "+201011112222",
                SpecId = cardiology.Id,
                License = "EGY-MED-2015-4421",
                Fee = 300.00m,
                Bio = "Senior Consultant in Interventional Cardiology with over 12 years of specialized clinical experience in heart failure and catheterization.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "sara.alsayed@medicare.com",
                FullName = "Dr. Sara Al-Sayed",
                Phone = "+201022223333",
                SpecId = dermatology.Id,
                License = "EGY-MED-2017-8892",
                Fee = 250.00m,
                Bio = "Specialist in Clinical & Aesthetic Dermatology with international European fellowship in laser and autoimmune skin therapies.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },
            new {
                Email = "youssef.nabil@medicare.com",
                FullName = "Dr. Youssef Nabil",
                Phone = "+201033334444",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2014-1109",
                Fee = 200.00m,
                Bio = "Consultant Pediatrician dedicated to neonatal development, immunizations, and pediatric pulmonary diseases.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) },
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) }
                }
            },
            new {
                Email = "mona.mansour@medicare.com",
                FullName = "Dr. Mona Mansour",
                Phone = "+201044445555",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2016-5531",
                Fee = 350.00m,
                Bio = "Orthopedic Surgeon specializing in knee arthroscopy, sports injury rehabilitation, and joint reconstructive procedures.",
                Hours = new[] {
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) }
                }
            },
            new {
                Email = "tarek.ezzat@medicare.com",
                FullName = "Dr. Tarek Ezzat",
                Phone = "+201055556666",
                SpecId = internalMed.Id,
                License = "EGY-MED-2012-7744",
                Fee = 180.00m,
                Bio = "Internal Medicine Consultant focusing on adult hypertension, diabetes mellitus type 2, and preventive health screenings.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            }
        };

        var doctorsList = new List<Doctor>();
        foreach (var docData in doctorsData)
        {
            var user = await userManager.FindByEmailAsync(docData.Email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = docData.Email,
                    Email = docData.Email,
                    FullName = docData.FullName,
                    PhoneNumber = docData.Phone,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                await userManager.CreateAsync(user, defaultPassword);
                await userManager.AddToRoleAsync(user, "Doctor");
            }

            var doctor = await context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.Id);
            if (doctor == null)
            {
                doctor = new Doctor
                {
                    UserId = user.Id,
                    SpecializationId = docData.SpecId,
                    LicenseNumber = docData.License,
                    ConsultationFee = docData.Fee,
                    SlotDurationMinutes = 30,
                    IsApproved = true,
                    Bio = docData.Bio,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Doctors.AddAsync(doctor);
                await context.SaveChangesAsync();

                foreach (var h in docData.Hours)
                {
                    await context.WorkingHours.AddAsync(new WorkingHours
                    {
                        DoctorId = doctor.Id,
                        DayOfWeek = h.Day,
                        StartTime = h.Start,
                        EndTime = h.End,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                await context.SaveChangesAsync();
            }
            doctorsList.Add(doctor);
        }

        // Add 1 sample doctor leave
        var firstDoc = doctorsList.First();
        if (!await context.DoctorLeaves.AnyAsync(l => l.DoctorId == firstDoc.Id))
        {
            await context.DoctorLeaves.AddAsync(new DoctorLeave
            {
                DoctorId = firstDoc.Id,
                StartDate = DateTime.UtcNow.Date.AddDays(20),
                EndDate = DateTime.UtcNow.Date.AddDays(24),
                Reason = "Annual Medical Cardiology Conference Attendance",
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // 5. Seed 5 Patients
        var patientsData = new[]
        {
            new { Email = "khaled.omar@medicare.com", Name = "Khaled Omar", Phone = "+201099112233", DoB = new DateTime(1988, 4, 12), Gender = "Male", Blood = "A+", Contact = "+201099112234" },
            new { Email = "nourhan.ali@medicare.com", Name = "Nourhan Ali", Phone = "+201088223344", DoB = new DateTime(1995, 9, 25), Gender = "Female", Blood = "O+", Contact = "+201088223345" },
            new { Email = "mostafa.hassan@medicare.com", Name = "Mostafa Hassan", Phone = "+201077334455", DoB = new DateTime(1978, 11, 3), Gender = "Male", Blood = "B+", Contact = "+201077334456" },
            new { Email = "dina.fathy@medicare.com", Name = "Dina Fathy", Phone = "+201066445566", DoB = new DateTime(2001, 2, 18), Gender = "Female", Blood = "AB+", Contact = "+201066445567" },
            new { Email = "mohamed.selim@medicare.com", Name = "Mohamed Selim", Phone = "+201055556677", DoB = new DateTime(1992, 7, 30), Gender = "Male", Blood = "O-", Contact = "+201055556678" }
        };

        var patientsList = new List<Patient>();
        foreach (var pData in patientsData)
        {
            var user = await userManager.FindByEmailAsync(pData.Email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = pData.Email,
                    Email = pData.Email,
                    FullName = pData.Name,
                    PhoneNumber = pData.Phone,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                await userManager.CreateAsync(user, defaultPassword);
                await userManager.AddToRoleAsync(user, "Patient");
            }

            var patient = await context.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (patient == null)
            {
                patient = new Patient
                {
                    UserId = user.Id,
                    DateOfBirth = pData.DoB,
                    Gender = pData.Gender,
                    BloodGroup = pData.Blood,
                    EmergencyContact = pData.Contact,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Patients.AddAsync(patient);
                await context.SaveChangesAsync();
            }
            patientsList.Add(patient);
        }

        // 6. Seed 20+ Past Appointments with Medical Records and Prescriptions
        if (!await context.Appointments.AnyAsync())
        {
            var basePastDate = DateTime.UtcNow.Date.AddDays(-28);
            var apptCount = 0;

            for (int i = 0; i < 22; i++)
            {
                var doc = doctorsList[i % doctorsList.Count];
                var pat = patientsList[i % patientsList.Count];
                var date = basePastDate.AddDays(i);
                var startHour = 9 + (i % 6);
                var startTime = new TimeSpan(startHour, 0, 0);
                var endTime = startTime.Add(TimeSpan.FromMinutes(30));

                var appt = new Appointment
                {
                    DoctorId = doc.Id,
                    PatientId = pat.Id,
                    AppointmentDate = date,
                    StartTime = startTime,
                    EndTime = endTime,
                    Status = AppointmentStatus.Completed,
                    ConsultationFee = doc.ConsultationFee,
                    PaymentStatus = PaymentStatus.Paid,
                    Type = AppointmentType.Consultation,
                    Notes = $"Routine medical clinical follow-up for visit {i + 1}.",
                    CreatedAt = date.AddHours(-12)
                };
                await context.Appointments.AddAsync(appt);
                await context.SaveChangesAsync();

                var medRecord = new MedicalRecord
                {
                    AppointmentId = appt.Id,
                    DoctorId = doc.Id,
                    PatientId = pat.Id,
                    Diagnosis = $"Diagnosis summary: Stage { (i % 2) + 1 } clinical assessment for {doc.Specialization.Name}.",
                    Symptoms = "Mild recurrent fatigue, elevated blood pressure, localized tension.",
                    VisitNotes = "Patient advised regular hydration, lifestyle modifications, and prescribed medical treatment regimen.",
                    CreatedAt = date.AddMinutes(35)
                };
                await context.MedicalRecords.AddAsync(medRecord);
                await context.SaveChangesAsync();

                var prescription = new Prescription
                {
                    MedicalRecordId = medRecord.Id,
                    DoctorId = doc.Id,
                    PatientId = pat.Id,
                    PrescriptionDate = date.AddMinutes(35),
                    Notes = "Take all medications strictly as directed. Contact clinic if adverse reactions appear.",
                    CreatedAt = date.AddMinutes(35)
                };
                await context.Prescriptions.AddAsync(prescription);
                await context.SaveChangesAsync();

                var items = new List<PrescriptionItem>
                {
                    new()
                    {
                        PrescriptionId = prescription.Id,
                        MedicationName = "Amoxicillin 500mg",
                        Dosage = "500 mg",
                        Frequency = "Every 8 hours",
                        DurationDays = 7,
                        Instructions = "Take orally after meals with a full glass of water.",
                        CreatedAt = date.AddMinutes(35)
                    },
                    new()
                    {
                        PrescriptionId = prescription.Id,
                        MedicationName = "Panadol Extra",
                        Dosage = "500 mg / 65 mg",
                        Frequency = "Twice daily as needed",
                        DurationDays = 5,
                        Instructions = "For symptomatic pain relief; do not exceed 4000mg paracetamol daily.",
                        CreatedAt = date.AddMinutes(35)
                    }
                };
                await context.PrescriptionItems.AddRangeAsync(items);
                await context.SaveChangesAsync();
                apptCount++;
            }

            // 7. Seed 5 Upcoming Appointments
            var futureDate = DateTime.UtcNow.Date.AddDays(2);
            for (int j = 0; j < 5; j++)
            {
                var doc = doctorsList[j % doctorsList.Count];
                var pat = patientsList[(j + 2) % patientsList.Count];
                var apptDate = futureDate.AddDays(j);
                var startTime = new TimeSpan(10 + j, 0, 0);

                var futureAppt = new Appointment
                {
                    DoctorId = doc.Id,
                    PatientId = pat.Id,
                    AppointmentDate = apptDate,
                    StartTime = startTime,
                    EndTime = startTime.Add(TimeSpan.FromMinutes(30)),
                    Status = j % 2 == 0 ? AppointmentStatus.Confirmed : AppointmentStatus.Pending,
                    ConsultationFee = doc.ConsultationFee,
                    PaymentStatus = PaymentStatus.Unpaid,
                    Type = AppointmentType.Consultation,
                    Notes = "Pre-booked upcoming consultation.",
                    CreatedAt = DateTime.UtcNow
                };
                await context.Appointments.AddAsync(futureAppt);
            }
            await context.SaveChangesAsync();

            logger?.LogInformation("DbInitializer: Successfully seeded database with Admin, Doctors, Patients, and Appointments.");
        }
    }
}
