using System;
using System.Linq;
using System.Web.Http;
using SmartNotesAI.Data;

namespace SmartNotesAI.Web.Controllers
{
    [RoutePrefix("api/admin")]
    public class AdminController : BaseApiController
    {
        private readonly SmartNotesDbContext _db;

        public AdminController()
        {
            _db = new SmartNotesDbContext();
        }

        [HttpGet]
        [Route("stats")]
        public IHttpActionResult GetAdminStats()
        {
            var totalUsers = _db.Users.Count();
            var totalDocuments = _db.Documents.Count();
            var totalQuizzes = _db.Quizzes.Count();
            var totalAttempts = _db.QuizAttempts.Count();

            return Ok(new
            {
                TotalUsers = totalUsers,
                TotalDocuments = totalDocuments,
                TotalQuizzes = totalQuizzes,
                TotalQuizAttempts = totalAttempts
            });
        }

        [HttpGet]
        [Route("users")]
        public IHttpActionResult GetAllUsers()
        {
            var users = _db.Users
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new
                {
                    u.Id,
                    u.DisplayName,
                    u.Email,
                    u.CreatedAt,
                    u.LastLoginAt,
                    DocumentCount = u.Documents.Count()
                })
                .ToList();

            return Ok(users);
        }

        [HttpGet]
        [Route("documents")]
        public IHttpActionResult GetAllDocuments()
        {
            var docs = _db.Documents
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new
                {
                    d.Id,
                    d.OriginalFilename,
                    d.Status,
                    d.CreatedAt,
                    UserEmail = d.User.Email,
                    d.PageCount
                })
                .Take(50) // Limit for performance
                .ToList();

            return Ok(docs);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
