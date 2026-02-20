# Lab 4: Julie Planner Agent

## Introduction

In this lab you will build and validate Julie as a marketing campaign planner agent in Foundry. Julie is implemented as a `workflow` type agent and orchestrates the flow with two sub-agents: `SqlAgent` and `MarketingAgent`. `SqlAgent` can use the `SqlExecutor` OpenAPI tool (Function App `FxContosoRetail`) to execute SQL against the database and return segmented customers. In this lab, you will progressively configure the environment, verify permissions and the SQL connection, and run the end-to-end flow to obtain the final campaign output in JSON format.

## Setup continuity

This lab assumes you have already completed:

- The Foundry base infrastructure deployment (`labs/foundry/README.md`)
- The Fabric data flow from **Lab 1** (`../fabric/lab01-data-setup.md`)

## Quick checklist

### 1) Verify SQL connection values

The updated setup uses these values:

- `FabricWarehouseSqlEndpoint`
- `FabricWarehouseDatabase`

Obtained from the Fabric Warehouse SQL connection string:

- `FabricWarehouseSqlEndpoint` = `Data Source` without `,1433`
- `FabricWarehouseDatabase` = `Initial Catalog`

### 2) Alternative if you are not following the full lab sequence

If you are not following the complete lab sequence, for Lab 4 you can also use a standalone SQL database (e.g. Azure SQL Database), adjusting those two values to the corresponding host and database name.

### 3) Behaviour when Fabric values are not provided

If you do not provide these values during setup, the infrastructure deployment does not fail, but the SQL connection for Lab 4 is not configured automatically and must be adjusted manually in the Function App.

## Manual permissions configuration in Fabric (required for Lab 4)

After deployment, make sure the Function App Managed Identity has access to the workspace and the `retail` SQL database.

### Part A — Workspace access

1. Open the workspace where the `retail` database was deployed.
2. Go to **Manage access**.
3. Click **Add people or groups**.
4. Search for and add the Function App identity.
	- Expected name: `func-contosoretail-[suffix]`
	- Example: `func-contosoretail-siwhb`
5. For the role, select **Contributor** (if your Fabric is in English) or **Colaborador** (if in Spanish).
6. Click **Add**.

### Part B — SQL user and database permissions

1. Inside the same workspace, open the `retail` database.
2. Click **New Query**.
3. Run the following T-SQL to create the external user:

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

## Julie project architecture (detail)

This solution is organised into 4 main classes inside `labs/foundry/code/agents/JulieAgent/`:

- `SqlAgent.cs`: defines the agent that transforms natural language into T-SQL.
- `MarketingAgent.cs`: defines the agent that drafts personalised messages supported by Bing.
- `JulieAgent.cs`: defines Julie as a `workflow` orchestrator in CSDL YAML format and invokes the sub-agents.
- `Program.cs`: loads configuration, creates/verifies agents in Foundry and runs the chat.

## What type of orchestration was chosen?

A **workflow** type orchestration was chosen for Julie.

- In a `prompt` agent, the model responds directly with its instruction and simple tools.
- In a `workflow` agent, the model coordinates steps and specialised tools to fulfil a composite task.

Here Julie uses `workflow` because the use case requires a multi-stage sequence:

1. interpret the business segment,
2. generate SQL,
3. generate messages per customer,
4. consolidate everything into a final JSON.

## How was the workflow implemented in this lab?

In the current version of the lab, Julie is built with the **typed SDK approach** using `WorkflowAgentDefinition`.

In `JulieAgent.cs`, `GetAgentDefinition(...)` explicitly returns `WorkflowAgentDefinition`:

```csharp
public static WorkflowAgentDefinition GetAgentDefinition(string modelDeployment, JsonElement? openApiSpec = null)
```

The definition is built with `WorkflowAgentDefinition` and a CSDL `workflowYaml`, then materialised with the SDK factory:

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

> Technical note: Julie remains **workflow-only** and orchestrates sub-agents via `InvokeAzureAgent` actions in the CSDL YAML; SQL execution via OpenAPI is encapsulated in `SqlAgent` when the spec is available.

The current orchestration uses 2 sub-agents:

- `SqlAgent` (tool type `agent`)
- `MarketingAgent` (tool type `agent`)


## Specialised agent definitions

### SqlAgent

`SqlAgent.cs` defines a `prompt` type agent with strict instructions to return exactly 4 columns (`FirstName`, `LastName`, `PrimaryEmail`, `FavoriteCategory`) and uses `db-structure.txt` as context.

Full instructions:

