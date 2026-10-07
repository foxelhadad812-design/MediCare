using FluentValidation;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;

namespace MediCare.Services.Implementations;

public class ScheduleService : IScheduleService
{
    private readonly IUnitOfWork _uow;
    private readonly IValidator<WorkingHoursDto> _workingHoursValidator;
    private readonly IValidator<DoctorLeaveDto> _doctorLeaveValidator;

    public ScheduleService(
        IUnitOfWork uow,
        IValidator<WorkingHoursDto> workingHoursValidator,
        IValidator<DoctorLeaveDto> doctorLeaveValidator)
    {
        _uow = uow;
        _workingHoursValidator = workingHoursValidator;
        _doctorLeaveValidator = doctorLeaveValidator;
    }

    public async Task<Result<List<WorkingHoursDto>>> GetWorkingHoursAsync(int doctorId)
    {
        var list = (await _uow.WorkingHours.FindAsync(w => w.DoctorId == doctorId))
            .OrderBy(w => w.DayOfWeek)
            .ThenBy(w => w.StartTime)
            .Select(w => new WorkingHoursDto
            {
                Id = w.Id,
                DoctorId = w.DoctorId,
                DayOfWeek = w.DayOfWeek,
                StartTime = w.StartTime,
                EndTime = w.EndTime
            })
            .ToList();

        return Result<List<WorkingHoursDto>>.Success(list);
    }

    public async Task<Result<WorkingHoursDto>> GetWorkingHoursByIdAsync(int id, int doctorId)
    {
        var entity = await _uow.WorkingHours.GetByIdAsync(id);
        if (entity == null || entity.DoctorId != doctorId)
        {
            return Result<WorkingHoursDto>.Failure("Working hours entry not found or unauthorized.");
        }

        return Result<WorkingHoursDto>.Success(new WorkingHoursDto
        {
            Id = entity.Id,
            DoctorId = entity.DoctorId,
            DayOfWeek = entity.DayOfWeek,
            StartTime = entity.StartTime,
            EndTime = entity.EndTime
        });
    }

    public async Task<Result<int>> AddWorkingHoursAsync(WorkingHoursDto dto, int doctorId)
    {
        dto.DoctorId = doctorId;
        var validationResult = await _workingHoursValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return Result<int>.Failure(validationResult.Errors.First().ErrorMessage);
        }

        // Check for overlapping intervals on the same day for this doctor
        var existing = await _uow.WorkingHours.FindAsync(w => w.DoctorId == doctorId && w.DayOfWeek == dto.DayOfWeek);
        bool hasOverlap = existing.Any(w => dto.StartTime < w.EndTime && dto.EndTime > w.StartTime);
        if (hasOverlap)
        {
            return Result<int>.Failure("Working hours overlap with an existing schedule for this day.");
        }

        var entity = new WorkingHours
        {
            DoctorId = doctorId,
            DayOfWeek = dto.DayOfWeek,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime
        };

        await _uow.WorkingHours.AddAsync(entity);
        await _uow.CommitAsync();

        return Result<int>.Success(entity.Id);
    }

    public async Task<Result> UpdateWorkingHoursAsync(WorkingHoursDto dto, int doctorId)
    {
        dto.DoctorId = doctorId;
        var validationResult = await _workingHoursValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return Result.Failure(validationResult.Errors.First().ErrorMessage);
        }

        var entity = await _uow.WorkingHours.GetByIdAsync(dto.Id);
        if (entity == null || entity.DoctorId != doctorId)
        {
            return Result.Failure("Working hours entry not found or unauthorized.");
        }

        // Check for overlapping intervals on the same day excluding current entry
        var existing = await _uow.WorkingHours.FindAsync(w => w.DoctorId == doctorId && w.DayOfWeek == dto.DayOfWeek && w.Id != dto.Id);
        bool hasOverlap = existing.Any(w => dto.StartTime < w.EndTime && dto.EndTime > w.StartTime);
        if (hasOverlap)
        {
            return Result.Failure("Working hours overlap with an existing schedule for this day.");
        }

        entity.DayOfWeek = dto.DayOfWeek;
        entity.StartTime = dto.StartTime;
        entity.EndTime = dto.EndTime;

        _uow.WorkingHours.Update(entity);
        await _uow.CommitAsync();

        return Result.Success();
    }

    public async Task<Result> DeleteWorkingHoursAsync(int id, int doctorId)
    {
        var entity = await _uow.WorkingHours.GetByIdAsync(id);
        if (entity == null || entity.DoctorId != doctorId)
        {
            return Result.Failure("Working hours entry not found or unauthorized.");
        }

        _uow.WorkingHours.Delete(entity);
        await _uow.CommitAsync();

        return Result.Success();
    }

    public async Task<Result<List<DoctorLeaveDto>>> GetDoctorLeavesAsync(int doctorId)
    {
        var list = (await _uow.DoctorLeaves.FindAsync(l => l.DoctorId == doctorId))
            .OrderByDescending(l => l.StartDate)
            .Select(l => new DoctorLeaveDto
            {
                Id = l.Id,
                DoctorId = l.DoctorId,
                StartDate = l.StartDate,
                EndDate = l.EndDate,
                Reason = l.Reason
            })
            .ToList();

        return Result<List<DoctorLeaveDto>>.Success(list);
    }

    public async Task<Result<List<LeaveConflictWarningDto>>> CheckLeaveConflictsAsync(int doctorId, DateTime startDate, DateTime endDate)
    {
        var activeAppointments = await _uow.Appointments.GetDoctorActiveAppointmentsInDateRangeAsync(doctorId, startDate, endDate);
        var warnings = activeAppointments.Select(a => new LeaveConflictWarningDto
        {
            AppointmentId = a.Id,
            AppointmentDate = a.AppointmentDate,
            StartTime = a.StartTime,
            PatientName = a.Patient.User.FullName,
            Status = a.Status.ToString()
        }).ToList();

        return Result<List<LeaveConflictWarningDto>>.Success(warnings);
    }

    public async Task<Result<DoctorLeaveCreateResultDto>> AddDoctorLeaveAsync(DoctorLeaveDto dto, int doctorId)
    {
        dto.DoctorId = doctorId;
        var validationResult = await _doctorLeaveValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
        {
            return Result<DoctorLeaveCreateResultDto>.Failure(validationResult.Errors.First().ErrorMessage);
        }

        // Query active appointments in this range to warn the doctor
        var conflictResult = await CheckLeaveConflictsAsync(doctorId, dto.StartDate, dto.EndDate);
        var warnings = conflictResult.Value ?? new List<LeaveConflictWarningDto>();

        var leave = new DoctorLeave
        {
            DoctorId = doctorId,
            StartDate = dto.StartDate.Date,
            EndDate = dto.EndDate.Date,
            Reason = dto.Reason?.Trim()
        };

        await _uow.DoctorLeaves.AddAsync(leave);
        await _uow.CommitAsync();

        return Result<DoctorLeaveCreateResultDto>.Success(new DoctorLeaveCreateResultDto
        {
            LeaveId = leave.Id,
            AffectedAppointments = warnings
        });
    }

    public async Task<Result> DeleteDoctorLeaveAsync(int id, int doctorId)
    {
        var entity = await _uow.DoctorLeaves.GetByIdAsync(id);
        if (entity == null || entity.DoctorId != doctorId)
        {
            return Result.Failure("Leave entry not found or unauthorized.");
        }

        _uow.DoctorLeaves.Delete(entity);
        await _uow.CommitAsync();

        return Result.Success();
    }
}
