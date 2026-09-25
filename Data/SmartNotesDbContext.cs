using System.Data.Entity;
using SmartNotesAI.Core.Models;

namespace SmartNotesAI.Data
{
    public class SmartNotesDbContext : DbContext
    {
        public SmartNotesDbContext() : base("name=DefaultConnection")
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<DocumentSection> DocumentSections { get; set; }
        public DbSet<Note> Notes { get; set; }
        public DbSet<Quiz> Quizzes { get; set; }
        public DbSet<QuizQuestion> QuizQuestions { get; set; }
        public DbSet<QuizAttempt> QuizAttempts { get; set; }
        public DbSet<QuizAnswerEvent> QuizAnswerEvents { get; set; }
        public DbSet<StudyPlanItem> StudyPlanItems { get; set; }
        public DbSet<ProgressSnapshot> ProgressSnapshots { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            // Configure conventions and relationships here if necessary
            base.OnModelCreating(modelBuilder);
        }
    }
}
