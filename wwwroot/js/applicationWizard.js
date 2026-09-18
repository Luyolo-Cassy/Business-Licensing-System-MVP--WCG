window.applicationWizard = {
    scrollToStep() {
        const target = document.getElementById("application-wizard-top");
        if (!target) return;
        target.scrollIntoView({ behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth", block: "start" });
    },
    focusInvalid(id) {
        const target = document.getElementById(id);
        if (!target) return;
        target.scrollIntoView({ behavior: "instant", block: "center" });
        if (typeof target.focus === "function") target.focus({ preventScroll: true });
    }
};
