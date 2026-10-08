using System.Security.Claims;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Data.Seed;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Security;

public class PrescriptionVerificationSecurityTests
{
    // =========================================================================
    // 1. PatientNameMasker Tests
    // =========================================================================

    [Theory]
    [InlineData("Mohamed Ahmed Ali", "M**** A**** A****")]
    [InlineData("John Doe", "J**** D****")]
    [InlineData("Ahmed", "A****")]
    [InlineData("A", "A****")]
    [InlineData("محمد أحمد علي", "م**** أ**** ع****")]
    [InlineData("علي", "ع****")]
    public void PatientNameMasker_MasksNameWithFixedStars_WithoutRevealingLength(string input, string expected)
    {
        var result = PatientNameMasker.Mask(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PatientNameMasker_HandlesNullOrWhitespace_ReturnsFixedStars(string? input)
    {
        var result = PatientNameMasker.Mask(input);
        result.Should().Be("****");
    }

    // =========================================================================
    // 2. Token Generation for New Prescriptions
    // =========================================================================

    [Fact]
    public async Task MedicalRecordService_SaveEncounter_GeneratesUnique128BitVerificationToken()
    {
        // Arrange
        var uowMock = new Mock<IUnitOfWork>();
        var fileStorageMock = new Mock<IFileStorageService>();
        var clinicClockMock = new Mock<IClinicClock>();
        var notifMock = new Mock<INotificationService>();
        var loggerMock = new Mock<ILogger<MedicalRecordService>>();

        var now = new DateTime(2026, 11, 15, 11, 0, 0);
        clinicClockMock.Setup(c => c.Now).Returns(now);
        clinicClockMock.Setup(c => c.Today).Returns(now.Date);

        var doctor = new Doctor
        {
            Id = 5,
            UserId = "doc_123",
            User = new ApplicationUser { FullName = "Dr. Sameh" }
        };
        var patient = new Patient
        {
            Id = 10,
            UserId = "pat_456",
            User = new ApplicationUser { FullName = "Kareem Tarek" }
        };
        var appointment = new Appointment
        {
            Id = 25,
            DoctorId = 5,
            Doctor = doctor,
            PatientId = 10,
            Patient = patient,
            Status = AppointmentStatus.Confirmed,
            AppointmentDate = now.Date,
            StartTime = new TimeSpan(10, 0, 0)
        };

        var apptRepoMock = new Mock<IAppointmentRepository>();
        apptRepoMock.Setup(r => r.GetByIdWithDetailsAsync(25)).ReturnsAsync(appointment);
        uowMock.Setup(u => u.Appointments).Returns(apptRepoMock.Object);

        var docRepoMock = new Mock<IDoctorRepository>();
        docRepoMock.Setup(d => d.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Doctor, bool>>>()))
            .ReturnsAsync(new List<Doctor> { doctor });
        uowMock.Setup(u => u.Doctors).Returns(docRepoMock.Object);

        var recRepoMock = new Mock<IMedicalRecordRepository>();
        recRepoMock.Setup(r => r.GetByAppointmentIdWithDetailsAsync(25)).ReturnsAsync((MedicalRecord?)null);
        uowMock.Setup(u => u.MedicalRecords).Returns(recRepoMock.Object);

        Prescription? savedPrescription = null;
        var prescRepoMock = new Mock<IPrescriptionRepository>();
        prescRepoMock.Setup(r => r.AddAsync(It.IsAny<Prescription>()))
            .Callback<Prescription>(p => savedPrescription = p)
            .Returns(Task.CompletedTask);
        uowMock.Setup(u => u.Prescriptions).Returns(prescRepoMock.Object);
        uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var service = new MedicalRecordService(uowMock.Object, fileStorageMock.Object, clinicClockMock.Object, notifMock.Object, loggerMock.Object);

        var encounterDto = new CreateEncounterDto
        {
            AppointmentId = 25,
            Diagnosis = "Acute Bronchitis",
            PrescriptionItems = new List<PrescriptionItemDto>
            {
                new() { MedicationName = "Amoxicillin 500mg", Dosage = "1 capsule", Frequency = "TID", DurationDays = 7 }
            }
        };

        // Act
        var result = await service.SaveEncounterAsync(encounterDto, null, "doc_123", "C:\\Storage");

        // Assert
        result.IsSuccess.Should().BeTrue();
        savedPrescription.Should().NotBeNull();
        savedPrescription!.VerificationToken.Should().NotBeNullOrWhiteSpace();
        savedPrescription.VerificationToken.Should().HaveLength(32); // 128-bit in hex = 32 chars
        savedPrescription.VerificationToken.Should().MatchRegex("^[a-f0-9]{32}$"); // lowercase hex only
        savedPrescription.IsDispensed.Should().BeFalse();
    }

    // =========================================================================
    // 3. PrescriptionService: VerifyPrescriptionByToken Minimal Disclosure
    // =========================================================================

    [Fact]
    public async Task VerifyPrescriptionByToken_ValidToken_ReturnsMinimalPublicDataOnly()
    {
        // Arrange
        var uowMock = new Mock<IUnitOfWork>();
        var clinicClockMock = new Mock<IClinicClock>();
        var loggerMock = new Mock<ILogger<PrescriptionService>>();

        var prescriptionRepoMock = new Mock<IPrescriptionRepository>();
        var validToken = "d41d8cd98f00b204e9800998ecf8427e";

        var prescription = new Prescription
        {
            Id = 99,
            VerificationToken = validToken,
            PrescriptionDate = new DateTime(2026, 11, 20),
            IsDispensed = false,
            Doctor = new Doctor
            {
                User = new ApplicationUser { FullName = "Dr. Mona Zaki" },
                Specialization = new Specialization { Name = "Pediatrics" }
            },
            Patient = new Patient
            {
                User = new ApplicationUser { FullName = "Hassan Mahmoud" }
            },
            Items = new List<PrescriptionItem>
            {
                new() { MedicationName = "Sensitive Antibiotic", Dosage = "500mg", Frequency = "BID", DurationDays = 5 }
            }
        };

        prescriptionRepoMock.Setup(r => r.GetByTokenWithDetailsAsync(validToken)).ReturnsAsync(prescription);
        uowMock.Setup(u => u.Prescriptions).Returns(prescriptionRepoMock.Object);

        var service = new PrescriptionService(uowMock.Object, clinicClockMock.Object, loggerMock.Object);

        // Act
        var result = await service.VerifyPrescriptionByTokenAsync(validToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Should().NotBeNull();
        dto!.IsValid.Should().BeTrue();
        dto.DoctorName.Should().Be("Dr. Mona Zaki");
        dto.Specialization.Should().Be("Pediatrics");
        dto.MaskedPatientName.Should().Be("H**** M****"); // Masked, not "Hassan Mahmoud"
        dto.IsDispensed.Should().BeFalse();

        // Critical Security Check: Ensure sensitive data is NOT on the public DTO type
        typeof(PrescriptionVerificationDto).GetProperty("Items").Should().BeNull();
        typeof(PrescriptionVerificationDto).GetProperty("Diagnosis").Should().BeNull();
        typeof(PrescriptionVerificationDto).GetProperty("PrescriptionItems").Should().BeNull();
        typeof(PrescriptionVerificationDto).GetProperty("VerificationToken").Should().BeNull();
        typeof(PrescriptionVerificationDto).GetProperty("DispensedNotes").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short_token")]
    [InlineData("invalid_nonexistent_token_123456")]
    public async Task VerifyPrescriptionByToken_InvalidOrUnknownToken_ReturnsGenericFailure(string? token)
    {
        // Arrange
        var uowMock = new Mock<IUnitOfWork>();
        var clinicClockMock = new Mock<IClinicClock>();
        var loggerMock = new Mock<ILogger<PrescriptionService>>();
        var prescriptionRepoMock = new Mock<IPrescriptionRepository>();

        prescriptionRepoMock.Setup(r => r.GetByTokenWithDetailsAsync(It.IsAny<string>()))
            .ReturnsAsync((Prescription?)null);
        uowMock.Setup(u => u.Prescriptions).Returns(prescriptionRepoMock.Object);

        var service = new PrescriptionService(uowMock.Object, clinicClockMock.Object, loggerMock.Object);

        // Act
        var result = await service.VerifyPrescriptionByTokenAsync(token!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid or unrecognized prescription verification token.");
    }

    // =========================================================================
    // 4. Dispense: Idempotency & Role Guards
    // =========================================================================

    [Fact]
    public async Task DispensePrescription_WhenValidPharmacist_SucceedsOnce_SecondAttemptFails()
    {
        // Arrange
        var uowMock = new Mock<IUnitOfWork>();
        var clinicClockMock = new Mock<IClinicClock>();
        var loggerMock = new Mock<ILogger<PrescriptionService>>();
        var prescriptionRepoMock = new Mock<IPrescriptionRepository>();

        var token = "e99a18c428cb38d5f260853678922e03";
        var now = new DateTime(2026, 11, 20, 14, 30, 0);
        clinicClockMock.Setup(c => c.Now).Returns(now);

        var prescription = new Prescription
        {
            Id = 50,
            VerificationToken = token,
            IsDispensed = false
        };

        prescriptionRepoMock.Setup(r => r.GetByTokenWithDetailsAsync(token)).ReturnsAsync(prescription);
        uowMock.Setup(u => u.Prescriptions).Returns(prescriptionRepoMock.Object);
        uowMock.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        var service = new PrescriptionService(uowMock.Object, clinicClockMock.Object, loggerMock.Object);

        // Act 1: First dispense attempt
        var firstResult = await service.DispensePrescriptionAsync(token, "pharmacist_id_1", "El-Ezaby Pharmacy");

        // Assert 1: First attempt succeeds
        firstResult.IsSuccess.Should().BeTrue();
        prescription.IsDispensed.Should().BeTrue();
        prescription.DispensedByUserId.Should().Be("pharmacist_id_1");
        prescription.DispensedAt.Should().Be(now);
        prescription.PharmacyNotes.Should().Be("El-Ezaby Pharmacy");

        // Act 2: Second attempt on the now-dispensed prescription
        var secondResult = await service.DispensePrescriptionAsync(token, "pharmacist_id_2", "Duplicate Attempt");

        // Assert 2: Second attempt rejected (idempotent guard)
        secondResult.IsSuccess.Should().BeFalse();
        secondResult.Error.Should().Contain("already been marked as dispensed");
    }

    [Fact]
    public async Task DispensePrescription_SimultaneousParallelRequests_OnlyOneCanSucceed_AndConcurrencyFailureReturned()
    {
        // Arrange
        var uowMock = new Mock<IUnitOfWork>();
        var clinicClockMock = new Mock<IClinicClock>();
        var loggerMock = new Mock<ILogger<PrescriptionService>>();
        var prescriptionRepoMock = new Mock<IPrescriptionRepository>();

        var token = "e99a18c428cb38d5f260853678922e03";
        var now = new DateTime(2026, 11, 20, 14, 30, 0);
        clinicClockMock.Setup(c => c.Now).Returns(now);

        prescriptionRepoMock.Setup(r => r.GetByTokenWithDetailsAsync(token))
            .ReturnsAsync(() => new Prescription
            {
                Id = 50,
                VerificationToken = token,
                IsDispensed = false
            });
        uowMock.Setup(u => u.Prescriptions).Returns(prescriptionRepoMock.Object);

        // Simulate database concurrency race condition:
        // First CommitAsync call succeeds; any simultaneous commit throws DbUpdateConcurrencyException
        int commitCalls = 0;
        uowMock.Setup(u => u.CommitAsync()).Returns(() =>
        {
            var callIndex = Interlocked.Increment(ref commitCalls);
            if (callIndex == 1)
            {
                return Task.FromResult(1);
            }
            throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(
                "Database operation expected to affect 1 row(s) but actually affected 0 row(s).");
        });

        var service = new PrescriptionService(uowMock.Object, clinicClockMock.Object, loggerMock.Object);

        // Act: Run two parallel dispense tasks simulating simultaneous requests from different pharmacy terminals
        var task1 = service.DispensePrescriptionAsync(token, "pharmacist_1", "Terminal 1");
        var task2 = service.DispensePrescriptionAsync(token, "pharmacist_2", "Terminal 2");
        var results = await Task.WhenAll(task1, task2);

        // Assert: Exactly one succeeds, and exactly one fails with concurrency error
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);

        successCount.Should().Be(1, "Exactly one concurrent dispense request must succeed");
        failureCount.Should().Be(1, "The competing concurrent dispense request must be rejected");

        var failure = results.First(r => !r.IsSuccess);
        failure.Error.Should().Be("Prescription was already dispensed by another concurrent request.");
    }

    // =========================================================================
    // 5. PrescriptionsController Security & Action Tests
    // =========================================================================

    private PrescriptionsController CreateController(Mock<IPrescriptionService> serviceMock, string? userId = null, string role = "")
    {
        var loggerMock = new Mock<ILogger<PrescriptionsController>>();
        var controller = new PrescriptionsController(serviceMock.Object, loggerMock.Object);

        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, string.IsNullOrEmpty(userId) ? null : "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }

    [Fact]
    public async Task Controller_Verify_InvalidToken_ReturnsVerifyInvalidView()
    {
        // Arrange
        var serviceMock = new Mock<IPrescriptionService>();
        serviceMock.Setup(s => s.VerifyPrescriptionByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(Result<PrescriptionVerificationDto>.Failure("Invalid token"));

        var controller = CreateController(serviceMock);

        // Act
        var result = await controller.Verify("invalid_token_123");

        // Assert
        result.Should().BeOfType<ViewResult>();
        var viewResult = (ViewResult)result;
        viewResult.ViewName.Should().Be("VerifyInvalid");
    }

    [Fact]
    public async Task Controller_Verify_ValidToken_ReturnsMinimalVerifyView()
    {
        // Arrange
        var validToken = "d41d8cd98f00b204e9800998ecf8427e";
        var dto = new PrescriptionVerificationDto
        {
            IsValid = true,
            DoctorName = "Dr. Aly",
            Specialization = "Cardiology",
            MaskedPatientName = "M**** A****"
        };

        var serviceMock = new Mock<IPrescriptionService>();
        serviceMock.Setup(s => s.VerifyPrescriptionByTokenAsync(validToken))
            .ReturnsAsync(Result<PrescriptionVerificationDto>.Success(dto));

        var controller = CreateController(serviceMock);

        // Act
        var result = await controller.Verify(validToken);

        // Assert
        result.Should().BeOfType<ViewResult>();
        var viewResult = (ViewResult)result;
        viewResult.Model.Should().Be(dto);
    }

    [Fact]
    public async Task Controller_Dispense_AnonymousUser_ReturnsChallenge()
    {
        // Arrange
        var serviceMock = new Mock<IPrescriptionService>();
        var controller = CreateController(serviceMock, userId: null); // Anonymous

        // Act
        var result = await controller.Dispense("valid_token_12345678901234567890", "Pharmacy notes");

        // Assert
        result.Should().BeOfType<ChallengeResult>();
    }

    [Fact]
    public async Task Controller_Dispense_ValidPharmacist_RedirectsWithSuccess()
    {
        // Arrange
        var token = "d41d8cd98f00b204e9800998ecf8427e";
        var serviceMock = new Mock<IPrescriptionService>();
        serviceMock.Setup(s => s.DispensePrescriptionAsync(token, "pharm_user_1", "Notes"))
            .ReturnsAsync(Result.Success());

        var controller = CreateController(serviceMock, userId: "pharm_user_1", role: "Pharmacist");
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            controller.HttpContext,
            Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

        // Act
        var result = await controller.Dispense(token, "Notes");

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be(nameof(PrescriptionsController.Dispense));
        controller.TempData["SuccessMessage"].Should().NotBeNull();
    }

    [Fact]
    public void Controller_Dispense_HasStrictAuthorizeAndAntiforgeryAttributes()
    {
        var method = typeof(PrescriptionsController).GetMethods()
            .First(m => m.Name == "Dispense" && m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any());

        var authAttr = method.GetCustomAttributes(typeof(AuthorizeAttribute), false).FirstOrDefault() as AuthorizeAttribute;
        authAttr.Should().NotBeNull("Dispense POST must be protected by [Authorize]");
        authAttr!.Roles.Should().Contain("Pharmacist");
        authAttr.Roles.Should().Contain("Admin");

        var antiforgery = method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false);
        antiforgery.Should().NotBeEmpty("Dispense POST must have [ValidateAntiForgeryToken]");
    }

    [Theory]
    [InlineData("Verify", typeof(HttpGetAttribute), "PrescriptionVerificationPolicy")]
    [InlineData("Dispense", typeof(HttpGetAttribute), "PrescriptionDispensePolicy")]
    [InlineData("Dispense", typeof(HttpPostAttribute), "PrescriptionDispensePolicy")]
    public void Controller_Endpoints_HaveRateLimitingPolicyConfigured(string actionName, Type httpMethodAttribute, string expectedPolicy)
    {
        var method = typeof(PrescriptionsController).GetMethods()
            .First(m => m.Name == actionName && m.GetCustomAttributes(httpMethodAttribute, false).Any());

        var rateLimitAttr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false)
            .FirstOrDefault() as EnableRateLimitingAttribute;

        rateLimitAttr.Should().NotBeNull($"Action {actionName} must have [EnableRateLimiting]");
        rateLimitAttr!.PolicyName.Should().Be(expectedPolicy);
    }

    // =========================================================================
    // 6. Pharmacist Seeding Environment & Secret Security Tests
    // =========================================================================

    private ServiceProvider BuildSeedServiceProvider(string environmentName, Dictionary<string, string?> configValues)
    {
        var services = new ServiceCollection();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        var hostEnvMock = new Mock<IHostEnvironment>();
        hostEnvMock.Setup(h => h.EnvironmentName).Returns(environmentName);
        services.AddSingleton(hostEnvMock.Object);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DbInitializer_InProduction_NeverSeedsPharmacistUser()
    {
        // Arrange: Production environment even if password is configured
        var config = new Dictionary<string, string?>
        {
            ["Seed:PharmacistPassword"] = "SecureP@ss123!"
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        // Act
        await DbInitializer.InitializeAsync(sp);

        // Assert
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var pharmacist = await userManager.FindByEmailAsync("pharmacist@medicare.com");
        pharmacist.Should().BeNull("Pharmacist test account must never be seeded in Production");
    }

    [Fact]
    public async Task DbInitializer_InDevelopment_WhenSecretMissing_SkipsSeedingPharmacist()
    {
        // Arrange: Development environment without Seed:PharmacistPassword or Seed:DefaultPassword
        var config = new Dictionary<string, string?>
        {
            ["Seed:OtherConfig"] = "value"
        };
        using var sp = BuildSeedServiceProvider("Development", config);

        // Act
        await DbInitializer.InitializeAsync(sp);

        // Assert
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var pharmacist = await userManager.FindByEmailAsync("pharmacist@medicare.com");
        pharmacist.Should().BeNull("Pharmacist test account must be skipped when no secret is configured");
    }

    [Fact]
    public async Task DbInitializer_InDevelopment_WhenSecretProvided_SeedsPharmacistWithRole()
    {
        // Arrange: Development environment with explicit secret
        var config = new Dictionary<string, string?>
        {
            ["Seed:PharmacistPassword"] = "P@ssword123!"
        };
        using var sp = BuildSeedServiceProvider("Development", config);

        // Act
        await DbInitializer.InitializeAsync(sp);

        // Assert
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var pharmacist = await userManager.FindByEmailAsync("pharmacist@medicare.com");
        pharmacist.Should().NotBeNull();
        pharmacist!.Email.Should().Be("pharmacist@medicare.com");

        var roles = await userManager.GetRolesAsync(pharmacist);
        roles.Should().Contain("Pharmacist");
    }
}
