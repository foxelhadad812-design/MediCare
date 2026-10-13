using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

/// <summary>
/// Realistic Clinical Evaluation Test Suite for MediCare AI & Rule-based Clinic Assistant.
/// Validates genuine medical safety triage, acute emergency detection, prescription refusals,
/// and outpatient specialty routing across authentic patient clinical vignettes (Bilingual AR / EN).
/// </summary>
public class ClinicalRealismMedicalTests
{
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<IAiAssistantService> _mockAiAssistant;
    private readonly ChatbotService _localChatbot;
    private readonly AiChatbotService _sut;

    public ClinicalRealismMedicalTests()
    {
        _mockUow = new Mock<IUnitOfWork>();
        _mockAiAssistant = new Mock<IAiAssistantService>();

        // Seed representative clinic doctors for specialty routing
        var cardiologyDoc = new Doctor
        {
            Id = 1,
            ConsultationFee = 400m,
            IsApproved = true,
            User = new ApplicationUser { FullName = "Dr. Magdi Yacoub" },
            Specialization = new Specialization { Id = 1, Name = "Cardiology" }
        };

        var orthopedicsDoc = new Doctor
        {
            Id = 2,
            ConsultationFee = 350m,
            IsApproved = true,
            User = new ApplicationUser { FullName = "Dr. Hazem Ortho" },
            Specialization = new Specialization { Id = 2, Name = "Orthopedics" }
        };

        var internalDoc = new Doctor
        {
            Id = 3,
            ConsultationFee = 300m,
            IsApproved = true,
            User = new ApplicationUser { FullName = "Dr. Sameh Internal" },
            Specialization = new Specialization { Id = 3, Name = "Internal Medicine" }
        };

        var pediatricsDoc = new Doctor
        {
            Id = 4,
            ConsultationFee = 300m,
            IsApproved = true,
            User = new ApplicationUser { FullName = "Dr. Hany Pediatrics" },
            Specialization = new Specialization { Id = 4, Name = "Pediatrics" }
        };

        _mockUow.Setup(u => u.Doctors.GetApprovedDoctorsAsync(It.IsAny<int?>()))
            .ReturnsAsync(new List<Doctor> { cardiologyDoc, orthopedicsDoc, internalDoc, pediatricsDoc });

        _localChatbot = new ChatbotService(_mockUow.Object);
        _sut = new AiChatbotService(_localChatbot, _mockAiAssistant.Object, NullLogger<AiChatbotService>.Instance);
    }

    [Theory]
    // Clinical Vignette 1: Acute Coronary Syndrome (ACS) / Myocardial Infarction
    [InlineData(
        "55-year-old experiencing crushing retrosternal chest pain radiating to left arm and jaw with cold sweats and shortness of breath for 30 minutes",
        "en",
        true,
        "123")]
    [InlineData(
        "رجل 55 سنة يعاني من ألم شديد في الصدر ممتد للكتف الأيسر والفك مع عرق بارد وغثيان وضيق تنفس مستمر منذ نصف ساعة",
        "ar",
        true,
        "123")]
    // Clinical Vignette 2: Acute Ischemic Stroke (FAST Protocol: Facial droop, Arm weakness, Slurred speech)
    [InlineData(
        "Sudden right-sided facial drop, arm weakness, and slurred speech starting 15 minutes ago with inability to walk",
        "en",
        true,
        "123")]
    [InlineData(
        "صعوبة مفاجئة في نطق الكلام وتنميل نصفي وشلل في الذراع الأيمن واعوجاج في الفم منذ ربع ساعة",
        "ar",
        true,
        "123")]
    // Clinical Vignette 3: Acute Respiratory Failure / Severe Bronchospasm
    [InlineData(
        "Severe shortness of breath, suffocating, unable to complete a sentence, audible wheezing",
        "en",
        true,
        "123")]
    [InlineData(
        "أزمة ربو حادة واختناق ومش قادر اتنفس خالص وزرقة في الشفايف",
        "ar",
        true,
        "123")]
    public async Task RealisticEmergencyVignettes_MustTriggerImmediateEmergencyTriage(
        string clinicalPrompt, string culture, bool expectedEmergency, string expectedEmergencyNumber)
    {
        // Arrange
        var request = new ChatbotRequestDto
        {
            Message = clinicalPrompt,
            Culture = culture,
            ConsentGiven = false // Even without AI consent, emergency triage MUST execute locally
        };

        // Act
        var response = await _sut.ProcessMessageAsync(request);

        // Assert
        response.IsEmergency.Should().Be(expectedEmergency, "Critical medical emergency must be flagged");
        response.EngineMode.Should().Be("emergency_triage");
        response.EmergencyNotice.Should().Contain(expectedEmergencyNumber, "Emergency notice must provide official ambulance dispatch number");
        _mockAiAssistant.Verify(a => a.GenerateGuidanceAsync(It.IsAny<AiAssistantRequestDto>(), It.IsAny<CancellationToken>()), Times.Never,
            "Critical emergency must never incur external network roundtrip latency");
    }

