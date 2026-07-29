#!/usr/bin/env bash
set -euo pipefail

# Evita que Git Bash (MINGW) convierta el argumento --scope "/subscriptions/..." en una ruta Windows.
export MSYS_NO_PATHCONV=1

# Asigna el rol "Cognitive Services User" a tu usuario sobre el recurso de AI Services,
# necesario para crear y ejecutar los agentes en Foundry.

# 0. Asegurar que hay una suscripcion activa (evita el error MissingSubscription)
subId=$(az account show --query id -o tsv 2>/dev/null | tr -d '\r' || true)
if [ -z "$subId" ]; then
    echo "No hay suscripcion activa. Ejecuta: az login  y luego  az account set --subscription <ID>"
    az account list --query "[].{Nombre:name, Id:id, Estado:state}" -o table
    exit 1
fi

# 1. Object ID del usuario (Graph primero; si falla, decodifica el token de MS Graph)
objectId=$(az ad signed-in-user show --query id -o tsv 2>/dev/null | tr -d '\r' || true)
if [ -z "$objectId" ]; then
    objectId=$(az account get-access-token --resource-type ms-graph --query accessToken -o tsv | \
        python3 -c "import sys,base64,json; t=sys.stdin.read().strip(); p=t.split('.')[1]; p+='='*(-len(p)%4); print(json.loads(base64.b64decode(p))['oid'])" | tr -d '\r')
fi

# 2. Nombre del recurso AI Services creado por el despliegue
aisName=$(az cognitiveservices account list \
    --resource-group rg-contoso-retail \
    --query "[0].name" -o tsv | tr -d '\r')

# 3. Asignar el rol usando el Object ID (no requiere permisos de Graph API)
az role assignment create \
    --assignee-object-id "$objectId" \
    --assignee-principal-type User \
    --role "Cognitive Services User" \
    --scope "/subscriptions/$subId/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/$aisName"

echo "Rol asignado. Espera ~1 minuto a que se propague antes de ejecutar los agentes."
