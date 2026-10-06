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

        // If database already initialized, ensure doctor profile photos and pending admin approvals exist
        if (await context.Specializations.AnyAsync() && await context.Users.AnyAsync())
        {
            await EnsureDoctorPhotosAndPendingDoctorAsync(context, userManager, configuration);
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

    private static async Task EnsureDoctorPhotosAndPendingDoctorAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration)
    {
        var defaultPassword = configuration["Seed:DefaultPassword"] ?? "P@ssword123!";
        var cardiology = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "Cardiology");
        var dermatology = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "Dermatology");
        var pediatrics = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "Pediatrics");
        var orthopedics = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "Orthopedics");
        var internalMed = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "General Internal Medicine");

        if (cardiology == null || dermatology == null || pediatrics == null || orthopedics == null || internalMed == null)
            return;

        var fullDoctorsCohort = new[]
        {
            new {
                Email = "ahmed.mahmoud@medicare.com",
                FullName = "Dr. Ahmed Mahmoud",
                Phone = "+201011112222",
                SpecId = cardiology.Id,
                License = "EGY-MED-2015-4421",
                Fee = 350.00m,
                Photo = "https://images.unsplash.com/photo-1622253692010-333f2da6031d?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ واستشاري أمراض القلب والقسطرة التداخلية بكلية الطب جامعة عين شمس، زميل جمعية القلب الأمريكية. عيادة المعادي، القاهرة.",
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
                Fee = 280.00m,
                Photo = "https://images.unsplash.com/photo-1594824813588-44243a41e976?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، زمالة الأكاديمية الأوروبية للأمراض الجلدية (EADV). عيادة سموحة، الإسكندرية.",
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
                Fee = 250.00m,
                Photo = "https://images.unsplash.com/photo-1537368910025-700350fe46c7?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أول طب الأطفال وحديثي الولادة وأمراض الجهاز التنفسي والحساسية لدى الأطفال، جامعة الإسكندرية. عيادة لوران، الإسكندرية.",
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
                Fee = 400.00m,
                Photo = "https://images.unsplash.com/photo-1559839734-2b71ea197ec2?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري جراحة العظام والعمود الفقري ومناظير المفاصل وإصابات الملاعب، كلية الطب القصر العيني. عيادة التجمع الخامس، القاهرة.",
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
                Fee = 220.00m,
                Photo = "https://images.unsplash.com/photo-1612349317150-e413f6a5b16d?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ أمراض الباطنة العامة والسكر والجهاز الهضمي، كلية الطب جامعة القاهرة. عيادة المهندسين، الجيزة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "nourhan.elshazly@medicare.com",
                FullName = "Dr. Nourhan El-Shazly",
                Phone = "+201066667777",
                SpecId = cardiology.Id,
                License = "EGY-MED-2018-2041",
                Fee = 280.00m,
                Photo = "https://images.unsplash.com/photo-1527613426441-4da17471b66d?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض القلب والأوعية الدموية وقصور الشرايين التاجية، جامعة المنصورة. شارع المشاية السفلية، المنصورة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },
            new {
                Email = "mohamed.eldesouky@medicare.com",
                FullName = "Dr. Mohamed El-Desouky",
                Phone = "+201077778888",
                SpecId = internalMed.Id,
                License = "EGY-MED-2011-8812",
                Fee = 320.00m,
                Photo = "https://images.unsplash.com/photo-1582750433449-648ed127bb54?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ أمراض الباطنة والكلى وارتفاع ضغط الدم، مركز الكلى والمسالك البولية بالمنصورة. شارع الجمهورية، المنصورة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "reham.abdelaziz@medicare.com",
                FullName = "Dr. Reham Abdel-Aziz",
                Phone = "+201088889999",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2017-3190",
                Fee = 260.00m,
                Photo = "https://images.unsplash.com/photo-1551836022-d5d88e9218df?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري طب الأطفال وحديثي الولادة والتغذية السريرية، البورد العربي في طب الأطفال. مصر الجديدة، القاهرة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) }
                }
            },
            new {
                Email = "khaled.elnaggar@medicare.com",
                FullName = "Dr. Khaled El-Naggar",
                Phone = "+201099990000",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2013-4412",
                Fee = 300.00m,
                Photo = "https://images.unsplash.com/photo-1622902046580-2b47f47f5471?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري جراحة العظام والمفاصل الصناعية وعلاج الكسور المعقدة، جامعة طنطا. شارع البحر، طنطا.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(19, 0, 0) }
                }
            },
            new {
                Email = "salma.elgohary@medicare.com",
                FullName = "Dr. Salma El-Gohary",
                Phone = "+201012345678",
                SpecId = dermatology.Id,
                License = "EGY-MED-2019-7711",
                Fee = 240.00m,
                Photo = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=400&q=80",
                Bio = "أخصائي الأمراض الجلدية والعلاج الضوئي والليزر، جامعة طنطا. شارع النحاس، طنطا.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "mostafa.abdelrahman@medicare.com",
                FullName = "Dr. Mostafa Abdel-Rahman",
                Phone = "+201023456789",
                SpecId = internalMed.Id,
                License = "EGY-MED-2010-9923",
                Fee = 270.00m,
                Photo = "https://images.unsplash.com/photo-1622253694242-abdb3c8b4b74?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ ورئيس قسم الباطنة العامة ومناظير الجهاز الهضمي، مستشفيات جامعة أسيوط. شارع النميس، أسيوط.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(16, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(16, 0, 0) }
                }
            },
            new {
                Email = "hoda.elqousi@medicare.com",
                FullName = "Dr. Hoda El-Qousi",
                Phone = "+201034567890",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2016-1890",
                Fee = 220.00m,
                Photo = "https://images.unsplash.com/photo-1594824813576-96b4ba63c5d6?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري طب الأطفال والمبتسرين وأمراض الحساسية والمناعة، كلية الطب جامعة أسيوط. شارع يسري راغب، أسيوط.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) }
                }
            },
            new {
                Email = "hazem.elsawy@medicare.com",
                FullName = "Dr. Hazem El-Sawy",
                Phone = "+201045678901",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2012-6634",
                Fee = 380.00m,
                Photo = "https://images.unsplash.com/photo-1537368910025-700350fe46c7?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري جراحة العظام والعمود الفقري وإصابات الملاعب، زميل الجمعية السويسرية لجراحة العظام (AO). الدقي، الجيزة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) }
                }
            },
            new {
                Email = "aya.elbaz@medicare.com",
                FullName = "Dr. Aya El-Baz",
                Phone = "+201056789012",
                SpecId = cardiology.Id,
                License = "EGY-MED-2018-5021",
                Fee = 260.00m,
                Photo = "https://images.unsplash.com/photo-1584467735871-8e85353a8413?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض القلب والإيكو المتقدم والضغط والشرايين، كلية الطب جامعة الزقازيق. شارع القومية، الزقازيق.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "maryam.salama@medicare.com",
                FullName = "Dr. Maryam Salama",
                Phone = "+201067890123",
                SpecId = dermatology.Id,
                License = "EGY-MED-2017-9102",
                Fee = 240.00m,
                Photo = "https://images.unsplash.com/photo-1614608682850-e0d6ed316d47?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري الأمراض الجلدية والتناسلية والعلاج بالليزر، جامعة قناة السويس. حي الشيخ زايد، الإسماعيلية.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "ibrahim.elhawary@medicare.com",
                FullName = "Dr. Ibrahim El-Hawary",
                Phone = "+201078901234",
                SpecId = internalMed.Id,
                License = "EGY-MED-2014-4320",
                Fee = 200.00m,
                Photo = "https://images.unsplash.com/photo-1582750433449-648ed127bb54?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أول أمراض الباطنة والغدد الصماء والسكري ومتابعة القدم السكري، جامعة سوهاج. شارع 15 مايو، سوهاج.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) }
                }
            }
        };

        foreach (var dData in fullDoctorsCohort)
        {
            var user = await userManager.FindByEmailAsync(dData.Email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = dData.Email,
                    Email = dData.Email,
                    FullName = dData.FullName,
                    PhoneNumber = dData.Phone,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                var createResult = await userManager.CreateAsync(user, defaultPassword);
                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, "Doctor");
                }
            }

            var doctor = await context.Doctors.Include(d => d.WorkingHours).FirstOrDefaultAsync(d => d.UserId == user.Id);
            if (doctor == null)
            {
                doctor = new Doctor
                {
                    UserId = user.Id,
                    SpecializationId = dData.SpecId,
                    LicenseNumber = dData.License,
                    ConsultationFee = dData.Fee,
                    SlotDurationMinutes = 30,
                    IsApproved = true,
                    ProfileImageUrl = dData.Photo,
                    Bio = dData.Bio,
                    CreatedAt = DateTime.UtcNow
                };
                await context.Doctors.AddAsync(doctor);
                await context.SaveChangesAsync();

                foreach (var h in dData.Hours)
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
            else
            {
                // Update photo & bio if changed
                doctor.ProfileImageUrl = dData.Photo;
                doctor.Bio = dData.Bio;
                doctor.ConsultationFee = dData.Fee;
                doctor.IsApproved = true;
                context.Doctors.Update(doctor);
            }
        }

        // Ensure at least 1 pending doctor exists for Admin Approval demo
        if (!await context.Doctors.AnyAsync(d => !d.IsApproved))
        {
            const string pendingEmail = "kareem.zaki@medicare.com";
            var pendingUser = await userManager.FindByEmailAsync(pendingEmail);
            if (pendingUser == null)
            {
                pendingUser = new ApplicationUser
                {
                    UserName = pendingEmail,
                    Email = pendingEmail,
                    FullName = "Dr. Kareem Zaki",
                    PhoneNumber = "+201066778899",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };
                await userManager.CreateAsync(pendingUser, defaultPassword);
                await userManager.AddToRoleAsync(pendingUser, "Doctor");
            }

            var cardSpec = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "Cardiology");
            var pendingDoc = new Doctor
            {
                UserId = pendingUser.Id,
                SpecializationId = cardSpec?.Id ?? cardiology.Id,
                LicenseNumber = "EGY-MED-2024-9988",
                ConsultationFee = 250.00m,
                SlotDurationMinutes = 30,
                IsApproved = false,
                ProfileImageUrl = "https://images.unsplash.com/photo-1582750433449-648ed127bb54?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض القلب والأوعية الدموية، متقدم بطلب الاعتماد السريري بشبكة عيادات ميدي كير. بورسعيد.",
                CreatedAt = DateTime.UtcNow
            };
            await context.Doctors.AddAsync(pendingDoc);
        }

        await context.SaveChangesAsync();
    }
}
