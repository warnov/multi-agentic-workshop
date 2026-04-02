# Microsoft Foundry — Intro and Infrastructure Setup

## Introduction

This section of the workshop covers the **reasoning and execution layer** of Contoso Retail's multi-agent architecture, implemented on **Microsoft Foundry**. This is where the intelligent agents are built: they interpret data and plan actions (executing some of them) based on the information produced by the data layer (Microsoft Fabric).

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

## Setup with GitHub Codespaces

> 💡 **Prefer working on your local machine?** See [setup.md](setup.md) for manual installation instructions with Azure CLI, .NET, and PowerShell 7.

GitHub Codespaces provides a complete cloud-based development environment, pre-configured with all the necessary tools. You don't need to install anything on your machine — just a browser and access to the GitHub repository.

> [!IMPORTANT]
> **Requirement: GitHub account**
> To use GitHub Codespaces you need a GitHub account. If you don't have one yet, you can create a free account following the instructions at [https://docs.github.com/en/get-started/start-your-journey/creating-an-account-on-github](https://docs.github.com/en/get-started/start-your-journey/creating-an-account-on-github).

### What's included in the environment?

| Tool | Pre-installed |
|---|---|
| .NET 8 SDK | ✅ |
| Azure CLI (latest version) | ✅ |
| PowerShell 7+ | ✅ |
| C# Dev Kit (VS Code extension) | ✅ |
| Bicep (VS Code extension) | ✅ |
| REST Client (VS Code extension) | ✅ |

---

### Step 1: Access the repository on GitHub

1. Open your browser and navigate to the workshop GitHub repository URL provided by your instructor.
2. Sign in to GitHub.

---

### Step 2: Create the Codespace

1. On the repository home page, click the green **`< > Code`** button.
2. Select the **Codespaces** tab.
3. Click **"Create codespace on main"**.

   > 💡 If you see an option to choose the machine type, the **2-core** option is more than enough for this workshop.

4. Wait **2 to 4 minutes** while GitHub builds the environment. You will see a loading screen with build logs. **This only happens the first time** — subsequent sessions start in seconds because the environment is saved.

5. When the environment is ready, **VS Code will open in the browser** with all repository files already available in the left panel. If the browser connection is problematic, you can launch the Codespace in your local VS Code from the Code section of the repo on GitHub:  
   ![Instructions to open Codespace in local VS Code](../../assets/codespaces-instructions.png)

> 💡 **Prefer VS Code desktop?** If you have VS Code installed with the **GitHub Codespaces** extension, click the `><` icon (bottom-left corner) → *Connect to Codespace* to connect from local VS Code without losing the cloud environment.

---

### Step 3: Verify the environment is ready

Open the integrated terminal with <kbd>Ctrl</kbd>+<kbd>`</kbd> (or **Terminal → New Terminal**) and run the following three commands to confirm everything is installed:

```bash
dotnet --version
```
You should see something like `8.0.xxx`.

```bash
az version
```
You should see the installed Azure CLI version (JSON with `"azure-cli": "2.x.x"`).

```bash
pwsh --version
```
You should see `PowerShell 7.x.x`.

If all three respond correctly, restore the workshop .NET dependencies:

```bash
dotnet restore en/labs/foundry/code/taller-multi-agentic.sln
```

The environment is ready for the next steps.

---

### Step 4: Authenticate with Azure

> ℹ️ **Your unique resource suffix is generated automatically** from your Azure subscription ID (a globally unique UUID). You don't need to enter any tenant number or manual identifier.

---

In the Codespace terminal, run:

```bash
az login --use-device-code
```

> ⚠️ It is important to use `--use-device-code` in Codespaces. The normal flow (`az login`) tries to open a local browser from the remote server, which does not work correctly in this environment. Remember to open the authentication URL in your local browser, where you are logged in with your lab account created for the workshop (the one ending in `@azurehol<number>.com`).

You will see output similar to this:

```
To sign in, use a web browser to open the page https://microsoft.com/devicelogin
and enter the code XXXXXXXX to authenticate.
```

Follow these steps:
1. Open `https://microsoft.com/devicelogin` in your browser (in a new tab).
2. Enter the 8-character code shown in the Codespace terminal.
3. Select the **workshop Azure account** (the one ending in `@azurehol<number>.com`).
4. Authorize access when prompted.
5. Return to the Codespace terminal — within a few seconds you will see the list of available subscriptions.

Verify that the active subscription is correct:

```bash
az account show --output table
```

If you need to change it:

```bash
az account set --subscription "subscription-name-or-id"
```

---

