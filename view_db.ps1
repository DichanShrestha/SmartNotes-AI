# SmartNotes AI - Database Viewer Script
param(
    [string]$Table = ""
)

$connString = "Data Source=(LocalDb)\MSSQLLocalDB;Initial Catalog=SmartNotesAI;Integrated Security=True"

try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($connString)
    $conn.Open()

    if ([string]::IsNullOrWhiteSpace($Table)) {
        Write-Host "==============================================" -ForegroundColor Cyan
        Write-Host "   SmartNotes AI - Database Tables Summary    " -ForegroundColor Cyan
        Write-Host "==============================================" -ForegroundColor Cyan
        
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND TABLE_NAME NOT LIKE '__Migration%' ORDER BY TABLE_SCHEMA, TABLE_NAME"
        $reader = $cmd.ExecuteReader()
        
        $tables = @()
        while ($reader.Read()) {
            $tables += [PSCustomObject]@{ Schema = $reader[0]; Name = $reader[1] }
        }
        $reader.Close()

        foreach ($t in $tables) {
            $countCmd = $conn.CreateCommand()
            $countCmd.CommandText = "SELECT COUNT(*) FROM [$($t.Schema)].[$($t.Name)]"
            $count = $countCmd.ExecuteScalar()
            $fullName = "$($t.Schema).$($t.Name)"
            Write-Host ("  {0,-32} : {1,4} row(s)" -f $fullName, $count) -ForegroundColor Yellow
        }

        Write-Host ""
        Write-Host "Tip: Run .\view_db.ps1 <TableName> (e.g. .\view_db.ps1 Documents) to view rows." -ForegroundColor Gray
    } else {
        Write-Host "==============================================" -ForegroundColor Cyan
        Write-Host "   Table Data: $Table                         " -ForegroundColor Cyan
        Write-Host "==============================================" -ForegroundColor Cyan

        $targetTable = if ($Table.Contains(".")) { $Table } else { "dbo.[$Table]" }
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter("SELECT TOP 20 * FROM $targetTable", $conn)
        $dt = New-Object System.Data.DataTable
        $adapter.Fill($dt) | Out-Null

        if ($dt.Rows.Count -eq 0) {
            Write-Host "No records found in table '$Table'." -ForegroundColor Yellow
        } else {
            $dt | Format-Table -AutoSize
        }
    }

    $conn.Close()
} catch {
    Write-Error "Failed to connect to LocalDB: $_"
}
