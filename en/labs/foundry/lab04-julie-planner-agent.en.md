# Lab 4: Julie Planner Agent

## Table of contents

- [Lab 4: Julie Planner Agent](#lab-4-julie-planner-agent)
	- [Table of contents](#table-of-contents)
	- [Introduction](#introduction)
	- [Setup continuity](#setup-continuity)
	- [Quick checklist](#quick-checklist)
		- [1) Verify SQL connection values](#1-verify-sql-connection-values)
		- [2) Alternative if you are not following the full lab sequence](#2-alternative-if-you-are-not-following-the-full-lab-sequence)
		- [3) Behavior when Fabric values are not provided](#3-behavior-when-fabric-values-are-not-provided)
	- [Manual configuration of permissions in Fabric (required for Lab 4)](#manual-configuration-of-permissions-in-fabric-required-for-lab-4)
		- [Part A — Workspace access](#part-a--workspace-access)
		- [Part B — SQL user and database permissions](#part-b--sql-user-and-database-permissions)
		- [Recommended validation](#recommended-validation)
	- [Julie project architecture (detailed)](#julie-project-architecture-detailed)
	- [What type of orchestration was chosen?](#what-type-of-orchestration-was-chosen)
	- [How was the workflow implemented in this lab?](#how-was-the-workflow-implemented-in-this-lab)
	- [Definition of specialized agents](#definition-of-specialized-agents)
		- [SqlAgent](#sqlagent)
		- [MarketingAgent](#marketingagent)
		- [JulieOrchestrator](#julieorchestrator)
	- [What does Program.cs do exactly?](#what-does-programcs-do-exactly)
	- [Lab steps](#lab-steps)
		- [Step 1: Configure appsettings.json](#step-1-configure-appsettingsjson)
		- [Step 2: Ensure Fabric permissions are configured](#step-2-ensure-fabric-permissions-are-configured)
		- [Step 3: Run Julie](#step-3-run-julie)
		- [Step 4: Test the end-to-end flow](#step-4-test-the-end-to-end-flow)
		- [Lab validation](#lab-validation)
	- [Challenges](#challenges)
		- [Challenge 1: Improve the MarketingAgent prompt for current campaigns](#challenge-1-improve-the-marketingagent-prompt-for-current-campaigns)
		- [Challenge 2: Create a no-code agent with Code Interpreter](#challenge-2-create-a-no-code-agent-with-code-interpreter)

---

## Introduction

In this lab you will build and validate Julie as a marketing campaign planner agent in Foundry. Julie is implemented as a `workflow`-type agent and orchestrates the flow with two sub-agents: `SqlAgent` and `MarketingAgent`. `SqlAgent` can use the OpenAPI tool `SqlExecutor` (Function App `FxContosoRetail`) to execute SQL against the database and return segmented customers. In this lab, you will progressively configure the environment, verify permissions and SQL connectivity, and run the end-to-end flow to obtain the final campaign output in JSON format.

## Setup continuity

This lab assumes you have already completed:

- The base Foundry infrastructure deployment (`en/labs/foundry/codespaces-setup.md`)
- The Fabric data flow from **Lab 1** (`../fabric/lab01-data-setup.en.md`)

## Quick checklist

### 1) Verify SQL connection values

For the updated setup, these values are used:

- `FabricWarehouseSqlEndpoint`
- `FabricWarehouseDatabase`

They are obtained from the Fabric Warehouse SQL connection string:

- `FabricWarehouseSqlEndpoint` = `Data Source` without `,1433`
- `FabricWarehouseDatabase` = `Initial Catalog`

### 2) Alternative if you are not following the full lab sequence

If you are not following the full sequence of labs, for Lab 4 you can also use a standalone SQL database (for example Azure SQL Database), adjusting those two values to the corresponding host and database name.

### 3) Behavior when Fabric values are not provided

If you do not provide these values during setup, the infrastructure deployment does not fail, but the SQL connection for Lab 4 is not configured automatically and must be adjusted manually in the Function App.

## Manual configuration of permissions in Fabric (required for Lab 4)

After deployment, make sure that the Managed Identity of the Function App has access to the workspace and to the `retail` SQL database.

### Part A — Workspace access

1. Open the workspace where the `retail` database was deployed.
2. Go to **Manage access**.
3. Click **Add people or groups**.
4. Search for and add the Function App identity.
	- Expected name: `func-contosoretail-[suffix]`
	- Example: `func-contosoretail-siwhb`
5. For the role, select **Contributor**.
6. Click **Add**.

### Part B — SQL user and database permissions

1. Within the same workspace, open the `retail` database.
2. Click **New Query**.
3. Run the following T-SQL code to create the external user:

```sql
CREATE USER [func-contosoretail-[suffix]] FROM EXTERNAL PROVIDER;
```

Real example:

```sql
CREATE USER [func-contosoretail-siwhb] FROM EXTERNAL PROVIDER;
```

4. Then assign read permissions:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-[suffix]];
```

Real example:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-siwhb];
```

### Recommended validation

- Wait 1–3 minutes for permission propagation.

## Julie project architecture (detailed)

This solution is organized into 4 main classes under `en/labs/foundry/code/agents/JulieAgent/`:

- `SqlAgent.cs`: defines the agent that transforms natural language into T-SQL.
- `MarketingAgent.cs`: defines the agent that writes personalized messages supported by Bing.
- `JulieAgent.cs`: defines Julie as a `workflow` orchestrator in CSDL YAML format and invokes sub-agents.
- `Program.cs`: loads configuration, creates/verifies agents in Foundry, and runs the chat.

## What type of orchestration was chosen?

A **workflow**-type orchestration was chosen for Julie.

- In a `prompt` agent, the model responds directly with its instructions and simple tools.
- In a `workflow` agent, the model coordinates steps and specialized tools to accomplish a composite task.

Julie uses `workflow` here because the scenario requires a multi-stage sequence:

1. interpret the business segment,
2. generate SQL,
3. generate per-customer messages,
4. consolidate everything into final JSON.

## How was the workflow implemented in this lab?

In the current version of the lab, Julie is built using the **typed SDK approach** with `WorkflowAgentDefinition`.

In `JulieAgent.cs`, `GetAgentDefinition(...)` explicitly returns `WorkflowAgentDefinition`:

```csharp
public static WorkflowAgentDefinition GetAgentDefinition(string modelDeployment, JsonElement? openApiSpec = null)
```

The definition is built with `WorkflowAgentDefinition` and a CSDL YAML `workflowYaml`, then materialized with the SDK factory:

```csharp
var workflowYaml = $$"""
kind: workflow
trigger:
  kind: OnConversationStart
  id: julie_workflow
  actions:
    - kind: InvokeAzureAgent
      id: sql_step
      conversationId: =System.ConversationId
      agent:
        name: {{SqlAgent.Name}}
    - kind: InvokeAzureAgent
      id: marketing_step
      conversationId: =System.ConversationId
      agent:
        name: {{MarketingAgent.Name}}
    - kind: EndConversation
      id: end_conversation
name: {{Name}}
""";

return ProjectsOpenAIModelFactory.WorkflowAgentDefinition(workflowYaml: workflowYaml);
```

> Technical note: Julie is **workflow-only** and orchestrates sub-agents through `InvokeAzureAgent` actions in the CSDL YAML; SQL execution via OpenAPI is encapsulated in `SqlAgent` when the spec is available. The `OnConversationStart` trigger with `EndConversation` defines a sequential flow that executes both steps and closes the workflow conversation.

The current orchestration uses 2 sub-agents:

- `SqlAgent` (tool of type `agent`)
- `MarketingAgent` (tool of type `agent`)

## Definition of specialized agents

### SqlAgent

`SqlAgent.cs` defines a `prompt`-type agent with strict instructions to return exactly 4 columns (`FirstName`, `LastName`, `PrimaryEmail`, `FavoriteCategory`) and uses `db-structure.txt` as context.

Full instructions:

```text
You are **SqlAgent**, an agent specialized in generating T-SQL queries
for the Contoso Retail database.

Your **ONLY** responsibility is to receive a natural language description
of a customer segment and generate a valid T-SQL query that returns
**EXACTLY** these columns:
- FirstName (customer first name)
- LastName (customer last name)
- PrimaryEmail (customer email address)
- FavoriteCategory (the product category in which the customer has spent the most money)

To determine **FavoriteCategory**, you must JOIN the
orders, order lines, and products tables, group by category, and select
the one with the highest total amount (SUM of LineTotal).

DATABASE STRUCTURE:
{dbStructure}

RULES:
1. ALWAYS return EXACTLY the 4 columns: FirstName, LastName, PrimaryEmail, FavoriteCategory.
2. Use appropriate JOINs between customer, orders, orderline, product, and productcategory.
3. For FavoriteCategory, use a subquery or CTE that groups by category
   and selects the highest spend (SUM(ol.LineTotal)).
4. Include only active customers (IsActive = 1).
5. Include only customers with a non-null and non-empty PrimaryEmail.
6. DO NOT execute the query; only generate it.
7. Return ONLY the T-SQL code, with no explanation, no markdown,
   and no code blocks. Pure SQL only.
8. Always respond in English if you need to add any SQL comments.
```

Design rationale:

- Explicitly restricting columns reduces ambiguity in the output.
- Enforcing pure SQL (no markdown) avoids ambiguity when chaining the output with Julie.
- Injecting `db-structure.txt` improves join accuracy and table naming.

```csharp
return new PromptAgentDefinition(modelDeployment)
{
    Instructions = GetInstructions(dbStructure)
};
```

### MarketingAgent

`MarketingAgent.cs` is also a `prompt` agent, but incorporates a Bing grounding tool via `connection.id`:

Full instructions:

```text
You are **MarketingAgent**, an agent specialized in creating personalized marketing messages
for Contoso Retail customers.

Your workflow is as follows:

1. You receive the full name of a customer and their favorite purchase category.
2. You use the Bing Search tool to look for recent or upcoming events
   related to that category. For example:
   - If the category is "Bikes", look for cycling events.
   - If the category is "Clothing", look for fashion events.
   - If the category is "Accessories", look for technology or lifestyle events.
   - If the category is "Components", look for engineering or manufacturing events.
3. From the search results, select the most relevant and current event.
4. Generate a brief and motivational marketing message (maximum 3 paragraphs) that:
   - Greets the customer by name.
   - Mentions the event found and why it is relevant to the customer.
   - Invites the customer to visit the Contoso Retail online catalog
     to find the best products in the category and be prepared for the event.
   - Uses a warm, enthusiastic, and professional tone.
   - Is written in English.

5. Return **ONLY** the text of the marketing message. No JSON, no metadata,
   and no additional explanations. Just the message ready to be sent by email.

IMPORTANT: If you do not find relevant events, generate a general message about
current trends in that category and invite the customer to explore the latest
offerings from Contoso Retail.

```

Design rationale:

- Separating marketing into its own agent decouples creativity from SQL logic.
- Bing grounding provides current context without “polluting” Julie with web searches.
- Limiting format/output simplifies later consolidation into campaign JSON.

```csharp
var bingGroundingAgentTool = new BingGroundingAgentTool(new BingGroundingSearchToolOptions(
    searchConfigurations: [new BingGroundingSearchConfiguration(projectConnectionId: bingConnectionName)]));

return new PromptAgentDefinition(modelDeployment)
{
    Instructions = Instructions,
    Tools = { bingGroundingAgentTool }
};
```

### JulieOrchestrator

`JulieAgent.cs` defines the main `workflow` agent that coordinates the other two agents using CSDL YAML.

Full instructions:

```text
You are Julie, the planning and orchestration agent for Contoso Retail marketing campaigns.

Your responsibility is to coordinate the creation of personalized marketing campaigns
for specific customer segments.

When you receive a campaign request, you follow these steps:

1. EXTRACTION: Analyze the user prompt and extract the description
   of the customer segment. Summarize that description into a clear sentence.

2. SQL GENERATION: Invoke SqlAgent, passing it the segment description.
   SqlAgent will return a T-SQL query.

3. SQL EXECUTION: Send the T-SQL to your OpenAPI tool (SqlExecutor)
   to execute it against the database. The tool will return the
   results as customer data.

4. PERSONALIZED MARKETING: For EACH returned customer, invoke
   MarketingAgent, passing it the customer's name and their favorite category.
   MarketingAgent will search for relevant events on Bing and generate a
   personalized message.

5. FINAL ORGANIZATION: With all generated messages, organize the
   result as a campaign JSON using the following format:

```json
{
  "campaign": "Descriptive campaign name",
  "generatedAt": "YYYY-MM-DDTHH:mm:ss",
  "totalEmails": N,
  "emails": [
     {
        "to": "email@example.com",
        "customerName": "First Last",
        "favoriteCategory": "Category",
        "subject": "Automatically generated email subject",
        "body": "Personalized marketing message"
     }
  ]
}
```

RULES:
- The "subject" field must be an attractive and relevant email subject line.
- The "body" field is the message generated by MarketingAgent for that customer.
- Always respond in English.
- If a customer does not have an email, omit them from the result.
- Generate a descriptive name for the campaign based on the segment.
```

Design rationale:

- `workflow` was chosen because there is a dependent sequence of steps (SQL → marketing).
- Julie does not "guess" results: it delegates SQL generation and content creation to specialized sub-agents.
- Centralizing the final output in Julie ensures a single, consistent JSON format for external consumption.

## What does Program.cs do exactly?

`Program.cs` does not contain campaign business logic; its role is operational:

1. Load `appsettings.json`.
2. Read `db-structure.txt`.
3. Download the Function App OpenAPI spec (if available).
4. Resolve the full ID of the Bing connection (the API requires the ARM resource ID, not just the name).
5. Create or reuse agents in Foundry.
6. Open an interactive chat with Julie.

The `EnsureAgent(...)` helper implements the **find → decide override → create version** pattern using SDK types:

```csharp
async Task EnsureAgent(string agentName, AgentDefinition agentDefinition)
{
	...
	var result = await projectClient.Agents.CreateAgentVersionAsync(
		agentName,
		new AgentVersionCreationOptions(agentDefinition));
	...
}
```

It then registers the 3 agents in order. In the current implementation, `SqlAgent` also receives the OpenAPI spec when available:

```csharp
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, bingConnectionId));
await EnsureAgent(JulieOrchestrator.Name, JulieOrchestrator.GetAgentDefinition(modelDeployment, openApiSpecJson));
```

Finally, the chat uses `ProjectResponsesClient` with Julie as the default agent:

```csharp
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
	defaultAgent: JulieOrchestrator.Name,
	defaultConversationId: conversation.Id);
```

With this, the local code is limited to orchestrating agent infrastructure; workflow execution happens inside Foundry on each `CreateResponse(...)` call.

> **Note about the Bing connection:** `Program.cs` resolves the Bing connection name (e.g., `ais-contosoretail-geoxs-bingsearchconnection`) to its full ARM resource ID using `projectClient.Connections.GetConnectionAsync()`. This is necessary because `BingGroundingSearchConfiguration(projectConnectionId:)` expects the full ID, not just the name.

> Note: `Program.cs` downloads OpenAPI with retries to tolerate intermittent DNS failures; that spec is passed to `SqlAgent` to enable the `SqlExecutor` tool and execute SQL from the sub-agent.

---

## Lab steps

### Step 1: Configure appsettings.json

Open `en/labs/foundry/code/agents/JulieAgent/appsettings.json` and replace all `<suffix>` values with the outputs from the deployment:

```json
{
  "FoundryProjectEndpoint": "https://ais-contosoretail-<suffix>.services.ai.azure.com/api/projects/aip-contosoretail-<suffix>",
  "ModelDeploymentName": "gpt-4.1",
  "FunctionAppBaseUrl": "https://func-contosoretail-<suffix>.azurewebsites.net/api",
  "BingConnectionName": "ais-contosoretail-<suffix>-bingsearchconnection"
}
```

All these values are obtained from the deployment script output (or from the portal → AI Foundry resource → **Project settings** → **Overview**).

> To obtain the `BingConnectionName` directly, run:
> ```bash
> az cognitiveservices account connection list \
>     --name ais-contosoretail-<suffix> \
>     --resource-group rg-contoso-retail \
>     --query "[?contains(name,'bing')].name" -o tsv
> ```
> Replace `<suffix>` with your suffix. The command returns the connection name ready to paste.

### Step 2: Ensure Fabric permissions are configured

Before running, confirm that you have already completed the **Manual configuration of permissions in Fabric** section in this document (Parts A and B). If you haven't, the Function App will not be able to execute SQL against the Warehouse and `SqlAgent` will fail.

### Step 3: Run Julie

From the terminal, at the root of the repository:

```bash
cd en/labs/foundry/code/agents/JulieAgent
dotnet run
```

On startup, the program:
1. Downloads the OpenAPI spec from the Function App (may take a few seconds).
2. Creates or updates the three agents in Foundry: `SqlAgent`, `MarketingAgent`, and `Julie`.
3. Opens an interactive chat in the terminal.

You will see messages like:

```
Agent SqlAgent created/updated.
Agent MarketingAgent created/updated.
Agent Julie created/updated.
Chat started. Type your campaign request (or 'exit' to quit):
>
```

### Step 4: Test the end-to-end flow

Type a prompt describing the customer segment for the campaign. For example:

```
Create a campaign for customers whose favorite category is Bikes
```

```
Generate a campaign for the 5 most recent customers who have purchased in the Clothing category
```

Julie will invoke `SqlAgent` (which will generate and execute SQL against Fabric), then `MarketingAgent` (which will search for events on Bing and write personalized messages for each customer), and finally consolidate everything into a campaign JSON:

```json
{
  "campaign": "Bikes Campaign - Spring 2026",
  "generatedAt": "2026-03-13T10:30:00",
  "totalEmails": 3,
  "emails": [
    {
      "to": "customer@example.com",
      "customerName": "Ana García",
      "favoriteCategory": "Bikes",
      "subject": "Ana, get ready for cycling season!",
      "body": "Hi Ana, ..."
    }
  ]
}
```

> The first run may take **30–60 seconds** because the workflow goes through SQL execution + Bing search + text generation for each customer in the segment.

### Lab validation

The lab is considered complete when:

- [ ] All three agents appear created in the Foundry portal (AI Foundry → your project → **Agents**).
- [ ] A campaign prompt returns a JSON with at least one generated email.
- [ ] The `body` of each email includes a reference to a current event or trend found via Bing.

---

## Challenges

### Challenge 1: Improve the MarketingAgent prompt for current campaigns

#### Context

When testing Julie's flow, MarketingAgent may generate messages based on outdated news or events (for example, events from 2024). This happens because the current prompt does not instruct Bing Search to filter by date, nor does it tell the agent to discard old results.

#### Objective

Ensure that MarketingAgent **always** generates marketing messages based on current or upcoming events, never on events that have already passed.

#### Part A — Iterate the prompt in the Playground

1. Open the **Azure AI Foundry** portal at [https://ai.azure.com](https://ai.azure.com).
2. Navigate to your project and open the **Agents** section.
3. Locate the **MarketingAgent** agent and open it.
4. In the **Instructions** panel, modify the prompt to solve the outdated events problem.
5. Use the **Chat** panel in the playground to test iteratively. Send messages like:
   - `"Generate a marketing message for John Smith, whose favorite category is Bikes"`
   - `"Generate a message for Maria López, category Clothing"`
6. Iterate the prompt until **all** responses reference current or upcoming events.

> 💡 **Tip:** The playground allows you to modify and test the prompt immediately, without recompiling or redeploying. Use it to experiment quickly.

#### Part B — Bring the improved prompt to the code

Once you have a prompt that works correctly in the playground:

1. Copy the final instructions from the playground.
2. Open the `MarketingAgent.cs` file in the `JulieAgent` project.
3. Replace the contents of the `Instructions` property with the improved prompt.
4. Run `dotnet run` and overwrite MarketingAgent when prompted.
5. Verify that the behavior is identical to what you validated in the playground.

#### Success criteria

- In the playground, MarketingAgent generates messages that only reference current or upcoming events.
- The same prompt, transferred to the code, produces the same result when running Julie end-to-end.

---

### Challenge 2: Create a no-code agent with Code Interpreter

#### Context

Azure AI Foundry offers a visual **no-code/low-code** experience for creating agents directly from the portal. In addition to Bing Grounding (which we already use), Foundry offers other integrated tools. In this challenge you will use **Code Interpreter** — a tool that allows the agent to write and execute Python code to analyze data, perform calculations, and generate charts.

#### Objective

Create an agent called **"SalesAnalyst"** from the Azure AI Foundry visual interface that analyzes Contoso Retail sales data and generates visualizations.

#### Steps

1. Open the **Azure AI Foundry** portal at [https://ai.azure.com](https://ai.azure.com).
2. Navigate to your project (`aip-contosoretail-<suffix>`).
3. In the side menu, go to **Agents**.
4. Click **+ New Agent**.
5. Configure the agent:
   - **Name:** `SalesAnalyst`
   - **Model:** Select `gpt-4.1`
   - **Instructions:** Copy and paste the following instructions:

```
You are SalesAnalyst, a sales data analyst for Contoso Retail.

Your role is to receive sales data (as text, CSV, or as a description),
analyze it, and generate useful insights for the commercial team.

Capabilities:
1. When you receive sales data, use Code Interpreter to:
   - Calculate totals, averages, and trends.
   - Generate bar, line, or pie charts as appropriate.
   - Identify the best-selling products or categories.
2. Present the results clearly and in an executive format.
3. If the user uploads a CSV file, analyze it automatically.

Rules:
- Always respond in English.
- Generate charts whenever the data allows it.
- Always include an executive text summary in addition to the chart.
- Use professional colors in visualizations.
```

6. In the **Tools** section, click **+ Add tool**.
7. Select **Code Interpreter**.
8. Click **Save** (or **Create**).

#### Testing

Use the **Chat** panel to test with these conversations:

a. `"I have these sales by category: Bikes $45,000, Clothing $12,000, Accessories $8,500, Components $23,000. Generate a pie chart and tell me which category is the strongest."`

b. `"Compare Q1 vs Q2 sales: Q1 — Bikes: 120 units, Clothing: 340, Accessories: 210. Q2 — Bikes: 155, Clothing: 290, Accessories: 380. Generate a comparative chart and analyze the trend."`

c. `"Calculate the percentage growth of each category between Q1 and Q2 and rank them from highest to lowest growth."`

#### Success criteria

- The agent generates **Python code** that executes within the conversation.
- Responses include **charts** visible directly in the chat.
- The agent provides an **executive summary** in English along with each visualization.
- The **Code Interpreter** tool appears as enabled in the agent configuration.

#### Reflection

- How does Code Interpreter differ from the other tools (Bing Grounding, OpenAPI)?
- What types of business tasks could you automate with an agent that executes code?
- Compare the experience of creating this agent visually vs. the programmatic creation of the previous agents:
  - What advantages does each approach have?
  - What limitations does the no-code approach have that the SDK doesn't?

When you receive a campaign request, you follow these steps:

1. **EXTRACTION:** Analyze the user prompt and extract the description of the customer segment. Summarize that description into a clear sentence.

2. **SQL GENERATION:** Invoke SqlAgent, passing it the segment description.  
   SqlAgent will return a T-SQL query.

3. **PERSONALIZED MARKETING:** Invoke  
   MarketingAgent, passing it the customer’s name and their favorite category.  
   MarketingAgent will search for relevant events on Bing and generate a personalized message.

4. **FINAL ORGANIZATION:** With all generated messages, organize the result as a campaign JSON using the following format:

```json
{
  "campaign": "Descriptive campaign name",
  "generatedAt": "YYYY-MM-DDTHH:mm:ss",
  "totalEmails": N,
  "emails": [
     {
        "to": "email@example.com",
        "customerName": "First Last",
        "favoriteCategory": "Category",
        "subject": "Automatically generated email subject",
        "body": "Personalized marketing message"
     }
  ]
}
```

RULES:
- The "subject" field must be an attractive and relevant email subject line.
- The "body" field is the message generated by MarketingAgent for that customer.
- Always respond in English.
- If a customer does not have an email, omit them from the result.
- Generate a descriptive name for the campaign based on the segment.
```

Design rationale:

- `workflow` was chosen because there is a dependent sequence of steps (SQL → marketing).
- Julie does not “guess” results: it delegates SQL generation and content creation to specialized sub-agents.
- Centralizing the final output in Julie ensures a single, consistent JSON format for external consumption.

## What does Program.cs do exactly?

`Program.cs` does not contain campaign business logic; its role is operational:

1. Load `appsettings.json`.
2. Read `db-structure.txt`.
3. Download the Function App OpenAPI spec (if available).
4. Create or reuse agents in Foundry.
5. Open an interactive chat with Julie.

The `EnsureAgent(...)` helper implements the **find → decide override → create version** pattern using SDK types:

```csharp
async Task EnsureAgent(string agentName, AgentDefinition agentDefinition)
{
    ...
    var result = await projectClient.Agents.CreateAgentVersionAsync(
        agentName,
        new AgentVersionCreationOptions(agentDefinition));
    ...
}
```

It then registers the 3 agents in order. In the current implementation, `SqlAgent` also receives the OpenAPI spec when available:

```csharp
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, bingConnectionId));
await EnsureAgent(JulieOrchestrator.Name, JulieOrchestrator.GetAgentDefinition(modelDeployment, openApiSpecJson));
```

Finally, the chat uses `ProjectResponsesClient` with Julie as the default agent:

```csharp
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: JulieOrchestrator.Name,
    defaultConversationId: conversation.Id);
```

With this, the local code is limited to orchestrating agent infrastructure; workflow execution happens inside Foundry on each `CreateResponse(...)` call.

> Note: `Program.cs` downloads OpenAPI with retries to tolerate intermittent DNS failures; that spec is passed to `SqlAgent` to enable the `SqlExecutor` tool and execute SQL from the sub-agent.

## Recommended pattern applied in this lab

To maintain consistency and maintainability, this lab applies the following pattern:

1. **Typed definitions in code**
    - `SqlAgent` and `MarketingAgent` return `PromptAgentDefinition`.
    - `JulieOrchestrator` returns `WorkflowAgentDefinition`.

2. **Typed version creation**
    - `CreateAgentVersionAsync(..., new AgentVersionCreationOptions(agentDefinition))` is used.

3. **Clear separation of responsibilities**
    - `Program.cs` creates/versions agents and opens the conversation.
    - Each agent class encapsulates its instructions and tools.

4. **Stable output contract**
    - Julie maintains a homogeneous final JSON output to facilitate consumption by other systems or automated validations.