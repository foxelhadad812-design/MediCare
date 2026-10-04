using FluentValidation;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Identity;

namespace MediCare.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IUnitOfWork _uow;
    private readonly IValidator<PatientRegisterDto> _patientValidator;
    private readonly IValidator<DoctorRegisterDto> _doctorValidator;
    private readonly IValidator<LoginDto> _loginValidator;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IUnitOfWork uow,
        IValidator<PatientRegisterDto> patientValidator,
        IValidator<DoctorRegisterDto> doctorValidator,
        IValidator<LoginDto> loginValidator)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _uow = uow;
        _patientValidator = patientValidator;
        _doctorValidator = doctorValidator;
        _loginValidator = loginValidator;
    }

    public async Task<Result<string>> RegisterPatientAsync(PatientRegisterDto dto)
    {
        var validation = await _patientValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return Result<string>.Failure(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var existingUser = await _userManager.FindByEmailAsync(dto.Email);
        if (existingUser != null)
        {
            return Result<string>.Failure("An account with this email address already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            FullName = dto.FullName.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, dto.Password);
        if (!createResult.Succeeded)
        {
            return Result<string>.Failure(string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(user, "Patient");

        var patient = new Patient
        {
            UserId = user.Id,
            DateOfBirth = dto.DateOfBirth,
            Gender = dto.Gender,
            BloodGroup = dto.BloodGroup,
            EmergencyContact = dto.EmergencyContact
        };

        await _uow.Patients.AddAsync(patient);
        await _uow.CommitAsync();

        // Patient accounts are immediately active per sprint requirements
        await _signInManager.SignInAsync(user, isPersistent: false);

        return Result<string>.Success(user.Id);
    }

    public async Task<Result<string>> RegisterDoctorAsync(DoctorRegisterDto dto)
    {
        var validation = await _doctorValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return Result<string>.Failure(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var existingUser = await _userManager.FindByEmailAsync(dto.Email);
        if (existingUser != null)
        {
            return Result<string>.Failure("An account with this email address already exists.");
        }

        var spec = await _uow.Specializations.GetByIdAsync(dto.SpecializationId);
        if (spec == null)
        {
            return Result<string>.Failure("The selected medical specialization is invalid.");
        }

        var existingLicense = (await _uow.Doctors.FindAsync(d => d.LicenseNumber == dto.LicenseNumber)).Any();
        if (existingLicense)
        {
            return Result<string>.Failure("A doctor with this license number is already registered.");
        }

        var user = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            FullName = dto.FullName.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, dto.Password);
        if (!createResult.Succeeded)
        {
            return Result<string>.Failure(string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(user, "Doctor");

        var doctor = new Doctor
        {
            UserId = user.Id,
            SpecializationId = dto.SpecializationId,
            LicenseNumber = dto.LicenseNumber.Trim(),
            ConsultationFee = dto.ConsultationFee,
            Bio = dto.Bio?.Trim(),
            IsApproved = false, // Pending admin approval
            SlotDurationMinutes = 30
        };

        await _uow.Doctors.AddAsync(doctor);
        await _uow.CommitAsync();

        // Doctor is not signed in automatically; status is Pending until approved
        return Result<string>.Success(user.Id);
    }

    public async Task<Result> LoginAsync(LoginDto dto)
    {
        var validation = await _loginValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return Result.Failure(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user == null)
        {
            return Result.Failure("Invalid email or password.");
        }

        if (await _userManager.IsInRoleAsync(user, "Doctor"))
        {
            var doctor = (await _uow.Doctors.FindAsync(d => d.UserId == user.Id)).FirstOrDefault();
            if (doctor != null && !doctor.IsApproved)
            {
                return Result.Failure("Your doctor account is pending administrator approval. You will receive access once verified.");
            }
        }

        var signInResult = await _signInManager.PasswordSignInAsync(
            user.UserName!,
            dto.Password,
            dto.RememberMe,
            lockoutOnFailure: false);

        if (signInResult.Succeeded)
        {
            return Result.Success();
        }

        return Result.Failure("Invalid email or password.");
    }

    public async Task LogoutAsync()
    {
        await _signInManager.SignOutAsync();
    }
}
