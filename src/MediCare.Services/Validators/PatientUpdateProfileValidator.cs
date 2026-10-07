using FluentValidation;
using MediCare.Services.DTOs;

namespace MediCare.Services.Validators;

public class PatientUpdateProfileValidator : AbstractValidator<PatientUpdateProfileDto>
{
    private static readonly string[] AllowedBloodGroups = { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };
    private static readonly string[] AllowedGenders = { "Male", "Female" };

    public PatientUpdateProfileValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full Name is required.")
            .MaximumLength(150).WithMessage("Full Name cannot exceed 150 characters.");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of Birth is required.")
            .LessThan(DateTime.Today).WithMessage("Date of Birth must be in the past.");

        RuleFor(x => x.Gender)
            .NotEmpty().WithMessage("Gender is required.")
            .Must(g => AllowedGenders.Contains(g)).WithMessage("Gender must be either 'Male' or 'Female'.");

        RuleFor(x => x.BloodGroup)
            .Must(bg => string.IsNullOrEmpty(bg) || AllowedBloodGroups.Contains(bg))
            .WithMessage("Blood Group must be a valid blood type (e.g. A+, O-, B+).");

        RuleFor(x => x.EmergencyContact)
            .MaximumLength(50).WithMessage("Emergency contact cannot exceed 50 characters.");

        RuleFor(x => x.Allergies)
            .MaximumLength(500).WithMessage("Allergies cannot exceed 500 characters.");

        RuleFor(x => x.MedicalHistory)
            .MaximumLength(1000).WithMessage("Medical History cannot exceed 1000 characters.");
    }
}
