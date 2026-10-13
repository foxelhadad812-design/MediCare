using System;
using System.Collections.Generic;
using System.Linq;

namespace MediCare.Services.Common;

/// <summary>
/// Server-side deterministic medical safety guardrails.
/// Detects emergency red flags, rejects unsafe prescription requests,
/// and guarantees standard medical disclaimer attachments.
/// </summary>
public static class MedicalSafetyGuard
{
    private static readonly string[] EmergencyArabicKeywords =
    {
        "ألم في الصدر", "الم في الصدر", "ألم شديد بالصدر", "وجع في صدري", "وجع في الصدر", "وجع بالصدر",
        "الم بالصدر", "ألم بالصدر", "جلطة", "سكته", "سكتة", "ازمة قلبية", "أزمة قلبية", "ذبحة", "ذبحة صدرية",
        "شلل", "تنميل نصفي", "نزيف حاد", "نزيف مستمر", "نزيف", "تشنج", "تشنجات", "فقدان وعي", "اغماء", "إغماء",
        "غيبوبة", "ضيق تنفس", "ضيق تنفس حاد", "ضيق في التنفس", "مش قادر اتنفس", "اختناق", "كحة دم", "سعال دموي",
        "تسمم", "انتحار"
    };

    private static readonly string[] EmergencyEnglishKeywords =
    {
        "chest pain", "heart attack", "stroke", "paralysis", "loss of consciousness",
        "unconscious", "passed out", "severe bleeding", "bleeding profusely", "coughing blood",
        "difficulty breathing", "cannot breathe", "can't breathe", "shortness of breath",
        "short of breath", "suffocating", "severe convulsion", "seizure", "poisoning",
        "suicide", "overdose", "faint", "fainting", "severe allergic reaction", "anaphylaxis"
    };

    private static readonly string[] PrescriptionArabicKeywords =
    {
        "اكتبلي علاج", "اكتب لي علاج", "اكتبلي دوا", "اكتب لي دواء", "روشتة", "جرعة الدواء",
        "ايه الجرعة", "عايز مضاد حيوي", "اديني مسكن", "جرعة الباراسيتامول"
    };

    private static readonly string[] PrescriptionEnglishKeywords =
    {
        "prescribe me", "prescribe medicine", "prescription for", "drug dosage",
        "what dosage", "give me antibiotics", "recommend a pill", "which medication should i take"
    };

    private static string NormalizeArabic(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text
            .Replace('أ', 'ا')
            .Replace('إ', 'ا')
            .Replace('آ', 'ا')
            .Replace('ة', 'ه')
            .Replace('ى', 'ي');
        return new string(normalized.Where(c => c < 0x064B || c > 0x0652).ToArray());
    }

    public static bool IsEmergencySymptom(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        var lower = message.Trim().ToLowerInvariant();
        if (EmergencyEnglishKeywords.Any(k => lower.Contains(k)))
        {
            return true;
        }

        var norm = NormalizeArabic(lower);
        return EmergencyArabicKeywords.Any(k => lower.Contains(k) || norm.Contains(NormalizeArabic(k)));
    }

    public static bool IsMedicationOrPrescriptionRequest(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        var lower = message.Trim().ToLowerInvariant();
        if (PrescriptionEnglishKeywords.Any(k => lower.Contains(k)))
        {
            return true;
        }

        var norm = NormalizeArabic(lower);
        return PrescriptionArabicKeywords.Any(k => lower.Contains(k) || norm.Contains(NormalizeArabic(k)));
    }

    public static string GetEmergencyGuidance(string? culture)
    {
        bool isArabic = culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ?? false;
        return isArabic
            ? "⚠️ تنبيه طوارئ عاجل: الأعراض التي وصفتها قد تشير إلى حالة طبية حرجة تتطلب تدخلاً فورياً. يرجى التوجه فوراً لأقرب قسم طوارئ في مستشفى أو الاتصال بالإسعاف (123 في مصر). لا تنتظر استشارة إلكترونية."
            : "⚠️ CRITICAL EMERGENCY NOTICE: The symptoms you described may indicate an urgent medical emergency. Please call local emergency services (123 in Egypt / 911) or proceed immediately to the nearest hospital Emergency Room without delay.";
    }

    public static string GetMedicationSafetyNotice(string? culture)
    {
        bool isArabic = culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ?? false;
        return isArabic
            ? "تنبيه أمان دوائي: المساعد الذكي غير مصرح له بوصف أدوية أو تعديل جرعات علاجية. استشارة الطبيب المرخص وفحصه السريري إلزامية لسلامتك قبل تناول أي علاج."
            : "Medication Safety Notice: This AI assistant is not permitted to prescribe medications, specify dosages, or modify treatment plans. A physical evaluation by a licensed doctor is required for safe prescribing.";
    }

    public static string GetStandardDisclaimer(string? culture)
    {
        bool isArabic = culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ?? false;
        return isArabic
            ? "معلومات استرشادية فقط لتوجيهك للتخصص المناسب بالعيادة. هذا المساعد ليس طبيباً ولا يقدم تشخيصاً طبياً أو بديلاً عن الفحص السريري."
            : "For general educational and clinic routing purposes only. This AI assistant does not provide a medical diagnosis or replace professional clinical care.";
    }
}
