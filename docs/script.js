// Fleptix Marketing Site — Client Interactivity

document.addEventListener('DOMContentLoaded', () => {
  // Command Snippets (Single Source of Truth for both copying & display)
  const snippets = {
    dockerrun: {
      text: `docker rm -f fleptix-observer 2>/dev/null; docker run -d \\
  --name fleptix-observer \\
  -p 7373:80 \\
  -v /var/run/docker.sock:/var/run/docker.sock \\
  -v fleptix-data:/app/snapshots \\
  --restart unless-stopped \\
  ghcr.io/fleptor/fleptix:latest`,
      html: `<span class="code-comment"># Stop & replace any existing container, then run Fleptix Observer</span>
<span class="code-keyword">docker</span> rm <span class="code-arg">-f</span> <span class="code-val">fleptix-observer</span> 2&gt;/dev/null; <span class="code-keyword">docker</span> run <span class="code-arg">-d</span> \\
  <span class="code-arg">--name</span> <span class="code-val">fleptix-observer</span> \\
  <span class="code-arg">-p</span> <span class="code-val">7373:80</span> \\
  <span class="code-arg">-v</span> <span class="code-val">/var/run/docker.sock:/var/run/docker.sock</span> \\
  <span class="code-arg">-v</span> <span class="code-val">fleptix-data:/app/snapshots</span> \\
  <span class="code-arg">--restart</span> <span class="code-val">unless-stopped</span> \\
  <span class="code-val">ghcr.io/fleptor/fleptix:latest</span>`
    },

    compose: {
      text: `services:
  fleptix-observer:
    image: ghcr.io/fleptor/fleptix:latest
    container_name: fleptix-observer
    ports:
      - "7373:80"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - fleptix-data:/app/snapshots
    restart: unless-stopped

volumes:
  fleptix-data:`,
      html: `<span class="code-comment"># docker-compose.yml (handles container replacement & volume persistence automatically)</span>
<span class="code-keyword">services:</span>
  <span class="code-keyword">fleptix-observer:</span>
    <span class="code-arg">image:</span> <span class="code-val">ghcr.io/fleptor/fleptix:latest</span>
    <span class="code-arg">container_name:</span> <span class="code-val">fleptix-observer</span>
    <span class="code-arg">ports:</span>
      - <span class="code-val">"7373:80"</span>
    <span class="code-arg">volumes:</span>
      - <span class="code-val">/var/run/docker.sock:/var/run/docker.sock</span>
      - <span class="code-val">fleptix-data:/app/snapshots</span>
    <span class="code-arg">restart:</span> <span class="code-val">unless-stopped</span>

<span class="code-keyword">volumes:</span>
  <span class="code-keyword">fleptix-data:</span>`
    },

    script: {
      text: `curl -fsSL https://raw.githubusercontent.com/fleptor/Fleptix/main/install.sh | bash`,
      html: `<span class="code-comment"># One-line automated installer with environment check & container replacement</span>
<span class="code-keyword">curl</span> <span class="code-arg">-fsSL</span> <span class="code-val">https://raw.githubusercontent.com/fleptor/Fleptix/main/install.sh</span> | <span class="code-keyword">bash</span>`
    }
  };

  const activeBtn = document.querySelector('.install-tab-btn.active');
  let activeTab = activeBtn ? activeBtn.getAttribute('data-tab') : 'script';

  // Tab Switching
  const tabButtons = document.querySelectorAll('.install-tab-btn');
  const codeDisplay = document.getElementById('installCodeBlock');

  function renderCode() {
    if (!codeDisplay || !snippets[activeTab]) return;
    codeDisplay.innerHTML = snippets[activeTab].html;
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
      const entry = snippets[activeTab];
      const textToCopy = entry ? entry.text : '';
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
