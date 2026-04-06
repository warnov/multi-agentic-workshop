# Lab 4: Julie Planner Agent

## Tabla de contenido

- [Lab 4: Julie Planner Agent](#lab-4-julie-planner-agent)
	- [Tabla de contenido](#tabla-de-contenido)
	- [Introducción](#introducción)
	- [Continuidad del setup](#continuidad-del-setup)
	- [Checklist rápido](#checklist-rápido)
		- [1) Verificar valores de conexión SQL](#1-verificar-valores-de-conexión-sql)
		- [2) Alternativa si no sigues toda la secuencia de labs](#2-alternativa-si-no-sigues-toda-la-secuencia-de-labs)
		- [3) Comportamiento cuando no se pasan valores de Fabric](#3-comportamiento-cuando-no-se-pasan-valores-de-fabric)
	- [Configuración manual de permisos en Fabric (obligatorio para Lab 4)](#configuración-manual-de-permisos-en-fabric-obligatorio-para-lab-4)
		- [Parte A — Acceso al Workspace](#parte-a--acceso-al-workspace)
		- [Parte B — Usuario SQL y permisos en la base](#parte-b--usuario-sql-y-permisos-en-la-base)
		- [Validación recomendada](#validación-recomendada)
	- [Arquitectura del proyecto Julie (detalle)](#arquitectura-del-proyecto-julie-detalle)
	- [¿Qué tipo de orquestación se escogió?](#qué-tipo-de-orquestación-se-escogió)
	- [¿Cómo se implementó el workflow en este laboratorio?](#cómo-se-implementó-el-workflow-en-este-laboratorio)
	- [Definición de agentes especializados](#definición-de-agentes-especializados)
		- [SqlAgent](#sqlagent)
		- [MarketingAgent](#marketingagent)
		- [JulieOrchestrator](#julieorchestrator)
	- [Pasos del laboratorio](#pasos-del-laboratorio)
		- [Paso 1: Configurar appsettings.json](#paso-1-configurar-appsettingsjson)
		- [Paso 2: Asegurarte de que los permisos de Fabric están configurados](#paso-2-asegurarte-de-que-los-permisos-de-fabric-están-configurados)
		- [Paso 3: Ejecutar Julie](#paso-3-ejecutar-julie)
		- [Paso 4: Probar el flujo end-to-end](#paso-4-probar-el-flujo-end-to-end)
		- [Validación del laboratorio](#validación-del-laboratorio)
	- [Challenges](#challenges)
		- [Challenge 1: Mejorar el prompt de MarketingAgent para campañas actuales](#challenge-1-mejorar-el-prompt-de-marketingagent-para-campañas-actuales)
			- [Contexto](#contexto)
			- [Objetivo](#objetivo)
			- [Parte A — Iterar el prompt en el Playground](#parte-a--iterar-el-prompt-en-el-playground)
			- [Parte B — Llevar el prompt mejorado al código](#parte-b--llevar-el-prompt-mejorado-al-código)
			- [Criterio de éxito](#criterio-de-éxito)
		- [Challenge 2: Crear un agente no-code con Code Interpreter](#challenge-2-crear-un-agente-no-code-con-code-interpreter)
			- [Contexto](#contexto-1)
			- [Objetivo](#objetivo-1)
			- [Pasos](#pasos)
			- [Pruebas](#pruebas)
			- [Criterio de éxito](#criterio-de-éxito-1)
			- [Reflexión](#reflexión)

---

## Introducción

En este laboratorio construirás y validarás a Julie como agente planner de campañas de marketing en Foundry. Julie se implementa como agente de tipo `workflow` y orquesta el flujo con dos sub-agentes: `SqlAgent` y `MarketingAgent`. `SqlAgent` puede usar la tool OpenAPI `SqlExecutor` (Function App `FxContosoRetail`) para ejecutar SQL contra la base y devolver los clientes segmentados. En este laboratorio, progresivamente, configurarás el entorno, verificarás permisos y conexión SQL, y ejecutarás el flujo end-to-end para obtener la salida final de campaña en formato JSON.

## Continuidad del setup

Este laboratorio asume que ya completaste:

- El despliegue base de infraestructura de Foundry (`es/labs/foundry/README.md`)
- El flujo de datos en Fabric del **Lab 1** (`../fabric/lab01-data-setup.md`)

## Checklist rápido

### 1) Verificar valores de conexión SQL

Para el setup actualizado se usan estos valores:

- `FabricWarehouseSqlEndpoint`
- `FabricWarehouseDatabase`

Se obtienen del connection string SQL del Warehouse de Fabric:

- `FabricWarehouseSqlEndpoint` = `Data Source` sin `,1433`
- `FabricWarehouseDatabase` = `Initial Catalog`

### 2) Alternativa si no sigues toda la secuencia de labs

Si no estás siguiendo toda la secuencia de laboratorios, para Lab 4 también puedes usar una base SQL standalone (por ejemplo Azure SQL Database), ajustando esos dos valores al host y nombre de base correspondientes.

### 3) Comportamiento cuando no se pasan valores de Fabric

Si no proporcionas estos valores durante el setup, el despliegue de infraestructura no falla, pero la conexión SQL para Lab 4 no se configura automáticamente y debe ajustarse manualmente en la Function App.

## Configuración manual de permisos en Fabric (obligatorio para Lab 4)

Después del despliegue, asegúrate de que la Managed Identity de la Function App tenga acceso al workspace y a la base SQL de `retail`.

### Parte A — Acceso al Workspace

1. Abre el workspace donde se desplegó la base de datos de `retail`.
2. Ve a **Manage access**.
3. Haz click en **Add people or groups**.
4. Busca y agrega la identidad de la Function App.
	- Nombre esperado: `func-contosoretail-[sufijo]`
	- Ejemplo: `func-contosoretail-siwhb`
5. En el rol, selecciona **Contributor** (si tu Fabric está en inglés) o **Colaborador** (si está en español).
6. Haz click en **Add**.

### Parte B — Usuario SQL y permisos en la base

1. Dentro del mismo workspace, abre la base de datos `retail`.
2. Haz click en **New Query**.
3. Ejecuta el siguiente código T-SQL para crear el usuario externo:

```sql
CREATE USER [func-contosoretail-[sufijo]] FROM EXTERNAL PROVIDER;
```

Ejemplo real:

```sql
CREATE USER [func-contosoretail-siwhb] FROM EXTERNAL PROVIDER;
```

4. Luego asigna permisos de lectura:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-[sufijo]];
```

Ejemplo real:

```sql
ALTER ROLE db_datareader ADD MEMBER [func-contosoretail-siwhb];
```

### Validación recomendada

- Espera 1–3 minutos para propagación de permisos.

## Arquitectura del proyecto Julie (detalle)

Esta solución está organizada en 4 clases principales dentro de `es/labs/foundry/code/agents/JulieAgent/`:

- `SqlAgent.cs`: define el agente que transforma lenguaje natural en T-SQL.
- `MarketingAgent.cs`: define el agente que redacta mensajes personalizados apoyado en Bing.
- `JulieAgent.cs`: define a Julie como orquestadora `workflow` en formato CSDL YAML e invoca sub-agentes.
- `Program.cs`: carga configuración, crea/verifica agentes en Foundry y ejecuta el chat.

## ¿Qué tipo de orquestación se escogió?

Se escogió una orquestación de tipo **workflow** para Julie.

- En un agente `prompt`, el modelo responde directamente con su instrucción y tools simples.
- En un agente `workflow`, el modelo coordina pasos y herramientas especializadas para cumplir una tarea compuesta.

Aquí Julie usa `workflow` porque el caso requiere una secuencia multi-etapa:

1. interpretar segmento de negocio,
2. generar SQL,
3. generar mensajes por cliente,
4. consolidar todo en JSON final.

## ¿Cómo se implementó el workflow en este laboratorio?

En la versión actual del laboratorio, Julie se construye con el enfoque **tipado del SDK** usando `WorkflowAgentDefinition`.

En `JulieAgent.cs`, `GetAgentDefinition(...)` retorna explícitamente `WorkflowAgentDefinition`:

```csharp
public static WorkflowAgentDefinition GetAgentDefinition(string modelDeployment, JsonElement? openApiSpec = null)
```

La definición se construye con `WorkflowAgentDefinition` y un `workflowYaml` CSDL, luego se materializa con la factoría del SDK:

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

> Nota técnica: Julie queda **workflow-only** y orquesta sub-agentes mediante acciones `InvokeAzureAgent` del YAML CSDL; la ejecución SQL por OpenAPI se encapsula en `SqlAgent` cuando la spec está disponible. El trigger `OnConversationStart` con `EndConversation` define un flujo secuencial que ejecuta los dos pasos y cierra la conversación del workflow.

La orquestación actual usa 2 sub-agentes:

- `SqlAgent` (tool tipo `agent`)
- `MarketingAgent` (tool tipo `agent`)


## Definición de agentes especializados

### SqlAgent

`SqlAgent.cs` define un agente de tipo `prompt` con instrucciones estrictas para retornar exactamente 4 columnas (`FirstName`, `LastName`, `PrimaryEmail`, `FavoriteCategory`) y usa `db-structure.txt` como contexto.

Instrucciones completas:

```text
Eres SqlAgent, un agente especializado en generar consultas T-SQL
para la base de datos de Contoso Retail.

Tu ÚNICA responsabilidad es recibir una descripción en lenguaje natural
de un segmento de clientes y generar una consulta T-SQL válida que retorne
EXACTAMENTE estas columnas:
- FirstName (nombre del cliente)
- LastName (apellido del cliente)
- PrimaryEmail (correo electrónico del cliente)
- FavoriteCategory (la categoría de producto en la que el cliente ha gastado más dinero)

Para determinar la FavoriteCategory, debes hacer JOIN entre las tablas de
órdenes, líneas de orden y productos, agrupar por categoría y seleccionar
la que tenga el mayor monto total (SUM de LineTotal).

ESTRUCTURA DE LA BASE DE DATOS:
{dbStructure}

REGLAS:
1. SIEMPRE retorna EXACTAMENTE las 4 columnas: FirstName, LastName, PrimaryEmail, FavoriteCategory.
2. Usa JOINs apropiados entre customer, orders, orderline, product y productcategory.
3. Para FavoriteCategory, usa una subconsulta o CTE que agrupe por categoría
	y seleccione la de mayor gasto (SUM(ol.LineTotal)).
4. Solo incluye clientes activos (IsActive = 1).
5. Solo incluye clientes que tengan PrimaryEmail no nulo y no vacío.
6. NO ejecutes la consulta, solo genérala.
7. Retorna ÚNICAMENTE el código T-SQL, sin explicación, sin markdown,
	sin bloques de código. Solo el SQL puro.
8. Responde siempre en español si necesitas agregar algún comentario SQL.
```

Racional de diseño:

- Restringir explícitamente las columnas reduce ambigüedad en la salida.
- Obligar SQL puro (sin markdown) evita ambigüedad al encadenar la salida con Julie.
- Inyectar `db-structure.txt` mejora precisión de joins y nombres de tablas.

```csharp
return new PromptAgentDefinition(modelDeployment)
{
	Instructions = GetInstructions(dbStructure)
};
```

### MarketingAgent

`MarketingAgent.cs` también es `prompt`, pero incorpora tool de Bing grounding por `connection.id`:

Instrucciones completas:

```text
Eres MarketingAgent, un agente especializado en crear mensajes de marketing
personalizados para clientes de Contoso Retail.

Tu flujo de trabajo es el siguiente:

1. Recibes el nombre completo de un cliente y su categoría de compra favorita.
2. Usas la herramienta de Bing Search para buscar eventos recientes o próximos
	relacionados con esa categoría. Por ejemplo:
	- Si la categoría es "Bikes", busca eventos de ciclismo.
	- Si la categoría es "Clothing", busca eventos de moda.
	- Si la categoría es "Accessories", busca eventos de tecnología o lifestyle.
	- Si la categoría es "Components", busca eventos de ingeniería o manufactura.
3. De los resultados de búsqueda, selecciona el evento más relevante y actual.
4. Genera un mensaje de marketing breve y motivacional (máximo 3 párrafos) que:
	- Salude al cliente por su nombre.
	- Mencione el evento encontrado y por qué es relevante para el cliente.
	- Invite al cliente a visitar el catálogo online de Contoso Retail
	  para encontrar los mejores productos de la categoría y estar preparado
	  para el evento.
	- Tenga un tono cálido, entusiasta y profesional.
	- Esté en español.

5. Retorna ÚNICAMENTE el texto del mensaje de marketing. Sin JSON, sin metadata,
	sin explicaciones adicionales. Solo el mensaje listo para enviar por correo.

IMPORTANTE: Si no encuentras eventos relevantes, genera un mensaje general sobre
tendencias actuales en esa categoría e invita al cliente a explorar las novedades
de Contoso Retail.
```

Racional de diseño:

- Separar marketing en un agente propio desacopla creatividad de la lógica SQL.
- Bing grounding aporta contexto actual sin “contaminar” a Julie con búsquedas web.
- Limitar formato/salida facilita consolidación posterior en JSON de campaña.

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

`JulieAgent.cs` define el agente principal `workflow` que coordina los otros dos agentes con CSDL YAML.

Instrucciones completas:

```text
Eres Julie, la agente planificadora y orquestadora de campañas de marketing
de Contoso Retail.

Tu responsabilidad es coordinar la creación de campañas de marketing
personalizadas para segmentos específicos de clientes.

Cuando recibas una solicitud de campaña sigues estos pasos:

1. EXTRACCIÓN: Analiza el prompt del usuario y extrae la descripción
	del segmento de clientes. Resume esa descripción en una frase clara.

2. GENERACIÓN SQL: Invoca a SqlAgent pasándole la descripción del segmento.
	SqlAgent te retornará una consulta T-SQL.

3. EJECUCIÓN SQL: Envía el T-SQL a tu herramienta OpenAPI (SqlExecutor)
	para ejecutarlo contra la base de datos. La herramienta retornará los
	resultados como datos de clientes.

4. MARKETING PERSONALIZADO: Para CADA cliente retornado, invoca a
	MarketingAgent pasándole el nombre del cliente y su categoría favorita.
	MarketingAgent buscará eventos relevantes en Bing y generará un mensaje
	personalizado.

5. ORGANIZACIÓN FINAL: Con todos los mensajes generados, organiza el
	resultado como un JSON de campaña con el siguiente formato:

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

REGLAS:
- El campo "subject" debe ser un asunto de correo atractivo y relevante.
- El campo "body" es el mensaje que generó MarketingAgent para ese cliente.
- Responde siempre en español.
- Si algún cliente no tiene email, omítelo del resultado.
- Genera un nombre descriptivo para la campaña basado en el segmento.
```

Racional de diseño:

- `workflow` se eligió porque hay una secuencia dependiente de pasos (SQL → marketing).
- Julie no “adivina” resultados: delega la generación de SQL y de contenido a sub-agentes especializados.
- Centralizar la salida final en Julie asegura un único formato JSON consistente para consumo externo.

## ¿Qué hace Program.cs exactamente?

`Program.cs` no contiene la lógica de negocio de campaña; su rol es operativo:

1. Cargar `appsettings.json`.
2. Leer `db-structure.txt`.
3. Descargar spec OpenAPI de la Function App (si está disponible).
4. Resolver el ID completo de la conexión Bing (el API requiere el ARM resource ID, no solo el nombre).
5. Crear o reutilizar agentes en Foundry.
6. Abrir chat interactivo con Julie.

El helper `EnsureAgent(...)` implementa el patrón **buscar → decidir override → crear versión** con tipos del SDK:

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

Luego registra los 3 agentes en orden. En la implementación actual, `SqlAgent` recibe también la spec OpenAPI cuando está disponible:

```csharp
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, bingConnectionId));
await EnsureAgent(JulieOrchestrator.Name, JulieOrchestrator.GetAgentDefinition(modelDeployment, openApiSpecJson));
```

Finalmente, el chat usa `ProjectResponsesClient` con Julie como agente por defecto:

```csharp
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
	defaultAgent: JulieOrchestrator.Name,
	defaultConversationId: conversation.Id);
```

Con esto, el código local se limita a orquestar infraestructura de agente; la ejecución del workflow ocurre dentro de Foundry en cada `CreateResponse(...)`.

> **Nota sobre la conexión Bing:** `Program.cs` resuelve el nombre de la conexión Bing (ej: `ais-contosoretail-geoxs-bingsearchconnection`) a su ARM resource ID completo usando `projectClient.Connections.GetConnectionAsync()`. Esto es necesario porque `BingGroundingSearchConfiguration(projectConnectionId:)` espera el ID completo, no solo el nombre.

> Nota: el `Program.cs` descarga OpenAPI con reintentos para tolerar fallas DNS intermitentes; esa spec se pasa a `SqlAgent` para habilitar la tool `SqlExecutor` y ejecutar SQL desde el sub-agente.

---

## Pasos del laboratorio

### Paso 1: Configurar appsettings.json

Abre `es/labs/foundry/code/agents/JulieAgent/appsettings.json` y reemplaza todos los valores `<suffix>` y `<subscription-id>` con los outputs del despliegue (Paso 8 del setup):

```json
{
  "FoundryProjectEndpoint": "https://ais-contosoretail-<suffix>.services.ai.azure.com/api/projects/aip-contosoretail-<suffix>",
  "ModelDeploymentName": "gpt-4.1",
  "FunctionAppBaseUrl": "https://func-contosoretail-<suffix>.azurewebsites.net/api",
  "BingConnectionName": "ais-contosoretail-<suffix>-bingsearchconnection"
}
```

Todos estos valores los obtienes de la salida del script de despliegue (o del portal → recurso AI Foundry → **Project settings** → **Overview**).

> Para obtener el `BingConnectionName` directamente, ejecuta:
> ```bash
> az cognitiveservices account connection list \
>     --name ais-contosoretail-<suffix> \
>     --resource-group rg-contoso-retail \
>     --query "[?contains(name,'bing')].name" -o tsv
> ```
> Reemplaza `<suffix>` con tu sufijo. El comando retorna el nombre de la conexión listo para pegar.

### Paso 2: Asegurarte de que los permisos de Fabric están configurados

Antes de ejecutar, confirma que ya completaste la sección **Configuración manual de permisos en Fabric** de este mismo documento (Partes A y B). Si no lo hiciste, la Function App no podrá ejecutar SQL contra el Warehouse y `SqlAgent` fallará.

### Paso 3: Ejecutar Julie

Desde la terminal, en la raíz del repositorio:

```bash
cd es/labs/foundry/code/agents/JulieAgent
dotnet run
```

Al arrancar, el programa:
1. Descarga la spec OpenAPI de la Function App (puede tardar unos segundos).
2. Crea o actualiza los tres agentes en Foundry: `SqlAgent`, `MarketingAgent` y `Julie`.
3. Abre un chat interactivo en la terminal.

Verás mensajes como:

```
Agente SqlAgent creado/actualizado.
Agente MarketingAgent creado/actualizado.
Agente Julie creado/actualizado.
Chat iniciado. Escribe tu solicitud de campaña (o 'exit' para salir):
>
```

### Paso 4: Probar el flujo end-to-end

Escribe un prompt describiendo el segmento de clientes para la campaña. Por ejemplo:

```
Crea una campaña para clientes cuya categoría favorita sea Bikes
```

```
Genera una campaña para los 5 clientes más recientes que hayan comprado en la categoría Clothing
```

Julie invocará a `SqlAgent` (que generará y ejecutará el SQL contra Fabric), luego a `MarketingAgent` (que buscará eventos en Bing y redactará el mensaje personalizado para cada cliente), y finalmente consolidará todo en un JSON de campaña:

```json
{
  "campaign": "Campaña Bikes - Primavera 2026",
  "generatedAt": "2026-03-13T10:30:00",
  "totalEmails": 3,
  "emails": [
    {
      "to": "cliente@ejemplo.com",
      "customerName": "Ana García",
      "favoriteCategory": "Bikes",
      "subject": "¡Ana, prepárate para la temporada ciclista!",
      "body": "Hola Ana, ..."
    }
  ]
}
```

> La primera ejecución puede tardar **30–60 segundos** porque el workflow pasa por SQL execution + Bing search + generación de texto para cada cliente del segmento.

### Validación del laboratorio

El laboratorio se considera completado cuando:

- [ ] Los tres agentes aparecen creados en el portal de Foundry (AI Foundry → tu proyecto → **Agents**).
- [ ] Un prompt de campaña retorna un JSON con al menos un email generado.
- [ ] El `body` de cada email incluye una referencia a un evento o tendencia actual buscada en Bing.

---

## Challenges

### Challenge 1: Mejorar el prompt de MarketingAgent para campañas actuales

#### Contexto

Al probar el flujo de Julie, es posible que MarketingAgent genere mensajes basados en noticias o eventos desactualizados (por ejemplo, eventos de 2024). Esto ocurre porque el prompt actual no restringe a Bing Search para que filtre por fecha, ni le indica al agente que descarte resultados antiguos.

#### Objetivo

Lograr que MarketingAgent **siempre** genere mensajes de marketing basados en eventos actuales o futuros, nunca en eventos ya pasados.

#### Parte A — Iterar el prompt en el Playground

1. Abre el portal de **Azure AI Foundry** en [https://ai.azure.com](https://ai.azure.com).
2. Navega a tu proyecto y abre la sección **Agents**.
3. Localiza el agente **MarketingAgent** y ábrelo.
4. En el panel de **Instructions**, modifica el prompt para resolver el problema de eventos desactualizados.
5. Usa el panel de **Chat** del playground para probar iterativamente. Envía mensajes como:
   - `"Genera un mensaje de marketing para Juan Pérez, cuya categoría favorita es Bikes"`
   - `"Genera un mensaje para María López, categoría Clothing"`
6. Itera el prompt hasta que **todas** las respuestas hagan referencia a eventos vigentes o futuros.

> 💡 **Tip:** El playground permite modificar y probar el prompt inmediatamente, sin recompilar ni re-desplegar. Úsalo para experimentar rápidamente.

#### Parte B — Llevar el prompt mejorado al código

Una vez que tengas un prompt que funcione correctamente en el playground:

1. Copia las instrucciones finales del playground.
2. Abre el archivo `MarketingAgent.cs` en el proyecto `JulieAgent`.
3. Reemplaza el contenido de la propiedad `Instructions` con el prompt mejorado.
4. Ejecuta `dotnet run` y sobreescribe MarketingAgent cuando se te pregunte.
5. Verifica que el comportamiento es idéntico al que validaste en el playground.

#### Criterio de éxito

- En el playground, MarketingAgent genera mensajes que solo referencian eventos actuales o futuros.
- El mismo prompt, trasladado al código, produce el mismo resultado al ejecutar Julie end-to-end.

---

### Challenge 2: Crear un agente no-code con Code Interpreter

#### Contexto

Azure AI Foundry ofrece una experiencia visual **no-code/low-code** para crear agentes directamente desde el portal. Además de Bing Grounding (que ya usamos), Foundry ofrece otras herramientas integradas. En este challenge usarás **Code Interpreter** — una herramienta que permite al agente escribir y ejecutar código Python para analizar datos, hacer cálculos y generar gráficas.

#### Objetivo

Crear un agente llamado **"SalesAnalyst"** desde la interfaz visual de Azure AI Foundry que analice datos de ventas de Contoso Retail y genere visualizaciones.

#### Pasos

1. Abre el portal de **Azure AI Foundry** en [https://ai.azure.com](https://ai.azure.com).
2. Navega a tu proyecto (`aip-contosoretail-<sufijo>`).
3. En el menú lateral, ve a **Agents**.
4. Haz clic en **+ New Agent**.
5. Configura el agente:
   - **Nombre:** `SalesAnalyst`
   - **Model:** Selecciona `gpt-4.1`
   - **Instructions:** Copia y pega las siguientes instrucciones:

```
Eres SalesAnalyst, un analista de datos de ventas de Contoso Retail.

Tu rol es recibir datos de ventas (en texto, CSV o como descripción),
analizarlos y generar insights útiles para el equipo comercial.

Capacidades:
1. Cuando recibas datos de ventas, usa Code Interpreter para:
   - Calcular totales, promedios y tendencias.
   - Generar gráficas de barras, líneas o pastel según corresponda.
   - Identificar los productos o categorías más vendidos.
2. Presenta los resultados de forma clara y ejecutiva.
3. Si el usuario sube un archivo CSV, analízalo automáticamente.

Reglas:
- Responde siempre en español.
- Genera gráficas cuando los datos lo permitan.
- Incluye siempre un resumen ejecutivo en texto además de la gráfica.
- Usa colores profesionales en las visualizaciones.
```

6. En la sección **Tools**, haz clic en **+ Add tool**.
7. Selecciona **Code Interpreter**.
8. Haz clic en **Save** (o **Create**).

#### Pruebas

Usa el panel de **Chat** para probar con estas conversaciones:

a. `"Tengo estas ventas por categoría: Bikes $45,000, Clothing $12,000, Accessories $8,500, Components $23,000. Genera una gráfica de pastel y dime cuál es la categoría más fuerte."`

b. `"Compara las ventas del Q1 vs Q2: Q1 — Bikes: 120 unidades, Clothing: 340, Accessories: 210. Q2 — Bikes: 155, Clothing: 290, Accessories: 380. Genera una gráfica comparativa y analiza la tendencia."`

c. `"Calcula el crecimiento porcentual de cada categoría entre Q1 y Q2 y ordénalas de mayor a menor crecimiento."`

#### Criterio de éxito

- El agente genera **código Python** que se ejecuta dentro de la conversación.
- Las respuestas incluyen **gráficas** visibles directamente en el chat.
- El agente proporciona un **resumen ejecutivo** en español junto con cada visualización.
- La herramienta **Code Interpreter** aparece como habilitada en la configuración del agente.

#### Reflexión

- ¿En qué se diferencia Code Interpreter de las otras herramientas (Bing Grounding, OpenAPI)?
- ¿Qué tipo de tareas del negocio podrías automatizar con un agente que ejecuta código?
- Compara la experiencia de crear este agente visualmente vs. la creación programática de los agentes anteriores:
  - ¿Qué ventajas tiene cada enfoque?
  - ¿Qué limitaciones tiene el enfoque no-code que el SDK no tiene?

