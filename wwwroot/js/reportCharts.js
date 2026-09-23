

window.reportCharts = (() => {
    const charts = new Map();
    const colors = ["#005a8b", "#2f9e44", "#f59f00", "#c92a2a", "#7048e8", "#0c8599", "#495057"];
    function render(id, model) {
        const canvas = document.getElementById(id); if (!canvas || !model) return; charts.get(id)?.destroy();
        const datasets = model.series.map((s, i) => ({ label: s.name, data: s.values,
            backgroundColor: model.type === "line" ? undefined : (model.type === "doughnut" || model.type === "pie" ? s.values.map((_, j) => colors[j % colors.length]) : colors[i % colors.length]),
            borderColor: colors[i % colors.length], borderWidth: 2, tension: .2 }));
        charts.set(id, new Chart(canvas, { type: model.type, data: { labels: model.labels, datasets }, options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: "bottom" } }, scales: model.type === "doughnut" || model.type === "pie" ? {} : { x: { stacked: model.stacked }, y: { stacked: model.stacked, beginAtZero: true, title: { display: true, text: model.wholeNumbers ? "Applications" : "Days" }, ticks: model.wholeNumbers ? { precision: 0, stepSize: 1 } : {} } } } }));
    }
    return { render };
})();
