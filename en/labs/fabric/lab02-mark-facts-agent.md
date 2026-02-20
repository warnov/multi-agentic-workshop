# Lab 02: Mark Facts Agent


# MF - Mark

Microsoft Fabric - Creación y configuración de agente de datos llamado Mark

## 🎯 Mission Brief

En este laboratorio aprenderás a construir un agente de datos que reconoce e interpreta lenguaje natural utilizando Microsoft Fabric. A lo largo de la guía, crearás un agente de datos en Microsoft Fabric que podrá responder, vía lenguaje natural, preguntas sobre el modelo de datos de órdenes de ventas que preparaste en el paso anterior. Siguiendo las instrucciones paso a paso, obtendrás experiencia práctica en la preparación de este agente de datos para su posterior utilización por Copilot Studio.

## 🔎 Objetivos

Al completar este laboratorio lograrás:

1. Crear el agente de datos llamado "Mark".
2. Revisión y prueba de las respuestas a preguntas.
3. Publicación del agente de datos.
4. Usar el modelo Semántico como Fuente de datos del Data Agent

En la siguiente sección, se presentan los pasos del laboratorio:

---

## 1. Crear el agente de datos llamado "Mark".

### a. Seleccionar la opción para crear un nuevo item

![Nuevo Item](images/M1.a.png)

### b. Buscar "Agent"

### c. Seleccionar el "Data Agent (preview)"


![Tipo de Nuevo Item](images/M1.c.png)

### d. Darle el nombre de "Mark" y seleccionar "Create"


![Nombre de Agente](images/M1.d.png)

### e. Agregar "Data Source"

![Data Source](images/M1.e.png)

### f. Seleccionar la "SQL database" creada en el laborario anterior.

![SQL Database](images/M1.f.png)

### g. Seleccionar solo las siguientes tablas:

i. customer  
ii. orderline  
iii. orders  
iv. product  

![Tablas de la BD](images/M1.g.png)

---

## 2. Revisión y prueba de las respuestas a preguntas.

### a. En la sección de "Test the agent's response", prueba las siguientes preguntas en la interfaz disponible para el agente

![Test Mark](images/M2.a.png)

i. what are the orders from Omar Bennett? / ¿Cuáles son las órdenes de Omar Bennett?  
ii. what are the orders from Omar Bennett and the detailed products for each order? / ¿Cuáles son las órdenes de Omar Bennett y el detalle de productos de cada orden?  
iii. What are the order from customer CID-069 from June 2019 to May 2021? / ¿Cuáles son las órdenes para el cliente CID-069 entre julio del 2019 a mayo del 2021?  
iv. What are the historical trends across all my data? / ¿Cuáles son las tendencias históricas en todos mis datos?

v. What are the product details for order F100241? ¿ Cuáles son los detalles de los productos de la orden F100241?

![Test Mark](images/M2.a.v.png)

Si al tratar de recuperar los productos no obtienes respuesta, realizar el paso b

---

### b. Ajustar el comportamiento del Agente en la sección de "Agent Instructions".

La sección Agent Instructions define el meta-prompt del Data Agent: establece cómo debe razonar, qué contexto de negocio usar y cómo responder. No ejecuta consultas, pero guía todo el razonamiento, ayudando a producir respuestas más precisas, con prioridad correcta de fuentes, mejor interpretación de la intención del usuario y un formato/estilo esperado.

