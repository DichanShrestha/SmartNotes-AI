using System;
using System.Collections.Generic;

namespace SmartNotesAI.Core.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string DisplayName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }

        public virtual ICollection<Document> Documents { get; set; }
    }

    public class Document
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string OriginalFilename { get; set; }
        public string StoragePath { get; set; }
        public int PageCount { get; set; }
        public string Status { get; set; } // Uploaded, Extracting, Generating, Ready, Failed
        public string ErrorMessage { get; set; }
        public string ExtractedTextPath { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }

        public virtual User User { get; set; }
        public virtual ICollection<DocumentSection> Sections { get; set; }
    }

    public class DocumentSection
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int OrderIndex { get; set; }
        public string Heading { get; set; }
        public string RawText { get; set; }
        public string PageRange { get; set; }

        public virtual Document Document { get; set; }
    }

    public class Note
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int? SectionId { get; set; }
        public string Title { get; set; }
        public string ContentMarkdown { get; set; }
        public int OrderIndex { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual Document Document { get; set; }
        public virtual DocumentSection Section { get; set; }
    }

    public class Quiz
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public string GenerationModel { get; set; }

        public virtual Document Document { get; set; }
        public virtual ICollection<QuizQuestion> Questions { get; set; }
    }

    public class QuizQuestion
    {
        public int Id { get; set; }
        public int QuizId { get; set; }
        public int? SectionId { get; set; }
        public string QuestionType { get; set; } // MultipleChoice, ShortAnswer
        public string PromptText { get; set; }
        public string OptionsJson { get; set; }
        public string CorrectAnswer { get; set; }
        public string Explanation { get; set; }
        public string Difficulty { get; set; }
        public int OrderIndex { get; set; }

        public virtual Quiz Quiz { get; set; }
        public virtual DocumentSection Section { get; set; }
    }

    public class QuizAttempt
    {
        public int Id { get; set; }
        public int QuizId { get; set; }
        public int UserId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public decimal ScorePercent { get; set; }
        public string Status { get; set; } // InProgress, Completed

        public virtual Quiz Quiz { get; set; }
        public virtual User User { get; set; }
    }

    public class QuizAnswerEvent
    {
        public int Id { get; set; }
        public int AttemptId { get; set; }
        public int QuestionId { get; set; }
        public string UserAnswer { get; set; }
        public bool IsCorrect { get; set; }
        public DateTime AnsweredAt { get; set; }

        public virtual QuizAttempt Attempt { get; set; }
        public virtual QuizQuestion Question { get; set; }
    }

    public class StudyPlanItem
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int DocumentId { get; set; }
        public int? SectionId { get; set; }
        public double EaseFactor { get; set; }
        public int IntervalDays { get; set; }
        public int Repetitions { get; set; }
        public DateTime DueAt { get; set; }
        public DateTime? LastReviewedAt { get; set; }
        public int? LastGrade { get; set; }

        public virtual User User { get; set; }
        public virtual Document Document { get; set; }
        public virtual DocumentSection Section { get; set; }
    }

    public class ProgressSnapshot
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int DocumentsProcessed { get; set; }
        public int QuizzesCompleted { get; set; }
        public decimal AverageScore { get; set; }
        public int CurrentStreakDays { get; set; }
        public DateTime ComputedAt { get; set; }

        public virtual User User { get; set; }
    }
}
