namespace MediCare.Data.Entities;

public class Prescription : BaseAuditableEntity
{
    public int MedicalRecordId { get; set; }
    public int DoctorId { get; set; }
    public int PatientId { get; set; }
    public DateTime PrescriptionDate { get; set; }
    public string? Notes { get; set; }

    public virtual MedicalRecord MedicalRecord { get; set; } = null!;
    public virtual Doctor Doctor { get; set; } = null!;
    public virtual Patient Patient { get; set; } = null!;
    public virtual ICollection<PrescriptionItem> Items { get; set; } = new List<PrescriptionItem>();
}