Para más información sobre Agent Instructions puedes consultar [Data agent configurations](https://learn.microsoft.com/en-us/fabric/data-science/data-agent-configurations#data-agent-instructions).

![Agent Instructions empty](images/M2.b.png)

---

### i. Agregar las siguientes instrucciones en la sección de "Agent Instructions"

```markdown 

These instructions are for the overall data agent and will always be sent regardless of the question asked.
Explain:
- Rules for planning how to approach each question
- Which data sources to use for different topics
- Any terminology or acronyms with consistent meanings across all connected data sources
- Tone, style, and formatting for finished responses

## General knowledge

This Data Agent answers questions about **orders**, **customers**, and **products**, using a transactional relational data model.

The data model consists of the following main tables:

- **customers**: customer information.
- **orders**: general order information (order header).
- **orderline**: detailed list of products included in each order.
- **products**: product catalog.

---

## Key relationships (mandatory joins)

The agent must always respect the following relationships when generating queries:

1. **Customer → Orders**
   - `customers.customerId = orders.customerId`
   - One customer can have multiple orders.

2. **Orders → Order details**
   - `orders.orderId = orderline.orderId`
   - One order can contain multiple product lines.

3. **Order details → Products**
   - `orderline.productID = products.productID`
   - Each order line references a product from the catalog.

When a query involves customers, orders, and products, the agent must traverse the full chain:


---

## Reasoning principles

- Questions about **customers** must start from the `customers` table.
- Questions about **orders** must use `orders` as the main table.
- Questions about **order details** must join `orders` with `orderline` and `products`.
- Questions about **products purchased** or **what a customer bought** must use `orderline` as the central table, filtering by customer through `orders`.
- If a question is ambiguous (for example, no specific order is provided), the agent should return a **reasonable summary** and clearly explain the criteria used.

---

## Table descriptions

### customers
- Purpose: stores customer information.
- Primary key: `customerId`.
- Contains descriptive customer attributes such as name, email, segment, country, etc.

### orders
- Purpose: represents the order header.
- Primary key: `orderId`.
- Foreign key: `customerId`.
- Contains general information such as order date, status, and order total.

### orderline
- Purpose: stores detailed product information per order.
- Foreign keys:
  - `orderId` → orders
  - `productID` → products
- Contains quantity, prices, discounts, taxes, and line totals.

### products
- Purpose: master product catalog.
- Primary key: `productID`.
- Contains attributes such as product name, category, and product characteristics.

---

## When asked about

### Customers
- Use `customers` as the primary table.
- If orders or purchases are required, join with `orders` using `customerId`.

### Orders for a customer
- Filter `orders` by `orders.customerId`.
- Enrich results with customer information from `customers`.

### Order details
- Use `orders` for general order information.
- Join with `orderline` for details and `products` for product information.

### Products purchased by a customer
- Use `orderline` as the central table.
- Join with `orders` to filter by customer.
- Join with `products` to retrieve product details.

---


```



iii. Al finalizar la configuración del comportamiento del agente se tendrá la sección de "Agent Instructions" de esta manera

![Agent Instructions](images/M2.b.3.png)

iv. Probar nuevamente el agente con las instrucciones agregadas:
1. limpiar el chat
2. confirmar que se quiere limpiar el chat

![Limpiar chat](images/M2.b.4.png)

v. Probar nuevamente el agente con la pregunta que no se pudo resolver: What are the product details for order F100241?

![Nueva sesión de chat](images/M2.b.5.png)

---
## 3. Publicación del agente de datos.

### a. Seleccionar "Publish" en el menu de opciones del agente

![Publicar Agente](images/M3.a.png)

### b. Agregar una descripción que detalle el obejtivo esperado cuando sea utilizado en Copilot Studio

### c. Seleccionar la opción para que se publique en "Agent Store in Microsoft 365 Copilot"

![Publicar Agente](images/M3.c.png)

---

## 4. Usar el modelo Semántico como Fuente de datos del Data Agent (Opcional)

Implementar el punto 4 de [Data setup](lab01-data-setup.md)

### a. Puedes crear un nuevo data Agent o Eliminar la fuente de datos de Mark


i. Eliminar la fuente de datos de Mark

![Eliminar nueva fuente de datos](images/M4.a.png)

ii. Eliminar la instrucciones de la sección "Agent Instructions"

### b. Agregar la nueva fuente de datos
![Agregar nueva fuente de datos](images/M4.b.png)

### c. Seleccionar el modelo semántico

![Seleccionar el modelo semántico](images/M4.c.png)

### d. Incluir las tablas Customer, Orders, Orderline, y Product
![seleccionar tablas](images/M4.d.png)

### e. Revisa el agente y si no responde de la forma esperada, agrega las instrucciones en la sección Agent Instructions.

### f. Si deseas puedes publicar una nueva versión del data agent o dejar la versión construida en el punto anterior

