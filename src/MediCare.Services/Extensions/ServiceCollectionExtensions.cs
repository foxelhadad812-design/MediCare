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

        // Communication & Notifications (Pluggable Cloud Providers):
        // Defaults to MailKitEmailService (SMTP) and MockSmsService (local simulation/log).
        // For production cloud deployments, SendGridEmailService and TwilioSmsService are pluggable
        // implementations of IEmailService and ISmsService (configured via SendGrid / Twilio options).
        services.AddScoped<IEmailService, MailKitEmailService>();
        services.AddScoped<ISmsService, MockSmsService>();
        services.AddScoped<IMedicalRecordService, MedicalRecordService>();
        services.AddScoped<IPrescriptionService, PrescriptionService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<AttachmentStorageMigrationHelper>();

        // Enterprise AI & Payment Services:
        // - IAiAssistantService (OpenAiAssistantService): Primary enterprise assistant engine with patient consent
        //   verification, PII redaction guardrails, prompt injection defenses, and Azure OpenAI failover.
        // - IOpenAiChatbotClient (OpenAiResponsesChatbotClient): Lightweight direct client for structured responses API.
        services.AddScoped<ChatbotService>();
        services.AddHttpClient<IAiAssistantService, OpenAiAssistantService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(25);
        });
        services.AddHttpClient<IOpenAiChatbotClient, OpenAiResponsesChatbotClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddScoped<IChatbotService, AiChatbotService>();
        services.AddHttpClient<PaymobPaymentGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<SimulatedPaymentGateway>();
        services.AddScoped<IPaymentGateway>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymobSettings>>().Value;
            if (string.Equals(options.Provider, "Paymob", StringComparison.OrdinalIgnoreCase))
            {
                return sp.GetRequiredService<PaymobPaymentGateway>();
            }
            if (string.Equals(options.Provider, "Simulated", StringComparison.OrdinalIgnoreCase))
            {
                return sp.GetRequiredService<SimulatedPaymentGateway>();
            }

            throw new InvalidOperationException(
                $"Unsupported payment provider '{options.Provider}'. Supported providers are 'Paymob' or 'Simulated' (Development only). " +
                "Application execution cannot continue with an unknown payment provider.");
        });
        services.AddScoped<IPaymentService, PaymentService>();

        // Automated 24-Hour Reminder Services
        services.AddScoped<IAppointmentReminderService, AppointmentReminderService>();
        services.AddHostedService<MediCare.Services.BackgroundServices.AppointmentReminderBackgroundService>();

        // FluentValidation Validators
        services.AddValidatorsFromAssemblyContaining<PatientRegisterValidator>();

        return services;
    }
}
