using FluentValidation;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.Factories;
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

        // Domain Factories
        services.AddSingleton<AppointmentFactory>();
        services.AddSingleton<NotificationFactory>();

        // Application Business Services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDoctorService, DoctorService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<ISlotEngineService, SlotEngineService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IAppointmentService, AppointmentService>();

        // Sprint 3 Services
        services.AddScoped<IFileStorageService, FileStorageService>();
        services.AddScoped<IEmailService, MailKitEmailService>();
        services.AddScoped<ISmsService, MockSmsService>();
        services.AddScoped<IMedicalRecordService, MedicalRecordService>();
        services.AddScoped<IPrescriptionService, PrescriptionService>();
        services.AddScoped<IAdminService, AdminService>();

        // Enterprise AI & Payment Services
        services.AddScoped<IChatbotService, ChatbotService>();
        services.AddScoped<IPaymentService, PaymentService>();

        // FluentValidation Validators
        services.AddValidatorsFromAssemblyContaining<PatientRegisterValidator>();

        return services;
    }
}
