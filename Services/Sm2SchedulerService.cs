using System;
using System.Collections.Generic;
using System.Linq;
using SmartNotesAI.Core.Models;
using SmartNotesAI.Data;

namespace SmartNotesAI.Services
{
    public class Sm2SchedulerService
    {
        /// <summary>
        /// Applies the SuperMemo SM-2 spaced repetition algorithm
        /// </summary>
        public (double NewEaseFactor, int NewIntervalDays, int NewRepetitions, DateTime NewDueAt) CalculateNextReview(
            double currentEaseFactor,
            int currentIntervalDays,
            int currentRepetitions,
            int grade)
        {
            // Clamp grade between 0 and 5
            grade = Math.Max(0, Math.Min(5, grade));

            // Default initial ease factor if invalid
            if (currentEaseFactor < 1.3) currentEaseFactor = 2.5;

            int newIntervalDays;
            int newRepetitions;

            if (grade >= 3)
            {
                if (currentRepetitions == 0)
                {
                    newIntervalDays = 1;
                }
                else if (currentRepetitions == 1)
                {
                    newIntervalDays = 6;
                }
                else
                {
                    newIntervalDays = (int)Math.Max(1, Math.Round(currentIntervalDays * currentEaseFactor));
                }
                newRepetitions = currentRepetitions + 1;
            }
            else
            {
                newRepetitions = 0;
                newIntervalDays = 1;
            }

            // Update Ease Factor: EF' = EF + (0.1 - (5 - q) * (0.08 + (5 - q) * 0.02))
            double newEaseFactor = currentEaseFactor + (0.1 - (5 - grade) * (0.08 + (5 - grade) * 0.02));
            if (newEaseFactor < 1.3)
            {
                newEaseFactor = 1.3;
            }

            DateTime newDueAt = DateTime.UtcNow.AddDays(newIntervalDays);

            return (Math.Round(newEaseFactor, 2), newIntervalDays, newRepetitions, newDueAt);
        }

        public void SeedStudyPlan(int userId, int documentId, List<DocumentSection> sections, SmartNotesDbContext context)
        {
            if (sections == null || sections.Count == 0) return;

            foreach (var section in sections)
            {
                // Ensure not already seeded
                if (!context.StudyPlanItems.Any(s => s.UserId == userId && s.DocumentId == documentId && s.SectionId == section.Id))
                {
                    context.StudyPlanItems.Add(new StudyPlanItem
                    {
                        UserId = userId,
                        DocumentId = documentId,
                        SectionId = section.Id,
                        EaseFactor = 2.5,
                        IntervalDays = 1,
                        Repetitions = 0,
                        DueAt = DateTime.UtcNow, // Due immediately for initial study
                        LastReviewedAt = null,
                        LastGrade = null
                    });
                }
            }
            context.SaveChanges();
        }

        public void UpdateStudyPlanAfterQuiz(int userId, int documentId, decimal scorePercent, SmartNotesDbContext context)
        {
            int grade;
            if (scorePercent >= 90) grade = 5;
            else if (scorePercent >= 80) grade = 4;
            else if (scorePercent >= 70) grade = 3;
            else if (scorePercent >= 60) grade = 2;
            else if (scorePercent >= 50) grade = 1;
            else grade = 0;

            var items = context.StudyPlanItems
                .Where(s => s.UserId == userId && s.DocumentId == documentId)
                .ToList();

            foreach (var item in items)
            {
                var (ef, interval, reps, dueAt) = CalculateNextReview(item.EaseFactor, item.IntervalDays, item.Repetitions, grade);
                item.EaseFactor = ef;
                item.IntervalDays = interval;
                item.Repetitions = reps;
                item.DueAt = dueAt;
                item.LastReviewedAt = DateTime.UtcNow;
                item.LastGrade = grade;
            }

            context.SaveChanges();
        }

        public StudyPlanItem ReviewItem(int itemId, int grade, SmartNotesDbContext context)
        {
            var item = context.StudyPlanItems.Find(itemId);
            if (item == null) return null;

            var (ef, interval, reps, dueAt) = CalculateNextReview(item.EaseFactor, item.IntervalDays, item.Repetitions, grade);
            item.EaseFactor = ef;
            item.IntervalDays = interval;
            item.Repetitions = reps;
            item.DueAt = dueAt;
            item.LastReviewedAt = DateTime.UtcNow;
            item.LastGrade = grade;

            context.SaveChanges();
            return item;
        }
    }
}
