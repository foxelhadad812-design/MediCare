using FluentValidation;
using MediCare.Services.DTOs;

namespace MediCare.Services.Validators;

public class DoctorRegisterValidator : AbstractValidator<DoctorRegisterDto>
{
    public DoctorRegisterValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full Name is required.")
            .MaximumLength(150).WithMessage("Full Name cannot exceed 150 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email cannot exceed 256 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage("Passwords do not match.");

        RuleFor(x => x.SpecializationId)
            .GreaterThan(0).WithMessage("Please select a valid medical specialization.");

        RuleFor(x => x.LicenseNumber)
            .NotEmpty().WithMessage("Medical license number is required.")
            .MaximumLength(50).WithMessage("License number cannot exceed 50 characters.");

        RuleFor(x => x.ConsultationFee)
            .GreaterThanOrEqualTo(0).WithMessage("Consultation fee cannot be negative.");

        RuleFor(x => x.Bio)
            .MaximumLength(1000).WithMessage("Biography cannot exceed 1000 characters.");
    }
}
