(() => {
    'use strict';

    const queryTabs = {
        tabs: [],
        activeTabId: null,
        get storageKey() {
            const workspace = document.getElementById('scdb-workspace');
            const scope = workspace?.dataset?.tabScope?.trim() || 'global';
            return `scdb.querytabs.${scope}`;
        }
    };

    const commandPaletteState = {
        commands: [],
        filtered: [],
        selectedIndex: 0,
        selectedTable: ''
    };

    const snippetState = {
        snippets: [],
        get storageKey() {
            return 'scdb.snippets';
        }
    };

    function switchResultTab(name) {
        const paneResults = document.getElementById('pane-results');
        const paneMessages = document.getElementById('pane-messages');
        if (name === 'results') {
            paneResults?.removeAttribute('hidden');
            paneMessages?.setAttribute('hidden', '');
        } else {
            paneResults?.setAttribute('hidden', '');
            paneMessages?.removeAttribute('hidden');
        }

        document.getElementById('tab-results')?.classList.toggle('active', name === 'results');
        document.getElementById('tab-messages')?.classList.toggle('active', name === 'messages');
    }

    function createTabTitle(sql, fallbackIndex) {
        const firstLine = (sql || '').split('\n').map(x => x.trim()).find(x => x.length > 0) ?? '';
        if (!firstLine) {
            return `Query ${fallbackIndex}`;
        }

        const normalized = firstLine.replace(/^--\s*/, '').trim();
        return normalized.length <= 28 ? normalized : `${normalized.substring(0, 28)}…`;
    }

    let idSequence = 0;

    function uniqueId(prefix) {
        idSequence += 1;
        return `${prefix}-${Date.now()}-${idSequence}`;
    }

    function createQueryTab(sql, title) {
        const id = uniqueId('tab');
        return {
            id,
            sql: sql || '',
            title: title || createTabTitle(sql, queryTabs.tabs.length + 1)
        };
    }

    function getEditor() {
        return document.getElementById('scdb-sql-editor');
    }

    function getActiveQueryTab() {
        return queryTabs.tabs.find(t => t.id === queryTabs.activeTabId) ?? null;
    }

    function syncEditorFromActiveTab() {
        const editor = getEditor();
        const active = getActiveQueryTab();
        if (!editor) {
            return;
        }

        editor.value = active?.sql ?? '';
    }

    function syncActiveTabFromEditor() {
        const editor = getEditor();
        const active = getActiveQueryTab();
        if (!editor || !active) {
            return;
        }

        active.sql = editor.value;
        active.title = createTabTitle(active.sql, queryTabs.tabs.indexOf(active) + 1);
        renderQueryTabs();
    }

    function renderQueryTabs() {
        const container = document.getElementById('scdb-query-tabs');
        if (!container) {
            return;
        }

        container.innerHTML = '';

        queryTabs.tabs.forEach((tab, index) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = `scdb-query-tab${tab.id === queryTabs.activeTabId ? ' active' : ''}`;
            button.setAttribute('role', 'tab');
            button.setAttribute('aria-selected', String(tab.id === queryTabs.activeTabId));
            button.title = tab.title || `Query ${index + 1}`;
            button.onclick = () => switchQueryTab(tab.id);
            button.ondblclick = () => renameQueryTab(tab.id);

            const title = document.createElement('span');
            title.className = 'scdb-query-tab__title';
            title.textContent = tab.title || `Query ${index + 1}`;
            button.appendChild(title);

            const close = document.createElement('span');
            close.className = 'scdb-query-tab__close';
            close.textContent = '✕';
            close.title = 'Close tab';
            close.onclick = event => {
                event.stopPropagation();
                closeQueryTab(tab.id);
            };
            button.appendChild(close);

            container.appendChild(button);
        });

        const addButton = document.createElement('button');
        addButton.type = 'button';
        addButton.className = 'scdb-query-tab scdb-query-tab--add';
        addButton.textContent = '+';
        addButton.title = 'New query tab (Ctrl+T)';
        addButton.onclick = () => addNewQueryTab();
        container.appendChild(addButton);
    }

    function persistQueryTabs() {
        try {
            localStorage.setItem(queryTabs.storageKey, JSON.stringify({
                activeTabId: queryTabs.activeTabId,
                tabs: queryTabs.tabs
            }));
        } catch {
        }
    }

    function switchQueryTab(tabId) {
        syncActiveTabFromEditor();
        queryTabs.activeTabId = tabId;
        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function addNewQueryTab(initialSql) {
        syncActiveTabFromEditor();
        const tab = createQueryTab(initialSql || '', null);
        queryTabs.tabs.push(tab);
        queryTabs.activeTabId = tab.id;
        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function closeQueryTab(tabId) {
        if (queryTabs.tabs.length === 1) {
            queryTabs.tabs[0].sql = '';
            queryTabs.tabs[0].title = 'Query 1';
            queryTabs.activeTabId = queryTabs.tabs[0].id;
            syncEditorFromActiveTab();
            renderQueryTabs();
            persistQueryTabs();
            return;
        }

        const index = queryTabs.tabs.findIndex(t => t.id === tabId);
        if (index < 0) {
            return;
        }

        queryTabs.tabs.splice(index, 1);
        if (queryTabs.activeTabId === tabId) {
            queryTabs.activeTabId = queryTabs.tabs[Math.max(0, index - 1)].id;
        }

        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function renameQueryTab(tabId) {
        const tab = queryTabs.tabs.find(t => t.id === tabId);
        if (!tab) {
            return;
        }

        const nextTitle = prompt('Tab name', tab.title);
        if (!nextTitle) {
            return;
        }

        tab.title = nextTitle.trim();
        renderQueryTabs();
        persistQueryTabs();
    }

    function loadQueryTabs() {
        const editor = getEditor();
        const initialSql = editor?.value ?? '';

        try {
            const raw = localStorage.getItem(queryTabs.storageKey);
            if (raw) {
                const parsed = JSON.parse(raw);
                if (Array.isArray(parsed?.tabs) && parsed.tabs.length > 0) {
                    queryTabs.tabs = parsed.tabs
                        .filter(t => t && typeof t.id === 'string' && typeof t.sql === 'string')
                        .map((t, i) => ({
                            id: t.id,
                            sql: t.sql,
                            title: typeof t.title === 'string' && t.title.trim().length > 0 ? t.title : createTabTitle(t.sql, i + 1)
                        }));

                    queryTabs.activeTabId = queryTabs.tabs.some(t => t.id === parsed.activeTabId)
                        ? parsed.activeTabId
                        : queryTabs.tabs[0].id;

                    syncEditorFromActiveTab();
                    renderQueryTabs();
                    return;
                }
            }
        } catch {
        }

        const initialTab = createQueryTab(initialSql, 'Query 1');
        queryTabs.tabs = [initialTab];
        queryTabs.activeTabId = initialTab.id;
        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function serializeQueryTabsForPost() {
        syncActiveTabFromEditor();

        const activeTabIdInput = document.getElementById('ActiveQueryTabId');
        const stateInput = document.getElementById('QueryTabsStateJson');

        if (activeTabIdInput) {
            activeTabIdInput.value = queryTabs.activeTabId || '';
        }

        if (stateInput) {
            stateInput.value = JSON.stringify({
                activeTabId: queryTabs.activeTabId,
                tabs: queryTabs.tabs
            });
        }

        persistQueryTabs();
    }

    function triggerExecuteShortcut() {
        const form = document.getElementById('form-execute-main');
        if (form && validateAndSubmit(form)) {
            form.submit();
        }
    }

    function getSelectedTableName() {
        const selected = document.querySelector('#scdb-table-list li.selected button[data-table]');
        return selected?.dataset.table?.trim() || '';
    }

    function setSelectedTableName(tableName) {
        commandPaletteState.selectedTable = tableName;
    }

    function selectTable(element, tableName) {
        document.querySelectorAll('#scdb-table-list li').forEach(li => li.classList.remove('selected'));
        element.closest('li')?.classList.add('selected');

        // Keep every hidden SelectedTable input in sync (preview, execute, script-table forms…).
        document.querySelectorAll('input[name="SelectedTable"]').forEach(hidden => {
            hidden.value = tableName;
        });

        setSelectedTableName(tableName);
    }

    function appendSqlToActiveTab(sql) {
        const active = getActiveQueryTab();
        if (!active) {
            addNewQueryTab(sql);
            return;
        }

        active.sql = sql;
        active.title = createTabTitle(active.sql, queryTabs.tabs.indexOf(active) + 1);
        syncEditorFromActiveTab();
        renderQueryTabs();
        persistQueryTabs();
    }

    function quoteIdentifier(input) {
        return `"${input.replaceAll('"', '""')}"`;
    }

    function newQueryFromSelection() {
        const table = getSelectedTableName();
        if (!table) {
            return;
        }

        addNewQueryTab(`SELECT * FROM ${quoteIdentifier(table)} LIMIT 100;`);
    }

    function selectTopFromSelection() {
        const table = getSelectedTableName();
        if (!table) {
            return;
        }

        appendSqlToActiveTab(`SELECT * FROM ${quoteIdentifier(table)} LIMIT 100;`);
    }

    function countRowsFromSelection() {
        const table = getSelectedTableName();
        if (!table) {
            return;
        }

        appendSqlToActiveTab(`SELECT COUNT(*) AS TotalRows FROM ${quoteIdentifier(table)};`);
    }

    function scriptTableFromSelection() {
        const table = getSelectedTableName();
        if (!table) {
            return;
        }

        const form = document.getElementById('form-script-table');
        const input = form ? form.querySelector('input[name="SelectedTable"]') : null;
        if (!form || !input) {
            return;
        }

        input.value = table;
        form.submit();
    }

    function showBusyOverlay(title, text) {
        const overlay = document.getElementById('scdb-busy-overlay');
        if (!overlay) {
            return;
        }

        const titleEl = document.getElementById('scdb-busy-title');
        const textEl = document.getElementById('scdb-busy-text');
        if (titleEl && title) {
            titleEl.textContent = title;
        }
        if (textEl && text) {
            textEl.textContent = text;
        }

        overlay.classList.remove('scdb-hidden');
        overlay.setAttribute('aria-hidden', 'false');
    }

    function hideBusyOverlay() {
        const overlay = document.getElementById('scdb-busy-overlay');
        if (!overlay) {
            return;
        }

        overlay.classList.add('scdb-hidden');
        overlay.setAttribute('aria-hidden', 'true');
    }

    function toggleGroup(header) {
        const list = header.nextElementSibling;
        const chevron = header.querySelector('.scdb-sidebar__chevron');
        const collapsed = list?.hasAttribute('hidden');
        if (list) {
            if (collapsed) {
                list.removeAttribute('hidden');
            } else {
                list.setAttribute('hidden', '');
            }
        }

        if (chevron) {
            chevron.textContent = collapsed ? '▾' : '▸';
        }

        header.setAttribute('aria-expanded', String(collapsed));
    }

    function toggleConnectionMode(mode) {
        const local = document.getElementById('scdb-local-fields');
        const server = document.getElementById('scdb-server-fields');
        if (!local || !server) {
            return;
        }

        if (mode === 'Local') {
            local.removeAttribute('hidden');
            server.setAttribute('hidden', '');
        } else {
            local.setAttribute('hidden', '');
            server.removeAttribute('hidden');
        }
    }

    function validateAndSubmit(form) {
        const editor = getEditor();
        const msgElement = document.getElementById('scdb-sql-validation-msg');

        serializeQueryTabsForPost();

        const sql = (editor?.value ?? '').trim();

        editor?.classList.remove('is-invalid');
        if (msgElement) {
            msgElement.textContent = '';
            msgElement.classList.add('scdb-hidden');
        }

        if (!sql) {
            editor?.classList.add('is-invalid');
            if (msgElement) {
                msgElement.textContent = 'SQL cannot be empty.';
                msgElement.classList.remove('scdb-hidden');
            }

            editor?.focus();
            return false;
        }

        const dangerPattern = /^\s*(DELETE\s+FROM|UPDATE\s+\S+\s+SET)\s/i;
        if (dangerPattern.test(sql) && !/\bWHERE\b/i.test(sql)) {
            if (!confirm('⚠ This statement has no WHERE clause and will affect all rows.\n\nContinue?')) {
                return false;
            }
        }

        return true;
    }

    function validateConnectionForm(form) {
        const modeElement = form.querySelector('input[name="Connection.ConnectionMode"]:checked');
        const mode = modeElement ? modeElement.value : 'Local';
        let isValid = true;

        if (mode === 'Local') {
            const path = form.querySelector('#Connection_LocalDatabasePath');
            if (path && !path.value.trim()) {
                path.classList.add('is-invalid');
                showFieldError(path, 'Database path is required.');
                isValid = false;
            }

            const password = form.querySelector('#Connection_Password');
            if (password && !password.value.trim()) {
                password.classList.add('is-invalid');
                showFieldError(password, 'Password is required.');
                isValid = false;
            }
        } else {
            [
                ['#Connection_ServerHost', 'Host is required.'],
                ['#Connection_ServerDatabase', 'Database is required.'],
                ['#Connection_ServerUsername', 'Username is required.']
            ].forEach(([selector, message]) => {
                const element = form.querySelector(selector);
                if (element && !element.value.trim()) {
                    element.classList.add('is-invalid');
                    showFieldError(element, message);
                    isValid = false;
                }
            });

            const port = form.querySelector('#Connection_ServerPort');
            if (port) {
                const value = Number.parseInt(port.value, 10);
                if (!value || value < 1 || value > 65535) {
                    port.classList.add('is-invalid');
                    showFieldError(port, 'Port must be 1–65535.');
                    isValid = false;
                }
            }
        }

        return isValid;
    }

    function showFieldError(element, message) {
        let span = element.nextElementSibling;
        if (!span?.classList.contains('scdb-validation-message')) {
            span = document.createElement('span');
            span.className = 'scdb-validation-message';
            element.after(span);
        }

        span.textContent = message;
    }

    function clearFieldError(element) {
        element.classList.remove('is-invalid');
        const span = element.nextElementSibling;
        if (span?.classList.contains('scdb-validation-message')) {
            span.textContent = '';
        }
    }

    function loadHistoryItem(element) {
        try {
            const sql = JSON.parse(element.dataset.sql || '""');
            if (sql) {
                appendSqlToActiveTab(sql);
            }
        } catch {
            // Ignore malformed/hostile payloads.
        }
    }

    function initHorizontalResizer(splitterId, leftElementId, rightElementId, leftStorageKey, rightStorageKey) {
        const splitter = document.getElementById(splitterId);
        const leftElement = document.getElementById(leftElementId);
        const rightElement = document.getElementById(rightElementId);
        if (!splitter || !leftElement || !rightElement) {
            return;
        }

        try {
            const leftWidth = localStorage.getItem(leftStorageKey);
            if (leftWidth) {
                const parsedLeftWidth = Number.parseInt(leftWidth, 10);
                leftElement.style.width = `${parsedLeftWidth}px`;
                leftElement.style.minWidth = `${parsedLeftWidth}px`;
            }

            const rightWidth = localStorage.getItem(rightStorageKey);
            if (rightWidth) {
                const parsedRightWidth = Number.parseInt(rightWidth, 10);
                rightElement.style.width = `${parsedRightWidth}px`;
                rightElement.style.minWidth = `${parsedRightWidth}px`;
            }
        } catch {
        }

        let startX = 0;
        let startLeftWidth = 0;
        let startRightWidth = 0;

        const onMouseMove = event => {
            const delta = event.clientX - startX;

            if (splitterId === 'scdb-splitter-left') {
                const newWidth = Math.min(460, Math.max(180, startLeftWidth + delta));
                leftElement.style.width = `${newWidth}px`;
                leftElement.style.minWidth = `${newWidth}px`;
                try {
                    localStorage.setItem(leftStorageKey, String(newWidth));
                } catch {
                }
            } else {
                const newWidth = Math.min(420, Math.max(220, startRightWidth - delta));
                rightElement.style.width = `${newWidth}px`;
                rightElement.style.minWidth = `${newWidth}px`;
                try {
                    localStorage.setItem(rightStorageKey, String(newWidth));
                } catch {
                }
            }
        };

        const onMouseUp = () => {
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
            document.body.classList.remove('scdb-resize-active');
        };

        const handleKeyResize = event => {
            const right = splitterId === 'scdb-splitter-left' ? leftElement : rightElement;
            if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
                return;
            }

            event.preventDefault();
            const currentWidth = Math.round(right.getBoundingClientRect().width);
            const delta = event.key === 'ArrowRight' ? 12 : -12;

            if (splitterId === 'scdb-splitter-left') {
                const newWidth = Math.min(460, Math.max(180, currentWidth + delta));
                leftElement.style.width = `${newWidth}px`;
                leftElement.style.minWidth = `${newWidth}px`;
                try {
                    localStorage.setItem(leftStorageKey, String(newWidth));
                } catch {
                }
            } else {
                const newWidth = Math.min(420, Math.max(220, currentWidth - delta));
                rightElement.style.width = `${newWidth}px`;
                rightElement.style.minWidth = `${newWidth}px`;
                try {
                    localStorage.setItem(rightStorageKey, String(newWidth));
                } catch {
                }
            }
        };

        splitter.addEventListener('mousedown', event => {
            startX = event.clientX;
            startLeftWidth = leftElement.getBoundingClientRect().width;
            startRightWidth = rightElement.getBoundingClientRect().width;
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
            document.body.classList.add('scdb-resize-active');
            event.preventDefault();
        });

        splitter.tabIndex = 0;
        splitter.addEventListener('keydown', handleKeyResize);
    }

    function collectResultsFromGrid() {
        const table = document.querySelector('#pane-results table.scdb-grid');
        if (!table) {
            return null;
        }

        const headers = [...table.querySelectorAll('thead th')]
            .map(th => th.textContent?.trim() ?? '')
            .filter((_, index) => index !== 0);

        const rows = [...table.querySelectorAll('tbody tr')].map(tr => {
            const cells = [...tr.querySelectorAll('td')].slice(1);
            return cells.map(td => {
                const nullSpan = td.querySelector('.scdb-grid__null');
                return nullSpan ? null : (td.textContent ?? '').trim();
            });
        });

        return { headers, rows };
    }

    function escapeCsvValue(value) {
        if (value === null || value === undefined) {
            return '';
        }

        const text = String(value);
        if (text.includes(',') || text.includes('"') || text.includes('\n') || text.includes('\r')) {
            return `"${text.replaceAll('"', '""')}"`;
        }

        return text;
    }

    function downloadText(content, fileName, mimeType) {
        const blob = new Blob([content], { type: mimeType });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
    }

    function exportResultsAsCsv() {
        const payload = collectResultsFromGrid();
        if (!payload || payload.rows.length === 0) {
            alert('No result rows available for export.');
            return;
        }

        const lines = [payload.headers.map(escapeCsvValue).join(',')];
        payload.rows.forEach(row => lines.push(row.map(escapeCsvValue).join(',')));
        downloadText(lines.join('\r\n'), 'sharpcoredb-results.csv', 'text/csv;charset=utf-8');
    }

    function exportResultsAsJson() {
        const payload = collectResultsFromGrid();
        if (!payload || payload.rows.length === 0) {
            alert('No result rows available for export.');
            return;
        }

        downloadText(JSON.stringify(payload.rows), 'sharpcoredb-results.json', 'application/json;charset=utf-8');
    }

    function renderSnippetList() {
        const container = document.getElementById('scdb-snippet-list');
        if (!container) {
            return;
        }

        container.innerHTML = '';

        snippetState.snippets.forEach((snippet, index) => {
            const item = document.createElement('div');
            item.className = 'scdb-snippet-item';
            item.draggable = true;
            item.dataset.index = index;
            item.title = snippet.sql;

            const text = document.createElement('span');
            text.className = 'scdb-snippet-text';
            text.textContent = snippet.sql;
            item.appendChild(text);

            const buttons = document.createElement('div');
            buttons.className = 'scdb-snippet-buttons';

            const copyButton = document.createElement('button');
            copyButton.type = 'button';
            copyButton.className = 'scdb-snippet-btn';
            copyButton.title = 'Copy SQL to editor';
            copyButton.onclick = () => appendSqlToActiveTab(snippet.sql);
            copyButton.innerHTML = '📋';

            const deleteButton = document.createElement('button');
            deleteButton.type = 'button';
            deleteButton.className = 'scdb-snippet-btn';
            deleteButton.title = 'Delete snippet';
            deleteButton.onclick = () => deleteSnippet(index);
            deleteButton.innerHTML = '🗑️';

            buttons.appendChild(copyButton);
            buttons.appendChild(deleteButton);
            item.appendChild(buttons);

            container.appendChild(item);
        });
    }

    function loadSnippets() {
        try {
            const raw = localStorage.getItem(snippetState.storageKey);
            if (raw) {
                const parsed = JSON.parse(raw);
                if (Array.isArray(parsed)) {
                    snippetState.snippets = parsed.filter(s => s && typeof s.id === 'string' && typeof s.name === 'string' && typeof s.sql === 'string');
                }
            }
        } catch {
        }
    }

    function persistSnippets() {
        try {
            localStorage.setItem(snippetState.storageKey, JSON.stringify(snippetState.snippets));
        } catch {
        }
    }

    function createSnippet(name, sql, category) {
        return {
            id: uniqueId('snippet'),
            name: name || 'Untitled Snippet',
            sql: sql || '',
            category: category || '',
            created: new Date().toISOString()
        };
    }

    function addSnippet(name, sql, category) {
        const snippet = createSnippet(name, sql, category);
        snippetState.snippets.push(snippet);
        persistSnippets();
        renderSnippetBrowser();
        return snippet;
    }

    function deleteSnippet(snippetId) {
        snippetState.snippets = snippetState.snippets.filter(s => s.id !== snippetId);
        persistSnippets();
        renderSnippetBrowser();
    }

    function createSampleDatabase(sampleName) {
        if (!sampleName) {
            return;
        }

        const nameInput = document.getElementById('ensure-sample-name');
        const form = document.getElementById('form-ensure-sample');
        if (nameInput) {
            nameInput.value = sampleName;
        }

        if (form) {
            showBusyOverlay('Creating sample database…', `Seeding tables and demo data for "${sampleName}". This can take a few seconds.`);
            form.submit();
        }
    }

    function openCreateDbDialog() {
        const dialog = document.getElementById('scdb-create-db-dialog');
        if (!dialog) return;

        const nameEl = document.getElementById('create-db-name');
        const pathEl = document.getElementById('create-db-path');
        const passEl = document.getElementById('create-db-password');
        const pathErr = document.getElementById('create-db-path-error');
        const passErr = document.getElementById('create-db-password-error');

        if (nameEl) nameEl.value = '';
        if (pathEl) pathEl.value = '';
        if (passEl) passEl.value = '';
        if (pathErr) pathErr.textContent = '';
        if (passErr) passErr.textContent = '';

        dialog.showModal();
        nameEl?.focus();
    }

    function closeCreateDbDialog() {
        const dialog = document.getElementById('scdb-create-db-dialog');
        if (!dialog) return;
        dialog.close();
    }

    function validateCreateDbForm() {
        let isValid = true;

        const path = (document.getElementById('create-db-path')?.value ?? '').trim();
        const pathError = document.getElementById('create-db-path-error');
        if (!path) {
            if (pathError) pathError.textContent = 'Database path is required.';
            document.getElementById('create-db-path')?.focus();
            isValid = false;
        } else if (pathError) {
            pathError.textContent = '';
        }

        const password = (document.getElementById('create-db-password')?.value ?? '').trim();
        const passwordError = document.getElementById('create-db-password-error');
        if (!password) {
            if (passwordError) passwordError.textContent = 'Password is required.';
            if (isValid) document.getElementById('create-db-password')?.focus();
            isValid = false;
        } else if (passwordError) {
            passwordError.textContent = '';
        }

        return isValid;
    }

    function openSnippetDialog() {
        const dialog = document.getElementById('scdb-snippet-dialog');
        if (!dialog) {
            return;
        }

        document.getElementById('snippet-name').value = '';
        document.getElementById('snippet-category').value = '';
        document.getElementById('snippet-sql').value = '';

        dialog.showModal();
        document.getElementById('snippet-name').focus();
    }

    function closeSnippetDialog() {
        const dialog = document.getElementById('scdb-snippet-dialog');
        if (!dialog) {
            return;
        }

        dialog.close();
    }

    function saveSnippet() {
        const name = (document.getElementById('snippet-name')?.value ?? '').trim();
        const category = (document.getElementById('snippet-category')?.value ?? '').trim();
        const sql = (document.getElementById('snippet-sql')?.value ?? '').trim();

        if (!name) {
            alert('Snippet name is required.');
            return;
        }

        if (!sql) {
            alert('SQL template is required.');
            return;
        }

        addSnippet(name, sql, category);
        closeSnippetDialog();
    }

    // ── Command palette ──────────────────────────────────────────────────────

    function buildCommandList() {
        return [
            { label: 'Execute Query', hint: 'F5', action: () => triggerExecuteShortcut() },
            { label: 'New Query Tab', hint: 'Ctrl+T', action: () => addNewQueryTab('') },
            { label: 'New Query From Selection', hint: '', action: () => newQueryFromSelection() },
            { label: 'Select Top 100', hint: '', action: () => selectTopFromSelection() },
            { label: 'Count Rows', hint: '', action: () => countRowsFromSelection() },
            { label: 'Export as CSV', hint: '', action: () => exportResultsAsCsv() },
            { label: 'Export as JSON', hint: '', action: () => exportResultsAsJson() },
            { label: 'Add Snippet', hint: '', action: () => openSnippetDialog() },
            { label: 'Create Contoso Sample Database', hint: '', action: () => createSampleDatabase('contoso') },
            { label: 'Create AdventureWorks Sample Database', hint: '', action: () => createSampleDatabase('adventureworks') },
            { label: 'Close Command Palette', hint: 'Esc', action: () => closeCommandPalette() },
        ];
    }

    function openCommandPalette() {
        const overlay = document.getElementById('scdb-command-palette');
        const input = document.getElementById('scdb-command-input');
        if (!overlay) return;
        commandPaletteState.commands = buildCommandList();
        commandPaletteState.filtered = [...commandPaletteState.commands];
        commandPaletteState.selectedIndex = 0;
        overlay.showModal();
        if (input) {
            input.value = '';
            input.focus();
        }
        renderCommandList();
    }

    function closeCommandPalette() {
        const overlay = document.getElementById('scdb-command-palette');
        if (!overlay) return;
        overlay.close();
    }

    function renderCommandList() {
        const list = document.getElementById('scdb-command-list');
        if (!list) return;
        list.innerHTML = '';
        commandPaletteState.filtered.forEach((cmd, i) => {
            const li = document.createElement('li');
            li.className = 'scdb-command-palette__item' + (i === commandPaletteState.selectedIndex ? ' scdb-command-palette__item--selected' : '');
            const labelHtml = `<span class="scdb-command-palette__label">${escapeHtml(cmd.label)}</span>`;
            const hintHtml = cmd.hint ? `<span class="scdb-command-palette__hint">${escapeHtml(cmd.hint)}</span>` : '';
            li.innerHTML = labelHtml + hintHtml;
            li.addEventListener('click', () => { cmd.action(); closeCommandPalette(); });
            list.appendChild(li);
        });
    }

    function escapeHtml(str) {
        return String(str).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
    }

    function initializeCommandPalette() {
        const input = document.getElementById('scdb-command-input');
        const overlay = document.getElementById('scdb-command-palette');
        if (!input || !overlay) return;

        input.addEventListener('input', () => {
            const q = input.value.trim().toLowerCase();
            commandPaletteState.filtered = q
                ? commandPaletteState.commands.filter(c => c.label.toLowerCase().includes(q))
                : [...commandPaletteState.commands];
            commandPaletteState.selectedIndex = 0;
            renderCommandList();
        });

        input.addEventListener('keydown', e => {
            const len = commandPaletteState.filtered.length;
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                commandPaletteState.selectedIndex = (commandPaletteState.selectedIndex + 1) % len;
                renderCommandList();
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                commandPaletteState.selectedIndex = (commandPaletteState.selectedIndex - 1 + len) % len;
                renderCommandList();
            } else if (e.key === 'Enter') {
                e.preventDefault();
                const cmd = commandPaletteState.filtered[commandPaletteState.selectedIndex];
                if (cmd) { cmd.action(); closeCommandPalette(); }
            }
        });

        // Close on backdrop click (native <dialog> routes backdrop clicks to the element)
        overlay.addEventListener('click', e => {
            if (e.target === overlay) closeCommandPalette();
        });

        // Escape is handled natively by <dialog>; route it through our close routine.
        overlay.addEventListener('cancel', e => {
            e.preventDefault();
            closeCommandPalette();
        });
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    function showContextMenu(x, y, tableName) {
        const menu = document.getElementById('scdb-explorer-contextmenu');
        if (!menu) return;
        setSelectedTableName(tableName);
        menu.style.left = `${x}px`;
        menu.style.top = `${y}px`;
        menu.classList.remove('scdb-hidden');
        menu.querySelector('.scdb-contextmenu__item')?.focus();
    }

    function hideContextMenu() {
        const menu = document.getElementById('scdb-explorer-contextmenu');
        if (!menu) return;
        menu.classList.add('scdb-hidden');
    }

    function contextMenuAction(action) {
        hideContextMenu();
        switch (action) {
            case 'new': newQueryFromSelection(); break;
            case 'top': selectTopFromSelection(); break;
            case 'count': countRowsFromSelection(); break;
            case 'ddl': scriptTableFromSelection(); break;
        }
    }

    function initializeContextMenu() {
        const tableList = document.getElementById('scdb-table-list');
        if (tableList) {
            tableList.addEventListener('contextmenu', e => {
                const btn = e.target.closest('button[data-table]');
                if (!btn) return;
                e.preventDefault();
                setSelectedTableName(btn.dataset.table);
                showContextMenu(e.clientX, e.clientY, btn.dataset.table);
            });
        }

        // Close on any outside click or Escape
        document.addEventListener('click', e => {
            const menu = document.getElementById('scdb-explorer-contextmenu');
            if (menu && !menu.contains(e.target)) hideContextMenu();
        });

        document.addEventListener('keydown', e => {
            if (e.key === 'Escape') hideContextMenu();
        });
    }

    // ── Snippet browser (in saved-panel) ─────────────────────────────────────

    function renderSnippetBrowser() {
        renderSnippetList();
    }

    // ── Keyboard shortcuts ───────────────────────────────────────────────────

    function wireKeyboardShortcuts() {
        document.addEventListener('keydown', e => {
            // Ctrl+Shift+P — command palette
            if (e.ctrlKey && e.shiftKey && e.key === 'P') {
                e.preventDefault();
                openCommandPalette();
                return;
            }
            // F5 or Ctrl+Enter — execute query
            if ((e.key === 'F5' || (e.ctrlKey && e.key === 'Enter')) && !e.shiftKey) {
                const editor = document.getElementById('scdb-sql-editor');
                if (editor && document.activeElement === editor) {
                    e.preventDefault();
                    triggerExecuteShortcut();
                }
                return;
            }
            // Ctrl+T — new query tab
            if (e.ctrlKey && e.key === 't') {
                e.preventDefault();
                addNewQueryTab('');
            }
        });
    }

    // ── Central event wiring (replaces all inline handlers) ─────────────────

    function wireEvents() {
        // Delegate all data-action clicks from document root
        document.addEventListener('click', e => {
            const btn = e.target.closest('[data-action]');
            if (!btn) return;

            const action = btn.dataset.action;

            switch (action) {
                case 'toggle-group':
                    toggleGroup(btn);
                    break;
                case 'select-table': {
                    const table = btn.dataset.table;
                    if (table) selectTable(btn, table);
                    break;
                }
                case 'create-sample-db': {
                    const sample = btn.dataset.sample;
                    if (sample) createSampleDatabase(sample);
                    break;
                }
                case 'new-query-selection':
                    newQueryFromSelection();
                    break;
                case 'select-top-selection':
                    selectTopFromSelection();
                    break;
                case 'count-rows-selection':
                    countRowsFromSelection();
                    break;
                case 'open-create-db':
                    openCreateDbDialog();
                    break;
                case 'close-create-db':
                    closeCreateDbDialog();
                    break;
                case 'execute-query':
                    triggerExecuteShortcut();
                    break;
                case 'open-command-palette':
                    openCommandPalette();
                    break;
                case 'switch-result-tab':
                    switchResultTab(btn.dataset.tab);
                    break;
                case 'export-csv':
                    exportResultsAsCsv();
                    break;
                case 'export-json':
                    exportResultsAsJson();
                    break;
                case 'open-snippet-dialog':
                    openSnippetDialog();
                    break;
                case 'close-snippet-dialog':
                    closeSnippetDialog();
                    break;
                case 'save-snippet':
                    saveSnippet();
                    break;
                case 'load-history':
                    loadHistoryItem(btn);
                    break;
                case 'ctx-new':
                    contextMenuAction('new');
                    break;
                case 'ctx-top':
                    contextMenuAction('top');
                    break;
                case 'ctx-count':
                    contextMenuAction('count');
                    break;
                case 'ctx-ddl':
                    contextMenuAction('ddl');
                    break;
                case 'dismiss-alert':
                    btn.closest('.scdb-alert')?.remove();
                    break;
                case 'close-about': {
                    const dialog = document.getElementById('scdb-about-dialog');
                    if (dialog && typeof dialog.close === 'function') {
                        dialog.close();
                    }
                    break;
                }
                case 'close-workspace': {
                    const dialog = document.getElementById('scdb-workspace-dialog');
                    if (dialog && typeof dialog.close === 'function') {
                        dialog.close();
                    }
                    break;
                }
                case 'cycle-theme':
                    cycleTheme();
                    break;
            }
        });

        // Sample database items: click + keyboard
        document.querySelectorAll('[data-sample]').forEach(el => {
            el.addEventListener('click', e => {
                const target = e.target.closest('[data-sample]');
                if (target) createSampleDatabase(target.dataset.sample);
            });
            el.addEventListener('keydown', e => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    createSampleDatabase(el.dataset.sample);
                }
            });
        });

        // Table items are native <button> elements, so Enter/Space activate them
        // via the delegated data-action click handler (no manual keydown needed).

        // Prevent browser context menu on table list (JS delegation handles it)
        document.getElementById('scdb-table-list')?.addEventListener('contextmenu', e => {
            e.preventDefault();
        });

        // Connection mode radio buttons
        document.getElementById('radio-mode-local')?.addEventListener('change', e => {
            if (e.target.checked) toggleConnectionMode('Local');
        });
        document.getElementById('radio-mode-server')?.addEventListener('change', e => {
            if (e.target.checked) toggleConnectionMode('Server');
        });

        // Clear field errors on input (delegated)
        const connectForm = document.getElementById('form-connect');
        connectForm?.querySelectorAll('.scdb-input').forEach(input => {
            input.addEventListener('input', () => clearFieldError(input));
        });

        // Form submit handlers
        connectForm?.addEventListener('submit', e => {
            if (!validateConnectionForm(connectForm)) e.preventDefault();
        });

        document.getElementById('form-execute-main')?.addEventListener('submit', e => {
            if (!validateAndSubmit(document.getElementById('form-execute-main'))) e.preventDefault();
        });

        document.getElementById('form-create-db')?.addEventListener('submit', e => {
            if (!validateCreateDbForm()) e.preventDefault();
        });
    }

    // ── Theme ─────────────────────────────────────────────────────────────────
    const THEME_STORAGE_KEY = 'scdb-theme';
    const THEMES = ['dark', 'light', 'system'];
    const THEME_META = {
        dark:   { icon: '🌙', label: 'Dark' },
        light:  { icon: '☀️', label: 'Light' },
        system: { icon: '🖥️', label: 'System' }
    };

    function applyTheme(theme) {
        document.documentElement.dataset.theme = theme;
        localStorage.setItem(THEME_STORAGE_KEY, theme);
        const meta = THEME_META[theme];
        const icon = document.getElementById('scdb-theme-icon');
        const label = document.getElementById('scdb-theme-label');
        if (icon) icon.textContent = meta.icon;
        if (label) label.textContent = meta.label;
    }

    function cycleTheme() {
        const current = document.documentElement.dataset.theme || 'dark';
        const next = THEMES[(THEMES.indexOf(current) + 1) % THEMES.length];
        applyTheme(next);
    }

    function initialize() {
        // Restore saved theme (blocking script already set data-theme before paint)
        applyTheme(localStorage.getItem(THEME_STORAGE_KEY) || 'dark');
        loadQueryTabs();
        loadSnippets();
        wireEvents();
        initializeCommandPalette();
        initializeContextMenu();
        renderSnippetBrowser();
        wireKeyboardShortcuts();
        initHorizontalResizer('scdb-splitter-left', 'scdb-object-explorer', 'scdb-workspace', 'scdb.sidebar.width', 'scdb.workspace.width');

        const createDbDialog = document.getElementById('scdb-create-db-dialog');
        if (createDbDialog) {
            createDbDialog.addEventListener('cancel', e => { e.preventDefault(); closeCreateDbDialog(); });
            createDbDialog.addEventListener('click', e => { if (e.target === createDbDialog) closeCreateDbDialog(); });
        }

        const createDbForm = document.getElementById('form-create-db');
        if (createDbForm) {
            createDbForm.addEventListener('submit', e => {
                if (!validateCreateDbForm()) {
                    e.preventDefault();
                    return;
                }

                showBusyOverlay('Creating database…', 'The new database is being created and opened. This can take a few seconds.');
            });
        }

        const ensureSampleForm = document.getElementById('form-ensure-sample');
        if (ensureSampleForm) {
            ensureSampleForm.addEventListener('submit', () => {
                showBusyOverlay('Creating sample database…', 'Seeding tables and demo data. This can take a few seconds.');
            });
        }

        const dialog = document.getElementById('scdb-snippet-dialog');
        if (dialog) {
            dialog.addEventListener('cancel', event => {
                event.preventDefault();
                closeSnippetDialog();
            });

            dialog.addEventListener('click', event => {
                if (event.target === dialog) {
                    closeSnippetDialog();
                }
            });
        }

        const workspaceDialog = document.getElementById('scdb-workspace-dialog');
        if (workspaceDialog) {
            workspaceDialog.addEventListener('cancel', event => {
                event.preventDefault();
                workspaceDialog.close();
            });
            workspaceDialog.addEventListener('click', event => {
                if (event.target === workspaceDialog) {
                    workspaceDialog.close();
                }
            });
            // After "File → Export Workspace" the server returns the JSON payload in the
            // textarea; open the dialog automatically so the user can copy or edit it.
            if (workspaceDialog.dataset.openOnLoad === 'true') {
                workspaceDialog.showModal();
            }
        }

        setTimeout(() => {
            document.getElementById('scdb-alert-status')?.remove();
        }, 5000);
    }

    window.switchResultTab = switchResultTab;
    window.selectTable = selectTable;
    window.toggleGroup = toggleGroup;
    window.toggleConnectionMode = toggleConnectionMode;
    window.validateAndSubmit = validateAndSubmit;
    window.validateConnectionForm = validateConnectionForm;
    window.showFieldError = showFieldError;
    window.clearFieldError = clearFieldError;
    window.loadHistoryItem = loadHistoryItem;
    window.triggerExecuteShortcut = triggerExecuteShortcut;
    window.addNewQueryTab = addNewQueryTab;
    window.newQueryFromSelection = newQueryFromSelection;
    window.selectTopFromSelection = selectTopFromSelection;
    window.countRowsFromSelection = countRowsFromSelection;
    window.exportResultsAsCsv = exportResultsAsCsv;
    window.exportResultsAsJson = exportResultsAsJson;
    window.openCommandPalette = openCommandPalette;
    window.closeCommandPalette = closeCommandPalette;
    window.openCreateDbDialog = openCreateDbDialog;
    window.closeCreateDbDialog = closeCreateDbDialog;
    window.createSampleDatabase = createSampleDatabase;
    window.validateCreateDbForm = validateCreateDbForm;
    window.scriptTableFromSelection = scriptTableFromSelection;
    window.showBusyOverlay = showBusyOverlay;
    window.hideBusyOverlay = hideBusyOverlay;
    window.contextMenuAction = contextMenuAction;
    window.openSnippetDialog = openSnippetDialog;
    window.closeSnippetDialog = closeSnippetDialog;
    window.saveSnippet = saveSnippet;
    window.initialize = initialize;

    // "Execute Selected": put the selection in the editor and run it.
    window.SharpCoreDBExecuteSelection = (selected) => {
        const editor = getEditor();
        if (!editor || !selected) {
            return;
        }
        editor.value = selected;
        triggerExecuteShortcut();
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }
})();
