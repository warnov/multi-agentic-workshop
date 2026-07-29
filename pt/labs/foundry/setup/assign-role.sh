#!/usr/bin/env bash
set -euo pipefail

# Atribui a funcao "Cognitive Services User" ao seu usuario no recurso de AI Services,
# necessaria para criar e executar os agentes no Foundry.

# 0. Garantir que ha uma assinatura ativa (evita o erro MissingSubscription)
subId=$(az account show --query id -o tsv 2>/dev/null || true)
if [ -z "$subId" ]; then
    echo "Nenhuma assinatura ativa. Execute: az login  e depois  az account set --subscription <ID>"
    az account list --query "[].{Nome:name, Id:id, Estado:state}" -o table
    exit 1
fi

# 1. Object ID do usuario (Graph primeiro; se falhar, decodifica o token de MS Graph)
objectId=$(az ad signed-in-user show --query id -o tsv 2>/dev/null || true)
if [ -z "$objectId" ]; then
    objectId=$(az account get-access-token --resource-type ms-graph --query accessToken -o tsv | \
        python3 -c "import sys,base64,json; t=sys.stdin.read().strip(); p=t.split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.b64decode(p))['oid'])")
fi

# 2. Nome do recurso AI Services criado pela implantacao
aisName=$(az cognitiveservices account list \
    --resource-group rg-contoso-retail \
    --query "[0].name" -o tsv)

# 3. Atribuir a funcao usando o Object ID (nao requer permissoes de Graph API)
az role assignment create \
    --assignee-object-id "$objectId" \
    --assignee-principal-type User \
    --role "Cognitive Services User" \
    --scope "/subscriptions/$subId/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/$aisName"

echo "Funcao atribuida. Aguarde ~1 minuto para propagar antes de executar os agentes."
