using Humatrix_HRMS.Data;
using Humatrix_HRMS.DTOsA.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Humatrix_HRMS.ControllersA.Api
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _configuration = configuration;
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequestDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);

            if (user == null)
            {
                return Unauthorized(new LoginResponseDto
                {
                    Success = false,
                    Message = "Invalid email or password"
                });
            }

            // User Active Check
            if (!user.IsActive)
            {
                return Unauthorized(new LoginResponseDto
                {
                    Success = false,
                    Message = "Account is deactivated"
                });
            }

            // Organization Active Check
            var organization = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrganizationId == user.OrganizationId);

            if (organization != null && !organization.IsActive)
            {
                return Unauthorized(new LoginResponseDto
                {
                    Success = false,
                    Message = "Organization is deactivated"
                });
            }

            // Password Check
            var validPassword = await _userManager.CheckPasswordAsync(user, dto.Password);

            if (!validPassword)
            {
                return Unauthorized(new LoginResponseDto
                {
                    Success = false,
                    Message = "Invalid email or password"
                });
            }

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "Employee";

            // JWT Claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim(ClaimTypes.Role, role),

                new Claim("OrganizationId",
                    user.OrganizationId?.ToString() ?? ""),

                new Claim("DepartmentId",
                    user.DepartmentId?.ToString() ?? "")
            };

            var jwtSettings = _configuration.GetSection("Jwt");

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings["Key"]!)
            );

            var creds = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256
            );

            var expiryMinutes =
                Convert.ToDouble(jwtSettings["DurationInMinutes"]);

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: creds
            );

            var jwtToken = new JwtSecurityTokenHandler().WriteToken(token);

            return Ok(new LoginResponseDto
            {
                Success = true,
                Message = "Login successful",
                Token = jwtToken,
                Email = user.Email ?? "",
                FullName = $"{user.FirstName} {user.LastName}",
                Role = role
            });
        }
    }
}