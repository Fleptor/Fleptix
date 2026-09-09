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

    function activateTab(btn) {
        tabButtons.forEach(b => {
            b.classList.remove('active', 'btn-light', 'border', 'fw-semibold');
            b.classList.add('btn-link', 'text-dark');
        });
        btn.classList.remove('btn-link', 'text-dark');
        btn.classList.add('active', 'btn-light', 'border', 'fw-semibold');

        const targetId = btn.getAttribute('data-tab');
        document.querySelectorAll('.tab-pane-content').forEach(p => p.classList.add('d-none'));
        const targetEl = document.getElementById(targetId);
        if (targetEl) targetEl.classList.remove('d-none');

        if (targetId === 'tabFiles' && typeof window.initFileBrowser === 'function') {
            window.initFileBrowser();
        }
    }

    tabButtons.forEach(btn => {
        btn.addEventListener('click', () => activateTab(btn));
    });

    const hash = window.location.hash.replace('#', '');
    if (hash) {
        const matchingBtn = document.querySelector(`.details-tab-link[data-tab="${hash}"]`) ||
                            document.querySelector(`.details-tab-link[data-tab="tab${hash.charAt(0).toUpperCase() + hash.slice(1)}"]`);
        if (matchingBtn) activateTab(matchingBtn);
    }
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

// ==========================================
// Container Filesystem Browser Controller
// ==========================================
let fileBrowserInitialized = false;
let currentDirectoryPath = '/';
let currentDirectoryItems = [];

window.initFileBrowser = function() {
    if (fileBrowserInitialized) return;
    fileBrowserInitialized = true;
    window.navigateFiles('/');
};

window.navigateFiles = function(path) {
    loadDirectory(path);
};

window.refreshCurrentFolder = function() {
    loadDirectory(currentDirectoryPath, { forceRefresh: true });
};

window.filterFileTable = function(query) {
    const q = (query || '').toLowerCase().trim();
    if (!q) {
        renderFileTableRows(currentDirectoryItems);
        return;
    }
    const filtered = currentDirectoryItems.filter(item => item.name.toLowerCase().includes(q));
    renderFileTableRows(filtered);
};

async function fetchDirectoryContents(path) {
    const cid = window.CURRENT_CONTAINER_ID;
    if (!cid) return [];

    const normPath = (path || '/').trim();
    const url = `/api/containers/${encodeURIComponent(cid)}/files?path=${encodeURIComponent(normPath)}`;
    const res = await fetch(url);
    if (!res.ok) {
        throw new Error(`Failed to read path: HTTP ${res.status}`);
    }
    const data = await res.json();
    return data.items || [];
}

async function loadDirectory(path, options = {}) {
    path = path || '/';
    if (!path.startsWith('/')) path = '/' + path;
    if (path.length > 1 && path.endsWith('/')) path = path.slice(0, -1);

    currentDirectoryPath = path;

    // Update UI headers & breadcrumbs
    const pathDisplay = document.getElementById('filesCurrentPathDisplay');
    if (pathDisplay) pathDisplay.textContent = path;

    const treeStatus = document.getElementById('filesTreeStatus');
    if (treeStatus) treeStatus.textContent = path;

    updateBreadcrumbs(path);

    // Show loading in table
    const tableBody = document.getElementById('filesTableBody');
    if (tableBody) {
        tableBody.innerHTML = `
            <tr>
                <td colspan="5" class="text-center text-muted font-mono py-4">
                    <span class="spinner-border spinner-border-sm me-1" style="width: 12px; height: 12px;"></span> Loading entries for <code class="text-dark">${escapeHtml(path)}</code>...
                </td>
            </tr>`;
    }

    // Clear filter input
    const filterInput = document.getElementById('filesSearchInput');
    if (filterInput) filterInput.value = '';

    try {
        const items = await fetchDirectoryContents(path);
        currentDirectoryItems = items;
        renderFileTableRows(items);

        // Update itemCount
        const countDisplay = document.getElementById('filesItemCountDisplay');
        if (countDisplay) {
            const dirs = items.filter(i => i.isDirectory).length;
            const files = items.length - dirs;
            countDisplay.textContent = `${items.length} items (${dirs} folders, ${files} files)`;
        }

        // Build or sync tree node
        ensureTreeNode(path, items);
        highlightActiveTreeNode(path);
    } catch (err) {
        if (tableBody) {
            tableBody.innerHTML = `
                <tr>
                    <td colspan="5" class="text-danger font-mono p-3 text-center">
                        <i class="bi bi-exclamation-octagon me-1"></i> Error loading directory: ${escapeHtml(err.message)}
                    </td>
                </tr>`;
        }
    }
}

