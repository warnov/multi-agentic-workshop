# Changelog

All notable engineering changes to this workshop are documented in this file, so
maintainers and future contributors have a historical record of *why* the code
and labs look the way they do. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

This file is intentionally **English-only and not duplicated** under `en/`, `es/`,
`pt/`: unlike the lab documents, it targets maintainers/architects rather than
workshop attendees.

## [V2-FY27] — 2026-09-23: Julie hosted agent + Toolbox/Web Search/MCP migration

This entry covers the branch `V2-FY27`, which evolves **Lab 4 (Julie)** and, as a
side effect, aligns **Lab 3 (Anders)** to the same current Foundry SDK surface.
Applied identically across `en/`, `es/`, `pt/`. On 2026-09-23 this branch was
merged into `master`, becoming the workshop's new main baseline.

### Added

- **Foundry Toolbox for `MarketingAgent`**: a `marketing-websearch-toolbox`
  Toolbox is created (or reused) at startup by `JulieAgent/Program.cs`, wrapping
  a single `WebSearchToolboxTool`. The Toolbox is addressed by `MarketingAgent`
  through its stable "consumer" MCP endpoint
  (`{project_endpoint}/toolboxes/{name}/mcp?api-version=v1`), which always
  resolves to the Toolbox's `default_version` — so promoting a new Toolbox
  version never requires touching or recompiling `MarketingAgent`.
- **`McpTool` + `AsProjectTool` wiring in `MarketingAgent`**: `MarketingAgent.cs`
  now builds its tool with `ResponseTool.CreateMcpTool(...)` (OpenAI SDK,
  `serverUri` = the Toolbox MCP endpoint, `NeverRequireApproval` policy) bridged
  into the agent definition via `ProjectsAgentTool.AsProjectTool(mcpTool)`. This
  is the documented pattern for **prompt agents** to consume an MCP server
  (remote or Toolbox-backed) without any `ProjectConnectionId`/API key.
- **RBAC auto-grant for the hosted agent identity**: after deploying `Julie` as
  a hosted agent, `Program.cs` resolves her `instance_identity.principal_id`
  and grants the **Foundry User** role (`53ca6127-db72-4b80-b1b0-d745d6d5456d`)
  at the **project** scope (account-scope alone was not honoured), so Julie can
  call `agents/read` against `SqlAgent`/`MarketingAgent`. The project ARM scope
  is now built explicitly from `SubscriptionId` + `ResourceGroupName` (see
  *Changed* below), since it can no longer be derived opportunistically from
  the removed Bing connection resource.
- **New conceptual documentation** in `lab04-julie-planner-agent*.md` (3
  languages): a full theory section on **Toolbox**, **Web Search**, and **MCP**
  — what a Toolbox is, why Web Search replaces Grounding with Bing Search, how
  MCP is the protocol connecting a prompt agent to a Toolbox, and how all three
  pieces wire together in `MarketingAgent`'s case. Includes a callout
  clarifying Web Search still incurs cost (same billing engine as Bing
  Grounding) and a note on **Web IQ** (still invitation-only access; expected
  to become the recommended option in the future), linking to
  `https://aka.ms/WebIQLearn`.
- **Additional setup/RBAC documentation**: `setup.md`/`readme.md` (3 languages)
  now document the extra permission needed to run Lab 4 — the user's own
  identity needs **Foundry Project Manager** (project scope) or **Owner** (RG
  scope) so the Julie deployer can create the RBAC assignment above; if it
  can't, the deployer prints the exact `az role assignment create` command
  instead of failing.

### Changed

- **`MarketingAgent`: Bing Grounding → Web Search via MCP.** The agent no
  longer uses `BingGroundingAgentTool`/`BingGroundingSearchConfiguration`; its
  definition type changed from `PromptAgentDefinition` to
  `DeclarativeAgentDefinition` to carry the MCP-backed tool.
- **`Julie`: promoted to a hosted agent.** `JulieAgent.cs` (the in-process
  `workflow` agent definition) was removed; `Julie` is now uploaded as source
  (`JulieHosted/`) and deployed/provisioned remotely by Foundry, running under
  its own Microsoft Entra identity (see RBAC above). `Program.cs` grew
  significantly (+346/-lines) to drive this: checks the existing agent's
  `kind`, deletes/recreates when it can't be changed in place, uploads
  `JulieHosted`, polls provisioning status, and routes the endpoint to the new
  version.
- **`AndersAgent`: consolidated onto the current Foundry SDK surface.** Removed
  the legacy `Azure.AI.Projects.OpenAI` / `Azure.AI.Agents.Persistent`-based
  variant; the remaining `ms-foundry` project now uses
  `Azure.AI.Projects.Agents` + `Azure.AI.Extensions.OpenAI` with GPT-5.1, and
  deletes-and-recreates the agent object on each run (rather than versioning
  it in place) so it is provisioned under the current object model and
  receives its own Entra agent identity, which Agent 365 registry sync depends
  on.
- **`appsettings.json` schema (`JulieAgent`)**: `BingConnectionName` replaced
  by `SubscriptionId`, `ResourceGroupName` (used only to grant Julie's RBAC —
  unrelated to `MarketingAgent`'s Web Search tool, which needs no connection or
  credentials) and `JulieDataMode` (`auto`/`demo`/`real` — controls the SQL
  fallback-to-demo-data behavior documented in Lab 4's *Data modes* section).
- **Infra (`main.bicep`, both `op-flex` and `op-consumption`)**: removed the
  `Microsoft.Bing/accounts` (Grounding with Bing Search) resource and its
  Foundry connection; added `subscriptionId`/`resourceGroupName` outputs used
  to seed the new `appsettings.json` fields.
- **Solution files** (`workshop-multi-agentic.sln` / `taller-multi-agentic.sln`):
  updated project references — single `AndersAgent` project (see *Removed*),
  and `JulieHosted` (`hosted.csproj`) replacing `JulieBackup`.
- **Required Azure permissions** to run the labs: an active subscription with
  **Owner**, or **Contributor** *together with* **User Access Administrator**
  (Contributor alone cannot create the Function App's role assignments); Lab 4
  additionally needs **Foundry Project Manager** (project scope) or RG-level
  **Owner** for Julie's RBAC auto-grant.

### Removed

- `AndersAgent/ai-foundry/` — obsolete `Azure.AI.Agents.Persistent`-based
  variant of Anders, superseded by the consolidated `ms-foundry/` project.
- `JulieAgent/JulieAgent.cs` — Julie's in-process `workflow` agent definition,
  no longer used now that Julie is a hosted agent (split responsibilities live
  in `Program.cs`, `SqlAgent.cs`, `MarketingAgent.cs`, and the uploaded
  `JulieHosted/` source).
- `JulieBackup/` — pre-migration backup copy of the old Julie project, no
  longer needed once the migration was validated end-to-end.
