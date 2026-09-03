param(
    [string]$BaseUrl = "http://127.0.0.1:5050",
    [string]$TestUsername = "",
    [string]$TestPassword = "TestPassword123",
    [switch]$VerifyPersistence
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")
if ([string]::IsNullOrWhiteSpace($TestUsername)) {
    $TestUsername = "test_" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
}

$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.Timeout = [TimeSpan]::FromSeconds(10)

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
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $Token)
    }

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Compress
        $request.Content = [System.Net.Http.StringContent]::new(
            $json,
            [Text.Encoding]::UTF8,
            "application/json")
    }

    try {
        $response = $script:httpClient.Send($request)
        $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Body = $responseBody
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
    param(
        [object]$Response,
        [int]$Expected,
        [string]$Label
    )

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
    Assert-Status $register 201 "register"
    $registerBody = Read-JsonBody $register
    if ([string]::IsNullOrWhiteSpace($registerBody.accessToken)) {
        throw "register response did not contain an access token."
    }
    $token = $registerBody.accessToken

    Assert-Status (Invoke-ApiRequest "GET" "/api/v1/auth/me" $null $token) 200 "me with token"
    Assert-Status (Invoke-ApiRequest "GET" "/api/v1/auth/me") 401 "me without token"
    Assert-Status (Invoke-ApiRequest "GET" "/api/v1/auth/me" $null "not-a-valid-token") 401 "me with invalid token"

    $duplicate = Invoke-ApiRequest "POST" "/api/v1/auth/register" @{
        username = $TestUsername.ToUpperInvariant()
        password = $TestPassword
    }
    Assert-Status $duplicate 409 "case-insensitive duplicate username"

    $login = Invoke-ApiRequest "POST" "/api/v1/auth/login" @{
        username = $TestUsername.ToUpperInvariant()
        password = $TestPassword
    }
    Assert-Status $login 200 "login"
    $loginBody = Read-JsonBody $login
    if ([string]::IsNullOrWhiteSpace($loginBody.accessToken)) {
        throw "login response did not contain an access token."
    }

    $wrongPassword = Invoke-ApiRequest "POST" "/api/v1/auth/login" @{
        username = $TestUsername
        password = "WrongPassword123"
    }
    Assert-Status $wrongPassword 401 "wrong password"

    if ($VerifyPersistence) {
        & docker compose --env-file .env.local restart | Out-Null
        $healthyAfterRestart = $false
        for ($attempt = 1; $attempt -le 30; $attempt++) {
            try {
                $healthAfterRestart = Invoke-ApiRequest "GET" "/health"
                if ($healthAfterRestart.StatusCode -eq 200) {
                    $healthyAfterRestart = $true
                    break
                }
            }
            catch {
            }
            Start-Sleep -Seconds 1
        }

        if (-not $healthyAfterRestart) {
            throw "The API did not become healthy after the container restart."
        }

        $persistedLogin = Invoke-ApiRequest "POST" "/api/v1/auth/login" @{
            username = $TestUsername
            password = $TestPassword
        }
        Assert-Status $persistedLogin 200 "login after container restart"
        $persistedLoginBody = Read-JsonBody $persistedLogin
        if ([string]::IsNullOrWhiteSpace($persistedLoginBody.accessToken)) {
            throw "The account could not be used after the container restart."
        }
        Assert-Status (Invoke-ApiRequest "GET" "/api/v1/auth/me" $null $persistedLoginBody.accessToken) 200 "me after container restart"
    }

    $rateLimitHit = $false
    for ($index = 1; $index -le 12; $index++) {
        $limited = Invoke-ApiRequest "POST" "/api/v1/auth/login" @{
            username = "rate_limit_probe"
            password = $TestPassword
        }
        if ($limited.StatusCode -eq 429) {
            $rateLimitHit = $true
            break
        }
    }
    if (-not $rateLimitHit) {
        throw "login rate limit was not reached."
    }
    Write-Host "PASS login rate limit"
    Write-Host "Authentication smoke test completed without printing credentials or tokens."
}
finally {
    $httpClient.Dispose()
}