function updateBreadcrumbs(path) {
    const container = document.getElementById('filesBreadcrumbs');
    if (!container) return;

    let html = `
        <button class="btn btn-sm ${path === '/' ? 'btn-secondary text-white' : 'btn-light border'} py-0 px-2 font-mono" style="font-size: 11px;" onclick="navigateFiles('/')" title="Root Directory">
            <i class="bi bi-hdd-network me-1"></i>/
        </button>`;

    if (path !== '/') {
        const parts = path.split('/').filter(p => p.length > 0);
        let accumulated = '';
        parts.forEach((p, idx) => {
            accumulated += '/' + p;
            const isLast = idx === parts.length - 1;
            html += `
                <span class="text-muted">/</span>
                <button class="btn btn-sm ${isLast ? 'btn-secondary text-white fw-bold' : 'btn-light border'} py-0 px-2 font-mono" style="font-size: 11px;" onclick="navigateFiles('${escapeAttr(accumulated)}')">
                    ${escapeHtml(p)}
                </button>`;
        });
    }

    container.innerHTML = html;
}

function renderFileTableRows(items) {
    const tableBody = document.getElementById('filesTableBody');
    if (!tableBody) return;

    let html = '';

    // Up one level row if not root
    if (currentDirectoryPath !== '/') {
        const parentPath = getParentPath(currentDirectoryPath);
        html += `
            <tr class="file-row-dir" onclick="navigateFiles('${escapeAttr(parentPath)}')">
                <td class="font-mono fw-semibold text-primary" colspan="5">
                    <i class="bi bi-arrow-90deg-up me-2 text-secondary"></i>.. <span class="text-muted fw-normal font-sans" style="font-size: 11px;">(Up to parent directory)</span>
                </td>
            </tr>`;
    }

    if (!items || items.length === 0) {
        html += `
            <tr>
                <td colspan="5" class="text-muted font-mono text-center py-4">
                    <i class="bi bi-folder2-open d-block fs-3 mb-1 text-secondary"></i>
                    Directory is empty.
                </td>
            </tr>`;
        tableBody.innerHTML = html;
        return;
    }

    items.forEach(item => {
        const isDir = item.isDirectory;
        const icon = getFileIcon(item);
        const formattedSize = isDir ? '<span class="text-muted font-mono">--</span>' : `<span class="font-mono">${formatFileSize(item.size)}</span>`;
        const modeStr = item.mode ? `<code class="text-secondary font-mono" style="font-size: 10px;">${escapeHtml(item.mode)}</code>` : '<span class="text-muted">--</span>';
        const dateStr = formatFileDate(item.modifiedTime);

        if (isDir) {
            html += `
                <tr class="file-row-dir" onclick="navigateFiles('${escapeAttr(item.path)}')">
                    <td class="font-mono fw-bold text-dark d-flex align-items-center gap-2">
                        ${icon}
                        <span>${escapeHtml(item.name)}</span>
                    </td>
                    <td class="font-mono text-muted" style="font-size: 11px;">directory</td>
                    <td style="text-align: right;">${formattedSize}</td>
                    <td>${modeStr}</td>
                    <td class="font-mono text-muted" style="font-size: 11px;">${dateStr}</td>
                </tr>`;
        } else {
            const symlinkInfo = item.linkTarget ? ` <span class="text-muted font-mono" style="font-size: 10px;">&rarr; ${escapeHtml(item.linkTarget)}</span>` : '';
            html += `
                <tr>
                    <td class="font-mono text-dark d-flex align-items-center gap-2">
                        ${icon}
                        <span>${escapeHtml(item.name)}</span>${symlinkInfo}
                    </td>
                    <td class="font-mono text-muted" style="font-size: 11px;">${escapeHtml(item.type || 'file')}</td>
                    <td style="text-align: right;">${formattedSize}</td>
                    <td>${modeStr}</td>
                    <td class="font-mono text-muted" style="font-size: 11px;">${dateStr}</td>
                </tr>`;
        }
    });

    tableBody.innerHTML = html;
}

