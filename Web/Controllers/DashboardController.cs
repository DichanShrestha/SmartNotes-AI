using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using SmartNotesAI.Core.DTOs;
using SmartNotesAI.Data;

namespace SmartNotesAI.Web.Controllers
{
    [RoutePrefix("api/dashboard")]
    public class DashboardController : BaseApiController
    {
        private readonly SmartNotesDbContext _context;

        public DashboardController()
        {
            _context = new SmartNotesDbContext();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _context?.Dispose();
            }
            base.Dispose(disposing);
        }

        // GET /api/dashboard
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetDashboard()
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var stats = CalculateStats(userId);

            var recentDocuments = _context.Documents
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.CreatedAt)
                .Take(5)
                .Select(d => new DocumentDto
                {
                    Id = d.Id,
                    UserId = d.UserId,
                    OriginalFilename = d.OriginalFilename,
                    PageCount = d.PageCount,
                    Status = d.Status,
                    ErrorMessage = d.ErrorMessage,
                    CreatedAt = d.CreatedAt,
                    ProcessedAt = d.ProcessedAt,
                    SectionsCount = d.Sections.Count
                })
                .ToList();

            var now = DateTime.UtcNow;
            var dueItems = _context.StudyPlanItems
                .Include("Document")
                .Include("Section")
                .Where(s => s.UserId == userId && s.DueAt <= now)
                .OrderBy(s => s.DueAt)
                .Take(5)
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

            var recentAttempts = _context.QuizAttempts
                .Include("Quiz")
                .Include("Quiz.Document")
                .Include("Quiz.Questions")
                .Include("Answers")
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.StartedAt)
                .Take(5)
                .Select(a => new QuizAttemptSummaryDto
                {
                    Id = a.Id,
                    QuizId = a.QuizId,
                    DocumentTitle = a.Quiz.Document.OriginalFilename,
                    StartedAt = a.StartedAt,
                    CompletedAt = a.CompletedAt,
                    ScorePercent = a.ScorePercent,
                    Status = a.Status,
                    TotalQuestions = a.Quiz.Questions.Count,
                    CorrectCount = a.Answers.Count(ans => ans.IsCorrect)
                })
                .ToList();

            var summary = new DashboardSummaryDto
            {
                Stats = stats,
                RecentDocuments = recentDocuments,
                DueItems = dueItems,
                RecentAttempts = recentAttempts
            };

            return Ok(summary);
        }

        // GET /api/dashboard/stats
        [HttpGet]
        [Route("stats")]
        public IHttpActionResult GetStats()
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var stats = CalculateStats(userId);
            return Ok(stats);
        }

        private DashboardStatsDto CalculateStats(int userId)
        {
            var docsProcessed = _context.Documents.Count(d => d.UserId == userId && d.Status == "Ready");
            var completedAttempts = _context.QuizAttempts
                .Where(a => a.UserId == userId && a.Status == "Completed")
                .ToList();

            var quizzesCompleted = completedAttempts.Count;
            var avgScore = quizzesCompleted > 0
                ? Math.Round(completedAttempts.Average(a => a.ScorePercent), 1)
                : 0;

            // Compute current streak days
            var activityDates = completedAttempts
                .Where(a => a.CompletedAt.HasValue)
                .Select(a => a.CompletedAt.Value.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

            int streak = 0;
            var today = DateTime.UtcNow.Date;
            var expected = today;

            if (activityDates.Count > 0 && activityDates[0] == today.AddDays(-1))
            {
                expected = today.AddDays(-1);
            }

            foreach (var date in activityDates)
            {
                if (date == expected)
                {
                    streak++;
                    expected = expected.AddDays(-1);
                }
                else if (date < expected)
                {
                    break;
                }
            }

            if (streak == 0 && docsProcessed > 0)
            {
                streak = 1;
            }

            return new DashboardStatsDto
            {
                DocumentsProcessed = docsProcessed,
                QuizzesCompleted = quizzesCompleted,
                AverageScore = avgScore,
                CurrentStreakDays = streak
            };
        }
    }
}
