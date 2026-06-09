// Modal helpers for exit management
window.showExitModal = () => {
    const modalEl = document.querySelector('#exitDetailsModal');
    if (modalEl) {
        const modal = new bootstrap.Modal(modalEl);
        modal.show();
    }
};

window.hideExitModal = () => {
    const modalEl = document.querySelector('#exitDetailsModal');
    if (modalEl) {
        const modal = bootstrap.Modal.getInstance(modalEl);
        if (modal) modal.hide();
    }
};

window.showRejectModal = () => {
    const modalEl = document.querySelector('#rejectModal');
    if (modalEl) {
        const modal = new bootstrap.Modal(modalEl);
        modal.show();
    }
};

window.hideRejectModal = () => {
    const modalEl = document.querySelector('#rejectModal');
    if (modalEl) {
        const modal = bootstrap.Modal.getInstance(modalEl);
        if (modal) modal.hide();
    }
};

window.loadExitModals = () => {
    // Ensure modals are initialized if they exist
    const exitModal = document.querySelector('#exitDetailsModal');
    if (exitModal && !exitModal.classList.contains('modal-init')) {
        exitModal.classList.add('modal-init');
    }

    const rejectModal = document.querySelector('#rejectModal');
    if (rejectModal && !rejectModal.classList.contains('modal-init')) {
        rejectModal.classList.add('modal-init');
    }
};