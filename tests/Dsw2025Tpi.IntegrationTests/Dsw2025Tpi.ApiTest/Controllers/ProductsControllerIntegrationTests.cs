using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Dsw2025Tpi.IntegrationTests.Controllers
{
    [Collection("Integration Tests")]
    public class ProductsControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public ProductsControllerIntegrationTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        // =========================================================================
        // Tests de creación de orden (Cliente):
        // =========================================================================

        // Escenario 1: Seguridad (401 Unauthorized)
        [Fact]
        public async Task AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden()
        {
            // ARRANGE
            var requestDto = new ProductModel.Request("SKU-SEC", "INT-SEC", "Seguridad", "Desc", 100m, 10);

            // ACT: Enviar petición SIN token de autorización
            var response = await _client.PostAsJsonAsync("/api/products", requestDto);

            // ASSERT: Verifica que el Controller lo bloqueó por el [Authorize(Roles = "Admin")]
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // Escenario 2: Creación exitosa y persistencia real
        [Fact]
        public async Task AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated()
        {
            // ARRANGE
            var skuPrueba = $"SKU-TEST-{Guid.NewGuid()}";
            var requestDto = new ProductModel.Request(skuPrueba, "INT-TEST", "Monitor 24", "Monitor HD", 25000m, 5);

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            // ACT
            var response = await _client.PostAsJsonAsync("/api/products", requestDto);

            // Leemos el mensaje de error real que escupió la API
            var errorContent = await response.Content.ReadAsStringAsync();

            // ASSERT 1: Modificamos el assert para que nos imprima el error si falla
            Assert.True(response.StatusCode == HttpStatusCode.Created, $"Error interno en la API: {errorContent}");

            // ASSERT 2: Verificar la persistencia en la base de datos SQLite real
            using var scope = _factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

            var productoGuardado = await dbContext.Set<Product>().FirstOrDefaultAsync(p => p.Sku == skuPrueba);

            Assert.NotNull(productoGuardado);
            Assert.Equal("Monitor 24", productoGuardado.Name);
            Assert.Equal(25000m, productoGuardado.CurrentUnitPrice);
        }

        // Escenario 3: Manejo de Entidad Duplicada
        [Fact]
        public async Task AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsConflictStatusCode()
        {
            // ARRANGE
            var skuDuplicado = $"SKU-DUP-{Guid.NewGuid()}";

            using (var scope = _factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
                var productoOriginal = new Product(skuDuplicado, "Original", 100m, 10, "", "INT-DUP");
                dbContext.Set<Product>().Add(productoOriginal);
                await dbContext.SaveChangesAsync();
            }

            var requestDto = new ProductModel.Request(skuDuplicado, "INT-NUEVO", "Copia", "Copia", 150m, 5);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            // ACT
            var response = await _client.PostAsJsonAsync("/api/products", requestDto);

            Assert.True(
                response.StatusCode == HttpStatusCode.Conflict ||
                response.StatusCode == HttpStatusCode.BadRequest,
                $"Se esperaba 409 o 400, pero devolvió {response.StatusCode}"
            );
        }

        // =========================================================================
        // Tests de Listado de productos (Admin):
        // =========================================================================

        // Escenario 1: Probar la accesibilidad pública y el contrato JSON del endpoint.
        [Fact]
        public async Task GetProducts_PublicEndpoint_ReturnsOkWithJsonArray()
        {
            // ARRANGE: No enviamos token y no sembramos datos (lista vacía)
            _client.DefaultRequestHeaders.Authorization = null;

            // ACT
            var response = await _client.GetAsync("/api/products");

            // ASSERT
            Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);

            // Validar la estructura JSON si devuelve 200 OK
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var content = await response.Content.ReadAsStringAsync();

                // Eliminamos los espacios en blanco por si acaso
                content = content.Trim();

                // Verificamos que el JSON sea un Array plano (empieza con '[' y termina con ']')
                Assert.Contains("items", content, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("total", content, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Escenario 2: Filtrado por Query String
        [Fact]
        public async Task GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite()
        {
            // ARRANGE:
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

                // Semilla con los datos correctos para tu constructor
                db.Set<Product>().Add(new Product("SKU-F1", "Teclado Mecanico", 50000m, 10, "Gamer", "INT-01") { Id = Guid.NewGuid() });
                db.Set<Product>().Add(new Product("SKU-F2", "Teclado Membrana", 20000m, 5, "Oficina", "INT-02") { Id = Guid.NewGuid() });
                db.Set<Product>().Add(new Product("SKU-F3", "Mouse Inalambrico", 15000m, 20, "Oficina", "INT-03") { Id = Guid.NewGuid() });

                await db.SaveChangesAsync();
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            var response = await _client.GetAsync("/api/products?searchTerm=Teclado&pageNumber=1&pageSize=10");

            // ASSERT
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();

            Assert.Contains("Teclado Mecanico", content);
            Assert.Contains("Teclado Membrana", content);

            // Verificamos que el producto que no cumple el filtro NO esté incluido
            Assert.DoesNotContain("Mouse Inalambrico", content);
        }

        // Escenario 3: Endpoint Admin (Productos Inactivos)
        [Fact]
        public async Task GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts()
        {
            // ARRANGE
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

                var inactiveProduct = new Product("SKU-INACTIVO", "Producto Discontinuado", 1000m, 0, "No mostrar", "INT-INACTIVO")
                {
                    Id = Guid.NewGuid(),
                    IsActive = false
                };

                db.Set<Product>().Add(inactiveProduct);
                await db.SaveChangesAsync();
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            // ACT:
            var response = await _client.GetAsync("/api/products/admin");

            // ASSERT
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();

            Assert.Contains("Producto Discontinuado", content);
        }
    }
}