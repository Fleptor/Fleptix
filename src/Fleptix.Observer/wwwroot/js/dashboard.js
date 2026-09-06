// Fleptix Observer — Dashboard Cockpit Controller & Real-Time Telemetry Client

let clusterCpuChart = null;
let clusterMemChart = null;
const MAX_CHART_POINTS = 20;

const chartTimeLabels = [];
const cpuDataPoints = [];
const memDataPoints = [];
let eventCount = 2;

// Initialize Chart.js instances
function initCharts() {
    const ctxCpu = document.getElementById('clusterCpuChart')?.getContext('2d');
    const ctxMem = document.getElementById('clusterMemChart')?.getContext('2d');

    if (ctxCpu) {
        clusterCpuChart = new Chart(ctxCpu, {
            type: 'line',
            data: {
                labels: chartTimeLabels,
                datasets: [{
                    label: 'CPU %',
                    data: cpuDataPoints,
                    borderColor: '#1E2761',
                    backgroundColor: 'rgba(202, 220, 252, 0.4)',
                    borderWidth: 1.5,
                    fill: true,
                    tension: 0.25,
                    pointRadius: 0,
                    pointHoverRadius: 3
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 250 },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        mode: 'index',
                        intersect: false,
                        backgroundColor: '#0f172a',
                        titleColor: '#CADCFC',
                        bodyColor: '#ffffff',
                        titleFont: { family: 'ui-monospace, monospace', size: 10 },
                        bodyFont: { family: 'ui-monospace, monospace', size: 10 }
                    }
                },
                scales: {
                    x: {
                        grid: { color: '#e2e5eb' },
                        ticks: { color: '#64748b', font: { family: 'ui-monospace, monospace', size: 9 }, maxRotation: 0 }
                    },
                    y: {
                        min: 0,
                        suggestedMax: 50,
                        grid: { color: '#e2e5eb' },
                        ticks: {
                            color: '#64748b',
                            font: { family: 'ui-monospace, monospace', size: 9 },
                            callback: v => v + '%'
                        }
                    }
                }
            }
        });
    }

    if (ctxMem) {
        clusterMemChart = new Chart(ctxMem, {
            type: 'line',
            data: {
                labels: chartTimeLabels,
                datasets: [{
                    label: 'RAM (MB)',
                    data: memDataPoints,
                    borderColor: '#4338ca',
                    backgroundColor: 'rgba(199, 210, 254, 0.4)',
                    borderWidth: 1.5,
                    fill: true,
                    tension: 0.25,
                    pointRadius: 0,
                    pointHoverRadius: 3
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 250 },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        mode: 'index',
                        intersect: false,
                        backgroundColor: '#0f172a',
                        titleColor: '#CADCFC',
                        bodyColor: '#ffffff',
                        titleFont: { family: 'ui-monospace, monospace', size: 10 },
                        bodyFont: { family: 'ui-monospace, monospace', size: 10 }
                    }
                },
                scales: {
                    x: {
                        grid: { color: '#e2e5eb' },
                        ticks: { color: '#64748b', font: { family: 'ui-monospace, monospace', size: 9 }, maxRotation: 0 }
                    },
                    y: {
                        min: 0,
                        grid: { color: '#e2e5eb' },
                        ticks: {
                            color: '#64748b',
                            font: { family: 'ui-monospace, monospace', size: 9 },
                            callback: v => v + ' MB'
                        }
                    }
                }
            }
        });
    }
}

// Push data point to charts
function pushChartTelemetry(timeStr, cpuPercent, memoryMb) {
    if (chartTimeLabels.length >= MAX_CHART_POINTS) {
        chartTimeLabels.shift();
        cpuDataPoints.shift();
        memDataPoints.shift();
    }

    chartTimeLabels.push(timeStr);
    cpuDataPoints.push(cpuPercent);
    memDataPoints.push(memoryMb);

    if (clusterCpuChart) clusterCpuChart.update('none');
    if (clusterMemChart) clusterMemChart.update('none');

    const cpuBadge = document.getElementById('liveCpuBadge');
    if (cpuBadge) cpuBadge.textContent = cpuPercent.toFixed(1) + '%';
    const memBadge = document.getElementById('liveMemBadge');
    if (memBadge) memBadge.textContent = memoryMb.toFixed(1) + ' MB';
}

// Append event to Live Event Feed
function appendEventFeed(text, isHighlight = false) {
    const feed = document.getElementById('clusterEventFeed');
    const counter = document.getElementById('eventFeedCount');
    if (!feed) return;

    eventCount++;
    if (counter) counter.textContent = `${eventCount} events`;

    const nowStr = new Date().toTimeString().split(' ')[0];
    const item = document.createElement('div');
    item.className = 'event-feed-item';
    item.innerHTML = `<span class="event-time">[${nowStr}]</span> <span class="event-text ${isHighlight ? 'highlight' : ''}">${text}</span>`;
    feed.appendChild(item);

    // Keep feed trimmed to 60 items
    while (feed.children.length > 60) {
        feed.removeChild(feed.firstChild);
    }

    feed.scrollTop = feed.scrollHeight;
}

