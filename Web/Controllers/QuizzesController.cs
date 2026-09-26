using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json;
using SmartNotesAI.Core.DTOs;
using SmartNotesAI.Core.Models;
using SmartNotesAI.Data;
using SmartNotesAI.Services;

namespace SmartNotesAI.Web.Controllers
{
    [RoutePrefix("api/quizzes")]
    public class QuizzesController : BaseApiController
    {
        private readonly SmartNotesDbContext _context;
        private readonly GeminiService _geminiService;
        private readonly Sm2SchedulerService _sm2Scheduler;

        public QuizzesController()
        {
            _context = new SmartNotesDbContext();
            _geminiService = new GeminiService();
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

        // POST /api/quizzes/{id}/attempts
        [HttpPost]
        [Route("{id:int}/attempts")]
        public IHttpActionResult StartAttempt(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var quiz = _context.Quizzes
                .Include("Document")
                .Include("Questions")
                .FirstOrDefault(q => q.Id == id && q.Document.UserId == userId);

            if (quiz == null) return NotFound();

            var attempt = new QuizAttempt
            {
                QuizId = quiz.Id,
                UserId = userId,
                StartedAt = DateTime.UtcNow,
                CompletedAt = null,
                ScorePercent = 0,
                Status = "InProgress"
            };

            _context.QuizAttempts.Add(attempt);
            _context.SaveChanges();

            return Ok(new
            {
                AttemptId = attempt.Id,
                QuizId = quiz.Id,
                DocumentId = quiz.DocumentId,
                QuizTitle = quiz.Title,
                StartedAt = attempt.StartedAt,
                TotalQuestions = quiz.Questions.Count
            });
        }

        // POST /api/quizzes/attempts/{id}/answers
        [HttpPost]
        [Route("attempts/{id:int}/answers")]
        public async Task<IHttpActionResult> SubmitAnswer(int id, [FromBody] SubmitAnswerRequestDto dto)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            if (dto == null) return BadRequest("Missing answer payload.");

            var attempt = _context.QuizAttempts
                .Include("Quiz")
                .FirstOrDefault(a => a.Id == id && a.UserId == userId);

            if (attempt == null) return NotFound();

            if (attempt.Status == "Completed")
            {
                return BadRequest("This quiz attempt is already completed.");
            }

            var question = _context.QuizQuestions
                .FirstOrDefault(q => q.Id == dto.QuestionId && q.QuizId == attempt.QuizId);

            if (question == null) return NotFound();

            bool isCorrect = false;
            string feedback = string.Empty;

            if (string.Equals(question.QuestionType, "ShortAnswer", StringComparison.OrdinalIgnoreCase))
            {
                // Call Gemini for semantic grading (or fallback)
                var gradeResult = await _geminiService.GradeShortAnswerAsync(
                    question.PromptText,
                    question.CorrectAnswer,
                    dto.UserAnswer,
                    question.Explanation);

                isCorrect = gradeResult.IsCorrect;
                feedback = gradeResult.Feedback;
            }
            else
            {
                // Multiple Choice: normalize and check answer
                var cleanUser = (dto.UserAnswer ?? string.Empty).Trim();
                var cleanCorrect = (question.CorrectAnswer ?? string.Empty).Trim();

                isCorrect = string.Equals(cleanUser, cleanCorrect, StringComparison.OrdinalIgnoreCase);
                feedback = isCorrect
                    ? "Correct! Excellent grasp of the concept."
                    : $"Incorrect. The correct answer was: {question.CorrectAnswer}";
            }

            // Save or update QuizAnswerEvent
            var existingEvent = _context.QuizAnswerEvents
                .FirstOrDefault(e => e.AttemptId == attempt.Id && e.QuestionId == question.Id);

            if (existingEvent != null)
            {
                existingEvent.UserAnswer = dto.UserAnswer;
                existingEvent.IsCorrect = isCorrect;
                existingEvent.AnsweredAt = DateTime.UtcNow;
            }
            else
            {
                _context.QuizAnswerEvents.Add(new QuizAnswerEvent
                {
                    AttemptId = attempt.Id,
                    QuestionId = question.Id,
                    UserAnswer = dto.UserAnswer,
                    IsCorrect = isCorrect,
                    AnsweredAt = DateTime.UtcNow
                });
            }

            _context.SaveChanges();

            return Ok(new AnswerResultDto
            {
                QuestionId = question.Id,
                IsCorrect = isCorrect,
                CorrectAnswer = question.CorrectAnswer,
                Explanation = question.Explanation,
                Feedback = feedback
            });
        }

        // POST /api/quizzes/attempts/{id}/complete
        [HttpPost]
        [Route("attempts/{id:int}/complete")]
        public IHttpActionResult CompleteAttempt(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var attempt = _context.QuizAttempts
                .Include("Quiz")
                .Include("Answers")
                .FirstOrDefault(a => a.Id == id && a.UserId == userId);

            if (attempt == null) return NotFound();

            var quiz = _context.Quizzes
                .Include("Questions")
                .FirstOrDefault(q => q.Id == attempt.QuizId);

            int totalQuestions = quiz?.Questions.Count ?? 0;
            int correctCount = attempt.Answers.Count(ans => ans.IsCorrect);

            decimal scorePercent = 0;
            if (totalQuestions > 0)
            {
                scorePercent = Math.Round(((decimal)correctCount / totalQuestions) * 100m, 1);
            }

            attempt.Status = "Completed";
            attempt.CompletedAt = DateTime.UtcNow;
            attempt.ScorePercent = scorePercent;

            _context.SaveChanges();

            // 1. Update SM-2 Study Plan for Document Sections
            try
            {
                _sm2Scheduler.UpdateStudyPlanAfterQuiz(userId, attempt.Quiz.DocumentId, scorePercent, _context);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("SM-2 update error: " + ex.Message);
            }

            // 2. Compute and Save Progress Snapshot
            UpdateUserProgressSnapshot(userId);

            int grade = CalculateGrade(scorePercent);

            return Ok(new CompleteAttemptResultDto
            {
                AttemptId = attempt.Id,
                ScorePercent = scorePercent,
                TotalQuestions = totalQuestions,
                CorrectCount = correctCount,
                Grade = grade,
                CompletedAt = attempt.CompletedAt.Value
            });
        }

