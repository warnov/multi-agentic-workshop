# Microsoft Foundry — Multi-Agent Workshop

## Introduction

This section of the workshop covers the **reasoning and execution layer** of the Contoso Retail multi-agent architecture, implemented on **Microsoft Foundry**. This is where the intelligent agents are built: they interpret data and plan actions (executing some of them) based on the information produced by the data layer (Microsoft Fabric).

### Agents in this layer

| Agent | Role | Description |
|-------|------|-------------|
| **Anders** | Executor Agent | Receives requests for operational actions (such as generating reports or rendering orders) and executes them by interacting with external services like the `FxContosoRetail` Azure Function. Type: `kind: "prompt"` with an OpenAPI tool. |
| **Julie** | Planner Workflow | Orchestrates personalized marketing campaigns. Receives a customer segment description and runs a 5-step flow: (1) extracts the customer filter, (2) calls **SqlAgent** to generate T-SQL, (3) executes the query against Fabric via **Function App OpenAPI**, (4) calls **MarketingAgent** (with Bing Search) to generate per-customer messages, (5) organizes the result as a JSON email campaign. Type: `kind: "workflow"` with 3 tools (2 agents + 1 OpenAPI). |

### Overall architecture

The Foundry layer sits at the center of the three-layer architecture:

```
┌─────────────────────┐
│   Copilot Studio    │  ← Interaction layer (Charles, Bill, Ric)
├─────────────────────┤
│  Microsoft Foundry  │  ← Reasoning layer (Anders, Julie) ★
├─────────────────────┤
│  Microsoft Fabric   │  ← Data layer (Mark, Amy)
└─────────────────────┘
```

The Anders and Julie agents use GPT-4.1 models deployed in Azure AI Services to reason about business information. Anders directly consumes the `FxContosoRetail` API via an OpenAPI tool. Julie orchestrates a multi-agent workflow: it uses **SqlAgent** (generates T-SQL), a **Function App** (executes the SQL against Fabric via OpenAPI), and **MarketingAgent** (generates personalized messages with Bing Search), coordinating everything autonomously as a `workflow`-type agent.

---

## Infrastructure setup

Before starting the labs, each participant must deploy the Azure infrastructure into their own subscription. The process is automated with Bicep and a PowerShell script.

### Prerequisites

