// wwwroot/js/site.js

// ── Audio — preloaded ONCE globally ──────────────────────────────────────────
const _notificationAudio = (() => {
    const audio = new Audio('/sounds/notification.mp3');
    audio.preload = 'auto';
    return audio;
})();

window.playNotificationSound = () => {
    try {
        _notificationAudio.currentTime = 0;
        _notificationAudio.play().catch(() => {/* autoplay blocked — ignore */ });
    } catch (e) { /* ignore */ }
};

// ── Toast notifications ───────────────────────────────────────────────────────
window.showToastNotification = (title, message, url, priority) => {
    // Browser Notification API
    if ('Notification' in window && Notification.permission === 'granted') {
        try {
            const n = new Notification(title, { body: message, icon: '/favicon.png' });
            n.onclick = () => { window.focus(); if (url) window.location.href = url; };
        } catch (e) { /* ignore */ }
    } else if ('Notification' in window && Notification.permission !== 'denied') {
        Notification.requestPermission();
    }

    // In-app toast
    const priorityClass = (priority === 'Urgent' || priority === 'High')
        ? 'toast-notification--urgent' : '';

    const toast = document.createElement('div');
    toast.className = `toast-notification shadow ${priorityClass}`;
    toast.style.cursor = url ? 'pointer' : 'default';
    toast.innerHTML = `
        <div class="toast-notification__title">${title}</div>
        <div class="toast-notification__body">${message}</div>
    `;
    if (url) toast.onclick = () => { window.location.href = url; };

    document.body.appendChild(toast);
    requestAnimationFrame(() => toast.classList.add('show'));
    setTimeout(() => toast.remove(), 5000);
};

// ── Notification permission request ─────────────────────────────────────────
window.requestNotificationPermission = async () => {
    if ('Notification' in window && Notification.permission === 'default') {
        return await Notification.requestPermission();
    }
    return Notification.permission ?? 'denied';
};