        // GET or POST /api/quizzes/{id}/regenerate
        [HttpGet]
        [HttpPost]
        [Route("{id:int}/regenerate")]
        public async Task<IHttpActionResult> RegenerateQuiz(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var quiz = _context.Quizzes
                .Include("Document")
                .FirstOrDefault(q => q.Id == id && q.Document.UserId == userId);

            if (quiz == null) return NotFound();

            var sections = _context.DocumentSections
                .Where(s => s.DocumentId == quiz.DocumentId)
                .OrderBy(s => s.OrderIndex)
                .ToList();

            var extractedSections = sections.Select(s => new ExtractedSection
            {
                OrderIndex = s.OrderIndex,
                Heading = s.Heading,
                RawText = s.RawText,
                PageRange = s.PageRange
            }).ToList();

            var generatedQuestions = await _geminiService.GenerateQuizAsync(quiz.Document.OriginalFilename, extractedSections);

            // Remove existing quiz questions
            var oldQuestions = _context.QuizQuestions.Where(q => q.QuizId == quiz.Id).ToList();
            _context.QuizQuestions.RemoveRange(oldQuestions);

            quiz.CreatedAt = DateTime.UtcNow;
            quiz.GenerationModel = _geminiService.HasValidApiKey ? "gemini-1.5-flash" : "SmartNotes-Deterministic-NLP";

            int qOrder = 1;
            foreach (var q in generatedQuestions)
            {
                var matchingSection = sections.FirstOrDefault(s => s.Heading == q.SectionHeading);
                _context.QuizQuestions.Add(new QuizQuestion
                {
                    QuizId = quiz.Id,
                    SectionId = matchingSection?.Id,
                    QuestionType = q.QuestionType,
                    PromptText = q.PromptText,
                    OptionsJson = JsonConvert.SerializeObject(q.Options ?? new List<string>()),
                    CorrectAnswer = q.CorrectAnswer,
                    Explanation = q.Explanation,
                    Difficulty = q.Difficulty,
                    OrderIndex = qOrder++
                });
            }

            _context.SaveChanges();

            // Reload and return safe client DTO
            var questions = _context.QuizQuestions
                .Where(q => q.QuizId == quiz.Id)
                .OrderBy(q => q.OrderIndex)
                .ToList();

            var quizDto = new QuizDto
            {
                Id = quiz.Id,
                DocumentId = quiz.DocumentId,
                Title = quiz.Title,
                CreatedAt = quiz.CreatedAt,
                Questions = questions.Select(q =>
                {
                    List<string> options = new List<string>();
                    if (!string.IsNullOrEmpty(q.OptionsJson))
                    {
                        try { options = JsonConvert.DeserializeObject<List<string>>(q.OptionsJson); }
                        catch { }
                    }

                    return new QuizQuestionClientDto
                    {
                        Id = q.Id,
                        QuizId = q.QuizId,
                        SectionId = q.SectionId,
                        SectionHeading = q.Section != null ? q.Section.Heading : null,
                        QuestionType = q.QuestionType,
                        PromptText = q.PromptText,
                        Options = options,
                        Difficulty = q.Difficulty,
                        OrderIndex = q.OrderIndex
                    };
                }).ToList()
            };

            return Ok(quizDto);
        }

        private int CalculateGrade(decimal scorePercent)
        {
            if (scorePercent >= 90) return 5;
            if (scorePercent >= 80) return 4;
            if (scorePercent >= 70) return 3;
            if (scorePercent >= 60) return 2;
            if (scorePercent >= 50) return 1;
            return 0;
        }

        private void UpdateUserProgressSnapshot(int userId)
        {
            try
            {
                var docsCount = _context.Documents.Count(d => d.UserId == userId && d.Status == "Ready");
                var completedAttempts = _context.QuizAttempts
                    .Where(a => a.UserId == userId && a.Status == "Completed")
                    .ToList();

                var quizzesCompleted = completedAttempts.Count;
                var avgScore = quizzesCompleted > 0
                    ? Math.Round(completedAttempts.Average(a => a.ScorePercent), 1)
                    : 0;

                // Calculate streak days (consecutive days with completed activity)
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

                if (streak == 0 && docsCount > 0)
                {
                    streak = 1;
                }

                var snapshot = _context.ProgressSnapshots
                    .OrderByDescending(p => p.ComputedAt)
                    .FirstOrDefault(p => p.UserId == userId);

                if (snapshot == null)
                {
                    _context.ProgressSnapshots.Add(new ProgressSnapshot
                    {
                        UserId = userId,
                        DocumentsProcessed = docsCount,
                        QuizzesCompleted = quizzesCompleted,
                        AverageScore = avgScore,
                        CurrentStreakDays = streak,
                        ComputedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    snapshot.DocumentsProcessed = docsCount;
                    snapshot.QuizzesCompleted = quizzesCompleted;
                    snapshot.AverageScore = avgScore;
                    snapshot.CurrentStreakDays = streak;
                    snapshot.ComputedAt = DateTime.UtcNow;
                }

                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Progress snapshot error: " + ex.Message);
            }
        }
    }
}
