# inspect_schema.ps1
# Read-only inspection script to extract exact EF Core metadata from ApplicationDbContextModelSnapshot.cs

$snapshotPath = "src/MediCare.Data/Migrations/ApplicationDbContextModelSnapshot.cs"
if (-not (Test-Path $snapshotPath)) {
    Write-Error "Snapshot file not found at $snapshotPath"
    exit 1
}

$lines = Get-Content $snapshotPath

Write-Host "=== MEDICARE EF CORE MODEL SNAPSHOT AUDIT ==="
Write-Host "Source: $snapshotPath"
Write-Host ""

# 1. Check WorkingHours index uniqueness
Write-Host "[1] Checking WorkingHours index uniqueness:"
$whBlock = $false
foreach ($line in $lines) {
    if ($line -match 'modelBuilder.Entity\("MediCare.Data.Entities.WorkingHours"') { $whBlock = $true }
    if ($whBlock -and $line -match 'b.HasIndex\("DoctorId", "DayOfWeek"\)') {
        Write-Host "Found line: $line"
    }
    if ($whBlock -and $line -match 'IsUnique') {
        Write-Host "Found IsUnique: $line"
    }
    if ($whBlock -and $line -match 'b.ToTable') {
        Write-Host "End of WorkingHours entity: $line"
        $whBlock = $false
    }
}

# 2. Check Appointment Filtered Index
Write-Host ""
Write-Host "[2] Checking Appointment Filtered Unique Index:"
$apptBlock = $false
foreach ($line in $lines) {
    if ($line -match 'modelBuilder.Entity\("MediCare.Data.Entities.Appointment"') { $apptBlock = $true }
    if ($apptBlock -and $line -match 'b.HasIndex\("DoctorId", "AppointmentDate", "StartTime"\)') {
        Write-Host "Found index: $line"
    }
    if ($apptBlock -and $line -match 'HasFilter') {
        Write-Host "Found filter: $line"
    }
    if ($apptBlock -and $line -match 'IsUnique') {
        Write-Host "Found IsUnique: $line"
    }
    if ($apptBlock -and $line -match 'b.ToTable') {
        $apptBlock = $false
    }
}

# 3. Check MedicalRecords properties (IsDraft, etc.)
Write-Host ""
Write-Host "[3] Checking MedicalRecords Properties & Types:"
$mrBlock = $false
foreach ($line in $lines) {
    if ($line -match 'modelBuilder.Entity\("MediCare.Data.Entities.MedicalRecord"') { $mrBlock = $true }
    if ($mrBlock -and $line -match 'b.Property<') {
        Write-Host "Property: $($line.Trim())"
    }
    if ($mrBlock -and $line -match 'b.ToTable') {
        $mrBlock = $false
    }
}

# 4. Check Prescriptions properties (VerificationToken, IsDispensed, etc.)
Write-Host ""
Write-Host "[4] Checking Prescriptions Properties & Concurrency Tokens:"
$rxBlock = $false
foreach ($line in $lines) {
    if ($line -match 'modelBuilder.Entity\("MediCare.Data.Entities.Prescription"') { $rxBlock = $true }
    if ($rxBlock -and $line -match 'b.Property<') {
        Write-Host "Property: $($line.Trim())"
    }
    if ($rxBlock -and $line -match 'IsConcurrencyToken') {
        Write-Host "Concurrency token: $($line.Trim())"
    }
    if ($rxBlock -and $line -match 'HasMaxLength') {
        Write-Host "MaxLength: $($line.Trim())"
    }
    if ($rxBlock -and $line -match 'b.ToTable') {
        $rxBlock = $false
    }
}
