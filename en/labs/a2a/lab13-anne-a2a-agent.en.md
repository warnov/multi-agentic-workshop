# Anne Agent (A2A) Lab

This workshop walks through building, running, securing, and deploying **Anne-Agent**:
an A2A-enabled assistant built with the Microsoft Agent Framework for Python and the
Microsoft 365 Agent SDK. The goal is to understand how a single agent is implemented
and made discoverable to other agents—not to build a complete production solution.

## Scenario

Anne is an enterprise assistant that answers natural-language questions and uses tools
when they can provide factual information. The starter agent includes two tools:
reporting the current time in an IANA time zone and counting weekdays between dates.
It can be called directly over A2A or added to an orchestrator such as Microsoft
Copilot Studio.

## Architecture

The solution has three main parts:

* **Reasoning:** The Microsoft Agent Framework agent uses an Azure OpenAI or Azure AI
  Foundry deployment and registered Python tools.
* **Agent service:** FastAPI hosts the A2A JSON-RPC endpoint and Agent Card. The card
  advertises the agent's skills, endpoint, and streaming capability.
* **Identity and hosting:** The Microsoft 365 Agent SDK validates inbound Entra ID
  tokens. Azure Container Apps hosts the service, with a managed identity available
  for model access and image pulls.

```text
Copilot Studio / A2A client
          │
          ├── GET  /.well-known/agent-card.json
          └── POST /a2a/v1
                    │
          Agent 365 authentication
                    │
          FastAPI → A2A executor → Anne → Azure OpenAI
                                  ├── get_current_time
                                  └── count_business_days
```

## Workshop flow

The labs are independent but build on one another. Complete them in order to go from
a local agent to a secured, discoverable cloud agent.

| Lab | Focus |
| --- | --- |
| 1 | Prepare the environment and run Anne locally |
| 2 | Explore the A2A interface and tools |
| 3 | Configure Entra ID authentication |
| 4 | Deploy to Azure Container Apps |
| 5 | Register the agent in Agent 365 |
| 6 | Connect the agent to Copilot Studio |

## Learning outcomes

By the end of the lab, participants will understand how to:

* Define an agent and callable tools with the Microsoft Agent Framework.
* Expose the agent through A2A and publish its capabilities in an Agent Card.
* Protect inbound A2A requests with Entra ID authentication.
* Deploy the service to Azure Container Apps and verify its health.
* Register an agent identity and connect the A2A agent to Copilot Studio.

## Prerequisites

Participants should have:

| Requirement | Purpose |
| --- | --- |
| Python 3.12 | Run the service and tests |
| Git | Get the repository |
| Azure CLI 2.60+ | Deploy Azure resources and manage identity |
| An Azure subscription | Host the Container App and supporting resources |
| An Azure OpenAI or Azure AI Foundry deployment | Provide the model endpoint and deployment name |
| Entra ID app registration | Required for authenticated A2A calls and Agent 365 onboarding |
| Microsoft Copilot Studio access | Required for Lab 6 |

Docker is optional; the deployment script uses Azure Container Registry build tasks.
For local development, either an Azure OpenAI API key or an Azure CLI sign-in with
permission to use the model is required.

## Lab 1 – Prepare and run Anne locally

### 1. Set up the Python environment

From the repository root:

```bash
python -m venv .venv
source .venv/bin/activate
pip install -r requirements-dev.txt
cp .env.sample .env
```

On Windows PowerShell, activate the environment with:

```powershell
.venv\Scripts\Activate.ps1
```

Edit `.env` and set `AZURE_OPENAI_ENDPOINT` and
`AZURE_OPENAI_DEPLOYMENT_NAME`. Set `AZURE_OPENAI_API_KEY` if using key-based
authentication; otherwise sign in with `az login` and ensure your identity can access
the model.

For an unauthenticated local development session, set these in `.env`:

```dotenv
AGENT365_ENABLED="false"
AGENT365_REQUIRE_AUTH="false"
```

Do not use this configuration for a public deployment.

### 2. Start the service and verify it

```bash
python main.py
```

In another terminal, check the health endpoint and retrieve the Agent Card:

```bash
curl http://localhost:8000/healthz
curl http://localhost:8000/.well-known/agent-card.json
```

The card should list Anne's general-assistance and date-and-time skills. The
pre-1.0-compatible alias is also available at
`/.well-known/agent.json`.

### 3. Try an A2A request

```bash
curl -X POST http://localhost:8000/a2a/v1 \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": "1",
    "method": "message/send",
    "params": {
      "message": {
        "role": "user",
        "messageId": "m1",
        "parts": [{"text": "What time is it in Tokyo?"}]
      }
    }
  }'
```

Try a second request, such as asking how many business days are between two dates.
The A2A message method is v0.3-compatible by default; the endpoint also supports the
A2A 1.0 method names.

### 4. Run the tests

```bash
pytest
```

The test suite uses a stub agent and does not make model calls.

## Lab 2 – Explore the agent and its tools

Inspect the key implementation files:

* `agent/agent.py` creates the chat client and agent.
* `agent/tools.py` defines the time and business-day tools and registers them in
  `AGENT_TOOLS`.
* `agent/a2a_server.py` defines the advertised skills and constructs the Agent Card
  and A2A routes.
* `agent/settings.py` reads service, model, A2A, and Agent 365 settings from the
  environment.

### Exercise: add a capability

Add a small, useful Python function to `agent/tools.py`, decorate it with `@tool`, and
add it to `AGENT_TOOLS`. Keep its type hints and docstring accurate: the framework
uses them to describe the tool to the model. Update `AGENT_SKILLS` in
`agent/a2a_server.py` so callers can discover the new capability in the Agent Card.

