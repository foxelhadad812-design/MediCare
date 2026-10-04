using MediCare.Data.Enums;

namespace MediCare.Data.Entities;

public class Appointment : BaseAuditableEntity
{
    public int DoctorId { get; set; }
    public int PatientId { get; set; }
    public DateTime AppointmentDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    public decimal ConsultationFee { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    public AppointmentType Type { get; set; } = AppointmentType.Consultation;
    public string? Notes { get; set; }

    public virtual Doctor Doctor { get; set; } = null!;
    public virtual Patient Patient { get; set; } = null!;
    public virtual MedicalRecord? MedicalRecord { get; set; }
}
