using System;
using System.Collections.Generic;

namespace SmartNotesAI.Core.DTOs
{
    // --- Auth DTOs ---
    public class RegisterRequestDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string DisplayName { get; set; }
    }

    public class LoginRequestDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class RefreshTokenRequestDto
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
    }

    public class AuthResponseDto
    {
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public UserDto User { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    // --- Document DTOs ---
    public class DocumentDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string OriginalFilename { get; set; }
        public int PageCount { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public int SectionsCount { get; set; }
    }

    public class DocumentDetailDto : DocumentDto
    {
        public List<DocumentSectionDto> Sections { get; set; }
        public int NotesCount { get; set; }
        public bool HasQuiz { get; set; }
        public int StudyPlanItemsCount { get; set; }
    }

    public class DocumentSectionDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int OrderIndex { get; set; }
        public string Heading { get; set; }
        public string RawText { get; set; }
        public string PageRange { get; set; }
    }

    public class ExtractedSection
    {
        public string Heading { get; set; }
        public string RawText { get; set; }
        public string PageRange { get; set; }
        public int OrderIndex { get; set; }
    }

    // --- Notes DTOs ---
    public class NoteDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public int? SectionId { get; set; }
        public string SectionHeading { get; set; }
        public string Title { get; set; }
        public string ContentMarkdown { get; set; }
        public int OrderIndex { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class GeneratedNotesResult
    {
        public string Summary { get; set; }
        public List<GeneratedNoteItem> Notes { get; set; } = new List<GeneratedNoteItem>();
    }

    public class GeneratedNoteItem
    {
        public string Title { get; set; }
        public string ContentMarkdown { get; set; }
        public string SectionHeading { get; set; }
        public string PageRange { get; set; }
    }

    // --- Quiz DTOs ---
    public class QuizDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<QuizQuestionClientDto> Questions { get; set; } = new List<QuizQuestionClientDto>();
    }

    public class QuizQuestionClientDto
    {
        public int Id { get; set; }
        public int QuizId { get; set; }
        public int? SectionId { get; set; }
        public string SectionHeading { get; set; }
        public string QuestionType { get; set; } // MultipleChoice, ShortAnswer
        public string PromptText { get; set; }
        public List<string> Options { get; set; } = new List<string>();
        public string Difficulty { get; set; }
        public int OrderIndex { get; set; }
    }

    public class GeneratedQuestionDto
    {
        public string QuestionType { get; set; } // MultipleChoice, ShortAnswer
        public string PromptText { get; set; }
        public List<string> Options { get; set; } = new List<string>();
        public string CorrectAnswer { get; set; }
        public string Explanation { get; set; }
        public string Difficulty { get; set; } // Easy, Medium, Hard
        public string SectionHeading { get; set; }
    }

    public class SubmitAnswerRequestDto
    {
        public int QuestionId { get; set; }
        public string UserAnswer { get; set; }
    }

    public class AnswerResultDto
    {
        public int QuestionId { get; set; }
        public bool IsCorrect { get; set; }
        public string CorrectAnswer { get; set; }
        public string Explanation { get; set; }
        public string Feedback { get; set; }
    }

    public class CompleteAttemptResultDto
    {
        public int AttemptId { get; set; }
        public decimal ScorePercent { get; set; }
        public int TotalQuestions { get; set; }
        public int CorrectCount { get; set; }
        public int Grade { get; set; } // 0 - 5
        public DateTime CompletedAt { get; set; }
    }

    public class QuizAttemptSummaryDto
    {
        public int Id { get; set; }
        public int QuizId { get; set; }
        public string DocumentTitle { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public decimal ScorePercent { get; set; }
        public string Status { get; set; }
        public int TotalQuestions { get; set; }
        public int CorrectCount { get; set; }
    }

    // --- Study Plan DTOs ---
    public class StudyPlanItemDto
    {
        public int Id { get; set; }
        public int DocumentId { get; set; }
        public string DocumentTitle { get; set; }
        public int? SectionId { get; set; }
        public string SectionHeading { get; set; }
        public string PageRange { get; set; }
        public double EaseFactor { get; set; }
        public int IntervalDays { get; set; }
        public int Repetitions { get; set; }
        public DateTime DueAt { get; set; }
        public DateTime? LastReviewedAt { get; set; }
        public int? LastGrade { get; set; }
        public bool IsDue => DueAt <= DateTime.UtcNow;
    }

    public class ReviewStudyPlanItemDto
    {
        public int Grade { get; set; } // 0 to 5
    }

    // --- Dashboard DTOs ---
    public class DashboardStatsDto
    {
        public int DocumentsProcessed { get; set; }
        public int QuizzesCompleted { get; set; }
        public decimal AverageScore { get; set; }
        public int CurrentStreakDays { get; set; }
    }

    public class DashboardSummaryDto
    {
        public DashboardStatsDto Stats { get; set; }
        public List<DocumentDto> RecentDocuments { get; set; }
        public List<StudyPlanItemDto> DueItems { get; set; }
        public List<QuizAttemptSummaryDto> RecentAttempts { get; set; }
    }
}
