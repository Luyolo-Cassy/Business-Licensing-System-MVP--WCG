

window.drawStatusChart = (submitted, underReview, approved, rejected, withdrawn) => {

    const ctx = document.getElementById("statusChart");

    new Chart(ctx, {
        type: "pie",
        data: {
            labels: [
                "Submitted",
                "Under Review",
                "Approved",
                "Rejected",
                "Withdrawn"
            ],
            datasets: [{
                data: [
                    submitted,
                    underReview,
                    approved,
                    rejected,
                    withdrawn
                ],
                backgroundColor: [
                    "#6c757d",
                    "#0dcaf0",
                    "#198754",
                    "#dc3545",
                    "#212529"
                ]
            }]
        },
        options: {
            responsive: true,
            plugins: {
                legend: {
                    position: "bottom"
                }
            }
        }
    });

    

};

window.drawMonthlyChart = (labels, values) => {

    const ctx = document.getElementById("monthlyChart");

    new Chart(ctx, {
        type: "bar",
        data: {
            labels: labels,
            datasets: [{
                label: "Applications",
                data: values,
                backgroundColor: "#0d6efd"
            }]
        },
        options: {
            responsive: true,
            scales: {
                y: {
                    beginAtZero: true
                }
            }
        }
    });

};

window.drawLicenceChart = (labels, values) => {

    const ctx = document.getElementById("licenceChart");

    new Chart(ctx, {
        type: "pie",
        data: {
            labels: labels,
            datasets: [{
                data: values
            }]
        },
        options: {
            responsive: true,
            plugins: {
                legend: {
                    position: "bottom"
                }
            }
        }
    });

};