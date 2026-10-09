using System.Security.Claims;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Security;

public class AppointmentCheckInSecurityTests
{
    private readonly Mock<IAppointmentService> _apptServiceMock = new();
    private readonly Mock<IDoctorService> _docServiceMock = new();
    private readonly Mock<IMedicalRecordService> _medRecordServiceMock = new();
    private readonly Mock<INotificationService> _notifServiceMock = new();
    private readonly Mock<IWebHostEnvironment> _webHostEnvironmentMock = new();
    private readonly Mock<ILogger<AppointmentsController>> _loggerMock = new();

    private AppointmentsController CreateController(string userId, string role)
    {
        var controller = new AppointmentsController(
            _apptServiceMock.Object,
            _docServiceMock.Object,
            _medRecordServiceMock.Object,
            _notifServiceMock.Object,
            _webHostEnvironmentMock.Object,
            _loggerMock.Object);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());

        return controller;
    }

    [Fact]
    public async Task CheckIn_WhenDoctorOwnsAppointment_SucceedsAndRedirects()
    {
        // Arrange: Doctor with Id = 5 owns the appointment with Id = 100
        var apptDetails = new AppointmentSummaryDto
        {
            Id = 100,
            DoctorId = 5,
            PatientName = "Karim Tarek",
            QueueNumber = 3
        };

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDetails));

        _docServiceMock.Setup(s => s.GetDoctorIdByUserIdAsync("doctor_5_user"))
            .ReturnsAsync(Result<int>.Success(5));

        var controller = CreateController("doctor_5_user", "Doctor");

        // Act
        var result = await controller.CheckIn(100);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be(nameof(AppointmentsController.Details));
        controller.TempData["SuccessMessage"].Should().NotBeNull();
    }

    [Fact]
    public async Task CheckIn_WhenAdminUser_SucceedsForAnyAppointment()
    {
        // Arrange: Admin user checks in appointment for Doctor 5
        var apptDetails = new AppointmentSummaryDto
        {
            Id = 100,
            DoctorId = 5,
            PatientName = "Karim Tarek",
            QueueNumber = 3
        };

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDetails));

        var controller = CreateController("admin_user_1", "Admin");

        // Act
        var result = await controller.CheckIn(100);

        // Assert
        result.Should().BeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.ActionName.Should().Be(nameof(AppointmentsController.Details));
    }

    [Fact]
    public async Task CheckIn_WhenUnrelatedDoctor_ReturnsForbid()
    {
        // Arrange: Doctor with Id = 99 attempts to check in an appointment owned by Doctor Id = 5
        var apptDetails = new AppointmentSummaryDto
        {
            Id = 100,
            DoctorId = 5,
            PatientName = "Karim Tarek",
            QueueNumber = 3
        };

        _apptServiceMock.Setup(s => s.GetAppointmentByIdAsync(100))
            .ReturnsAsync(Result<AppointmentSummaryDto>.Success(apptDetails));

        _docServiceMock.Setup(s => s.GetDoctorIdByUserIdAsync("doctor_99_user"))
            .ReturnsAsync(Result<int>.Success(99)); // Doctor 99 is not Doctor 5

        var controller = CreateController("doctor_99_user", "Doctor");

        // Act
        var result = await controller.CheckIn(100);

        // Assert: MUST return ForbidResult
        result.Should().BeOfType<ForbidResult>("A doctor cannot check in an appointment belonging to another doctor");
    }

    [Fact]
    public void CheckIn_HasStrictAuthorizeAndAntiforgeryAttributes()
    {
        var method = typeof(AppointmentsController).GetMethods()
            .First(m => m.Name == "CheckIn" && m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any());

        var authAttr = method.GetCustomAttributes(typeof(AuthorizeAttribute), false).FirstOrDefault() as AuthorizeAttribute;
        authAttr.Should().NotBeNull("CheckIn POST must have [Authorize]");
        authAttr!.Roles.Should().Contain("Doctor");
        authAttr.Roles.Should().Contain("Admin");

        var antiforgery = method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false);
        antiforgery.Should().NotBeEmpty("CheckIn POST must have [ValidateAntiForgeryToken]");
    }
}