function getFileIcon(item) {
    if (item.isDirectory) {
        return '<i class="bi bi-folder-fill text-warning" style="font-size: 13px;"></i>';
    }
    if (item.type === 'symlink') {
        return '<i class="bi bi-link-45deg text-primary" style="font-size: 13px;"></i>';
    }
    const name = (item.name || '').toLowerCase();
    if (name.endsWith('.sh') || name.endsWith('.py') || name.endsWith('.js') || name.endsWith('.cs') || name.endsWith('.json') || name.endsWith('.yml') || name.endsWith('.yaml') || name.endsWith('.xml') || name.endsWith('.conf') || name.endsWith('.config')) {
        return '<i class="bi bi-file-earmark-code text-info" style="font-size: 13px;"></i>';
    }
    if (name.endsWith('.log') || name.endsWith('.txt') || name.endsWith('.md')) {
        return '<i class="bi bi-file-earmark-text text-secondary" style="font-size: 13px;"></i>';
    }
    if (name.endsWith('.tar') || name.endsWith('.gz') || name.endsWith('.dll') || name.endsWith('.so') || name.endsWith('.bin')) {
        return '<i class="bi bi-file-earmark-binary text-secondary" style="font-size: 13px;"></i>';
    }
    return '<i class="bi bi-file-earmark text-secondary" style="font-size: 13px;"></i>';
}