// Update DOM elements on SignalR Telemetry Push
function handleClusterTelemetry(payload) {
    if (!payload) return;

    // Update Top Strip Metrics
    const cpuEl = document.getElementById('dashTotalCpu');
    if (cpuEl) cpuEl.textContent = payload.totalCpuPercentage.toFixed(1) + '%';

    const memEl = document.getElementById('dashTotalMem');
    if (memEl) memEl.textContent = payload.totalMemoryUsageMb.toFixed(1) + ' MB';

    const ratioEl = document.getElementById('dashStateRatio');
    if (ratioEl) {
        ratioEl.innerHTML = `<span class="text-success">${payload.runningCount}</span> / <span class="text-warning">${payload.pausedCount}</span> / <span class="text-muted">${payload.stoppedCount}</span>`;
    }

    // Push to charts
    const timeStr = new Date(payload.timestamp).toLocaleTimeString();
    pushChartTelemetry(timeStr, payload.totalCpuPercentage, payload.totalMemoryUsageMb);

    // Update Active Workloads Table
    if (payload.containers && payload.metrics) {
        payload.containers.forEach(c => {
            const m = payload.metrics[c.id];
            updateWorkloadRow(c, m);
        });
    }
}

function updateWorkloadRow(container, metric) {
    const cid = container.id;
    const row = document.querySelector(`.dash-workload-row[data-container-id="${cid}"]`);
    if (!row) return;

    row.setAttribute('data-state', container.state);

    // Update Pill
    const pillContainer = document.getElementById(`dash-pill-${cid}`);
    if (pillContainer) {
        const pillClass = container.state === 'running' ? 'running' : (container.state === 'paused' ? 'restarting' : 'stopped');
        pillContainer.innerHTML = `<span class="status-pill ${pillClass}"><span class="pill-dot"></span><span class="pill-text">${container.state}</span></span>`;
    }

    // Update Mini Meters
    if (metric) {
        const cpuVal = document.getElementById(`dash-cpu-val-${cid}`);
        const cpuBar = document.getElementById(`dash-cpu-bar-${cid}`);
        if (cpuVal) cpuVal.textContent = `${metric.cpuPercentage.toFixed(1)}%`;
        if (cpuBar) cpuBar.style.width = `${Math.min(100, Math.max(2, metric.cpuPercentage))}%`;

        const memVal = document.getElementById(`dash-mem-val-${cid}`);
        const memBar = document.getElementById(`dash-mem-bar-${cid}`);
        if (memVal) memVal.textContent = `${metric.memoryUsageMb.toFixed(1)} MB`;
        if (memBar) memBar.style.width = `${Math.min(100, Math.max(2, metric.memoryPercentage))}%`;
    }

    updateDashboardActionButtons(cid, container.state);
}

function updateDashboardActionButtons(cid, state) {
    const group = document.getElementById(`dash-actions-${cid}`);
    if (!group) return;

    let html = '';
    if (state !== 'running') {
        html += `<button class="btn-icon-action action-start" onclick="executeDashboardAction('${cid}', 'start', this)" title="Start"><i class="bi bi-play-fill"></i></button> `;
    }
    if (state === 'running') {
        html += `<button class="btn-icon-action action-stop" onclick="executeDashboardAction('${cid}', 'stop', this)" title="Stop"><i class="bi bi-stop-fill"></i></button> `;
        html += `<button class="btn-icon-action action-restart" onclick="executeDashboardAction('${cid}', 'restart', this)" title="Restart"><i class="bi bi-arrow-clockwise"></i></button> `;
    }
    if (state === 'paused') {
        html += `<button class="btn-icon-action action-start" onclick="executeDashboardAction('${cid}', 'unpause', this)" title="Resume"><i class="bi bi-play-fill"></i></button> `;
    }
    html += `<a href="/Details?id=${cid}" class="btn-icon-action" title="Details"><i class="bi bi-chevron-right"></i></a>`;

    group.innerHTML = html;
}

// Execute Action on Dashboard
window.executeDashboardAction = async function(containerId, action, buttonEl) {
    if (buttonEl) {
        buttonEl.disabled = true;
        buttonEl.innerHTML = `<span class="spinner-border spinner-border-sm" style="width: 10px; height: 10px;" role="status"></span>`;
    }

    try {
        const response = await fetch('/api/containers/action', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ containerId, action })
        });

        const data = await response.json();
        if (response.ok && data.success) {
            window.showToast(data.message || `Action ${action} executed successfully!`);
            appendEventFeed(`Action '${action}' executed on container ${containerId.substring(0, 12)} (${data.newState})`, true);
            if (data.newState) {
                updateDashboardActionButtons(containerId, data.newState);
            }
        } else {
            window.showToast(data.message || 'Action failed.', true);
            appendEventFeed(`Action '${action}' failed on container ${containerId.substring(0, 12)}: ${data.message}`);
        }
    } catch (err) {
        window.showToast(`Error executing ${action}: ${err.message}`, true);
    } finally {
        if (buttonEl) buttonEl.disabled = false;
    }
};

// SignalR Setup
function setupSignalR() {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/containers")
        .withAutomaticReconnect()
        .build();

    connection.on("ReceiveClusterMetrics", (payload) => {
        handleClusterTelemetry(payload);
    });

    connection.start()
        .then(() => console.log("Connected to Fleptix SignalR Hub"))
        .catch(err => console.error("SignalR Connection Error:", err));
}

document.addEventListener('DOMContentLoaded', () => {
    initCharts();
    setupSignalR();
});
