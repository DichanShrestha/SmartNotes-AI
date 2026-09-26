using System.Data.Entity;
using System.Data.Entity.ModelConfiguration.Conventions;
using SmartNotesAI.Core.Models;

namespace SmartNotesAI.Data
{
    public class SmartNotesDbContext : DbContext
    {
        public SmartNotesDbContext() : base("name=DefaultConnection")
        {
            Database.SetInitializer(new CreateDatabaseIfNotExists<SmartNotesDbContext>());
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
            modelBuilder.Conventions.Remove<OneToManyCascadeDeleteConvention>();
            modelBuilder.Conventions.Remove<ManyToManyCascadeDeleteConvention>();
            base.OnModelCreating(modelBuilder);
        }
    }
}
