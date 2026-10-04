using FluentValidation;
using MediCare.Services.Contracts;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace MediCare.Services.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Application Business Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDoctorService, DoctorService>();

        // FluentValidation Validators
        services.AddValidatorsFromAssemblyContaining<PatientRegisterValidator>();

        return services;
    }
}
