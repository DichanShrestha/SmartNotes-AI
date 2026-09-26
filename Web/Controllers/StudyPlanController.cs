using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using SmartNotesAI.Core.DTOs;
using SmartNotesAI.Data;
using SmartNotesAI.Services;

namespace SmartNotesAI.Web.Controllers
{
    [RoutePrefix("api/study-plan")]
    public class StudyPlanController : BaseApiController
    {
        private readonly SmartNotesDbContext _context;
        private readonly Sm2SchedulerService _sm2Scheduler;

        public StudyPlanController()
        {
            _context = new SmartNotesDbContext();
            _sm2Scheduler = new Sm2SchedulerService();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _context?.Dispose();
            }
            base.Dispose(disposing);
        }

        // GET /api/study-plan?filter=due|week|all
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetStudyPlan([FromUri] string filter = "due")
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var query = _context.StudyPlanItems
                .Include("Document")
                .Include("Section")
                .Where(s => s.UserId == userId);

            var now = DateTime.UtcNow;
            if (string.Equals(filter, "due", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(s => s.DueAt <= now);
            }
            else if (string.Equals(filter, "week", StringComparison.OrdinalIgnoreCase))
            {
                var weekFromNow = now.AddDays(7);
                query = query.Where(s => s.DueAt <= weekFromNow);
            }

            var items = query
                .OrderBy(s => s.DueAt)
                .Select(s => new StudyPlanItemDto
                {
                    Id = s.Id,
                    DocumentId = s.DocumentId,
                    DocumentTitle = s.Document.OriginalFilename,
                    SectionId = s.SectionId,
                    SectionHeading = s.Section != null ? s.Section.Heading : "Document Section",
                    PageRange = s.Section != null ? s.Section.PageRange : "",
                    EaseFactor = s.EaseFactor,
                    IntervalDays = s.IntervalDays,
                    Repetitions = s.Repetitions,
                    DueAt = s.DueAt,
                    LastReviewedAt = s.LastReviewedAt,
                    LastGrade = s.LastGrade
                })
                .ToList();

            return Ok(items);
        }

        // GET /api/study-plan/documents/{id}
        [HttpGet]
        [Route("documents/{id:int}")]
        public IHttpActionResult GetDocumentStudyPlan(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var items = _context.StudyPlanItems
                .Include("Document")
                .Include("Section")
                .Where(s => s.UserId == userId && s.DocumentId == id)
                .OrderBy(s => s.Section != null ? s.Section.OrderIndex : 0)
                .Select(s => new StudyPlanItemDto
                {
                    Id = s.Id,
                    DocumentId = s.DocumentId,
                    DocumentTitle = s.Document.OriginalFilename,
                    SectionId = s.SectionId,
                    SectionHeading = s.Section != null ? s.Section.Heading : "Document Section",
                    PageRange = s.Section != null ? s.Section.PageRange : "",
                    EaseFactor = s.EaseFactor,
                    IntervalDays = s.IntervalDays,
                    Repetitions = s.Repetitions,
                    DueAt = s.DueAt,
                    LastReviewedAt = s.LastReviewedAt,
                    LastGrade = s.LastGrade
                })
                .ToList();

            return Ok(items);
        }

        // POST /api/study-plan/items/{id}/review
        [HttpPost]
        [Route("items/{id:int}/review")]
        public IHttpActionResult ReviewItem(int id, [FromBody] ReviewStudyPlanItemDto dto)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            if (dto == null) return BadRequest("Missing review payload.");

            var item = _context.StudyPlanItems
                .Include("Document")
                .Include("Section")
                .FirstOrDefault(s => s.Id == id && s.UserId == userId);

            if (item == null) return NotFound();

            var updated = _sm2Scheduler.ReviewItem(item.Id, dto.Grade, _context);
            if (updated == null) return BadRequest("Could not review item.");

            return Ok(new StudyPlanItemDto
            {
                Id = updated.Id,
                DocumentId = updated.DocumentId,
                DocumentTitle = updated.Document?.OriginalFilename ?? item.Document.OriginalFilename,
                SectionId = updated.SectionId,
                SectionHeading = updated.Section?.Heading ?? item.Section?.Heading,
                PageRange = updated.Section?.PageRange ?? item.Section?.PageRange,
                EaseFactor = updated.EaseFactor,
                IntervalDays = updated.IntervalDays,
                Repetitions = updated.Repetitions,
                DueAt = updated.DueAt,
                LastReviewedAt = updated.LastReviewedAt,
                LastGrade = updated.LastGrade
            });
        }
    }
}
