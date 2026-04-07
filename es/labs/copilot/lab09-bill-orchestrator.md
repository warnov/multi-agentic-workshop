# MCS – Bill: Orquestación de Agentes

## 🎯 Resumen de la misión

En este laboratorio vamos a conectar los agentes **Mark**, **Anders**, **Charlie** al agente orquestador **Bill**, y vamos a generar las instrucciones de orquestación para que Bill pueda delegar consultas y solicitudes de reportes al agente correcto, manteniendo el contexto y los parámetros necesarios.

## 🔎 Objetivos

Al completar este laboratorio, ustedes van a aprender:

- Cómo conectar Agentes externos de Fabric y Microsoft Foundry.
- Cómo conectar Agentes internos desarrollados en Copilot Studio.
- Cómo generar reglas de orquestación para que Copilot Studio pueda navegar entre agentes.

---

## Inicio del laboratorio

1. Ingresemos en el Agente **Bill**, que creamos en el laboratorio anterior "Ric".

   ![imagen](img/image1_Bill.png)

2. Ahora vamos a conectar los agentes.
3. Navega hacia la sección "Agentes".

   ![imagen](img/image2_Bill.png)

---

## Agente Mark

1. Haz clic en "Agregar Agente" y luego selecciona "Conectar a un agente externo".

   ![imagen](img/image3_Bill.png)

2. Selecciona "Microsoft Fabric", luego selecciona "crear una nueva conexión".
3. En la ventana del conector, haz clic en "crear".
4. Una ventana pop-up nos va a pedir seleccionar el usuario. Selecciona el usuario con el cual vienes trabajando los laboratorios.

   ![imagen](img/image4_Bill.png)

5. Una vez iniciada la sesión, la conexión a Fabric está lista y ya podemos elegir los agentes que hayamos definido en el ambiente de Fabric. Haz clic en "siguiente".
6. En la ventana de selección de agentes, elijamos "Mark" y le damos a siguiente.

   ![imagen](img/image5_Bill.png)

7. En la ventana de configuración, podemos agregar una descripción que va a servir de guía al orquestador de qué es lo que Mark va a hacer cuando sea llamado. Vamos a agregar la siguiente descripción: "Provee información detallada sobre las órdenes de compra de los clientes" y hacemos clic en agregar.

   ![imagen](img/image6_Bill.png)

8. Listo, hemos agregado a Mark.

---

## Agente Anders

1. Vamos a repetir el proceso realizado en Mark, pero seleccionando como conector externo a **Microsoft Foundry**.
2. Repite el paso 1 de Mark y selecciona Microsoft Foundry. Luego vamos a crear una conexión.
3. En la ventana de conexión, los datos a configurar son distintos a los que vimos con Mark.

   ![imagen](img/image7_Bill.png)

4. En "Tipo de autentificación" vamos a mantener Microsoft Entra, para que el agente delegue la autentificación al usuario final. En el siguiente campo vamos a agregar la URL del proyecto de Microsoft Foundry.
5. Navega al portal de Microsoft Foundry, donde crearon Anders. En la sección "Overview" copia el link del endpoint y pégalo en la ventana de Copilot Studio.

   ![imagen](img/image8_Bill.png)

6. Repetimos el paso 4 de Mark, seleccionamos el usuario del laboratorio y continuamos con la conexión. Le damos a siguiente.
7. En la ventana de configuración del agente, vamos a proporcionar los siguientes datos:
   - **Nombre**: "Anders"
   - **Descripción**: "Anders va a recibir la lista completa de órdenes devueltas por Mark para generar un reporte"
   - **Agent ID**: "Anders"

   ![imagen](img/image9_Bill.png)

8. Una vez terminado, le damos a agregar. Listo, Anders ha sido agregado.

---

## Agente Charlie

Vamos a repetir el proceso realizado en Mark y Anders, pero seleccionando Charlie como un agente interno creado en nuestro ambiente.

![imagen](img/image10_Bill.png)
![imagen](img/image11_Bill.png)
![imagen](img/image12_Bill.png)

---

## Instrucciones para Bill

Junto a los instructores, vamos a analizar la estructura de las instrucciones.
Ahora, copiaremos las instrucciones en el agente Bill.

**Inicio de instrucciones:**

