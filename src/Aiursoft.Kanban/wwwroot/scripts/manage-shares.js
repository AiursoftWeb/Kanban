(() => {
    'use strict';
    const page = document.getElementById('sharing-page');
    if (!page) return;
    const text = (key, fallback) => page.querySelector(`[data-key="${key}"]`)?.textContent ?? fallback;
    const addDetails = document.getElementById('add-share-details');
    const addForm = document.getElementById('add-share-form');
    const roleSelect = document.getElementById('roleSelect');
    const userId = document.getElementById('selectedUserId');
    const recipientPrompt = () => (userId.disabled ? roleSelect : userId).options[0].textContent;
    const visibilityForm = document.getElementById('visibility-form');
    const linkAccess = document.getElementById('link-access');

    function showError(form, message) {
        const details = form.closest('details');
        if (details) details.open = true;
        const error = form.querySelector('.form-error');
        error.textContent = message;
        error.hidden = false;
    }

    page.querySelectorAll('input[name="shareType"]').forEach(radio => radio.addEventListener('change', () => {
        const isUser = document.getElementById('shareTypeUser').checked;
        document.getElementById('role-selection').hidden = isUser;
        document.getElementById('user-selection').hidden = !isUser;
        roleSelect.disabled = isUser;
        userId.disabled = !isUser;
    }));

    linkAccess.addEventListener('change', () => {
        document.getElementById('visibility-pending').hidden = linkAccess.value === visibilityForm.dataset.savedPublic;
    });
    addForm.addEventListener('invalid', () => { addDetails.open = true; }, true);

    // Keep inputs in place on validation, network or session failures. Only a
    // redirect back to this management page confirms a successful operation.
    page.querySelectorAll('form').forEach(form => form.addEventListener('submit', async event => {
        event.preventDefault();
        if (form.dataset.busy === 'true') return;
        if (form === addForm && ((!roleSelect.disabled && !roleSelect.value) ||
            (!userId.disabled && !userId.value))) {
            showError(form, recipientPrompt());
            (userId.disabled ? roleSelect : userId).focus();
            return;
        }
        if (form.classList.contains('remove-share-form') && !window.confirm(text('remove', 'Remove this share? Other access may still apply.'))) return;
        const data = new FormData(form);
        const submit = form.querySelector('[type="submit"]');
        form.dataset.busy = 'true';
        form.setAttribute('aria-busy', 'true');
        submit.disabled = true;
        form.querySelector('.form-error').hidden = true;
        try {
            const response = await fetch(form.action, { method: 'POST', body: data });
            const destination = new URL(response.url);
            if (!response.ok || !response.redirected || destination.origin !== location.origin || destination.pathname !== location.pathname) {
                throw new Error('Change not confirmed');
            }
            const error = destination.searchParams.get('error');
            if (error) {
                showError(form, error === 'duplicate'
                    ? text('duplicate', 'This user or role already has access.')
                    : recipientPrompt());
                return;
            }
            location.assign(destination.href);
        } catch {
            showError(form, text('failed', 'Could not confirm the change. Your input is kept. Try again, or reload to check the saved state.'));
        } finally {
            form.dataset.busy = 'false';
            form.removeAttribute('aria-busy');
            submit.disabled = false;
        }
    }));

    document.getElementById('copy-share-link').addEventListener('click', async () => {
        const status = document.getElementById('copy-status');
        const link = document.getElementById('share-link');
        try {
            await navigator.clipboard.writeText(link.value);
            status.textContent = text('copied', 'Link copied.');
        } catch {
            link.focus();
            link.select();
            status.textContent = text('copyFailed', 'Could not copy. Select and copy the link above.');
        }
        status.hidden = false;
    });

    const error = new URLSearchParams(location.search).get('error');
    if (error) showError(addForm, error === 'duplicate'
        ? text('duplicate', 'This user or role already has access.')
        : recipientPrompt());
})();
