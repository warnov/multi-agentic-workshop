param(
    [string]$FoundryProjectEndpoint = $env:FOUNDRY_PROJECT_ENDPOINT,
    [string]$FoundryModelDeploymentName = $env:FOUNDRY_MODEL_DEPLOYMENT_NAME,
    [string]$FoundryAgentName = $(if ($env:FOUNDRY_AGENT_NAME) { $env:FOUNDRY_AGENT_NAME } else { "AndersAgent" }),
    [string]$FoundryAgentInstructions = $(if ($env:FOUNDRY_AGENT_INSTRUCTIONS) { $env:FOUNDRY_AGENT_INSTRUCTIONS } else { "You are an analytical AI agent specialized in reading, understanding, and extracting insights from provided information." }),
    [string]$ProjectFile = "./es/labs/foundry/code/agents/AndersAgent/ms-foundry/ms_foundry_agent.csproj"
)

$ErrorActionPreference = "Stop"

function Require-Command {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Missing required command: $Name"
    }
}

function Require-Value {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Missing required value: $Name"
    }
}

Require-Command -Name "az"
Require-Command -Name "dotnet"

Require-Value -Name "FoundryProjectEndpoint (or FOUNDRY_PROJECT_ENDPOINT)" -Value $FoundryProjectEndpoint
Require-Value -Name "FoundryModelDeploymentName (or FOUNDRY_MODEL_DEPLOYMENT_NAME)" -Value $FoundryModelDeploymentName

$resolvedProjectFile = Resolve-Path -Path $ProjectFile -ErrorAction Stop

try {
    az account show | Out-Null
}
catch {
    Write-Host "Azure CLI is not logged in. Running az login..."
    az login | Out-Null
}

$env:Foundry__ProjectEndpoint = $FoundryProjectEndpoint
$env:Foundry__ModelDeployment = $FoundryModelDeploymentName
$env:Foundry__AgentName = $FoundryAgentName
$env:Foundry__AgentInstructions = $FoundryAgentInstructions

Write-Host "Deploying agent '$FoundryAgentName' to Azure AI Foundry..."
dotnet run --project $resolvedProjectFile -- deploy
