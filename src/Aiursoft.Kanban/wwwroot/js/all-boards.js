document.addEventListener('DOMContentLoaded', () => {
    const error = document.getElementById('allBoardsError');
    const deleteForm = document.getElementById('deleteBoardForm');
    const deleteModal = document.getElementById('deleteBoardModal');
    deleteModal.addEventListener('show.bs.modal', event => {
        const button = event.relatedTarget;
        document.getElementById('deleteBoardId').value = button.dataset.boardId;
        document.getElementById('deleteBoardName').textContent = button.dataset.boardName;
    });

    [...document.querySelectorAll('.board-rename-form'), deleteForm].forEach(form => {
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const button = form.querySelector('button[type="submit"]');
            button.disabled = true;
            error.classList.add('d-none');
            try {
                const response = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form)
                });
                if (!response.ok || response.redirected) throw new Error('Request failed');
                window.location.reload();
            } catch {
                bootstrap.Modal.getInstance(deleteModal)?.hide();
                error.querySelector('.alert-message').textContent = document.querySelector('#allBoardsLocalization [data-key="failed"]').textContent;
                error.classList.remove('d-none');
                error.scrollIntoView({ block: 'nearest' });
                button.disabled = false;
            }
        });
    });
});
