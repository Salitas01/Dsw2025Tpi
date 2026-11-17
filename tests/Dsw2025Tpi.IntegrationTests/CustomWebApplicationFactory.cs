using System;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Dsw2025Tpi.Api;
using Dsw2025Tpi.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost; // <--- CLAVE PARA SOBREESCRIBIR PROGRAM.CS
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dsw2025Tpi.IntegrationTests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _mainConnection;
        private readonly SqliteConnection _authConnection;

        public CustomWebApplicationFactory()
        {
            _mainConnection = new SqliteConnection("DataSource=MainDb;Mode=Memory;Cache=Shared");
            _mainConnection.Open();

            _authConnection = new SqliteConnection("DataSource=AuthDb;Mode=Memory;Cache=Shared");
            _authConnection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // USAMOS ConfigureTestServices PARA GARANTIZAR QUE SE EJECUTE DESPUÉS DE Program.cs
            builder.ConfigureTestServices(services =>
            {
                // 1. Borramos las opciones inyectadas por Program.cs
                services.RemoveAll<DbContextOptions<Dsw2025TpiContext>>();
                services.RemoveAll<DbContextOptions<AuthenticateContext>>();
                services.RemoveAll<DbConnection>();

                // 2. Forzamos SQLite
                services.AddScoped<DbContextOptions<Dsw2025TpiContext>>(sp =>
                {
                    return new DbContextOptionsBuilder<Dsw2025TpiContext>()
                        .UseSqlite(_mainConnection)
                        .Options;
                });

                services.AddScoped<DbContextOptions<AuthenticateContext>>(sp =>
                {
                    return new DbContextOptionsBuilder<AuthenticateContext>()
                        .UseSqlite(_authConnection)
                        .Options;
                });

                // 3. LA SOLUCIÓN AL UNAUTHORIZED: Forzar nuestro TestScheme por defecto
                services.Configure<AuthenticationOptions>(options =>
                {
                    options.DefaultAuthenticateScheme = "TestScheme";
                    options.DefaultChallengeScheme = "TestScheme";
                    options.DefaultScheme = "TestScheme";
                });

                services.AddAuthentication("TestScheme")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        "TestScheme", options => { });

                // 4. Crear la base de datos de prueba desde cero
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();

                var mainDb = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
                mainDb.Database.EnsureDeleted(); // Limpiamos cualquier rastro fantasma
                mainDb.Database.EnsureCreated();

                var authDb = scope.ServiceProvider.GetRequiredService<AuthenticateContext>();
                authDb.Database.EnsureDeleted();
                authDb.Database.EnsureCreated();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _mainConnection.Close();
            _authConnection.Close();
        }
    }

    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.Authorization.ToString().StartsWith("Test"))
            {
                var claims = new[] {
                    new Claim(ClaimTypes.Name, "TestAdmin"),
                    new Claim(ClaimTypes.Role, "Admin")
                };
                var identity = new ClaimsIdentity(claims, "TestScheme");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "TestScheme");

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}