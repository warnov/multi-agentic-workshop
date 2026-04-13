# ============================================================================
# Contoso Retail - Script de Implantação para Azure Cloud Shell
# Workshop Multi-Agente  |  Plano: Flex Consumption (FC1 / Linux)
# ============================================================================
#
# PRÉ-REQUISITOS no Cloud Shell:
#   1. Clone o repositório (somente na primeira vez):
#        git clone https://github.com/<org>/taller-multi-agentic.git
#   2. Execute este script:
#        cd taller-multi-agentic/pt/labs/foundry/setup/op-flex
#        pwsh ./deployFromAzure.ps1
#
# Uso:
#   ./deployFromAzure.ps1
#   ./deployFromAzure.ps1 -FabricWarehouseSqlEndpoint "<endpoint>" -FabricWarehouseDatabase "<db>"
#   ./deployFromAzure.ps1 -Location "eastus" -FabricWarehouseSqlEndpoint "<endpoint>" -FabricWarehouseDatabase "<db>"
# TenantName é opcional: exibido apenas na tela, não afeta os recursos criados.
# ============================================================================

param(
    [Parameter(Mandatory = $false, HelpMessage = "Rótulo descritivo opcional (ex: número de tenant do participante). Exibido apenas na tela.")]
    [string]$TenantName = "",

    [Parameter(Mandatory = $false, HelpMessage = "Região do Azure (padrão: eastus).")]
    [string]$Location = "eastus",

    [Parameter(Mandatory = $false, HelpMessage = "Nome do Resource Group (padrão: rg-contoso-retail).")]
    [string]$ResourceGroupName = "rg-contoso-retail",

    [Parameter(Mandatory = $false, HelpMessage = "Endpoint SQL do Warehouse do Fabric (sem protocolo). Ex: xyz.datawarehouse.fabric.microsoft.com")]
    [string]$FabricWarehouseSqlEndpoint = "",

    [Parameter(Mandatory = $false, HelpMessage = "Nome do banco de dados do Warehouse do Fabric.")]
    [string]$FabricWarehouseDatabase = ""
)

$ErrorActionPreference = "Stop"

Write-Host "Pressione Enter para o padrão." -ForegroundColor DarkGray

if (-not $PSBoundParameters.ContainsKey('Location')) {
    $locationInput = Read-Host "Location [$Location]"
    if (-not [string]::IsNullOrWhiteSpace($locationInput)) {
        $Location = $locationInput.Trim()
    }
}

