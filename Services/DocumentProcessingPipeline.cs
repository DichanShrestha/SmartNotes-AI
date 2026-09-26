using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SmartNotesAI.Core.Models;
using SmartNotesAI.Data;

namespace SmartNotesAI.Services
{
    public class DocumentProcessingPipeline
    {
        private readonly PdfExtractionService _pdfExtractor;
        private readonly GeminiService _geminiService;
        private readonly Sm2SchedulerService _sm2Scheduler;

        public DocumentProcessingPipeline()
        {
            _pdfExtractor = new PdfExtractionService();
            _geminiService = new GeminiService();
            _sm2Scheduler = new Sm2SchedulerService();
        }

        public async Task ProcessDocumentAsync(int documentId)
        {
            using (var context = new SmartNotesDbContext())
            {
                var doc = context.Documents.Find(documentId);
                if (doc == null) return;

                try
                {
                    // 1. Mark as Extracting
                    doc.Status = "Extracting";
                    doc.ErrorMessage = null;
                    context.SaveChanges();

                    if (!File.Exists(doc.StoragePath))
                    {
                        throw new FileNotFoundException("PDF file not found on server storage.", doc.StoragePath);
                    }

                    // 2. Extract Text & Sections
                    var (pageCount, extractedSections) = _pdfExtractor.ExtractSections(doc.StoragePath);
                    doc.PageCount = pageCount;

                    // Save extracted raw text file for fast retrieval/reference
                    var extractedDir = Path.Combine(Path.GetDirectoryName(doc.StoragePath), "Extracted");
                    if (!Directory.Exists(extractedDir)) Directory.CreateDirectory(extractedDir);
                    var textPath = Path.Combine(extractedDir, $"{doc.Id}_extracted.txt");
                    File.WriteAllText(textPath, string.Join("\n\n", extractedSections.Select(s => $"=== {s.Heading} ({s.PageRange}) ===\n{s.RawText}")));
                    doc.ExtractedTextPath = textPath;

                    // Remove any old sections (if re-processing)
                    var oldSections = context.DocumentSections.Where(s => s.DocumentId == doc.Id).ToList();
                    context.DocumentSections.RemoveRange(oldSections);

                    // Add new DocumentSection records
                    var dbSections = extractedSections.Select(s => new DocumentSection
                    {
                        DocumentId = doc.Id,
                        OrderIndex = s.OrderIndex,
                        Heading = s.Heading,
                        RawText = s.RawText,
                        PageRange = s.PageRange
                    }).ToList();

                    context.DocumentSections.AddRange(dbSections);
                    context.SaveChanges();

                    // 3. Mark as Generating
                    doc.Status = "Generating";
                    context.SaveChanges();

                    // 4. Generate Study Notes via Gemini
                    var oldNotes = context.Notes.Where(n => n.DocumentId == doc.Id).ToList();
                    context.Notes.RemoveRange(oldNotes);

                    var notesResult = await _geminiService.GenerateNotesAsync(doc.OriginalFilename, extractedSections);

                    // Add Executive Summary Note
                    context.Notes.Add(new Note
                    {
                        DocumentId = doc.Id,
                        SectionId = null,
                        Title = "📌 Executive Summary & Key Takeaways",
                        ContentMarkdown = notesResult.Summary,
                        OrderIndex = 0,
                        CreatedAt = DateTime.UtcNow
                    });

                    // Add section notes
                    int noteOrder = 1;
                    foreach (var noteItem in notesResult.Notes)
                    {
                        var matchingSection = dbSections.FirstOrDefault(s => s.Heading == noteItem.SectionHeading);
                        context.Notes.Add(new Note
                        {
                            DocumentId = doc.Id,
                            SectionId = matchingSection?.Id,
                            Title = noteItem.Title,
                            ContentMarkdown = noteItem.ContentMarkdown,
                            OrderIndex = noteOrder++,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    context.SaveChanges();

                    // 5. Generate Quiz via Gemini
                    var oldQuizzes = context.Quizzes.Where(q => q.DocumentId == doc.Id).ToList();
                    foreach (var oq in oldQuizzes)
                    {
                        var qQuestions = context.QuizQuestions.Where(q => q.QuizId == oq.Id).ToList();
                        context.QuizQuestions.RemoveRange(qQuestions);
                    }
                    context.Quizzes.RemoveRange(oldQuizzes);

                    var quizQuestions = await _geminiService.GenerateQuizAsync(doc.OriginalFilename, extractedSections);
                    var quiz = new Quiz
                    {
                        DocumentId = doc.Id,
                        Title = $"{Path.GetFileNameWithoutExtension(doc.OriginalFilename)} - Mastery Quiz",
                        CreatedAt = DateTime.UtcNow,
                        GenerationModel = _geminiService.HasValidApiKey ? "gemini-1.5-flash" : "SmartNotes-Deterministic-NLP"
                    };
                    context.Quizzes.Add(quiz);
                    context.SaveChanges();

                    int qOrder = 1;
                    foreach (var q in quizQuestions)
                    {
                        var matchingSection = dbSections.FirstOrDefault(s => s.Heading == q.SectionHeading);
                        context.QuizQuestions.Add(new QuizQuestion
                        {
                            QuizId = quiz.Id,
                            SectionId = matchingSection?.Id,
                            QuestionType = q.QuestionType,
                            PromptText = q.PromptText,
                            OptionsJson = JsonConvert.SerializeObject(q.Options ?? new System.Collections.Generic.List<string>()),
                            CorrectAnswer = q.CorrectAnswer,
                            Explanation = q.Explanation,
                            Difficulty = q.Difficulty,
                            OrderIndex = qOrder++
                        });
                    }
                    context.SaveChanges();

                    // 6. Seed SM-2 Study Plan
                    _sm2Scheduler.SeedStudyPlan(doc.UserId, doc.Id, dbSections, context);

                    // 7. Mark as Ready!
                    doc.Status = "Ready";
                    doc.ProcessedAt = DateTime.UtcNow;
                    doc.ErrorMessage = null;
                    context.SaveChanges();
                }
                catch (Exception ex)
                {
                    doc.Status = "Failed";
                    doc.ErrorMessage = ex.Message;
                    context.SaveChanges();
                }
            }
        }
    }
}
