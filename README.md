# SmartNotes AI - Intelligent PDF-to-Learning Platform

**SmartNotes AI** is an intelligent, full-stack learning platform built on **ASP.NET Framework 4.8** and **Entity Framework 6** that converts uploaded academic PDFs into structured study notes, auto-generated quizzes (Multiple Choice + Short Answer), and personalized spaced repetition study schedules based on the SuperMemo SM-2 algorithm.

---

## 🌟 Key Features

1. **Authentication & Session Security**
   - User registration and login with **BCrypt.Net** password hashing.
   - Custom **JWT (JSON Web Token)** authentication filter with access & refresh token lifecycle.
   - Real-time dashboard showing documents processed, quizzes completed, average mastery score, and consecutive learning streaks.

2. **Asynchronous PDF Processing Pipeline (Hangfire)**
   - Accepts academic PDFs up to **50MB** and **~150 pages** with binary signature (`%PDF`) and MIME validation.
   - Non-blocking HTTP **202 Accepted** upload endpoint.
   - Background execution via **Hangfire** (with SQL Server storage):
     - **Text Extraction**: High-performance extraction via **UglyToad.PdfPig** with **Tesseract OCR** fallback for scanned/image pages.
     - **Section & Chapter Detection**: Intelligent heading pattern detection and semantic boundary chunking.
     - **AI Study Notes Generation**: Map-reduce notes generator utilizing **Google Gemini API** (`gemini-1.5-flash`) with structured JSON schema outputs and resilient deterministic NLP fallbacks.
     - **Auto-Generated Mastery Quizzes**: Balanced mix of Multiple Choice (4 distinct options) and Short Answer questions tagged by section and difficulty level.
     - **SM-2 Seeding**: Automatic creation of Spaced Repetition items for each extracted document section.
     - **Real-Time Polling**: Visual pipeline status progression (`Uploaded` ➔ `Extracting` ➔ `Generating` ➔ `Ready`).

3. **AI-Graded Quiz Engine**
   - Stateful quiz attempts with real-time answer persistence.
   - **Semantic Grading**: Short answers are evaluated semantically by Google Gemini to assess conceptual accuracy rather than exact keyword match.
   - Instant feedback with correct answers, grading explanations, and hints.
   - Automatic calculation of attempt scores and performance grade (0 to 5).

4. **Personalized Spaced Repetition (SuperMemo SM-2)**
   - Section-level tracking of Ease Factor ($EF \ge 1.3$), repetition count, and interval days ($I$).
   - Automatic adjustment of review schedules upon quiz completion.
   - Manual review ratings (Grades 0 to 5) with instant interval recalculation.
   - Dashboard alerts for items due for review today and this week.

5. **Pure Vanilla Modern Frontend**
   - Zero frontend frameworks (No React, No TypeScript, No Bootstrap, No external bundles).
   - Single Page Application (SPA) architecture using native `fetch()` and DOM manipulation.
   - Luxury dark theme with obsidian glassmorphism, responsive grid, and custom markdown rendering.

---

## 🏗 Solution Architecture

The solution `SmartNotesAI.sln` follows a clean, modular multi-tier architecture:

```
SmartNotesAI/
├── Core/                     # SmartNotesAI.Core
│   ├── Models/
│   │   └── Entities.cs       # 10 EF6 Entity Models
│   └── DTOs/
│       └── Models.cs         # API Request/Response DTOs
├── Data/                     # SmartNotesAI.Data
│   └── SmartNotesDbContext.cs# EF6 DbContext with LocalDB config
├── Services/                 # SmartNotesAI.Services
│   ├── AuthService.cs        # BCrypt hashing & refresh token rotation
│   ├── PdfExtractionService.cs # PdfPig extraction + OCR fallback
│   ├── GeminiService.cs      # Google Gemini API client + JSON schema parser
│   ├── DocumentProcessingPipeline.cs # Background pipeline orchestrator
│   └── Sm2SchedulerService.cs# SuperMemo SM-2 algorithm implementation
├── Web/                      # SmartNotesAI.Web
│   ├── Controllers/
│   │   ├── BaseApiController.cs    # Common JWT user extraction
│   │   ├── AuthController.cs       # /api/auth
│   │   ├── DocumentsController.cs  # /api/documents
│   │   ├── QuizzesController.cs    # /api/quizzes
│   │   ├── StudyPlanController.cs  # /api/study-plan
│   │   └── DashboardController.cs  # /api/dashboard
│   ├── Security/
│   │   └── JwtHelper.cs      # JWT token creation & validation
│   ├── App_Data/Uploads/     # Physical PDF storage directory
│   ├── css/style.css         # Custom Vanilla CSS design system
│   ├── js/app.js             # SPA Router, API client & quiz engine
│   ├── index.html            # Main application dashboard & views
│   ├── login.html            # Sign-in page
│   ├── register.html         # Sign-up page
│   └── Web.config            # Connection strings & AppSettings
├── run.ps1                   # One-click startup script (PowerShell)
├── run.bat                   # Windows batch launcher
└── SmartNotesAI.sln          # Visual Studio / MSBuild Solution
```

