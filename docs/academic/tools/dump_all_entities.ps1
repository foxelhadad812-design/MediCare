# dump_all_entities.ps1
$snapshotPath = "src/MediCare.Data/Migrations/ApplicationDbContextModelSnapshot.cs"
$content = Get-Content $snapshotPath -Raw

# Match each entity block
$regex = 'modelBuilder\.Entity\("([^"]+)", b =>\s*\{([\s\S]*?)\n\s*\}\);'
$matches = [regex]::Matches($content, $regex)

Write-Host "Total Entity Blocks Found: $($matches.Count)"

foreach ($m in $matches) {
    $entityName = $m.Groups[1].Value
    $body = $m.Groups[2].Value

    # Skip relationship-only blocks (second pass in snapshot)
    if ($body -notmatch 'b\.Property<') { continue }

    Write-Host "=========================================="
    Write-Host "ENTITY: $entityName"
    
    # Extract table name
    if ($body -match 'b\.ToTable\("([^"]+)"') {
        Write-Host "TABLE: $($Matches[1])"
    }

    # Extract properties
    $propMatches = [regex]::Matches($body, 'b\.Property<([^>]+)>\("([^"]+)"\)([\s\S]*?)(?=(b\.Property<|b\.HasKey|b\.HasIndex|\Z))')
    foreach ($p in $propMatches) {
        $clrType = $p.Groups[1].Value
        $propName = $p.Groups[2].Value
        $details = $p.Groups[3].Value.Trim()
        
        $sqlType = ""
        if ($details -match 'HasColumnType\("([^"]+)"\)') { $sqlType = $Matches[1] }
        $maxLen = ""
        if ($details -match 'HasMaxLength\((\d+)\)') { $maxLen = $Matches[1] }
        $defaultVal = ""
        if ($details -match 'HasDefaultValue\(([^)]+)\)') { $defaultVal = $Matches[1] }
        if ($details -match 'HasDefaultValueSql\("([^"]+)"\)') { $defaultVal = $Matches[1] }
        $concurrency = if ($details -match 'IsConcurrencyToken') { "CONCURRENCY_TOKEN" } else { "" }

        Write-Host "  * $propName ($clrType) => SQL: '$sqlType' MaxLen: '$maxLen' Default: '$defaultVal' $concurrency"
    }

    # Extract Indexes
    $indexMatches = [regex]::Matches($body, 'b\.HasIndex\(([^)]+)\)([\s\S]*?)(?=(b\.HasIndex|b\.ToTable|\Z))')
    foreach ($idx in $indexMatches) {
        $cols = $idx.Groups[1].Value.Replace('"', '')
        $idxDetails = $idx.Groups[2].Value.Trim()
        $isUnique = if ($idxDetails -match '\.IsUnique\(\)') { "UNIQUE" } else { "NON-UNIQUE" }
        $idxName = ""
        if ($idxDetails -match 'HasDatabaseName\("([^"]+)"\)') { $idxName = $Matches[1] }
        $filter = ""
        if ($idxDetails -match 'HasFilter\("([^"]+)"\)') { $filter = $Matches[1] }

        Write-Host "  -> INDEX: ($cols) Name: '$idxName' $isUnique Filter: '$filter'"
    }
}
