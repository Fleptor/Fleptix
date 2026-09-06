// Fleptix Marketing Site — Client Interactivity

document.addEventListener('DOMContentLoaded', () => {
  // Command Snippets
  const snippets = {
    dockerrun: `docker run -d \\
  --name fleptix-observer \\
  -p 5000:80 \\
  -v /var/run/docker.sock:/var/run/docker.sock:ro \\
  --restart unless-stopped \\
  ghcr.io/fleptix/observer:latest`,

    compose: `services:
  fleptix-observer:
    image: ghcr.io/fleptix/observer:latest
    container_name: fleptix-observer
    ports:
      - "5000:80"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
    restart: unless-stopped`
  };

  let activeTab = 'dockerrun';

  // Tab Switching
  const tabButtons = document.querySelectorAll('.install-tab-btn');
  const codeDisplay = document.getElementById('installCodeBlock');

  function renderCode() {
    if (!codeDisplay) return;
    if (activeTab === 'dockerrun') {
      codeDisplay.innerHTML = `<span class="code-comment"># Pull & run Fleptix Observer via Docker CLI</span>
<span class="code-keyword">docker</span> run <span class="code-arg">-d</span> \\
  <span class="code-arg">--name</span> <span class="code-val">fleptix-observer</span> \\
  <span class="code-arg">-p</span> <span class="code-val">5000:80</span> \\
  <span class="code-arg">-v</span> <span class="code-val">/var/run/docker.sock:/var/run/docker.sock:ro</span> \\
  <span class="code-arg">--restart</span> <span class="code-val">unless-stopped</span> \\
  <span class="code-val">ghcr.io/fleptix/observer:latest</span>`;
    } else {
      codeDisplay.innerHTML = `<span class="code-comment"># docker-compose.yml snippet</span>
<span class="code-keyword">services:</span>
  <span class="code-keyword">fleptix-observer:</span>
    <span class="code-arg">image:</span> <span class="code-val">ghcr.io/fleptix/observer:latest</span>
    <span class="code-arg">container_name:</span> <span class="code-val">fleptix-observer</span>
    <span class="code-arg">ports:</span>
      - <span class="code-val">"5000:80"</span>
    <span class="code-arg">volumes:</span>
      - <span class="code-val">/var/run/docker.sock:/var/run/docker.sock:ro</span>
    <span class="code-arg">restart:</span> <span class="code-val">unless-stopped</span>`;
    }
  }

  tabButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      tabButtons.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      activeTab = btn.getAttribute('data-tab');
      renderCode();
    });
  });

  // Copy to Clipboard
  const copyBtn = document.getElementById('copyInstallBtn');
  const toast = document.getElementById('toastMsg');
  let toastTimeout = null;

  function showToast(message) {
    if (!toast) return;
    const textEl = toast.querySelector('.toast-text') || toast;
    textEl.textContent = message;
    toast.classList.add('show');
    if (toastTimeout) clearTimeout(toastTimeout);
    toastTimeout = setTimeout(() => {
      toast.classList.remove('show');
    }, 2800);
  }

  if (copyBtn) {
    copyBtn.addEventListener('click', async () => {
      const textToCopy = snippets[activeTab];
      try {
        await navigator.clipboard.writeText(textToCopy);
        const originalText = copyBtn.innerHTML;
        copyBtn.innerHTML = `<i class="bi bi-check2"></i> Copied!`;
        showToast('Command copied to clipboard!');
        setTimeout(() => {
          copyBtn.innerHTML = originalText;
        }, 2200);
      } catch (err) {
        // Fallback for older browsers
        const ta = document.createElement('textarea');
        ta.value = textToCopy;
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
        showToast('Command copied to clipboard!');
      }
    });
  }

  renderCode();
});