---

## 🗄 Database Model (EF6 on MS SQL Server)

The database schema implements all 10 core tables:

1. **Users**: `Id`, `Email`, `PasswordHash`, `DisplayName`, `CreatedAt`, `LastLoginAt`, `RefreshToken`, `RefreshTokenExpiryTime`
2. **Documents**: `Id`, `UserId`, `OriginalFilename`, `StoragePath`, `PageCount`, `Status`, `ErrorMessage`, `ExtractedTextPath`, `CreatedAt`, `ProcessedAt`
3. **DocumentSections**: `Id`, `DocumentId`, `OrderIndex`, `Heading`, `RawText`, `PageRange`
4. **Notes**: `Id`, `DocumentId`, `SectionId`, `Title`, `ContentMarkdown`, `OrderIndex`, `CreatedAt`
5. **Quizzes**: `Id`, `DocumentId`, `Title`, `CreatedAt`, `GenerationModel`
6. **QuizQuestions**: `Id`, `QuizId`, `SectionId`, `QuestionType`, `PromptText`, `OptionsJson`, `CorrectAnswer`, `Explanation`, `Difficulty`, `OrderIndex`
7. **QuizAttempts**: `Id`, `QuizId`, `UserId`, `StartedAt`, `CompletedAt`, `ScorePercent`, `Status`
8. **QuizAnswerEvents**: `Id`, `AttemptId`, `QuestionId`, `UserAnswer`, `IsCorrect`, `AnsweredAt`
9. **StudyPlanItems**: `Id`, `UserId`, `DocumentId`, `SectionId`, `EaseFactor`, `IntervalDays`, `Repetitions`, `DueAt`, `LastReviewedAt`, `LastGrade`
10. **ProgressSnapshots**: `Id`, `UserId`, `DocumentsProcessed`, `QuizzesCompleted`, `AverageScore`, `CurrentStreakDays`, `ComputedAt`

---

## 🚀 Getting Started on Windows

### Prerequisites
- **Windows 10/11** or **Windows Server**
- **.NET Framework 4.8 Developer Pack**
- **.NET SDK (6.0, 7.0, or 8.0)** (supports `dotnet build` on SDK-style projects)
- **IIS Express** (installed by default with Visual Studio, or available as standalone download)
- **SQL Server LocalDB** (`MSSQLLocalDB` - included with Visual Studio)

### 1. Configure Web.config
Open `Web\Web.config` and configure your settings:

```xml
<configuration>
  <connectionStrings>
    <!-- Default points to SQL Server LocalDB -->
    <add name="DefaultConnection" 
         connectionString="Data Source=(LocalDb)\MSSQLLocalDB;Initial Catalog=SmartNotesAI;Integrated Security=True" 
         providerName="System.Data.SqlClient" />
  </connectionStrings>

  <appSettings>
    <!-- Enter your free Google Gemini API Key -->
    <add key="GeminiApiKey" value="YOUR_GEMINI_API_KEY_HERE" />
    <add key="UploadPath" value="~/App_Data/Uploads" />
  </appSettings>
  ...
</configuration>
```

> **Note**: If `GeminiApiKey` is left blank or set to the placeholder, SmartNotes AI automatically activates its high-quality deterministic NLP engine to generate structured notes and quizzes without network latency or quota limits.

### 2. Launch the Application

#### Option A: One-Click Batch Script
Double click **`run.bat`** in the root folder.

#### Option B: PowerShell Script
Open PowerShell in the project directory and run:
```powershell
powershell -ExecutionPolicy Bypass -File .\run.ps1
```

