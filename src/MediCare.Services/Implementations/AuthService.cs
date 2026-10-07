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
    private readonly IValidator<PatientUpdateProfileDto> _updateProfileValidator;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IUnitOfWork uow,
        IValidator<PatientRegisterDto> patientValidator,
        IValidator<DoctorRegisterDto> doctorValidator,
        IValidator<LoginDto> loginValidator,
        IValidator<PatientUpdateProfileDto>? updateProfileValidator = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _uow = uow;
        _patientValidator = patientValidator;
        _doctorValidator = doctorValidator;
        _loginValidator = loginValidator;
        _updateProfileValidator = updateProfileValidator ?? new MediCare.Services.Validators.PatientUpdateProfileValidator();
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
            EmergencyContact = dto.EmergencyContact,
            Allergies = dto.Allergies?.Trim(),
            MedicalHistory = dto.MedicalHistory?.Trim()
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
            Governorate = string.IsNullOrWhiteSpace(dto.Governorate) ? "Cairo" : dto.Governorate.Trim(),
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

    public async Task<Result<PatientProfileDto>> GetPatientProfileAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result<PatientProfileDto>.Failure("User ID is required.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Result<PatientProfileDto>.Failure("Patient user account not found.");
        }

        var patients = await _uow.Patients.FindAsync(p => p.UserId == userId);
        var patient = patients.FirstOrDefault();
        if (patient == null)
        {
            return Result<PatientProfileDto>.Failure("Patient profile details not found.");
        }

        var dto = new PatientProfileDto
        {
            Id = patient.Id,
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
            DateOfBirth = patient.DateOfBirth,
            Gender = patient.Gender,
            BloodGroup = patient.BloodGroup,
            EmergencyContact = patient.EmergencyContact,
            Allergies = patient.Allergies,
            MedicalHistory = patient.MedicalHistory
        };

        return Result<PatientProfileDto>.Success(dto);
    }

    public async Task<Result> UpdatePatientProfileAsync(string userId, PatientUpdateProfileDto dto)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result.Failure("User ID is required.");
        }

        var validation = await _updateProfileValidator.ValidateAsync(dto);
        if (!validation.IsValid)
        {
            return Result.Failure(string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Result.Failure("Patient user account not found.");
        }

        var patients = await _uow.Patients.FindAsync(p => p.UserId == userId);
        var patient = patients.FirstOrDefault();
        if (patient == null)
        {
            return Result.Failure("Patient profile details not found.");
        }

        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = dto.PhoneNumber?.Trim();
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Result.Failure(string.Join("; ", updateResult.Errors.Select(e => e.Description)));
        }

        patient.DateOfBirth = dto.DateOfBirth;
        patient.Gender = dto.Gender;
        patient.BloodGroup = dto.BloodGroup;
        patient.EmergencyContact = dto.EmergencyContact?.Trim();
        patient.Allergies = dto.Allergies?.Trim();
        patient.MedicalHistory = dto.MedicalHistory?.Trim();

        _uow.Patients.Update(patient);
        await _uow.CommitAsync();

        return Result.Success();
    }
}
