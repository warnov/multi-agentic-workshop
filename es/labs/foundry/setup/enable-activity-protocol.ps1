# ============================================================================
# Contoso Retail - Habilitar Activity Protocol en el agente Anders
# Taller Multi-Agentico
# ============================================================================
# Copilot Studio invoca a los agentes de Foundry usando el Activity Protocol
# (el de Azure Bot Service). Un agente recien creado solo expone el protocolo
# Responses API, por lo que Copilot Studio falla al conectarlo con:
#   HTTP 400 - Agent Anders endpoint does not support activity.
#
# Este script habilita el protocolo 'activity' en el endpoint del agente
# mediante la REST API de Foundry. Es el equivalente por codigo al paso manual
# de publicar a M365/Teams desde el portal. Mantiene 'responses' y el esquema
# 'Entra' (que Copilot Studio usa) para no romper la interaccion por SDK/portal.
#
# API preview: usa api-version=v1 + header Foundry-Features.
#
# Uso:
#   ./enable-activity-protocol.ps1 -Suffix "sytao"
#   ./enable-activity-protocol.ps1 -ProjectEndpoint "https://ais-...azure.com/api/projects/aip-..." -AgentName "Anders"
# ============================================================================

param(
    [Parameter(Mandatory = $false, HelpMessage = "Sufijo de 5 caracteres asignado al attendee.")]
    [ValidatePattern('^[a-z0-9]{5}$')]
    [string]$Suffix,

    [Parameter(Mandatory = $false, HelpMessage = "Endpoint del proyecto Foundry. Si se provee, tiene prioridad sobre -Suffix.")]
    [string]$ProjectEndpoint,

    [Parameter(Mandatory = $false, HelpMessage = "Nombre del agente (default: Anders).")]
    [string]$AgentName = "Anders"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Taller Multi-Agentico" -ForegroundColor Cyan
Write-Host " Habilitar Activity Protocol ($AgentName)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- 1. Resolver el endpoint del proyecto ---
if ([string]::IsNullOrWhiteSpace($ProjectEndpoint)) {
    if ([string]::IsNullOrWhiteSpace($Suffix)) {
        Write-Error "Debes proporcionar -Suffix o -ProjectEndpoint."
        exit 1
    }
    $ProjectEndpoint = "https://ais-contosoretail-$Suffix.services.ai.azure.com/api/projects/aip-contosoretail-$Suffix"
}
$ProjectEndpoint = $ProjectEndpoint.TrimEnd('/')
Write-Host "  Endpoint: $ProjectEndpoint" -ForegroundColor Yellow
Write-Host "  Agente:   $AgentName" -ForegroundColor Yellow
Write-Host ""

# --- 2. Verificar sesion de Azure ---
Write-Host "[1/3] Verificando sesion de Azure..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  No hay sesion activa. Iniciando login..." -ForegroundColor Yellow
    az login | Out-Null
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Suscripcion: $($account.name)" -ForegroundColor Gray

# --- 3. Obtener token para el plano de datos de Foundry ---
Write-Host "[2/3] Obteniendo token (audiencia https://ai.azure.com)..." -ForegroundColor Green
$token = az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error "No se pudo obtener el token de acceso."
    exit 1
}

# --- 4. Habilitar el protocolo 'activity' via PATCH ---
Write-Host "[3/3] Habilitando el protocolo 'activity' en el agente..." -ForegroundColor Green
$uri = "$ProjectEndpoint/agents/${AgentName}?api-version=v1"
$headers = @{
    Authorization      = "Bearer $token"
    "Foundry-Features" = "AgentEndpoints=V1Preview"
}
$body = @{
    agent_endpoint = @{
        protocol_configuration = @{
            responses = @{}
            activity  = @{}
        }
        authorization_schemes = @(
            @{ type = "Entra" },
            @{ type = "BotServiceRbac" }
        )
    }
} | ConvertTo-Json -Depth 6

try {
    Invoke-RestMethod -Method Patch -Uri $uri -Headers $headers `
        -ContentType "application/merge-patch+json" -Body $body | Out-Null
    Write-Host ""
    Write-Host "  Activity Protocol habilitado en '$AgentName'." -ForegroundColor Green
    Write-Host "  Ya puedes conectar Anders desde Copilot Studio." -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Warning "No se pudo habilitar el protocolo por API (recuerda que es preview)."
    Write-Warning "Detalle: $($_.Exception.Message)"
    Write-Host "  Alternativa: publica Anders a M365/Teams desde el portal de Foundry (ver lab03)." -ForegroundColor Yellow
    exit 1
}