function formatFileSize(bytes) {
    if (bytes === null || bytes === undefined || bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

function formatFileDate(dateStr) {
    if (!dateStr) return '--';
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return '--';
    return d.toISOString().replace('T', ' ').substring(0, 16);
}

function getParentPath(path) {
    if (!path || path === '/') return '/';
    const trimmed = path.replace(/\/+$/, '');
    const lastSlash = trimmed.lastIndexOf('/');
    if (lastSlash <= 0) return '/';
    return trimmed.substring(0, lastSlash);
}

function escapeHtml(str) {
    if (!str) return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function escapeAttr(str) {
    if (!str) return '';
    return String(str).replace(/'/g, "\\'");
}

// Tree Rendering & Lazy Loading
function ensureTreeNode(path, currentItems) {
    const treeRoot = document.getElementById('filesTreeRoot');
    if (!treeRoot) return;

    if (path === '/') {
        if (!treeRoot.querySelector('[data-path="/"]')) {
            const dirItems = (currentItems || []).filter(i => i.isDirectory);
            let childHtml = '';
            dirItems.forEach(d => {
                childHtml += createTreeNodeHtml(d.path, d.name);
            });

            treeRoot.innerHTML = `
                <div class="file-tree-node" data-path="/">
                    <div class="file-tree-row" onclick="handleTreeNodeClick('/', event)">
                        <span class="file-tree-toggle" onclick="handleTreeToggleClick('/', event)"><i class="bi bi-chevron-down"></i></span>
                        <i class="bi bi-hdd-network text-secondary"></i>
                        <span class="fw-semibold">/ (root)</span>
                    </div>
                    <div class="file-tree-children" id="treeChildren_root" data-loaded="true">
                        ${childHtml}
                    </div>
                </div>`;
        }
    } else {
        const node = treeRoot.querySelector(`.file-tree-node[data-path="${path}"]`);
        if (node) {
            const childrenContainer = node.querySelector('.file-tree-children');
            if (childrenContainer && (!childrenContainer.dataset.loaded || childrenContainer.dataset.loaded !== "true")) {
                const dirItems = (currentItems || []).filter(i => i.isDirectory);
                let childHtml = '';
                dirItems.forEach(d => {
                    childHtml += createTreeNodeHtml(d.path, d.name);
                });
                childrenContainer.innerHTML = childHtml || '<div class="text-muted font-mono" style="font-size: 10px; padding: 2px 4px;">(no subfolders)</div>';
                childrenContainer.dataset.loaded = "true";
                childrenContainer.classList.remove('d-none');
                const toggle = node.querySelector('.file-tree-toggle');
                if (toggle) toggle.innerHTML = '<i class="bi bi-chevron-down"></i>';
            }
        }
    }
}

function createTreeNodeHtml(path, name) {
    return `
        <div class="file-tree-node" data-path="${escapeHtml(path)}">
            <div class="file-tree-row" onclick="handleTreeNodeClick('${escapeAttr(path)}', event)">
                <span class="file-tree-toggle" onclick="handleTreeToggleClick('${escapeAttr(path)}', event)">
                    <i class="bi bi-chevron-right"></i>
                </span>
                <i class="bi bi-folder-fill text-warning"></i>
                <span>${escapeHtml(name)}</span>
            </div>
            <div class="file-tree-children d-none" data-loaded="false"></div>
        </div>`;
}

window.handleTreeNodeClick = function(path, event) {
    if (event) event.stopPropagation();
    loadDirectory(path);
};

window.handleTreeToggleClick = async function(path, event) {
    if (event) event.stopPropagation();

    const node = document.querySelector(`.file-tree-node[data-path="${path}"]`);
    if (!node) return;

    const childrenContainer = node.querySelector('.file-tree-children');
    const toggleBtn = node.querySelector('.file-tree-toggle');
    if (!childrenContainer || !toggleBtn) return;

    const isCurrentlyOpen = !childrenContainer.classList.contains('d-none');

    if (isCurrentlyOpen) {
        childrenContainer.classList.add('d-none');
        toggleBtn.innerHTML = '<i class="bi bi-chevron-right"></i>';
    } else {
        if (childrenContainer.dataset.loaded !== "true") {
            toggleBtn.innerHTML = '<span class="spinner-border spinner-border-sm" style="width: 8px; height: 8px;"></span>';
            try {
                const items = await fetchDirectoryContents(path);
                const dirItems = items.filter(i => i.isDirectory);
                let html = '';
                dirItems.forEach(d => {
                    html += createTreeNodeHtml(d.path, d.name);
                });
                childrenContainer.innerHTML = html || '<div class="text-muted font-mono" style="font-size: 10px; padding: 2px 4px;">(no subfolders)</div>';
                childrenContainer.dataset.loaded = "true";
            } catch (err) {
                childrenContainer.innerHTML = `<div class="text-danger font-mono" style="font-size: 10px; padding: 2px 4px;">! ${escapeHtml(err.message)}</div>`;
            }
        }
        childrenContainer.classList.remove('d-none');
        toggleBtn.innerHTML = '<i class="bi bi-chevron-down"></i>';
    }
};

function highlightActiveTreeNode(path) {
    document.querySelectorAll('.file-tree-row').forEach(row => row.classList.remove('active'));
    const targetNode = document.querySelector(`.file-tree-node[data-path="${path}"]`);
    if (targetNode) {
        const row = targetNode.querySelector(':scope > .file-tree-row');
        if (row) row.classList.add('active');
    }
}
