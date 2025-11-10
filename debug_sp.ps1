# PowerShell script to debug GetUserDatabaseInformation stored procedure
$connectionString = "Server=172.29.1.40,53257;Database=CS_DEMO_2502;User Id=CSDBUSER2508SQL;Password=CSS3cur1ty!;TrustServerCertificate=true;"
$email = "georgek@inecom.com.au"

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $connectionString
    $connection.Open()
    Write-Host "Connected to database successfully" -ForegroundColor Green

    # Step 1: Check if user exists
    Write-Host "`n=== STEP 1: Check if user exists ===" -ForegroundColor Cyan
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT OnboardedUserId, Email, Name, IsActive, UserActive, OrganizationId FROM OnboardedUsers WHERE Email = @email"
    $command.Parameters.AddWithValue("@email", $email) | Out-Null
    $reader = $command.ExecuteReader()
    if ($reader.HasRows) {
        while ($reader.Read()) {
            Write-Host "Found: UserId=$($reader[0]), Email=$($reader[1]), Name=$($reader[2]), IsActive=$($reader[3]), UserActive=$($reader[4]), OrgId=$($reader[5])"
        }
    } else {
        Write-Host "No user found with email: $email" -ForegroundColor Yellow
    }
    $reader.Close()

    # Step 2: Check user with organization
    Write-Host "`n=== STEP 2: User with Organization Details ===" -ForegroundColor Cyan
    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT
    u.OnboardedUserId, u.Email, u.Name, u.IsActive, u.UserActive, u.OrganizationId,
    o.Name as OrgName, o.StateCode, o.StatusCode
FROM OnboardedUsers u
LEFT JOIN Organizations o ON u.OrganizationId = o.OrganizationId
WHERE u.Email = @email
"@
    $command.Parameters.Clear()
    $command.Parameters.AddWithValue("@email", $email) | Out-Null
    $reader = $command.ExecuteReader()
    if ($reader.HasRows) {
        while ($reader.Read()) {
            Write-Host "UserId=$($reader[0])"
            Write-Host "  Email: $($reader[1])"
            Write-Host "  Name: $($reader[2])"
            Write-Host "  IsActive: $($reader[3])"
            Write-Host "  UserActive: $($reader[4])"
            Write-Host "  OrgId: $($reader[5])"
            Write-Host "  OrgName: $($reader[6])"
            Write-Host "  StateCode: $($reader[7])"
            Write-Host "  StatusCode: $($reader[8])"
        }
    } else {
        Write-Host "No matching records found" -ForegroundColor Yellow
    }
    $reader.Close()

    # Step 3: Check UserDatabaseAssignments
    Write-Host "`n=== STEP 3: UserDatabaseAssignments ===" -ForegroundColor Cyan
    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT uda.UserId, uda.DatabaseCredentialId, uda.IsActive, uda.AssignedOn
FROM UserDatabaseAssignments uda
WHERE uda.UserId IN (SELECT OnboardedUserId FROM OnboardedUsers WHERE Email = @email)
"@
    $command.Parameters.Clear()
    $command.Parameters.AddWithValue("@email", $email) | Out-Null
    $reader = $command.ExecuteReader()
    if ($reader.HasRows) {
        $count = 0
        while ($reader.Read()) {
            $count++
            Write-Host "Assignment $($count): UserId=$($reader[0]), DbCredId=$($reader[1]), IsActive=$($reader[2]), AssignedOn=$($reader[3])"
        }
    } else {
        Write-Host "No database assignments found" -ForegroundColor Yellow
    }
    $reader.Close()

    # Step 4: Check DatabaseCredentials
    Write-Host "`n=== STEP 4: DatabaseCredentials ===" -ForegroundColor Cyan
    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT dc.Id, dc.DatabaseName, dc.InstanceName, dc.IsActive
FROM DatabaseCredentials dc
WHERE dc.Id IN (
    SELECT uda.DatabaseCredentialId FROM UserDatabaseAssignments uda
    WHERE uda.UserId IN (SELECT OnboardedUserId FROM OnboardedUsers WHERE Email = @email)
)
"@
    $command.Parameters.Clear()
    $command.Parameters.AddWithValue("@email", $email) | Out-Null
    $reader = $command.ExecuteReader()
    if ($reader.HasRows) {
        while ($reader.Read()) {
            Write-Host "DbCred: Id=$($reader[0]), DbName=$($reader[1]), Instance=$($reader[2]), IsActive=$($reader[3])"
        }
    } else {
        Write-Host "No database credentials found" -ForegroundColor Yellow
    }
    $reader.Close()

    # Step 5: Execute the stored procedure
    Write-Host "`n=== STEP 5: Execute GetUserDatabaseInformation ===" -ForegroundColor Cyan
    $command = $connection.CreateCommand()
    $command.CommandText = "EXEC [dbo].[GetUserDatabaseInformation] @UserEmail = @email"
    $command.Parameters.Clear()
    $command.Parameters.AddWithValue("@email", $email) | Out-Null
    $reader = $command.ExecuteReader()
    if ($reader.HasRows) {
        Write-Host "SP returned results:" -ForegroundColor Green
        $count = 0
        while ($reader.Read()) {
            $count++
            Write-Host "Result $($count):"
            Write-Host "  DatabaseName: $($reader['new_dbnames'])"
            Write-Host "  InstanceName: $($reader['new_dbinstance'])"
            Write-Host "  IsActive: $($reader['cr032_isactive'])"
        }
    } else {
        Write-Host "Stored procedure returned NO RESULTS" -ForegroundColor Red
    }
    $reader.Close()

    $connection.Close()
    Write-Host "`nDebug complete" -ForegroundColor Green
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.Exception
}
