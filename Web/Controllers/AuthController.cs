using System;
using System.Web.Http;
using SmartNotesAI.Core.DTOs;
using SmartNotesAI.Data;
using SmartNotesAI.Services;
using SmartNotesAI.Web.Security;

namespace SmartNotesAI.Web.Controllers
{
    [RoutePrefix("api/auth")]
    public class AuthController : BaseApiController
    {
        private readonly AuthService _authService;
        private readonly SmartNotesDbContext _context;

        public AuthController()
        {
            _context = new SmartNotesDbContext();
            _authService = new AuthService(_context);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _context?.Dispose();
            }
            base.Dispose(disposing);
        }

        [HttpPost]
        [Route("register")]
        public IHttpActionResult Register([FromBody] RegisterRequestDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest("Email and password are required.");
            }

            try
            {
                var user = _authService.Register(dto.Email, dto.Password, dto.DisplayName);
                var token = JwtHelper.GenerateToken(user.Id, user.Email);

                return Ok(new AuthResponseDto
                {
                    Token = token,
                    RefreshToken = user.RefreshToken,
                    User = new UserDto
                    {
                        Id = user.Id,
                        Email = user.Email,
                        DisplayName = user.DisplayName,
                        CreatedAt = user.CreatedAt,
                        LastLoginAt = user.LastLoginAt
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        [Route("login")]
        public IHttpActionResult Login([FromBody] LoginRequestDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest("Email and password are required.");
            }

            var user = _authService.Login(dto.Email, dto.Password);
            if (user == null)
            {
                return Unauthorized();
            }

            var token = JwtHelper.GenerateToken(user.Id, user.Email);

            return Ok(new AuthResponseDto
            {
                Token = token,
                RefreshToken = user.RefreshToken,
                User = new UserDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    DisplayName = user.DisplayName,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt
                }
            });
        }

        [HttpPost]
        [Route("refresh")]
        public IHttpActionResult Refresh([FromBody] RefreshTokenRequestDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.RefreshToken))
            {
                return BadRequest("Refresh token is required.");
            }

            int? userId = null;
            if (!string.IsNullOrWhiteSpace(dto.Token))
            {
                userId = JwtHelper.ValidateAndGetUserId(dto.Token);
            }
            if (!userId.HasValue)
            {
                userId = GetCurrentUserId();
            }

            if (!userId.HasValue)
            {
                return Unauthorized();
            }

            var user = _authService.RefreshToken(userId.Value, dto.RefreshToken);
            if (user == null)
            {
                return Unauthorized();
            }

            var newToken = JwtHelper.GenerateToken(user.Id, user.Email);
            return Ok(new AuthResponseDto
            {
                Token = newToken,
                RefreshToken = user.RefreshToken,
                User = new UserDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    DisplayName = user.DisplayName,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = user.LastLoginAt
                }
            });
        }

        [HttpPost]
        [Route("logout")]
        public IHttpActionResult Logout()
        {
            var userId = GetCurrentUserId();
            if (userId.HasValue)
            {
                _authService.RevokeRefreshToken(userId.Value);
            }
            return Ok(new { Message = "Logged out successfully" });
        }
    }
}
