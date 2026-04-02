# Microsoft Foundry — Workshop Multi-Agêntico

## Introdução

Esta seção do workshop cobre a **camada de raciocínio e execução** da arquitetura multi-agêntica da Contoso Retail, implementada sobre o **Microsoft Foundry**. Aqui são construídos os agentes inteligentes que interpretam dados e planejam ações (executando algumas delas), a partir das informações geradas pela camada de dados (Microsoft Fabric).

### Agentes desta camada

| Agente | Papel | Descrição |
|--------|-------|-----------|
| **Anders** | Executor Agent | Recebe solicitações de ações operacionais (como a geração de relatórios ou renderização de pedidos) e as executa interagindo com serviços externos como a Azure Function `FxContosoRetail`. Tipo: `kind: "prompt"` com ferramenta OpenAPI. |
| **Julie** | Planner Workflow | Orquestra campanhas de marketing personalizadas. Recebe uma descrição de segmento de clientes e executa um fluxo de 5 etapas: (1) extrai o filtro de clientes, (2) invoca o **SqlAgent** para gerar T-SQL, (3) executa a consulta contra o Fabric via **Function App OpenAPI**, (4) invoca o **MarketingAgent** (com Bing Search) para gerar mensagens por cliente, (5) organiza o resultado como JSON de campanha de e-mails. Tipo: `kind: "workflow"` com 3 ferramentas (2 agentes + 1 OpenAPI). |

### Arquitetura geral

A camada Foundry se localiza no centro da arquitetura de três camadas:

```
┌─────────────────────┐
│   Copilot Studio    │  ← Camada de interação (Charles, Bill, Ric)
├─────────────────────┤
│  Microsoft Foundry  │  ← Camada de raciocínio (Anders, Julie) ★
├─────────────────────┤
│  Microsoft Fabric   │  ← Camada de dados (Mark, Amy)
└─────────────────────┘
```

Os agentes Anders e Julie utilizam modelos GPT-4.1 implantados no Azure AI Services para raciocinar sobre as informações do negócio. Anders consome diretamente a API do `FxContosoRetail` via ferramenta OpenAPI. Julie orquestra um workflow multi-agente: usa o **SqlAgent** (gera T-SQL), uma **Function App** (executa o SQL contra o Fabric via OpenAPI) e o **MarketingAgent** (gera mensagens personalizadas com Bing Search), coordenando tudo de forma autônoma como um agente do tipo `workflow`.

---

## Setup de infraestrutura

Antes de iniciar os laboratórios, cada participante precisa implantar a infraestrutura do Azure em sua própria assinatura. O processo é automatizado com Bicep e um script PowerShell.

### Pré-requisitos

