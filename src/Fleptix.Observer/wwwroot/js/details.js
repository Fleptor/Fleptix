// Fleptix Observer — Container Details Controller & Log Terminal

function pushContainerMetric(metric) {
    if (!metric) return;

    // Update Telemetry Strip
    const liveTelemetry = document.getElementById('detailsLiveTelemetry');
    if (liveTelemetry) {
        liveTelemetry.textContent = `CPU: ${metric.cpuPercentage.toFixed(1)}% | RAM: ${metric.memoryUsageMb.toFixed(1)} MB`;
    }

    // Update CPU Value
    const cpuVal = document.getElementById('detailsCpuVal');
    if (cpuVal) {
        cpuVal.textContent = `${metric.cpuPercentage.toFixed(1)}%`;
    }

    // Update Memory Value and Bar
    const memVal = document.getElementById('detailsMemVal');
    if (memVal) {
        memVal.textContent = `${metric.memoryUsageMb.toFixed(0)} MB`;
    }
    const memBar = document.getElementById('detailsMemBar');
    if (memBar && metric.memoryPercentage) {
        memBar.style.width = `${Math.min(100, Math.max(5, metric.memoryPercentage))}%`;
    }

    // Update Network I/O
    const netRx = document.getElementById('detailsNetRx');
    if (netRx && metric.networkRxBytes != null) {
        netRx.textContent = `${(metric.networkRxBytes / (1024 * 1024)).toFixed(1)} MB`;
    }
    const netTx = document.getElementById('detailsNetTx');
    if (netTx && metric.networkTxBytes != null) {
        netTx.textContent = `${(metric.networkTxBytes / (1024 * 1024)).toFixed(1)} MB`;
    }

    // Update Block / Disk I/O
    const diskRead = document.getElementById('detailsDiskRead');
    if (diskRead && metric.blockReadMb != null) {
        diskRead.textContent = `${metric.blockReadMb.toFixed(1)} MB`;
    }
    const diskWrite = document.getElementById('detailsDiskWrite');
    if (diskWrite && metric.blockWriteMb != null) {
        diskWrite.textContent = `${metric.blockWriteMb.toFixed(1)} MB`;
    }
    const diskTotal = document.getElementById('detailsDiskTotal');
    if (diskTotal && metric.blockReadMb != null && metric.blockWriteMb != null) {
        diskTotal.textContent = (metric.blockReadMb + metric.blockWriteMb).toFixed(1);
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
                if (typeof window.updateExecContainerState === 'function') {
                    window.updateExecContainerState(data.newState);
                }
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
        if (targetId === 'tabExec' && typeof window.initExecConsole === 'function') {
            window.initExecConsole();
            const input = document.getElementById('execCommandInput');
            if (input) setTimeout(() => input.focus(), 50);
        }
        if (targetId === 'tabSnapshots' && typeof window.initContainerAutomatedProtection === 'function') {
            window.initContainerAutomatedProtection();
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
    if (typeof window.initExecConsole === 'function') {
        window.initExecConsole();
    }
    if (typeof window.initContainerAutomatedProtection === 'function') {
        window.initContainerAutomatedProtection();
    }
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

    // Update UI headers — Current Path is the single source of truth
    const pathDisplay = document.getElementById('filesCurrentPathDisplay');
    if (pathDisplay) {
        pathDisplay.textContent = path;
        pathDisplay.title = path;
    }

    // Show loading in table
    const tableBody = document.getElementById('filesTableBody');
    if (tableBody) {
        tableBody.innerHTML = `
            <tr>
                <td colspan="6" class="text-center text-muted font-mono py-4">
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
                    <td colspan="6" class="text-danger font-mono p-3 text-center">
                        <i class="bi bi-exclamation-octagon me-1"></i> Error loading directory: ${escapeHtml(err.message)}
                    </td>
                </tr>`;
        }
    }
}

function renderFileTableRows(items) {
    const tableBody = document.getElementById('filesTableBody');
    if (!tableBody) return;
    const cid = window.CURRENT_CONTAINER_ID || '';

    let html = '';

    // Up one level row if not root
    if (currentDirectoryPath !== '/') {
        const parentPath = getParentPath(currentDirectoryPath);
        html += `
            <tr class="file-row-dir" onclick="navigateFiles('${escapeAttr(parentPath)}')">
                <td class="font-mono fw-semibold text-primary" colspan="6">
                    <i class="bi bi-arrow-90deg-up me-2 text-secondary"></i>.. <span class="text-muted fw-normal font-sans" style="font-size: 11px;">(Up to parent directory)</span>
                </td>
            </tr>`;
    }

    if (!items || items.length === 0) {
        html += `
            <tr>
                <td colspan="6" class="text-muted font-mono text-center py-4">
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
                    <td class="text-end font-mono" style="font-size: 11px;">
                        <button type="button" class="btn btn-xs btn-light border py-0 px-1 font-mono text-muted" style="font-size: 10px;" title="Open folder" onclick="navigateFiles('${escapeAttr(item.path)}')">
                            <i class="bi bi-folder2-open"></i>
                        </button>
                    </td>
                </tr>`;
        } else {
            const symlinkInfo = item.linkTarget ? ` <span class="text-muted font-mono" style="font-size: 10px;">&rarr; ${escapeHtml(item.linkTarget)}</span>` : '';
            html += `
                <tr class="file-row-item" onclick="openFilePreview('${escapeAttr(item.path)}')">
                    <td class="font-mono text-dark d-flex align-items-center gap-2">
                        ${icon}
                        <span class="text-decoration-underline text-dark fw-medium">${escapeHtml(item.name)}</span>${symlinkInfo}
                    </td>
                    <td class="font-mono text-muted" style="font-size: 11px;">${escapeHtml(item.type || 'file')}</td>
                    <td style="text-align: right;">${formattedSize}</td>
                    <td>${modeStr}</td>
                    <td class="font-mono text-muted" style="font-size: 11px;">${dateStr}</td>
                    <td class="text-end font-mono" style="font-size: 11px;">
                        <button type="button" class="btn btn-xs btn-light border py-0 px-1 font-mono text-dark" title="View file contents" onclick="event.stopPropagation(); openFilePreview('${escapeAttr(item.path)}')">
                            <i class="bi bi-eye"></i>
                        </button>
                        <a class="btn btn-xs btn-light border py-0 px-1 font-mono text-dark ms-1" title="Download file" href="/api/containers/${encodeURIComponent(cid)}/file/download?path=${encodeURIComponent(item.path)}" download onclick="event.stopPropagation();">
                            <i class="bi bi-download"></i>
                        </a>
                    </td>
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
            <div class="file-tree-row" onclick="handleTreeNodeClick('${escapeAttr(path)}', event)" title="${escapeAttr(name)}">
                <span class="file-tree-toggle" onclick="handleTreeToggleClick('${escapeAttr(path)}', event)">
                    <i class="bi bi-chevron-right"></i>
                </span>
                <i class="bi bi-folder-fill text-warning flex-shrink-0"></i>
                <span class="text-truncate">${escapeHtml(name)}</span>
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

// ==========================================
// File Viewer & Download Modal Controller
// ==========================================
window.openFilePreview = async function(filePath) {
    const cid = window.CURRENT_CONTAINER_ID;
    if (!cid || !filePath) return;

    const modalEl = document.getElementById('filePreviewModal');
    if (!modalEl) return;

    const label = document.getElementById('filePreviewModalLabel');
    const sizeBadge = document.getElementById('filePreviewSizeBadge');
    const downloadLink = document.getElementById('btnDownloadFileLink');
    const binaryDownloadLink = document.getElementById('btnDownloadBinaryLink');
    const loading = document.getElementById('filePreviewLoading');
    const contentBox = document.getElementById('filePreviewContentContainer');
    const binaryBox = document.getElementById('filePreviewBinaryContainer');
    const errorBox = document.getElementById('filePreviewErrorContainer');
    const pre = document.getElementById('filePreviewPre');

    if (label) label.textContent = filePath;
    if (sizeBadge) sizeBadge.textContent = 'loading...';

    const downloadUrl = `/api/containers/${encodeURIComponent(cid)}/file/download?path=${encodeURIComponent(filePath)}`;
    if (downloadLink) downloadLink.href = downloadUrl;
    if (binaryDownloadLink) binaryDownloadLink.href = downloadUrl;

    if (loading) loading.classList.remove('d-none');
    if (contentBox) contentBox.classList.add('d-none');
    if (binaryBox) binaryBox.classList.add('d-none');
    if (errorBox) errorBox.classList.add('d-none');

    if (typeof bootstrap !== 'undefined' && bootstrap.Modal) {
        const bsModal = bootstrap.Modal.getOrCreateInstance(modalEl);
        bsModal.show();
    }

    try {
        const res = await fetch(`/api/containers/${encodeURIComponent(cid)}/file/content?path=${encodeURIComponent(filePath)}`);
        const data = await res.json();

        if (loading) loading.classList.add('d-none');

        if (!res.ok || !data.success) {
            if (errorBox) {
                const errText = document.getElementById('filePreviewErrorText');
                if (errText) errText.textContent = data.errorMessage || `HTTP Error ${res.status}`;
                errorBox.classList.remove('d-none');
            }
            if (sizeBadge) sizeBadge.textContent = 'error';
            return;
        }

        if (sizeBadge) sizeBadge.textContent = formatFileSize(data.size);

        if (data.isText) {
            if (pre) pre.textContent = data.contentText || '(empty file)';
            if (contentBox) contentBox.classList.remove('d-none');
        } else {
            if (binaryBox) binaryBox.classList.remove('d-none');
        }
    } catch (err) {
        if (loading) loading.classList.add('d-none');
        if (errorBox) {
            const errText = document.getElementById('filePreviewErrorText');
            if (errText) errText.textContent = err.message;
            errorBox.classList.remove('d-none');
        }
    }
};

window.copyCurrentFileContent = function() {
    const pre = document.getElementById('filePreviewPre');
    if (!pre) return;
    const text = pre.textContent || '';
    if (!text) {
        window.showToast("No content to copy.", true);
        return;
    }
    navigator.clipboard.writeText(text)
        .then(() => window.showToast("File content copied to clipboard!"))
        .catch(() => {
            copyFallback(text);
        });
};

// ==========================================
// Container Exec & Interactive Console Controller
// ==========================================
let execHistory = [];
let execHistoryIndex = -1;
let execCurrentDraft = '';
let isExecRunning = false;
let execConsoleInitialized = false;

window.initExecConsole = function() {
    if (execConsoleInitialized) return;
    execConsoleInitialized = true;

    const cmdInput = document.getElementById('execCommandInput');
    const workDirInput = document.getElementById('execWorkDirInput');
    const userInput = document.getElementById('execUserInput');

    if (workDirInput) {
        workDirInput.addEventListener('input', updateExecPromptPrefix);
    }
    if (userInput) {
        userInput.addEventListener('input', updateExecPromptPrefix);
    }

    if (cmdInput) {
        cmdInput.addEventListener('keydown', handleExecKeyDown);
    }

    updateExecPromptPrefix();
};

function updateExecPromptPrefix() {
    const prefixEl = document.getElementById('execPromptPrefix');
    if (!prefixEl) return;

    const user = (document.getElementById('execUserInput')?.value || 'root').trim() || 'root';
    const shortId = window.CURRENT_CONTAINER_SHORT_ID || (window.CURRENT_CONTAINER_ID ? window.CURRENT_CONTAINER_ID.substring(0, 12) : 'container');
    let dir = (document.getElementById('execWorkDirInput')?.value || '/').trim() || '/';
    if (!dir.startsWith('/')) dir = '/' + dir;

    prefixEl.textContent = `${user}@${shortId}:${dir}#`;
}

window.updateExecContainerState = function(state) {
    const alertEl = document.getElementById('execStateAlert');
    const stateText = document.getElementById('execContainerStateText');
    if (stateText) stateText.textContent = state;
    if (alertEl) {
        if (state === 'running') {
            alertEl.classList.add('d-none');
        } else {
            alertEl.classList.remove('d-none');
        }
    }
    window.CURRENT_CONTAINER_STATE = state;
};

function handleExecKeyDown(e) {
    const input = e.target;

    // Up Arrow: History backward
    if (e.key === 'ArrowUp') {
        if (execHistory.length === 0) return;
        e.preventDefault();

        if (execHistoryIndex === -1) {
            execCurrentDraft = input.value;
        }

        if (execHistoryIndex < execHistory.length - 1) {
            execHistoryIndex++;
            input.value = execHistory[execHistory.length - 1 - execHistoryIndex];
        }
    }
    // Down Arrow: History forward
    else if (e.key === 'ArrowDown') {
        if (execHistoryIndex === -1) return;
        e.preventDefault();

        if (execHistoryIndex > 0) {
            execHistoryIndex--;
            input.value = execHistory[execHistory.length - 1 - execHistoryIndex];
        } else {
            execHistoryIndex = -1;
            input.value = execCurrentDraft;
        }
    }
    // Ctrl + L: Clear console
    else if ((e.ctrlKey || e.metaKey) && e.key === 'l') {
        e.preventDefault();
        window.clearExecTerminal();
    }
}

window.insertExecPreset = function(command) {
    const input = document.getElementById('execCommandInput');
    if (input) {
        input.value = command;
        input.focus();
    }
    window.executeContainerCommand(command);
};

window.handleExecSubmit = function(event) {
    if (event) event.preventDefault();

    const input = document.getElementById('execCommandInput');
    if (!input) return;

    const cmd = input.value.trim();
    if (!cmd) return;

    // Save to history (avoid duplicates at top)
    if (execHistory.length === 0 || execHistory[execHistory.length - 1] !== cmd) {
        execHistory.push(cmd);
        if (execHistory.length > 50) execHistory.shift();
    }
    execHistoryIndex = -1;
    execCurrentDraft = '';

    input.value = '';
    window.executeContainerCommand(cmd);
};

window.executeContainerCommand = async function(cmd) {
    const cid = window.CURRENT_CONTAINER_ID;
    if (!cid || !cmd) return;

    if (isExecRunning) {
        window.showToast("Another command is currently executing in this container.", true);
        return;
    }

    const entriesContainer = document.getElementById('execTerminalEntries');
    const placeholder = document.getElementById('execTerminalPlaceholder');
    if (placeholder) placeholder.remove();

    const shell = document.getElementById('execShellSelect')?.value || '/bin/sh';
    const workingDir = document.getElementById('execWorkDirInput')?.value?.trim() || '/';
    const user = document.getElementById('execUserInput')?.value?.trim() || 'root';
    const shortId = window.CURRENT_CONTAINER_SHORT_ID || cid.substring(0, 12);
    const autoScroll = document.getElementById('execAutoScrollSwitch')?.checked;

    // Expand viewport from compact idle to active
    const viewport = document.getElementById('execTerminalViewport');
    if (viewport) {
        viewport.classList.remove('exec-terminal-idle');
        viewport.classList.add('exec-terminal-active');
    }

    // Create execution block in viewport
    const execId = 'exec-' + Date.now();
    const timeStr = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });

    const entryDiv = document.createElement('div');
    entryDiv.className = 'exec-entry mb-2 pb-2 border-bottom border-secondary border-opacity-10';
    entryDiv.id = execId;
    entryDiv.innerHTML = `
        <div class="d-flex align-items-center justify-content-between font-mono mb-1" style="font-size: 11px;">
            <div class="d-flex align-items-center gap-1.5 overflow-x-auto text-truncate">
                <span style="color: #94a3b8;">${escapeHtml(user)}@${escapeHtml(shortId)}:${escapeHtml(workingDir)}#</span>
                <span style="color: #f8fafc; font-weight: 600;">${escapeHtml(cmd)}</span>
            </div>
            <div class="d-flex align-items-center gap-1.5 ms-2 flex-shrink-0" style="font-size: 10px;">
                <span style="color: #64748b;">${timeStr}</span>
                <span id="badge-${execId}" class="badge font-mono d-inline-flex align-items-center gap-1" style="background-color: #1e293b; color: #94a3b8; border: 1px solid #334155; font-size: 9px; padding: 1px 5px;">
                    <span class="spinner-border spinner-border-sm" style="width: 7px; height: 7px;" role="status"></span>
                    <span>running</span>
                </span>
            </div>
        </div>
        <div id="out-${execId}" class="exec-output font-mono ps-1" style="font-size: 11px; line-height: 1.45;">
            <span style="color: #64748b; font-style: italic; font-size: 10px;">Executing via ${escapeHtml(shell)}...</span>
        </div>
    `;

    if (entriesContainer) {
        entriesContainer.appendChild(entryDiv);
    }

    if (autoScroll) {
        if (viewport) viewport.scrollTop = viewport.scrollHeight;
    }

    // Toggle running state
    setExecRunningState(true);

    try {
        const response = await fetch(`/api/containers/${encodeURIComponent(cid)}/exec`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                command: cmd,
                shell: shell,
                workingDir: workingDir,
                user: user
            })
        });

        const outContainer = document.getElementById(`out-${execId}`);
        const badge = document.getElementById(`badge-${execId}`);

        if (!response.ok) {
            const errData = await response.json().catch(() => ({}));
            const msg = errData.errorMessage || `HTTP Error ${response.status}`;
            if (badge) {
                badge.className = 'badge font-mono';
                badge.style.cssText = 'background-color: #7f1d1d; color: #fca5a5; border: 1px solid #991b1b; font-size: 9px; padding: 1px 5px;';
                badge.textContent = `error`;
            }
            if (outContainer) {
                outContainer.innerHTML = `<div style="color: #f87171; font-size: 11px;">${escapeHtml(msg)}</div>`;
            }
            return;
        }

        const data = await response.json();

        // Update badge with exit status and duration (green for 0, red for nonzero)
        if (badge) {
            badge.className = 'badge font-mono';
            if (data.exitCode === 0) {
                badge.style.cssText = 'background-color: #14532d; color: #86efac; border: 1px solid #166534; font-size: 9px; padding: 1px 5px;';
                badge.textContent = `exit 0 • ${data.durationMs}ms`;
            } else {
                badge.style.cssText = 'background-color: #7f1d1d; color: #fca5a5; border: 1px solid #991b1b; font-size: 9px; padding: 1px 5px;';
                badge.textContent = `exit ${data.exitCode} • ${data.durationMs}ms`;
            }
        }

        // Render stdout and stderr using exactly the 3 purposeful colors
        if (outContainer) {
            let html = '';
            if (data.stdout && data.stdout.length > 0) {
                html += `<pre class="m-0 font-mono" style="font-size: 11px; color: #e2e8f0; white-space: pre-wrap; word-break: break-all;">${escapeHtml(data.stdout)}</pre>`;
            }
            if (data.stderr && data.stderr.length > 0) {
                html += `<pre class="m-0 font-mono mt-1" style="font-size: 11px; color: #f87171; white-space: pre-wrap; word-break: break-all;">${escapeHtml(data.stderr)}</pre>`;
            }
            if (data.errorMessage) {
                html += `<div class="font-mono mt-1" style="font-size: 11px; color: #f87171;">${escapeHtml(data.errorMessage)}</div>`;
            }
            if (!html) {
                html = `<div style="color: #64748b; font-size: 10px; font-style: italic;">(Process exited with code ${data.exitCode} and produced no output)</div>`;
            }
            outContainer.innerHTML = html;
        }
    } catch (err) {
        const outContainer = document.getElementById(`out-${execId}`);
        const badge = document.getElementById(`badge-${execId}`);
        if (badge) {
            badge.className = 'badge font-mono';
            badge.style.cssText = 'background-color: #7f1d1d; color: #fca5a5; border: 1px solid #991b1b; font-size: 9px; padding: 1px 5px;';
            badge.textContent = 'fail';
        }
        if (outContainer) {
            outContainer.innerHTML = `<div style="color: #f87171; font-size: 11px;">Network request failed: ${escapeHtml(err.message)}</div>`;
        }
    } finally {
        setExecRunningState(false);
        const autoScroll = document.getElementById('execAutoScrollSwitch')?.checked;
        if (autoScroll) {
            const viewport = document.getElementById('execTerminalViewport');
            if (viewport) viewport.scrollTop = viewport.scrollHeight;
        }
        const cmdInput = document.getElementById('execCommandInput');
        if (cmdInput) cmdInput.focus();
    }
};

