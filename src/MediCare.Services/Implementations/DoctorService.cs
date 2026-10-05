using FluentValidation;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;

namespace MediCare.Services.Implementations;

public class DoctorService : IDoctorService
{
    private readonly IUnitOfWork _uow;
    private readonly IValidator<DoctorFilterDto> _filterValidator;

    public DoctorService(IUnitOfWork uow, IValidator<DoctorFilterDto> filterValidator)
    {
        _uow = uow;
        _filterValidator = filterValidator;
    }

    public async Task<PagedResult<DoctorSummaryDto>> SearchDoctorsAsync(DoctorFilterDto filter)
    {
        await _filterValidator.ValidateAsync(filter);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 6 : filter.PageSize;

        var (doctors, totalCount) = await _uow.Doctors.SearchApprovedDoctorsAsync(
            filter.SpecializationId,
            filter.MaxFee,
            filter.AvailableDay,
            filter.SearchTerm,
            page,
            pageSize);

        var dtos = doctors.Select(d => new DoctorSummaryDto
        {
            Id = d.Id,
            FullName = d.User.FullName,
            SpecializationId = d.SpecializationId,
            SpecializationName = d.Specialization.Name,
            ConsultationFee = d.ConsultationFee,
            ProfileImageUrl = d.ProfileImageUrl,
            Bio = d.Bio,
            WorkingDays = d.WorkingHours.Select(w => w.DayOfWeek).Distinct().OrderBy(day => day).ToList()
        }).ToList();

        return new PagedResult<DoctorSummaryDto>(dtos, totalCount, page, pageSize);
    }

    public async Task<Result<DoctorDetailDto>> GetDoctorDetailsAsync(int id)
    {
        var doctor = await _uow.Doctors.GetDoctorWithScheduleAsync(id);
        if (doctor == null)
        {
            return Result<DoctorDetailDto>.Failure("Doctor not found or account is not approved.");
        }

        var dto = new DoctorDetailDto
        {
            Id = doctor.Id,
            FullName = doctor.User.FullName,
            Email = doctor.User.Email ?? string.Empty,
            PhoneNumber = doctor.User.PhoneNumber,
            SpecializationId = doctor.SpecializationId,
            SpecializationName = doctor.Specialization.Name,
            LicenseNumber = doctor.LicenseNumber,
            ConsultationFee = doctor.ConsultationFee,
            SlotDurationMinutes = doctor.SlotDurationMinutes,
            ProfileImageUrl = doctor.ProfileImageUrl,
            Bio = doctor.Bio,
            WorkingHours = doctor.WorkingHours
                .OrderBy(w => w.DayOfWeek)
                .ThenBy(w => w.StartTime)
                .Select(w => new WorkingHourDto
                {
                    DayOfWeek = w.DayOfWeek,
                    StartTime = w.StartTime,
                    EndTime = w.EndTime
                }).ToList()
        };

        return Result<DoctorDetailDto>.Success(dto);
    }

    public async Task<List<SpecializationDto>> GetSpecializationsAsync()
    {
        var specs = await _uow.Specializations.GetAllAsync();
        return specs
            .OrderBy(s => s.Name)
            .Select(s => new SpecializationDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description
            }).ToList();
    }

    public async Task<Result<DoctorDetailDto>> GetDoctorByUserIdAsync(string userId)
    {
        var doctor = await _uow.Doctors.GetByUserIdAsync(userId);
        if (doctor == null)
        {
            return Result<DoctorDetailDto>.Failure("Doctor profile not found.");
        }

        var dto = new DoctorDetailDto
        {
            Id = doctor.Id,
            FullName = doctor.User.FullName,
            Email = doctor.User.Email ?? string.Empty,
            PhoneNumber = doctor.User.PhoneNumber,
            SpecializationId = doctor.SpecializationId,
            SpecializationName = doctor.Specialization?.Name ?? string.Empty,
            LicenseNumber = doctor.LicenseNumber,
            ConsultationFee = doctor.ConsultationFee,
            SlotDurationMinutes = doctor.SlotDurationMinutes,
            ProfileImageUrl = doctor.ProfileImageUrl,
            Bio = doctor.Bio,
            WorkingHours = doctor.WorkingHours
                .OrderBy(w => w.DayOfWeek)
                .ThenBy(w => w.StartTime)
                .Select(w => new WorkingHourDto
                {
                    DayOfWeek = w.DayOfWeek,
                    StartTime = w.StartTime,
                    EndTime = w.EndTime
                }).ToList()
        };

        return Result<DoctorDetailDto>.Success(dto);
    }

    public async Task<Result<int>> GetDoctorIdByUserIdAsync(string userId)
    {
        var doctor = await _uow.Doctors.GetByUserIdAsync(userId);
        if (doctor == null)
        {
            return Result<int>.Failure("Doctor profile not found.");
        }

        return Result<int>.Success(doctor.Id);
    }
}
