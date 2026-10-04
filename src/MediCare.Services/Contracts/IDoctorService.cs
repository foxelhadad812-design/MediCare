using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IDoctorService
{
    Task<PagedResult<DoctorSummaryDto>> SearchDoctorsAsync(DoctorFilterDto filter);
    Task<Result<DoctorDetailDto>> GetDoctorDetailsAsync(int id);
    Task<List<SpecializationDto>> GetSpecializationsAsync();
}