function setExecRunningState(running) {
    isExecRunning = running;
    const btn = document.getElementById('btnExecRun');
    const text = document.getElementById('execRunBtnText');
    const icon = document.getElementById('execRunIcon');
    const spinner = document.getElementById('execRunSpinner');
    const input = document.getElementById('execCommandInput');

    if (btn) btn.disabled = running;
    if (input) input.disabled = running;
    if (text) text.textContent = running ? 'Running...' : 'Run';
    if (icon) {
        if (running) icon.classList.add('d-none');
        else icon.classList.remove('d-none');
    }
    if (spinner) {
        if (running) spinner.classList.remove('d-none');
        else spinner.classList.add('d-none');
    }
}

window.clearExecTerminal = function() {
    const entriesContainer = document.getElementById('execTerminalEntries');
    if (entriesContainer) {
        entriesContainer.innerHTML = `
            <div class="font-mono" id="execTerminalPlaceholder" style="font-size: 11px; color: #64748b; font-style: italic;">
                Terminal session ready. Select a preset above or type a command below.
            </div>
        `;
    }
    const viewport = document.getElementById('execTerminalViewport');
    if (viewport) {
        viewport.classList.remove('exec-terminal-active');
        viewport.classList.add('exec-terminal-idle');
    }
};

window.copyExecTerminalOutput = function() {
    const entries = document.getElementById('execTerminalEntries');
    if (!entries) return;

    const text = entries.innerText || entries.textContent || '';
    if (!text.trim()) {
        window.showToast("Terminal buffer is empty.", true);
        return;
    }

    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text)
            .then(() => window.showToast("Terminal output copied to clipboard!"))
            .catch(() => copyFallback(text));
    } else {
        copyFallback(text);
    }
};

