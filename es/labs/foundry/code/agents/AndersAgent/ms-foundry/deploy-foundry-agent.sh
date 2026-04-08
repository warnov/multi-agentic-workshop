#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="$SCRIPT_DIR/ms_foundry_agent.csproj"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require_value() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "Missing required environment variable: $name" >&2
    exit 1
  fi
}

require_command az
require_command dotnet

require_value FOUNDRY_PROJECT_ENDPOINT
require_value FOUNDRY_MODEL_DEPLOYMENT_NAME

if ! az account show >/dev/null 2>&1; then
  echo "Azure CLI is not logged in. Running az login..."
  az login >/dev/null
fi

export Foundry__ProjectEndpoint="$FOUNDRY_PROJECT_ENDPOINT"
export Foundry__ModelDeployment="$FOUNDRY_MODEL_DEPLOYMENT_NAME"
export Foundry__AgentName="${FOUNDRY_AGENT_NAME:-AndersAgent}"
export Foundry__AgentInstructions="${FOUNDRY_AGENT_INSTRUCTIONS:-You are an analytical AI agent specialized in reading, understanding, and extracting insights from provided information.}"

echo "Deploying agent '$Foundry__AgentName' to Azure AI Foundry..."
dotnet run --project "$PROJECT_FILE" -- deploy
