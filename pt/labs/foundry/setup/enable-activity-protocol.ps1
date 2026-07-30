# ============================================================================
# Contoso Retail - Habilitar o Activity Protocol no agente Anders
# Workshop Multi-Agentico
# ============================================================================
# O Copilot Studio invoca os agentes do Foundry usando o Activity Protocol
# (o do Azure Bot Service). Um agente recem-criado expoe apenas o protocolo
# Responses API, portanto o Copilot Studio falha ao conecta-lo com:
#   HTTP 400 - Agent Anders endpoint does not support activity.
#
# Este script habilita o protocolo 'activity' no endpoint do agente por meio
# da REST API do Foundry. E o equivalente em codigo ao passo manual de publicar
# no M365/Teams pelo portal. Mantem 'responses' e o esquema 'Entra' (que o
# Copilot Studio usa) para nao quebrar a interacao por SDK/portal.
#
# API preview: usa api-version=v1 + header Foundry-Features.
#
# Uso:
#   ./enable-activity-protocol.ps1 -Suffix "sytao"
#   ./enable-activity-protocol.ps1 -ProjectEndpoint "https://ais-...azure.com/api/projects/aip-..." -AgentName "Anders"
# ============================================================================

param(
    [Parameter(Mandatory = $false, HelpMessage = "Sufixo de 5 caracteres atribuido ao attendee.")]
    [ValidatePattern('^[a-z0-9]{5}$')]
    [string]$Suffix,

    [Parameter(Mandatory = $false, HelpMessage = "Endpoint do projeto Foundry. Se fornecido, tem prioridade sobre -Suffix.")]
    [string]$ProjectEndpoint,

    [Parameter(Mandatory = $false, HelpMessage = "Nome do agente (default: Anders).")]
    [string]$AgentName = "Anders"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Workshop Multi-Agentico" -ForegroundColor Cyan
Write-Host " Habilitar Activity Protocol ($AgentName)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- 1. Resolver o endpoint do projeto ---
if ([string]::IsNullOrWhiteSpace($ProjectEndpoint)) {
    if ([string]::IsNullOrWhiteSpace($Suffix)) {
        Write-Error "Voce deve fornecer -Suffix ou -ProjectEndpoint."
        exit 1
    }
    $ProjectEndpoint = "https://ais-contosoretail-$Suffix.services.ai.azure.com/api/projects/aip-contosoretail-$Suffix"
}
$ProjectEndpoint = $ProjectEndpoint.TrimEnd('/')
Write-Host "  Endpoint: $ProjectEndpoint" -ForegroundColor Yellow
Write-Host "  Agente:   $AgentName" -ForegroundColor Yellow
Write-Host ""

# --- 2. Verificar sessao do Azure ---
Write-Host "[1/3] Verificando sessao do Azure..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  Nenhuma sessao ativa. Iniciando login..." -ForegroundColor Yellow
    az login | Out-Null
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Assinatura: $($account.name)" -ForegroundColor Gray

# --- 3. Obter token para o plano de dados do Foundry ---
Write-Host "[2/3] Obtendo token (audiencia https://ai.azure.com)..." -ForegroundColor Green
$token = az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error "Nao foi possivel obter o token de acesso."
    exit 1
}

# --- 4. Habilitar o protocolo 'activity' via PATCH ---
Write-Host "[3/3] Habilitando o protocolo 'activity' no agente..." -ForegroundColor Green
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
    Write-Host "  Activity Protocol habilitado em '$AgentName'." -ForegroundColor Green
    Write-Host "  Agora voce pode conectar o Anders pelo Copilot Studio." -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Warning "Nao foi possivel habilitar o protocolo pela API (lembre-se de que e preview)."
    Write-Warning "Detalhe: $($_.Exception.Message)"
    Write-Host "  Alternativa: publique o Anders no M365/Teams pelo portal do Foundry (ver lab03)." -ForegroundColor Yellow
    exit 1
}
