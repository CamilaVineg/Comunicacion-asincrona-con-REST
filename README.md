# Ejercicio 1 — Comunicación síncrona con REST

**Sistemas Distribuidos — UADER.** Práctica de comunicación por paso de mensajes.

---

## 1. Qué pide la consigna

> **Ejercicio 1 – Comunicación síncrona con REST**
> Implementar un prototipo donde OrdersService consulta a InventoryService si un producto está disponible antes de confirmar el pedido.
> - Tecnologías: REST
> - Compare gRPC frente a REST en este escenario.

---

## 2. Arquitectura

```
┌────────────────┐   POST /api/orders    ┌────────────────┐   GET /api/inventory/check/
│  OrdersClient  │ ────────────────────► │ OrdersService  │ ─────────────────────────────► ┌────────────────┐
│   (consola)    │                       │  (8080 host)   │  ◄───────────────────────────── │ InventoryService│
└────────────────┘ ◄──────────────────── └────────────────┘   StockCheckResponse            │   (8081 host)  │
       OrderResponse (201/409/404/503)   │ HttpClient tipado └──────────────────────────────► └────────────────┘
                                          │ + 3 reintentos
                                          ▼
                                    INVENTORY_BASE_URL
```

El mensaje **sincrónico** es el `GET /api/inventory/check/{productId}/{quantity}`: `OrdersService` **bloquea el request** del cliente hasta que `InventoryService` responda. El cliente no habla nunca con `InventoryService` directamente; todo el ida y vuelta pasa por `OrdersService`.

Para que la confirmación tenga efecto real, `OrdersService` hace una segunda llamada síncrona: `POST /api/inventory/reserve/{productId}/{quantity}`, que es la que **descuenta** el stock. Si entre el check y la reserva otro pedido se llevó las unidades, `InventoryService` responde `409` y el pedido se rechaza, así que nunca se sobre-vende.

Si `InventoryService` no responde, `OrdersService` **no puede decidir** el pedido y devuelve `503`. Esa es exactamente la fragilidad de la comunicación síncrona que hay que comparar contra gRPC.

---

## 3. Estructura del proyecto

```
Ejercicio 1 - REST/
├── Ejercicio1REST.sln
├── docker-compose.yml
├── InventoryService/            # sabe el stock. No llama a nadie.
│   ├── Controllers/InventoryController.cs
│   ├── Program.cs
│   ├── InventoryService.csproj
│   ├── Dockerfile
│   └── .dockerignore
├── OrdersService/               # recibe pedidos, consulta al inventario, decide
│   ├── Controllers/OrdersController.cs
│   ├── Models/OrderModels.cs
│   ├── Services/InventoryClient.cs     # HttpClient tipado + reintentos
│   ├── Program.cs
│   ├── OrdersService.csproj
│   ├── Dockerfile
│   └── .dockerignore
└── OrdersClient/                # consola que dispara los pedidos
    ├── Program.cs
    ├── OrdersClient.csproj
    ├── Dockerfile
    └── .dockerignore
```

---

## 4. Requisitos

| Herramienta | Para qué | ¿Obligatoria? |
|---|---|---|
| **Docker Desktop** | Levantar los 3 servicios | Sí |
| **.NET 8 SDK** | Solo para compilar/depurar localmente | No (el SDK corre dentro de las imágenes) |

Instalación del SDK, si lo necesitás:

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e
```

---

## 5. Cómo ejecutar

```powershell
docker compose up -d --build
```

Después seguí la salida del cliente automático, que dispara los 3 escenarios de la consigna:

```powershell
docker compose logs -f ordersclient
```

| Servicio | URL desde Windows | URL desde otro contenedor |
|---|---|---|
| InventoryService | http://localhost:8081 | `http://inventoryservice:80` |
| OrdersService | http://localhost:8080 | `http://ordersservice:80` |
| OrdersClient | — (consola) | — |

Para detener todo:

```powershell
docker compose down
```

---

## 6. Endpoints

### InventoryService (puerto 8081)

| Método | Ruta | Devuelve |
|---|---|---|
| `GET` | `/api/inventory/{productId}` | `200` stock del producto · `404` si no existe |
| `GET` | `/api/inventory/check/{productId}/{quantity}` | `200` con `available` · `404` si el producto no existe |
| `POST` | `/api/inventory/reserve/{productId}/{quantity}` | `200` reserva hecha y stock restante · `400` cantidad inválida · `404` si no existe · `409` stock insuficiente |

### OrdersService (puerto 8080)

| Método | Ruta | Devuelve |
|---|---|---|
| `POST` | `/api/orders` | `201` confirmado · `400` datos inválidos · `404` producto inexistente · `409` stock insuficiente · `503` inventario caído |
| `GET` | `/api/orders` | `200` listado de pedidos (en memoria) |
| `GET` | `/api/orders/{orderId}` | `200` pedido · `404` si no existe |

