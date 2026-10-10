using System.Data;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dsw2025Tpi.Api.Controllers
{
    [ApiController]
    [Route("/api/auth")]
    public class AuthenticateController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly JwtTokenService _jwtTokenService;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<AuthenticateController> _logger;
        private readonly IRepository _repository; 

        public AuthenticateController(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            JwtTokenService jwtTokenService,
            RoleManager<IdentityRole> roleManager,
            ILogger<AuthenticateController> logger,
            IRepository repository)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _jwtTokenService = jwtTokenService;
            _roleManager = roleManager;
            _logger = logger;
            _repository = repository;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel request)
        {
            _logger.LogInformation("Recibida solicitud de login para: {Username}", request.Username);

            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null) return Unauthorized("Usuario o contraseña incorrectos.");

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
            if (!result.Succeeded) return Unauthorized("Usuario o contraseña incorrectos.");

            var userRoles = await _userManager.GetRolesAsync(user);
            var role = userRoles.FirstOrDefault() ?? "User";

            string? customerId = null;

            if (role != "Admin")
            {
                var customer = await _repository.First<Customer>(c => c.IdentityUserId == user.Id);

                if (customer != null)
                {
                    customerId = customer.Id.ToString();
                }
                else
                {
                    _logger.LogWarning("El usuario {User} es rol {Role} pero no tiene Customer asociado.", user.UserName, role);
                    
                }
            }

            var token = _jwtTokenService.GenerateToken(request.Username, role, customerId);

            _logger.LogInformation("Usuario {Username} logueado. Rol: {Role}, CustomerId: {CId}", request.Username, role, customerId ?? "N/A");

            return Ok(new
            {
                Token = token,
                Message = "Sesión iniciada con éxito.",
                Role = role,
                CustomerId = customerId
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            _logger.LogInformation("Recibida solicitud de registro para el usuario: {Username}", model.Username);

            var user = new IdentityUser { UserName = model.Username, Email = model.Email };
            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                _logger.LogWarning("Fallo el registro para {Username}", model.Username);
                return BadRequest(result.Errors);
            }

            string userRole = "User";
            if (!await _roleManager.RoleExistsAsync(userRole))
            {
                await _roleManager.CreateAsync(new IdentityRole(userRole));
            }
            await _userManager.AddToRoleAsync(user, userRole);

            var newCustomer = new Customer
            {
                Id = Guid.NewGuid(),
                IdentityUserId = user.Id,
                Email = model.Email,
                Name = model.Username, 
                PhoneNumber = "N/A"   
            };

            try
            {
                await _repository.Add(newCustomer);
            }
            catch (Exception ex)
            {
                await _userManager.DeleteAsync(user);
                _logger.LogError(ex, "Error al crear el Customer. Se revirtió el usuario.");
                return StatusCode(500, "Error interno al crear el perfil del usuario.");
            }

            var token = _jwtTokenService.GenerateToken(model.Username, userRole, newCustomer.Id.ToString());

            return Ok(new
            {
                Token = token,
                Message = "Usuario registrado y sesión iniciada con éxito.",
                Role = userRole,
                CustomerId = newCustomer.Id.ToString() 
            });
        }

        [HttpPost("register-with-role")]
        public async Task<IActionResult> RegisterWithRole([FromBody] RegisterAuthModel model)
        {
            return await ProcessRegistration(model.Username, model.Email, model.Password, model.Role);
        }

        private async Task<IActionResult> ProcessRegistration(string username, string email, string password, string roleName)
        {
            _logger.LogInformation("Procesando registro para: {Username} con rol: {Role}", username, roleName);

            var user = new IdentityUser { UserName = username, Email = email };
            var result = await _userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                return BadRequest(result.Errors);
            }

            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                await _roleManager.CreateAsync(new IdentityRole(roleName));
            }
            await _userManager.AddToRoleAsync(user, roleName);

            string? newCustomerId = null;

            if (roleName != "Admin")
            {
                try
                {
                    var newCustomer = new Customer
                    {
                        Id = Guid.NewGuid(),
                        IdentityUserId = user.Id, 
                        Email = email,
                        Name = username,
                        PhoneNumber = "N/A" 
                    };

                    await _repository.Add(newCustomer);
                    newCustomerId = newCustomer.Id.ToString();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al crear entidad Customer. Revertiendo IdentityUser.");

                    await _userManager.DeleteAsync(user);
                    return StatusCode(500, "Error interno al crear el perfil del cliente.");
                }
            }

            var token = _jwtTokenService.GenerateToken(username, roleName, newCustomerId);

            return Ok(new
            {
                Token = token,
                Message = $"Usuario registrado como {roleName}.",
                Role = roleName,
                CustomerId = newCustomerId
            });
        }
    }
}