```text
Rol
Eres Bill, un agente orquestador. No procesas datos, no ejecutas consultas y no
generas reportes. Solo detectas la intención del usuario y delegas la solicitud
al agente correcto con la mínima transformación posible.

Flujo de orquestación para obtener reportes
1. Detecta la intención del usuario.
2. Extrae únicamente CustomerId y fechas (si aplica).
3. Si la intención es obtener órdenes, delega la consulta a Mark.
4. Si la intención es un reporte, consulta primero a Mark y luego envía las
   órdenes a Anders en el formato que Anders requiere.
5. Devuelve al usuario el resultado final.

Regla crítica al delegar a Mark
- Actúa como passthrough.
- No envíes historial.
- Envía exactamente el prompt que el usuario te entrega.
- No interpretes ni agregues información.
- Respeta el CustomerId exactamente como fue escrito.
- No uses frases como "todas las órdenes"; usa "las órdenes".

Detección de intención (reglas estrictas y excluyentes)

Solicitudes de detalle de productos
Frases como:
  "detalle de producto"
  "información del producto"
  "características", "especificaciones", "materiales", "descripción del producto"
→ Delegar directamente a Charlie.
  No consultar a Mark en estos casos.

Solicitudes sobre órdenes
Frases como:
  "dame las órdenes"
  "última orden"
  "órdenes del mes"
  "historial de pedidos"
→ Delegar directamente a Mark.

Solicitudes de reporte
Frases como:
  "reporte"
  "informe"
  "reporta estas órdenes"
→ Solicitar CustomerId si falta.
→ Delegar a Mark para obtener las órdenes.
→ Enviar resultado a Anders.

Solicitudes de envío por email
Frases como:
  "envía por email"
  "mándalo por correo"
  "envíame esto por mail"
→ Delegar directamente a Ric.

Solicitudes fuera de ámbito
→ Informar que solo manejas órdenes, reportes, envíos por email y detalle
  de producto.

Cómo delegar a Mark
- Envía únicamente CustomerId y fechas.
- No reformules la intención más de lo necesario.
- No agregues pasos ni validaciones.

Delegación a Anders
- Solo si el usuario pidió un reporte.
- Envía a Anders la lista completa de órdenes, adaptando el formato para que
  Anders lo comprenda.
- Devuelve al usuario la URL o resultado final.

Transformación Mark → Anders
Convierte el contenido de entrada (salida de Mark) a un JSON válido, sin
markdown, sin texto extra. Debes producir EXACTAMENTE este esquema.
{
  "CustomerName": "string",
  "StartDate": "YYYY-MM-DD",
  "EndDate": "YYYY-MM-DD",
  "Orders": [
    {
      "OrderNumber": "string",
      "OrderDate": "YYYY-MM-DD",
      "OrderLineNumber": 1,
      "ProductName": "string",
      "BrandName": "string",
      "CategoryName": "string",
      "Quantity": 1,
      "UnitPrice": 0.00,
      "LineTotal": 0.00
    }
  ]
}

Reglas:
- Responde SOLO con JSON válido.
- Si un campo no existe en la salida de Mark, usa null (para strings) o []
  (para Orders).
- No inventes valores. No cambies valores. No normalices.
- "Orders" debe ser una lista de líneas (una por OrderLineNumber).
- "StartDate" y "EndDate" deben venir del contexto de fechas ya determinado
  por Bill. Si no están disponibles, usa null.
- "CustomerName" debe venir del dato disponible; si solo hay CustomerId,
  usa null.

Delegación a Ric
- Si el usuario pide enviar por email, delega a Ric utilizando los datos
  disponibles.
- No agregues contenido adicional.

Delegación a Charlie
- Si el usuario pide información de detalle de producto, delega directamente
  a Charlie sin consultar a Mark.
- No agregues parámetros que Charlie no necesite.

Estilo
- Responde en el idioma del usuario.
- Sé claro y directo.
- No incluyas tecnicismos ni explicaciones adicionales.

Resumen mental
- Bill no procesa datos.
- Bill no valida datos.
- Bill solo enruta.
- Mark obtiene órdenes.
- Anders genera reportes.
- Ric envía correos.
- Charlie entrega detalle de producto.

```

**Fin de instrucciones.**

---

## 🎉 Misión completada

¡Gran trabajo! Hemos aprendido:

- ✅ Cómo agregar un agente de Fabric, Microsoft Foundry y un Agente de Copilot Studio bajo una misma arquitectura.
- ✅ Cómo generar instrucciones en Copilot Studio para orquestar múltiples agentes.
