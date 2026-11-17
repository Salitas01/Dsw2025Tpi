using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests.Controllers
{
    [Collection("Integration Tests")]
    public class OrdersControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;
        private readonly System.Net.Http.HttpClient _client;

        public OrdersControllerIntegrationTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
            _client = _factory.CreateClient();
        }

        // Escenario 1: Endpoint protegido por JWT
        [Fact]
        public async Task CreateOrder_WhenUnauthenticated_ReturnsUnauthorized()
        {
            // ARRANGE: Payload usando tu DTO exacto
            var request = new OrderRequest(
                CustomerId: Guid.NewGuid(),
                ShippingAddress: "Av. Falsa 123",
                BillingAddress: "Av. Falsa 123",
                Notes: "Sin token",
                Items: new List<OrderItemRequest> { new OrderItemRequest(Guid.NewGuid(), 1) }
            );

            // Importante: NO enviamos el header de Autorización
            _client.DefaultRequestHeaders.Authorization = null;

            // ACT
            var response = await _client.PostAsJsonAsync("/api/orders", request);

            // ASSERT
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // Escenario 2: Transacción completa (Orden + Reducción de Stock)
        [Fact]
        public async Task CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite()
        {
            // ARRANGE
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            // 1. Sembrar base de datos SQLite
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

                // USAMOS db.Set<T>() PARA EVITAR ERRORES DE NOMBRE
                db.Set<Customer>().Add(new Customer { 
                    Id = customerId, 
                    Name = "Cliente Integracion", 
                    Email = "cliente@integration.com", 
                    PhoneNumber = "1234567890" 
                    }
                );

                db.Set<Product>().Add(new Product(
                    sku: "SKU-ORDER-TEST",
                    name: "Monitor 24",
                    price: 150000m,
                    stock: 10,
                    description: "Monitor de prueba",
                    internalCode: "INT-001"
                )
                { Id = productId });

                await db.SaveChangesAsync();
            }

            // 2. Autenticación (Admin o User)
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            // 3. Payload: Compramos 3 unidades usando tu record OrderRequest
            var request = new OrderRequest(
                CustomerId: customerId,
                ShippingAddress: "Calle Verdadera 456",
                BillingAddress: "Calle Verdadera 456",
                Notes: "Dejar en portería",
                Items: new List<OrderItemRequest>
                {
                    new OrderItemRequest(ProductId: productId, Quantity: 3)
                }
            );

            // ACT
            var response = await _client.PostAsJsonAsync("/api/orders", request);

            // Leemos el error interno si falla para que no tengas que buscar a ciegas
            var responseText = await response.Content.ReadAsStringAsync();

            // ASSERT - Respuesta HTTP
            Assert.True(response.StatusCode == HttpStatusCode.Created, $"Fallo de API: {responseText}");

            // ASSERT - Inspeccionar Base de Datos
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

                // Verificar que la orden se guardó usando Set<Order>()
                var orderExists = await db.Set<Order>().AnyAsync(o => o.CustomerId == customerId);
                Assert.True(orderExists);

                // Verificar la deducción del stock usando Set<Product>()
                var product = await db.Set<Product>().FindAsync(productId);
                Assert.NotNull(product);
                Assert.Equal(7, product.StockQuantity);
            }
        }

        // Escenario 3: Validación de inventario insuficiente
        [Fact]
        public async Task CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage()
        {
            // ARRANGE
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

                // USAMOS db.Set<T>()
                db.Set<Customer>().Add(new Customer { 
                    Id = customerId, 
                    Name = "Cliente Sin Suerte", 
                    Email = "cliente@insuficiente.com", 
                    PhoneNumber = "1234567890" 
                    }
                );

                db.Set<Product>().Add(new Product(
                    sku: "SKU-NO-STOCK",
                    name: "Placa de Video",
                    price: 300000m,
                    stock: 5,
                    description: "Últimas unidades",
                    internalCode: "INT-002"
                )
                { Id = productId });

                await db.SaveChangesAsync();
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            // Payload solicitando 10 unidades (5 más de las que hay)
            var request = new OrderRequest(
                CustomerId: customerId,
                ShippingAddress: "Local",
                BillingAddress: "Local",
                Notes: null,
                Items: new List<OrderItemRequest>
                {
                    new OrderItemRequest(ProductId: productId, Quantity: 10)
                }
            );

            // ACT
            var response = await _client.PostAsJsonAsync("/api/orders", request);

            // ASSERT - Debe devolver 400 Bad Request
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}