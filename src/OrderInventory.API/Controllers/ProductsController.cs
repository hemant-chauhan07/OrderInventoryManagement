using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderInventory.Application.DTOs.Products;
using OrderInventory.Application.Interfaces;

namespace OrderInventory.API.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    // GET: api/products
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(
        PagedResponseDto<ProductResponseDto>), 200)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] ProductQueryDto query)
    {
        var result = await _productService.GetProductsAsync(query);

        return Ok(result);
    }

    // GET: api/products/1
    [AllowAnonymous]
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductResponseDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetProduct(int id)
    {
        var result = await _productService.GetProductByIdAsync(id);

        return Ok(result);
    }

    // POST: api/products
    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ProducesResponseType(typeof(ProductResponseDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> CreateProduct(
        [FromBody] CreateProductRequestDto request)
    {
        var result = await _productService.CreateProductAsync(request);

        return CreatedAtAction(
            nameof(GetProduct),
            new { id = result.Id },
            result);
    }

    // PUT: api/products/1
    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ProductResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> UpdateProduct(
        int id,
        [FromBody] UpdateProductRequestDto request)
    {
        var result = await _productService.UpdateProductAsync(
            id,
            request);

        return Ok(result);
    }
}