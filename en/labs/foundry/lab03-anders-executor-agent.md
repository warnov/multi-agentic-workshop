# Lab 3: Anders — Executor Agent

## Tabla de contenido

- [Lab 3: Anders — Executor Agent](#lab-3-anders--executor-agent)
  - [Tabla de contenido](#tabla-de-contenido)
  - [Introducción](#introducción)
    - [¿Qué vamos a hacer en este lab?](#qué-vamos-a-hacer-en-este-lab)
    - [Prerrequisitos](#prerrequisitos)
      - [Herramientas en tu máquina](#herramientas-en-tu-máquina)
      - [Infraestructura Azure](#infraestructura-azure)
      - [Permisos RBAC](#permisos-rbac)
  - [3.1 — Verificar soporte OpenAPI (ya viene preconfigurado)](#31--verificar-soporte-openapi-ya-viene-preconfigurado)
    - [Checklist rápido de validación](#checklist-rápido-de-validación)
    - [Paso 1: Verificar paquetes NuGet](#paso-1-verificar-paquetes-nuget)
    - [Paso 2: Verificar endpoints expuestos](#paso-2-verificar-endpoints-expuestos)
    - [Paso 3: Verificar compilación](#paso-3-verificar-compilación)
    - [Endpoints OpenAPI generados](#endpoints-openapi-generados)
  - [3.2 — Redesplegar la Function App](#32--redesplegar-la-function-app)
    - [¿Cómo obtener `FabricWarehouseSqlEndpoint` y `FabricWarehouseDatabase`?](#cómo-obtener-fabricwarehousesqlendpoint-y-fabricwarehousedatabase)
    - [Opción 0: Re-ejecutar setup de infraestructura (si necesitas refrescar settings)](#opción-0-re-ejecutar-setup-de-infraestructura-si-necesitas-refrescar-settings)
    - [Opción A: Usando Azure Functions Core Tools (recomendada)](#opción-a-usando-azure-functions-core-tools-recomendada)
    - [Opción B: Usando Azure CLI](#opción-b-usando-azure-cli)
  - [3.3 — Verificar la especificación OpenAPI](#33--verificar-la-especificación-openapi)
    - [Obtener la especificación JSON](#obtener-la-especificación-json)
    - [Explorar el Swagger UI](#explorar-el-swagger-ui)
  - [3.4 — El agente Anders: Dos versiones de SDK](#34--el-agente-anders-dos-versiones-de-sdk)
    - [¿Por qué dos versiones?](#por-qué-dos-versiones)
    - [¿Cuál versión debo usar?](#cuál-versión-debo-usar)
    - [Entendiendo el código (versión `ms-foundry/` — recomendada)](#entendiendo-el-código-versión-ms-foundry--recomendada)
      - [Fase 1 — Descargar la especificación OpenAPI](#fase-1--descargar-la-especificación-openapi)
      - [Fase 2 — Verificar agente existente o crear uno nuevo](#fase-2--verificar-agente-existente-o-crear-uno-nuevo)
      - [Fase 3 — Chat interactivo con Responses API](#fase-3--chat-interactivo-con-responses-api)
    - [Paso 1: Configurar `appsettings.json`](#paso-1-configurar-appsettingsjson)
    - [Paso 2: Compilar y ejecutar](#paso-2-compilar-y-ejecutar)
    - [Paso 3: Inspeccionar el agente en Azure AI Foundry](#paso-3-inspeccionar-el-agente-en-azure-ai-foundry)
    - [Paso 4: Probar el agente](#paso-4-probar-el-agente)
  - [Solución de problemas](#solución-de-problemas)
    - [Storage Account bloqueado por política (error 503)](#storage-account-bloqueado-por-política-error-503)
  - [Siguiente paso](#siguiente-paso)

---

## Introducción

Anders es el **agente ejecutor** de la arquitectura multi-agéntica de Contoso Retail. Su rol es recibir solicitudes de acciones operativas — como la generación y publicación de reportes de órdenes — y ejecutarlas interactuando con servicios externos como la Azure Function `FxContosoRetail`.

Para que Anders pueda interactuar con la API de Contoso Retail, definiremos una **OpenAPI Tool** que permite al agente descubrir e invocar automáticamente los endpoints de la Function App a partir de su especificación OpenAPI. Adicionalmente, agregaremos soporte **OpenAPI** a la Function App para documentar la API y facilitar la exploración de sus endpoints.

### ¿Qué vamos a hacer en este lab?

| Paso | Descripción |
|------|-------------|
| **3.1** | Agregar soporte OpenAPI a la Azure Function `FxContosoRetail` |
| **3.2** | Redesplegar la Function App con los cambios |
| **3.3** | Verificar la especificación OpenAPI |
| **3.4** | Entender, configurar, ejecutar y probar el agente Anders |

### Prerrequisitos

#### Herramientas en tu máquina

| Herramienta | Descripción | Descarga |
|-------------|-------------|----------|
| **.NET 8 SDK** | Compilar y ejecutar la Function App y el agente Anders | [Descargar](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Azure CLI** | Autenticarse en Azure, desplegar recursos y asignar roles RBAC | [Instalar](https://learn.microsoft.com/cli/azure/install-azure-cli) |
| **Azure Functions Core Tools** | Publicar la Function App a Azure (opción recomendada) | [Instalar](https://learn.microsoft.com/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools) |
| **PowerShell 7+** | Ejecutar scripts de despliegue. **Requerido en todos los OS** (incluido Windows). No usar PowerShell 5.1. | [Instalar](https://learn.microsoft.com/powershell/scripting/install/installing-powershell) · Windows: `winget install Microsoft.PowerShell` |
| **Git** | Clonar el repositorio del taller | [Descargar](https://git-scm.com/downloads) |

> [!IMPORTANT]
> **Windows:** Después de instalar PowerShell 7, configura la ExecutionPolicy ejecutando **una vez** en `pwsh`:
> ```powershell
> Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
> ```

> [!TIP]
> En **macOS**, puedes instalar las herramientas con Homebrew:
> ```bash
> brew install dotnet-sdk azure-cli azure-functions-core-tools@4 powershell git
> ```

> [!TIP]
> En **Linux** (Ubuntu/Debian), puedes instalar PowerShell 7 con:
> ```bash
> sudo apt-get update && sudo apt-get install -y wget apt-transport-https software-properties-common
> wget -q "https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb"
> sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
> sudo apt-get update && sudo apt-get install -y powershell
> ```
> Ver: [Instalar PowerShell en Linux](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux)

#### Infraestructura Azure

- Haber completado el **setup de infraestructura** descrito en el [Setup de Foundry](README.md)
- Tener anotados **todos los valores generados en el despliegue** de la infraestructura (nombres de recursos, URLs, sufijo, endpoint de AI Foundry, etc.)
- Tener identificados estos 2 valores del Warehouse de Fabric (se usan en el setup actualizado):
    - `FabricWarehouseSqlEndpoint`
    - `FabricWarehouseDatabase`

#### Permisos RBAC

Tu usuario necesita el rol **Cognitive Services User** sobre el recurso de AI Services para poder crear y ejecutar agentes. Como tu usuario es **Owner del tenant**, puedes asignarte el rol tú mismo.

Ejecuta los siguientes comandos (reemplaza `{suffix}` con tu sufijo de 5 caracteres):

```powershell
# 1. Obtener tu nombre de usuario (UPN)
$upn = az account show --query "user.name" -o tsv

# 2. Obtener el nombre del recurso de AI Services (si no lo recuerdas)
az cognitiveservices account list --resource-group rg-contoso-retail --query "[].name" -o tsv

# 3. Asignar el rol Cognitive Services User
az role assignment create `
    --assignee $upn `
    --role "Cognitive Services User" `
    --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/rg-contoso-retail/providers/Microsoft.CognitiveServices/accounts/ais-contosoretail-{suffix}"
```

> **Nota:** La propagación de RBAC puede tardar hasta 1 minuto. Espera antes de continuar con el lab.

---

## 3.1 — Verificar soporte OpenAPI (ya viene preconfigurado)

En la versión actual del taller, la Function App `FxContosoRetail` **ya incluye** OpenAPI y endpoints decorados en el código base. En este paso no vas a implementar OpenAPI desde cero: solo validar que todo está correcto antes del despliegue.

### Checklist rápido de validación

### Paso 1: Verificar paquetes NuGet

Abre `FxContosoRetail.csproj` y confirma que existen estas referencias:

- `Microsoft.Azure.Functions.Worker.Extensions.OpenApi`
- `Microsoft.Data.SqlClient`

### Paso 2: Verificar endpoints expuestos

Abre `FxContosoRetail.cs` y confirma que existen estos endpoints:

- `HolaMundo`
- `OrdersReporter`
- `SqlExecutor`

Además, valida que `OrdersReporter` y `SqlExecutor` tengan atributos OpenAPI (`OpenApiOperation`, `OpenApiRequestBody`, `OpenApiResponseWithBody`).

### Paso 3: Verificar compilación

```powershell
cd labs\foundry\code\api\FxContosoRetail
dotnet build
```

Si compila sin errores, puedes pasar al redespliegue del paso 3.2.

> **Nota:** OpenAPI ya está registrado en el proyecto. No necesitas agregar paquetes ni modificar `Program.cs` en este lab.

> [!IMPORTANT]
> **Sobre la autenticación de los endpoints**
>
> En este taller usamos `AuthorizationLevel.Anonymous` para simplificar la configuración y permitir que Azure AI Foundry pueda invocar la Function App directamente como OpenAPI Tool sin necesidad de gestionar secrets ni configurar autenticación adicional.
>
> **En un entorno de producción, esto no es recomendable.** La práctica correcta es proteger la Function App con **Azure Entra ID (Easy Auth)** y hacer que Foundry se autentique usando **Managed Identity**. El flujo sería:
>
> 1. **Registrar una aplicación en Entra ID** que represente la Function App, obteniendo un Application (client) ID y un Application ID URI (por ejemplo, `api://<client-id>`).
> 2. **Habilitar Easy Auth** en la Function App con `az webapp auth update`, configurándola para validar tokens emitidos por Entra ID contra la app registration. Esto protege todos los endpoints a nivel de plataforma — las peticiones sin un bearer token válido se rechazan con 401 antes de llegar al código.
> 3. **Asignar permisos a la Managed Identity** del recurso de AI Services (`ais-contosoretail-{suffix}`) como principal autorizado en la app registration, ya sea agregándola como miembro de un app role o como identidad permitida en la configuración de Easy Auth.
> 4. **Usar `OpenApiManagedAuthDetails`** en el código del agente en lugar de `OpenApiAnonymousAuthDetails`, especificando el audience de la app registration:
>    ```csharp
>    openApiAuthentication: new OpenApiManagedAuthDetails(
>        audience: "api://<app-registration-client-id>")
>    ```
>
> Con esta configuración, cuando Foundry necesita llamar a la Function App, obtiene un token de Entra ID usando la managed identity del recurso de AI Services, lo envía como `Authorization: Bearer <token>`, y Easy Auth lo valida automáticamente. Los endpoints de la Function pueden mantener `AuthorizationLevel.Anonymous` en el código C# porque la autenticación ocurre en la capa de plataforma.

### Endpoints OpenAPI generados

Una vez desplegada, la Function App expondrá estos endpoints adicionales:

| Endpoint | Descripción |
|----------|-------------|
| `/api/openapi/v3.json` | Especificación OpenAPI 3.0 en formato JSON |
| `/api/swagger/ui` | Interfaz Swagger UI interactiva |

---

## 3.2 — Redesplegar la Function App

La infraestructura ya está desplegada desde el setup inicial. Solo necesitas **publicar el código actualizado** de la Function App.

> [!IMPORTANT]
> El setup de infraestructura actualizado (`op-flex/deploy.ps1` y `op-consumption/deploy.ps1`) acepta estos parámetros para configurar SQL del Lab 4:
> - `FabricWarehouseSqlEndpoint`
> - `FabricWarehouseDatabase`
>
> Si no se proporcionan, el despliegue continúa y solo omite la configuración automática del app setting `FabricWarehouseConnectionString`.

### ¿Cómo obtener `FabricWarehouseSqlEndpoint` y `FabricWarehouseDatabase`?

En Fabric, abre tu **Warehouse** y copia el **connection string** (SQL). Verás algo similar a:

```text
Data Source=kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com,1433;Initial Catalog=retail_sqldatabase_xxx;... 
```

Mapeo de valores:

- `FabricWarehouseSqlEndpoint` = valor de `Data Source` **sin** `,1433`
    - Ejemplo: `kqbvkknqlijebcyrtw2rgtsx2e-dvthxhg2tsuurev2kck26gww4q.database.fabric.microsoft.com`
- `FabricWarehouseDatabase` = valor de `Initial Catalog`
    - Ejemplo: `retail_sqldatabase_xxx`

> [!TIP]
> Estos valores se obtienen del entorno de **Fabric desplegado en el Lab 1** (`../fabric/lab01-data-setup.md`).
>
> Si no estás siguiendo la secuencia completa de laboratorios, en este lab solo necesitamos una base SQL para ejecutar consultas. Puedes usar una base SQL standalone (por ejemplo Azure SQL Database) y ajustar la conexión:
> - `FabricWarehouseSqlEndpoint` por el host SQL de tu base standalone
> - `FabricWarehouseDatabase` por el nombre de tu base
>
> En ese escenario, asegúrate también de configurar permisos de la identidad de la Function App sobre esa base.

### Opción 0: Re-ejecutar setup de infraestructura (si necesitas refrescar settings)

Si quieres redeploy completo (infra + publish) usando el setup:

```powershell
# Flex Consumption
cd labs\foundry\setup\op-flex
.\deploy.ps1 `
    -TenantName "<tu-tenant>" `
    -ResourceGroupName "rg-contoso-retail" `
    -Location "eastus" `
    -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" `
    -FabricWarehouseDatabase "<database-warehouse>"
```

```powershell
# Consumption (Y1)
cd labs\foundry\setup\op-consumption
.\deploy.ps1 `
    -TenantName "<tu-tenant>" `
    -ResourceGroupName "rg-contoso-retail" `
    -Location "eastus" `
    -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" `
    -FabricWarehouseDatabase "<database-warehouse>"
```

> Si solo cambiaste código de la Function App y no necesitas tocar infraestructura, usa la Opción A u Opción B de abajo.

### Opción A: Usando Azure Functions Core Tools (recomendada)

Si tienes instalado [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools), el redespliegue es un solo comando:

```powershell
cd labs\foundry\code\api\FxContosoRetail
func azure functionapp publish func-contosoretail-<suffix>
```

> Reemplaza `<suffix>` con el sufijo de 5 caracteres que obtuviste durante el setup (por ejemplo, `func-contosoretail-a1b2c`).

### Opción B: Usando Azure CLI

Si no tienes `func` CLI, puedes publicar manualmente con `az`:

```powershell
# 1. Compilar el proyecto
cd labs\foundry\code\api\FxContosoRetail
dotnet publish --configuration Release --output bin\publish

# 2. Crear el paquete zip
Compress-Archive -Path "bin\publish\*" -DestinationPath "$env:TEMP\fxcontosoretail.zip" -Force

# 3. Desplegar a Azure
az functionapp deployment source config-zip `
    --resource-group rg-contoso-retail `
    --name func-contosoretail-<suffix> `
    --src "$env:TEMP\fxcontosoretail.zip"

# 4. Limpiar archivos temporales
Remove-Item "$env:TEMP\fxcontosoretail.zip" -Force
Remove-Item "bin\publish" -Recurse -Force
```

---

## 3.3 — Verificar la especificación OpenAPI

Una vez desplegada, verifica que los endpoints OpenAPI están disponibles.

### Obtener la especificación JSON

Abre en el navegador o con `curl`:

```
https://func-contosoretail-<suffix>.azurewebsites.net/api/openapi/v3.json
```

Deberías ver un JSON con la estructura OpenAPI que describe los endpoints `HolaMundo`, `OrdersReporter` y `SqlExecutor`, incluyendo los esquemas de request/response.

### Explorar el Swagger UI

Navega a:

```
https://func-contosoretail-<suffix>.azurewebsites.net/api/swagger/ui
```

Desde la interfaz de Swagger UI puedes explorar los endpoints y probarlos interactivamente.

> **Importante:** La especificación OpenAPI documenta la API y sirve como referencia para entender qué parámetros enviar y qué respuesta esperar. El agente Anders usará esta información indirectamente a través de la Function Tool que definiremos en el siguiente paso.

---

## 3.4 — El agente Anders: Dos versiones de SDK

La implementación del agente Anders se proporciona en **dos versiones separadas**, cada una ubicada bajo `labs/foundry/code/agents/AndersAgent/`:

| Carpeta | SDK | Paradigma de API | Estado |
|---------|-----|------------------|--------|
| `ai-foundry/` | `Azure.AI.Projects` + `Azure.AI.Agents.Persistent` | Persistent Agents (threads, runs, polling) | GA — se conserva por retrocompatibilidad |
| `ms-foundry/` | `Azure.AI.Projects` + `Azure.AI.Projects.OpenAI` | Responses API (conversaciones, respuestas de proyecto) | **Preview** (a febrero 2026) — **recomendada** |

### ¿Por qué dos versiones?

A finales de 2025, Microsoft introdujo una **nueva experiencia para Microsoft Foundry** basada en la **Responses API** y una superficie de gestión de agentes rediseñada. Esta nueva experiencia — expuesta a través del paquete `Azure.AI.Projects.OpenAI` — reemplaza el modelo anterior de Persistent Agents (`Azure.AI.Agents.Persistent`) con un enfoque más ágil que utiliza **agentes con nombre y versionado**, **conversaciones** y la **Responses API** en lugar de threads y runs con polling.

Las diferencias clave entre ambos enfoques son:

| Aspecto | `ai-foundry/` (Persistent Agents) | `ms-foundry/` (Responses API) |
|---------|-----------------------------------|-------------------------------|
| **Ciclo de vida del agente** | Se crea con un ID generado; se busca por nombre iterando la lista | Se crea/actualiza por nombre con versionado explícito (`CreateAgentVersionAsync`) |
| **Modelo de conversación** | `PersistentAgentThread` + `ThreadRun` con polling | `ProjectConversation` + `ProjectResponsesClient` — respuesta síncrona |
| **Definición de herramientas** | `OpenApiToolDefinition` con clases tipadas | Protocol method vía `BinaryContent` (los tipos son internos en SDK 1.2.x) |
| **Patrón de chat** | Crear run → hacer polling hasta completar → leer mensajes | Una sola llamada a `CreateResponse()` retorna la salida directamente |

### ¿Cuál versión debo usar?

**Se recomienda la versión `ms-foundry/`** para desarrollo nuevo. Está alineada con la dirección de la plataforma Microsoft Foundry y ofrece un modelo de programación más simple — particularmente la eliminación del loop de polling en favor de una sola llamada síncrona de respuesta.

La versión `ai-foundry/` se conserva en este taller por **retrocompatibilidad**: los asistentes cuyos recursos de Azure AI Services fueron aprovisionados antes de que la nueva experiencia estuviera disponible pueden completar el lab usando la API de Persistent Agents.

> [!IMPORTANT]
> A febrero de 2026, el paquete `Azure.AI.Projects.OpenAI` y la Responses API están en **preview pública**. Las formas de la API, schemas de payload y tipos del SDK pueden cambiar antes de alcanzar disponibilidad general (GA). Si encuentras problemas como propiedades faltantes o renombradas (por ejemplo, el campo `kind` requerido en el payload de definición del agente), consulta las últimas [notas de versión de Azure.AI.Projects.OpenAI](https://www.nuget.org/packages/Azure.AI.Projects.OpenAI) para conocer los cambios que rompen compatibilidad.

---

### Entendiendo el código (versión `ms-foundry/` — recomendada)

Abre el archivo `labs/foundry/code/agents/AndersAgent/ms-foundry/Program.cs` y observa que está organizado en **3 fases**:

#### Fase 1 — Descargar la especificación OpenAPI

```csharp
var openApiSpecUrl = $"{functionAppBaseUrl}/openapi/v3.json";
var openApiSpec = await httpClient.GetStringAsync(openApiSpecUrl);
```

El programa descarga la especificación OpenAPI de la Function App **en tiempo de ejecución**. Esto significa que si la API cambia (nuevos endpoints, nuevos parámetros), el agente lo detecta automáticamente al reiniciarse.

#### Fase 2 — Verificar agente existente o crear uno nuevo

Esta fase tiene dos partes clave:

**Detección de agente existente:**

Antes de crear una nueva versión, el programa verifica si el agente ya existe llamando a `GetAgent`. Si lo encuentra, le pregunta al usuario si desea conservar el agente existente o sobreescribirlo con una nueva versión. Esto evita la proliferación innecesaria de versiones del agente durante el desarrollo iterativo.

**Definición del agente con herramienta OpenAPI (protocol method):**

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
                    description = "API de Contoso Retail...",
                    spec = openApiSpecJson,
                    auth = new { type = "anonymous" }
                }
            }
        }
    }
};
```

Dado que los tipos `OpenApiAgentTool` son internos en el SDK 1.2.x, la definición de la herramienta se construye como un objeto anónimo y se serializa vía `BinaryContent`. El campo `kind = "prompt"` es requerido por la API para indicar un agente basado en prompt.

**System prompt (instrucciones):**

El system prompt incluye el schema JSON exacto que Anders debe construir al invocar la API:

```json
{
  "customerName": "Nombre del Cliente",
  "startDate": "YYYY-MM-DD",
  "endDate": "YYYY-MM-DD",
  "orders": [
    {
      "orderNumber": "código de la orden",
      "orderDate": "YYYY-MM-DD",
      "orderLineNumber": 1,
      "productName": "nombre del producto",
      "brandName": "nombre de la marca",
      "categoryName": "nombre de la categoría",
      "quantity": 1.0,
      "unitPrice": 0.00,
      "lineTotal": 0.00
    }
  ]
}
```

> [!TIP]
> Incluir el schema en las instrucciones es una buena práctica cuando el agente debe construir payloads complejos. Aunque la especificación OpenAPI ya describe el schema, reforzarlo en el system prompt reduce significativamente los errores de formato.

**Reutilización del agente:**

```csharp
try
{
    existingAgent = projectClient.Agents.GetAgent(agentName);
    // Pregunta al usuario si desea sobreescribir o conservar
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    // Agente no encontrado — crear uno nuevo
}
```

Antes de crear una nueva versión del agente, el programa intenta recuperar el agente existente por nombre. Si lo encuentra, le pide al usuario que confirme si desea sobreescribirlo. Esto evita crear versiones innecesarias en Foundry al reiniciar la aplicación.

#### Fase 3 — Chat interactivo con Responses API

```csharp
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: agentName,
    defaultConversationId: conversation.Id);

ResponseResult response = responseClient.CreateResponse(input);
Console.WriteLine(response.GetOutputText());
```

El patrón de interacción en la versión `ms-foundry/` es más simple que el enfoque de Persistent Agents:
1. Se crea una `ProjectConversation` (el contexto de conversación)
2. Se obtiene un `ProjectResponsesClient`, vinculado al agente y la conversación
3. Cada mensaje del usuario se envía vía `CreateResponse()` que retorna la salida **síncronamente** — sin necesidad de loop de polling
4. El texto de respuesta se extrae con `GetOutputText()`

> **¿Qué pasa durante una llamada de respuesta?** Cuando el modelo decide que necesita llamar a la API, Foundry ejecuta la llamada HTTP automáticamente usando la especificación OpenAPI. El resultado se envía de vuelta al modelo, que formula la respuesta final al usuario. Todo esto ocurre dentro de la única llamada a `CreateResponse()` — el código simplemente recibe la respuesta terminada.

**Limpieza al salir:**

Cuando el usuario escribe `salir`, el loop de chat termina. El agente **persiste** en Foundry y se reutiliza automáticamente en la siguiente ejecución.

### Paso 1: Configurar `appsettings.json`

Abre el archivo `labs/foundry/code/agents/AndersAgent/ms-foundry/appsettings.json` y reemplaza los valores con los de tu entorno:

```json
{
  "FoundryProjectEndpoint": "<TU-AI-FOUNDRY-PROJECT-ENDPOINT>",
  "ModelDeploymentName": "gpt-4.1",
  "FunctionAppBaseUrl": "https://func-contosoretail-<suffix>.azurewebsites.net/api"
}
```

> **¿Dónde encuentro estos valores?**
> - **FoundryProjectEndpoint**: El `AI Foundry Endpoint` de la salida del despliegue.
> - **ModelDeploymentName**: `gpt-4.1` (nombre del deployment creado por el Bicep).
> - **FunctionAppBaseUrl**: La URL de tu Function App + `/api`.

### Paso 2: Compilar y ejecutar

```powershell
cd labs\foundry\code\agents\AndersAgent\ms-foundry
dotnet build
```

Asegúrate de que no haya errores de compilación. Luego ejecuta:

```powershell
dotnet run
```

Verás en consola que el agente verifica si ya existe una versión en Foundry. Si la encuentra, te preguntará si deseas conservarla o sobreescribirla. Si no existe, se crea un agente nuevo automáticamente.

### Paso 3: Inspeccionar el agente en Azure AI Foundry

**Antes de interactuar con Anders**, ve al portal para inspeccionar lo que se creó:

1. Abre [Azure AI Foundry](https://ai.azure.com) y navega a tu proyecto
2. En el menú lateral, selecciona **Agents**
3. Busca el agente **"Anders"** y haz clic en él

Observa dos cosas clave:

- **System prompt (instrucciones):** Verás las instrucciones completas que le dimos al agente, incluyendo el schema JSON. Esto es lo que guía su comportamiento al decidir cuándo y cómo invocar la API.
- **Tools (herramientas):** Verás **ContosoRetailAPI** listada como herramienta OpenAPI. Puedes expandirla para ver la especificación completa con el endpoint `ordersReporter`, los schemas de request/response, y la configuración de autenticación anónima.

> [!TIP]
> El system prompt y las tools son los dos pilares que determinan qué puede hacer un agente y cómo lo hace. Entender esta relación es clave para diseñar agentes efectivos.

### Paso 4: Probar el agente

De vuelta en la consola, pruébalo primero con un saludo:

```
Tú: Hola Anders, ¿qué puedes hacer?
```

Anders debería responder explicando que puede generar reportes de órdenes. Luego, prueba con datos reales (pega todo en una sola línea):

```
Tú: Genera un reporte para Izabella Celma (periodo: 1-31 enero 2026). Orden ORD-CID-069-001 (2026-01-04): Sport-100 Helmet Black, Contoso Outdoor, Helmets, 6x$34.99=$209.94 | HL Road Frame Red 62, Contoso Outdoor, Road Frames, 10x$1431.50=$14315.00 | Long-Sleeve Logo Jersey S, Contoso Outdoor, Jerseys, 8x$49.99=$399.92. Orden ORD-CID-069-003 (2026-01-08): HL Road Frame Black 58, Contoso Outdoor, Road Frames, 3x$1431.50=$4294.50 | HL Road Frame Red 44, Contoso Outdoor, Road Frames, 7x$1431.50=$10020.50. Orden ORD-CID-069-002 (2026-01-17): HL Road Frame Red 62, Contoso Outdoor, Road Frames, 2x$1431.50=$2863.00 | LL Road Frame Black 60, Contoso Outdoor, Road Frames, 4x$337.22=$1348.88.
```

Lo que ocurre internamente:
1. Anders analiza el mensaje y decide que necesita llamar al endpoint `ordersReporter`
2. **Foundry ejecuta la llamada HTTP** automáticamente a la Function App con los datos estructurados según el schema
3. La Function App genera el reporte HTML, lo sube a Blob Storage y retorna la URL
4. Foundry envía el resultado de vuelta al modelo
5. Anders formula su respuesta y presenta la URL al usuario

Abre la URL del reporte en el navegador para verificar que se generó correctamente.

Ahora prueba con un caso más sencillo — un solo pedido con dos productos:

```
Tú: Genera un reporte para Marco Rivera (periodo: 5-10 febrero 2026). Orden ORD-CID-112-001 (2026-02-07): Mountain Bike Socks M, Contoso Outdoor, Socks, 3x$9.50=$28.50 | Water Bottle 30oz, Contoso Outdoor, Bottles and Cages, 1x$6.99=$6.99.
```

> **Nota:** Al escribir `salir`, solo se termina la conversación. El agente **persiste** en Foundry y se reutiliza automáticamente en la siguiente ejecución.

---

## Solución de problemas

### Storage Account bloqueado por política (error 503)

En suscripciones con políticas estrictas de Azure, el Storage Account que respalda la Function App puede tener su **acceso público de red deshabilitado** automáticamente después del aprovisionamiento. Esto impide que el host de Functions alcance su propio almacenamiento, causando un error persistente **503 (Site Unavailable)** — aunque la app reporte como `Running` y `Enabled`.

**Síntomas:**
- La Function App aparece como `Running` en el Portal de Azure y CLI
- Todas las restricciones de acceso de red muestran "Allow all"
- Cada solicitud HTTP a cualquier endpoint retorna 503 después de un timeout de ~60 segundos

**Diagnóstico:**
```powershell
az storage account show --name stcontosoretail<suffix> --resource-group rg-contoso-retail --query "publicNetworkAccess" -o tsv
```

Si retorna `Disabled`, esa es la causa raíz.

**Solución:**

Se incluye un script de conveniencia en el repositorio:

```powershell
cd labs/foundry/setup
.\unlock-storage.ps1
```

El script detecta automáticamente el sufijo desde la Function App. Si necesitas forzarlo, también acepta `-Suffix` o `-FunctionAppName`.

Este script habilita el acceso público de red en el Storage Account y reinicia la Function App. Ver [unlock-storage.ps1](setup/unlock-storage.ps1) para detalles.

---

## Siguiente paso

Continúa con el [Lab 4 — Julie (Planner Agent)](lab04-julie-planner-agent.md).
