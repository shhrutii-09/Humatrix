window.playNotificationSound = () => {

    const audio = new Audio('/sounds/notification.mp3');

    audio.play();
};

window.showToastNotification = (title, message, url) => {

    // ===== Browser Notification =====
    try {

        if ("Notification" in window) {

            if (Notification.permission === "granted") {

                const notification = new Notification(title, {
                    body: message,
                    icon: "/favicon.png"
                });

                notification.onclick = function () {

                    window.focus();

                    if (url && url.trim() !== "") {

                        window.location.href = url;
                    }
                };
            }
            else if (Notification.permission !== "denied") {

                Notification.requestPermission().then(permission => {

                    if (permission === "granted") {

                        const notification = new Notification(title, {
                            body: message,
                            icon: "/favicon.png"
                        });

                        notification.onclick = function () {

                            window.focus();

                            if (url && url.trim() !== "") {

                                window.location.href = url;
                            }
                        };
                    }
                });
            }
        }

    }
    catch (err) {

        console.error("Browser notification error:", err);
    }

    // ===== In-App Toast =====
    const toast = document.createElement("div");

    toast.className = "toast-notification shadow";

    toast.style.cursor = "pointer";

    toast.innerHTML = `
        <div class="fw-bold">${title}</div>
        <div>${message}</div>
    `;

    // CLICK REDIRECT
    toast.onclick = () => {

        if (url && url.trim() !== "") {

            window.location.href = url;
        }
    };

    document.body.appendChild(toast);

    setTimeout(() => {
        toast.classList.add("show");
    }, 100);

    setTimeout(() => {
        toast.remove();
    }, 5000);
};