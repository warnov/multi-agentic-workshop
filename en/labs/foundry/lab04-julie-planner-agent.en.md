# Lab 4: Julie Planner Agent

## Introduction

In this lab you will build and validate Julie as a marketing campaign planner agent in Foundry. Julie is implemented as a `workflow`-type agent and orchestrates the flow with two sub-agents: `SqlAgent` and `MarketingAgent`. `SqlAgent` can use the OpenAPI tool `SqlExecutor` (Function App `FxContosoRetail`) to execute SQL against the database and return segmented customers. In this lab, you will progressively configure the environment, verify permissions and SQL connectivity, and run the end-to-end flow to obtain the final campaign output in JSON format.

## Setup continuity

This lab assumes you have already completed:

- The base Foundry infrastructure deployment (`labs/foundry/README.en.md`)
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

### Part B — SQL user and database permissions

1. Within the same workspace, open the `retail` database.
2. Click **New Query**.
3. Run the following T-SQL code to create the external user:

```sql
CREATE USER [func-contosoretail-[sufijo]] FROM EXTERNAL PROVIDER;
```

Real example:

```sql
CREATE USER [func-contosoretail-siwhb] FROM EXTERNAL PROVIDER;
```

4. Then assign read permissions:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-[sufijo]];
```

Real example:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-siwhb];
```

### Recommended validation

- Wait 1–3 minutes for permission propagation.

## Julie project architecture (detailed)

This solution is organized into 4 main classes under `labs/foundry/code/agents/JulieAgent/`:

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
kind: Workflow
trigger:
    kind: OnActivity
workflow:
    actions:
        - kind: InvokeAzureAgent
            id: sql_step
            agent:
                name: {{SqlAgent.Name}}
            conversationId: =System.ConversationId
            input:
                messages: =System.LastMessage
            output:
                messages: Local.SqlMessages

        - kind: InvokeAzureAgent
            id: marketing_step
            agent:
                name: {{MarketingAgent.Name}}
            conversationId: =System.ConversationId
            input:
                messages: =Local.SqlMessages
            output:
                autoSend: true
""";

return ProjectsOpenAIModelFactory.WorkflowAgentDefinition(workflowYaml: workflowYaml);
```

> Technical note: Julie is **workflow-only** and orchestrates sub-agents through `InvokeAzureAgent` actions in the CSDL YAML; SQL execution via OpenAPI is encapsulated in `SqlAgent` when the spec is available.

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
8. Always respond in Spanish if you need to add any SQL comments.
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
   - Is written in Spanish.

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
    searchConfigurations: [new BingGroundingSearchConfiguration(projectConnectionId: bingConnectionId)]));

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
You are **Julie**, the planning and orchestration agent for Contoso Retail marketing campaigns.

Your responsibility is to coordinate the creation of personalized marketing campaigns for specific customer segments.

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
  "campaign": "Nombre descriptivo de la campaña",
  "generatedAt": "YYYY-MM-DDTHH:mm:ss",
  "totalEmails": N,
  "emails": [
     {
        "to": "email@ejemplo.com",
        "customerName": "Nombre Apellido",
        "favoriteCategory": "Categoría",
        "subject": "Asunto del correo generado automáticamente",
        "body": "Mensaje de marketing personalizado"
     }
  ]
}
```

RULES:
- The "subject" field must be an attractive and relevant email subject line.
- The "body" field is the message generated by MarketingAgent for that customer.
- Always respond in Spanish.
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