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

        // Ensure expanded specializations exist
        var extraSpecs = new List<Specialization>
        {
            new() { Name = "Ophthalmology", Description = "Eye care, cataract surgery, refractive surgery (LASIK), and retinal care." },
            new() { Name = "Obstetrics & Gynecology", Description = "Women's health, prenatal care, high-risk pregnancy, and reproductive health." },
            new() { Name = "Neurology", Description = "Disorders of the nervous system, brain, stroke care, and neuro-rehabilitation." },
            new() { Name = "ENT / Ear, Nose & Throat", Description = "Ear infections, sinus endoscopy, throat disorders, and audiology." },
            new() { Name = "General Surgery", Description = "Laparoscopic procedures, tumor surgery, digestive care, and hernia repairs." }
        };

        foreach (var s in extraSpecs)
        {
            if (!await context.Specializations.AnyAsync(e => e.Name == s.Name))
            {
                await context.Specializations.AddAsync(s);
            }
        }
        await context.SaveChangesAsync();

        var cardiology = await context.Specializations.FirstAsync(s => s.Name == "Cardiology");
        var dermatology = await context.Specializations.FirstAsync(s => s.Name == "Dermatology");
        var pediatrics = await context.Specializations.FirstAsync(s => s.Name == "Pediatrics");
        var orthopedics = await context.Specializations.FirstAsync(s => s.Name == "Orthopedics");
        var internalMed = await context.Specializations.FirstAsync(s => s.Name == "General Internal Medicine");
        var ophthalmology = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "Ophthalmology") ?? internalMed;
        var obGyn = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "Obstetrics & Gynecology") ?? pediatrics;
        var neurology = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "Neurology") ?? internalMed;
        var ent = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "ENT / Ear, Nose & Throat") ?? internalMed;
        var surgery = await context.Specializations.FirstOrDefaultAsync(s => s.Name == "General Surgery") ?? orthopedics;

        var fullDoctorsCohort = new[]
        {
            // --- 1. القاهرة (Cairo) ---
            new {
                Email = "ahmed.mahmoud@medicare.com",
                FullName = "Dr. Ahmed Mahmoud",
                Phone = "+201011112222",
                SpecId = cardiology.Id,
                License = "EGY-MED-2015-4421",
                Fee = 350.00m,
                Photo = "https://images.unsplash.com/photo-1622253692010-333f2da6031d?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ واستشاري أمراض القلب والقسطرة التداخلية بكلية الطب جامعة عين شمس، زميل جمعية القلب الأمريكية. عيادة المعادي، شارع النصر، القاهرة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }
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
                Bio = "استشاري جراحة العظام والعمود الفقري ومناظير المفاصل وإصابات الملاعب، كلية الطب القصر العيني. التجمع الخامس، شارع التسعين، القاهرة الجديدة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(20, 0, 0) }
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
                Bio = "استشاري طب الأطفال وحديثي الولادة والتغذية السريرية، البورد العربي في طب الأطفال. ميدان روكسي، مصر الجديدة، القاهرة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) }
                }
            },

            // --- 2. الجيزة (Giza) ---
            new {
                Email = "tarek.ezzat@medicare.com",
                FullName = "Dr. Tarek Ezzat",
                Phone = "+201055556666",
                SpecId = internalMed.Id,
                License = "EGY-MED-2012-7744",
                Fee = 280.00m,
                Photo = "https://images.unsplash.com/photo-1612349317150-e413f6a5b16d?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ أمراض الباطنة العامة والسكر والجهاز الهضمي، كلية الطب جامعة القاهرة. شارع مصدق، الدقي، الجيزة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(17, 0, 0) }
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
                Bio = "استشاري جراحة العظام والعمود الفقري وإصابات الملاعب، زميل الجمعية السويسرية لجراحة العظام (AO). ميدان مصطفى محمود، المهندسين، الجيزة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(21, 0, 0) }
                }
            },
            new {
                Email = "dalia.farouk@medicare.com",
                FullName = "Dr. Dalia Farouk",
                Phone = "+201033221144",
                SpecId = dermatology.Id,
                License = "EGY-MED-2018-9901",
                Fee = 300.00m,
                Photo = "https://images.unsplash.com/photo-1594824813588-44243a41e976?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، زمالة الأكاديمية الأمريكية للأمراض الجلدية (AAD). مجمع زايد الطبي، الشيخ زايد، الجيزة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },

            // --- 3. الإسكندرية (Alexandria) ---
            new {
                Email = "sara.alsayed@medicare.com",
                FullName = "Dr. Sara Al-Sayed",
                Phone = "+201022223333",
                SpecId = dermatology.Id,
                License = "EGY-MED-2017-8892",
                Fee = 280.00m,
                Photo = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، زمالة الأكاديمية الأوروبية للأمراض الجلدية (EADV). سموحة، ميدان فيكتور عمانويل، الإسكندرية.",
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
                Photo = "https://images.unsplash.com/photo-1582750433449-648ed127bb54?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أول طب الأطفال وحديثي الولادة وأمراض الجهاز التنفسي والحساسية، جامعة الإسكندرية. طريق الحرية، لوران، الإسكندرية.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) }
                }
            },
            new {
                Email = "sherif.ghoneim@medicare.com",
                FullName = "Dr. Sherif Ghoneim",
                Phone = "+201088776655",
                SpecId = cardiology.Id,
                License = "EGY-MED-2013-3342",
                Fee = 320.00m,
                Photo = "https://images.unsplash.com/photo-1622253692010-333f2da6031d?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض القلب وقصور الشرايين التاجية والقسطرة، كلية الطب جامعة الإسكندرية. شارع المشير أحمد إسماعيل، سيدي جابر، الإسكندرية.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(19, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(19, 0, 0) }
                }
            },

            // --- 4. المنصورة (Mansoura) ---
            new {
                Email = "nourhan.elshazly@medicare.com",
                FullName = "Dr. Nourhan El-Shazly",
                Phone = "+201066667777",
                SpecId = cardiology.Id,
                License = "EGY-MED-2018-2041",
                Fee = 280.00m,
                Photo = "https://images.unsplash.com/photo-1527613426441-4da17471b66d?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض القلب والأوعية الدموية وقصور الشرايين التاجية، جامعة المنصورة. المشاية السفلية، أمام نادي جزيرة الورد، المنصورة.",
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
                Photo = "https://images.unsplash.com/photo-1622902046580-2b47f47f5471?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ أمراض الباطنة والكلى وارتفاع ضغط الدم، مركز الكلى والمسالك البولية بالمنصورة. شارع الجمهورية، المنصورة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "rania.eldeeb@medicare.com",
                FullName = "Dr. Rania El-Deeb",
                Phone = "+201099887766",
                SpecId = ophthalmology.Id,
                License = "EGY-MED-2016-4432",
                Fee = 260.00m,
                Photo = "https://images.unsplash.com/photo-1584467735871-8e85353a8413?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري طب وجراحة العيون وتصحيح الإبصار بالليزك، مركز العيون الدولي، جامعة المنصورة. حي توريل، شارع سعد زغلول، المنصورة.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },

            // --- 5. طنطا (Tanta) ---
            new {
                Email = "khaled.elnaggar@medicare.com",
                FullName = "Dr. Khaled El-Naggar",
                Phone = "+201099990000",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2013-4412",
                Fee = 300.00m,
                Photo = "https://images.unsplash.com/photo-1612349317150-e413f6a5b16d?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري جراحة العظام والمفاصل الصناعية وعلاج الكسور المعقدة، جامعة طنطا. شارع البحر، أمام المحافظة، طنطا.",
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
                Photo = "https://images.unsplash.com/photo-1594824813576-96b4ba63c5d6?auto=format&fit=crop&w=400&q=80",
                Bio = "أخصائي الأمراض الجلدية والعلاج الضوئي والليزر، جامعة طنطا. شارع النحاس مع المحطة، طنطا.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "essam.metwally@medicare.com",
                FullName = "Dr. Essam Metwally",
                Phone = "+201044556677",
                SpecId = ent.Id,
                License = "EGY-MED-2014-9988",
                Fee = 250.00m,
                Photo = "https://images.unsplash.com/photo-1537368910025-700350fe46c7?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري جراحة الأنف والأذن والحنجرة ومناظير الجيوب الأنفية، جامعة طنطا. ميدان الساعة، برج الأطباء، طنطا.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) }
                }
            },

            // --- 6. أسيوط (Assiut) ---
            new {
                Email = "mostafa.abdelrahman@medicare.com",
                FullName = "Dr. Mostafa Abdel-Rahman",
                Phone = "+201023456789",
                SpecId = internalMed.Id,
                License = "EGY-MED-2010-9923",
                Fee = 270.00m,
                Photo = "https://images.unsplash.com/photo-1622253694242-abdb3c8b4b74?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ ورئيس قسم الباطنة العامة ومناظير الجهاز الهضمي، مستشفيات جامعة أسيوط. شارع النميس، برج الأطباء، أسيوط.",
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
                Photo = "https://images.unsplash.com/photo-1614608682850-e0d6ed316d47?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري طب الأطفال والمبتسرين وأمراض الحساسية والمناعة، كلية الطب جامعة أسيوط. شارع يسري راغب، أسيوط.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) }
                }
            },
            new {
                Email = "ahmed.elghamry@medicare.com",
                FullName = "Dr. Ahmed El-Ghamry",
                Phone = "+201055667788",
                SpecId = cardiology.Id,
                License = "EGY-MED-2015-6677",
                Fee = 260.00m,
                Photo = "https://images.unsplash.com/photo-1622253692010-333f2da6031d?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض القلب والأوعية الدموية والقسطرة القلبية، جامعة أسيوط. شارع الهلالي، برج الأطباء، أسيوط.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },

            // --- 7. الزقازيق (Zagazig) ---
            new {
                Email = "aya.elbaz@medicare.com",
                FullName = "Dr. Aya El-Baz",
                Phone = "+201056789012",
                SpecId = cardiology.Id,
                License = "EGY-MED-2018-5021",
                Fee = 260.00m,
                Photo = "https://images.unsplash.com/photo-1559839734-2b71ea197ec2?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض القلب والإيكو المتقدم والضغط والشرايين، كلية الطب جامعة الزقازيق. شارع القومية، برج الأطباء، الزقازيق.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "maged.elsayad@medicare.com",
                FullName = "Dr. Maged El-Sayad",
                Phone = "+201011335577",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2014-3321",
                Fee = 280.00m,
                Photo = "https://images.unsplash.com/photo-1537368910025-700350fe46c7?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري جراحة العظام والمفاصل والعمود الفقري ومناظير الركبة، جامعة الزقازيق. شارع المحافظة، الزقازيق.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) }
                }
            },
            new {
                Email = "walaa.attia@medicare.com",
                FullName = "Dr. Walaa Attia",
                Phone = "+201022446688",
                SpecId = dermatology.Id,
                License = "EGY-MED-2019-1123",
                Fee = 240.00m,
                Photo = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري الأمراض الجلدية والليزر وتجميل الجلد، كلية الطب جامعة الزقازيق. شارع سعد زغلول، برج النخبة، الزقازيق.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },

            // --- 8. بنها (Banha) ---
            new {
                Email = "medhat.elnemr@medicare.com",
                FullName = "Dr. Medhat El-Nemr",
                Phone = "+201033557799",
                SpecId = cardiology.Id,
                License = "EGY-MED-2013-8877",
                Fee = 290.00m,
                Photo = "https://images.unsplash.com/photo-1622253692010-333f2da6031d?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ واستشاري أمراض القلب والقسطرة التداخلية، كلية الطب جامعة بنها. شارع فريد ندا، برج الأطباء، بنها.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "naglaa.qoura@medicare.com",
                FullName = "Dr. Naglaa Qoura",
                Phone = "+201044668800",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2017-5544",
                Fee = 230.00m,
                Photo = "https://images.unsplash.com/photo-1551836022-d5d88e9218df?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري طب الأطفال وحديثي الولادة ورعاية المبتسرين، جامعة بنها. شارع سعد زغلول، ميدان المحطة، بنها.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },
            new {
                Email = "omar.khedr@medicare.com",
                FullName = "Dr. Omar Khedr",
                Phone = "+201055779911",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2015-2211",
                Fee = 270.00m,
                Photo = "https://images.unsplash.com/photo-1612349317150-e413f6a5b16d?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري جراحة العظام والمفاصل ومناظير الركبة والكتف، جامعة بنها. شارع الأهرام، برج الأطباء، بنها.",
                Hours = new[] {
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) }
                }
            },

            // --- 9. الإسماعيلية (Ismailia) ---
            new {
                Email = "maryam.salama@medicare.com",
                FullName = "Dr. Maryam Salama",
                Phone = "+201067890123",
                SpecId = dermatology.Id,
                License = "EGY-MED-2017-9102",
                Fee = 240.00m,
                Photo = "https://images.unsplash.com/photo-1614608682850-e0d6ed316d47?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري الأمراض الجلدية والتناسلية والعلاج بالليزر، جامعة قناة السويس. حي الشيخ زايد، الشارع التجاري، الإسماعيلية.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "bassem.elattar@medicare.com",
                FullName = "Dr. Bassem El-Attar",
                Phone = "+201066880022",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2014-7765",
                Fee = 260.00m,
                Photo = "https://images.unsplash.com/photo-1537368910025-700350fe46c7?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري جراحة العظام ومناظير المفاصل وإصابات الملاعب، جامعة قناة السويس. شارع شبين الكوم، الإسماعيلية.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },
            new {
                Email = "fatma.shalaby@medicare.com",
                FullName = "Dr. Fatma Shalaby",
                Phone = "+201077991133",
                SpecId = internalMed.Id,
                License = "EGY-MED-2016-3399",
                Fee = 230.00m,
                Photo = "https://images.unsplash.com/photo-1527613426441-4da17471b66d?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض الباطنة العامة والسكر والغدد الصماء، جامعة قناة السويس. الشارع التجاري، الإسماعيلية.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) }
                }
            },

            // --- 10. بورسعيد (Port Said) ---
            new {
                Email = "yasmine.osman@medicare.com",
                FullName = "Dr. Yasmine Osman",
                Phone = "+201088002244",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2018-8811",
                Fee = 240.00m,
                Photo = "https://images.unsplash.com/photo-1594824813576-96b4ba63c5d6?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري طب الأطفال وحديثي الولادة ورعاية المبتسرين، مستشفيات التأمين الصحي الشامل. حي الشرق، شارع الجمهورية، بورسعيد.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }
                }
            },
            new {
                Email = "ashraf.elghandour@medicare.com",
                FullName = "Dr. Ashraf El-Ghandour",
                Phone = "+201099113355",
                SpecId = cardiology.Id,
                License = "EGY-MED-2012-4455",
                Fee = 310.00m,
                Photo = "https://images.unsplash.com/photo-1622253692010-333f2da6031d?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض القلب والأوعية الدموية وقسطرة الشرايين، كلية الطب جامعة بورسعيد. شارع محمد علي، بورسعيد.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) }
                }
            },
            new {
                Email = "nourhan.hassouna@medicare.com",
                FullName = "Dr. Nourhan Hassouna",
                Phone = "+201011224466",
                SpecId = dermatology.Id,
                License = "EGY-MED-2019-6677",
                Fee = 250.00m,
                Photo = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري الأمراض الجلدية والتجميل والليزر، مستشفيات بورسعيد. شارع الثلاثيني، بورسعيد.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) }
                }
            },

            // --- 11. السويس (Suez) ---
            new {
                Email = "gamal.elqousi@medicare.com",
                FullName = "Dr. Gamal El-Qousi",
                Phone = "+201022335577",
                SpecId = internalMed.Id,
                License = "EGY-MED-2013-1199",
                Fee = 250.00m,
                Photo = "https://images.unsplash.com/photo-1622253694242-abdb3c8b4b74?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أمراض الباطنة والجهاز الهضمي والمناظير والسكر، مجمع السويس الطبي. شارع الجيش، السويس.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },
            new {
                Email = "samar.elgamal@medicare.com",
                FullName = "Dr. Samar El-Gamal",
                Phone = "+201033446688",
                SpecId = dermatology.Id,
                License = "EGY-MED-2018-7788",
                Fee = 230.00m,
                Photo = "https://images.unsplash.com/photo-1594824813588-44243a41e976?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري الأمراض الجلدية والعلاج بالضوء والليزر، جامعة السويس. حي الأربعين، ميدان الإسعاف، السويس.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(10, 0, 0), End = new TimeSpan(16, 0, 0) }
                }
            },
            new {
                Email = "mahmoud.elhawary@medicare.com",
                FullName = "Dr. Mahmoud El-Hawary",
                Phone = "+201044557799",
                SpecId = orthopedics.Id,
                License = "EGY-MED-2015-8833",
                Fee = 270.00m,
                Photo = "https://images.unsplash.com/photo-1582750433449-648ed127bb54?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري جراحة العظام والعمود الفقري ومناظير المفاصل والكسور، السويس. بورتوفيق، السويس.",
                Hours = new[] {
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(12, 0, 0), End = new TimeSpan(18, 0, 0) }
                }
            },

            // --- 12. سوهاج (Sohag) ---
            new {
                Email = "ibrahim.elhawary@medicare.com",
                FullName = "Dr. Ibrahim El-Hawary",
                Phone = "+201078901234",
                SpecId = internalMed.Id,
                License = "EGY-MED-2014-4320",
                Fee = 220.00m,
                Photo = "https://images.unsplash.com/photo-1612349317150-e413f6a5b16d?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري أول أمراض الباطنة والغدد الصماء والسكري ومتابعة القدم السكري، جامعة سوهاج. شارع 15 مايو، أمام مستشفى الهلال، سوهاج.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) },
                    new { Day = DayOfWeek.Wednesday, Start = new TimeSpan(9, 0, 0), End = new TimeSpan(15, 0, 0) }
                }
            },
            new {
                Email = "kareem.radwan@medicare.com",
                FullName = "Dr. Kareem Radwan",
                Phone = "+201055668811",
                SpecId = cardiology.Id,
                License = "EGY-MED-2012-3399",
                Fee = 270.00m,
                Photo = "https://images.unsplash.com/photo-1622253692010-333f2da6031d?auto=format&fit=crop&w=400&q=80",
                Bio = "أستاذ واستشاري أمراض القلب والقسطرة التداخلية، كلية الطب جامعة سوهاج. شارع الجمهورية، أمام مجمع المحاكم، سوهاج.",
                Hours = new[] {
                    new { Day = DayOfWeek.Sunday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) },
                    new { Day = DayOfWeek.Tuesday, Start = new TimeSpan(13, 0, 0), End = new TimeSpan(19, 0, 0) }
                }
            },
            new {
                Email = "asmaa.elansary@medicare.com",
                FullName = "Dr. Asmaa El-Ansary",
                Phone = "+201066779922",
                SpecId = pediatrics.Id,
                License = "EGY-MED-2017-9944",
                Fee = 210.00m,
                Photo = "https://images.unsplash.com/photo-1559839734-2b71ea197ec2?auto=format&fit=crop&w=400&q=80",
                Bio = "استشاري طب الأطفال وحديثي الولادة ورعاية المبتسرين، كلية الطب جامعة سوهاج. حي سيتي، برج النخبة، سوهاج.",
                Hours = new[] {
                    new { Day = DayOfWeek.Monday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) },
                    new { Day = DayOfWeek.Thursday, Start = new TimeSpan(11, 0, 0), End = new TimeSpan(17, 0, 0) }
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
