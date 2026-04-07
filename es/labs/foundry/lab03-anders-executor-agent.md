# Lab 3: Anders — Executor Agent

## Tabla de contenido

- [Lab 3: Anders — Executor Agent](#lab-3-anders--executor-agent)
  - [Tabla de contenido](#tabla-de-contenido)
  - [Introducción](#introducción)
    - [¿Qué vamos a hacer en este lab?](#qué-vamos-a-hacer-en-este-lab)
    - [Verificar endpoints expuestos](#paso-2-verificar-endpoints-expuestos)
    - [Endpoints OpenAPI generados](#endpoints-openapi-generados)
  - [3.2 — Verificar la especificación OpenAPI](#32--verificar-la-especificación-openapi)
    - [Obtener la especificación JSON](#obtener-la-especificación-json)
    - [Explorar el Swagger UI](#explorar-el-swagger-ui)
  - [3.3 — El agente Anders](#33--el-agente-anders-dos-versiones-de-sdk)
    - [Paso 1: Entendiendo el código (`ms-foundry/`)](#entendiendo-el-código-versión-ms-foundry--recomendada)
    - [Paso 2: Inspeccionar el agente en Azure AI Foundry](#paso-3-inspeccionar-el-agente-en-azure-ai-foundry)
    - [Paso 3: Probar el agente](#paso-4-probar-el-agente)
  - [Notas Importantes](#notas-impotantes)

---

## Introducción

Anders es el **agente ejecutor** de la arquitectura multi-agéntica de Contoso Retail. Su rol es recibir solicitudes de acciones operativas — como la generación y publicación de reportes de órdenes — y ejecutarlas interactuando con servicios externos como la Azure Function `FxContosoRetail`.

Para que Anders pueda interactuar con la API de Contoso Retail, usaremos una **Microsoft Foundry Tool** que permite al agente descubrir e invocar automáticamente los endpoints de la Function App a partir de su especificación OpenAPI. Adicionalmente, agregaremos soporte **OpenAPI** a la Function App para documentar la API y facilitar la exploración de sus endpoints.

### ¿Qué vamos a hacer en este lab?

### Paso 1: Verificar endpoints expuestos

Abre [`es/labs/foundry/code/api/FxContosoRetail/FxContosoRetail.cs`](../code/api/FxContosoRetail/FxContosoRetail.cs) y confirma que existen estos endpoints:

- `HolaMundo`
- `OrdersReporter`
- `SqlExecutor`

Además, valida que `OrdersReporter` y `SqlExecutor` tengan atributos OpenAPI (`OpenApiOperation`, `OpenApiRequestBody`, `OpenApiResponseWithBody`). Justamente estos cambios son los que necesitarás hacer cuando desees exponer tus Azure Functions existentes como herramientas OpenAPI para los agentes.

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

## 3.2 — Verificar la especificación OpenAPI

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

## 3.3 — El agente Anders

La implementación del agente Anders se proporciona bajo `es/labs/foundry/code/agents/AndersAgent/ms-foundry`:

### Entendiendo el código (versión `ms-foundry/` — recomendada)

Abre el archivo `es/labs/foundry/code/agents/AndersAgent/ms-foundry/Program.cs` y observa como está organizado

### Paso 1: Configuración del Entorno en GitHub Codespaces

1. Abre el repositorio en **GitHub Codespaces**.
2. Autentícate en tu cuenta de Azure desde la terminal:
   ```bash
   az login --use-device-code
   ```

### Paso 2: Variables de Entorno de Azure AI Foundry

Desde el portal de Azure AI Foundry, navega a la sección Overview de tu proyecto y obtén los siguientes valores para configurarlos en tu terminal de Codespaces:

# Reemplaza los valores entre comillas con tu información real

```
export FOUNDRY_PROJECT_ENDPOINT="https://ais-contosoretail-XXXX.services.ai.azure.com/..."
export FOUNDRY_MODEL_DEPLOYMENT_NAME="gpt-4.1"
```

Las siguientres variables de entorno son opcionales, no es necesario crearlas. Pero si lo consideras necesario lo puede hacer para cambiar el nombre del agente y su compotanmiento.

```
export FOUNDRY_AGENT_NAME="AndersAgent"
export FOUNDRY_AGENT_INSTRUCTIONS="Eres un agente analítico especializado en lectura, comprensión y extracción de insights a partir de información proporcionada."
```

### Paso 3: Despliegue del Agente

Ejecuta el script de despliegue para registrar el agente en el servicio:

./deploy-foundry-agent.sh

### Paso 4: Configuración de Herramientas (Portal)

Una vez desplegado, sigue estos pasos en el portal web:

Ve a Build > Agents y selecciona AndersAgent.

Añadir Herramienta OpenAPI:

Haz clic en Add Tool > OpenAPI tool.

Ingresa el nombre de la herramienta (ej. myapitool).

Pega la definición JSON de la API (Swagger/OpenAPI) de tu servicio (ej. una Azure Function que consulta pedidos).

```
https://func-contosoretail-<suffix>.azurewebsites.net/api/openapi/v3.json
```

Haz clic en Create tool.

Activar Code Interpreter: Asegúrate de que el interruptor de "Code Interpreter" esté encendido para permitir cálculos matemáticos complejos sobre los datos.


### Paso 5: Inspeccionar el agente en Azure AI Foundry

**Antes de interactuar con Anders**, ve al portal para inspeccionar lo que se creó:

1. Abre [Azure AI Foundry](https://ai.azure.com) y navega a tu proyecto
2. En el menú lateral, selecciona **Agents**
3. Busca el agente **"Anders"** y haz clic en él

Observa dos cosas clave:

- **System prompt (instrucciones):** Verás las instrucciones completas que le dimos al agente, incluyendo el schema JSON. Esto es lo que guía su comportamiento al decidir cuándo y cómo invocar la API.
- **Tools (herramientas):** Verás **ContosoRetailAPI** listada como herramienta OpenAPI. Puedes expandirla para ver la especificación completa con el endpoint `ordersReporter`, los schemas de request/response, y la configuración de autenticación anónima.

> [!TIP]
> El system prompt y las tools son los dos pilares que determinan qué puede hacer un agente y cómo lo hace. Entender esta relación es clave para diseñar agentes efectivos.

### Paso 6: Validación y Pruebas

Inicia una sesión de chat en el Playground del agente y prueba con prompts de negocio como:

```
"Genera un reporte para el cliente Marco Rivera del periodo de enero a febrero de 2026. Muestra los pedidos en una tabla y calcula el total gastado."
```

Resultados Esperados:
El agente debe identificar qué herramienta llamar, procesar los datos y gGenerar una tabla de salida y un enlace de descarga para el reporte.


### Paso 7: Validación y Pruebas
Inicia una sesión de chat en el Playground del agente y prueba con prompts de negocio como:

```
Tú: Hola Anders, ¿qué puedes hacer?
```

Anders debería responder explicando que puede generar reportes de órdenes. Luego, prueba con datos reales (pega todo en una sola línea):

```
Genera un reporte para Izabella Celma (periodo: 1-31 enero 2026). Orden ORD-CID-069-001 (2026-01-04): Sport-100 Helmet Black, Contoso Outdoor, Helmets, 6x$34.99=$209.94 | HL Road Frame Red 62, Contoso Outdoor, Road Frames, 10x$1431.50=$14315.00 | Long-Sleeve Logo Jersey S, Contoso Outdoor, Jerseys, 8x$49.99=$399.92. Orden ORD-CID-069-003 (2026-01-08): HL Road Frame Black 58, Contoso Outdoor, Road Frames, 3x$1431.50=$4294.50 | HL Road Frame Red 44, Contoso Outdoor, Road Frames, 7x$1431.50=$10020.50. Orden ORD-CID-069-002 (2026-01-17): HL Road Frame Red 62, Contoso Outdoor, Road Frames, 2x$1431.50=$2863.00 | LL Road Frame Black 60, Contoso Outdoor, Road Frames, 4x$337.22=$1348.88.
```

Lo que ocurre internamente:
1. Anders analiza el mensaje y decide que necesita llamar al endpoint `ordersReporter` por medio del tool configurado.
2. **Foundry Tool ejecuta la llamada HTTP** automáticamente a la Function App con los datos estructurados según el schema
3. La Function App genera el reporte HTML, lo sube a Blob Storage y retorna la URL
4. Foundry envía el resultado de vuelta al modelo
5. Anders formula su respuesta y presenta la URL al usuario

Abre la URL del reporte en el navegador para verificar que se generó correctamente.

Ahora prueba con un caso más sencillo — un solo pedido con dos productos:

```
Tú: Genera un reporte para Marco Rivera (periodo: 5-10 febrero 2026). Orden ORD-CID-112-001 (2026-02-07): Mountain Bike Socks M, Contoso Outdoor, Socks, 3x$9.50=$28.50 | Water Bottle 30oz, Contoso Outdoor, Bottles and Cages, 1x$6.99=$6.99.
```

---

## Notas Importantes

Cierre de sesión: Al finalizar, recuerda detener tu Codespace para evitar consumos innecesarios.

Regiones: Verifica que tu proyecto de Foundry esté en una región que soporte agentes (ej. East US, East US 2).

## Siguiente paso

Continúa con el [Lab 5 — Laboratrios de Copilot Studio](lab05-mcs-setup.md).
