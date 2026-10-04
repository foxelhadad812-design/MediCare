using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.AppointmentDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(a => a.StartTime)
            .HasColumnType("time(0)")
            .IsRequired();

        builder.Property(a => a.EndTime)
            .HasColumnType("time(0)")
            .IsRequired();

        builder.Property(a => a.Status)
            .IsRequired();

        builder.Property(a => a.ConsultationFee)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(a => a.PaymentStatus)
            .IsRequired();

        builder.Property(a => a.Type)
            .IsRequired();

        builder.Property(a => a.Notes)
            .HasMaxLength(500);

        builder.Property(a => a.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        // Concurrency-Safe Filtered Unique Index: guarantees 0 double-booking for active slots
        // Note: SQL Server filtered indexes do not support NOT / NOT IN predicates; <> and AND are required.
        builder.HasIndex(a => new { a.DoctorId, a.AppointmentDate, a.StartTime })
            .IsUnique()
            .HasFilter("[Status] <> 3 AND [Status] <> 4")
            .HasDatabaseName("IX_Appointments_Doctor_NoOverlap");

        builder.HasOne(a => a.Doctor)
            .WithMany(d => d.Appointments)
            .HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
