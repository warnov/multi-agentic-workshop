## <a id="descripcion-general"></a>Visão geral

Este workshop orienta os participantes no desenho e na implementação de uma arquitetura **multiagente** usando serviços da Microsoft, aplicada a um cenário de negócios do tipo **Contoso Retail**. O objetivo do exercício não é construir um sistema produtivo, mas entender como **orquestrar agentes com responsabilidades bem definidas** para responder a diferentes tipos de perguntas de negócio sobre o mesmo conjunto de dados.

A arquitetura é composta por três camadas bem definidas:

- **Microsoft Fabric**, como camada de dados: hospeda o banco de dados SQL de varejo e um Data Agent que responde a perguntas em linguagem natural.
- **Microsoft Foundry**, como camada de raciocínio e execução: agentes que geram relatórios HTML via OpenAPI e planejam campanhas de marketing consultando o banco de dados.
- **Copilot Studio**, como camada de orquestração e experiência conversacional: um agente orquestrador que conecta todos os agentes, um agente de produto com conhecimento em SharePoint e um agente filho que envia e‑mails.

Vamos explorar as capacidades de orquestração tanto no Copilot Studio quanto no Microsoft Foundry.

------

## <a id="escenario"></a>Cenário de negócios: Contoso Retail

A Contoso é uma empresa de varejo que vende produtos para clientes corporativos e finais. Seu modelo de dados inclui informações de clientes, contas, pedidos, itens de pedido, faturas, pagamentos, produtos e categorias.

Com base nesse modelo, o negócio precisa responder a dois tipos principais de perguntas recorrentes:

1. **Perguntas operacionais**, voltadas a entender o que aconteceu em um caso específico.
2. **Perguntas analíticas**, voltadas a entender padrões, tendências e sinais do negócio.

O workshop mostra como uma única arquitetura pode atender a ambos os tipos de necessidade sem duplicar sistemas nem lógica.

------

## <a id="flujos"></a>Fluxos de negócio cobertos

### <a id="flujo-operativo"></a>Fluxo operacional

O fluxo operacional responde a solicitações concretas sobre clientes, pedidos e faturamento. Nesse fluxo, o objetivo é reconstruir os fatos transacionais com precisão e produzir artefatos visuais (relatórios HTML) que sintetizem as informações.

O pipeline implementado funciona da seguinte forma:

1. O usuário pede informações operacionais (por exemplo, um relatório de pedidos de um cliente específico).
2. **Bill** (o orquestrador) delega para **Mark** (Fabric) a obtenção dos fatos transacionais exatos: pedidos, itens, valores, datas, etc.
3. **Bill** delega para **Anders** (Foundry) a geração de um relatório visual a partir desses dados. Anders chama a Azure Function `OrdersReporter` por meio de sua ferramenta OpenAPI, que constrói um relatório HTML e o publica no Blob Storage.
4. Anders retorna a URL do relatório publicado e Bill consolida a resposta final para o usuário.

Exemplos de perguntas operacionais:

- Gere um relatório dos pedidos de Izabella Celma.
- Quais são os pedidos e produtos de Marco Rivera?
- Preciso de um resumo visual das compras recentes de um cliente.

### <a id="flujo-analitico"></a>Fluxo analítico e de planejamento

O fluxo analítico responde a perguntas de caráter estratégico e exploratório. Aqui o objetivo não é explicar um caso pontual, mas identificar sinais relevantes que ajudem a priorizar ações e gerar planos concretos.

Nesse fluxo, **Julie** (Foundry) atua como agente orquestrador de campanhas de marketing, definido como um `workflow`. Julie coordena um fluxo de 5 etapas: (1) extrai do prompt do usuário o filtro de segmento de clientes, (2) chama o **SqlAgent** para gerar a consulta T‑SQL correspondente, (3) executa o T‑SQL contra o banco de dados do Fabric por meio da ferramenta OpenAPI (`SqlExecutor` da Azure Function `FxContosoRetail`), (4) para cada cliente retornado, chama o **MarketingAgent** (que usa o Bing Search para encontrar eventos relevantes e gerar uma mensagem de marketing personalizada) e (5) organiza tudo em um JSON de campanha de e‑mails.

Exemplos de perguntas analíticas e de planejamento:

- Crie uma campanha para clientes que tenham comprado bicicletas.
- Planeje uma campanha de retenção para clientes inativos.

------

## <a id="arquitectura"></a>Arquitetura e agentes

![alt text](../assets/image-1.png)

### <a id="capa-datos"></a>Microsoft Fabric – Camada de dados

- **Mark (Data Agent)**
  Data Agent do Fabric que interpreta linguagem natural e consulta o modelo semântico construído sobre o banco SQL `db_retail` (tabelas `customer`, `orders`, `orderline`, `product`). Reconstrói fatos transacionais exatos e os entrega como dados rastreáveis, sem interpretação.