- **Azure CLI** instalado e atualizado ([instalar](https://aka.ms/installazurecli))

- **.NET 8 SDK** instalado ([baixar](https://dotnet.microsoft.com/download/dotnet/8.0))

- **PowerShell 7+** (necessário em todos os sistemas operacionais, incluindo Windows)
  - Windows: `winget install Microsoft.PowerShell` ou [baixar MSI](https://aka.ms/powershell-release?tag=stable)
  - Linux: [instruções](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux)
  - macOS: `brew install powershell/tap/powershell` ou [instruções](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-macos)
  > ⚠️ **Importante:** Execute os scripts no `pwsh` (PowerShell 7), **não** no `powershell` (5.1). O PowerShell 5.1 não é compatível.
  
- **ExecutionPolicy** configurada (somente Windows): Para executar scripts provenientes de uma origem como o GitHub, é necessário habilitar esta opção. Para isso, abra o `pwsh` como administrador e execute:
  
  ```powershell
  Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
  ```
  
  ✅ Isso só é necessário uma vez.
  
- Uma **assinatura do Azure** ativa com permissões de Owner ou Contributor

   - Quando seu tenant estiver pronto para trabalhar, anote o **número do tenant temporário** atribuído: se o usuário atribuído for usuario@azurehol3387.com, então seu número de tenant é 3387.

- Os valores de conexão e banco de dados no Microsoft Fabric. Para obtê-los, siga [este](./setup/sql-parameters.md) guia.


### ↗️ Implantação

Para implantar os elementos necessários nestes laboratórios, preparamos scripts com Bicep e PowerShell que permitem automatizar o processo sem precisar acessar manualmente o portal do Azure ou do Foundry para criar recursos. 

Esses scripts podem ser executados nas nossas máquinas locais. Mas, para poder executar ações, precisamos autenticar nosso processo local com o Azure para obter as permissões necessárias. Portanto, devemos começar autenticando no Azure pelo terminal.

1. **Abrir um terminal no VS Code:** use o menu **Terminal → New Terminal** ou o atalho <kbd>Ctrl</kbd>+<kbd>`</kbd>.

2. **Fazer login com Azure CLI:**

   ```powershell
   az login
   ```
   Isso abrirá o navegador para que você se autentique com a conta do Azure atribuída para o laboratório. Após concluir, o terminal exibirá a lista de assinaturas disponíveis.

3. **Verificar a assinatura ativa:**

   ```powershell
   az account show --output table
   ```
   Confirme que a assinatura exibida é a correta para o workshop. Se precisar alterá-la:
   
   ```powershell
   az account set --subscription "nome-ou-id-da-assinatura"
   ```

### Execução do Script

Após confirmar o login com o usuário adequado para sua assinatura do Azure, execute: 

``` powershell
cd labs\foundry\setup\op-flex
.\deploy.ps1
```

Após isso, o script solicitará interativamente os parâmetros da sua implantação. Pressione **Enter** para aceitar o valor padrão no caso da zona e grupo de recursos. Aqui você pode ver um exemplo de execução:

``` powershell
TenantName: 3345
Pressione Enter para o padrão.
Location [eastus]: 
ResourceGroupName [rg-contoso-retail]: 
Deseja configurar agora a conexão SQL do Fabric para o Lab04? (s/N): s
FabricWarehouseSqlEndpoint (sem protocolo, sem porta): kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com
FabricWarehouseDatabase: retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b
```

A seguinte confirmação será apresentada:

``` powershell
========================================
 Workshop Multi-Agêntico - Implantação
 Plano: Flex Consumption (FC1 / Linux)
========================================

  Tenant:         3345
  Location:       eastus
  Resource Group: rg-contoso-retail
  Fabric SQL:     kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com
  Fabric DB:      retail_sqldatabase_danrdol6ases3c-6d18d61e-43a5-4281-a754-b255fc9a6c9b
```

Após isso, você começará a ver o progresso da implantação e será informado sobre os recursos que estão sendo criados. Em menos de 10 minutos seu ambiente de trabalho estará pronto.

---

> 👁️ **Revisar a saída.** Ao finalizar, o script exibe os nomes e URLs de todos os recursos criados. Anote esses valores — você precisará deles nos laboratórios!

> **Nota:** Se você não fornecer os parâmetros do Fabric, a implantação **não falha**. Ela omite a configuração da conexão SQL e exibe um aviso para configurá-la manualmente depois. A conexão SQL só é necessária para o Lab 4 (Julie) e a Function App `SqlExecutor`.

---

### Verificação

Após a implantação, verifique que os recursos foram criados corretamente:

```powershell
az resource list --resource-group rg-contoso-retail --output table
```

---

O resultado deve conter estes elementos (os nomes podem variar):

| Recurso             | Nome                          | Descrição                                                    |
| ------------------- | ----------------------------- | ------------------------------------------------------------ |
| Storage Account     | `stcontosoretail{suffix}`     | Armazenamento para a Function App                            |
| App Service Plan    | `asp-contosoretail-{suffix}`  | Plano de hospedagem: Flex para Azure Functions               |
| Function App        | `func-contosoretail-{suffix}` | API da Contoso Retail (.NET 8, dotnet-isolated)              |
| AI Foundry Resource | `ais-contosoretail-{suffix}`  | Recurso unificado do AI Foundry (AI Services + gerenciamento de projetos) com modelo GPT-4.1 implantado |
| AI Foundry Project  | `aip-contosoretail-{suffix}`  | Projeto de trabalho dentro do Foundry Resource               |

> **Nota:** O `{suffix}` é um identificador único de 5 caracteres gerado automaticamente a partir do número de tenant fornecido. Isso garante que os nomes dos recursos não colidam entre participantes.

### Permissões RBAC para o Microsoft Foundry

Para que os agentes possam ser criados e executados no Microsoft Foundry, seu usuário precisa do role **Cognitive Services User** sobre o recurso de AI Services. Este role inclui o data action `Microsoft.CognitiveServices/*` necessário para operações de agentes. Se não o tiver, você receberá um erro `PermissionDenied` ao tentar criar agentes.

Execute os seguintes comandos para atribuir o role (substitua `{suffix}` pelo seu sufixo de 5 caracteres):

```powershell
# Obter seu nome de usuário (UPN)
$upn = az account show --query "user.name" -o tsv

# Atribuir o role Cognitive Services User sobre o recurso de AI Services
az role assignment create `
    --assignee $upn `
    --role "Cognitive Services User" `
    --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/ais-contosoretail-{suffix}"
```

> **Nota:** Se não souber o nome exato do recurso, você pode verificá-lo com:
> ```powershell
> az cognitiveservices account list --resource-group rg-contoso-retail --query "[].name" -o tsv
> ```
>
> A propagação do RBAC pode levar até 1 minuto. Aguarde antes de tentar criar agentes.

---

## Estrutura do código

```
labs/foundry/
├── README.md                              ← Este arquivo
├── lab03-anders-executor-agent.md         ← Lab 3: Agente Anders
├── lab04-julie-planner-agent.md           ← Lab 4: Agente Julie
├── setup/
│   ├── op-flex/                           ← ⭐ Opção recomendada (Flex Consumption / Linux)
│   │   ├── main.bicep
│   │   ├── storage-rbac.bicep
│   │   └── deploy.ps1
│   └── op-consumption/                    ← Opção clássica (Consumption Y1 / Windows)
│       ├── main.bicep
│       ├── storage-rbac.bicep
│       └── deploy.ps1
└── code/
    ├── api/
    │   └── FxContosoRetail/               ← Azure Function (API)
    │       ├── FxContosoRetail.cs         ← Endpoints: OlaMundo, OrdersReporter, SqlExecutor
    │       ├── Program.cs
    │       ├── Models/
    │       └── ...
    ├── agents/
    │   ├── AndersAgent/                   ← Console App: Agente Anders (kind: prompt + OpenAPI tool)
    │   │   ├── ms-foundry/                ← Versão Responses API (recomendada)
    │   │   │   ├── Program.cs
    │   │   │   └── appsettings.json
    │   │   └── ai-foundry/                ← Versão Persistent Agents API (alternativa)
    │   │       └── ...
    │   └── JulieAgent/                    ← Console App: Agente Julie (kind: workflow)
    │       ├── Program.cs                 ← Cria os 3 agentes + chat com Julie
    │       ├── JulieAgent.cs              ← Julie: workflow com 3 tools (SqlAgent, MarketingAgent, OpenAPI)
    │       ├── SqlAgent.cs                ← Sub-agente: gera T-SQL a partir de linguagem natural
    │       ├── MarketingAgent.cs          ← Sub-agente: gera mensagens com Bing Search
    │       ├── db-structure.txt           ← DDL do BD injetado no SqlAgent
    │       └── appsettings.json
    └── tests/
        ├── bruno/                         ← Coleção Bruno (REST client)
        │   ├── bruno.json
        │   ├── OrdersReporter.bru
        │   └── environments/
        │       └── local.bru
        └── http/
            └── FxContosoRetail.http       ← Arquivo .http (VS Code REST Client)
```

---

## Laboratórios

| Lab   | Arquivo                                                         | Descrição                                                    |
| ----- | --------------------------------------------------------------- | ------------------------------------------------------------ |
| Lab 3 | [Anders — Executor Agent](lab03-anders-executor-agent.md)       | Criar o agente executor que gera relatórios e interage com serviços da Contoso Retail. |
| Lab 4 | [Julie — Planner Agent](lab04-julie-planner-agent.md)           | Criar o agente orquestrador de campanhas de marketing usando o padrão workflow com sub-agentes (SqlAgent, MarketingAgent) e ferramenta OpenAPI. |

---

## Próximo passo

Após concluir o setup, continue com o [Lab 3 — Anders (Executor Agent)](lab03-anders-executor-agent.md).
