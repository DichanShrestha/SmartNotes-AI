// SmartNotes AI - Core SPA Application Engine
const API_BASE = '/api';

// --- State Store ---
const AppState = {
    token: localStorage.getItem('token') || null,
    refreshToken: localStorage.getItem('refreshToken') || null,
    user: JSON.parse(localStorage.getItem('user') || 'null'),
    currentView: 'dashboard',
    activeDocId: null,
    activeDocData: null,
    activeQuiz: null,
    activeAttempt: null,
    currentQuestionIndex: 0,
    pollingInterval: null
};

// --- Helper Functions ---
function getAuthHeaders(isJson = true) {
    const headers = {};
    if (AppState.token) {
        headers['Authorization'] = `Bearer ${AppState.token}`;
    }
    if (isJson) {
        headers['Content-Type'] = 'application/json';
    }
    return headers;
}

async function apiFetch(endpoint, options = {}) {
    // We attach the authorization header if we have a token stored in our AppState.
    if (!options.headers) {
        options.headers = getAuthHeaders(options.body && !(options.body instanceof FormData));
    }

    try {
        let response = await fetch(`${API_BASE}${endpoint}`, options);

        // Oops! Looks like the token expired (401). Let's try to refresh it silently.
        // If we can get a new one, we retry the original request.
        if (response.status === 401 && AppState.refreshToken && !endpoint.includes('/auth/')) {
            const refreshed = await attemptTokenRefresh();
            if (refreshed) {
                options.headers['Authorization'] = `Bearer ${AppState.token}`;
                response = await fetch(`${API_BASE}${endpoint}`, options);
            } else {
                // If refresh fails, boot the user out so they can log back in.
                logout();
                return null;
            }
        }

        return response;
    } catch (err) {
        console.error(`API Error on ${endpoint}:`, err);
        showToast('Network error or server unreachable.', 'error');
        throw err;
    }
}

async function attemptTokenRefresh() {
    try {
        const res = await fetch(`${API_BASE}/auth/refresh`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token: AppState.token, refreshToken: AppState.refreshToken })
        });
        if (res.ok) {
            const data = await res.json();
            setAuthData(data.Token, data.RefreshToken, data.User);
            return true;
        }
    } catch { }
    return false;
}

function setAuthData(token, refreshToken, user) {
    AppState.token = token;
    AppState.refreshToken = refreshToken;
    AppState.user = user;
    localStorage.setItem('token', token);
    if (refreshToken) localStorage.setItem('refreshToken', refreshToken);
    if (user) localStorage.setItem('user', JSON.stringify(user));
}

function logout() {
    if (AppState.token) {
        apiFetch('/auth/logout', { method: 'POST' }).catch(() => {});
    }
    localStorage.removeItem('token');
    localStorage.removeItem('refreshToken');
    localStorage.removeItem('user');
    AppState.token = null;
    AppState.refreshToken = null;
    AppState.user = null;
    window.location.href = 'login.html';
}

function showToast(message, type = 'info') {
    const container = document.getElementById('toastContainer');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;
    const icon = type === 'success' ? '✅' : type === 'error' ? '❌' : 'ℹ️';
    toast.innerHTML = `<span>${icon}</span><span style="flex:1;">${escapeHtml(message)}</span>`;
    container.appendChild(toast);

    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transform = 'translateX(100%)';
        toast.style.transition = 'all 0.3s ease';
        setTimeout(() => toast.remove(), 300);
    }, 4000);
}

function escapeHtml(str) {
    if (!str) return '';
    return String(str).replace(/[&<>"']/g, m => ({
        '&': '&amp;',
        '<': '&lt;',
        '>': '&gt;',
        '"': '&quot;',
        "'": '&#39;'
    }[m]));
}

