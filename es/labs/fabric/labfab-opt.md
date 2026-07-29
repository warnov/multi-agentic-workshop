# Lab Fabric Opcional: Crear un agente usando un Modelo Semantico

## 1. Crear Modelo Semántico 

En Microsoft Fabric, un modelo semántico es la capa de negocio que da significado a los datos técnicos y los hace fáciles de analizar, reutilizar y gobernar.

a. Ir al workspace

![ir al Workspace](images/sm4.a.png)

b. Abrir el SQL Analytics Endpoint de la base de datos db_retail

![SQL Analytics Endpoint ](images/sm4.b.png)

c. Crear un nuevo modelo semántico

![Nuevo modelo semántico](images/sm4.c.png)

d. Configurar el modelo semántico:

i. Nombre: sm_retail  
ii. Workspace correspondiente  
iii. Tablas: customer, orders, orderline, product  
iv. Confirmar


![Configuración modelo semántico](images/sm4.d.png)

e. Abrir el modelo semántico creado

![Abrir modelo semántico](images/sm4.e.png)

f. Cambiar a la vista de edición


![Vista edición](images/sm4.f.png)

g. Crear relaciones del modelo semántico:

![Nueva relación](images/sm.4.g.png)
Agregar relación 
![Vista edición](images/sm4.g.1.png)

i. Customer → Orders (1:*)  

![Customer → Orders](images/sm4.g.2.png)

ii. Orders → Orderline (1:*)  

![Orders → Orderline](images/sm4.g.3.png)

iii. Orderline → Product (1:1)

![Orderline → Product](images/sm4.g.4.png)


h. Resultado final del modelo semántico


![Modelo semántico](images/sm4.g.5.png)

---


## 2. Usar el modelo Semántico como Fuente de datos del Data Agent

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

## 3. Challenge

Ir a [Challenge](Challenge.md)


## Mission Complete

