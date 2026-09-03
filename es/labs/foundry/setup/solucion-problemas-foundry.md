# Limpieza y redespliegue de Microsoft Foundry

Usa esta guía cuando necesites eliminar completamente los recursos de Azure creados por el setup de Foundry y desplegarlos nuevamente desde cero.

> [!WARNING]
> Estas operaciones eliminan permanentemente los recursos y datos de Foundry del workshop. Verifica la suscripción activa y los nombres de los recursos antes de continuar.

## Por qué eliminar el Resource Group puede no ser suficiente

El despliegue crea sus recursos de Azure en `rg-contoso-retail` de forma predeterminada. Al eliminar este Resource Group se eliminan la Function App, Storage Account, App Service Plan, el proyecto de Foundry, el despliegue del modelo, las conexiones y las asignaciones de roles.

El recurso de Foundry, llamado `ais-contosoretail-<suffix>`, es un recurso de Azure AI Services. Azure lo conserva en estado de eliminación temporal (soft-delete) hasta por 48 horas. Mientras permanezca allí, su nombre no se puede reutilizar y un nuevo despliegue con el mismo sufijo puede fallar.

El sufijo se genera de forma determinista a partir del `TenantName` enviado al script de despliegue. Reutilizar ese valor genera el mismo sufijo y los mismos nombres de recursos.

## Requisitos previos

- Azure CLI instalado y actualizado.
- Una sesión activa en la suscripción de Azure correcta.
- Permisos para eliminar el Resource Group.
- Permisos para purgar recursos de Cognitive Services. Puede ser necesario tener el rol `Contributor` en el ámbito de la suscripción; el ámbito del Resource Group por sí solo no es suficiente para la operación de purga.

Confirma la suscripción activa:

```powershell
az account show --output table
```

Si es necesario, selecciona la suscripción correcta:

```powershell
az account set --subscription "<nombre-o-id-de-la-suscripcion>"
```

## Paso 1 - Guarda los datos del recurso de Foundry

Antes de eliminar el Resource Group, guarda el nombre, la ubicación y el Resource Group del recurso de Foundry:

```powershell
az cognitiveservices account list `
  --resource-group rg-contoso-retail `
  --query "[].{Name:name, Location:location, ResourceGroup:resourceGroup}" `
  --output table
```

De forma predeterminada, los valores esperados son:

- Resource Group: `rg-contoso-retail`
- Recurso de Foundry: `ais-contosoretail-<suffix>`
- Ubicación: `eastus`, salvo que se haya elegido otra durante el despliegue

## Paso 2 - Elimina el Resource Group

```powershell
az group delete `
  --name rg-contoso-retail `
  --yes
```

Espera a que termine la eliminación antes de buscar el recurso eliminado temporalmente.

## Paso 3 - Busca el recurso de Foundry eliminado temporalmente

```powershell
az cognitiveservices account list-deleted `
  --query "[].{Name:name, Location:location, ResourceGroup:resourceGroup}" `
  --output table
```

El recurso puede tardar algunos minutos en aparecer. Si no aparece de inmediato, espera brevemente y vuelve a ejecutar el comando.

## Paso 4 - Purga permanentemente el recurso de Foundry

Reemplaza `<suffix>` y la ubicación cuando sea necesario:

```powershell
az cognitiveservices account purge `
  --name ais-contosoretail-<suffix> `
  --resource-group rg-contoso-retail `
  --location eastus
```

El valor de `--resource-group` debe ser el nombre del Resource Group original, aunque ese grupo ya haya sido eliminado.

> [!CAUTION]
> La purga es permanente. Después de ejecutarla no se pueden recuperar el recurso, sus datos ni sus claves.

## Paso 5 - Verifica la purga

```powershell
az cognitiveservices account list-deleted `
  --query "[?name=='ais-contosoretail-<suffix>']" `
  --output table
```

Si no aparece ningún resultado, el recurso eliminado ya no está en la lista. Azure todavía puede tardar unos minutos en liberar el nombre antes de permitir un nuevo despliegue.

Ahora puedes volver a ejecutar el script de despliegue de Foundry con el mismo `TenantName` y nombre de Resource Group.

## Errores comunes

### No se encuentra el recurso eliminado

Es posible que la eliminación aún se esté propagando. Espera unos minutos, vuelve a ejecutar `az cognitiveservices account list-deleted` y usa exactamente el nombre, el Resource Group original y la ubicación que aparecen en el resultado.

### Error de autorización durante la purga

Verifica que tu cuenta tenga el permiso de purga requerido en el ámbito de la suscripción. El rol `Contributor` asignado únicamente al Resource Group eliminado no es suficiente.

### El redespliegue indica que el nombre ya existe

Confirma que el recurso de Foundry ya no aparezca en `list-deleted`. Si acabas de completar la purga, espera unos minutos antes de intentar nuevamente el despliegue.