#### Documentação do banco de dados

Para entender melhor o modelo de dados sobre o qual os agentes do Fabric operam, foi adicionada documentação detalhada do banco de dados Contoso Retail. Essa documentação inclui:

- **Diagrama ER (Entidade–Relacionamento)** mostrando as relações entre as principais tabelas
- **Esquemas de tabelas** com todas as colunas e tipos de dados

Você pode consultar a documentação completa aqui: [Database Documentation](./assets/database.md)

### <a id="capa-razonamiento"></a>Microsoft Foundry – Camada de raciocínio

- **Anders (Executor Agent)**
  Executa ações operacionais chamando serviços externos por meio de uma ferramenta OpenAPI. Recebe dados de pedidos e chama o endpoint `OrdersReporter` da Azure Function `FxContosoRetail`, que gera um relatório HTML e o publica no Blob Storage, retornando a URL do documento. Usa o SDK `Azure.AI.Agents.Persistent` com um modelo GPT‑4.1 para interpretar a solicitação, construir o payload JSON e orquestrar a chamada à API.
- **Julie (Planner Agent)**
  Agente orquestrador de campanhas de marketing definido como `kind: "workflow"`. Coordena três ferramentas: **SqlAgent** (`type: "agent"`), que gera consultas T‑SQL a partir de linguagem natural; uma Azure Function usada como ferramenta chamada **SqlExecutor** (`type: "openapi"`), que executa o SQL contra o banco de dados do Fabric (a mesma Function App `FxContosoRetail` utilizada anteriormente por Anders); e o **MarketingAgent** (`type: "agent"`), que usa o Bing Search para encontrar eventos relevantes e gerar mensagens de marketing personalizadas por cliente. O resultado final é uma campanha em JSON com rascunhos de e‑mail prontos para envio.

### <a id="capa-orquestacion"></a>Copilot Studio – Camada de orquestração

- **Charles (Product Q&A Agent)**
  Agente analista de produto que responde a perguntas usando documentação armazenada no SharePoint como base de conhecimento. Também realiza análises competitivas e comparações de mercado usando informações públicas quando solicitado.
- **Bill (Orchestrator)**
  Orquestrador central. Detecta a intenção do usuário e delega para o agente apropriado: conecta agentes externos do Fabric (Mark) e do Foundry (Anders) com agentes internos do Copilot Studio (Charles, Ric). É publicado no Microsoft 365 e Teams.
- **Ric (Child Agent)**
  Agente filho de Bill responsável por enviar e‑mails ao usuário com as informações solicitadas (por exemplo, resultados de consultas ou links para relatórios).

------

## <a id="objetivo"></a>Objetivos do workshop

Ao final do workshop, os participantes irão compreender:

- Como separar dados, raciocínio e experiência do usuário.
- Como desenhar agentes com responsabilidades bem definidas.
- Como orquestrar fluxos operacionais e analíticos sobre um único domínio de negócios.
- Como usar o Copilot Studio ou o Microsoft Foundry como camada central de controle em soluções multiagente.

Este repositório serve como um guia prático e reutilizável para entender e replicar esse padrão arquitetônico em cenários reais.

## <a id="laboratorios"></a>Conteúdo do workshop

O workshop está dividido em laboratórios independentes, porém conectados, organizados por camada arquitetônica. Recomenda‑se segui‑los na ordem indicada.

### 1. Laboratórios de Microsoft Fabric

- [Lab 1 – Configuração do ambiente: capacidade do Fabric, workspace, banco SQL e modelo semântico](./labs/fabric/lab01-data-setup.md)
- [Lab 2 – Agente Mark: Data Agent sobre o modelo semântico de varejo](./labs/fabric/lab02-mark-facts-agent.md)

### 2. Laboratórios de Azure AI Foundry

- [Configuração de infraestrutura do Foundry](./labs/foundry/README.md)
- [Lab 3 – Agente Anders: suporte a OpenAPI, implantação da Function App e execução do agente executor](./labs/foundry/lab03-anders-executor-agent.md)
- [Lab 4 – Agente Julie: workflow agent com subagentes SqlAgent e MarketingAgent](./labs/foundry/lab04-julie-planner-agent.md)

### 3. Laboratórios de Copilot Studio

- [Lab 5 – Configuração do Copilot Studio: ambiente, solução e publisher](./labs/copilot/lab05-mcs-setup.md)
- [Lab 6 – Agente Charles: perguntas e respostas de produto com SharePoint e análise de mercado](./labs/copilot/lab06-charles-copilot-agent.md)
- [Lab 7 – Agente Ric: agente filho para envio de e‑mails + configuração inicial de Bill](./labs/copilot/lab07-ric-child-agent.md)
- [Lab 8 – Orquestrador Bill: conexão de agentes externos (Mark, Anders) e internos (Charles) e regras de orquestração](./labs/copilot/lab08-bill-orchestrator.md)
- [Lab 9 – Publicação de Bill no Microsoft 365 / Teams e testes ponta a ponta](./labs/copilot/lab09-bill-publishing.md)

