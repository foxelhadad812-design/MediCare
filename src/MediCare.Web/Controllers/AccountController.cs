using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IDoctorService _doctorService;

    public AccountController(IAuthService authService, IDoctorService doctorService)
    {
        _authService = authService;
        _doctorService = doctorService;
    }

    [HttpGet]
    public IActionResult RegisterPatient()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }
        return View(new PatientRegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterPatient(PatientRegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var dto = new PatientRegisterDto
        {
            FullName = model.FullName,
            Email = model.Email,
            Password = model.Password,
            ConfirmPassword = model.ConfirmPassword,
            PhoneNumber = model.PhoneNumber,
            DateOfBirth = model.DateOfBirth,
            Gender = model.Gender,
            BloodGroup = model.BloodGroup,
            EmergencyContact = model.EmergencyContact
        };

        var result = await _authService.RegisterPatientAsync(dto);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "Your patient account has been created successfully! Welcome to MediCare.";
            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Registration failed. Please verify your details.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> RegisterDoctor()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        var model = new DoctorRegisterViewModel
        {
            AvailableSpecializations = await _doctorService.GetSpecializationsAsync()
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterDoctor(DoctorRegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableSpecializations = await _doctorService.GetSpecializationsAsync();
            return View(model);
        }

        var dto = new DoctorRegisterDto
        {
            FullName = model.FullName,
            Email = model.Email,
            Password = model.Password,
            ConfirmPassword = model.ConfirmPassword,
            PhoneNumber = model.PhoneNumber,
            SpecializationId = model.SpecializationId,
            LicenseNumber = model.LicenseNumber,
            ConsultationFee = model.ConsultationFee,
            Bio = model.Bio
        };

        var result = await _authService.RegisterDoctorAsync(dto);
        if (result.IsSuccess)
        {
            TempData["InfoMessage"] = "Thank you for registering! Your doctor application has been submitted and is currently pending administrator verification. You will be able to log in once your medical credentials are approved.";
            return RedirectToAction("Login");
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Doctor registration failed.");
        model.AvailableSpecializations = await _doctorService.GetSpecializationsAsync();
        return View(model);
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var dto = new LoginDto
        {
            Email = model.Email,
            Password = model.Password,
            RememberMe = model.RememberMe
        };

        var result = await _authService.LoginAsync(dto);
        if (result.IsSuccess)
        {
            TempData["SuccessMessage"] = "You have successfully signed in.";
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }
            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Invalid login attempt.");
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _authService.LogoutAsync();
        TempData["SuccessMessage"] = "You have been logged out.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
