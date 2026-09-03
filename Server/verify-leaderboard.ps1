param(
    [string]$BaseUrl = "http://127.0.0.1:5050",
    [string]$TestUsername = "",
    [string]$TestPassword = "TestPassword123",
    [switch]$VerifyPersistence
)

$ErrorActionPreference = "Stop"
$script:BaseUrl = $BaseUrl.TrimEnd("/")
if ([string]::IsNullOrWhiteSpace($TestUsername)) {
    $TestUsername = "board_" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
}
if ($VerifyPersistence -and $script:BaseUrl -ne "http://127.0.0.1:5050") {
    throw "-VerifyPersistence is intended for the local Compose endpoint only."
}

$script:httpClient = [System.Net.Http.HttpClient]::new()
$script:httpClient.Timeout = [TimeSpan]::FromSeconds(15)

function Invoke-ApiRequest {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$Token = ""
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        $script:BaseUrl + $Path)
    $request.Headers.Accept.ParseAdd("application/json")
    if (-not [string]::IsNullOrEmpty($Token)) {
        $request.Headers.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $Token)
    }

    if ($null -ne $Body) {
        $request.Content = [System.Net.Http.StringContent]::new(
            ($Body | ConvertTo-Json -Compress),
            [Text.Encoding]::UTF8,
            "application/json")
    }

    try {
        $response = $script:httpClient.Send($request)
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        }
    }
    finally {
        $request.Dispose()
    }
}

function Read-JsonBody {
    param([object]$Response)
    if ([string]::IsNullOrWhiteSpace($Response.Body)) {
        return $null
    }
    return $Response.Body | ConvertFrom-Json
}

function Assert-Status {
    param([object]$Response, [int]$Expected, [string]$Label)
    if ($Response.StatusCode -ne $Expected) {
        throw "$Label expected HTTP $Expected but received HTTP $($Response.StatusCode)."
    }
    Write-Host "PASS $Label"
}

try {
    Assert-Status (Invoke-ApiRequest "GET" "/health") 200 "health"

    $register = Invoke-ApiRequest "POST" "/api/v1/auth/register" @{
        username = $TestUsername
        password = $TestPassword
    }
    Assert-Status $register 201 "register leaderboard probe account"
    $registerBody = Read-JsonBody $register
    $token = $registerBody.accessToken
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "register response did not contain an access token."
    }

    $red = Invoke-ApiRequest "POST" "/api/v1/ninjas" @{ name = "Red" } $token
    Assert-Status $red 201 "create first Ninja"
    $redBody = Read-JsonBody $red
    $redId = $redBody.id

    $blue = Invoke-ApiRequest "POST" "/api/v1/ninjas" @{ name = "Blue" } $token
    Assert-Status $blue 201 "create second Ninja"
    $blueBody = Read-JsonBody $blue
    $blueId = $blueBody.id

    $firstScore = Invoke-ApiRequest "PUT" "/api/v1/ninjas/$redId/best-score" @{ bestScore = 12 } $token
    Assert-Status $firstScore 200 "submit first Ninja score"
    $secondScore = Invoke-ApiRequest "PUT" "/api/v1/ninjas/$blueId/best-score" @{ bestScore = 20 } $token
    Assert-Status $secondScore 200 "submit account best score"
    $secondScoreBody = Read-JsonBody $secondScore
    if (-not $secondScoreBody.accountImproved -or $secondScoreBody.accountRank -lt 1) {
        throw "account aggregate did not report the expected improvement and rank."
    }

    $ninjas = Invoke-ApiRequest "GET" "/api/v1/ninjas" $null $token
    Assert-Status $ninjas 200 "list cloud Ninjas"
    $ninjasBody = Read-JsonBody $ninjas
    if ($ninjasBody.ninjas.Count -ne 2 -or $ninjasBody.accountBest.bestScore -ne 20) {
        throw "cloud Ninja list did not preserve two profiles and account best 20."
    }
    Write-Host "PASS account-scoped Ninja snapshot"

    $board = Invoke-ApiRequest "GET" "/api/v1/leaderboard?limit=100" $null $token
    Assert-Status $board 200 "read online leaderboard"
    $boardBody = Read-JsonBody $board
    $currentRows = @($boardBody.entries | Where-Object { $_.isCurrentUser })
    if ($currentRows.Count -ne 1 -or $currentRows[0].bestScore -ne 20) {
        throw "leaderboard did not aggregate this account into exactly one row."
    }
    Write-Host "PASS one-row account aggregation"

    $targets = Invoke-ApiRequest "GET" "/api/v1/leaderboard/targets?fromScore=21&limit=20" $null $token
    Assert-Status $targets 200 "read online targets"
    $targetsBody = Read-JsonBody $targets
    if ($null -eq $targetsBody.targets) {
        throw "targets response did not contain a targets array."
    }
    Write-Host "PASS target milestones response"

    $lower = Invoke-ApiRequest "PUT" "/api/v1/ninjas/$blueId/best-score" @{ bestScore = 3 } $token
    Assert-Status $lower 200 "idempotent lower score submission"
    $lowerBody = Read-JsonBody $lower
    if ($lowerBody.ninja.bestScore -ne 20 -or $lowerBody.ninjaImproved) {
        throw "a lower score changed the canonical best."
    }
    Write-Host "PASS monotonic score update"

    if ($VerifyPersistence) {
        & docker compose --env-file .env.local restart | Out-Null
        $healthy = $false
        for ($attempt = 1; $attempt -le 30; $attempt++) {
            try {
                if ((Invoke-ApiRequest "GET" "/health").StatusCode -eq 200) {
                    $healthy = $true
                    break
                }
            }
            catch {
            }
            Start-Sleep -Seconds 1
        }
        if (-not $healthy) {
            throw "The API did not become healthy after the container restart."
        }

        $login = Invoke-ApiRequest "POST" "/api/v1/auth/login" @{
            username = $TestUsername
            password = $TestPassword
        }
        Assert-Status $login 200 "login after container restart"
        $loginBody = Read-JsonBody $login
        $persisted = Invoke-ApiRequest "GET" "/api/v1/ninjas" $null $loginBody.accessToken
        Assert-Status $persisted 200 "Ninja snapshot after container restart"
        $persistedBody = Read-JsonBody $persisted
        if ($persistedBody.accountBest.bestScore -ne 20) {
            throw "account best did not survive the container restart."
        }
        Write-Host "PASS leaderboard persistence"
    }

    Write-Host "Leaderboard smoke test completed without printing credentials or tokens."
}
finally {
    $script:httpClient.Dispose()
}