### Step 5: Obtain the Microsoft Fabric parameters

For Lab 4 (Julie/SqlAgent), you will need two values from the Fabric Warehouse:

- **FabricWarehouseSqlEndpoint**: SQL endpoint of the Warehouse, without `tcp://` or port. Example: `xyz.datawarehouse.fabric.microsoft.com`
- **FabricWarehouseDatabase**: exact full name of the database.

To obtain them from the Fabric portal, follow the [sql-parameters.md](./setup/sql-parameters.md) guide.

> **Note:** If you don't have these values yet, you can skip them. The deployment will continue without them and the rest of the infrastructure will be created correctly. You can configure the SQL connection manually later from the Azure portal.

---

### Step 6: Run the deployment script

In the Codespace terminal, navigate to the script folder:

```bash
cd /workspaces/multi-agentic-workshop/en/labs/foundry/setup/op-flex
```

Run the deployment script (using `pwsh` to start PowerShell 7):

```bash
pwsh ./deployFromAzure.ps1
```

The script will only ask for optional parameters. Press <kbd>Enter</kbd> to accept the default values for `Location` and `ResourceGroupName`:

```
Press Enter for default.
Location [eastus]:
ResourceGroupName [rg-contoso-retail]:
Do you want to configure the Fabric SQL connection for Lab04 now? (y/N): y
FabricWarehouseSqlEndpoint (no protocol, no port): kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com
FabricWarehouseDatabase: retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b
```

You will see the plan confirmation before execution begins:

```
========================================
 Multi-Agent Workshop - Deployment
 Plan: Flex Consumption (FC1 / Linux)
 Mode: Azure Cloud Shell
========================================

  Subscription:   My Azure Subscription (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)
  Suffix:         ab3f2
  Location:       eastus
  Resource Group: rg-contoso-retail
  Fabric SQL:     kqbvkknqlijebcyrtw2rgtsx2e-...
  Fabric DB:      retail_sqldatabase_...
```

You will then see the deployment progress resource by resource:

```
  ⏳ CognitiveServices/accounts/ais-contosoretail-ab3f2 ...
  ✅ Storage/storageAccounts/stcontosoretailab3f2
  ✅ Web/serverFarms/asp-contosoretail-ab3f2
  ✅ Web/sites/func-contosoretail-ab3f2
  ✅ CognitiveServices/accounts/ais-contosoretail-ab3f2
  ✅ Code published successfully.
```

The full process takes between **5 and 10 minutes**.

> 👁️ **Take note of the final output!** When finished, the script displays the names and URLs of all created resources. You will need these values to configure the agents in the following steps.

---

### Step 7: Record the deployment outputs

When finished, note these values from the script output:

| Script output | Description | Where it's used |
|---|---|---|
| `Unique suffix` | 5 characters, e.g.: `ab3f2` | To identify your resources in Azure |
| `Function App Base URL` | API base URL | `appsettings.json` for Anders and Julie |
| `Foundry Project Endpoint` | Foundry project endpoint | `appsettings.json` for Anders and Julie |
| `Bing Connection Name` | Bing connection name | `appsettings.json` for Julie |
| `Bing Connection ID (Julie)` | Bing connection ID | `appsettings.json` for Julie |

---

### Step 8: Configure the agents' appsettings.json

#### Anders (Lab 3)

In the Codespace file panel, open:
`en/labs/foundry/code/agents/AndersAgent/ms-foundry/appsettings.json`

Replace the `<suffix>` values with the suffix you obtained in the previous step:

```json
{
  "FoundryProjectEndpoint": "https://ais-contosoretail-<suffix>.services.ai.azure.com/api/projects/aip-contosoretail-<suffix>",
  "ModelDeploymentName": "gpt-4.1",
  "FunctionAppBaseUrl": "https://func-contosoretail-<suffix>.azurewebsites.net/api"
}
```

#### Julie (Lab 4)

Open `en/labs/foundry/code/agents/JulieAgent/appsettings.json` and fill in all values using the deployment outputs noted in Step 8.

---

### Step 9: Assign RBAC permissions in Foundry

For agents to be created and run, your user needs the **Cognitive Services User** role on the AI Services resource. Without this role you will get a `PermissionDenied` error when trying to create agents.

Run these commands in the Codespace terminal (bash):

