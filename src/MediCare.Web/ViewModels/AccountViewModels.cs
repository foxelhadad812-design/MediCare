using System.ComponentModel.DataAnnotations;
using MediCare.Services.DTOs;

namespace MediCare.Web.ViewModels;

public class PatientRegisterViewModel
{
    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(150, ErrorMessage = "Full Name cannot exceed 150 characters.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Invalid phone number.")]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Date of Birth is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
    public DateTime DateOfBirth { get; set; } = DateTime.Today.AddYears(-25);

    [Required(ErrorMessage = "Please select gender.")]
    public string Gender { get; set; } = "Male";

    [Display(Name = "Blood Group")]
    public string? BloodGroup { get; set; }

    [Display(Name = "Emergency Contact Phone")]
    public string? EmergencyContact { get; set; }

    [Display(Name = "Known Allergies")]
    [StringLength(500, ErrorMessage = "Allergies cannot exceed 500 characters.")]
    public string? Allergies { get; set; }

    [Display(Name = "Medical History & Chronic Conditions")]
    [StringLength(1000, ErrorMessage = "Medical history cannot exceed 1000 characters.")]
    public string? MedicalHistory { get; set; }
}

public class DoctorRegisterViewModel
{
    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(150, ErrorMessage = "Full Name cannot exceed 150 characters.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Invalid phone number.")]
    [Display(Name = "Contact Phone Number")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Please select a specialization.")]
    [Display(Name = "Medical Specialization")]
    public int SpecializationId { get; set; }

    [Required(ErrorMessage = "Medical License Number is required.")]
    [Display(Name = "Medical License Number")]
    public string LicenseNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Consultation fee is required.")]
    [Range(0, 10000, ErrorMessage = "Fee must be a non-negative number.")]
    [Display(Name = "Consultation Fee (EGP)")]
    public decimal ConsultationFee { get; set; } = 200.00m;

    [Required(ErrorMessage = "Governorate / Clinic Location is required.")]
    [Display(Name = "Governorate / Location")]
    public string Governorate { get; set; } = "Cairo";

    [Display(Name = "Professional Biography")]
    public string? Bio { get; set; }

    public List<SpecializationDto> AvailableSpecializations { get; set; } = new();
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember Me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class PatientProfileViewModel
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full Name is required.")]
    [StringLength(150, ErrorMessage = "Full Name cannot exceed 150 characters.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Invalid phone number.")]
    [Display(Name = "Contact Phone Number")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Date of Birth is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
    public DateTime DateOfBirth { get; set; }

    [Required(ErrorMessage = "Please select gender.")]
    [Display(Name = "Gender")]
    public string Gender { get; set; } = "Male";

    [Display(Name = "Blood Group")]
    public string? BloodGroup { get; set; }

    [Display(Name = "Emergency Contact Phone")]
    public string? EmergencyContact { get; set; }

    [Display(Name = "Known Allergies")]
    [StringLength(500, ErrorMessage = "Allergies cannot exceed 500 characters.")]
    public string? Allergies { get; set; }

    [Display(Name = "Medical History & Chronic Conditions")]
    [StringLength(1000, ErrorMessage = "Medical history cannot exceed 1000 characters.")]
    public string? MedicalHistory { get; set; }
}
