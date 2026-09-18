(() => {
    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.matches("[data-busy-form]")) return;
        if (form.dataset.submitting === "true") {
            event.preventDefault();
            return;
        }
        if (!form.checkValidity()) return;

        const button = form.querySelector("[data-busy-submit]");
        if (!(button instanceof HTMLButtonElement)) return;
        button.dataset.originalText ||= button.textContent.trim();
        form.dataset.submitting = "true";
        button.disabled = true;
        button.setAttribute("aria-busy", "true");
        button.replaceChildren();
        const spinner = document.createElement("span");
        spinner.className = "spinner-border spinner-border-sm me-2";
        spinner.setAttribute("aria-hidden", "true");
        button.append(spinner, document.createTextNode(button.dataset.busyText));

        let status = form.querySelector("[data-busy-status]");
        if (!status) {
            status = document.createElement("span");
            status.className = "visually-hidden";
            status.dataset.busyStatus = "";
            status.setAttribute("role", "status");
            form.append(status);
        }
        status.textContent = button.dataset.busyText;
    }, true);

    // Browsers can restore the previous form from the back-forward cache.
    window.addEventListener("pageshow", () => {
        document.querySelectorAll("[data-busy-form]").forEach(form => {
            form.dataset.submitting = "";
            const button = form.querySelector("[data-busy-submit]");
            if (button?.dataset.originalText) {
                button.disabled = false;
                button.removeAttribute("aria-busy");
                button.textContent = button.dataset.originalText;
            }
            form.querySelector("[data-busy-status]")?.remove();
        });
    });
})();