Body del `POST /api/orders`:

```json
{ "customerId": "CLI-001", "productId": "P001", "quantity": 3 }
```

---

## 7. Formas de probar las funcionalidades

> En PowerShell usá **`curl.exe`** (con el `.exe`), porque `curl` es un alias de `Invoke-WebRequest` y no acepta `-d` ni `-X POST`.

### Prueba 1 — Pedido con stock suficiente → se confirma y descuenta el stock

```powershell
curl.exe -X POST http://localhost:8080/api/orders -H "Content-Type: application/json" -d '{\"customerId\":\"CLI-9\",\"productId\":\"P002\",\"quantity\":5}'
```

```json
{"orderId":"ORD-EADABB11","customerId":"CLI-9","productId":"P002","quantity":5,
 "status":"Confirmed","message":"Pedido confirmado. Stock restante: 45."}   →  HTTP 201
```

Comprobá que el stock **realmente** bajó:

```powershell
curl.exe http://localhost:8081/api/inventory/P002
```

```json
{"productId":"P002","stock":45}   →  HTTP 200
```

Un segundo pedido de 5 unidades devuelve `"Stock restante: 40"` y el inventario queda en `{"productId":"P002","stock":40}`. El dato viene del inventario, no es una estimación del `OrdersService`.

**Qué demuestra:** `OrdersService` consultó a `InventoryService`, recibió stock suficiente, confirmó el pedido y **descontó** las unidades reservándolas en el inventario.

### Prueba 2 — Stock insuficiente → se rechaza con 409

```powershell
curl.exe -X POST http://localhost:8080/api/orders -H "Content-Type: application/json" -d '{\"customerId\":\"CLI-9\",\"productId\":\"P002\",\"quantity\":999}'
```

```json
{"orderId":"ORD-84D0D68E","customerId":"CLI-9","productId":"P002","quantity":999,
 "status":"Rejected","message":"Stock insuficiente. Disponible: 50, solicitado: 999."}   →  HTTP 409
```

**Qué demuestra:** la consulta previa evitó confirmar un pedido imposible. `409 Conflict` en vez de `400`: el pedido en sí es válido, lo que no lo es el estado del recurso.

### Prueba 3 — Producto inexistente → 404

```powershell
curl.exe -X POST http://localhost:8080/api/orders -H "Content-Type: application/json" -d '{\"customerId\":\"CLI-3\",\"productId\":\"NOPE\",\"quantity\":1}'
```

```json
{"orderId":"...","status":"Rejected","message":"El producto NOPE no existe."}   →  HTTP 404
```

### Prueba 3 bis — Agotar el stock de un producto

`P003` arranca con 5 unidades, así que sirve para ver el ciclo completo hasta el rechazo:

```powershell
# Se lleva todo el stock disponible
curl.exe -X POST http://localhost:8080/api/orders -H "Content-Type: application/json" -d '{\"customerId\":\"C\",\"productId\":\"P003\",\"quantity\":5}'
```

```json
{"orderId":"ORD-61934D84","customerId":"C","productId":"P003","quantity":5,
 "status":"Confirmed","message":"Pedido confirmado. Stock restante: 0."}   →  HTTP 201
```

```powershell
# Ahora cualquier pedido de P003 se rechaza
curl.exe -X POST http://localhost:8080/api/orders -H "Content-Type: application/json" -d '{\"customerId\":\"C\",\"productId\":\"P003\",\"quantity\":1}'
```

```json
{"orderId":"ORD-D538EA7B","customerId":"C","productId":"P003","quantity":1,
 "status":"Rejected","message":"Stock insuficiente. Disponible: 0, solicitado: 1."}   →  HTTP 409
```

Para volver al estado inicial: `docker compose restart inventoryservice`.

### Prueba 4 — Validación de entrada → 400 (sin llegar al inventario)

```powershell
# Cantidad fuera de rango (definida como [Range(1, 1000)] en el modelo)
curl.exe -X POST http://localhost:8080/api/orders -H "Content-Type: application/json" -d '{\"customerId\":\"C1\",\"productId\":\"P001\",\"quantity\":0}'

# Campo obligatorio ausente
curl.exe -X POST http://localhost:8080/api/orders -H "Content-Type: application/json" -d '{\"customerId\":\"C1\",\"quantity\":2}'
```

```json
{"title":"One or more validation errors occurred.","status":400,
 "errors":{"Quantity":["The field Quantity must be between 1 and 1000."]}}   →  HTTP 400

{"title":"One or more validation errors occurred.","status":400,
 "errors":{"ProductId":["The ProductId field is required."]}}   →  HTTP 400
```

