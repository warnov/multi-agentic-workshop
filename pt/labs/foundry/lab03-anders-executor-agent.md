# Lab 3: Anders — Agente Executor

## Índice

- [Lab 3: Anders — Agente Executor](#lab-3-anders--agente-executor)
  - [Índice](#índice)
  - [Introdução](#introdução)
    - [O que vamos fazer neste lab?](#o-que-vamos-fazer-neste-lab)
    - [Pré-requisitos](#pré-requisitos)
      - [Ferramentas na sua máquina](#ferramentas-na-sua-máquina)
      - [Infraestrutura Azure](#infraestrutura-azure)
      - [Permissões RBAC](#permissões-rbac)
  - [3.1 — Verificar suporte OpenAPI (já vem pré-configurado)](#31--verificar-suporte-openapi-já-vem-pré-configurado)
    - [Checklist rápido de validação](#checklist-rápido-de-validação)
    - [Passo 1: Verificar pacotes NuGet](#passo-1-verificar-pacotes-nuget)
    - [Passo 2: Verificar endpoints expostos](#passo-2-verificar-endpoints-expostos)
    - [Passo 3: Verificar compilação](#passo-3-verificar-compilação)
    - [Endpoints OpenAPI gerados](#endpoints-openapi-gerados)
  - [3.2 — Reimplantar a Function App](#32--reimplantar-a-function-app)
    - [Como obter `FabricWarehouseSqlEndpoint` e `FabricWarehouseDatabase`?](#como-obter-fabricwarehousesqlendpoint-e-fabricwarehousedatabase)
    - [Opção 0: Reexecutar setup de infraestrutura (se precisar atualizar settings)](#opção-0-reexecutar-setup-de-infraestrutura-se-precisar-atualizar-settings)
    - [Opção A: Usando Azure Functions Core Tools (recomendada)](#opção-a-usando-azure-functions-core-tools-recomendada)
    - [Opção B: Usando Azure CLI](#opção-b-usando-azure-cli)
  - [3.3 — Verificar a especificação OpenAPI](#33--verificar-a-especificação-openapi)
    - [Obter a especificação JSON](#obter-a-especificação-json)
    - [Explorar o Swagger UI](#explorar-o-swagger-ui)
  - [3.4 — O agente Anders: Duas versões de SDK](#34--o-agente-anders-duas-versões-de-sdk)
    - [Por que duas versões?](#por-que-duas-versões)
    - [Qual versão devo usar?](#qual-versão-devo-usar)
    - [Entendendo o código (versão `ms-foundry/` — recomendada)](#entendendo-o-código-versão-ms-foundry--recomendada)
      - [Fase 1 — Baixar a especificação OpenAPI](#fase-1--baixar-a-especificação-openapi)
      - [Fase 2 — Verificar agente existente ou criar um novo](#fase-2--verificar-agente-existente-ou-criar-um-novo)
      - [Fase 3 — Chat interativo com Responses API](#fase-3--chat-interativo-com-responses-api)
    - [Passo 1: Configurar `appsettings.json`](#passo-1-configurar-appsettingsjson)
    - [Passo 2: Compilar e executar](#passo-2-compilar-e-executar)
    - [Passo 3: Inspecionar o agente no Microsoft Foundry](#passo-3-inspecionar-o-agente-no-microsoft-foundry)
    - [Passo 4: Testar o agente](#passo-4-testar-o-agente)
  - [Solução de problemas](#solução-de-problemas)
    - [Storage Account bloqueado por política (erro 503)](#storage-account-bloqueado-por-política-erro-503)
  - [Próximo passo](#próximo-passo)

---

## Introdução

Anders é o **agente executor** da arquitetura multi-agêntica da Contoso Retail. Seu papel é receber solicitações de ações operacionais — como a geração e publicação de relatórios de pedidos — e executá-las interagindo com serviços externos como a Azure Function `FxContosoRetail`.

Para que Anders possa interagir com a API da Contoso Retail, definiremos uma **OpenAPI Tool** que permite ao agente descobrir e invocar automaticamente os endpoints da Function App a partir de sua especificação OpenAPI. Adicionalmente, adicionaremos suporte **OpenAPI** à Function App para documentar a API e facilitar a exploração de seus endpoints.

### O que vamos fazer neste lab?

| Passo | Descrição |
|-------|-----------|
| **3.1** | Adicionar suporte OpenAPI à Azure Function `FxContosoRetail` |
| **3.2** | Reimplantar a Function App com as alterações |
| **3.3** | Verificar a especificação OpenAPI |
| **3.4** | Entender, configurar, executar e testar o agente Anders |

### Pré-requisitos

#### Ferramentas na sua máquina

| Ferramenta | Descrição | Download |
|------------|-----------|----------|
| **.NET 8 SDK** | Compilar e executar a Function App e o agente Anders | [Baixar](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Azure CLI** | Autenticar no Azure, implantar recursos e atribuir roles RBAC | [Instalar](https://learn.microsoft.com/cli/azure/install-azure-cli) |
| **Azure Functions Core Tools** | Publicar a Function App no Azure (opção recomendada) | [Instalar](https://learn.microsoft.com/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools) |
| **PowerShell 7+** | Executar scripts de implantação. **Requerido em todos os SOs** (incluindo Windows). Não use PowerShell 5.1. | [Instalar](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) · Windows: `winget install Microsoft.PowerShell` |
| **Git** | Clonar o repositório do workshop | [Baixar](https://git-scm.com/downloads) |

> [!IMPORTANT]
> **Windows:** Após instalar o PowerShell 7, configure a ExecutionPolicy executando **uma vez** no `pwsh`:
> ```powershell
> Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
> ```

> [!TIP]
> No **macOS**, você pode instalar as ferramentas com Homebrew:
> ```bash
> brew install dotnet-sdk azure-cli azure-functions-core-tools@4 powershell git
> ```

> [!TIP]
> No **Linux** (Ubuntu/Debian), você pode instalar o PowerShell 7 com:
> ```bash
> sudo apt-get update && sudo apt-get install -y wget apt-transport-https software-properties-common
> wget -q "https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb"
> sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
> sudo apt-get update && sudo apt-get install -y powershell
> ```
> Ver: [Instalar PowerShell no Linux](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux)

#### Infraestrutura Azure

- Ter concluído o **setup de infraestrutura** descrito no [Setup do Foundry](README.md)
- Ter anotados **todos os valores gerados na implantação** da infraestrutura (nomes de recursos, URLs, sufixo, endpoint do AI Foundry, etc.)
- Ter identificados estes 2 valores do Warehouse do Fabric (usados no setup atualizado):
    - `FabricWarehouseSqlEndpoint`
    - `FabricWarehouseDatabase`

#### Permissões RBAC

Seu usuário precisa do role **Cognitive Services User** sobre o recurso de AI Services para poder criar e executar agentes. Como seu usuário é **Owner do tenant**, você pode atribuir o role a si mesmo.

Execute os seguintes comandos (substitua `{suffix}` pelo seu sufixo de 5 caracteres):

```powershell
# 1. Obter seu nome de usuário (UPN)
$upn = az account show --query "user.name" -o tsv

# 2. Obter o nome do recurso de AI Services (se não se lembrar)
az cognitiveservices account list --resource-group rg-contoso-retail --query "[].name" -o tsv

# 3. Atribuir o role Cognitive Services User
az role assignment create `
    --assignee $upn `
    --role "Cognitive Services User" `
    --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/ais-contosoretail-{suffix}"
```

> **Nota:** A propagação do RBAC pode levar até 1 minuto. Aguarde antes de continuar com o lab.

---

## 3.1 — Verificar suporte OpenAPI (já vem pré-configurado)

Na versão atual do workshop, a Function App `FxContosoRetail` **já inclui** OpenAPI e endpoints decorados na base de código. Neste passo você não vai implementar OpenAPI do zero: apenas validar que tudo está correto antes da implantação.

### Checklist rápido de validação

### Passo 1: Verificar pacotes NuGet

Abra `FxContosoRetail.csproj` e confirme que existem estas referências:

- `Microsoft.Azure.Functions.Worker.Extensions.OpenApi`
- `Microsoft.Data.SqlClient`

### Passo 2: Verificar endpoints expostos

Abra `FxContosoRetail.cs` e confirme que existem estes endpoints:

- `OlaMundo`
- `OrdersReporter`
- `SqlExecutor`

Além disso, valide que `OrdersReporter` e `SqlExecutor` possuam atributos OpenAPI (`OpenApiOperation`, `OpenApiRequestBody`, `OpenApiResponseWithBody`).

### Passo 3: Verificar compilação

```powershell
cd labs\foundry\code\api\FxContosoRetail
dotnet build
```

Se compilar sem erros, você pode prosseguir para a reimplantação no passo 3.2.

> **Nota:** O OpenAPI já está registrado no projeto. Você não precisa adicionar pacotes nem modificar `Program.cs` neste lab.

> [!IMPORTANT]
> **Sobre a autenticação dos endpoints**
>
> Neste workshop usamos `AuthorizationLevel.Anonymous` para simplificar a configuração e permitir que o Microsoft Foundry possa invocar a Function App diretamente como OpenAPI Tool sem necessidade de gerenciar secrets nem configurar autenticação adicional.
>
> **Em um ambiente de produção, isso não é recomendável.** A prática correta é proteger a Function App com **Azure Entra ID (Easy Auth)** e fazer com que o Foundry se autentique usando **Managed Identity**. O fluxo seria:
>
> 1. **Registrar um aplicativo no Entra ID** que represente a Function App, obtendo um Application (client) ID e um Application ID URI (por exemplo, `api://<client-id>`).
> 2. **Habilitar Easy Auth** na Function App com `az webapp auth update`, configurando-a para validar tokens emitidos pelo Entra ID contra o app registration. Isso protege todos os endpoints no nível de plataforma — requisições sem um bearer token válido são rejeitadas com 401 antes de chegar ao código.
> 3. **Atribuir permissões à Managed Identity** do recurso de AI Services (`ais-contosoretail-{suffix}`) como principal autorizado no app registration, seja adicionando-a como membro de um app role ou como identidade permitida na configuração do Easy Auth.
> 4. **Usar `OpenApiManagedAuthDetails`** no código do agente em vez de `OpenApiAnonymousAuthDetails`, especificando o audience do app registration:
>    ```csharp
>    openApiAuthentication: new OpenApiManagedAuthDetails(
>        audience: "api://<app-registration-client-id>")
>    ```
>
> Com essa configuração, quando o Foundry precisa chamar a Function App, ele obtém um token do Entra ID usando a managed identity do recurso de AI Services, envia-o como `Authorization: Bearer <token>`, e o Easy Auth valida automaticamente. Os endpoints da Function podem manter `AuthorizationLevel.Anonymous` no código C# porque a autenticação ocorre na camada de plataforma.

### Endpoints OpenAPI gerados

Uma vez implantada, a Function App exporá estes endpoints adicionais:

| Endpoint | Descrição |
|----------|-----------|
| `/api/openapi/v3.json` | Especificação OpenAPI 3.0 em formato JSON |
| `/api/swagger/ui` | Interface Swagger UI interativa |

---

## 3.2 — Reimplantar a Function App

A infraestrutura já está implantada desde o setup inicial. Você só precisa **publicar o código atualizado** da Function App.

> [!IMPORTANT]
> O setup de infraestrutura atualizado (`op-flex/deploy.ps1` e `op-consumption/deploy.ps1`) aceita estes parâmetros para configurar o SQL do Lab 4:
> - `FabricWarehouseSqlEndpoint`
> - `FabricWarehouseDatabase`
>
> Se não forem fornecidos, a implantação continua e apenas omite a configuração automática do app setting `FabricWarehouseConnectionString`.

### Como obter `FabricWarehouseSqlEndpoint` e `FabricWarehouseDatabase`?

No Fabric, abra seu **Warehouse** e copie a **connection string** (SQL). Você verá algo semelhante a:

```text
Data Source=kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com,1433;Initial Catalog=retail_sqldatabase_xxx;... 
```

Mapeamento de valores:

- `FabricWarehouseSqlEndpoint` = valor de `Data Source` **sem** `,1433`
    - Exemplo: `kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com`
- `FabricWarehouseDatabase` = valor de `Initial Catalog`
    - Exemplo: `retail_sqldatabase_xxx`

> [!TIP]
> Esses valores são obtidos do ambiente de **Fabric implantado no Lab 1** (`../fabric/lab01-data-setup.md`).
>
> Se você não estiver seguindo a sequência completa de laboratórios, neste lab precisamos apenas de um banco SQL para executar consultas. Você pode usar um banco SQL standalone (por exemplo Azure SQL Database) e ajustar a conexão:
> - `FabricWarehouseSqlEndpoint` pelo host SQL do seu banco standalone
> - `FabricWarehouseDatabase` pelo nome do seu banco
>
> Nesse cenário, certifique-se também de configurar permissões da identidade da Function App sobre esse banco.

### Opção 0: Reexecutar setup de infraestrutura (se precisar atualizar settings)

Se quiser redeploy completo (infra + publish) usando o setup:

```powershell
# Flex Consumption
cd labs\foundry\setup\op-flex
.\deploy.ps1 `
    -TenantName "<seu-tenant>" `
    -ResourceGroupName "rg-contoso-retail" `
    -Location "eastus" `
    -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" `
    -FabricWarehouseDatabase "<database-warehouse>"
```

```powershell
# Consumption (Y1)
cd labs\foundry\setup\op-consumption
.\deploy.ps1 `
    -TenantName "<seu-tenant>" `
    -ResourceGroupName "rg-contoso-retail" `
    -Location "eastus" `
    -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" `
    -FabricWarehouseDatabase "<database-warehouse>"
```

> Se você apenas alterou o código da Function App e não precisa tocar na infraestrutura, use a Opção A ou Opção B abaixo.

### Opção A: Usando Azure Functions Core Tools (recomendada)

Se você tem o [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools) instalado, a reimplantação é um único comando:

```powershell
cd labs\foundry\code\api\FxContosoRetail
func azure functionapp publish func-contosoretail-<suffix>
```

> Substitua `<suffix>` pelo sufixo de 5 caracteres obtido durante o setup (por exemplo, `func-contosoretail-a1b2c`).

### Opção B: Usando Azure CLI

Se você não tem o `func` CLI, pode publicar manualmente com `az`:

```powershell
# 1. Compilar o projeto
cd labs\foundry\code\api\FxContosoRetail
dotnet publish --configuration Release --output bin\publish

# 2. Criar o pacote zip
Compress-Archive -Path "bin\publish\*" -DestinationPath "$env:TEMP\fxcontosoretail.zip" -Force

# 3. Implantar no Azure
az functionapp deployment source config-zip `
    --resource-group rg-contoso-retail `
    --name func-contosoretail-<suffix> `
    --src "$env:TEMP\fxcontosoretail.zip"

# 4. Limpar arquivos temporários
Remove-Item "$env:TEMP\fxcontosoretail.zip" -Force
Remove-Item "bin\publish" -Recurse -Force
```

---

## 3.3 — Verificar a especificação OpenAPI

Uma vez implantada, verifique que os endpoints OpenAPI estão disponíveis.

### Obter a especificação JSON

Abra no navegador ou com `curl`:

```
https://func-contosoretail-<suffix>.azurewebsites.net/api/openapi/v3.json
```

Você deverá ver um JSON com a estrutura OpenAPI que descreve os endpoints `OlaMundo`, `OrdersReporter` e `SqlExecutor`, incluindo os schemas de request/response.

### Explorar o Swagger UI

Navegue para:

```
https://func-contosoretail-<suffix>.azurewebsites.net/api/swagger/ui
```

Na interface do Swagger UI você pode explorar os endpoints e testá-los interativamente.

> **Importante:** A especificação OpenAPI documenta a API e serve como referência para entender quais parâmetros enviar e qual resposta esperar. O agente Anders usará essa informação indiretamente por meio da Function Tool que definiremos no próximo passo.

---

## 3.4 — O agente Anders: Duas versões de SDK

A implementação do agente Anders é fornecida em **duas versões separadas**, cada uma localizada em `labs/foundry/code/agents/AndersAgent/`:

| Pasta | SDK | Paradigma de API | Status |
|-------|-----|------------------|--------|
| `ai-foundry/` | `Azure.AI.Projects` + `Azure.AI.Agents.Persistent` | Persistent Agents (threads, runs, polling) | GA — mantido por retrocompatibilidade |
| `ms-foundry/` | `Azure.AI.Projects` + `Azure.AI.Projects.OpenAI` | Responses API (conversações, respostas de projeto) | **Preview** (fevereiro 2026) — **recomendada** |

### Por que duas versões?

No final de 2025, a Microsoft introduziu uma **nova experiência para o Microsoft Foundry** baseada na **Responses API** e uma superfície de gerenciamento de agentes redesenhada. Essa nova experiência — exposta por meio do pacote `Azure.AI.Projects.OpenAI` — substitui o modelo anterior de Persistent Agents (`Azure.AI.Agents.Persistent`) com uma abordagem mais ágil que utiliza **agentes com nome e versionamento**, **conversações** e a **Responses API** em vez de threads e runs com polling.

As diferenças principais entre as duas abordagens são:

| Aspecto | `ai-foundry/` (Persistent Agents) | `ms-foundry/` (Responses API) |
|---------|-----------------------------------|-------------------------------|
| **Ciclo de vida do agente** | Criado com um ID gerado; buscado por nome iterando a lista | Criado/atualizado por nome com versionamento explícito (`CreateAgentVersionAsync`) |
| **Modelo de conversação** | `PersistentAgentThread` + `ThreadRun` com polling | `ProjectConversation` + `ProjectResponsesClient` — resposta síncrona |
| **Definição de ferramentas** | `OpenApiToolDefinition` com classes tipadas | Protocol method via `BinaryContent` (os tipos são internos no SDK 1.2.x) |
| **Padrão de chat** | Criar run → fazer polling até concluir → ler mensagens | Uma única chamada a `CreateResponse()` retorna a saída diretamente |

### Qual versão devo usar?

**Recomenda-se a versão `ms-foundry/`** para desenvolvimento novo. Está alinhada com a direção da plataforma Microsoft Foundry e oferece um modelo de programação mais simples — particularmente a eliminação do loop de polling em favor de uma única chamada síncrona de resposta.

A versão `ai-foundry/` é mantida neste workshop por **retrocompatibilidade**: participantes cujos recursos de Azure AI Services foram provisionados antes de a nova experiência estar disponível podem concluir o lab usando a API de Persistent Agents.

> [!IMPORTANT]
> Em fevereiro de 2026, o pacote `Azure.AI.Projects.OpenAI` e a Responses API estão em **preview pública**. As formas da API, schemas de payload e tipos do SDK podem mudar antes de atingir disponibilidade geral (GA). Se você encontrar problemas como propriedades ausentes ou renomeadas (por exemplo, o campo `kind` exigido no payload de definição do agente), consulte as últimas [notas de versão de Azure.AI.Projects.OpenAI](https://www.nuget.org/packages/Azure.AI.Projects.OpenAI) para conhecer as mudanças que quebram compatibilidade.

---

### Entendendo o código (versão `ms-foundry/` — recomendada)

Abra o arquivo `labs/foundry/code/agents/AndersAgent/ms-foundry/Program.cs` e observe que está organizado em **3 fases**:

#### Fase 1 — Baixar a especificação OpenAPI

```csharp
var openApiSpecUrl = $"{functionAppBaseUrl}/openapi/v3.json";
var openApiSpec = await httpClient.GetStringAsync(openApiSpecUrl);
```

O programa baixa a especificação OpenAPI da Function App **em tempo de execução**. Isso significa que, se a API mudar (novos endpoints, novos parâmetros), o agente detecta automaticamente ao reiniciar.

#### Fase 2 — Verificar agente existente ou criar um novo

Esta fase tem duas partes principais:

**Detecção de agente existente:**

Antes de criar uma nova versão, o programa verifica se o agente já existe chamando `GetAgent`. Se o encontrar, pergunta ao usuário se deseja manter o agente existente ou sobrescrevê-lo com uma nova versão. Isso evita a proliferação desnecessária de versões do agente durante o desenvolvimento iterativo.

**Definição do agente com ferramenta OpenAPI (protocol method):**

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
                    description = "API da Contoso Retail...",
                    spec = openApiSpecJson,
                    auth = new { type = "anonymous" }
                }
            }
        }
    }
};
```

Como os tipos `OpenApiAgentTool` são internos no SDK 1.2.x, a definição da ferramenta é construída como um objeto anônimo e serializada via `BinaryContent`. O campo `kind = "prompt"` é exigido pela API para indicar um agente baseado em prompt.

**System prompt (instruções):**

O system prompt inclui o schema JSON exato que Anders deve construir ao invocar a API:

```json
{
  "customerName": "Nome do Cliente",
  "startDate": "YYYY-MM-DD",
  "endDate": "YYYY-MM-DD",
  "orders": [
    {
      "orderNumber": "código do pedido",
      "orderDate": "YYYY-MM-DD",
      "orderLineNumber": 1,
      "productName": "nome do produto",
      "brandName": "nome da marca",
      "categoryName": "nome da categoria",
      "quantity": 1.0,
      "unitPrice": 0.00,
      "lineTotal": 0.00
    }
  ]
}
```

> [!TIP]
> Incluir o schema nas instruções é uma boa prática quando o agente precisa construir payloads complexos. Embora a especificação OpenAPI já descreva o schema, reforçá-lo no system prompt reduz significativamente os erros de formato.

**Reutilização do agente:**

```csharp
try
{
    existingAgent = projectClient.Agents.GetAgent(agentName);
    // Pergunta ao usuário se deseja sobrescrever ou manter
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    // Agente não encontrado — criar um novo
}
```

Antes de criar uma nova versão do agente, o programa tenta recuperar o agente existente por nome. Se o encontrar, pede ao usuário que confirme se deseja sobrescrevê-lo. Isso evita criar versões desnecessárias no Foundry ao reiniciar a aplicação.

#### Fase 3 — Chat interativo com Responses API

```csharp
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: agentName,
    defaultConversationId: conversation.Id);

ResponseResult response = responseClient.CreateResponse(input);
Console.WriteLine(response.GetOutputText());
```

O padrão de interação na versão `ms-foundry/` é mais simples do que a abordagem de Persistent Agents:
1. É criada uma `ProjectConversation` (o contexto de conversação)
2. É obtido um `ProjectResponsesClient`, vinculado ao agente e à conversação
3. Cada mensagem do usuário é enviada via `CreateResponse()`, que retorna a saída **sincronamente** — sem necessidade de loop de polling
4. O texto de resposta é extraído com `GetOutputText()`

> **O que acontece durante uma chamada de resposta?** Quando o modelo decide que precisa chamar a API, o Foundry executa a chamada HTTP automaticamente usando a especificação OpenAPI. O resultado é enviado de volta ao modelo, que formula a resposta final ao usuário. Tudo isso ocorre dentro da única chamada a `CreateResponse()` — o código simplesmente recebe a resposta concluída.

**Limpeza ao sair:**

Quando o usuário digita `sair`, o loop de chat termina. O agente **persiste** no Foundry e é reutilizado automaticamente na próxima execução.

### Passo 1: Configurar `appsettings.json`

Abra o arquivo `labs/foundry/code/agents/AndersAgent/ms-foundry/appsettings.json` e substitua os valores pelos do seu ambiente:

```json
{
  "FoundryProjectEndpoint": "<SEU-AI-FOUNDRY-PROJECT-ENDPOINT>",
  "ModelDeploymentName": "gpt-4.1",
  "FunctionAppBaseUrl": "https://func-contosoretail-<suffix>.azurewebsites.net/api"
}
```

> **Onde encontro esses valores?**
> - **FoundryProjectEndpoint**: O `AI Foundry Endpoint` da saída da implantação.
> - **ModelDeploymentName**: `gpt-4.1` (nome do deployment criado pelo Bicep).
> - **FunctionAppBaseUrl**: A URL da sua Function App + `/api`.

### Passo 2: Compilar e executar

```powershell
cd labs\foundry\code\agents\AndersAgent\ms-foundry
dotnet build
```

Certifique-se de que não há erros de compilação. Em seguida, execute:

```powershell
dotnet run
```

Você verá no console que o agente verifica se já existe uma versão no Foundry. Se a encontrar, perguntará se deseja mantê-la ou sobrescrevê-la. Se não existir, um novo agente é criado automaticamente.

### Passo 3: Inspecionar o agente no Microsoft Foundry

**Antes de interagir com Anders**, acesse o portal para inspecionar o que foi criado:

1. Abra o [Microsoft Foundry](https://ai.azure.com) e navegue até seu projeto
2. No menu lateral, selecione **Agents**
3. Procure o agente **"Anders"** e clique nele

Observe dois pontos importantes:

- **System prompt (instruções):** Você verá as instruções completas que fornecemos ao agente, incluindo o schema JSON. Isso é o que guia seu comportamento ao decidir quando e como invocar a API.
- **Tools (ferramentas):** Você verá **ContosoRetailAPI** listada como ferramenta OpenAPI. Você pode expandi-la para ver a especificação completa com o endpoint `ordersReporter`, os schemas de request/response e a configuração de autenticação anônima.

> [!TIP]
> O system prompt e as tools são os dois pilares que determinam o que um agente pode fazer e como ele o faz. Entender essa relação é fundamental para projetar agentes eficazes.

### Passo 4: Testar o agente

De volta ao console, teste primeiro com uma saudação:

```
Você: Olá Anders, o que você pode fazer?
```

Anders deve responder explicando que pode gerar relatórios de pedidos. Em seguida, teste com dados reais (cole tudo em uma única linha):

```
Você: Gere um relatório para Izabella Celma (período: 1-31 janeiro 2026). Pedido ORD-CID-069-001 (2026-01-04): Sport-100 Helmet Black, Contoso Outdoor, Helmets, 6x$34.99=$209.94 | HL Road Frame Red 62, Contoso Outdoor, Road Frames, 10x$1431.50=$14315.00 | Long-Sleeve Logo Jersey S, Contoso Outdoor, Jerseys, 8x$49.99=$399.92. Pedido ORD-CID-069-003 (2026-01-08): HL Road Frame Black 58, Contoso Outdoor, Road Frames, 3x$1431.50=$4294.50 | HL Road Frame Red 44, Contoso Outdoor, Road Frames, 7x$1431.50=$10020.50. Pedido ORD-CID-069-002 (2026-01-17): HL Road Frame Red 62, Contoso Outdoor, Road Frames, 2x$1431.50=$2863.00 | LL Road Frame Black 60, Contoso Outdoor, Road Frames, 4x$337.22=$1348.88.
```

O que acontece internamente:
1. Anders analisa a mensagem e decide que precisa chamar o endpoint `ordersReporter`
2. **O Foundry executa a chamada HTTP** automaticamente para a Function App com os dados estruturados conforme o schema
3. A Function App gera o relatório HTML, envia para o Blob Storage e retorna a URL
4. O Foundry envia o resultado de volta ao modelo
5. Anders formula sua resposta e apresenta a URL ao usuário

Abra a URL do relatório no navegador para verificar que foi gerado corretamente.

Agora teste com um caso mais simples — um único pedido com dois produtos:

```
Você: Gere um relatório para Marco Rivera (período: 5-10 fevereiro 2026). Pedido ORD-CID-112-001 (2026-02-07): Mountain Bike Socks M, Contoso Outdoor, Socks, 3x$9.50=$28.50 | Water Bottle 30oz, Contoso Outdoor, Bottles and Cages, 1x$6.99=$6.99.
```

> **Nota:** Ao digitar `sair`, apenas a conversa é encerrada. O agente **persiste** no Foundry e é reutilizado automaticamente na próxima execução.

---

## Solução de problemas

### Storage Account bloqueado por política (erro 503)

Em assinaturas com políticas rígidas do Azure, o Storage Account que suporta a Function App pode ter seu **acesso público de rede desabilitado** automaticamente após o provisionamento. Isso impede que o host do Functions acesse seu próprio armazenamento, causando um erro persistente **503 (Site Unavailable)** — mesmo que o app apareça como `Running` e `Enabled`.

**Sintomas:**
- A Function App aparece como `Running` no Portal do Azure e CLI
- Todas as restrições de acesso de rede mostram "Allow all"
- Cada requisição HTTP para qualquer endpoint retorna 503 após um timeout de ~60 segundos

**Diagnóstico:**
```powershell
az storage account show --name stcontosoretail<suffix> --resource-group rg-contoso-retail --query "publicNetworkAccess" -o tsv
```

Se retornar `Disabled`, essa é a causa raiz.

**Solução:**

Um script de conveniência está incluído no repositório:

```powershell
cd labs/foundry/setup
.\unlock-storage.ps1
```

O script detecta automaticamente o sufixo a partir da Function App. Se precisar forçar, também aceita `-Suffix` ou `-FunctionAppName`.

Este script habilita o acesso público de rede no Storage Account e reinicia a Function App. Consulte [unlock-storage.ps1](setup/unlock-storage.ps1) para detalhes.

---

## Próximo passo

Continue com o [Lab 4 — Julie (Agente Planejador)](lab04-julie-planner-agent.md).
