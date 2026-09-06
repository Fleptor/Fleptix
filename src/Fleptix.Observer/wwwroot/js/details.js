// Fleptix Observer — Container Details Controller & Log Terminal

function pushContainerMetric(metric) {
    if (!metric) return;

    // Update Telemetry Strip
    const liveTelemetry = document.getElementById('detailsLiveTelemetry');
    if (liveTelemetry) {
        liveTelemetry.textContent = `CPU: ${metric.cpuPercentage.toFixed(1)}% | RAM: ${metric.memoryUsageMb.toFixed(1)} MB`;
    }
}

// Action Dispatcher for Details view
window.executeDetailsAction = async function(containerId, action, buttonEl) {
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
            window.showToast(data.message || `Action executed!`);
            if (data.newState) {
                const pillContainer = document.getElementById('detailsStatusPill');
                const statusText = document.getElementById('detailsStatusText');
                if (pillContainer) {
                    const pillClass = data.newState === 'running' ? 'running' : (data.newState === 'paused' ? 'restarting' : 'stopped');
                    pillContainer.innerHTML = `<span class="status-pill ${pillClass}"><span class="pill-dot"></span><span class="pill-text">${data.newState}</span></span>`;
                }
                if (statusText) {
                    statusText.textContent = data.newState;
                }
                updateDetailsActionButtons(containerId, data.newState);
            }
        } else {
            window.showToast(data.message || 'Action failed.', true);
        }
    } catch (err) {
        window.showToast(`Error: ${err.message}`, true);
    } finally {
        if (buttonEl) buttonEl.disabled = false;
    }
};

function updateDetailsActionButtons(cid, state) {
    const group = document.getElementById('lifecycleActionButtons') || document.getElementById('detailsActionGroup');
    if (!group) return;

    let html = '';
    if (state !== 'running') {
        html += `<button class="btn btn-sm d-inline-flex align-items-center gap-1 text-white" style="background-color: var(--accent-navy); font-size: 11px; padding: 4px 12px;" onclick="executeDetailsAction('${cid}', 'start', this)"><i class="bi bi-play-fill"></i> Start</button> `;
    }
    if (state === 'running') {
        html += `<button class="btn btn-sm btn-light border d-inline-flex align-items-center gap-1 font-mono text-danger" style="font-size: 11px; padding: 4px 10px;" onclick="executeDetailsAction('${cid}', 'stop', this)"><i class="bi bi-stop-fill"></i> Stop</button> `;
        html += `<button class="btn btn-sm btn-light border d-inline-flex align-items-center gap-1 font-mono text-dark" style="font-size: 11px; padding: 4px 10px;" onclick="executeDetailsAction('${cid}', 'restart', this)"><i class="bi bi-arrow-clockwise"></i> Restart</button> `;
    }
    if (state === 'paused') {
        html += `<button class="btn btn-sm btn-light border d-inline-flex align-items-center gap-1 font-mono text-success" style="font-size: 11px; padding: 4px 10px;" onclick="executeDetailsAction('${cid}', 'unpause', this)"><i class="bi bi-play-fill"></i> Resume</button> `;
    }

    group.innerHTML = html;
}

// Tab Switching Logic
function setupTabs() {
    const tabButtons = document.querySelectorAll('.details-tab-link');
    tabButtons.forEach(btn => {
        btn.addEventListener('click', () => {
            tabButtons.forEach(b => {
                b.classList.remove('active');
                b.classList.replace('btn-secondary', 'btn-outline-secondary');
            });
            btn.classList.add('active');
            btn.classList.replace('btn-outline-secondary', 'btn-secondary');

            const targetId = btn.getAttribute('data-tab');
            document.querySelectorAll('.tab-pane-content').forEach(p => p.classList.add('d-none'));
            const targetEl = document.getElementById(targetId);
            if (targetEl) targetEl.classList.remove('d-none');
        });
    });
}

// Log Terminal Helpers
let logPollingInterval = null;
async function fetchLatestLogs() {
    const cid = window.CURRENT_CONTAINER_ID;
    if (!cid) return;

    try {
        const res = await fetch(`/api/containers/${cid}/logs?tail=100`);
        if (res.ok) {
            const data = await res.json();
            renderLogs(data.logs);
        }
    } catch (err) {
        console.error("Error fetching logs:", err);
    }
}

function renderLogs(logs) {
    const body = document.getElementById('terminalLogBody');
    if (!body || !logs) return;

    const autoScroll = document.getElementById('autoScrollSwitch')?.checked;

    body.innerHTML = '';
    logs.forEach(line => {
        const div = document.createElement('div');
        const lower = line.toLowerCase();
        let lineClass = 'log-line';
        if (lower.includes('error') || lower.includes('fail')) lineClass += ' error';
        else if (lower.includes('warn')) lineClass += ' warn';
        else if (lower.includes('info')) lineClass += ' info';

        div.className = lineClass;
        div.textContent = line;
        body.appendChild(div);
    });

    if (autoScroll) {
        body.scrollTop = body.scrollHeight;
    }
}

function setupLogControls() {
    document.getElementById('btnClearLogs')?.addEventListener('click', () => {
        const body = document.getElementById('terminalLogBody');
        if (body) body.innerHTML = '<div class="text-muted italic">Log buffer cleared by operator.</div>';
    });

    document.getElementById('btnRefreshLogs')?.addEventListener('click', () => {
        fetchLatestLogs();
    });

    // Auto-poll logs every 3 seconds
    logPollingInterval = setInterval(fetchLatestLogs, 3000);
}

// SignalR Room Listener
function setupDetailsSignalR() {
    const cid = window.CURRENT_CONTAINER_ID;
    if (!cid) return;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/containers")
        .withAutomaticReconnect()
        .build();

    connection.on("ReceiveContainerMetrics", (metric) => {
        if (metric.containerId === cid) {
            pushContainerMetric(metric);
        }
    });

    connection.start()
        .then(() => {
            connection.invoke("JoinContainerRoom", cid);
            console.log(`Subscribed to telemetry room for container ${cid}`);
        })
        .catch(err => console.error("SignalR Connection Error:", err));
}

document.addEventListener('DOMContentLoaded', () => {
    setupTabs();
    setupLogControls();
    setupDetailsSignalR();
});