Restart the service, inspect the card, and test a request that should use the new
tool. Run `pytest` to check that existing behavior remains intact.

## Lab 3 – Configure Entra ID authentication

In the default configuration, Agent 365 integration and authentication are enabled.
For authenticated requests, the service expects a valid bearer token for the agent's
Entra application audience, issued by the configured tenant.

1. Follow the app-registration steps in [DEPLOYMENT.md](DEPLOYMENT.md#5-entra-id-app-registration-microsoft-agent-365)
   to create or identify the agent app registration.
2. Set `AGENT365_CLIENT_ID` to its application (client) ID and
   `AGENT365_TENANT_ID` to the issuing tenant ID in `.env`.
3. Keep `AGENT365_ENABLED` and `AGENT365_REQUIRE_AUTH` set to `true`.
4. Optionally set `AGENT365_AUTHORIZED_CALLERS` to a comma-separated allow-list of
   calling application IDs.
5. Restart the service and send an A2A request without a bearer token. It should
   receive `401 Unauthorized`. The Agent Card and health endpoints remain publicly
   readable for discovery and platform probes.

Use a valid token with the correct audience and tenant to test an authorized call.
Keep credentials out of source control. For more detail on app registration,
authentication, and caller restrictions, see
[DEPLOYMENT.md](DEPLOYMENT.md#5-entra-id-app-registration-microsoft-agent-365).

## Lab 4 – Deploy to Azure Container Apps

The `deploy.sh` script provisions or reuses an Azure resource group, ACR, Log
Analytics workspace, and Container Apps environment; builds the image remotely; and
deploys the app with external HTTPS ingress.

1. Sign in and select the subscription to use:

   ```bash
   az login
   az account set --subscription "<subscription-id>"
   ```

2. Provide the model endpoint and Entra application settings. The deployment script
   requires client and tenant IDs because Agent 365 authentication is enabled by
   default:

   ```bash
   export AZURE_OPENAI_ENDPOINT="https://<your-resource>.openai.azure.com"
   export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini"
   export AGENT365_CLIENT_ID="<agent-app-client-id>"
   export AGENT365_TENANT_ID="<tenant-id>"
   ```

   Set `AZURE_OPENAI_API_KEY` only if using API-key model authentication. Otherwise,
   grant the Container App's system-assigned managed identity the
   **Cognitive Services OpenAI User** role on the model resource after deployment.

3. Deploy:

   ```bash
   ./deploy.sh
   ```

   The script prints the resource names, Agent Card URL, A2A endpoint, and health
   URL. Use environment variables such as `RESOURCE_GROUP` and `LOCATION` to override
   defaults; see [DEPLOYMENT.md](DEPLOYMENT.md) for all deployment options and the
   Bicep alternative.

4. Verify the deployed service using the printed URLs:

   ```bash
   curl "https://<container-app-fqdn>/healthz"
   curl "https://<container-app-fqdn>/.well-known/agent-card.json"
   ```

   Requests to `/a2a/v1` must include a valid bearer token when authentication is
   enabled. Follow the detailed verification and role-assignment steps in
   [DEPLOYMENT.md](DEPLOYMENT.md#4-verifying-the-deployment).

Azure resources may incur charges. When finished, remove only the workshop resource
group if it is dedicated to this exercise:

```bash
az group delete --name rg-anne-agent --yes --no-wait
```

## Lab 5 – Register Anne in Agent 365

Agent 365 authentication and registry onboarding are separate steps. The service
validates inbound tokens and exposes identity metadata; the deployment does not
automatically create an inventory registration.

Use `register-agent365.sh` to register an existing Entra Agent Identity Blueprint
through the Microsoft Graph beta Agent Registry API:

1. Follow the current [Agent 365 CLI documentation](https://learn.microsoft.com/en-us/microsoft-agent-365/developer/agent-365-cli)
   to create an Agent Identity Blueprint.
2. Grant the registration client the required Microsoft Graph
   `AgentInstance.ReadWrite.All` permission and admin consent. The operator also
   needs the Agent Registry Administrator role.
3. Set the blueprint app ID, owner object ID, deployed Agent Card URL, and a
   suitable Graph access token as described in
   [DEPLOYMENT.md](DEPLOYMENT.md#register-in-the-agent-365-inventory).
4. Validate without making a registry change:

   ```bash
   ./register-agent365.sh --dry-run
   ```

5. When ready, run `./register-agent365.sh` to submit the registration.

The registry API is a preview API. Test in a non-production tenant first, and do not
commit tokens or registration output to the repository.

## Lab 6 – Connect Anne from Copilot Studio

1. In Copilot Studio, open an agent and choose **Agents → Add an agent → Connect to
   an existing agent (A2A)**.
2. Enter the deployed Agent Card URL:
   `https://<container-app-fqdn>/.well-known/agent-card.json`.
3. Configure Microsoft Entra ID authentication using the agent's client ID as the
   resource/scope (`api://<agent-client-id>/.default`). For restricted access, add
   the Copilot Studio application ID to `AGENT365_AUTHORIZED_CALLERS`.
4. Save and test by asking a question that uses one of Anne's tools.

Copilot Studio reads the card to discover the skills, streaming capability, and A2A
endpoint. The inventory registration from Lab 5 does not replace the Agent Card URL
when connecting this A2A service. See
[DEPLOYMENT.md](DEPLOYMENT.md#6-connecting-from-microsoft-copilot-studio) for details.

## Further reading

* [Project overview and local usage](README.md)
* [Deployment, Agent 365, and Copilot Studio guide](DEPLOYMENT.md)
* [Build an A2A-enabled agent prompt](.github/prompts/build-a2a-agent365-agent.prompt.md)
