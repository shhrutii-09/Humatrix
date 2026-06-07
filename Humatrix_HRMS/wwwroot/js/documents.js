// Add these to your existing wwwroot/js/documents.js file
window.showModal = (modalId) => {
    const modalElement = document.getElementById(modalId);
    if (modalElement) {
        const modal = new bootstrap.Modal(modalElement);
        modal.show();
    }
};

window.hideModal = (modalId) => {
    const modalElement = document.getElementById(modalId);
    if (modalElement) {
        const modal = bootstrap.Modal.getInstance(modalElement);
        if (modal) modal.hide();
    }
};

window.showToast = (type, message) => {
    // You can implement a proper toast notification system here
    // For now, using alert
    if (type === 'success') {
        alert('✓ Success: ' + message);
    } else if (type === 'error') {
        alert('✗ Error: ' + message);
    } else {
        alert(message);
    }
};