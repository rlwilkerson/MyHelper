/* chat.js — SignalR client for MyHelper */
(function () {
  'use strict';

  // ── State ──────────────────────────────────────────────────────
  let connection = null;
  let activeSessionId = null;
  let currentAssistantEl = null;
  let activeToolCallId = null;
  let isProcessing = false;

  // ── DOM refs ───────────────────────────────────────────────────
  const feed          = document.getElementById('message-feed');
  const input         = document.getElementById('composer-input');
  const sendBtn       = document.getElementById('send-btn');
  const sendLabel     = document.getElementById('send-label');
  const abortBtn      = document.getElementById('abort-btn');
  const modelSelect   = document.getElementById('model-select');
  const sessionList   = document.getElementById('session-list');
  const newSessionBtn = document.getElementById('new-session-btn');
  const statusBadge   = document.getElementById('connection-status');
  const sessionBadge  = document.getElementById('active-session-badge');

  // ── Utilities ──────────────────────────────────────────────────
  function setStatus(state, text) {
    statusBadge.className = 'conn-badge conn-' + state;
    statusBadge.textContent = '● ' + text;
  }

  function setProcessing(on) {
    isProcessing = on;
    input.disabled = on || !activeSessionId;
    sendBtn.disabled = on || !activeSessionId;
    abortBtn.disabled = !on;
    sendBtn.classList.toggle('processing', on);
  }

  function autoResize() {
    input.style.height = 'auto';
    input.style.height = Math.min(input.scrollHeight, 192) + 'px';
  }

  function scrollToBottom() {
    feed.scrollTop = feed.scrollHeight;
  }

  // ── Message rendering ──────────────────────────────────────────
  function appendUserMessage(text) {
    const msg = document.createElement('div');
    msg.className = 'msg msg-user';
    msg.innerHTML = `<div class="msg-content">${escapeHtml(text)}</div>`;
    clearWelcome();
    feed.appendChild(msg);
    scrollToBottom();
  }

  function startAssistantMessage() {
    const msg = document.createElement('div');
    msg.className = 'msg msg-assistant';
    msg.innerHTML = '<div class="msg-content"></div>';
    clearWelcome();
    feed.appendChild(msg);
    currentAssistantEl = msg.querySelector('.msg-content');
    scrollToBottom();
    return currentAssistantEl;
  }

  function appendDelta(chunk) {
    if (!currentAssistantEl) startAssistantMessage();
    currentAssistantEl.textContent += chunk;
    scrollToBottom();
  }

  function finalizeAssistantMessage() {
    if (currentAssistantEl) {
      // Render simple markdown code fences
      renderCodeBlocks(currentAssistantEl);
      currentAssistantEl = null;
    }
  }

  function appendToolLog(toolName, done, error) {
    const el = document.createElement('div');
    const state = error ? 'tool-error' : (done ? 'tool-done' : '');
    el.className = 'tool-log ' + state;
    const icon = error ? '✗' : (done ? '✓' : '▶');
    el.innerHTML = `<span class="tool-icon">${icon}</span><code>${escapeHtml(toolName)}</code>`;
    if (!currentAssistantEl) clearWelcome();
    feed.appendChild(el);
    scrollToBottom();
    return el;
  }

  function appendError(message) {
    const el = document.createElement('div');
    el.className = 'error-banner';
    el.textContent = '⚠ ' + message;
    clearWelcome();
    feed.appendChild(el);
    scrollToBottom();
  }

  function clearWelcome() {
    const w = feed.querySelector('.welcome-msg');
    if (w) w.remove();
  }

  // Very lightweight code-fence renderer (```lang ... ```)
  function renderCodeBlocks(el) {
    const text = el.textContent;
    if (!text.includes('```')) return;
    el.innerHTML = escapeHtml(text).replace(
      /```(?:\w+)?\n([\s\S]*?)```/g,
      (_, code) => `<pre><code>${code.trim()}</code></pre>`
    );
  }

  function escapeHtml(str) {
    return str
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  // ── Session list UI ────────────────────────────────────────────
  function addSessionItem(sessionId, model) {
    // Remove existing item if present
    const existing = sessionList.querySelector(`[data-session-id="${sessionId}"]`);
    if (existing) existing.remove();

    const li = document.createElement('li');
    li.className = 'session-item';
    li.dataset.sessionId = sessionId;
    li.innerHTML = `
      <span class="session-id" title="${sessionId}">${sessionId.slice(0, 8)}…</span>
      <span class="session-model">${escapeHtml(model || '—')}</span>`;
    li.addEventListener('click', () => activateSession(sessionId));
    sessionList.appendChild(li);
    return li;
  }

  function activateSession(sessionId) {
    activeSessionId = sessionId;
    sessionBadge.textContent = sessionId.slice(0, 12) + '…';

    document.querySelectorAll('.session-item').forEach(el => {
      el.classList.toggle('active', el.dataset.sessionId === sessionId);
    });

    setProcessing(false);
    input.disabled = false;
    sendBtn.disabled = false;
    input.focus();
  }

  // ── API helpers ────────────────────────────────────────────────
  async function fetchModels() {
    try {
      const res = await fetch('/api/models');
      if (!res.ok) return;
      const data = await res.json();
      modelSelect.innerHTML = '';
      (data.models || data).forEach(m => {
        const opt = document.createElement('option');
        opt.value = m.id || m;
        opt.textContent = m.name || m.id || m;
        modelSelect.appendChild(opt);
      });
    } catch { /* swallow */ }
  }

  async function createSession() {
    const model = modelSelect.value || undefined;
    try {
      const res = await fetch('/api/sessions', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ model }),
      });
      if (!res.ok) throw new Error(await res.text());
      const { sessionId } = await res.json();
      addSessionItem(sessionId, model);
      activateSession(sessionId);
      // Clear feed for fresh session
      feed.innerHTML = '';
    } catch (err) {
      appendError('Could not create session: ' + err.message);
    }
  }

  // ── SignalR ────────────────────────────────────────────────────
  function buildConnection() {
    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/chat')
      .withAutomaticReconnect([500, 1000, 2000, 5000, 10000])
      .build();

    connection.onreconnecting(() => setStatus('connecting', 'reconnecting…'));
    connection.onreconnected(() => setStatus('connected', 'connected'));
    connection.onclose(() => setStatus('disconnected', 'disconnected'));

    connection.on('MessageDelta', (sessionId, chunk) => {
      if (sessionId !== activeSessionId) return;
      appendDelta(chunk);
    });

    connection.on('MessageComplete', (sessionId) => {
      if (sessionId !== activeSessionId) return;
      finalizeAssistantMessage();
      setProcessing(false);
    });

    connection.on('ToolStarted', (sessionId, toolName) => {
      if (sessionId !== activeSessionId) return;
      activeToolCallId = toolName;
      appendToolLog(toolName, false, false);
    });

    connection.on('ToolCompleted', (sessionId, result) => {
      if (sessionId !== activeSessionId) return;
      // Update the last tool log entry to done/error state
      const logs = feed.querySelectorAll('.tool-log');
      const last = logs[logs.length - 1];
      if (last && !last.classList.contains('tool-done') && !last.classList.contains('tool-error')) {
        const isError = result === 'error';
        last.classList.add(isError ? 'tool-error' : 'tool-done');
        const icon = last.querySelector('.tool-icon');
        if (icon) icon.textContent = isError ? '✗' : '✓';
      }
      activeToolCallId = null;
    });

    connection.on('SessionError', (sessionId, message) => {
      if (sessionId !== activeSessionId) return;
      finalizeAssistantMessage();
      appendError(message);
      setProcessing(false);
    });
  }

  async function startConnection() {
    setStatus('connecting', 'connecting…');
    try {
      await connection.start();
      setStatus('connected', 'connected');
    } catch (err) {
      setStatus('disconnected', 'disconnected');
      setTimeout(startConnection, 3000);
    }
  }

  // ── Send ───────────────────────────────────────────────────────
  async function sendMessage() {
    const text = input.value.trim();
    if (!text || !activeSessionId || isProcessing) return;

    input.value = '';
    input.style.height = 'auto';

    appendUserMessage(text);
    startAssistantMessage();
    setProcessing(true);

    try {
      await connection.invoke('SendMessage', activeSessionId, text);
    } catch (err) {
      finalizeAssistantMessage();
      appendError('Send failed: ' + err.message);
      setProcessing(false);
    }
  }

  // ── Init ───────────────────────────────────────────────────────
  sendBtn.addEventListener('click', sendMessage);
  abortBtn.addEventListener('click', async () => {
    if (activeSessionId && connection)
      await connection.invoke('AbortSession', activeSessionId).catch(() => {});
  });

  newSessionBtn.addEventListener('click', createSession);

  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  });

  input.addEventListener('input', autoResize);

  // Wire existing session items from server-rendered list
  document.querySelectorAll('.session-item').forEach(li => {
    li.addEventListener('click', () => activateSession(li.dataset.sessionId));
  });

  buildConnection();
  startConnection().then(fetchModels);
})();
