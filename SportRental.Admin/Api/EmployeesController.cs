using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using SportRental.Shared.Identity;

namespace SportRental.Admin.Api
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmployeesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly ILogger<EmployeesController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole<Guid>> _roleManager;

        public EmployeesController(ApplicationDbContext context, ITenantProvider tenantProvider, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, ILogger<EmployeesController> logger)
        {
            _context = context;
            _tenantProvider = tenantProvider;
            _userManager = userManager;
            _roleManager = roleManager;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetEmployees(int page = 1, int pageSize = 20)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                return Unauthorized();

            _context.SetTenant(tenantId);

            var employees = await _context.Employees
                .Include(e => e.Permissions)
                .Where(e => !e.IsDeleted)
                .OrderBy(e => e.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new
                {
                    e.Id,
                    e.FullName,
                    e.Email,
                    e.City,
                    e.Telephone,
                    e.Position,
                    e.Role,
                    e.Comment,
                    e.AllRentalsNumber,
                    e.CreatedAtUtc,
                    HasPermissions = e.Permissions != null
                })
                .ToListAsync();

            return Ok(employees);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetEmployee(Guid id)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                return Unauthorized();

            _context.SetTenant(tenantId);

            var employee = await _context.Employees
                .Include(e => e.Permissions)
                .Where(e => e.Id == id && !e.IsDeleted)
                .Select(e => new
                {
                    e.Id,
                    e.FullName,
                    e.Email,
                    e.City,
                    e.Telephone,
                    e.Position,
                    e.Role,
                    e.Comment,
                    e.AllRentalsNumber,
                    e.UserId,
                    e.CreatedAtUtc,
                    e.UpdatedAtUtc,
                    Permissions = e.Permissions
                })
                .FirstOrDefaultAsync();

            if (employee == null)
                return NotFound();

            return Ok(employee);
        }

        [HttpPost]
        public async Task<IActionResult> CreateEmployee([FromBody] CreateEmployeeRequest request)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                return Unauthorized();

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            _context.SetTenant(tenantId);

            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Value,
                FullName = request.FullName,
                Email = request.Email,
                City = request.City,
                Telephone = request.Telephone,
                Position = request.Position ?? "Pracownik",
                Role = request.Role,
                Comment = request.Comment,
                CreatedAtUtc = DateTime.UtcNow
            };

            var provision = await EnsureEmployeeIdentityAsync(employee, tenantId.Value, HttpContext.RequestAborted);

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created employee {EmployeeName} with ID {EmployeeId}. IdentityCreated={Created}", employee.FullName, employee.Id, provision.Created);

            return CreatedAtAction(nameof(GetEmployee), new { id = employee.Id }, new { employee.Id, provision.Created, provision.TemporaryPassword });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateEmployee(Guid id, [FromBody] UpdateEmployeeRequest request)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                return Unauthorized();

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            _context.SetTenant(tenantId);

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

            if (employee == null)
                return NotFound();

            employee.FullName = request.FullName;
            employee.Email = request.Email;
            employee.City = request.City;
            employee.Telephone = request.Telephone;
            employee.Position = request.Position ?? employee.Position;
            employee.Role = request.Role;
            employee.Comment = request.Comment;
            employee.UpdatedAtUtc = DateTime.UtcNow;

            var provision = await EnsureEmployeeIdentityAsync(employee, tenantId.Value, HttpContext.RequestAborted);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated employee {EmployeeName} with ID {EmployeeId}. IdentityCreated={Created}", employee.FullName, employee.Id, provision.Created);

            return Ok(new { provision.Created, provision.TemporaryPassword });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteEmployee(Guid id)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                return Unauthorized();

            _context.SetTenant(tenantId);

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee == null)
                return NotFound();

            employee.IsDeleted = true;
            employee.UpdatedAtUtc = DateTime.UtcNow;

            if (employee.UserId.HasValue)
            {
                var user = await _userManager.FindByIdAsync(employee.UserId.Value.ToString());
                if (user != null)
                {
                    if (await _userManager.IsInRoleAsync(user, RoleNames.Employee))
                    {
                        await _userManager.RemoveFromRoleAsync(user, RoleNames.Employee);
                    }
                }
            }

            if (employee.UserId.HasValue)
            {
                var tenantUser = await _context.TenantUsers.FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == employee.UserId.Value);
                if (tenantUser != null)
                {
                    _context.TenantUsers.Remove(tenantUser);
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted employee {EmployeeName} with ID {EmployeeId}", employee.FullName, employee.Id);

            return Ok();
        }

        [HttpPost("{id}/permissions")]
        public async Task<IActionResult> UpdateEmployeePermissions(Guid id, [FromBody] EmployeePermissionsRequest request)
        {
            var tenantId = _tenantProvider.GetCurrentTenantId();
            if (tenantId == null)
                return Unauthorized();

            _context.SetTenant(tenantId);

            var employee = await _context.Employees
                .Include(e => e.Permissions)
                .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

            if (employee == null)
                return NotFound();

            if (employee.Permissions == null)
            {
                employee.Permissions = new EmployeePermissions
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId.Value,
                    EmployeeId = id,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.EmployeePermissions.Add(employee.Permissions);
            }

            UpdatePermissions(employee.Permissions, request);
            employee.Permissions.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated permissions for employee {EmployeeName} with ID {EmployeeId}", employee.FullName, employee.Id);

            return Ok();
        }

        private static void UpdatePermissions(EmployeePermissions permissions, EmployeePermissionsRequest request)
        {
            // Kierownik permissions
            permissions.KierownikCanAddClient = request.KierownikCanAddClient;
            permissions.KierownikCanEditClient = request.KierownikCanEditClient;
            permissions.KierownikCanDeleteClient = request.KierownikCanDeleteClient;
            permissions.KierownikCanAddProduct = request.KierownikCanAddProduct;
            permissions.KierownikCanEditProduct = request.KierownikCanEditProduct;
            permissions.KierownikCanDeleteProduct = request.KierownikCanDeleteProduct;
            permissions.KierownikCanAddRental = request.KierownikCanAddRental;
            permissions.KierownikCanEditRental = request.KierownikCanEditRental;
            permissions.KierownikCanDeleteRental = request.KierownikCanDeleteRental;
            permissions.KierownikCanAddEmployee = request.KierownikCanAddEmployee;
            permissions.KierownikCanEditEmployee = request.KierownikCanEditEmployee;
            permissions.KierownikCanDeleteEmployee = request.KierownikCanDeleteEmployee;
            permissions.KierownikCanSeeReports = request.KierownikCanSeeReports;
            permissions.KierownikCanAddMultipleRental = request.KierownikCanAddMultipleRental;
            permissions.KierownikCanEditMultipleRental = request.KierownikCanEditMultipleRental;
            permissions.KierownikCanDeleteMultipleRental = request.KierownikCanDeleteMultipleRental;

            // Manager permissions
            permissions.ManagerCanAddClient = request.ManagerCanAddClient;
            permissions.ManagerCanEditClient = request.ManagerCanEditClient;
            permissions.ManagerCanDeleteClient = request.ManagerCanDeleteClient;
            permissions.ManagerCanAddProduct = request.ManagerCanAddProduct;
            permissions.ManagerCanEditProduct = request.ManagerCanEditProduct;
            permissions.ManagerCanDeleteProduct = request.ManagerCanDeleteProduct;
            permissions.ManagerCanAddRental = request.ManagerCanAddRental;
            permissions.ManagerCanEditRental = request.ManagerCanEditRental;
            permissions.ManagerCanDeleteRental = request.ManagerCanDeleteRental;
            permissions.ManagerCanAddEmployee = request.ManagerCanAddEmployee;
            permissions.ManagerCanEditEmployee = request.ManagerCanEditEmployee;
            permissions.ManagerCanDeleteEmployee = request.ManagerCanDeleteEmployee;
            permissions.ManagerCanSeeReports = request.ManagerCanSeeReports;
            permissions.ManagerCanAddMultipleRental = request.ManagerCanAddMultipleRental;
            permissions.ManagerCanEditMultipleRental = request.ManagerCanEditMultipleRental;
            permissions.ManagerCanDeleteMultipleRental = request.ManagerCanDeleteMultipleRental;

            // Pracownik permissions
            permissions.PracownikCanAddClient = request.PracownikCanAddClient;
            permissions.PracownikCanEditClient = request.PracownikCanEditClient;
            permissions.PracownikCanDeleteClient = request.PracownikCanDeleteClient;
            permissions.PracownikCanAddProduct = request.PracownikCanAddProduct;
            permissions.PracownikCanEditProduct = request.PracownikCanEditProduct;
            permissions.PracownikCanDeleteProduct = request.PracownikCanDeleteProduct;
            permissions.PracownikCanAddRental = request.PracownikCanAddRental;
            permissions.PracownikCanEditRental = request.PracownikCanEditRental;
            permissions.PracownikCanDeleteRental = request.PracownikCanDeleteRental;
            permissions.PracownikCanAddEmployee = request.PracownikCanAddEmployee;
            permissions.PracownikCanEditEmployee = request.PracownikCanEditEmployee;
            permissions.PracownikCanDeleteEmployee = request.PracownikCanDeleteEmployee;
            permissions.PracownikCanSeeReports = request.PracownikCanSeeReports;
            permissions.PracownikCanAddMultipleRental = request.PracownikCanAddMultipleRental;
            permissions.PracownikCanEditMultipleRental = request.PracownikCanEditMultipleRental;
            permissions.PracownikCanDeleteMultipleRental = request.PracownikCanDeleteMultipleRental;
        }
        private async Task EnsureRolesAsync(IEnumerable<string> roles, CancellationToken cancellationToken)
        {
            foreach (var role in roles)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    var createResult = await _roleManager.CreateAsync(new IdentityRole<Guid> { Name = role, NormalizedName = role.ToUpperInvariant() });
                    if (!createResult.Succeeded)
                    {
                        var reason = string.Join(", ", createResult.Errors.Select(e => e.Description));
                        throw new InvalidOperationException($"Failed to create role {role}: {reason}");
                    }
                }
            }
        }

        private record EmployeeIdentityProvisionResult(Guid UserId, bool Created, string? TemporaryPassword);

        private async Task<EmployeeIdentityProvisionResult> EnsureEmployeeIdentityAsync(Employee employee, Guid tenantId, CancellationToken cancellationToken)
        {
            var email = employee.Email.Trim();
            var user = employee.UserId.HasValue
                ? await _userManager.FindByIdAsync(employee.UserId.Value.ToString())
                : null;

            if (user == null)
            {
                user = await _userManager.FindByEmailAsync(email);
            }

            var created = false;
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    TenantId = tenantId
                };

                var createResult = await _userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    var reason = string.Join(", ", createResult.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Failed to create Identity account for employee {email}: {reason}");
                }

                created = true;
            }
            else
            {
                var needsUpdate = false;
                if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    user.Email = email;
                    needsUpdate = true;
                }
                if (!string.Equals(user.UserName, email, StringComparison.OrdinalIgnoreCase))
                {
                    user.UserName = email;
                    needsUpdate = true;
                }
                if (user.TenantId != tenantId)
                {
                    user.TenantId = tenantId;
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    var updateResult = await _userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                    {
                        var reason = string.Join(", ", updateResult.Errors.Select(e => e.Description));
                        throw new InvalidOperationException($"Failed to update Identity account for employee {email}: {reason}");
                    }
                }
            }

            employee.UserId = user.Id;

            await EnsureRolesAsync(new[] { RoleNames.Employee, RoleNames.Client }, cancellationToken);

            foreach (var role in new[] { RoleNames.Employee, RoleNames.Client })
            {
                if (!await _userManager.IsInRoleAsync(user, role))
                {
                    var roleResult = await _userManager.AddToRoleAsync(user, role);
                    if (!roleResult.Succeeded)
                    {
                        var reason = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                        throw new InvalidOperationException($"Failed to assign role {role} to employee {email}: {reason}");
                    }
                }
            }

            var tenantUser = await _context.TenantUsers.FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == user.Id, cancellationToken);
            if (tenantUser == null)
            {
                _context.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = user.Id,
                    DisplayName = employee.FullName,
                    Role = RoleNames.Employee
                });
            }
            else
            {
                if (!string.Equals(tenantUser.DisplayName, employee.FullName, StringComparison.Ordinal))
                {
                    tenantUser.DisplayName = employee.FullName;
                }
                if (!string.Equals(tenantUser.Role, RoleNames.Employee, StringComparison.Ordinal))
                {
                    tenantUser.Role = RoleNames.Employee;
                }
            }

            string? temporaryPassword = null;
            if (created && !await _userManager.HasPasswordAsync(user))
            {
                var generated = GenerateTemporaryPassword();
                var passwordResult = await _userManager.AddPasswordAsync(user, generated);
                if (passwordResult.Succeeded)
                {
                    temporaryPassword = generated;
                }
                else
                {
                    _logger.LogWarning("Created employee {EmployeeEmail} without password due to Identity validation: {Errors}", email, string.Join(", ", passwordResult.Errors.Select(e => e.Description)));
                }
            }

            return new EmployeeIdentityProvisionResult(user.Id, created, temporaryPassword);
        }

        private static string GenerateTemporaryPassword()
        {
            var guid = Guid.NewGuid().ToString("N");
            return $"Tmp#{guid[..6]}1!";
        }

    }

    public class CreateEmployeeRequest
    {
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(200)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        [Required]
        [Phone]
        [MaxLength(20)]
        public string Telephone { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Position { get; set; }

        [Required]
        public EmployeeRole Role { get; set; } = EmployeeRole.Pracownik;

        [MaxLength(500)]
        public string? Comment { get; set; }
    }

    public class UpdateEmployeeRequest
    {
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(200)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        [Required]
        [Phone]
        [MaxLength(20)]
        public string Telephone { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Position { get; set; }

        [Required]
        public EmployeeRole Role { get; set; }

        [MaxLength(500)]
        public string? Comment { get; set; }
    }

    public class EmployeePermissionsRequest
    {
        // Kierownik permissions
        public bool KierownikCanAddClient { get; set; }
        public bool KierownikCanEditClient { get; set; }
        public bool KierownikCanDeleteClient { get; set; }
        public bool KierownikCanAddProduct { get; set; }
        public bool KierownikCanEditProduct { get; set; }
        public bool KierownikCanDeleteProduct { get; set; }
        public bool KierownikCanAddRental { get; set; }
        public bool KierownikCanEditRental { get; set; }
        public bool KierownikCanDeleteRental { get; set; }
        public bool KierownikCanAddEmployee { get; set; }
        public bool KierownikCanEditEmployee { get; set; }
        public bool KierownikCanDeleteEmployee { get; set; }
        public bool KierownikCanSeeReports { get; set; }
        public bool KierownikCanAddMultipleRental { get; set; }
        public bool KierownikCanEditMultipleRental { get; set; }
        public bool KierownikCanDeleteMultipleRental { get; set; }

        // Manager permissions
        public bool ManagerCanAddClient { get; set; }
        public bool ManagerCanEditClient { get; set; }
        public bool ManagerCanDeleteClient { get; set; }
        public bool ManagerCanAddProduct { get; set; }
        public bool ManagerCanEditProduct { get; set; }
        public bool ManagerCanDeleteProduct { get; set; }
        public bool ManagerCanAddRental { get; set; }
        public bool ManagerCanEditRental { get; set; }
        public bool ManagerCanDeleteRental { get; set; }
        public bool ManagerCanAddEmployee { get; set; }
        public bool ManagerCanEditEmployee { get; set; }
        public bool ManagerCanDeleteEmployee { get; set; }
        public bool ManagerCanSeeReports { get; set; }
        public bool ManagerCanAddMultipleRental { get; set; }
        public bool ManagerCanEditMultipleRental { get; set; }
        public bool ManagerCanDeleteMultipleRental { get; set; }

        // Pracownik permissions
        public bool PracownikCanAddClient { get; set; }
        public bool PracownikCanEditClient { get; set; }
        public bool PracownikCanDeleteClient { get; set; }
        public bool PracownikCanAddProduct { get; set; }
        public bool PracownikCanEditProduct { get; set; }
        public bool PracownikCanDeleteProduct { get; set; }
        public bool PracownikCanAddRental { get; set; }
        public bool PracownikCanEditRental { get; set; }
        public bool PracownikCanDeleteRental { get; set; }
        public bool PracownikCanAddEmployee { get; set; }
        public bool PracownikCanEditEmployee { get; set; }
        public bool PracownikCanDeleteEmployee { get; set; }
        public bool PracownikCanSeeReports { get; set; }
        public bool PracownikCanAddMultipleRental { get; set; }
        public bool PracownikCanEditMultipleRental { get; set; }
        public bool PracownikCanDeleteMultipleRental { get; set; }
    }
}










