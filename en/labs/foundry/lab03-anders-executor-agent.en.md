## Table of contents

- [Lab 3: Anders — Executor Agent](#lab-3-anders--executor-agent)
  - [Table of contents](#table-of-contents)
  - [Introduction](#introduction)
    - [What are we going to do in this lab?](#what-are-we-going-to-do-in-this-lab)
    - [Prerequisites](#prerequisites)
      - [Tools on your machine](#tools-on-your-machine)
      - [Azure infrastructure](#azure-infrastructure)
      - [RBAC permissions](#rbac-permissions)
  - [3.1 — Verify OpenAPI support (already preconfigured)](#31--verify-openapi-support-already-preconfigured)
    - [Quick validation checklist](#quick-validation-checklist)
    - [Step 1: Verify NuGet packages](#step-1-verify-nuget-packages)
    - [Step 2: Verify exposed endpoints](#step-2-verify-exposed-endpoints)
    - [Step 3: Verify build](#step-3-verify-build)
    - [Generated OpenAPI endpoints](#generated-openapi-endpoints)
  - [3.2 — Redeploy the Function App](#32--redeploy-the-function-app)
    - [How to obtain `FabricWarehouseSqlEndpoint` and `FabricWarehouseDatabase`?](#how-to-obtain-fabricwarehousesqlendpoint-and-fabricwarehousedatabase)
    - [Option 0: Re-run infrastructure setup (if you need to refresh settings)](#option-0-re-run-infrastructure-setup-if-you-need-to-refresh-settings)
    - [Option A: Using Azure Functions Core Tools (recommended)](#option-a-using-azure-functions-core-tools-recommended)
    - [Option B: Using Azure CLI](#option-b-using-azure-cli)
  - [3.3 — Verify the OpenAPI specification](#33--verify-the-openapi-specification)
    - [Get the JSON specification](#get-the-json-specification)
    - [Explore the Swagger UI](#explore-the-swagger-ui)
  - [3.4 — The Anders agent: Two SDK versions](#34--the-anders-agent-two-sdk-versions)
    - [Why two versions?](#why-two-versions)
    - [Which version should I use?](#which-version-should-i-use)
    - [Understanding the code (version `ms-foundry/` — recommended)](#understanding-the-code-version-ms-foundry--recommended)
      - [Phase 1 — Download the OpenAPI specification](#phase-1--download-the-openapi-specification)
      - [Phase 2 — Verify existing agent or create a new one](#phase-2--verify-existing-agent-or-create-a-new-one)
      - [Phase 3 — Interactive chat with the Responses API](#phase-3--interactive-chat-with-the-responses-api)
    - [Step 1: Configure `appsettings.json`](#step-1-configure-appsettingsjson)
    - [Step 2: Build and run](#step-2-build-and-run)
    - [Step 3: Inspect the agent in Azure AI Foundry](#step-3-inspect-the-agent-in-azure-ai-foundry)
    - [Step 4: Test the agent](#step-4-test-the-agent)
  - [Troubleshooting](#troubleshooting)
    - [Storage Account blocked by policy (error 503)](#storage-account-blocked-by-policy-error-503)
  - [Next step](#next-step)

---

## Introduction

Anders is the **executor agent** of Contoso Retail’s multi-agent architecture. Its role is to receive operational action requests — such as generating and publishing order reports — and execute them by interacting with external services such as the Azure Function `FxContosoRetail`.

For Anders to interact with the Contoso Retail API, we will define an **OpenAPI Tool** that allows the agent to automatically discover and invoke the Function App endpoints based on its OpenAPI specification. Additionally, we will add **OpenAPI** support to the Function App to document the API and make its endpoints easier to explore.

### What are we going to do in this lab?

| Step | Description |
|------|-------------|
| **3.1** | Add OpenAPI support to the Azure Function `FxContosoRetail` |
| **3.2** | Redeploy the Function App with the changes |
| **3.3** | Verify the OpenAPI specification |
| **3.4** | Understand, configure, run, and test the Anders agent |

### Prerequisites

#### Tools on your machine

| Tool | Description | Download |
|------|-------------|----------|
| **.NET 8 SDK** | Build and run the Function App and the Anders agent | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Azure CLI** | Authenticate to Azure, deploy resources, and assign RBAC roles | [Install](https://learn.microsoft.com/cli/azure/install-azure-cli) |
| **Azure Functions Core Tools** | Publish the Function App to Azure (recommended option) | [Install](https://learn.microsoft.com/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools) |
| **PowerShell** | Run deployment scripts | Windows: included · macOS/Linux: [Install PowerShell 7+](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) |
| **Git** | Clone the workshop repository | [Download](https://git-scm.com/downloads) |

> [!TIP]
> On **macOS**, you can install the tools using Homebrew:
> ```bash
> brew install dotnet-sdk azure-cli azure-functions-core-tools@4 powershell git
> ```

#### Azure infrastructure

- Have completed the **infrastructure setup** described in [Foundry Setup](README.md)
- Have noted **all values generated during the infrastructure deployment** (resource names, URLs, suffix, AI Foundry endpoint, etc.)
- Have identified these 2 Fabric Warehouse values (used in the updated setup):
    - `FabricWarehouseSqlEndpoint`
    - `FabricWarehouseDatabase`

#### RBAC permissions

Your user needs the **Cognitive Services User** role on the AI Services resource to be able to create and run agents. Since your user is **Owner of the tenant**, you can assign the role to yourself.

Run the following commands (replace `{suffix}` with your 5-character suffix):

```powershell
# 1. Get your user name (UPN)
$upn = az account show --query "user.name" -o tsv

# 2. Get the AI Services resource name (if you don't remember it)
az cognitiveservices account list --resource-group rg-contoso-retail --query "[].name" -o tsv

# 3. Assign the Cognitive Services User role
az role assignment create `
    --assignee $upn `
    --role "Cognitive Services User" `
    --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/ais-contosoretail-{suffix}"
```

> **Note:** RBAC propagation may take up to 1 minute. Wait before continuing with the lab.

---

## 3.1 — Verify OpenAPI support (already preconfigured)

In the current version of the workshop, the Function App `FxContosoRetail` **already includes** OpenAPI and decorated endpoints in the base code. In this step, you will not implement OpenAPI from scratch: you will only validate that everything is correct before deployment.

### Quick validation checklist

### Step 1: Verify NuGet packages

Open `FxContosoRetail.csproj` and confirm that these references exist:

- `Microsoft.Azure.Functions.Worker.Extensions.OpenApi`
- `Microsoft.Data.SqlClient`

### Step 2: Verify exposed endpoints

Open `FxContosoRetail.cs` and confirm that these endpoints exist:

- `HolaMundo`
- `OrdersReporter`
- `SqlExecutor`

Additionally, validate that `OrdersReporter` and `SqlExecutor` have OpenAPI attributes (`OpenApiOperation`, `OpenApiRequestBody`, `OpenApiResponseWithBody`).

### Step 3: Verify build

```powershell
cd labs\foundry\code\api\FxContosoRetail
dotnet build
```

If it builds without errors, you can move on to the redeployment in step 3.2.

> **Note:** OpenAPI is already registered in the project. You do not need to add packages or modify `Program.cs` in this lab.

> [!IMPORTANT]
> **About endpoint authentication**
>
> In this workshop we use `AuthorizationLevel.Anonymous` to simplify the configuration and allow Azure AI Foundry to invoke the Function App directly as an OpenAPI Tool without having to manage secrets or configure additional authentication.
>
> **In a production environment, this is not recommended.** The correct practice is to protect the Function App with **Azure Entra ID (Easy Auth)** and have Foundry authenticate using **Managed Identity**. The flow would be:
>
> 1. **Register an application in Entra ID** representing the Function App, obtaining an Application (client) ID and an Application ID URI (for example, `api://<client-id>`).
> 2. **Enable Easy Auth** on the Function App with `az webapp auth update`, configuring it to validate tokens issued by Entra ID against the app registration. This protects all endpoints at the platform level — requests without a valid bearer token are rejected with 401 before reaching the code.
> 3. **Assign permissions to the Managed Identity** of the AI Services resource (`ais-contosoretail-{suffix}`) as an authorized principal in the app registration, either by adding it as a member of an app role or as an allowed identity in the Easy Auth configuration.
> 4. **Use `OpenApiManagedAuthDetails`** in the agent code instead of `OpenApiAnonymousAuthDetails`, specifying the audience of the app registration:
>    ```csharp
>    openApiAuthentication: new OpenApiManagedAuthDetails(
>        audience: "api://<app-registration-client-id>")
>    ```
>
> With this configuration, when Foundry needs to call the Function App, it obtains a token from Entra ID using the managed identity of the AI Services resource, sends it as `Authorization: Bearer <token>`, and Easy Auth validates it automatically. The Function endpoints can keep `AuthorizationLevel.Anonymous` in the C# code because authentication occurs at the platform layer.

### Generated OpenAPI endpoints

Once deployed, the Function App will expose these additional endpoints:

| Endpoint | Description |
|----------|-------------|
| `/api/openapi/v3.json` | OpenAPI 3.0 specification in JSON format |
| `/api/swagger/ui` | Interactive Swagger UI interface |

---

## 3.2 — Redeploy the Function App

The infrastructure is already deployed from the initial setup. You only need to **publish the updated code** of the Function App.

> [!IMPORTANT]
> The updated infrastructure setup (`op-flex/deploy.ps1` and `op-consumption/deploy.ps1`) accepts these parameters to configure SQL for Lab 4:
> - `FabricWarehouseSqlEndpoint`
> - `FabricWarehouseDatabase`
>
> If they are not provided, the deployment continues and simply skips the automatic configuration of the `FabricWarehouseConnectionString` app setting.

### How to obtain `FabricWarehouseSqlEndpoint` and `FabricWarehouseDatabase`?

In Fabric, open your **Warehouse** and copy the SQL **connection string**. You will see something similar to:

```text
Data Source=kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com,1433;Initial Catalog=retail_sqldatabase_xxx;... 
```

Value mapping:

- `FabricWarehouseSqlEndpoint` = value of `Data Source` **without** `,1433`
    - Example: `kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com`
- `FabricWarehouseDatabase` = value of `Initial Catalog`
    - Example: `retail_sqldatabase_xxx`

> [!TIP]
> These values are obtained from the **Fabric environment deployed in Lab 1** (`../fabric/lab01-data-setup.en.md`).
>
> If you are not following the full lab sequence, in this lab we only need a SQL database to run queries. You can use a standalone SQL database (for example Azure SQL Database) and adjust the connection:
> - Replace `FabricWarehouseSqlEndpoint` with the SQL host of your standalone database
> - Replace `FabricWarehouseDatabase` with the name of your database
>
> In that scenario, also make sure to configure permissions for the Function App identity on that database.

### Option 0: Re-run infrastructure setup (if you need to refresh settings)

If you want a full redeploy (infra + publish) using the setup:

```powershell
# Flex Consumption
cd labs\foundry\setup\op-flex
.\deploy.ps1 `
    -TenantName "<tu-tenant>" `
    -ResourceGroupName "rg-contoso-retail" `
    -Location "eastus" `
    -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" `
    -FabricWarehouseDatabase "<database-warehouse>"
```

```powershell
# Consumption (Y1)
cd labs\foundry\setup\op-consumption
.\deploy.ps1 `
    -TenantName "<tu-tenant>" `
    -ResourceGroupName "rg-contoso-retail" `
    -Location "eastus" `
    -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" `
    -FabricWarehouseDatabase "<database-warehouse>"
```

> If you only changed Function App code and do not need to touch infrastructure, use Option A or Option B below.

### Option A: Using Azure Functions Core Tools (recommended)

If you have [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools) installed, redeployment is a single command:

```powershell
cd labs\foundry\code\api\FxContosoRetail
func azure functionapp publish func-contosoretail-<suffix>
```

> Replace `<suffix>` with the 5-character suffix you obtained during setup (for example, `func-contosoretail-a1b2c`).

### Option B: Using Azure CLI

If you do not have the `func` CLI, you can publish manually using `az`:

```powershell
# 1. Build the project
cd labs\foundry\code\api\FxContosoRetail
dotnet publish --configuration Release --output bin\publish

# 2. Create the zip package
Compress-Archive -Path "bin\publish\*" -DestinationPath "$env:TEMP\fxcontosoretail.zip" -Force

# 3. Deploy to Azure
az functionapp deployment source config-zip `
    --resource-group rg-contoso-retail `
    --name func-contosoretail-<suffix> `
    --src "$env:TEMP\fxcontosoretail.zip"

# 4. Clean up temporary files
Remove-Item "$env:TEMP\fxcontosoretail.zip" -Force
Remove-Item "bin\publish" -Recurse -Force
```

---

## 3.3 — Verify the OpenAPI specification

Once deployed, verify that the OpenAPI endpoints are available.

### Get the JSON specification

Open in a browser or with `curl`:

```
https://func-contosoretail-<suffix>.azurewebsites.net/api/openapi/v3.json
```

You should see a JSON with the OpenAPI structure describing the `HelloWorld`, `OrdersReporter`, and `SqlExecutor` endpoints, including the request/response schemas.

### Explore the Swagger UI

Navigate to:

```
https://func-contosoretail-<suffix>.azurewebsites.net/api/swagger/ui
```

From the Swagger UI interface you can explore the endpoints and test them interactively.

> **Important:** The OpenAPI specification documents the API and serves as a reference to understand which parameters to send and what response to expect. The Anders agent will use this information indirectly through the Function Tool that we will define in the next step.

---

## 3.4 — The Anders agent: Two SDK versions

The Anders agent implementation is provided in **two separate versions**, each located under `labs/foundry/code/agents/AndersAgent/`:

| Folder | SDK | API paradigm | Status |
|--------|-----|--------------|--------|
| `ai-foundry/` | `Azure.AI.Projects` + `Azure.AI.Agents.Persistent` | Persistent Agents (threads, runs, polling) | GA — kept for backward compatibility |
| `ms-foundry/` | `Azure.AI.Projects` + `Azure.AI.Projects.OpenAI` | Responses API (conversations, project responses) | **Preview** (as of February 2026) — **recommended** |

### Why two versions?

At the end of 2025, Microsoft introduced a **new experience for Microsoft Foundry** based on the **Responses API** and a redesigned agent management surface. This new experience — exposed through the `Azure.AI.Projects.OpenAI` package — replaces the previous Persistent Agents model (`Azure.AI.Agents.Persistent`) with a more agile approach that uses **named and versioned agents**, **conversations**, and the **Responses API** instead of threads and runs with polling.

The key differences between both approaches are:

| Aspect | `ai-foundry/` (Persistent Agents) | `ms-foundry/` (Responses API) |
|-------|-----------------------------------|-------------------------------|
| **Agent lifecycle** | Created with a generated ID; searched by name by iterating the list | Created/updated by name with explicit versioning (`CreateAgentVersionAsync`) |
| **Conversation model** | `PersistentAgentThread` + `ThreadRun` with polling | `ProjectConversation` + `ProjectResponsesClient` — synchronous response |
| **Tool definition** | `OpenApiToolDefinition` with typed classes | Protocol method via `BinaryContent` (types are internal in SDK 1.2.x) |
| **Chat pattern** | Create run → poll until completion → read messages | A single call to `CreateResponse()` returns the output directly |

### Which version should I use?

**The `ms-foundry/` version is recommended** for new development. It is aligned with the direction of the Microsoft Foundry platform and offers a simpler programming model — particularly the elimination of the polling loop in favor of a single synchronous response call.

The `ai-foundry/` version is kept in this workshop for **backward compatibility**: attendees whose Azure AI Services resources were provisioned before the new experience was available can complete the lab using the Persistent Agents API.

> [!IMPORTANT]
> As of February 2026, the `Azure.AI.Projects.OpenAI` package and the Responses API are in **public preview**. API shapes, payload schemas, and SDK types may change before reaching general availability (GA). If you encounter issues such as missing or renamed properties (for example, the required `kind` field in the agent definition payload), check the latest [Azure.AI.Projects.OpenAI release notes](https://www.nuget.org/packages/Azure.AI.Projects.OpenAI) for breaking changes.

---

### Understanding the code (version `ms-foundry/` — recommended)

Open the file `labs/foundry/code/agents/AndersAgent/ms-foundry/Program.cs` and note that it is organized into **3 phases**:

#### Phase 1 — Download the OpenAPI specification

```csharp
var openApiSpecUrl = $"{functionAppBaseUrl}/openapi/v3.json";
var openApiSpec = await httpClient.GetStringAsync(openApiSpecUrl);
```

The program downloads the OpenAPI specification from the Function App **at runtime**. This means that if the API changes (new endpoints, new parameters), the agent automatically detects it when restarted.

#### Phase 2 — Verify existing agent or create a new one

This phase has two key parts:

**Existing agent detection:**

Before creating a new version, the program checks whether the agent already exists by calling `GetAgent`. If it finds one, it asks the user whether they want to keep the existing agent or overwrite it with a new version. This avoids unnecessary proliferation of agent versions during iterative development.

**Agent definition with OpenAPI tool (protocol method):**

```csharp
var agentDefinitionJson = new
{
    definition = new
    {
        kind = "prompt",
        model = modelDeployment,
        instructions = andersInstructions,
        tools = new object[]
        {
            new
            {
                type = "openapi",
                openapi = new
                {
                    name = "ContosoRetailAPI",
                    description = "API de Contoso Retail...",
                    spec = openApiSpecJson,
                    auth = new { type = "anonymous" }
                }
            }
        }
    }
};
```

Since the `OpenApiAgentTool` types are internal in SDK 1.2.x, the tool definition is built as an anonymous object and serialized via `BinaryContent`. The `kind = "prompt"` field is required by the API to indicate a prompt-based agent.

**System prompt (instructions):**

The system prompt includes the exact JSON schema that Anders must build when invoking the API:

```json
{
  "customerName": "Nombre del Cliente",
  "startDate": "YYYY-MM-DD",
  "endDate": "YYYY-MM-DD",
  "orders": [
    {
      "orderNumber": "código de la orden",
      "orderDate": "YYYY-MM-DD",
      "orderLineNumber": 1,
      "productName": "nombre del producto",
      "brandName": "nombre de la marca",
      "categoryName": "nombre de la categoría",
      "quantity": 1.0,
      "unitPrice": 0.00,
      "lineTotal": 0.00
    }
  ]
}
```

> [!TIP]
> Including the schema in the instructions is a good practice when the agent must construct complex payloads. Although the OpenAPI specification already describes the schema, reinforcing it in the system prompt significantly reduces formatting errors.

**Agent reuse:**

```csharp
try
{
    existingAgent = projectClient.Agents.GetAgent(agentName);
    // Ask the user whether they want to overwrite or keep it
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    // Agent not found — create a new one
}
```

Before creating a new agent version, the program attempts to retrieve the existing agent by name. If it finds one, it asks the user to confirm whether they want to overwrite it. This avoids creating unnecessary versions in Foundry when restarting the application.

#### Phase 3 — Interactive chat with the Responses API

```csharp
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: agentName,
    defaultConversationId: conversation.Id);

ResponseResult response = responseClient.CreateResponse(input);
Console.WriteLine(response.GetOutputText());
```

The interaction pattern in the `ms-foundry/` version is simpler than the Persistent Agents approach:
1. A `ProjectConversation` is created (the conversation context)
2. A `ProjectResponsesClient` is obtained, bound to the agent and the conversation
3. Each user message is sent via `CreateResponse()`, which returns the output **synchronously** — without the need for a polling loop
4. The response text is extracted using `GetOutputText()`

> **What happens during a response call?** When the model decides it needs to call the API, Foundry automatically executes the HTTP call using the OpenAPI specification. The result is sent back to the model, which formulates the final response to the user. All of this happens within the single `CreateResponse()` call — the code simply receives the completed response.

**Cleanup on exit:**

When the user types `salir`, the chat loop ends. The agent **persists** in Foundry and is automatically reused on the next execution.

### Step 1: Configure `appsettings.json`

Open the file `labs/foundry/code/agents/AndersAgent/ms-foundry/appsettings.json` and replace the values with those from your environment:

```json
{
  "FoundryProjectEndpoint": "<YOUR-AI-FOUNDRY-PROJECT-ENDPOINT>",
  "ModelDeploymentName": "gpt-4.1",
  "FunctionAppBaseUrl": "https://func-contosoretail-<suffix>.azurewebsites.net/api"
}
```

> **Where do I find these values?**
> - **FoundryProjectEndpoint**: The `AI Foundry Endpoint` from the deployment output.
> - **ModelDeploymentName**: `gpt-4.1` (name of the deployment created by the Bicep).
> - **FunctionAppBaseUrl**: The URL of your Function App + `/api`.

### Step 2: Build and run

```powershell
cd labs\foundry\code\agents\AndersAgent\ms-foundry
dotnet build
```

Make sure there are no build errors. Then run:

```powershell
dotnet run
```

You will see in the console that the agent checks whether a version already exists in Foundry. If it finds one, it will ask whether you want to keep it or overwrite it. If it does not exist, a new agent is created automatically.

### Step 3: Inspect the agent in Azure AI Foundry

**Before interacting with Anders**, go to the portal to inspect what was created:

1. Open [Azure AI Foundry](https://ai.azure.com) and navigate to your project
2. In the side menu, select **Agents**
3. Find the **"Anders"** agent and click it

Observe two key things:

- **System prompt (instructions):** You will see the full instructions given to the agent, including the JSON schema. This is what guides its behavior when deciding when and how to invoke the API.
- **Tools:** You will see **ContosoRetailAPI** listed as an OpenAPI tool. You can expand it to see the full specification with the `ordersReporter` endpoint, the request/response schemas, and the anonymous authentication configuration.

> [!TIP]
> The system prompt and the tools are the two pillars that determine what an agent can do and how it does it. Understanding this relationship is key to designing effective agents.

### Step 4: Test the agent

Back in the console, test it first with a greeting:

```
You: Hi Anders, what can you do?
```

Anders should respond explaining that it can generate order reports. Then try with real data (paste everything on a single line):

```
You: Generate a report for Izabella Celma (period: January 1–31, 2026). Order ORD-CID-069-001 (2026-01-04): Sport-100 Helmet Black, Contoso Outdoor, Helmets, 6x$34.99=$209.94 | HL Road Frame Red 62, Contoso Outdoor, Road Frames, 10x$1431.50=$14315.00 | Long-Sleeve Logo Jersey S, Contoso Outdoor, Jerseys, 8x$49.99=$399.92. Order ORD-CID-069-003 (2026-01-08): HL Road Frame Black 58, Contoso Outdoor, Road Frames, 3x$1431.50=$4294.50 | HL Road Frame Red 44, Contoso Outdoor, Road Frames, 7x$1431.50=$10020.50. Order ORD-CID-069-002 (2026-01-17): HL Road Frame Red 62, Contoso Outdoor, Road Frames, 2x$1431.50=$2863.00 | LL Road Frame Black 60, Contoso Outdoor, Road Frames, 4x$337.22=$1348.88.
```

What happens internally:
1. Anders analyzes the message and decides it needs to call the `ordersReporter` endpoint
2. **Foundry executes the HTTP call** automatically to the Function App with the structured data according to the schema
3. The Function App generates the HTML report, uploads it to Blob Storage, and returns the URL
4. Foundry sends the result back to the model
5. Anders formulates its response and presents the URL to the user

Open the report URL in the browser to verify that it was generated correctly.

Now try a simpler case — a single order with two products:

```
You: Generate a report for Marco Rivera (period: February 5–10, 2026). Order ORD-CID-112-001 (2026-02-07): Mountain Bike Socks M, Contoso Outdoor, Socks, 3x$9.50=$28.50 | Water Bottle 30oz, Contoso Outdoor, Bottles and Cages, 1x$6.99=$6.99.
```

> **Note:** Typing `salir` only ends the conversation. The agent **persists** in Foundry and is automatically reused on the next execution.

---

## Troubleshooting

### Storage Account blocked by policy (error 503)

In subscriptions with strict Azure policies, the Storage Account backing the Function App may have its **public network access disabled** automatically after provisioning. This prevents the Functions host from reaching its own storage, causing a persistent **503 (Site Unavailable)** error — even though the app reports as `Running` and `Enabled`.

**Symptoms:**
- The Function App appears as `Running` in the Azure Portal and CLI
- All network access restrictions show "Allow all"
- Every HTTP request to any endpoint returns 503 after a ~60-second timeout

**Diagnosis:**
```powershell
az storage account show --name stcontosoretail<suffix> --resource-group rg-contoso-retail --query "publicNetworkAccess" -o tsv
```

If it returns `Disabled`, that is the root cause.

**Solution:**

A convenience script is included in the repository:

```powershell
cd labs/foundry/setup
.\unlock-storage.ps1
```

The script automatically detects the suffix from the Function App. If you need to force it, it also accepts `-Suffix` or `-FunctionAppName`.

This script enables public network access on the Storage Account and restarts the Function App. See [unlock-storage.ps1](setup/unlock-storage.ps1) for details.

---

## Next step

Continue with [Lab 4 — Julie (Planner Agent)](lab04-julie-planner-agent.en.md).
``