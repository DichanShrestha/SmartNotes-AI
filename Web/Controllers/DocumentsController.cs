using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Hangfire;
using Newtonsoft.Json;
using SmartNotesAI.Core.DTOs;
using SmartNotesAI.Core.Models;
using SmartNotesAI.Data;
using SmartNotesAI.Services;

namespace SmartNotesAI.Web.Controllers
{
    [RoutePrefix("api/documents")]
    public class DocumentsController : BaseApiController
    {
        private readonly SmartNotesDbContext _context;
        private readonly DocumentProcessingPipeline _pipeline;
        private readonly GeminiService _geminiService;
        private readonly PdfExtractionService _pdfExtractor;

        public DocumentsController()
        {
            _context = new SmartNotesDbContext();
            _pipeline = new DocumentProcessingPipeline();
            _geminiService = new GeminiService();
            _pdfExtractor = new PdfExtractionService();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _context?.Dispose();
            }
            base.Dispose(disposing);
        }

        // GET /api/documents
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetDocuments()
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var docs = _context.Documents
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.CreatedAt)
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

            return Ok(docs);
        }

        // GET /api/documents/{id}
        [HttpGet]
        [Route("{id:int}")]
        public IHttpActionResult GetDocument(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var doc = _context.Documents
                .Include("Sections")
                .Include("Notes")
                .Include("Quizzes")
                .Include("StudyPlanItems")
                .FirstOrDefault(d => d.Id == id && d.UserId == userId);

            if (doc == null) return NotFound();

            var dto = new DocumentDetailDto
            {
                Id = doc.Id,
                UserId = doc.UserId,
                OriginalFilename = doc.OriginalFilename,
                PageCount = doc.PageCount,
                Status = doc.Status,
                ErrorMessage = doc.ErrorMessage,
                CreatedAt = doc.CreatedAt,
                ProcessedAt = doc.ProcessedAt,
                SectionsCount = doc.Sections?.Count ?? 0,
                NotesCount = doc.Notes?.Count ?? 0,
                HasQuiz = doc.Quizzes != null && doc.Quizzes.Any(),
                StudyPlanItemsCount = doc.StudyPlanItems?.Count ?? 0,
                Sections = doc.Sections?.OrderBy(s => s.OrderIndex).Select(s => new DocumentSectionDto
                {
                    Id = s.Id,
                    DocumentId = s.DocumentId,
                    OrderIndex = s.OrderIndex,
                    Heading = s.Heading,
                    PageRange = s.PageRange,
                    RawText = s.RawText
                }).ToList() ?? new List<DocumentSectionDto>()
            };

            return Ok(dto);
        }

        // POST /api/documents (and alias /upload) -> Returns HTTP 202 Accepted
        [HttpPost]
        [Route("")]
        [Route("upload")]
        public async Task<IHttpActionResult> UploadPdf()
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            if (!Request.Content.IsMimeMultipartContent())
            {
                return StatusCode(HttpStatusCode.UnsupportedMediaType);
            }

            var uploadDir = HttpContext.Current.Server.MapPath("~/App_Data/Uploads");
            if (!Directory.Exists(uploadDir))
            {
                Directory.CreateDirectory(uploadDir);
            }

            var provider = new MultipartMemoryStreamProvider();
            await Request.Content.ReadAsMultipartAsync(provider);

            var fileContent = provider.Contents.FirstOrDefault(c => !string.IsNullOrEmpty(c.Headers.ContentDisposition?.FileName));
            if (fileContent == null)
            {
                return BadRequest("No file found in request.");
            }

            var originalFilename = fileContent.Headers.ContentDisposition.FileName.Trim('\"');
            var extension = Path.GetExtension(originalFilename)?.ToLower();
            if (extension != ".pdf")
            {
                return BadRequest("Only PDF files are supported.");
            }

            var fileBytes = await fileContent.ReadAsByteArrayAsync();
            if (fileBytes.Length == 0)
            {
                return BadRequest("Uploaded file is empty.");
            }

            // Max file size 50MB
            if (fileBytes.Length > 50 * 1024 * 1024)
            {
                return BadRequest("File size exceeds 50MB maximum limit.");
            }

            // Validate PDF signature (%PDF)
            if (fileBytes.Length < 4 ||
                fileBytes[0] != 0x25 || fileBytes[1] != 0x50 || fileBytes[2] != 0x44 || fileBytes[3] != 0x46)
            {
                return BadRequest("Invalid PDF file signature.");
            }

            var safeFilename = $"{Guid.NewGuid()}_{Path.GetFileName(originalFilename)}";
            var savedFilePath = Path.Combine(uploadDir, safeFilename);
            File.WriteAllBytes(savedFilePath, fileBytes);

            var document = new Document
            {
                UserId = userId,
                OriginalFilename = originalFilename,
                StoragePath = savedFilePath,
                PageCount = 0,
                Status = "Uploaded",
                CreatedAt = DateTime.UtcNow
            };

            _context.Documents.Add(document);
            _context.SaveChanges();

            // Enqueue Hangfire background job (with async Task fallback)
            try
            {
                BackgroundJob.Enqueue<DocumentProcessingPipeline>(p => p.ProcessDocumentAsync(document.Id));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Hangfire enqueue fallback: " + ex.Message);
                System.Web.Hosting.HostingEnvironment.QueueBackgroundWorkItem(async ct =>
                {
                    var pipeline = new DocumentProcessingPipeline();
                    await pipeline.ProcessDocumentAsync(document.Id);
                });
            }

            var responseDto = new DocumentDto
            {
                Id = document.Id,
                UserId = document.UserId,
                OriginalFilename = document.OriginalFilename,
                PageCount = 0,
                Status = "Uploaded",
                CreatedAt = document.CreatedAt
            };

            // Return 202 Accepted
            return Content(HttpStatusCode.Accepted, responseDto);
        }

        // DELETE /api/documents/{id}
        [HttpDelete]
        [Route("{id:int}")]
        public IHttpActionResult DeleteDocument(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var doc = _context.Documents.FirstOrDefault(d => d.Id == id && d.UserId == userId);
            if (doc == null) return NotFound();

            // Remove associated entities
            var studyItems = _context.StudyPlanItems.Where(s => s.DocumentId == doc.Id).ToList();
            _context.StudyPlanItems.RemoveRange(studyItems);

            var quizzes = _context.Quizzes.Where(q => q.DocumentId == doc.Id).ToList();
            foreach (var q in quizzes)
            {
                var attempts = _context.QuizAttempts.Where(a => a.QuizId == q.Id).ToList();
                foreach (var a in attempts)
                {
                    var answers = _context.QuizAnswerEvents.Where(ans => ans.AttemptId == a.Id).ToList();
                    _context.QuizAnswerEvents.RemoveRange(answers);
                }
                _context.QuizAttempts.RemoveRange(attempts);

                var questions = _context.QuizQuestions.Where(qq => qq.QuizId == q.Id).ToList();
                _context.QuizQuestions.RemoveRange(questions);
            }
            _context.Quizzes.RemoveRange(quizzes);

            var notes = _context.Notes.Where(n => n.DocumentId == doc.Id).ToList();
            _context.Notes.RemoveRange(notes);

            var sections = _context.DocumentSections.Where(s => s.DocumentId == doc.Id).ToList();
            _context.DocumentSections.RemoveRange(sections);

            _context.Documents.Remove(doc);
            _context.SaveChanges();

            // Remove files from disk
            try
            {
                if (File.Exists(doc.StoragePath)) File.Delete(doc.StoragePath);
                if (!string.IsNullOrEmpty(doc.ExtractedTextPath) && File.Exists(doc.ExtractedTextPath))
                {
                    File.Delete(doc.ExtractedTextPath);
                }
            }
            catch { }

            return Ok(new { Message = "Document deleted successfully." });
        }

        // GET /api/documents/{id}/notes
        [HttpGet]
        [Route("{id:int}/notes")]
        public IHttpActionResult GetNotes(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var doc = _context.Documents.FirstOrDefault(d => d.Id == id && d.UserId == userId);
            if (doc == null) return NotFound();

            var notes = _context.Notes
                .Where(n => n.DocumentId == id)
                .OrderBy(n => n.OrderIndex)
                .Select(n => new NoteDto
                {
                    Id = n.Id,
                    DocumentId = n.DocumentId,
                    SectionId = n.SectionId,
                    SectionHeading = n.Section != null ? n.Section.Heading : null,
                    Title = n.Title,
                    ContentMarkdown = n.ContentMarkdown,
                    OrderIndex = n.OrderIndex,
                    CreatedAt = n.CreatedAt
                })
                .ToList();

            return Ok(notes);
        }

        // POST /api/documents/{id}/notes/regenerate
        [HttpPost]
        [Route("{id:int}/notes/regenerate")]
        public async Task<IHttpActionResult> RegenerateNotes(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var doc = _context.Documents.FirstOrDefault(d => d.Id == id && d.UserId == userId);
            if (doc == null) return NotFound();

            // 1. Remove existing notes first
            var oldNotes = _context.Notes.Where(n => n.DocumentId == id).ToList();
            _context.Notes.RemoveRange(oldNotes);
            _context.SaveChanges();

            // 2. If physical file exists, re-extract and update sections in-place
            var existingSections = _context.DocumentSections
                .Where(s => s.DocumentId == id)
                .OrderBy(s => s.OrderIndex)
                .ToList();

            if (File.Exists(doc.StoragePath))
            {
                var (pageCount, freshSections) = _pdfExtractor.ExtractSections(doc.StoragePath);
                doc.PageCount = pageCount;

                int minCount = Math.Min(existingSections.Count, freshSections.Count);
                for (int i = 0; i < minCount; i++)
                {
                    existingSections[i].Heading = freshSections[i].Heading;
                    existingSections[i].RawText = freshSections[i].RawText;
                    existingSections[i].PageRange = freshSections[i].PageRange;
                    existingSections[i].OrderIndex = freshSections[i].OrderIndex;
                }

                if (freshSections.Count > existingSections.Count)
                {
                    for (int i = existingSections.Count; i < freshSections.Count; i++)
                    {
                        var newSec = new DocumentSection
                        {
                            DocumentId = doc.Id,
                            OrderIndex = freshSections[i].OrderIndex,
                            Heading = freshSections[i].Heading,
                            RawText = freshSections[i].RawText,
                            PageRange = freshSections[i].PageRange
                        };
                        _context.DocumentSections.Add(newSec);
                        existingSections.Add(newSec);
                    }
                }
                else if (freshSections.Count < existingSections.Count)
                {
                    for (int i = freshSections.Count; i < existingSections.Count; i++)
                    {
                        var secToRemove = existingSections[i];
                        var questions = _context.QuizQuestions.Where(q => q.SectionId == secToRemove.Id).ToList();
                        foreach (var q in questions) q.SectionId = null;
                        var planItems = _context.StudyPlanItems.Where(p => p.SectionId == secToRemove.Id).ToList();
                        foreach (var p in planItems) p.SectionId = null;

                        _context.DocumentSections.Remove(secToRemove);
                    }
                    existingSections.RemoveRange(freshSections.Count, existingSections.Count - freshSections.Count);
                }

                _context.SaveChanges();
            }

            var extractedSections = existingSections.Select(s => new ExtractedSection
            {
                OrderIndex = s.OrderIndex,
                Heading = s.Heading,
                RawText = s.RawText,
                PageRange = s.PageRange
            }).ToList();

            // 3. Generate structured notes
            var notesResult = await _geminiService.GenerateNotesAsync(doc.OriginalFilename, extractedSections);

            // Insert summary note
            _context.Notes.Add(new Note
            {
                DocumentId = doc.Id,
                SectionId = null,
                Title = "📌 Executive Summary & Key Takeaways",
                ContentMarkdown = notesResult.Summary,
                OrderIndex = 0,
                CreatedAt = DateTime.UtcNow
            });

            int order = 1;
            foreach (var item in notesResult.Notes)
            {
                var matchingSection = existingSections.FirstOrDefault(s => s.Heading == item.SectionHeading);
                _context.Notes.Add(new Note
                {
                    DocumentId = doc.Id,
                    SectionId = matchingSection?.Id,
                    Title = item.Title,
                    ContentMarkdown = item.ContentMarkdown,
                    OrderIndex = order++,
                    CreatedAt = DateTime.UtcNow
                });
            }


            _context.SaveChanges();

            return Ok(new { Message = "Notes regenerated successfully." });
        }

        // GET /api/documents/{id}/quiz
        [HttpGet]
        [Route("{id:int}/quiz")]
        public IHttpActionResult GetQuiz(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var doc = _context.Documents.FirstOrDefault(d => d.Id == id && d.UserId == userId);
            if (doc == null) return NotFound();

            var quiz = _context.Quizzes
                .Include("Questions")
                .FirstOrDefault(q => q.DocumentId == id);

            if (quiz == null)
            {
                return NotFound();
            }

            // CRITICAL RULE: Correct answers must never be sent to the client before the user answers
            var quizDto = new QuizDto
            {
                Id = quiz.Id,
                DocumentId = quiz.DocumentId,
                Title = quiz.Title,
                CreatedAt = quiz.CreatedAt,
                Questions = quiz.Questions.OrderBy(q => q.OrderIndex).Select(q =>
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

        // GET /api/documents/{id}/attempts
        [HttpGet]
        [Route("{id:int}/attempts")]
        public IHttpActionResult GetDocumentAttempts(int id)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            var doc = _context.Documents.FirstOrDefault(d => d.Id == id && d.UserId == userId);
            if (doc == null) return NotFound();

            var quiz = _context.Quizzes.FirstOrDefault(q => q.DocumentId == id);
            if (quiz == null) return Ok(new List<QuizAttemptSummaryDto>());

            var attempts = _context.QuizAttempts
                .Where(a => a.QuizId == quiz.Id && a.UserId == userId)
                .OrderByDescending(a => a.StartedAt)
                .Select(a => new QuizAttemptSummaryDto
                {
                    Id = a.Id,
                    QuizId = a.QuizId,
                    DocumentTitle = doc.OriginalFilename,
                    StartedAt = a.StartedAt,
                    CompletedAt = a.CompletedAt,
                    ScorePercent = a.ScorePercent,
                    Status = a.Status,
                    TotalQuestions = a.Quiz.Questions.Count,
                    CorrectCount = a.Answers.Count(ans => ans.IsCorrect)
                })
                .ToList();

            return Ok(attempts);
        }
    }
}
