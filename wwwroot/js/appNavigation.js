(() => {
    const preferenceKey = "wcg-sidebar-collapsed";

    function shell() {
        return document.querySelector("[data-app-shell]");
    }

    function updateNavigation() {
        const current = shell();
        if (!current) return;

        try {
            current.classList.toggle("is-collapsed", sessionStorage.getItem(preferenceKey) === "true");
        } catch {
            // Session storage can be unavailable in restricted browser modes.
        }

        const collapsed = current.classList.contains("is-collapsed");
        const toggle = current.querySelector("[data-sidebar-toggle]");
        if (toggle) {
            toggle.setAttribute("aria-expanded", String(!collapsed));
            toggle.setAttribute("aria-label", collapsed ? "Expand sidebar" : "Collapse sidebar");
            toggle.title = collapsed ? "Expand sidebar" : "Collapse sidebar";
        }

        const mobile = current.querySelector("[data-mobile-toggle]");
        if (mobile) {
            mobile.setAttribute("aria-expanded", String(current.classList.contains("is-drawer-open")));
            mobile.setAttribute("aria-label", current.classList.contains("is-drawer-open") ? "Close navigation" : "Open navigation");
        }

        const sidebar = current.querySelector(".app-sidebar");
        const isMobile = matchMedia("(max-width: 800px)").matches;
        const drawerOpen = current.classList.contains("is-drawer-open");
        if (sidebar) sidebar.inert = isMobile && !drawerOpen;
        const main = current.querySelector(".app-main");
        if (main) main.inert = isMobile && drawerOpen;
    }

    function closeDrawer(restoreFocus) {
        const current = shell();
        if (!current) return;
        current.classList.remove("is-drawer-open");
        updateNavigation();
        if (restoreFocus) current.querySelector("[data-mobile-toggle]")?.focus();
    }

    document.addEventListener("click", event => {
        const current = shell();
        if (!current) return;
        const target = event.target;
        if (!(target instanceof Element)) return;

        if (target.closest("[data-sidebar-toggle]")) {
            const collapsed = current.classList.toggle("is-collapsed");
            try { sessionStorage.setItem(preferenceKey, String(collapsed)); } catch {}
            updateNavigation();
        } else if (target.closest("[data-mobile-toggle]")) {
            const opening = !current.classList.contains("is-drawer-open");
            current.classList.toggle("is-drawer-open", opening);
            updateNavigation();
            if (opening) current.querySelector("[data-mobile-close]")?.focus();
        } else if (target.closest("[data-mobile-close]")) {
            closeDrawer(true);
        } else if (target.closest(".app-sidebar a")) {
            closeDrawer(false);
        }

        const menu = current.querySelector("[data-account-menu]");
        if (menu?.open && !menu.contains(target)) menu.open = false;
    });

    document.addEventListener("keydown", event => {
        if (event.key !== "Escape") return;
        const current = shell();
        if (!current) return;
        const menu = current.querySelector("[data-account-menu]");
        if (menu?.open) {
            menu.open = false;
            menu.querySelector("summary")?.focus();
        } else if (current.classList.contains("is-drawer-open")) {
            closeDrawer(true);
        }
    });

    addEventListener("resize", updateNavigation);
    document.addEventListener("DOMContentLoaded", updateNavigation);
    document.addEventListener("enhancedload", () => {
        const current = shell();
        if (current) {
            current.classList.remove("is-drawer-open");
            const menu = current.querySelector("[data-account-menu]");
            if (menu) menu.open = false;
        }
        updateNavigation();
    });
})();