// Robust Markdown to HTML parser
function renderMarkdown(md) {
    if (!md) return '';
    let escaped = escapeHtml(md);

    // Code blocks ```code```
    escaped = escaped.replace(/```([\s\S]*?)```/g, '<pre><code>$1</code></pre>');

    // Inline code `code`
    escaped = escaped.replace(/`([^`]+)`/g, '<code>$1</code>');

    // Bold **text**
    escaped = escaped.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');

    // Italic *text*
    escaped = escaped.replace(/\*(.*?)\*/g, '<em>$1</em>');

    const lines = escaped.split(/\r?\n/);
    const out = [];
    let inList = false;
    let inQuote = false;
    let quoteLines = [];

    function flushList() {
        if (inList) {
            out.push('</ul>');
            inList = false;
        }
    }

    function flushQuote() {
        if (inQuote) {
            out.push(`<blockquote>${quoteLines.join('<br/>')}</blockquote>`);
            inQuote = false;
            quoteLines = [];
        }
    }

    for (let i = 0; i < lines.length; i++) {
        let line = lines[i].trim();

        if (!line) {
            flushList();
            flushQuote();
            continue;
        }

        // Blockquotes
        if (line.startsWith('&gt;')) {
            flushList();
            inQuote = true;
            quoteLines.push(line.replace(/^&gt;\s?/, ''));
            continue;
        } else {
            flushQuote();
        }

        // Headings
        if (line.startsWith('### ')) {
            flushList();
            out.push(`<h3>${line.substring(4)}</h3>`);
            continue;
        } else if (line.startsWith('## ')) {
            flushList();
            out.push(`<h2>${line.substring(3)}</h2>`);
            continue;
        } else if (line.startsWith('# ')) {
            flushList();
            out.push(`<h1>${line.substring(2)}</h1>`);
            continue;
        }

        // List items
        if (/^[-*•]\s+/.test(line)) {
            if (!inList) {
                out.push('<ul>');
                inList = true;
            }
            out.push(`<li>${line.replace(/^[-*•]\s+/, '')}</li>`);
            continue;
        } else {
            flushList();
        }

        // Paragraphs
        out.push(`<p>${line}</p>`);
    }

    flushList();
    flushQuote();

    return out.join('\n');
}

// --- Navigation Controller ---
// This handles our SPA routing. It swaps out the active view and manages sidebar highlights.
function navigateTo(viewName, params = {}) {
    AppState.currentView = viewName;

    // First, hide all views
    document.querySelectorAll('.view-container').forEach(el => el.classList.remove('active'));

    // Update sidebar highlights
    document.querySelectorAll('.sidebar-link').forEach(el => el.classList.remove('active'));
    const sidebarLink = document.getElementById(`nav-${viewName}`);
    if (sidebarLink) sidebarLink.classList.add('active');

    // Update page title in the header
    const pageTitle = document.getElementById('pageTitle');

    if (viewName === 'dashboard') {
        const view = document.getElementById('dashboardView');
        if (view) view.classList.add('active');
        if (pageTitle) pageTitle.textContent = 'Dashboard';
        loadDashboard();
    } else if (viewName === 'documentDetail') {
        const view = document.getElementById('documentDetailView');
        if (view) view.classList.add('active');
        if (pageTitle) pageTitle.textContent = 'Document Details';
        if (params.docId) {
            loadDocumentDetail(params.docId, params.tab || 'notes');
        }
    } else if (viewName === 'quizTaking') {
        const view = document.getElementById('quizTakingView');
        if (view) view.classList.add('active');
        if (pageTitle) pageTitle.textContent = 'Quiz Attempt';
    } else if (viewName === 'studyPlan') {
        const view = document.getElementById('studyPlanView');
        if (view) view.classList.add('active');
        if (pageTitle) pageTitle.textContent = 'Study Plan';
        loadStudyPlanFilter('due');
    }
    
    // Smooth scroll back to top for a fresh feel
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

// --- Initialization ---
document.addEventListener('DOMContentLoaded', () => {
    const isAuthPage = window.location.pathname.includes('login.html') || window.location.pathname.includes('register.html');

    if (!AppState.token && !isAuthPage) {
        window.location.href = 'login.html';
        return;
    } else if (AppState.token && isAuthPage) {
        window.location.href = 'index.html';
        return;
    }

    // Initialize Navbar Info
    if (AppState.user) {
        const nameEl = document.getElementById('navUsername');
        const avatarEl = document.getElementById('navAvatar');
        if (nameEl) nameEl.textContent = AppState.user.DisplayName || AppState.user.Email;
        if (avatarEl) {
            const initial = (AppState.user.DisplayName || AppState.user.Email || 'U')[0].toUpperCase();
            avatarEl.textContent = initial;
        }
    }

    // Attach Logout Button
    const logoutBtn = document.getElementById('logoutBtn');
    if (logoutBtn) {
        logoutBtn.addEventListener('click', logout);
    }

    // 1. Auth Page: Login
    const loginForm = document.getElementById('loginForm');
    if (loginForm) {
        loginForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const email = document.getElementById('email').value.trim();
            const password = document.getElementById('password').value;
            const submitBtn = document.getElementById('loginSubmitBtn');

            submitBtn.disabled = true;
            submitBtn.textContent = 'Signing In...';

            try {
                const res = await fetch(`${API_BASE}/auth/login`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ email, password })
                });

                if (res.ok) {
                    const data = await res.json();
                    setAuthData(data.Token, data.RefreshToken, data.User);
                    showToast('Login successful! Redirecting...', 'success');
                    setTimeout(() => window.location.href = 'index.html', 500);
                } else {
                    showToast('Invalid credentials. Please verify your email and password.', 'error');
                }
            } catch {
                showToast('Login connection failed.', 'error');
            } finally {
                submitBtn.disabled = false;
                submitBtn.textContent = 'Sign In';
            }
        });
    }

    // 2. Auth Page: Register
    const registerForm = document.getElementById('registerForm');
    if (registerForm) {
        registerForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const displayName = document.getElementById('displayName').value.trim();
            const email = document.getElementById('email').value.trim();
            const password = document.getElementById('password').value;
            const submitBtn = document.getElementById('registerSubmitBtn');

            submitBtn.disabled = true;
            submitBtn.textContent = 'Creating Account...';

            try {
                const res = await fetch(`${API_BASE}/auth/register`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ displayName, email, password })
                });

                if (res.ok) {
                    const data = await res.json();
                    setAuthData(data.Token, data.RefreshToken, data.User);
                    showToast('Account registered successfully! Redirecting...', 'success');
                    setTimeout(() => window.location.href = 'index.html', 500);
                } else {
                    const err = await res.text();
                    showToast(err || 'Registration failed.', 'error');
                }
            } catch {
                showToast('Registration failed.', 'error');
            } finally {
                submitBtn.disabled = false;
                submitBtn.textContent = 'Create Free Account';
            }
        });
    }

    // 3. Setup Dashboard Upload & Dropzone
    setupUpload();

    // 4. Start Dashboard if on index.html
    if (document.getElementById('dashboardView')) {
        loadDashboard();
    }
});

// --- Upload Logic & Dropzone ---
function setupUpload() {
    const dropzone = document.getElementById('uploadDropzone');
    const fileInput = document.getElementById('pdfUpload');
    const selectBtn = document.getElementById('selectFileBtn');

    if (!dropzone || !fileInput) return;

    if (selectBtn) {
        selectBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            fileInput.click();
        });
    }

    dropzone.addEventListener('click', () => fileInput.click());

    ['dragenter', 'dragover'].forEach(eventName => {
        dropzone.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.add('dragover');
        });
    });

    ['dragleave', 'drop'].forEach(eventName => {
        dropzone.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.remove('dragover');
        });
    });

    dropzone.addEventListener('drop', (e) => {
        if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
            handleFileUpload(e.dataTransfer.files[0]);
        }
    });

    fileInput.addEventListener('change', () => {
        if (fileInput.files && fileInput.files.length > 0) {
            handleFileUpload(fileInput.files[0]);
        }
    });
}

async function handleFileUpload(file) {
    if (!file) return;

    if (!file.name.toLowerCase().endsWith('.pdf') && file.type !== 'application/pdf') {
        showToast('Please select a valid PDF document.', 'error');
        return;
    }

    if (file.size > 50 * 1024 * 1024) {
        showToast('File size exceeds the 50MB limit.', 'error');
        return;
    }

    const statusBox = document.getElementById('uploadStatusBox');
    const statusText = document.getElementById('uploadStatusText');
    if (statusBox) statusBox.style.display = 'flex';
    if (statusText) statusText.textContent = `Uploading "${file.name}" (${(file.size / 1024 / 1024).toFixed(1)} MB)...`;

    const formData = new FormData();
    formData.append('file', file);

    try {
        const res = await apiFetch('/documents', {
            method: 'POST',
            body: formData
        });

        if (res && (res.status === 202 || res.ok)) {
            const data = await res.json();
            showToast(`"${file.name}" uploaded! Background AI pipeline initiated.`, 'success');
            if (statusBox) statusBox.style.display = 'none';
            document.getElementById('pdfUpload').value = '';
            await loadDashboard();
            startPollingStatus();
        } else {
            const err = await res.text();
            showToast(`Upload failed: ${err}`, 'error');
            if (statusBox) statusBox.style.display = 'none';
        }
    } catch {
        showToast('Error uploading file. Please try again.', 'error');
        if (statusBox) statusBox.style.display = 'none';
    }
}

// --- Dashboard Loader ---
// Pulls in the latest stats, recent docs, and items due for review from the backend.
async function loadDashboard() {
    try {
        const res = await apiFetch('/dashboard');
        if (!res || !res.ok) return;

        const data = await res.json();

        // 1. Render Stats
        if (data.Stats) {
            const statDocs = document.getElementById('statDocs');
            const statQuizzes = document.getElementById('statQuizzes');
            const statScore = document.getElementById('statScore');
            const statStreak = document.getElementById('statStreak');
            const navStreak = document.getElementById('navStreak');

            // Fallback to 0 if we don't have the data yet
            if (statDocs) statDocs.textContent = data.Stats.DocumentsProcessed ?? 0;
            if (statQuizzes) statQuizzes.textContent = data.Stats.QuizzesCompleted ?? 0;
            if (statScore) statScore.textContent = `${data.Stats.AverageScore ?? 0}%`;
            if (statStreak) statStreak.textContent = `${data.Stats.CurrentStreakDays ?? 0} Days`;
            if (navStreak) navStreak.textContent = `${data.Stats.CurrentStreakDays ?? 0} Days`;
        }

        // 2. Render Documents
        renderDocumentsList(data.RecentDocuments || []);

        // 3. Render Due for Review
        renderDueItems(data.DueItems || []);

        // 4. Render Recent Attempts
        renderRecentAttempts(data.RecentAttempts || []);

        // 5. Check if any document is processing, trigger polling
        const isProcessing = (data.RecentDocuments || []).some(d => 
            d.Status === 'Uploaded' || d.Status === 'Extracting' || d.Status === 'Generating'
        );
        if (isProcessing) {
            startPollingStatus();
        }
    } catch (err) {
        console.error('Error loading dashboard:', err);
    }
}

// --- Live Polling for Document Status ---
function startPollingStatus() {
    if (AppState.pollingInterval) return;

    AppState.pollingInterval = setInterval(async () => {
        try {
            const res = await apiFetch('/documents');
            if (!res || !res.ok) return;

            const docs = await res.json();
            renderDocumentsList(docs);

            const stillProcessing = docs.some(d => 
                d.Status === 'Uploaded' || d.Status === 'Extracting' || d.Status === 'Generating'
            );

            if (!stillProcessing) {
                clearInterval(AppState.pollingInterval);
                AppState.pollingInterval = null;
                showToast('All documents processed and ready for study!', 'success');
                // Refresh dashboard stats & due items
                loadDashboard();
            }
        } catch {
            clearInterval(AppState.pollingInterval);
            AppState.pollingInterval = null;
        }
    }, 2500);
}

function renderDocumentsList(docs) {
    const grid = document.getElementById('documentsGrid');
    const badge = document.getElementById('docCountBadge');
    if (!grid) return;

    if (badge) badge.textContent = `${docs.length} Total`;

    if (docs.length === 0) {
        grid.innerHTML = `
            <div style="grid-column: 1/-1; text-align: center; padding: 3rem 1rem; color: var(--text-muted);">
                <div style="font-size: 2.5rem; margin-bottom: 0.5rem;">📚</div>
                <div style="font-weight: 600; font-size: 1.1rem; color: var(--text-primary);">No documents yet</div>
                <p>Upload a course syllabus, academic paper, or textbook PDF above to begin.</p>
            </div>
        `;
        return;
    }

    grid.innerHTML = docs.map(doc => {
        const dateStr = new Date(doc.CreatedAt).toLocaleDateString(undefined, {
            month: 'short',
            day: 'numeric'
        });

        let statusClass = 'status-uploaded';
        let statusText = doc.Status;
        if (doc.Status === 'Ready') statusClass = 'status-ready';
        else if (doc.Status === 'Extracting') { statusClass = 'status-extracting'; statusText = 'Extracting Text...'; }
        else if (doc.Status === 'Generating') { statusClass = 'status-generating'; statusText = 'AI Generating...'; }
        else if (doc.Status === 'Failed') statusClass = 'status-failed';

        const isReady = doc.Status === 'Ready';

        return `
            <div class="doc-card" id="doc-card-${doc.Id}">
                <div>
                    <div class="doc-card-top">
                        <div class="doc-file-icon">📄</div>
                        <div class="doc-card-content">
                            <div class="doc-card-title" title="${escapeHtml(doc.OriginalFilename)}">
                                ${escapeHtml(doc.OriginalFilename)}
                            </div>
                            <div class="doc-card-meta">
                                <span>${doc.PageCount > 0 ? `${doc.PageCount} Pages` : 'Processing'}</span>
                                <span>•</span>
                                <span>${dateStr}</span>
                            </div>
                        </div>
                    </div>
                    <div style="margin-bottom: 1rem;">
                        <span class="status-badge ${statusClass}">
                            <span class="status-dot"></span>
                            ${statusText}
                        </span>
                        ${doc.ErrorMessage ? `<div style="color: var(--accent-rose); font-size: 0.8rem; margin-top: 0.35rem;">${escapeHtml(doc.ErrorMessage)}</div>` : ''}
                    </div>
                </div>
                <div class="doc-card-bottom">
                    <div class="doc-actions-group">
                        <button class="btn btn-primary btn-sm" ${!isReady ? 'disabled' : ''} onclick="openDocumentDetail(${doc.Id}, 'notes')">
                            Notes
                        </button>
                        <button class="btn btn-secondary btn-sm" ${!isReady ? 'disabled' : ''} onclick="openDocumentDetail(${doc.Id}, 'quiz')">
                            Quiz
                        </button>
                        <button class="btn btn-secondary btn-sm" ${!isReady ? 'disabled' : ''} onclick="openDocumentDetail(${doc.Id}, 'plan')">
                            Plan
                        </button>
                    </div>
                    <button class="btn btn-danger btn-sm" onclick="deleteDocument(${doc.Id}, '${escapeHtml(doc.OriginalFilename)}')">
                        ✕
                    </button>
                </div>
            </div>
        `;
    }).join('');
}

function renderDueItems(items) {
    const grid = document.getElementById('dueGrid');
    const badge = document.getElementById('dueCountBadge');
    if (!grid) return;

    if (badge) badge.textContent = `${items.length} Due`;

    if (items.length === 0) {
        grid.innerHTML = `
            <div style="grid-column: 1/-1; text-align: center; padding: 2rem 1rem; color: var(--text-muted); background: rgba(15,23,42,0.4); border-radius: var(--radius-md);">
                🎉 All caught up! No items currently due for review under the SM-2 algorithm.
            </div>
        `;
        return;
    }

    grid.innerHTML = items.map(item => `
        <div class="due-card" id="due-item-${item.Id}">
            <div>
                <div class="due-doc-title">${escapeHtml(item.DocumentTitle)}</div>
                <div class="due-heading">${escapeHtml(item.SectionHeading)}</div>
                <div class="due-meta">
                    <span>${item.PageRange ? escapeHtml(item.PageRange) : 'Section'}</span>
                    <span class="due-badge-urgent">Due Now</span>
                </div>
            </div>
            <div>
                <div style="font-size: 0.75rem; color: var(--text-muted); margin-bottom: 0.25rem;">Rate Recall (SM-2):</div>
                <div class="grade-buttons-group">
                    <button class="grade-btn grade-0" title="Blackout" onclick="reviewPlanItem(${item.Id}, 0)">0</button>
                    <button class="grade-btn grade-1" title="Wrong" onclick="reviewPlanItem(${item.Id}, 1)">1</button>
                    <button class="grade-btn grade-2" title="Hard" onclick="reviewPlanItem(${item.Id}, 2)">2</button>
                    <button class="grade-btn grade-3" title="Pass" onclick="reviewPlanItem(${item.Id}, 3)">3</button>
                    <button class="grade-btn grade-4" title="Good" onclick="reviewPlanItem(${item.Id}, 4)">4</button>
                    <button class="grade-btn grade-5" title="Easy" onclick="reviewPlanItem(${item.Id}, 5)">5</button>
                </div>
            </div>
        </div>
    `).join('');
}

function renderRecentAttempts(attempts) {
    const container = document.getElementById('recentAttemptsList');
    if (!container) return;

    if (attempts.length === 0) {
        container.innerHTML = `
            <div style="text-align: center; padding: 1.5rem; color: var(--text-muted);">
                No quizzes attempted yet. Open a document and take a quiz to track your progress!
            </div>
        `;
        return;
    }

    container.innerHTML = attempts.map(a => {
        const dateStr = new Date(a.StartedAt).toLocaleDateString(undefined, {
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });

        const isGood = a.ScorePercent >= 70;
        const scoreColor = isGood ? 'var(--accent-emerald)' : 'var(--accent-amber)';

        return `
            <div class="doc-item">
                <div class="doc-info">
                    <div class="doc-title">🎯 ${escapeHtml(a.DocumentTitle)}</div>
                    <div class="doc-meta">
                        <span>${dateStr}</span>
                        <span>•</span>
                        <span>${a.CorrectCount} of ${a.TotalQuestions} questions correct</span>
                    </div>
                </div>
                <div style="font-size: 1.25rem; font-weight: 800; font-family: 'Outfit', sans-serif; color: ${scoreColor};">
                    ${a.ScorePercent}%
                </div>
            </div>
        `;
    }).join('');
}

// --- Review SM-2 Item ---
async function reviewPlanItem(itemId, grade) {
    try {
        const res = await apiFetch(`/study-plan/items/${itemId}/review`, {
            method: 'POST',
            body: JSON.stringify({ Grade: grade })
        });

        if (res && res.ok) {
            const data = await res.json();
            showToast(`Item updated! Next review in ${data.IntervalDays} day(s). (Ease Factor: ${data.EaseFactor})`, 'success');
            // Remove item from due view
            const el = document.getElementById(`due-item-${itemId}`);
            if (el) el.remove();
            // Refresh dashboard
            loadDashboard();
        }
    } catch {
        showToast('Failed to review study plan item.', 'error');
    }
}

// --- Delete Document ---
async function deleteDocument(docId, filename) {
    if (!confirm(`Are you sure you want to delete "${filename}" and all its notes, quizzes, and study plan data?`)) {
        return;
    }

    try {
        const res = await apiFetch(`/documents/${docId}`, { method: 'DELETE' });
        if (res && res.ok) {
            showToast(`"${filename}" deleted successfully.`, 'success');
            loadDashboard();
        }
    } catch {
        showToast('Error deleting document.', 'error');
    }
}

// --- Document Detail Management ---
function openDocumentDetail(docId, initialTab = 'notes') {
    AppState.activeDocId = docId;
    navigateTo('documentDetail', { docId, tab: initialTab });
}

async function loadDocumentDetail(docId, activeTab = 'notes') {
    try {
        const res = await apiFetch(`/documents/${docId}`);
        if (!res || !res.ok) {
            showToast('Document not found.', 'error');
            navigateTo('dashboard');
            return;
        }

        const doc = await res.json();
        AppState.activeDocData = doc;

        document.getElementById('detailDocTitle').textContent = doc.OriginalFilename;
        document.getElementById('detailPageCount').textContent = `${doc.PageCount} Page${doc.PageCount === 1 ? '' : 's'}`;
        document.getElementById('detailSectionsCount').textContent = `${doc.SectionsCount} Sections`;
        document.getElementById('detailCreatedAt').textContent = `Uploaded on ${new Date(doc.CreatedAt).toLocaleDateString()}`;

        const statusBadge = document.getElementById('detailStatusBadge');
        if (statusBadge) {
            statusBadge.innerHTML = `<span class="status-badge status-ready"><span class="status-dot"></span>${doc.Status}</span>`;
        }

        switchDetailTab(activeTab);
    } catch (err) {
        console.error('Error loading document detail:', err);
    }
}

function switchDetailTab(tabName) {
    ['Notes', 'Quiz', 'Plan'].forEach(t => {
        const btn = document.getElementById(`tabBtn${t}`);
        const pane = document.getElementById(`pane${t}`);
        if (btn) btn.classList.remove('active');
        if (pane) pane.classList.remove('active');
    });

    const targetBtn = document.getElementById(`tabBtn${tabName.charAt(0).toUpperCase() + tabName.slice(1)}`);
    const targetPane = document.getElementById(`pane${tabName.charAt(0).toUpperCase() + tabName.slice(1)}`);
    if (targetBtn) targetBtn.classList.add('active');
    if (targetPane) targetPane.classList.add('active');

    if (tabName === 'notes') loadNotes(AppState.activeDocId);
    else if (tabName === 'quiz') loadQuizDetail(AppState.activeDocId);
    else if (tabName === 'plan') loadDocStudyPlan(AppState.activeDocId);
}

// 1. Notes Tab
async function loadNotes(docId) {
    const container = document.getElementById('notesContainer');
    if (!container) return;
    container.innerHTML = '<div style="text-align: center; padding: 2rem; color: var(--text-muted);">Loading study notes...</div>';

    try {
        const res = await apiFetch(`/documents/${docId}/notes`);
        if (!res || !res.ok) return;

        const notes = await res.json();
        if (notes.length === 0) {
            container.innerHTML = '<div style="text-align: center; padding: 2rem;">No study notes generated yet.</div>';
            return;
        }

        container.innerHTML = notes.map(n => `
            <div class="note-item">
                <div class="note-item-header">
                    <h3 class="note-item-title">${escapeHtml(n.Title)}</h3>
                    ${n.SectionHeading ? `<span class="page-badge">${escapeHtml(n.SectionHeading)}</span>` : ''}
                </div>
                <div class="markdown-body">
                    ${renderMarkdown(n.ContentMarkdown)}
                </div>
            </div>
        `).join('');
    } catch {
        container.innerHTML = '<div style="text-align: center; padding: 2rem; color: var(--accent-rose);">Failed to load notes.</div>';
    }
}

async function regenerateNotes() {
    if (!AppState.activeDocId) return;
    const btn = document.getElementById('regenNotesBtn');
    btn.disabled = true;
    btn.textContent = 'Generating Notes with Gemini...';

    try {
        const res = await apiFetch(`/documents/${AppState.activeDocId}/notes/regenerate`, { method: 'POST' });
        if (res && res.ok) {
            showToast('Notes regenerated successfully!', 'success');
            await loadNotes(AppState.activeDocId);
        } else {
            showToast('Failed to regenerate notes.', 'error');
        }
    } catch {
        showToast('Error regenerating notes.', 'error');
    } finally {
        btn.disabled = false;
        btn.textContent = '✨ Regenerate Notes (AI)';
    }
}

// 2. Quiz Tab
async function loadQuizDetail(docId) {
    try {
        const res = await apiFetch(`/documents/${docId}/quiz`);
        if (!res || !res.ok) {
            document.getElementById('quizPaneSubtitle').textContent = 'Quiz is being generated or not available yet.';
            return;
        }

        const quiz = await res.json();
        AppState.activeQuiz = quiz;

        document.getElementById('quizPaneTitle').textContent = quiz.Title;
        document.getElementById('quizPaneSubtitle').textContent = 
            `Contains ${quiz.Questions.length} questions tagged by section and difficulty. Short answers are evaluated semantically by Gemini.`;

        // Load attempts
        const attemptsRes = await apiFetch(`/documents/${docId}/attempts`);
        const attemptsList = document.getElementById('docQuizAttemptsList');
        if (attemptsRes && attemptsRes.ok && attemptsList) {
            const attempts = await attemptsRes.json();
            if (attempts.length === 0) {
                attemptsList.innerHTML = '<div style="color: var(--text-muted); font-size: 0.9rem;">No previous attempts yet. Take the quiz to test your recall!</div>';
            } else {
                attemptsList.innerHTML = attempts.map(a => `
                    <div class="doc-item" style="margin-bottom: 0.5rem;">
                        <div class="doc-info">
                            <div class="doc-title">${new Date(a.StartedAt).toLocaleString()}</div>
                            <div class="doc-meta">
                                <span>${a.CorrectCount} / ${a.TotalQuestions} Correct</span>
                                <span>•</span>
                                <span>${a.Status}</span>
                            </div>
                        </div>
                        <div style="font-weight: 800; font-family: 'Outfit', sans-serif; font-size: 1.15rem; color: ${a.ScorePercent >= 70 ? 'var(--accent-emerald)' : 'var(--accent-amber)'};">
                            ${a.ScorePercent}%
                        </div>
                    </div>
                `).join('');
            }
        }
    } catch (err) {
        console.error('Error loading quiz detail:', err);
    }
}

async function regenerateQuiz() {
    if (!AppState.activeQuiz) return;
    if (!confirm('Regenerate all quiz questions with Gemini? This will replace current questions.')) return;

    try {
        showToast('Regenerating quiz questions with AI...', 'info');
        const res = await apiFetch(`/quizzes/${AppState.activeQuiz.Id}/regenerate`, { method: 'POST' });
        if (res && res.ok) {
            showToast('Quiz regenerated successfully!', 'success');
            await loadQuizDetail(AppState.activeDocId);
        }
    } catch {
        showToast('Failed to regenerate quiz.', 'error');
    }
}

// 3. Document Study Plan Tab
async function loadDocStudyPlan(docId) {
    const grid = document.getElementById('docStudyPlanGrid');
    if (!grid) return;
    grid.innerHTML = '<div style="text-align: center; padding: 2rem; color: var(--text-muted);">Loading schedule...</div>';

    try {
        const res = await apiFetch(`/study-plan/documents/${docId}`);
        if (!res || !res.ok) return;

        const items = await res.json();
        if (items.length === 0) {
            grid.innerHTML = '<div style="text-align: center; padding: 2rem;">No study plan items created yet.</div>';
            return;
        }

        grid.innerHTML = items.map(item => `
            <div class="due-card">
                <div>
                    <div class="due-doc-title">${escapeHtml(item.PageRange || 'Section')}</div>
                    <div class="due-heading">${escapeHtml(item.SectionHeading)}</div>
                    <div class="due-meta">
                        <span>Interval: <strong>${item.IntervalDays}d</strong></span>
                        <span>Ease Factor: <strong>${item.EaseFactor}</strong></span>
                        <span>Reps: <strong>${item.Repetitions}</strong></span>
                    </div>
                    <div style="font-size: 0.8rem; color: ${item.IsDue ? 'var(--accent-rose)' : 'var(--text-muted)'}; margin-bottom: 0.75rem;">
                        Due: ${new Date(item.DueAt).toLocaleDateString()} ${item.IsDue ? '(DUE NOW)' : ''}
                    </div>
                </div>
                <div>
                    <div style="font-size: 0.75rem; color: var(--text-muted); margin-bottom: 0.25rem;">Manual Recall Rating:</div>
                    <div class="grade-buttons-group">
                        <button class="grade-btn grade-0" title="Blackout" onclick="reviewDocPlanItem(${item.Id}, 0)">0</button>
                        <button class="grade-btn grade-1" title="Wrong" onclick="reviewDocPlanItem(${item.Id}, 1)">1</button>
                        <button class="grade-btn grade-2" title="Hard" onclick="reviewDocPlanItem(${item.Id}, 2)">2</button>
                        <button class="grade-btn grade-3" title="Pass" onclick="reviewDocPlanItem(${item.Id}, 3)">3</button>
                        <button class="grade-btn grade-4" title="Good" onclick="reviewDocPlanItem(${item.Id}, 4)">4</button>
                        <button class="grade-btn grade-5" title="Easy" onclick="reviewDocPlanItem(${item.Id}, 5)">5</button>
                    </div>
                </div>
            </div>
        `).join('');
    } catch {
        grid.innerHTML = '<div style="text-align: center; padding: 2rem; color: var(--accent-rose);">Failed to load study plan.</div>';
    }
}

async function reviewDocPlanItem(itemId, grade) {
    await reviewPlanItem(itemId, grade);
    if (AppState.activeDocId) loadDocStudyPlan(AppState.activeDocId);
}

// --- Interactive Quiz Taking Engine ---
async function startQuizFromDetail() {
    if (!AppState.activeQuiz) return;

    try {
        const res = await apiFetch(`/quizzes/${AppState.activeQuiz.Id}/attempts`, { method: 'POST' });
        if (!res || !res.ok) {
            showToast('Could not start quiz attempt.', 'error');
            return;
        }

        const data = await res.json();
        AppState.activeAttempt = data;
        AppState.currentQuestionIndex = 0;

        // Open Quiz Taking View
        navigateTo('quizTaking');

        // Reset quiz cards
        document.getElementById('questionCard').style.display = 'block';
        document.getElementById('quizCompletionCard').style.display = 'none';

        renderCurrentQuestion();
    } catch {
        showToast('Error starting quiz attempt.', 'error');
    }
}

function renderCurrentQuestion() {
    const questions = AppState.activeQuiz?.Questions || [];
    if (AppState.currentQuestionIndex >= questions.length) {
        finishQuiz();
        return;
    }

    const q = questions[AppState.currentQuestionIndex];
    const total = questions.length;

    // Counter & progress bar
    document.getElementById('quizCounterText').textContent = `Question ${AppState.currentQuestionIndex + 1} of ${total}`;
    const progressPercent = ((AppState.currentQuestionIndex) / total) * 100;
    document.getElementById('quizProgressBar').style.width = `${progressPercent}%`;

    // Meta badges
    const diffBadge = document.getElementById('qDiffBadge');
    diffBadge.textContent = q.Difficulty || 'MEDIUM';
    diffBadge.className = `diff-badge diff-${(q.Difficulty || 'medium').toLowerCase()}`;

    document.getElementById('qSectionBadge').textContent = q.SectionHeading || 'Section';
    document.getElementById('qTypeBadge').textContent = q.QuestionType === 'ShortAnswer' ? 'Short Answer' : 'Multiple Choice';

    // Prompt
    document.getElementById('qPromptText').textContent = q.PromptText;

    // Reset feedback card
    const feedbackCard = document.getElementById('answerFeedbackCard');
    feedbackCard.style.display = 'none';
    feedbackCard.className = 'feedback-card';

    // Buttons
    const submitBtn = document.getElementById('submitAnswerBtn');
    const nextBtn = document.getElementById('nextQuestionBtn');
    submitBtn.style.display = 'inline-flex';
    submitBtn.disabled = false;
    submitBtn.textContent = 'Submit Answer';
    nextBtn.style.display = 'none';

    // Options vs Short Answer Container
    const optionsContainer = document.getElementById('qOptionsContainer');
    const shortAnswerContainer = document.getElementById('qShortAnswerContainer');
    const shortAnswerInput = document.getElementById('shortAnswerInput');

    if (q.QuestionType === 'ShortAnswer') {
        optionsContainer.style.display = 'none';
        shortAnswerContainer.style.display = 'block';
        shortAnswerInput.value = '';
        shortAnswerInput.disabled = false;
        shortAnswerInput.focus();
    } else {
        shortAnswerContainer.style.display = 'none';
        optionsContainer.style.display = 'flex';
        optionsContainer.innerHTML = '';

        const letters = ['A', 'B', 'C', 'D'];
        (q.Options || []).forEach((opt, idx) => {
            const optDiv = document.createElement('div');
            optDiv.className = 'option-item';
            optDiv.innerHTML = `
                <div class="option-letter">${letters[idx] || (idx + 1)}</div>
                <div style="flex:1;">${escapeHtml(opt)}</div>
            `;
            optDiv.addEventListener('click', () => {
                if (submitBtn.disabled && nextBtn.style.display !== 'none') return; // already answered
                document.querySelectorAll('.option-item').forEach(el => el.classList.remove('selected'));
                optDiv.classList.add('selected');
            });
            optionsContainer.appendChild(optDiv);
        });
    }
}

async function submitCurrentAnswer() {
    const questions = AppState.activeQuiz?.Questions || [];
    const q = questions[AppState.currentQuestionIndex];
    if (!q || !AppState.activeAttempt) return;

    let userAnswer = '';
    if (q.QuestionType === 'ShortAnswer') {
        userAnswer = document.getElementById('shortAnswerInput').value.trim();
        if (!userAnswer) {
            showToast('Please type an answer before submitting.', 'info');
            return;
        }
    } else {
        const selected = document.querySelector('.option-item.selected');
        if (!selected) {
            showToast('Please select an option before submitting.', 'info');
            return;
        }
        // Extract text
        userAnswer = selected.innerText.replace(/^[A-D]\s*/, '').trim();
    }

    const submitBtn = document.getElementById('submitAnswerBtn');
    submitBtn.disabled = true;
    submitBtn.textContent = 'Grading...';

    try {
        const res = await apiFetch(`/quizzes/attempts/${AppState.activeAttempt.AttemptId}/answers`, {
            method: 'POST',
            body: JSON.stringify({ QuestionId: q.Id, UserAnswer: userAnswer })
        });

        if (res && res.ok) {
            const result = await res.json();

            // Display Feedback Card
            const feedbackCard = document.getElementById('answerFeedbackCard');
            const feedbackTitle = document.getElementById('feedbackTitle');
            const feedbackExplanation = document.getElementById('feedbackExplanation');

            feedbackCard.style.display = 'block';
            feedbackCard.className = `feedback-card ${result.IsCorrect ? 'correct' : 'incorrect'}`;

            feedbackTitle.innerHTML = result.IsCorrect
                ? '<span>🎉</span> Correct!'
                : '<span>❌</span> Needs Review';

            feedbackExplanation.innerHTML = `
                ${result.Feedback ? `<p style="margin-bottom: 0.5rem;"><strong>Feedback:</strong> ${escapeHtml(result.Feedback)}</p>` : ''}
                ${!result.IsCorrect && result.CorrectAnswer ? `<p style="margin-bottom: 0.5rem;"><strong>Correct Answer:</strong> ${escapeHtml(result.CorrectAnswer)}</p>` : ''}
                ${result.Explanation ? `<p><strong>Explanation:</strong> ${escapeHtml(result.Explanation)}</p>` : ''}
            `;

            // Disable options
            document.querySelectorAll('.option-item').forEach(el => el.classList.add('disabled'));
            const shortInput = document.getElementById('shortAnswerInput');
            if (shortInput) shortInput.disabled = true;

            submitBtn.style.display = 'none';
            const nextBtn = document.getElementById('nextQuestionBtn');
            nextBtn.style.display = 'inline-flex';
            nextBtn.textContent = (AppState.currentQuestionIndex + 1 === questions.length) ? 'Complete Quiz 🏆' : 'Next Question →';
        } else {
            showToast('Failed to record answer.', 'error');
            submitBtn.disabled = false;
            submitBtn.textContent = 'Submit Answer';
        }
    } catch {
        showToast('Error grading answer.', 'error');
        submitBtn.disabled = false;
        submitBtn.textContent = 'Submit Answer';
    }
}

function nextQuestion() {
    AppState.currentQuestionIndex++;
    const questions = AppState.activeQuiz?.Questions || [];
    if (AppState.currentQuestionIndex >= questions.length) {
        finishQuiz();
    } else {
        renderCurrentQuestion();
    }
}

async function finishQuiz() {
    if (!AppState.activeAttempt) return;

    try {
        const res = await apiFetch(`/quizzes/attempts/${AppState.activeAttempt.AttemptId}/complete`, { method: 'POST' });
        if (res && res.ok) {
            const result = await res.json();

            document.getElementById('questionCard').style.display = 'none';
            const completionCard = document.getElementById('quizCompletionCard');
            completionCard.style.display = 'block';

            document.getElementById('quizProgressBar').style.width = '100%';
            document.getElementById('finalScoreDisplay').textContent = `${result.ScorePercent}%`;
            document.getElementById('finalScoreSummary').textContent = 
                `You answered ${result.CorrectCount} out of ${result.TotalQuestions} questions correctly. (Performance Grade: ${result.Grade}/5)`;

            showToast('Quiz attempt recorded! SM-2 study plan updated.', 'success');
        }
    } catch {
        showToast('Error completing quiz.', 'error');
    }
}

function returnFromQuiz() {
    if (AppState.activeDocId) {
        openDocumentDetail(AppState.activeDocId, 'notes');
    } else {
        navigateTo('dashboard');
    }
}

function exitQuiz() {
    if (confirm('Are you sure you want to exit? Your progress so far is saved.')) {
        returnFromQuiz();
    }
}

// --- Dedicated Full Study Plan View ---
async function loadStudyPlanFilter(filter) {
    ['Due', 'Week', 'All'].forEach(f => {
        const btn = document.getElementById(`filter${f}Btn`);
        if (btn) btn.classList.remove('active');
    });

    const activeBtn = document.getElementById(`filter${filter.charAt(0).toUpperCase() + filter.slice(1)}Btn`);
    if (activeBtn) activeBtn.classList.add('active');

    const grid = document.getElementById('allStudyPlanGrid');
    if (!grid) return;
    grid.innerHTML = '<div style="text-align: center; padding: 2rem; color: var(--text-muted);">Loading study schedule...</div>';

    try {
        const res = await apiFetch(`/study-plan?filter=${filter}`);
        if (!res || !res.ok) return;

        const items = await res.json();
        if (items.length === 0) {
            grid.innerHTML = `
                <div style="grid-column: 1/-1; text-align: center; padding: 3rem 1rem; color: var(--text-muted);">
                    <div style="font-size: 2rem; margin-bottom: 0.5rem;">🎉</div>
                    <div style="font-weight: 700; color: var(--text-primary);">No items matching this filter</div>
                    <p>All items in this category have been scheduled for future intervals.</p>
                </div>
            `;
            return;
        }

        grid.innerHTML = items.map(item => `
            <div class="due-card">
                <div>
                    <div class="due-doc-title">${escapeHtml(item.DocumentTitle)}</div>
                    <div class="due-heading">${escapeHtml(item.SectionHeading)}</div>
                    <div class="due-meta">
                        <span>${item.PageRange ? escapeHtml(item.PageRange) : 'Section'}</span>
                        <span style="color: ${item.IsDue ? 'var(--accent-rose)' : 'var(--accent-emerald)'}; font-weight: 700;">
                            ${item.IsDue ? 'Due Now' : `In ${item.IntervalDays}d`}
                        </span>
                    </div>
                    <div style="font-size: 0.8rem; color: var(--text-muted); margin-bottom: 0.75rem;">
                        Ease Factor: <strong>${item.EaseFactor}</strong> • Repetitions: <strong>${item.Repetitions}</strong>
                    </div>
                </div>
                <div>
                    <div style="font-size: 0.75rem; color: var(--text-muted); margin-bottom: 0.25rem;">Rate Recall (SM-2):</div>
                    <div class="grade-buttons-group">
                        <button class="grade-btn grade-0" title="Blackout" onclick="reviewFullPlanItem(${item.Id}, 0, '${filter}')">0</button>
                        <button class="grade-btn grade-1" title="Wrong" onclick="reviewFullPlanItem(${item.Id}, 1, '${filter}')">1</button>
                        <button class="grade-btn grade-2" title="Hard" onclick="reviewFullPlanItem(${item.Id}, 2, '${filter}')">2</button>
                        <button class="grade-btn grade-3" title="Pass" onclick="reviewFullPlanItem(${item.Id}, 3, '${filter}')">3</button>
                        <button class="grade-btn grade-4" title="Good" onclick="reviewFullPlanItem(${item.Id}, 4, '${filter}')">4</button>
                        <button class="grade-btn grade-5" title="Easy" onclick="reviewFullPlanItem(${item.Id}, 5, '${filter}')">5</button>
                    </div>
                </div>
            </div>
        `).join('');
    } catch {
        grid.innerHTML = '<div style="text-align: center; padding: 2rem; color: var(--accent-rose);">Failed to load schedule.</div>';
    }
}

async function reviewFullPlanItem(itemId, grade, filter) {
    await reviewPlanItem(itemId, grade);
    loadStudyPlanFilter(filter);
}