```bash
# Get the Object ID of the authenticated user (works with MSA/personal and work accounts)
objectId=$(az ad signed-in-user show --query id -o tsv 2>/dev/null || \
    az account get-access-token --query accessToken -o tsv | \
    python3 -c "import sys,base64,json; t=sys.stdin.read().strip(); p=t.split('.')[1]; p+='='*(4-len(p)%4); print(json.loads(base64.b64decode(p))['oid'])")

# Get the name of the AI Services resource created by the deployment
aisName=$(az cognitiveservices account list \
    --resource-group rg-contoso-retail \
    --query "[0].name" -o tsv)

# Assign the role using the Object ID (does not require Graph API permissions)
az role assignment create \
    --assignee-object-id "$objectId" \
    --assignee-principal-type User \
    --role "Cognitive Services User" \
    --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/$aisName"
```

Wait **1 minute** for the permission to propagate before running the agents.

> **Note:** If you get a `RoleAssignmentExists` error, the role was already assigned automatically by the deployment script. You can continue.

---

### Step 10: Verify the deployment

Confirm that all resources were created correctly:

```bash
az resource list --resource-group rg-contoso-retail --output table
```

The result should include these resources:

| Resource            | Name                            | Description |
| ------------------- | ------------------------------- | ----------- |
| Storage Account     | `stcontosoretail{suffix}`       | Storage for the Function App |
| App Service Plan    | `asp-contosoretail-{suffix}`    | Flex Consumption hosting plan |
| Function App        | `func-contosoretail-{suffix}`   | Contoso Retail API (.NET 8, dotnet-isolated) |
| AI Foundry Resource | `ais-contosoretail-{suffix}`    | AI Services + Foundry projects with GPT-4.1 |
| AI Foundry Project  | `aip-contosoretail-{suffix}`    | Working project in Foundry |
| Bing Search         | `bing-contosoretail-{suffix}`   | Web search connection for the Julie agent |

> **Note:** The `{suffix}` is a unique 5-character identifier generated automatically from your subscription ID. This ensures that resource names don't collide between participants.

---

### Codespace management

#### Pause (to conserve free hours)

The Codespace pauses automatically after **30 minutes of inactivity**. You can also pause it manually from the Codespaces tab on GitHub. Your files and configuration are preserved between sessions.

#### Resume a saved session

- Go to the repository on GitHub → **Code** → **Codespaces** → click your existing Codespace.
- The environment reopens in seconds with everything as you left it.
- Verify that the Azure CLI session is still active with `az account show`. If it expired, repeat Step 4.

#### Delete when the workshop is finished

To free up your quota hours:
- Go to `github.com/codespaces`
- Find your Codespace → click `···` → **Delete**.

> ⚠️ Deleting the Codespace will lose any uncommitted local changes. If you modified `appsettings.json` files and want to save them, copy them somewhere safe before deleting.

#### Available free hours

GitHub offers **120 hours/month** free on 2-core machines for personal accounts. An 8-hour workshop uses only 7% of the monthly limit.

---

## Code structure

```
labs/foundry/
├── setup.md                               ← Local machine setup guide
├── codespaces-setup.md                    ← This file (Codespaces guide — recommended)
├── lab03-anders-executor-agent.en.md      ← Lab 3: Anders Agent
├── lab04-julie-planner-agent.en.md        ← Lab 4: Julie Agent
├── setup/
│   ├── op-flex/                           ← ⭐ Recommended option (Flex Consumption / Linux)
│   │   ├── main.bicep
│   │   ├── storage-rbac.bicep
│   │   ├── deploy.ps1                     ← Script for local machine (Windows/macOS/Linux)
│   │   └── deployFromAzure.ps1            ← Script for Codespaces / Azure Cloud Shell
│   └── op-consumption/                    ← Classic option (Consumption Y1 / Windows; see es/ folder)
└── code/
    ├── api/
    │   └── FxContosoRetail/               ← Azure Function (API)
    │       ├── FxContosoRetail.cs         ← Endpoints: HelloWorld, OrdersReporter, SqlExecutor
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
    │       ├── MarketingAgent.cs          ← Sub-agent: generates messages with Bing Search
    │       ├── db-structure.txt           ← DB DDL injected into SqlAgent
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

| Lab   | File                                                            | Description                                                  |
| ----- | --------------------------------------------------------------- | ------------------------------------------------------------ |
| Lab 3 | [Anders — Executor Agent](lab03-anders-executor-agent.en.md)   | Create the executor agent that generates reports and interacts with Contoso Retail services. |
| Lab 4 | [Julie — Planner Agent](lab04-julie-planner-agent.en.md)       | Create the marketing campaign orchestrator agent using the workflow pattern with sub-agents (SqlAgent, MarketingAgent) and an OpenAPI tool. |

---

## Next step

Once setup is complete, continue with [Lab 3 — Anders (Executor Agent)](lab03-anders-executor-agent.en.md).
