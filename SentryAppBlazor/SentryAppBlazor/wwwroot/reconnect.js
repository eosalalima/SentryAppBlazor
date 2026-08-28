(() => {
    const modal = document.getElementById("components-reconnect-modal");
    if (!modal) return;

    let reloadScheduled = false;
    const reloadWhenCircuitIsGone = () => {
        if (reloadScheduled || !modal.classList.contains("components-reconnect-rejected")) return;

        reloadScheduled = true;
        // A rejected reconnect means the server is reachable, but the old circuit no
        // longer exists (for example, after an application restart). A page reload is
        // the only way to create a new circuit, so don't leave the monitor retrying it.
        window.setTimeout(() => window.location.reload(), 1000);
    };

    new MutationObserver(reloadWhenCircuitIsGone).observe(modal, {
        attributes: true,
        attributeFilter: ["class"]
    });
    reloadWhenCircuitIsGone();
})();
