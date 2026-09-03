# Microsoft Foundry cleanup and redeployment

Use this guide when you need to completely remove the Azure resources created by the Foundry setup and deploy them again from scratch.

> [!WARNING]
> These operations permanently delete the workshop's Foundry resources and data. Verify the active subscription and resource names before continuing.

## Why deleting the Resource Group may not be enough

The deployment creates its Azure resources in `rg-contoso-retail` by default. Deleting this Resource Group removes the Function App, Storage Account, App Service Plan, Foundry project, model deployment, connections, and role assignments.

The Foundry resource itself, named `ais-contosoretail-<suffix>`, is an Azure AI Services resource. Azure keeps it in a soft-deleted state for up to 48 hours. While it remains there, its name cannot be reused, and redeployment with the same suffix may fail.

The suffix is generated deterministically from the `TenantName` passed to the deployment script. Reusing that value generates the same suffix and resource names.

## Prerequisites

- Azure CLI installed and updated.
- An active session in the correct Azure subscription.
- Permission to delete the Resource Group.
- Permission to purge Cognitive Services resources. A `Contributor` assignment may need to exist at subscription scope; Resource Group scope alone is not sufficient for the purge operation.

Confirm the active subscription:

```powershell
az account show --output table
```

If necessary, select the correct subscription:

```powershell
az account set --subscription "<subscription-name-or-id>"
```

## Step 1 - Record the Foundry resource details

Before deleting the Resource Group, record the Foundry resource name, location, and Resource Group:

```powershell
az cognitiveservices account list `
  --resource-group rg-contoso-retail `
  --query "[].{Name:name, Location:location, ResourceGroup:resourceGroup}" `
  --output table
```

By default, the expected values are:

- Resource Group: `rg-contoso-retail`
- Foundry resource: `ais-contosoretail-<suffix>`
- Location: `eastus`, unless another location was selected during deployment

## Step 2 - Delete the Resource Group

```powershell
az group delete `
  --name rg-contoso-retail `
  --yes
```

Wait for the deletion to finish before checking for the soft-deleted resource.

## Step 3 - Find the soft-deleted Foundry resource

```powershell
az cognitiveservices account list-deleted `
  --query "[].{Name:name, Location:location, ResourceGroup:resourceGroup}" `
  --output table
```

It may take a few minutes for the resource to appear. If it is not listed immediately, wait briefly and run the command again.

## Step 4 - Permanently purge the Foundry resource

Replace `<suffix>` and the location if required:

```powershell
az cognitiveservices account purge `
  --name ais-contosoretail-<suffix> `
  --resource-group rg-contoso-retail `
  --location eastus
```

The `--resource-group` value must be the original Resource Group name, even though that group has already been deleted.

> [!CAUTION]
> Purging is permanent. The resource, its data, and its keys cannot be recovered afterward.

## Step 5 - Verify the purge

```powershell
az cognitiveservices account list-deleted `
  --query "[?name=='ais-contosoretail-<suffix>']" `
  --output table
```

No result means the deleted resource is no longer listed. Azure may still need a few minutes to release the name before a new deployment succeeds.

You can now rerun the Foundry deployment script using the same `TenantName` and Resource Group name.

## Common errors

### The deleted resource is not found

Deletion may still be propagating. Wait a few minutes, run `az cognitiveservices account list-deleted` again, and use the exact name, original Resource Group, and location shown in its output.

### Authorization failed during purge

Verify that your account has the required purge permission at subscription scope. A `Contributor` role assigned only on the deleted Resource Group is not sufficient.

### Redeployment reports that the name already exists

Confirm that the Foundry resource no longer appears in `list-deleted`. If the purge just completed, wait a few minutes before retrying the deployment.