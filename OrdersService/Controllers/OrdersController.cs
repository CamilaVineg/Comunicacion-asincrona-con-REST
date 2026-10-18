using Microsoft.AspNetCore.Mvc;
using OrdersService.Models;
using OrdersService.Services;

namespace OrdersService.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    // Prototipo: los pedidos viven en memoria del proceso.
    private static readonly Dictionary<string, OrderResponse> Pedidos = new();

    private readonly IInventoryClient _inventory;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IInventoryClient inventory, ILogger<OrdersController> logger)
    {
        _inventory = inventory;
        _logger = logger;
    }

    /// <summary>
    /// POST api/orders
    /// Confirma el pedido SOLO si InventoryService dice que hay stock.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        // Comunicacion SINCRONA: este request queda bloqueado esperando la
        // respuesta de InventoryService. Es el tradeoff que hay que comparar con gRPC.
        var check = await _inventory.CheckStockAsync(request.ProductId, request.Quantity, ct);

        var orderId = $"ORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        // 1) InventoryService caido -> no hay forma de decidir el pedido.
        if (!check.ServiceReachable)
        {
            _logger.LogError("Pedido {OrderId} rechazado: InventoryService inalcanzable.", orderId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, Rejected(
                orderId, request, "InventoryService no esta disponible. Reintentar mas tarde."));
        }

        // 2) Producto inexistente.
        if (!check.ProductExists)
        {
            return NotFound(Rejected(
                orderId, request, $"El producto {request.ProductId} no existe."));
        }

        // 3) Stock insuficiente -> 409 Conflict (el pedido es valido, el estado del recurso no).
        if (!check.Available)
        {
            return Conflict(Rejected(
                orderId, request,
                $"Stock insuficiente. Disponible: {check.Stock}, solicitado: {request.Quantity}."));
        }

        // 4) Confirmado -> 201 Created.
        var created = new OrderResponse(
            orderId, request.CustomerId, request.ProductId, request.Quantity,
            OrderStatus.Confirmed,
            $"Pedido confirmado. Stock restante: {check.Stock - request.Quantity}.");

        Pedidos[orderId] = created;
        _logger.LogInformation("Pedido {OrderId} confirmado para {CustomerId}.", orderId, request.CustomerId);

        return CreatedAtAction(nameof(GetOrder), new { orderId }, created);
    }

    [HttpGet]
    public ActionResult<IEnumerable<OrderResponse>> GetOrders() => Ok(Pedidos.Values);

    [HttpGet("{orderId}")]
    public ActionResult<OrderResponse> GetOrder(string orderId)
    {
        return Pedidos.TryGetValue(orderId, out var order)
            ? Ok(order)
            : NotFound(new { Message = $"El pedido {orderId} no existe." });
    }

    private static OrderResponse Rejected(string orderId, CreateOrderRequest request, string message) =>
        new(orderId, request.CustomerId, request.ProductId, request.Quantity, OrderStatus.Rejected, message);
}