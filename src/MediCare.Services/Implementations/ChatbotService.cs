using System.Text.RegularExpressions;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;

namespace MediCare.Services.Implementations;

public class ChatbotService : IChatbotService
{
    private readonly IUnitOfWork _unitOfWork;

    public ChatbotService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ChatbotResponseDto> ProcessMessageAsync(ChatbotRequestDto request)
    {
        var message = (request.Message ?? string.Empty).Trim().ToLowerInvariant();
        var isArabic = request.Culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true
                       || Regex.IsMatch(request.Message ?? "", @"[\u0600-\u06FF]");

        var response = new ChatbotResponseDto();
        string? matchedSpecialtyName = null;

        // 0. Critical Emergency Triage
        if (ContainsAny(message, "chest pain", "ألم في الصدر", "وجع في الصدر", "shortness of breath", "ضيق تنفس", "cannot breathe", "مش قادر اتنفس", "bleeding", "نزيف", "stroke", "جلطة", "numbness", "unconscious", "فقدان وعي", "faint", "إغماء"))
        {
            response.IsEmergency = true;
            response.EmergencyNotice = isArabic
                ? "تنبيه طوارئ عاجل: هذه الأعراض قد تشير لحالة حرجة. يرجى التوجه فوراً لأقرب قسم طوارئ بمستشفى أو الاتصال بالإسعاف (123)."
                : "EMERGENCY NOTICE: These symptoms may indicate an urgent or life-threatening condition. Please proceed immediately to the nearest Emergency Department or dial Emergency Services (123 / 911).";
        }

        // 1. Symptom & Specialty Analysis
        if (ContainsAny(message, "قلب", "صدر", "ألم في الصدر", "نهجان", "ضغط", "ضربات", "خفقان", "heart", "chest", "palpitation", "cardio", "hypertension"))
        {
            matchedSpecialtyName = "Cardiology";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "بناءً على الأعراض المذكورة (ألم الصدر أو اضطراب ضربات القلب)، ننصح بمراجعة استشاري أمراض القلب والأوعية الدموية لإجراء فحص سريري وتخطيط للقلب."
                : "Based on the reported symptoms (chest discomfort, palpitations, or blood pressure concerns), we recommend consulting a Cardiology Specialist for comprehensive clinical assessment.";
        }
        else if (ContainsAny(message, "جلد", "بشرة", "حبوب", "طفح", "حكة", "تساقط", "شعر", "إكزيما", "skin", "rash", "acne", "hair", "derma", "itching", "eczema"))
        {
            matchedSpecialtyName = "Dermatology";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "تشير الأعراض إلى مشكلة جلدية. نوصي باستشارة طبيب متخصص في الجلدية والتجميل لتقييم الحالة ووصف العلاج المناسب."
                : "These symptoms suggest a dermatological condition. We recommend booking with a Dermatology specialist for direct clinical diagnosis.";
        }
        else if (ContainsAny(message, "طفل", "أطفال", "رضيع", "سخونية الطفل", "تطعيم", "حرارة طفلي", "child", "pediatric", "baby", "infant", "toddler"))
        {
            matchedSpecialtyName = "Pediatrics";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "لصحة وسلامة طفلك، نوفر نخبة من استشاريي طب الأطفال وحديثي الولادة لمتابعة النمو والتطعيمات وعلاج الحالات الطارئة."
                : "For your child's well-being, we have experienced Pediatric specialists available for developmental screenings, immunizations, and general care.";
        }
        else if (ContainsAny(message, "عظام", "مفصل", "ركبة", "كسر", "ظهر", "فقرات", "عمود فقري", "bone", "joint", "ortho", "knee", "spine", "fracture", "back pain"))
        {
            matchedSpecialtyName = "Orthopedics";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "آلام العظام والمفاصل والعمود الفقري تتطلب فحصاً سريرياً دقيقاً. نوصي بحجز موعد مع استشاري جراحة العظام والمفاصل."
                : "Musculoskeletal or joint discomfort requires specialized examination. We suggest consulting with an Orthopedic Surgery consultant.";
        }
        else if (ContainsAny(message, "باطنة", "معدة", "سكر", "قولون", "كبد", "إرهاق", "صداع", "حرارة", "سخونة", "stomach", "diabetes", "colon", "internal", "headache", "fatigue", "fever"))
        {
            matchedSpecialtyName = "General Internal Medicine";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "يوصى باستشارة طبيب الأمراض الباطنية العامة لإجراء فحص شامل وتشخيص الأعراض وتحديد التحاليل المطلوبة."
                : "We recommend seeing an Internal Medicine consultant for a comprehensive health assessment, blood work evaluation, and targeted treatment.";
        }
        else if (ContainsAny(message, "إلغاء", "الغاء", "cancel"))
        {
            response.ReplyText = isArabic
                ? "يمكنك إلغاء موعدك بسهولة من صفحة 'مواعيدي' بشرط أن يتبقى أكثر من ساعتين على موعد الكشف، وسيتم تحرير الشريحة الزمنية تلقائياً."
                : "You can cancel your consultation at any time up to 2 hours prior to the scheduled slot from your 'My Appointments' page.";
            response.QuickReplies = isArabic
                ? new() { "استعراض الأطباء", "سعر الكشف", "طريقة الحجز" }
                : new() { "Find Doctors", "Consultation Fees", "How to Book" };
            return response;
        }
        else if (ContainsAny(message, "سعر", "اسعار", "أتعاب", "fee", "cost", "price"))
        {
            response.ReplyText = isArabic
                ? "تتراوح رسوم الكشف في MediCare بين 180 إلى 350 جنيهاً مصرياً حسب خبرة وتخصص الطبيب، مع إمكانية الدفع نقداً بالعيادة أو إلكترونياً بالبطاقة."
                : "Consultation fees at MediCare range between 180 and 350 EGP depending on physician seniority. You can pay online or upon clinic arrival.";
            response.QuickReplies = isArabic
                ? new() { "أطباء القلب", "أطباء الباطنة", "أطباء الأطفال" }
                : new() { "Cardiology", "Internal Medicine", "Pediatrics" };
            return response;
        }
        else if (ContainsAny(message, "حجز", "احجز", "book", "appointment", "schedule"))
        {
            response.ReplyText = isArabic
                ? "لحجز موعد: توجه لصفحة 'ابحث عن طبيب'، اختر التخصص والطبيب المناسب، ثم اختر التاريخ والوقت المتاح واضغط 'حجز موعد'."
                : "To book an appointment: Navigate to 'Find Doctors', select your preferred specialist, choose an available date and time slot, and click 'Book Slot'.";
            response.QuickReplies = isArabic
                ? new() { "ابحث عن طبيب", "استشارة باطنة", "استشارة قلب" }
                : new() { "Find Doctors", "Book Cardiology", "Book Internal" };
            return response;
        }
        else if (ContainsAny(message, "سلام", "مرحبا", "أهلا", "اهلا", "صباح الخير", "مساء الخير", "hello", "hi", "hey"))
        {
            response.ReplyText = isArabic
                ? "أهلاً بك في MediCare! أنا مساعدك الطبي الذكي. كيف يمكنني مساعدتك اليوم؟ يمكنك إخباري بأعراضك لتوجيهك للتخصص والطبيب المناسب."
                : "Welcome to MediCare! I am your AI clinical assistant. How can I assist you today? Tell me what you are feeling and I will recommend the right specialist.";
            response.QuickReplies = isArabic
                ? new() { "ألم في الصدر", "حساسية أو طفح جلدي", "كشف أطفال", "ألم في المفاصل", "استشارة باطنة" }
                : new() { "Chest discomfort", "Skin allergy", "Child fever", "Joint pain", "Internal Medicine" };
            return response;
        }
        else
        {
            response.ReplyText = isArabic
                ? "شكراً لتواصلك مع MediCare! هل يمكنك توضيح الأعراض التي تشعر بها أكثر (مثل ألم في الصدر، مشكلة جلدية، ألم مفاصل، أو كشف أطفال)؟"
                : "Thank you for reaching out to MediCare! Could you tell me more about your symptoms (e.g. chest discomfort, skin symptoms, joint pain, or child care)?";
            response.QuickReplies = isArabic
                ? new() { "ألم في الصدر", "طفح جلدي", "ألم في الركبة", "ارتفاع حرارة" }
                : new() { "Cardiology", "Dermatology", "Orthopedics", "General Internal" };
            return response;
        }

        // Fetch doctors for recommended specialty
        if (!string.IsNullOrEmpty(matchedSpecialtyName) && _unitOfWork.Doctors != null)
        {
            var allDoctors = await _unitOfWork.Doctors.GetApprovedDoctorsAsync(null);
            if (allDoctors != null)
            {
                var specialtyDocs = allDoctors
                    .Where(d => d.Specialization != null && d.Specialization.Name.Contains(matchedSpecialtyName, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToList();

                foreach (var doc in specialtyDocs)
                {
                    response.Doctors.Add(new ChatbotDoctorCardDto
                    {
                        Id = doc.Id,
                        FullName = doc.User != null ? doc.User.FullName : $"Doctor #{doc.Id}",
                        Specialization = doc.Specialization?.Name ?? matchedSpecialtyName,
                        Fee = doc.ConsultationFee,
                        AvatarUrl = doc.ProfileImageUrl
                    });
                }
            }
        }

        response.QuickReplies = isArabic
            ? new() { "حجز موعد", "سعر الكشف", "سياسة الإلغاء", "تخصص آخر" }
            : new() { "Book Appointment", "Consultation Fees", "Cancellation Policy", "Other Specialty" };

        return response;
    }

    private static bool ContainsAny(string source, params string[] terms)
    {
        return terms.Any(t => source.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}