### Prueba 5 — Inventario caído → 503 (el caso clave de la consigna)

```powershell
docker compose stop inventoryservice
Start-Sleep -Seconds 2

curl.exe -X POST http://localhost:8080/api/orders -H "Content-Type: application/json" -d '{\"customerId\":\"C1\",\"productId\":\"P001\",\"quantity\":1}'
```

```json
{"orderId":"ORD-E9A0899B","status":"Rejected",
 "message":"InventoryService no esta disponible. Reintentar mas tarde."}   →  HTTP 503
```

Y en el log de `OrdersService` se ven los reintentos:

```powershell
docker compose logs ordersservice
```

```
warn: InventoryService no respondio (intento 1/3). Reintentando...
warn: InventoryService no respondio (intento 2/3). Reintentando...
error: InventoryService inalcanzable tras 3 intentos.
error: Pedido ORD-E9A0899B rechazado: InventoryService inalcanzable.
```

**Qué demuestra:** la dependencia síncrona se propaga. El cliente no puede obtener su respuesta hasta que la caída se resuelve, y el pedido queda sin confirmar. Volvé a levantar con `docker compose start inventoryservice`.

### Prueba 6 — Consultas directas al inventario

```powershell
curl.exe http://localhost:8081/api/inventory/P001
curl.exe http://localhost:8081/api/inventory/NOEXISTE
```

```json
{"productId":"P001","stock":100}                                    →  HTTP 200
{"message":"El producto NOEXISTE no existe."}                      →  HTTP 404
```

### Prueba 7 — Consultar un pedido

```powershell
# Listar todos los pedidos confirmados
curl.exe http://localhost:8080/api/orders

# Consultar uno puntual (usá el orderId que te devolvió la respuesta del POST)
curl.exe http://localhost:8080/api/orders/ORD-EADABB11
```

Los pedidos viven en memoria, así que el listado arranca vacío después de `docker compose restart ordersservice`.

### Prueba 8 — Cliente automático (los 3 escenarios de una vez)

```powershell
docker compose run --rm ordersclient
```

```
OrdersClient consumiendo http://ordersservice/
----------------------------------------------------------------------
> POST api/orders  [stock suficiente]  cliente=CLI-001 producto=P001 cantidad=3
  HTTP 201 Created
  Status : Confirmed
  Mensaje: Pedido confirmado. Stock restante: 97.

> POST api/orders  [stock insuficiente]  cliente=CLI-002 producto=P002 cantidad=999
  HTTP 409 Conflict
  Status : Rejected
  Mensaje: Stock insuficiente. Disponible: 50, solicitado: 999.

> POST api/orders  [producto inexistente]  cliente=CLI-003 producto=NOPE cantidad=1
  HTTP 404 NotFound
  Status : Rejected
  Mensaje: El producto NOPE no existe.
```

---

## 8. Cómo cumple la consigna

| Requisito de la consigna | Dónde está implementado | Cómo se comprueba |
|---|---|---|
| `OrdersService` consulta a `InventoryService` si el producto está disponible **antes** de confirmar | `OrdersService/Controllers/OrdersController.cs` → `_inventory.CheckStockAsync(...)` | Pruebas 1, 2 y 3 |
| El pedido **solo** se confirma si hay stock | `CreateOrder` devuelve `201` únicamente cuando `Available == true` | Prueba 2 (409) |
| La confirmación **efecta** la reserva | `_inventory.ReserveStockAsync(...)` → `POST /api/inventory/reserve/{id}/{cantidad}` | Pruebas 1 y 3 bis (el stock baja de verdad) |
| **Dos servicios separados** que se comunican | `InventoryService` y `OrdersService`, cada uno en su contenedor con su puerto | `docker compose ps` |
| Comunicación **sincrona** por paso de mensajes | `OrdersService/Services/InventoryClient.cs`: el request queda bloqueado esperando la respuesta | Prueba 5 (el `503` solo es posible por ser síncrono) |
| **Tecnología: REST** | Endpoints HTTP + JSON; nombres de recurso y verbos HTTP semánticos | Sección 6 |
| **Ambos en Docker** | Un `Dockerfile` multi-stage por proyecto + `docker-compose.yml` | `docker compose up -d --build` |
| **Comparar gRPC frente a REST** | Sección 9 de este documento | — |

---

## 9. Comparación: gRPC frente a REST en este escenario