function copyFallback(text) {
    const ta = document.createElement('textarea');
    ta.value = text;
    document.body.appendChild(ta);
    ta.select();
    try {
        document.execCommand('copy');
        window.showToast("Terminal output copied to clipboard!");
    } catch {
        window.showToast("Failed to copy output.", true);
    }
    document.body.removeChild(ta);
}

// ==========================================
// Container Automated Protection Controller
// ==========================================
let currentGlobalRetentionSettings = null;
let currentContainerOverride = null;
let currentContainerStorage = null;
let containerProtectionInitialized = false;

function formatProtectionTimeAgo(dateStr) {
    if (!dateStr) return 'Never';
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    if (isNaN(diffMs) || diffMs < 0) return 'just now';
    const diffSec = Math.floor(diffMs / 1000);
    if (diffSec < 60) return 'just now';
    const diffMin = Math.floor(diffSec / 60);
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHours = Math.floor(diffMin / 60);
    if (diffHours < 24) return `${diffHours}h ago`;
    const diffDays = Math.floor(diffHours / 24);
    return `${diffDays}d ago`;
}

function updateContainerEffectivePolicyDisplay() {
    const policyEl = document.getElementById('textEffectivePolicy');
    const badgeEl = document.getElementById('badgeEffectiveStatus');
    const iconEl = document.getElementById('iconProtectionStatus');
    if (!policyEl || !currentGlobalRetentionSettings || !currentContainerOverride) return;

    const overrideState = currentContainerOverride.override || 'UseGlobalDefault';
    const isAutoEnabled = currentContainerOverride.isAutoSnapshotEnabled;
    const global = currentGlobalRetentionSettings;

    if (overrideState === 'AlwaysOff') {
        if (badgeEl) {
            badgeEl.className = 'status-pill stopped';
            badgeEl.innerHTML = '<span class="pill-dot"></span><span class="pill-text">Disabled</span>';
        }
        if (iconEl) iconEl.style.color = '#94a3b8';
        policyEl.textContent = 'Disabled · container override is set to Always off';
        return;
    }

    if (overrideState === 'UseGlobalDefault' && !global.autoSnapshotEnabled) {
        if (badgeEl) {
            badgeEl.className = 'status-pill stopped';
            badgeEl.innerHTML = '<span class="pill-dot"></span><span class="pill-text">Disabled</span>';
        }
        if (iconEl) iconEl.style.color = '#94a3b8';
        policyEl.textContent = 'Disabled · global automated snapshots are turned off in Settings';
        return;
    }

    // Protection is active
    if (badgeEl) {
        badgeEl.className = 'status-pill running';
        badgeEl.innerHTML = '<span class="pill-dot"></span><span class="pill-text">Active</span>';
    }
    if (iconEl) iconEl.style.color = 'var(--accent-navy)';

    const interval = global.intervalHours || 24;
    const intervalPart = `Every ${interval}h`;

    let retentionPart = 'keep all snapshots';
    if (!global.neverDeleteOldSnapshots && global.maxSnapshotCount) {
        retentionPart = `keep last ${global.maxSnapshotCount}`;
    }

    let nextRunPart = 'next run pending';
    const lastSnapTime = currentContainerStorage?.lastSnapshotTimestamp || currentContainerStorage?.lastAutoSnapshotTimestamp;
    if (lastSnapTime) {
        const lastTime = new Date(lastSnapTime).getTime();
        const nextTime = lastTime + (interval * 3600 * 1000);
        const diffMs = nextTime - Date.now();
        if (diffMs > 0) {
            const hours = diffMs / (3600 * 1000);
            if (hours >= 1) {
                nextRunPart = `next run in ${Math.round(hours)}h`;
            } else {
                const mins = Math.max(1, Math.round(diffMs / (60 * 1000)));
                nextRunPart = `next run in ${mins}m`;
            }
        } else {
            nextRunPart = 'next run due';
        }
    } else {
        nextRunPart = `next run in ${interval}h`;
    }

    policyEl.textContent = `${intervalPart} · ${retentionPart} · ${nextRunPart}`;
}

