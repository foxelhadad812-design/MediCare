using FluentValidation;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Services.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Infrastructure & Clock
        services.AddSingleton<IClinicClock, ClinicClock>();

        // Application Business Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDoctorService, DoctorService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<ISlotEngineService, SlotEngineService>();

        // FluentValidation Validators
        services.AddValidatorsFromAssemblyContaining<PatientRegisterValidator>();

        return services;
    }
}