    [Theory]
    // Clinical Vignette 4: Dangerous Medication Prescription & Dosage Request
    [InlineData("Prescribe me Augmentin 1g twice daily for sore throat and advise me on drug dosage", "en")]
    [InlineData("اكتب لي روشتة مضاد حيوي أوجمنتين 1 جم لالتهاب الحلق وقولي جرعة الدواء المناسبة كام", "ar")]
    public async Task PrescriptionRequests_MustRefusePrescription_AndRequireClinicalEncounter(
        string medicationRequestPrompt, string culture)
    {
        // Arrange
        var request = new ChatbotRequestDto
        {
            Message = medicationRequestPrompt,
            Culture = culture,
            ConsentGiven = false
        };

        // Act
        var response = await _sut.ProcessMessageAsync(request);

        // Assert
        MedicalSafetyGuard.IsMedicationOrPrescriptionRequest(medicationRequestPrompt).Should().BeTrue();
        response.ReplyText.Should().NotContain("mg", "Assistant must NEVER formulate dosage or prescription");
        response.Disclaimer.Should().NotBeNullOrEmpty("Standard clinical disclaimer must accompany all responses");
    }

    [Theory]
    // Clinical Vignette 5: Chronic Knee Osteoarthritis (Non-Emergency Outpatient Routing)
    [InlineData("Severe knee joint pain and swelling when climbing stairs for past 2 months", "en", "Orthopedics")]
    [InlineData("أعاني من ألم شديد في الركبة وتورم في المفصل عند صعود الدرج", "ar", "Orthopedics")]
    // Clinical Vignette 6: Infant Skin Disorder / Pediatric Outpatient Routing
    [InlineData("Child persistent cough and high fever for 3 days", "en", "Pediatrics")]
    [InlineData("طفلي يعاني من كحة مستمرة وارتفاع في درجة الحرارة وسخونة منذ يومين", "ar", "Pediatrics")]
    public async Task RealisticOutpatientPresentations_CorrectlyRouteToAppropriateSpecialty(
        string outpatientPrompt, string culture, string expectedSpecialty)
    {
        // Arrange
        var request = new ChatbotRequestDto
        {
            Message = outpatientPrompt,
            Culture = culture,
            ConsentGiven = false
        };

        // Act
        var response = await _sut.ProcessMessageAsync(request);

        // Assert
        response.IsEmergency.Should().BeFalse("Chronic outpatient symptoms should not be flagged as life-threatening emergencies");
        response.RecommendedSpecialization.Should().Be(expectedSpecialty, $"'{outpatientPrompt}' should route to {expectedSpecialty}");
        response.Doctors.Should().NotBeEmpty("Matching clinic specialist doctors must be suggested to the patient");
        response.Doctors.Should().AllSatisfy(d => d.Specialization.Should().Be(expectedSpecialty));
    }
}