- **Azure CLI** installed and up to date ([install](https://aka.ms/installazurecli))

- **.NET 8 SDK** installed ([download](https://dotnet.microsoft.com/download/dotnet/8.0))

- **PowerShell 7+** (required on all operating systems, including Windows)
  - Windows: `winget install Microsoft.PowerShell` or [download MSI](https://aka.ms/powershell-release?tag=stable)
  - Linux: [instructions](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux)
  - macOS: `brew install powershell/tap/powershell` or [instructions](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-macos)
  > ⚠️ **Important:** Run the scripts from `pwsh` (PowerShell 7), **not** from `powershell` (5.1). PowerShell 5.1 is not supported.
  
- **ExecutionPolicy** configured (Windows only): To be able to run scripts from an external source such as GitHub, you need to enable this option. Open `pwsh` as administrator and run:
  
  ```powershell
  Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
  ```
  
  ✅ This is only required once.
  
- An active **Azure subscription** with Owner or Contributor permissions

   - Once your tenant is ready, note down the **temporary tenant number** assigned to you: if the user you were assigned is usuario@azurehol3387.com, then your tenant number is 3387.

- The connection and database values from Microsoft Fabric. To obtain them, follow [this](./setup/sql-parameters.md) guide.


### ↗️ Deployment

To deploy the resources required for these labs, we have prepared Bicep and PowerShell scripts that automate the process without needing to manually create resources through the Azure or Foundry portal.

These scripts can be run from your local machine. However, to perform actions, you need to authenticate your local session with Azure to obtain the necessary permissions. Therefore, you must start by signing in to Azure from the terminal.

1. **Open a terminal in VS Code:** use the menu **Terminal → New Terminal** or the shortcut <kbd>Ctrl</kbd>+<kbd>`</kbd>.

2. **Sign in with Azure CLI:**

   ```powershell
   az login
   ```
   This will open the browser so you can authenticate with the Azure account assigned to you for the lab. Once completed, the terminal will display the list of available subscriptions.

3. **Verify the active subscription:**

   ```powershell
   az account show --output table
   ```
   Confirm that the subscription shown is the correct one for the workshop. If you need to change it:
   
   ```powershell
   az account set --subscription "subscription-name-or-id"
   ```

### Running the Script

Once you have confirmed the login with the user corresponding to your Azure subscription, run:

``` powershell
cd labs\foundry\setup\op-flex
.\deploy.ps1
```

After this, the script will interactively prompt you for your deployment parameters. Press **Enter** to accept the default value for location and resource group. Here is an example of a full run:

``` powershell
TenantName: 3345
Press Enter for default.
Location [eastus]: 
ResourceGroupName [rg-contoso-retail]: 
Do you want to configure the Fabric SQL connection for Lab04 now? (y/N): y
FabricWarehouseSqlEndpoint (no protocol, no port): kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com
FabricWarehouseDatabase: retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b
```

The following confirmation will be shown:

``` powershell
========================================
 Multi-Agent Workshop - Deployment
 Plan: Flex Consumption (FC1 / Linux)
========================================

  Tenant:         3345
  Location:       eastus
  Resource Group: rg-contoso-retail
  Fabric SQL:     kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com
  Fabric DB:      retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b
```

After this, you will start seeing the deployment progress and be informed about the resources being created. In less than 10 minutes your working environment should be fully ready.

---

> 👁️ **Review the output.** When the script finishes, it displays the names and URLs of all created resources. Take note of these values — you will need them in the labs!

> **Note:** If you do not provide the Fabric parameters, the deployment **does not fail**. It skips the SQL connection configuration and shows a warning to configure it manually later. The SQL connection is only needed for Lab 4 (Julie) and the `SqlExecutor` Function App.

---

### Verification

After the deployment, verify that the resources were created correctly:

```powershell
az resource list --resource-group rg-contoso-retail --output table
```

---

The output should contain the following resources (names may vary):

| Resource            | Name                          | Description                                                  |
| ------------------- | ----------------------------- | ------------------------------------------------------------ |
| Storage Account     | `stcontosoretail{suffix}`     | Storage for the Function App                                 |
| App Service Plan    | `asp-contosoretail-{suffix}`  | Hosting plan: Flex for Azure Functions                       |
| Function App        | `func-contosoretail-{suffix}` | Contoso Retail API (.NET 8, dotnet-isolated)                 |
| AI Foundry Resource | `ais-contosoretail-{suffix}`  | Unified AI Foundry resource (AI Services + project management) with GPT-4.1 model deployed |
| AI Foundry Project  | `aip-contosoretail-{suffix}`  | Working project inside the Foundry Resource                  |

> **Note:** The `{suffix}` is a unique 5-character identifier automatically generated from the tenant number you provided. This ensures resource names do not collide between participants.

### RBAC permissions for Microsoft Foundry

For agents to be created and run in Microsoft Foundry, your user needs the **Cognitive Services User** role on the AI Services resource. This role includes the data action `Microsoft.CognitiveServices/*` required for agent operations. Without it, you will get a `PermissionDenied` error when trying to create agents.

Run the following commands to assign the role (replace `{suffix}` with your 5-character suffix):

```powershell
# Get your username (UPN)
$upn = az account show --query "user.name" -o tsv

# Assign the Cognitive Services User role on the AI Services resource
az role assignment create `
    --assignee $upn `
    --role "Cognitive Services User" `
    --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/ais-contosoretail-{suffix}"
```

> **Note:** If you do not know the exact resource name, you can look it up with:
> ```powershell
> az cognitiveservices account list --resource-group rg-contoso-retail --query "[].name" -o tsv
> ```
>
> RBAC propagation can take up to 1 minute. Wait before attempting to create agents.

---

## Code structure

```
labs/foundry/
├── README.md                              ← This file
├── lab03-anders-executor-agent.en.md      ← Lab 3: Anders Agent
├── lab04-julie-planner-agent.en.md       ← Lab 4: Julie Agent
├── setup/
│   ├── op-flex/                           ← ⭐ Recommended option (Flex Consumption / Linux)
│   │   ├── main.bicep
│   │   ├── storage-rbac.bicep
│   │   └── deploy.ps1
│   └── op-consumption/                    ← Classic option (Consumption Y1 / Windows; see es/ folder)
└── code/
    ├── api/
    │   └── FxContosoRetail/               ← Azure Function (API)
    │       ├── FxContosoRetail.cs          ← Endpoints: HelloWorld, OrdersReporter, SqlExecutor
    │       ├── Program.cs
    │       ├── Models/
    │       └── ...
    ├── agents/
    │   ├── AndersAgent/                   ← Console App: Anders Agent (kind: prompt + OpenAPI tool)
    │   │   ├── ms-foundry/                ← Responses API version (recommended)
    │   │   │   ├── Program.cs
    │   │   │   └── appsettings.json
    │   │   └── ai-foundry/                ← Persistent Agents API version (alternative)
    │   │       └── ...
    │   └── JulieAgent/                    ← Console App: Julie Agent (kind: workflow)
    │       ├── Program.cs                 ← Creates the 3 agents + chat with Julie
    │       ├── JulieAgent.cs              ← Julie: workflow with 3 tools (SqlAgent, MarketingAgent, OpenAPI)
    │       ├── SqlAgent.cs                ← Sub-agent: generates T-SQL from natural language
    │       ├── MarketingAgent.cs           ← Sub-agent: generates messages with Bing Search
    │       ├── db-structure.txt            ← DB DDL injected into SqlAgent
    │       └── appsettings.json
    └── tests/
        ├── bruno/                         ← Bruno collection (REST client)
        │   ├── bruno.json
        │   ├── OrdersReporter.bru
        │   └── environments/
        │       └── local.bru
        └── http/
            └── FxContosoRetail.http       ← .http file (VS Code REST Client)
```

---

## Labs

| Lab   | File                                                          | Description                                                  |
| ----- | ------------------------------------------------------------- | ------------------------------------------------------------ |
| Lab 3 | [Anders — Executor Agent](lab03-anders-executor-agent.en.md) | Create the executor agent that generates reports and interacts with Contoso Retail services. |
| Lab 4 | [Julie — Planner Agent](lab04-julie-planner-agent.en.md)     | Create the marketing campaign orchestrator agent using the workflow pattern with sub-agents (SqlAgent, MarketingAgent) and an OpenAPI tool. |

---

## 

## Next step

Once the setup is complete, continue with [Lab 3 — Anders (Executor Agent)](lab03-anders-executor-agent.en.md).
