#!/usr/bin/env bash
set -euo pipefail

# Prevents Git Bash (MINGW) from converting the --scope "/subscriptions/..." argument into a Windows path.
export MSYS_NO_PATHCONV=1

# Assigns the "Cognitive Services User" role to your user on the AI Services resource,
# required to create and run the agents in Foundry.

# 0. Ensure there is an active subscription (avoids the MissingSubscription error)
subId=$(az account show --query id -o tsv 2>/dev/null | tr -d '\r' || true)
if [ -z "$subId" ]; then
    echo "No active subscription. Run: az login  and then  az account set --subscription <ID>"
    az account list --query "[].{Name:name, Id:id, State:state}" -o table
    exit 1
fi

# 1. Object ID of the user (Graph first; if it fails, decode the MS Graph token)
objectId=$(az ad signed-in-user show --query id -o tsv 2>/dev/null | tr -d '\r' || true)
if [ -z "$objectId" ]; then
    objectId=$(az account get-access-token --resource-type ms-graph --query accessToken -o tsv | \
        python3 -c "import sys,base64,json; t=sys.stdin.read().strip(); p=t.split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.b64decode(p))['oid'])" | tr -d '\r')
fi

# 2. Name of the AI Services resource created by the deployment
aisName=$(az cognitiveservices account list \
    --resource-group rg-contoso-retail \
    --query "[0].name" -o tsv | tr -d '\r')

# 3. Assign the role using the Object ID (does not require Graph API permissions)
az role assignment create \
    --assignee-object-id "$objectId" \
    --assignee-principal-type User \
    --role "Cognitive Services User" \
    --scope "/subscriptions/$subId/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/$aisName"

echo "Role assigned. Wait ~1 minute for it to propagate before running the agents."
