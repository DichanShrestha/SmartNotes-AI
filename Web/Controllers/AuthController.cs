using System.Net;
using System.Net.Http;
using System.Web.Http;
using SmartNotesAI.Services;
using SmartNotesAI.Data;
using SmartNotesAI.Web.Security;

namespace SmartNotesAI.Web.Controllers
{
    [RoutePrefix("api/auth")]
    public class AuthController : ApiController
    {
        private readonly AuthService _authService;

        public AuthController()
        {
            // In a real scenario, use Dependency Injection (e.g., Unity or Autofac)
            _authService = new AuthService(new SmartNotesDbContext());
        }

        public class RegisterDto
        {
            public string Email { get; set; }
            public string Password { get; set; }
            public string DisplayName { get; set; }
        }

        [HttpPost]
        [Route("register")]
        public IHttpActionResult Register(RegisterDto dto)
        {
            try
            {
                var user = _authService.Register(dto.Email, dto.Password, dto.DisplayName);
                return Ok(new { Message = "Registration successful" });
            }
            catch (System.Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        public class LoginDto
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }

        [HttpPost]
        [Route("login")]
        public IHttpActionResult Login(LoginDto dto)
        {
            var user = _authService.Login(dto.Email, dto.Password);
            if (user == null)
                return Unauthorized();

            var token = JwtHelper.GenerateToken(user.Id, user.Email);
            return Ok(new { Token = token, User = new { user.Id, user.Email, user.DisplayName } });
        }
    }
}
