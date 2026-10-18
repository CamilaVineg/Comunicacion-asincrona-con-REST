using Microsoft.AspNetCore.Mvc;

namespace InventoryService.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController : ControllerBase
{
    private static readonly Dictionary<string, int> Stock = new()
    {
        ["P001"] = 100,
        ["P002"] = 50,
        ["P003"] = 5
    };

    // El stock es compartido por todas las peticiones concurrentes, asi que las
    // operaciones de leer-modificar-escribir van con lock para no perder actualizaciones.
    private static readonly object StockLock = new();

    // GET api/inventory/check/{productId}/{quantity}
    [HttpGet("check/{productId}/{quantity}")]
    public ActionResult<StockCheckResponse> CheckStock(string productId, int quantity)
    {
        lock (StockLock)
        {
            var response = BuildResponse(productId, quantity);

            // 404 si el producto no existe; 200 con available=false si no alcanza el stock.
            return response.Stock < 0
                ? NotFound(new { Message = $"El producto {productId} no existe." })
                : Ok(response);
        }
    }

    // GET api/inventory/{productId}
    [HttpGet("{productId}")]
    public ActionResult<StockLevelResponse> GetStock(string productId)
    {
        lock (StockLock)
        {
            return Stock.TryGetValue(productId, out var stock)
                ? Ok(new StockLevelResponse(productId, stock))
                : NotFound(new { Message = $"El producto {productId} no existe." });
        }
    }

    // POST api/inventory/reserve/{productId}/{quantity}
    // Descunta el stock. Es la operacion que realmente bloquea las unidades para el pedido.
    [HttpPost("reserve/{productId}/{quantity}")]
    public ActionResult<ReserveResponse> ReserveStock(string productId, int quantity)
    {
        if (quantity <= 0)
        {
            return BadRequest(new { Message = "La cantidad debe ser mayor a 0." });
        }

        lock (StockLock)
        {
            if (!Stock.TryGetValue(productId, out var stock))
            {
                return NotFound(new { Message = $"El producto {productId} no existe." });
            }

            if (stock < quantity)
            {
                return Conflict(new ReserveResponse(productId, quantity, stock, Reserved: false));
            }

            Stock[productId] = stock - quantity;

            return Ok(new ReserveResponse(productId, quantity, Stock[productId], Reserved: true));
        }
    }

    private static StockCheckResponse BuildResponse(string productId, int quantity)
    {
        var exists = Stock.TryGetValue(productId, out var stock);

        return new StockCheckResponse(
            ProductId: productId,
            RequestedQuantity: quantity,
            Stock: exists ? stock : -1,
            Available: exists && stock >= quantity
        );
    }
}

public record StockCheckResponse(string ProductId, int RequestedQuantity, int Stock, bool Available);

public record StockLevelResponse(string ProductId, int Stock);

public record ReserveResponse(string ProductId, int ReservedQuantity, int Remaining, bool Reserved);