The script will:
1. Verify and start SQL Server LocalDB (`MSSQLLocalDB`).
2. Terminate any stale IIS Express processes.
3. Compile `SmartNotesAI.sln` using `dotnet build`.
4. Launch IIS Express on **`http://localhost:5000`**.
5. Automatically open **`http://localhost:5000/login.html`** in your default web browser.

#### Option C: Visual Studio 2019/2022
1. Open `SmartNotesAI.sln` in Visual Studio.
2. Set `SmartNotesAI.Web` as the StartUp Project.
3. Press **F5** to build and run with IIS Express.

---

## 📡 RESTful API Reference

### Authentication (`/api/auth`)
| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/auth/register` | Register new user account (`{ Email, Password, DisplayName }`) |
| `POST` | `/api/auth/login` | Authenticate user & receive JWT access + refresh tokens |
| `POST` | `/api/auth/refresh` | Rotate access token using valid refresh token |
| `POST` | `/api/auth/logout` | Revoke current user's refresh token |

### Documents (`/api/documents`)
| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/documents` | Multipart PDF upload. Returns `202 Accepted` with Document DTO |
| `GET` | `/api/documents` | Retrieve list of uploaded documents for authenticated user |
| `GET` | `/api/documents/{id}` | Retrieve document details, sections, and processing status |
| `DELETE`| `/api/documents/{id}` | Permanently delete document, files, notes, quizzes, and study plan |
| `GET` | `/api/documents/{id}/notes` | Retrieve structured Markdown study notes for document |
| `POST`| `/api/documents/{id}/notes/regenerate` | Regenerate study notes via Gemini AI |
| `GET` | `/api/documents/{id}/quiz` | Retrieve generated quiz questions (answers stripped for student) |
| `GET` | `/api/documents/{id}/attempts` | Retrieve quiz attempt history and scores for document |

### Quizzes (`/api/quizzes`)
| Method | Endpoint | Description |
|---|---|---|
| `POST` | `/api/quizzes/{id}/attempts` | Initialize a new stateful quiz attempt |
| `POST` | `/api/quizzes/attempts/{id}/answers` | Submit answer for question. Short answers evaluated by Gemini |
| `POST` | `/api/quizzes/attempts/{id}/complete` | Finish attempt, calculate grade, and update SM-2 study plan |
| `POST` | `/api/quizzes/{id}/regenerate` | Regenerate questions for the specified quiz |

### Spaced Repetition Study Plan (`/api/study-plan`)
| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/study-plan?filter=due\|week\|all` | Retrieve user's study plan items filtered by due date |
| `GET` | `/api/study-plan/documents/{id}` | Retrieve study plan items for a specific document |
| `POST` | `/api/study-plan/items/{id}/review` | Manually review section item (`{ Grade: 0..5 }`) and recalculate SM-2 interval |

### Dashboard (`/api/dashboard`)
| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/dashboard` | Retrieve aggregated dashboard summary (stats, recent docs, due items, attempts) |
| `GET` | `/api/dashboard/stats` | Retrieve user performance metrics (docs processed, quizzes completed, avg score, streak) |

---

## 🧠 SuperMemo SM-2 Spaced Repetition Implementation

SmartNotes AI implements the standard SuperMemo SM-2 algorithm:

$$\Delta I = \begin{cases} 
1 & \text{if } \text{Repetitions} = 0 \\
6 & \text{if } \text{Repetitions} = 1 \\
\text{Round}(I \times EF) & \text{if } \text{Repetitions} \ge 2 
\end{cases}$$

$$EF' = EF + \left(0.1 - (5 - q) \times (0.08 + (5 - q) \times 0.02)\right)$$

Where:
- $q \in [0, 5]$ is the recall performance grade.
- $EF$ (Ease Factor) defaults to $2.5$ and is clamped to a minimum of $1.3$.
- If $q < 3$, repetitions reset to $0$ and the interval resets to $1$ day.

---

## 🧪 Automated End-to-End Verification

To run the automated verification script covering the entire pipeline (Auth ➔ Upload ➔ Background Processing ➔ Notes ➔ Quiz ➔ Semantic Grading ➔ SM-2 Scheduling ➔ Stats):

```powershell
powershell -ExecutionPolicy Bypass -File .\scratch\test_pipeline.ps1
```

All integration tests pass with 100% success.