---

## <a id="resultado"></a>Resultados esperados

Ao final do workshop, os participantes terão construído e compreendido:

- Como desenhar agentes com responsabilidades claras.
- Como separar dados, raciocínio e experiência do usuário.
- Como orquestrar múltiplos agentes a partir do Copilot Studio.
- Como reutilizar o mesmo padrão arquitetônico para diferentes cenários de negócios.

Este repositório serve como um guia prático e reutilizável para desenhar soluções multiagente em projetos reais.

---

## <a id="requisitos"></a>Pré‑requisitos

### <a id="conocimientos"></a>Conhecimentos

- Conhecimentos básicos de Azure.
- Familiaridade geral com conceitos de dados e análise.
- Não é necessária experiência prévia aprofundada em Fabric, Foundry ou Copilot Studio.

### <a id="requisitos-tecnicos"></a>Requisitos técnicos (instalar antes do workshop)

Cada participante deve ter as seguintes ferramentas instaladas em sua máquina **antes de chegar ao workshop**:

| Ferramenta | Descrição | Download |
|-----------|-----------|----------|
| **.NET 8 SDK** | Compilar e executar as Azure Functions e os agentes do Foundry | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Azure CLI** | Autenticar no Azure, implantar recursos e atribuir funções RBAC | [Instalar](https://learn.microsoft.com/cli/azure/install-azure-cli) |
| **Azure Functions Core Tools v4** | Publicar Azure Functions no Azure | [Instalar](https://learn.microsoft.com/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools) |
| **PowerShell 7+** | Executar scripts de implantação de infraestrutura. **Obrigatório em todos os sistemas operacionais** (incluindo Windows). Não use o PowerShell 5.1. | [Instalar](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) · Windows: `winget install Microsoft.PowerShell` |
| **Git** | Clonar o repositório do workshop | [Download](https://git-scm.com/downloads) |
| **VS Code** (recomendado) | Editor de código com extensões para Azure e .NET | [Download](https://code.visualstudio.com/) |

> [!TIP]
> Em **macOS**, você pode instalar todas as ferramentas com o Homebrew:
> ```bash
> brew install dotnet-sdk azure-cli azure-functions-core-tools@4 powershell git
> brew install --cask visual-studio-code
> ```

> [!TIP]
> Em **Linux** (Ubuntu/Debian), você pode instalar o PowerShell 7 com:
> ```bash
> # Instalar o PowerShell 7
> sudo apt-get update && sudo apt-get install -y wget apt-transport-https software-properties-common
> wget -q "https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb"
> sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
> sudo apt-get update && sudo apt-get install -y powershell
> # Outras ferramentas
> sudo apt-get install -y dotnet-sdk-8.0 azure-cli git
> ```
> Veja as instruções completas em: [Instalar o PowerShell no Linux](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux)

> [!TIP]
> Em **Windows**, você pode instalar todas as ferramentas com o winget:
> ```powershell
> winget install Microsoft.DotNet.SDK.8 Microsoft.AzureCLI Microsoft.Azure.FunctionsCoreTools Microsoft.PowerShell Git.Git Microsoft.VisualStudioCode
> ```

### Verificar a instalação

Depois de instalar, verifique se tudo está disponível executando estes comandos em um terminal:

```powershell
dotnet --version        # Deve mostrar 8.x.x
az --version            # Deve mostrar azure-cli 2.x.x
func --version          # Deve mostrar 4.x.x
pwsh --version          # Deve mostrar PowerShell 7.x.x (OBRIGATÓRIO em todos os sistemas operacionais)
git --version           # Deve mostrar git version 2.x.x
```

### Configurar a ExecutionPolicy (apenas Windows)

O PowerShell no Windows pode bloquear a execução de scripts por padrão. Execute **uma única vez** em `pwsh`:

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

Isso permite executar scripts locais e scripts baixados que estejam assinados. Afeta apenas o usuário atual e não requer permissões de administrador.

### Recursos do Azure

- Uma **assinatura do Azure** ativa com permissões de **Owner** ou **Contributor**
- O **nome do tenant temporário** atribuído para o workshop (fornecido no dia do evento)

---

## <a id="notas"></a>Notas finais

Este workshop foi pensado como um exercício **pedagógico e arquitetural**. O foco está no desenho do fluxo e na colaboração entre agentes, não na otimização extrema de modelos ou consultas.

[➡️ Próximo: Lab 1 – Configuração do ambiente no Microsoft Fabric](./labs/fabric/lab01-data-setup.md)