async function loadContainerProtectionData() {
    const cid = window.CURRENT_CONTAINER_ID;
    if (!cid) return;

    try {
        const [overrideRes, settingsRes, storageRes] = await Promise.all([
            fetch(`/api/containers/${encodeURIComponent(cid)}/retention/override`),
            fetch(`/api/settings/retention`),
            fetch(`/api/containers/${encodeURIComponent(cid)}/snapshots/storage`)
        ]);

        if (overrideRes.ok) {
            currentContainerOverride = await overrideRes.json();
            const state = currentContainerOverride.override || 'UseGlobalDefault';
            if (state === 'AlwaysOn') {
                const r = document.getElementById('overrideAlwaysOn');
                if (r) r.checked = true;
            } else if (state === 'AlwaysOff') {
                const r = document.getElementById('overrideAlwaysOff');
                if (r) r.checked = true;
            } else {
                const r = document.getElementById('overrideUseGlobal');
                if (r) r.checked = true;
            }
        }

        if (settingsRes.ok) {
            currentGlobalRetentionSettings = await settingsRes.json();
        }

        if (storageRes.ok) {
            currentContainerStorage = await storageRes.json();
            const storageEl = document.getElementById('statTotalStorage');
            const countEl = document.getElementById('statSnapshotCount');
            const lastSnapEl = document.getElementById('statLastAutoSnapshot');

            if (storageEl) {
                const bytes = currentContainerStorage.totalStorageBytes || 0;
                const mb = currentContainerStorage.totalStorageMB || 0;
                if (bytes === 0) {
                    storageEl.textContent = '0 MB';
                } else if (mb < 0.1) {
                    storageEl.textContent = `${(bytes / 1024).toFixed(1)} KB`;
                } else {
                    storageEl.textContent = `${mb.toFixed(1)} MB`;
                }
            }

            if (countEl) {
                const cnt = currentContainerStorage.snapshotCount ?? 0;
                countEl.textContent = `${cnt} ${cnt === 1 ? 'snapshot' : 'snapshots'}`;
            }

            if (lastSnapEl) {
                const timeAgo = formatProtectionTimeAgo(currentContainerStorage.lastSnapshotTimestamp || currentContainerStorage.lastAutoSnapshotTimestamp);
                lastSnapEl.textContent = `Last auto-snapshot: ${timeAgo}`;
            }
        }

        updateContainerEffectivePolicyDisplay();
    } catch (err) {
        console.error('Failed to load container automated protection details:', err);
    }
}

