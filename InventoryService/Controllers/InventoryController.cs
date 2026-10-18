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

    // GET api/inventory/check/{productId}/{quantity}
    [HttpGet("check/{productId}/{quantity}")]
    public ActionResult<StockCheckResponse> CheckStock(string productId, int quantity)
    {
        var response = BuildResponse(productId, quantity);

        // 404 si el producto no existe; 200 con available=false si no alcanza el stock.
        return response.Stock < 0
            ? NotFound(new { Message = $"El producto {productId} no existe." })
            : Ok(response);
    }

    // GET api/inventory/{productId}
    [HttpGet("{productId}")]
    public ActionResult<StockLevelResponse> GetStock(string productId)
    {
        return Stock.TryGetValue(productId, out var stock)
            ? Ok(new StockLevelResponse(productId, stock))
            : NotFound(new { Message = $"El producto {productId} no existe." });
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