| Criterio | REST (lo implementado) | gRPC |
|---|---|---|
| **Transporte** | HTTP/1.1, texto JSON | HTTP/2, binario Protobuf |
| **Latencia** | Más alta: parsing de texto, headers HTTP/1.1 solo sin comillas | Menor: payload compacto, menos bytes en la red |
| **Streams / concurrencia** | Un request por conexión (HTTP/1.1). Requiere más sockets | Multiplexación nativa en HTTP/2: muchos requests en una conexión |
| **Contrato** | Implícito: hay que leer el código o la documentación | Explícito y tipado: un `.proto` que genera cliente y servidor |
| **Consistencia del contrato** | Riesgo de drift si alguien cambia el JSON | `Incorrect version of field` si el contrato no matchea |
| **Depuración** | `curl`, Postman, cualquier HTTP client. Legible a simple vista | Necesita `grpcurl` o un proxy; menos intuitivo |
| **Descubrimiento / docs** | OpenAPI / Swagger (`/swagger`) | Automatico desde el `.proto`, pero requiere reflección habilitada |
| **Navegador** | `fetch` / `XMLHttpRequest` directo | Necesita gRPC-Web como proxy |
| **Manejo de errores** | Códigos HTTP estándar y ageing | `status` enum en el mensaje; no hay semántica HTTP |
| **Timeouts y reintentos** | Manual (aquí: 3 reintentos con backoff en `InventoryClient`) | Manual, con deadline propenso por defecto |
| **Curva de aprendizaje** | Baja: es HTTP | Media: hay que aprender Protobuf, `.proto` y code generation |

**Conclusión para este escenario.** REST es la opción correcta acá: el flujo es un `request/response` simple, de bajo volumen, y probablemente quieras consumptionar desde un front o `curl` durante la cursada. gRPC pagaría su complejidad (`.proto`, code generation, tooling) sin ganancia apreciable.

Donde **sí** convendría gRPC es si `OrdersService` tuviera que consultar a `InventoryService` en un loop (por ejemplo, validar 200 líneas de un pedido), porque ahí la latencia por llamada se acumula y la multiplexación HTTP/2 importa. El problema abierto de ambos enfoques es el mismo: si el inventario se cae, el pedido no se puede decidir — lo que cambia es **cuánto** tarda el sistema en recuperarse, no si se recupera solo.

---

## 10. Limitaciones conocidas

- **Todo el estado está en memoria** (`static`). Reiniciar cualquier contenedor resetea stock y pedidos.
- **El check y la reserva no son atómicos.** Son dos llamadas separadas: entre `check` y `reserve` otro pedido puede consumir el stock. La reserva lo detecta (`409`) y el pedido se rechaza, así que **nunca se sobre-vende**, pero el cliente puede recibir un rechazo después de haber pasado el chequeo. La solución real es una transacción o una reserva atómica en el inventario.
- **Sin autenticación ni autorización** entre servicios: cualquiera puede reservar stock.
- **Los reintentos son ingenuos**: 3 intentos con backoff fijo. En producción corresponde un circuit breaker con `Polly` para dejar de castigar al servicio caído.
- **Sin idempotencia**: si el cliente reintenta un `POST` por timeout, puede crear pedidos duplicados.

---

## 11. Comandos útiles

```powershell
docker compose up -d --build           # levantar todo
docker compose logs -f ordersclient    # ver el cliente automático
docker compose logs -f ordersservice   # ver consultas y reintentos
docker compose run --rm ordersclient   # re-ejecutar el cliente (resetea el stock antes)
docker compose stop inventoryservice   # simular caída del inventario (prueba 5)
docker compose start inventoryservice  # recuperarlo
docker compose restart inventoryservice# reinicia el inventario (vuelve el stock a 100/50/5)
docker compose down                    # detener y borrar contenedores
docker compose down -v                 # borrar también los volúmenes
```

---

## 12. Problemas frecuentes

| Síntoma | Causa | Solución |
|---|---|---|
| `failed to connect to the docker API at npipe://...` | Docker Desktop apagado | `Start-Process "$env:LOCALAPPDATA\Programs\DockerDesktop\Docker Desktop.exe"` |
| `failed to solve: failed to read dockerfile: invalid file request` | OneDrive dejó los archivos como placeholders | Ver abajo |
| `curl : la opción '-d' no se reconoce` | En PowerShell `curl` es alias de `Invoke-WebRequest` | Usar `curl.exe` |
| `Connection refused` al llamar a `localhost:8081` | Stack no levantado, o `inventoryservice` detenido por la prueba 5 | `docker compose ps` y `docker compose up -d` |
| El cliente queda en `intento N: OrdersService todavia no responde` | Los healthchecks no dieron health a tiempo | `docker compose logs ordersservice` para ver el error real |

**Si estás dentro de OneDrive:** OneDrive con *Files On-Demand* deja los archivos como placeholders que solo existen en la nube. Windows los lee sin problema, pero **Docker Desktop no**, y el build falla. Solución: click derecho en la carpeta → **Siempre disponible en este dispositivo**. Lo más seguro igual es trabajar fuera de OneDrive y commitear seguido.