async function handleContainerOverrideChange(e) {
    const cid = window.CURRENT_CONTAINER_ID;
    if (!cid) return;

    const newState = e.target.value; // 'UseGlobalDefault', 'AlwaysOn', 'AlwaysOff'
    try {
        const res = await fetch(`/api/containers/${encodeURIComponent(cid)}/retention/override`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ overrideState: newState })
        });

        if (res.ok) {
            const data = await res.json();
            currentContainerOverride = data;
            const labelMap = {
                'UseGlobalDefault': 'Use global default',
                'AlwaysOn': 'Always on',
                'AlwaysOff': 'Always off'
            };
            if (typeof window.showToast === 'function') {
                window.showToast(`Protection override set to: ${labelMap[newState] || newState}`);
            }
            updateContainerEffectivePolicyDisplay();
        } else {
            throw new Error('Server returned ' + res.status);
        }
    } catch (err) {
        console.error('Failed to set override:', err);
        if (typeof window.showToast === 'function') {
            window.showToast('Failed to update protection override', true);
        }
    }
}

async function handlePruneNowClick() {
    const cid = window.CURRENT_CONTAINER_ID;
    if (!cid) return;

    const btn = document.getElementById('btnPruneNow');
    const text = document.getElementById('textPruneNow');
    const spinner = document.getElementById('spinnerPruneNow');
    const icon = document.getElementById('iconPruneNow');

    if (btn) btn.disabled = true;
    if (spinner) spinner.classList.remove('d-none');
    if (icon) icon.classList.add('d-none');
    if (text) text.textContent = 'Pruning...';

    try {
        const res = await fetch(`/api/containers/${encodeURIComponent(cid)}/snapshots/prune`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({})
        });

        if (res.ok) {
            const data = await res.json();
            if (data.prunedCount > 0) {
                if (typeof window.showToast === 'function') {
                    window.showToast(`Pruned ${data.prunedCount} snapshot(s), reclaimed ${data.reclaimedMB} MB.`);
                }
                setTimeout(() => window.location.reload(), 1000);
            } else {
                if (typeof window.showToast === 'function') {
                    window.showToast('Retention policy already compliant (0 snapshots pruned).');
                }
                await loadContainerProtectionData();
            }
        } else {
            throw new Error('Server returned ' + res.status);
        }
    } catch (err) {
        console.error('Prune failed:', err);
        if (typeof window.showToast === 'function') {
            window.showToast('Failed to prune snapshots', true);
        }
    } finally {
        if (btn) btn.disabled = false;
        if (spinner) spinner.classList.add('d-none');
        if (icon) icon.classList.remove('d-none');
        if (text) text.textContent = 'Prune now';
    }
}

window.initContainerAutomatedProtection = function() {
    const panel = document.getElementById('panelContainerAutomatedProtection');
    if (!panel) return;

    if (!containerProtectionInitialized) {
        containerProtectionInitialized = true;
        document.querySelectorAll('input[name="radioContainerOverride"]').forEach(radio => {
            radio.addEventListener('change', handleContainerOverrideChange);
        });

        const btnPrune = document.getElementById('btnPruneNow');
        if (btnPrune) {
            btnPrune.addEventListener('click', handlePruneNowClick);
        }
    }

    loadContainerProtectionData();
};

