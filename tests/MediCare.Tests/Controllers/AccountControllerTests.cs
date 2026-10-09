using System.Security.Claims;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class AccountControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<IDoctorService> _doctorServiceMock;
    private readonly AccountController _controller;

    public AccountControllerTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _doctorServiceMock = new Mock<IDoctorService>();

        _controller = new AccountController(_authServiceMock.Object, _doctorServiceMock.Object);

        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        _controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    private void SetUserContext(string? userId, string role, bool isAuthenticated = true)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, isAuthenticated ? "TestAuth" : null);
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        _controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    // =========================================================================
    // 1. Patient Registration Tests
    // =========================================================================

    [Fact]
    public void RegisterPatient_Get_WhenAlreadyAuthenticated_RedirectsToHome()
    {
        SetUserContext("existing_user", "Patient", isAuthenticated: true);

        var result = _controller.RegisterPatient();

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
    }

    [Fact]
    public void RegisterPatient_Get_WhenAnonymous_ReturnsView()
    {
        SetUserContext(null, "", isAuthenticated: false);

        var result = _controller.RegisterPatient();

        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public async Task RegisterPatient_Post_WhenModelStateInvalid_ReturnsViewWithoutCallingService()
    {
        var model = new PatientRegisterViewModel();
        _controller.ModelState.AddModelError("Email", "Email is required");

        var result = await _controller.RegisterPatient(model);

        result.Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeSameAs(model);
        _authServiceMock.Verify(a => a.RegisterPatientAsync(It.IsAny<PatientRegisterDto>()), Times.Never);
    }

    [Fact]
    public async Task RegisterPatient_Post_WhenAuthServiceFails_AddsModelErrorAndReturnsView()
    {
        var model = new PatientRegisterViewModel
        {
            FullName = "Ahmed Aly",
            Email = "duplicate@medicare.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            PhoneNumber = "01011112222"
        };

        _authServiceMock.Setup(a => a.RegisterPatientAsync(It.IsAny<PatientRegisterDto>()))
            .ReturnsAsync(Result<string>.Failure("An account with this email address already exists."));

        var result = await _controller.RegisterPatient(model);

        result.Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeSameAs(model);
        _controller.ModelState.IsValid.Should().BeFalse();
        _controller.ModelState[string.Empty]!.Errors.Should().Contain(e => e.ErrorMessage.Contains("email address already exists"));
    }

    [Fact]
    public async Task RegisterPatient_Post_WhenAuthServiceSucceeds_SetsSuccessMessageAndRedirects()
    {
        var model = new PatientRegisterViewModel
        {
            FullName = "Ahmed Aly",
            Email = "newpatient@medicare.com",
            Password = "P@ssword123!",
            ConfirmPassword = "P@ssword123!",
            PhoneNumber = "01011112222"
        };

        _authServiceMock.Setup(a => a.RegisterPatientAsync(It.IsAny<PatientRegisterDto>()))
            .ReturnsAsync(Result<string>.Success("user_id_123"));

        var result = await _controller.RegisterPatient(model);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("patient account has been created");
    }

    // =========================================================================
    // 2. Doctor Registration Tests
    // =========================================================================

    [Fact]
    public async Task RegisterDoctor_Get_WhenAlreadyAuthenticated_RedirectsToHome()
    {
        SetUserContext("doc_user", "Doctor", isAuthenticated: true);

        var result = await _controller.RegisterDoctor();

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
    }

    [Fact]
    public async Task RegisterDoctor_Get_WhenAnonymous_LoadsSpecializationsAndReturnsView()
    {
        SetUserContext(null, "", isAuthenticated: false);
        var specs = new List<SpecializationDto>
        {
            new() { Id = 1, Name = "Cardiology" }
        };
        _doctorServiceMock.Setup(d => d.GetSpecializationsAsync()).ReturnsAsync(specs);

        var result = await _controller.RegisterDoctor();

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<DoctorRegisterViewModel>().Subject;
        model.AvailableSpecializations.Should().HaveCount(1);
    }

    [Fact]
    public async Task RegisterDoctor_Post_WhenModelStateInvalid_ReloadsSpecializationsAndReturnsView()
    {
        var model = new DoctorRegisterViewModel();
        _controller.ModelState.AddModelError("LicenseNumber", "License is required");

        var specs = new List<SpecializationDto>
        {
            new() { Id = 1, Name = "Cardiology" }
        };
        _doctorServiceMock.Setup(d => d.GetSpecializationsAsync()).ReturnsAsync(specs);

        var result = await _controller.RegisterDoctor(model);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.Model.Should().BeSameAs(model);
        model.AvailableSpecializations.Should().HaveCount(1);
        _authServiceMock.Verify(a => a.RegisterDoctorAsync(It.IsAny<DoctorRegisterDto>()), Times.Never);
    }

    [Fact]
    public async Task RegisterDoctor_Post_WhenAuthServiceFails_AddsModelErrorAndReturnsView()
    {
        var model = new DoctorRegisterViewModel
        {
            FullName = "Dr. Tarek",
            Email = "tarek@medicare.com",
            LicenseNumber = "LIC-12345",
            SpecializationId = 1
        };

        var specs = new List<SpecializationDto>
        {
            new() { Id = 1, Name = "Cardiology" }
        };
        _doctorServiceMock.Setup(d => d.GetSpecializationsAsync()).ReturnsAsync(specs);

        _authServiceMock.Setup(a => a.RegisterDoctorAsync(It.IsAny<DoctorRegisterDto>()))
            .ReturnsAsync(Result<string>.Failure("Doctor license already registered."));

        var result = await _controller.RegisterDoctor(model);

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        viewResult.Model.Should().BeSameAs(model);
        _controller.ModelState.IsValid.Should().BeFalse();
        _controller.ModelState[string.Empty]!.Errors.Should().Contain(e => e.ErrorMessage.Contains("license already registered"));
    }

    [Fact]
    public async Task RegisterDoctor_Post_WhenAuthServiceSucceeds_SetsInfoMessageAndRedirectsToLogin()
    {
        var model = new DoctorRegisterViewModel
        {
            FullName = "Dr. Tarek",
            Email = "tarek@medicare.com",
            LicenseNumber = "LIC-12345",
            SpecializationId = 1
        };

        _authServiceMock.Setup(a => a.RegisterDoctorAsync(It.IsAny<DoctorRegisterDto>()))
            .ReturnsAsync(Result<string>.Success("doc_id_99"));

        var result = await _controller.RegisterDoctor(model);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Login");
        _controller.TempData["InfoMessage"]!.ToString().Should().Contain("pending administrator verification");
    }

    // =========================================================================
    // 3. Login Tests
    // =========================================================================

    [Fact]
    public void Login_Get_WhenAlreadyAuthenticated_RedirectsToHome()
    {
        SetUserContext("existing_user", "Patient", isAuthenticated: true);

        var result = _controller.Login();

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
    }

    [Fact]
    public void Login_Get_WhenAnonymous_ReturnsViewWithReturnUrl()
    {
        SetUserContext(null, "", isAuthenticated: false);

        var result = _controller.Login("/Appointments/Book/1");

        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<LoginViewModel>().Subject;
        model.ReturnUrl.Should().Be("/Appointments/Book/1");
    }

    [Fact]
    public async Task Login_Post_WhenModelStateInvalid_ReturnsViewWithoutCallingService()
    {
        var model = new LoginViewModel();
        _controller.ModelState.AddModelError("Email", "Email is required");

        var result = await _controller.Login(model);

        result.Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeSameAs(model);
        _authServiceMock.Verify(a => a.LoginAsync(It.IsAny<LoginDto>()), Times.Never);
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceFails_AddsModelErrorAndReturnsView()
    {
        var model = new LoginViewModel
        {
            Email = "user@medicare.com",
            Password = "WrongPassword!"
        };

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Failure("Invalid email or password."));

        var result = await _controller.Login(model);

        result.Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeSameAs(model);
        _controller.ModelState.IsValid.Should().BeFalse();
        _controller.ModelState[string.Empty]!.Errors.Should().Contain(e => e.ErrorMessage.Contains("Invalid email or password"));
    }

    [Fact]
    public async Task Login_Post_WhenAuthServiceSucceeds_WithoutReturnUrl_RedirectsToHome()
    {
        var model = new LoginViewModel
        {
            Email = "user@medicare.com",
            Password = "CorrectPassword123!"
        };

        _authServiceMock.Setup(a => a.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.Login(model);

        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("successfully signed in");
    }

    // =========================================================================
    // 4. Logout Tests
    // =========================================================================

    [Fact]
    public async Task Logout_Post_CallsLogoutAsync_SetsSuccessMessageAndRedirectsToHome()
    {
        SetUserContext("doc_user", "Doctor");

        _authServiceMock.Setup(a => a.LogoutAsync())
            .Returns(Task.CompletedTask);

        var result = await _controller.Logout();

        _authServiceMock.Verify(a => a.LogoutAsync(), Times.Once);
        result.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be("Index");
        _controller.TempData["SuccessMessage"]!.ToString().Should().Contain("logged out");
    }
}
