# Lab 01: Data Setup

# MF – Environment Setup


MF – Environment Setup  

Microsoft Fabric – Setup del Ambiente


## 🎯 Mission Brief

En este laboratorio aprenderás a construir la base de tu plataforma de datos utilizando Microsoft Fabric. A lo largo de la guía, crearás la capacidad de Fabric que servirá como entorno central para alojar la base de datos y administrar la información de manera organizada y escalable. Posteriormente, desarrollarás el modelo semántico, habilitando que los datos puedan ser consumidos de forma eficiente por diferentes experiencias analíticas y de inteligencia artificial.

Siguiendo las instrucciones paso a paso, obtendrás experiencia práctica en la preparación de datos y en la creación de una base sólida que permitirá su integración con soluciones como Copilot y agentes de IA.

## 🔎 Objetivos

Al completar este laboratorio lograrás:

1. Crear la capacidad de Microsoft Fabric "wsfbcagentic".
2. Crear el workspace "wsfcagentic". El nombre debe ser único, por lo tanto concatena el nombre de tu usuario a "wsfcagentic"
3. Crear base de datos SQL "db_retail" y cargar los datos.
4. Crear Modelo Semántico sobre los datos cargados a la base de datos "db_retail".

En la siguiente sección, se presentan los pasos del laboratorio:

---

## 0 Registrar Microsoft.Fabric como recurso en la subscripción

a. Abrir Suscripción en Azure Portal

![Abrir suscripción](images/0.1.png)

b. Registrar el recuerso en la suscripción
![Registrar Fabric en la suscripción](images/0.2.png)

## 1. Crear tu capacidad de Microsoft Fabric

a. Inicia sesión en [Microsoft Azure](https://portal.azure.com/#home)

b. Buscar el servicio de Microsoft Fabric y seleccionarlo

![Buscar Servicio](images/1.1.png)
c. Dar clic en Crear una nueva capacidad de Microsoft Fabric

![Crear Capacidad](images/1.1.c.png)

d. Crear un grupo de recursos para la capacidad de Microsoft Fabric

![Crear Grupo de Recursos](images/1.2.png)

e. Establecer la configuración que se va a crear:

i. Definir nombre. El nombre debe ser único, por lo tanto concatena el nombre de tu usuario a "wsfcagentic".
ii. Seleccionar región  
iii. Cambiar tamaño de capacidad  
iv. Seleccionar tamaño de capacidad  
v. Revisar la configuración

![Validación](images/1.3.e.png)

f. Una vez validada la configuración, proceder a crear la capacidad de Microsoft Fabric

![Crear Capacidad](images/1.6.png)

g. Una vez finalice la creación de la capacidad, ya puedes ir al recurso

![Explorar el recuros](images/1.7.png)

h. Explorar el recurso de Microsoft Fabric desplegado

i. Iniciar o pausar la capacidad  
ii. Cambiar el tamaño de la capacidad  
iii. Nombrar nuevos administradores de la capacidad


![Crear Capacidad](images/1.8.png)

---

## 2. Crear tu workspace "wsfcagentic"

a. Iniciar sesión en [Microsoft Fabric](https://app.fabric.microsoft.com/)

b. Ir al tab de Workspaces y seleccionar Nuevo Workspace


![Crear Capacidad](images/2.1.png)

c. Especificar la configuración del workspace

![Crear Capacidad](images/2.2.png)

d. Especificar el tipo de workspace (Fabric)

![Crear Capacidad](images/2.3.png)

e. Seleccionar la capacidad de Fabric que usará el workspace. Solo aparecerán las capacidades que se encuentren encendidas. 


![Crear Capacidad](images/2.4.png)

f. Al finalizar la configuración especificar que la capacidad usará el fomrato de almacenamiento por defecto y aplicar los cambios para crear el workspace.  Para mayor información sobre Large semantic models in Power BI Premium consultar el [link](https://learn.microsoft.com/es-es/fabric/enterprise/powerbi/service-premium-large-models#enable-large-models).

![Crear Capacidad](images/2.5.png)

f. Una vez se haya creado el workspace tendrá una area de trabajo que luce como la siguiente imagen:

![Crear Capacidad](images/2.6.png)
 
---

## 3. Crear Base de Datos y Cargar Datos

a. Seleccionar la opción para crear un nuevo item

![Nuevo Item](images/3.1.png)

b. Filtrar por SQL database y seleccionar la opción SQL database como se muestra en la imagen

![Buscar SQL Data Base](images/3.2.png)

c. Asignar el nombre db_retail y crear la base de datos

![Crear BD](images/3.3.png)

d. Una vez creada la base datos tendrás un nuevo tab abierto y este te permitirá acceder a la base de datos rapidamente. Asimimso, podrás navegar rapidamente sobre los elementos de la base de datos, como tablas, vistas, procedimientos almacenados, funciones, etc, a través del explorador de objetos. 


![Explorar BD](images/3.4.png)

e. Abrir una pestaña New Query para ejecutar scripts SQL

![Nueva Consulta](images/3.5.png)

f. Para crear las tablas con sus respectivos datos, copier el código SQL contenido en el archivo [Create database.sql ](SQLScripts/CreateDatabase.sql) y ejecutarlo dando click en la opción Run. 

![Creación de tablas e inserción de datos](images/3.6.png)

g. Confirmar ejecución correcta del script

![Script ejecutado correctamente](images/3.7.png)

h. Para terminar de ajustar los datos, por favor en la pestaña SQL Query 1, reemplace el código SQL que ya fue ejecutado en el anterior paso por el código del archivo [Update Dates.sql](SQLScripts/UpdateDates.sql)  y ejecutelo. 

![Abrir pestaña de ejecución de Código SQL](images/3.8.png)

i. Después de ejecutarlo se mostrará que como resultado se han afectado varias filas de las tablas SQL. Este script solo se encarga de hacer ajustes sobre fechas de los datos de la base de datos. 

![Actualización de datos](images/3.9.png)

---

## 4. Crear Modelo Semántico (opcional)

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

## Mission Complete

Tu plataforma de datos ha sido creada y tus datos están listos para ser procesados y consumidos por agentes de IA.