```text
You are SqlAgent, a specialised agent for generating T-SQL queries
for the Contoso Retail database.

Your ONLY responsibility is to receive a natural-language description
of a customer segment and generate a valid T-SQL query that returns
EXACTLY these columns:
- FirstName (customer first name)
- LastName (customer last name)
- PrimaryEmail (customer email address)
- FavoriteCategory (the product category in which the customer has spent the most money)

To determine FavoriteCategory, you must JOIN the orders, order lines,
and products tables, group by category, and select the one with the
highest total amount (SUM of LineTotal).

DATABASE STRUCTURE:
{dbStructure}

RULES:
1. ALWAYS return EXACTLY the 4 columns: FirstName, LastName, PrimaryEmail, FavoriteCategory.
2. Use appropriate JOINs between customer, orders, orderline, product and productcategory.
3. For FavoriteCategory, use a subquery or CTE that groups by category
	and selects the one with the highest spend (SUM(ol.LineTotal)).
4. Only include active customers (IsActive = 1).
5. Only include customers who have a non-null and non-empty PrimaryEmail.
6. Do NOT execute the query, just generate it.
7. Return ONLY the T-SQL code, without explanation, without markdown,
	without code blocks. Plain SQL only.
8. Always respond in Spanish if you need to add any SQL comment.
```

Design rationale:

- Explicitly restricting the columns reduces ambiguity in the output.
- Requiring plain SQL (no markdown) avoids ambiguity when chaining the output with Julie.
- Injecting `db-structure.txt` improves precision of joins and table names.

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
You are MarketingAgent, a specialised agent for creating personalised
marketing messages for Contoso Retail customers.

Your workflow is as follows:

1. You receive the full name of a customer and their favourite purchase category.
2. You use the Bing Search tool to look for recent or upcoming events
	related to that category. For example:
	- If the category is "Bikes", search for cycling events.
	- If the category is "Clothing", search for fashion events.
	- If the category is "Accessories", search for technology or lifestyle events.
	- If the category is "Components", search for engineering or manufacturing events.
3. From the search results, select the most relevant and current event.
4. Generate a brief and motivational marketing message (maximum 3 paragraphs) that:
	- Greets the customer by name.
	- Mentions the event found and why it is relevant to the customer.
	- Invites the customer to visit the Contoso Retail online catalogue
	  to find the best products in the category and be prepared
	  for the event.
	- Has a warm, enthusiastic, and professional tone.
	- Written in Spanish.

5. Return ONLY the text of the marketing message. No JSON, no metadata,
	no additional explanations. Just the message ready to send by email.

IMPORTANT: If you cannot find relevant events, generate a general message about
current trends in that category and invite the customer to explore Contoso
Retail's latest offerings.
```

Design rationale:

- Separating marketing into its own agent decouples creativity from the SQL logic.
- Bing grounding brings current context without "contaminating" Julie with web searches.
- Limiting the output format/structure facilitates subsequent consolidation into the campaign JSON.

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

`JulieAgent.cs` defines the main `workflow` agent that coordinates the other two agents with CSDL YAML.

Full instructions:

```text
You are Julie, the campaign planning and orchestration agent
for Contoso Retail.

Your responsibility is to coordinate the creation of personalised
marketing campaigns for specific customer segments.

When you receive a campaign request, follow these steps:

1. EXTRACTION: Analyse the user's prompt and extract the description
	of the customer segment. Summarise that description in a clear sentence.

2. SQL GENERATION: Invoke SqlAgent, passing it the segment description.
	SqlAgent will return a T-SQL query.

3. PERSONALISED MARKETING: Invoke
	MarketingAgent, passing it the customer's name and their favourite category.
	MarketingAgent will search for relevant events on Bing and generate a
	personalised message.

4. FINAL ORGANISATION: With all the generated messages, organise the
	result as a campaign JSON in the following format:

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
		"body": "Personalised marketing message"
	 }
  ]
}
```

RULES:
- The "subject" field must be an attractive and relevant email subject.
- The "body" field is the message that MarketingAgent generated for that customer.
- Always respond in Spanish.
- If a customer has no email, omit them from the result.
- Generate a descriptive name for the campaign based on the segment.
```

Design rationale:

- `workflow` was chosen because there is a dependent sequence of steps (SQL → marketing).
- Julie does not "guess" results: it delegates SQL generation and content creation to specialised sub-agents.
- Centralising the final output in Julie ensures a single consistent JSON format for external consumption.

## What does Program.cs do exactly?

`Program.cs` does not contain the campaign business logic; its role is operational:

1. Load `appsettings.json`.
2. Read `db-structure.txt`.
3. Download the OpenAPI spec from the Function App (if available).
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

With this, the local code is limited to orchestrating agent infrastructure; the workflow execution happens inside Foundry on each `CreateResponse(...)`.

> Note: `Program.cs` downloads the OpenAPI spec with retries to tolerate intermittent DNS failures; that spec is passed to `SqlAgent` to enable the `SqlExecutor` tool and execute SQL from the sub-agent.

## Recommended pattern applied in this lab

To maintain consistency and maintainability, this lab applies the following pattern:

1. **Typed definitions in code**
	- `SqlAgent` and `MarketingAgent` return `PromptAgentDefinition`.
	- `JulieOrchestrator` returns `WorkflowAgentDefinition`.

2. **Typed version creation**
	- Uses `CreateAgentVersionAsync(..., new AgentVersionCreationOptions(agentDefinition))`.

3. **Clear separation of concerns**
	- `Program.cs` creates/versions agents and opens a conversation.
	- Each agent class encapsulates its instructions and tools.

4. **Stable output contract**
	- Julie maintains a consistent final JSON output to facilitate consumption by other systems or automated validations.