if (-not $PSBoundParameters.ContainsKey('ResourceGroupName')) {
    $resourceGroupInput = Read-Host "ResourceGroupName [$ResourceGroupName]"
    if (-not [string]::IsNullOrWhiteSpace($resourceGroupInput)) {
        $ResourceGroupName = $resourceGroupInput.Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $configureFabricNow = Read-Host "Deseja configurar a conexão SQL do Fabric para o Lab04 agora? (s/N)"
    if ($configureFabricNow -match '^(s|sim|y|yes)$') {
        $FabricWarehouseSqlEndpoint = (Read-Host "FabricWarehouseSqlEndpoint (sem protocolo, sem porta)").Trim()
        $FabricWarehouseDatabase = (Read-Host "FabricWarehouseDatabase").Trim()
    }
}

if (-not [string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $FabricWarehouseDatabase = (Read-Host "Falta FabricWarehouseDatabase. Informe o valor ou deixe vazio para ignorar").Trim()
}

if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and -not [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $FabricWarehouseSqlEndpoint = (Read-Host "Falta FabricWarehouseSqlEndpoint. Informe o valor ou deixe vazio para ignorar").Trim()
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Workshop Multi-Agente - Implantação" -ForegroundColor Cyan
Write-Host " Plano: Flex Consumption (FC1 / Linux)" -ForegroundColor Cyan
Write-Host " Modo: Azure Cloud Shell" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tenant:         $TenantName" -ForegroundColor Yellow
Write-Host "  Location:       $Location" -ForegroundColor Yellow
Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor Yellow
Write-Host "  Fabric SQL:     $(if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint)) { '<omitido>' } else { $FabricWarehouseSqlEndpoint })" -ForegroundColor Yellow
Write-Host "  Fabric DB:      $(if ([string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) { '<omitido>' } else { $FabricWarehouseDatabase })" -ForegroundColor Yellow
Write-Host ""

$hasFabricSql = -not [string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint)
$hasFabricDb = -not [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)
$hasCompleteFabricConfig = $hasFabricSql -and $hasFabricDb
$FabricWarehouseConnectionString = ""

if ($hasFabricSql -xor $hasFabricDb) {
    Write-Warning "Apenas um dos parâmetros do Fabric foi fornecido. Ambos serão ignorados para não configurar uma conexão incompleta."
    $FabricWarehouseSqlEndpoint = ""
    $FabricWarehouseDatabase = ""
    $hasCompleteFabricConfig = $false
}

if (-not $hasCompleteFabricConfig) {
    Write-Warning "A conexão SQL para o Lab04 não será configurada nesta implantação. Você precisará ajustá-la manualmente depois."
}

# --- 1. Verificar sessão ativa ---
# No Cloud Shell a sessão costuma estar ativa, mas verificamos por precaução.
Write-Host "[1/5] Verificando sessão do Azure..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  Nenhuma sessão ativa. Iniciando login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Assinatura: $($account.name) ($($account.id))" -ForegroundColor Gray
Write-Host "  Registrando provider Microsoft.Bing (se aplicável)..." -ForegroundColor Gray
az provider register --namespace Microsoft.Bing --output none 2>$null

# --- 2. Criar Resource Group ---
Write-Host "[2/5] Criando Resource Group '$ResourceGroupName'..." -ForegroundColor Green
az group create --name $ResourceGroupName --location $Location --output none
Write-Host "  Resource Group pronto." -ForegroundColor Gray

# Sufixo para tentar localizar uma Function App já existente antes do deploy.
# NOTA: Bicep deriva o sufixo real via uniqueString(subscriptionId), que produz um valor diferente.
$suffixForNames = $account.id.Replace('-', '').Substring(0, 5).ToLower()

if (-not $hasCompleteFabricConfig -and -not [string]::IsNullOrWhiteSpace($suffixForNames)) {
    $existingFunctionAppName = "func-contosoretail-$suffixForNames"
    $existingConnection = az functionapp config appsettings list `
        --resource-group $ResourceGroupName `
        --name $existingFunctionAppName `
        --query "[?name=='FabricWarehouseConnectionString'].value | [0]" `
        --output tsv 2>$null

    if (-not [string]::IsNullOrWhiteSpace($existingConnection) -and $existingConnection -ne "null") {
        $FabricWarehouseConnectionString = $existingConnection
        Write-Host "  FabricWarehouseConnectionString existente na Function App será preservada." -ForegroundColor Yellow
    }
}

# --- 3. Implantar Bicep ---
Write-Host "[3/5] Implantando infraestrutura..." -ForegroundColor Green

Write-Host "" -ForegroundColor Gray
Write-Host "  Isso pode levar ~5 minutos." -ForegroundColor Yellow
Write-Host ""

$scriptDir = $PSScriptRoot
$templateFile = Join-Path $scriptDir "main.bicep"
$deploymentName = "main"

if (-not (Test-Path $templateFile)) {
    Write-Error "main.bicep não encontrado em '$scriptDir'. Certifique-se de executar o script a partir da pasta op-flex do repositório clonado."
    exit 1
}

# Iniciar implantação em background (--no-wait)
az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file $templateFile `
    --parameters location=$Location tenantName=$TenantName fabricWarehouseSqlEndpoint=$FabricWarehouseSqlEndpoint fabricWarehouseDatabase=$FabricWarehouseDatabase fabricWarehouseConnectionString="$FabricWarehouseConnectionString" `
    --name $deploymentName `
    --no-wait `
    --output none

if ($LASTEXITCODE -ne 0) {
    Write-Error "Não foi possível iniciar a implantação. Verifique se não há recursos soft-deleted (az cognitiveservices account list-deleted)."
    exit 1
}

# Aguardar o deployment aparecer no ARM
$retries = 0
do {
    Start-Sleep -Seconds 3
    $retries++
    $depState = az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.provisioningState' `
        --output tsv 2>$null
} while (-not $depState -and $retries -lt 10)

if (-not $depState) {
    Write-Error "O deployment '$deploymentName' não foi registrado no Azure. Verifique erros de validação."
    exit 1
}

# Acompanhamento recurso a recurso
$completedOps = @{}
$deployFailed = $false

while ($true) {
    Start-Sleep -Seconds 3

    $opsJson = az deployment operation group list `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --output json 2>$null

    if (-not $opsJson) { continue }
    $ops = $opsJson | ConvertFrom-Json

    foreach ($op in $ops) {
        $resType = $op.properties.targetResource.resourceType
        $resName = $op.properties.targetResource.resourceName
        $status  = $op.properties.provisioningState

        if (-not $resType -or -not $resName) { continue }

        $key = "$resType/$resName"
        $prevStatus = $completedOps[$key]
        if ($prevStatus -ne $status) {
            $completedOps[$key] = $status
            $shortType = $resType -replace '^Microsoft\.', '' -replace '/providers/.*', ''
            switch ($status) {
                'Running'   { Write-Host "  ⏳ $shortType/$resName ..." -ForegroundColor Gray }
                'Succeeded' { Write-Host "  ✅ $shortType/$resName" -ForegroundColor Green }
                'Failed'    { Write-Host "  ❌ $shortType/$resName" -ForegroundColor Red; $deployFailed = $true }
            }
        }
    }

    $depJson = az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.provisioningState' `
        --output tsv 2>$null

    if ($depJson -eq 'Succeeded' -or $depJson -eq 'Failed' -or $depJson -eq 'Canceled') {
        break
    }
}

if ($depJson -ne 'Succeeded') {
    Write-Host ""
    az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.error' `
        --output json
    Write-Error "A implantação falhou. Revise os erros acima."
    exit 1
}

# Obter outputs da implantação bem-sucedida
$result = az deployment group show `
    --resource-group $ResourceGroupName `
    --name $deploymentName `
    --output json | ConvertFrom-Json

$outputs = $result.properties.outputs
$functionAppName = $outputs.functionAppName.value

# --- 4. Compilar e publicar código da Function App ---
Write-Host "[4/5] Compilando FxContosoRetail..." -ForegroundColor Green

$projectDir = Join-Path $scriptDir "../../code/api/FxContosoRetail"
$projectDir = (Resolve-Path $projectDir).Path
$publishDir = Join-Path $projectDir "bin/publish"

$publishOutput = dotnet publish $projectDir --configuration Release --output $publishDir 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "===== Detalhe do erro de compilação =====" -ForegroundColor Red
    $publishOutput | ForEach-Object { Write-Host $_ }
    Write-Host "=========================================" -ForegroundColor Red
    Write-Error "Erro ao compilar o projeto. Verifique o código."
    exit 1
}

# Criar zip para implantação
# FIX: Compress-Archive no PowerShell 7 no Linux exclui diretórios ocultos (dotfiles)
# como .azurefunctions, que o Flex Consumption valida como obrigatório.
# Usa-se o comando zip do sistema, que com '.' inclui todos os arquivos sem exceção.
$zipPath = "/tmp/fxcontosoretail-publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Push-Location $publishDir
& zip -r $zipPath . | Out-Null
Pop-Location

# Aguardar o endpoint SCM estar disponível
# Nota: no Cloud Shell (Linux) usa-se [System.Net.Dns] pois Resolve-DnsName não está disponível.
$scmHost = "$functionAppName.scm.azurewebsites.net"
Write-Host "[5/5] Aguardando o endpoint SCM ficar disponível..." -ForegroundColor Green
$dnsReady = $false
for ($i = 0; $i -lt 30; $i++) {
    try {
        [System.Net.Dns]::GetHostAddresses($scmHost) | Out-Null
        $dnsReady = $true
        break
    } catch {
        Start-Sleep -Seconds 10
    }
}
if (-not $dnsReady) {
    Write-Warning "  O DNS de $scmHost não resolveu após 5 minutos. Tentando deploy mesmo assim..."
}

# Deploy com tentativas
$maxRetries = 3
$deploySuccess = $false
for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
    Write-Host "  Implantando em $functionAppName (tentativa $attempt/$maxRetries)..." -ForegroundColor Gray
    az functionapp deployment source config-zip `
        --resource-group $ResourceGroupName `
        --name $functionAppName `
        --src $zipPath `
        --timeout 600 `
        --output none 2>&1 | Out-Null

    if ($LASTEXITCODE -eq 0) {
        $deploySuccess = $true
        break
    }

    if ($attempt -lt $maxRetries) {
        $waitSecs = $attempt * 30
        Write-Host "  ⚠️  Tentativa $attempt falhou. Tentando novamente em $waitSecs segundos..." -ForegroundColor Yellow
        Start-Sleep -Seconds $waitSecs
    }
}

if (-not $deploySuccess) {
    Write-Error "Erro ao publicar o código após $maxRetries tentativas. Você pode tentar manualmente com: az functionapp deployment source config-zip --resource-group $ResourceGroupName --name $functionAppName --src '$zipPath'"
    exit 1
}

# Limpar arquivos temporários
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "  ✅ Código publicado com sucesso." -ForegroundColor Green

# --- Resumo final ---
$functionAppUrl = $outputs.functionAppUrl.value
$apiUrl = "$functionAppUrl/api/OrdersReporter"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Implantação concluída!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Sufixo único:                $($outputs.suffix.value)" -ForegroundColor White
Write-Host "  Storage Account:             $($outputs.storageAccountName.value)" -ForegroundColor White
Write-Host "  Function App:                $functionAppName" -ForegroundColor White
Write-Host "  Function App Base URL:       $functionAppUrl/api" -ForegroundColor White
Write-Host "  API OrdersReporter:          $apiUrl" -ForegroundColor White
Write-Host "  Foundry Project Endpoint:    $($outputs.foundryProjectEndpoint.value)" -ForegroundColor White
Write-Host "  Bing Grounding Resource:     $($outputs.bingGroundingName.value)" -ForegroundColor White
Write-Host "  Bing Connection Name:        $($outputs.bingConnectionName.value)" -ForegroundColor White
Write-Host "  Bing Connection Name (Julie): $($outputs.bingConnectionName.value)" -ForegroundColor White
if ($hasCompleteFabricConfig) {
    Write-Host "  Fabric SQL Connection:       atualizada a partir dos parâmetros" -ForegroundColor White
}
elseif (-not [string]::IsNullOrWhiteSpace($FabricWarehouseConnectionString)) {
    Write-Host "  Fabric SQL Connection:       preservada da configuração existente" -ForegroundColor White
}
else {
    Write-Host "  Fabric SQL Connection:       não configurada" -ForegroundColor Yellow
}
if (-not $hasCompleteFabricConfig -and [string]::IsNullOrWhiteSpace($FabricWarehouseConnectionString)) {
    Write-Host "  Aviso Lab04:                 A conexão SQL (FabricWarehouseConnectionString) não foi configurada. Configure-a manualmente na Function App." -ForegroundColor Yellow
}
Write-Host ""
