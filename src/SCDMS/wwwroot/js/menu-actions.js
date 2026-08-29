(() => {
    'use strict';

    const MENU_EVENT = 'scdb:menu-action';

    // Map menu action to existing data-action or DOM selector click
    const actionMap = {
        // File
        'file.new-query': () => {
            if (typeof window.addNewQueryTab === 'function') {
                window.addNewQueryTab('');
                return;
            }
            clickDataAction('add-new-query-tab');
        },
        'file.new-database': () => clickDataAction('open-create-db'),
        'file.new-contoso-sample': () => createSampleDatabase('contoso'),
        'file.new-adventureworks-sample': () => createSampleDatabase('adventureworks'),
        'file.open': () => {
            // SSMS layout: the connection form lives in the left Object Explorer sidebar.
            revealSidebarGroup('scdb-group-connect');
        },
        'file.disconnect': () => submitFormByHandler('Disconnect'),
        'file.export-workspace': () => exportWorkspace(),
        'file.import-workspace': () => importWorkspace(),
        'file.save-query': () => saveActiveQuery(),

        // Edit
        'edit.undo': () => {
            const ed = document.getElementById('scdb-sql-editor');
            if (ed) {
                ed.focus();
                // No non-deprecated API exists for programmatic textarea undo.
                document.execCommand('undo'); // NOSONAR(S1874)
            }
        },
        'edit.redo': () => {
            const ed = document.getElementById('scdb-sql-editor');
            if (ed) {
                ed.focus();
                // No non-deprecated API exists for programmatic textarea redo.
                document.execCommand('redo'); // NOSONAR(S1874)
            }
        },
        'edit.select-all': () => {
            const ed = document.getElementById('scdb-sql-editor');
            if (ed) { ed.focus(); ed.select(); }
        },

        // View
        'view.object-explorer': () => toggleVisibility('#scdb-object-explorer'),
        'view.saved-queries': () => revealSidebarGroup('scdb-group-saved'),
        'view.query-history': () => revealSidebarGroup('scdb-group-history'),
        'view.cycle-theme': () => clickDataAction('cycle-theme'),
        'view.command-palette': () => clickSelector('#scdb-open-command-palette'),

        // Query
        'query.execute': () => clickSelector('#btn-execute, #btn-execute-inner'),
        'query.execute-selection': (ctx) => executeSelection(ctx),
        'query.preview-selected': () => submitFormByHandler('PreviewTable'),
        'query.count-selected': () => clickDataAction('count-rows-selection'),
        'query.export-csv': () => clickDataAction('export-csv'),
        'query.export-json': () => clickDataAction('export-json'),

        // Tools
        'tools.create-table': () => showCreateTableDialog(),
        'tools.import-csv': () => showImportCsvDialog(),
        'tools.settings': () => showSettingsHint(),

        // Help
        'help.sql-reference': () => openDocs('https://github.com/MPCoreDeveloper/SharpCoreDB'),
        'help.user-manual': () => openDocs('https://github.com/MPCoreDeveloper/SCDMS/blob/main/docs/usage.md'),
        'help.about': () => showAbout()
    };

    function createSampleDatabase(sampleName) {
        if (typeof window.createSampleDatabase === 'function') {
            window.createSampleDatabase(sampleName);
            return;
        }

        // Fallback: submit the hidden EnsureSample form directly.
        const nameInput = document.getElementById('ensure-sample-name');
        const form = document.getElementById('form-ensure-sample');
        if (nameInput) {
            nameInput.value = sampleName;
        }
        if (form) {
            if (typeof window.showBusyOverlay === 'function') {
                window.showBusyOverlay('Creating sample database…', 'Seeding tables and demo data. This can take a few seconds.');
            }
            form.submit();
        }
    }

    function clickDataAction(action) {
        const el = document.querySelector('[data-action="' + action + '"]');
        if (el) { el.click(); }
    }

    function clickSelector(selector) {
        const el = document.querySelector(selector);
        if (el) { el.click(); }
    }

    function submitFormByHandler(handler) {
        // Forms use asp-page-handler and names like handler=PreviewTable
        const form = document.querySelector('form[asp-page-handler="' + handler + '"], form[data-handler="' + handler + '"]');
        if (form) { form.submit(); return; }

        // Fallback: any _RequestVerificationToken + hidden FormHandler
        document.querySelectorAll('form').forEach(f => {
            const h = f.querySelector('input[name="__handler"], input[name="FormHandler"]');
            if (h && h.value === handler) { f.submit(); }
        });
    }

    function toggleVisibility(selector) {
        const el = document.querySelector(selector);
        if (el) { el.classList.toggle('scdb-hidden'); }
    }

    function revealSidebarGroup(groupId) {
        const group = document.getElementById(groupId);
        if (!group) { return; }

        const header = group.querySelector('.scdb-sidebar__group-header');
        const content = group.querySelector('.scdb-sidebar__group-content');
        if (header && content?.hasAttribute('hidden')) {
            content.removeAttribute('hidden');
            header.setAttribute('aria-expanded', 'true');
            const chevron = header.querySelector('.scdb-sidebar__chevron');
            if (chevron) { chevron.textContent = '▾'; }
        }

        group.scrollIntoView({ block: 'start', behavior: 'smooth' });
    }

    function executeSelection(ctx) {
        const ed = document.getElementById('scdb-sql-editor');
        if (!ed) {
            clickSelector('#btn-execute, #btn-execute-inner');
            return;
        }

        const start = ed.selectionStart;
        const end = ed.selectionEnd;
        if (start === end) {
            clickSelector('#btn-execute, #btn-execute-inner');
            return;
        }

        const selected = ed.value.substring(start, end);
        if (!selected.trim()) {
            clickSelector('#btn-execute, #btn-execute-inner');
            return;
        }

        // Put selection in the editor and reset focus so F5 executes it
        ed.focus();
        ed.setSelectionRange(start, end);
        if (typeof window.SharpCoreDBExecuteSelection === 'function') {
            window.SharpCoreDBExecuteSelection(selected);
        }
    }

    function saveActiveQuery() {
        const editor = document.getElementById('scdb-sql-editor');
        if (!editor) { return; }

        const sql = editor.value?.trim();
        if (!sql) {
            return;
        }

        const form = document.querySelector('#form-save-query, form[data-handler="SaveQuery"]');
        if (form) {
            const sqlInput = form.querySelector('input[name="Query.Sql"]');
            const paramsInput = form.querySelector('input[name="Query.ParametersJson"]');
            if (sqlInput) { sqlInput.value = sql; }
            if (paramsInput) {
                paramsInput.value = document.getElementById('scdb-params-field')?.value ?? '';
            }
            form.submit();
            return;
        }

        // No dedicated form yet — surface to status bar
        const status = document.getElementById('scdb-statusbar-msg');
        if (status) {
            status.textContent = 'Query ready to save (use the Saved Queries group in the Object Explorer).';
        }
    }

    function exportWorkspace() {
        const form = document.getElementById('form-export-workspace');
        if (form) {
            form.submit();
            return;
        }
        showStatusMessage('Workspace export is not available on this page.');
    }

    function importWorkspace() {
        const dialog = document.getElementById('scdb-workspace-dialog');
        if (dialog && typeof dialog.showModal === 'function') {
            dialog.showModal();
            const textarea = document.getElementById('scdb-workspace-json');
            if (textarea) { textarea.focus(); }
            return;
        }
        showStatusMessage('Workspace import is not available on this page.');
    }

    function showSettingsHint() {
        showStatusMessage('SCDMS settings are configured via SCDMS__ environment variables or appsettings.json (see docs/usage.md).');
    }

    function showStatusMessage(message) {
        const status = document.getElementById('scdb-statusbar-msg');
        if (status) {
            status.textContent = message;
        } else {
            alert(message);
        }
    }

    function showCreateTableDialog() {
        const dialog = document.getElementById('scdb-create-table-dialog');
        if (dialog && typeof dialog.showModal === 'function') {
            dialog.showModal();
            return;
        }
        // Fallback event for pages without the dialog markup
        document.dispatchEvent(new CustomEvent('scdb:create-table-requested', { bubbles: true }));
    }

    function showImportCsvDialog() {
        const existing = document.getElementById('scdb-import-csv-dialog');
        if (existing && typeof existing.showModal === 'function') {
            existing.showModal();
            return;
        }

        // Placeholder: dispatch event that the main viewer can handle
        document.dispatchEvent(new CustomEvent('scdb:import-csv-requested', { bubbles: true }));
    }

    function openDocs(url) {
        // The docs are hosted in the GitHub repos (not served from the web root).
        window.open(url, '_blank', 'noopener');
    }

    function showAbout() {
        const existing = document.getElementById('scdb-about-dialog');
        if (existing && typeof existing.showModal === 'function') {
            existing.showModal();
            return;
        }
        const msg = 'SCDMS — Sharp Core Database Management System. Local-first database studio for SharpCoreDB.';
        // Accessible, CSP-safe: use a dialog when available, else status bar
        const status = document.getElementById('scdb-statusbar-msg');
        if (status) { status.textContent = msg; }
    }

    function handleMenuAction(e) {
        const { action } = e.detail || {};
        if (!action) { return; }

        // Build editor context from DOM (Monaco exposes API in future)
        const ctx = { editor: null };

        const handler = actionMap[action];
        if (handler) {
            try {
                handler(ctx);
            } catch (err) {
                console.error('[menu-actions] Failed action ' + action + ':', err);
            }
        }
    }

    document.addEventListener(MENU_EVENT, handleMenuAction);

    // Expose public API for other modules
    window.SharpCoreDBMenuActions = {
        handle: handleMenuAction,
        clickDataAction,
        clickSelector,
        submitFormByHandler
    };

    // Attach when DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => { /* no-op */ });
    }
})();