namespace AgentCore.Diagnostics;

internal static class ExplorerUi
{
    internal const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>AgentCore — Trace Explorer</title>
          <link rel="preconnect" href="https://fonts.googleapis.com" />
          <link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap" rel="stylesheet" />
          <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

            :root {
              --bg:         #0b0d12;
              --surface:    #13161e;
              --surface2:   #1c2030;
              --border:     #252a38;
              --border2:    #2e3347;
              --text:       #e2e8f0;
              --muted:      #64748b;
              --accent:     #6366f1;
              --accent-dim: rgba(99,102,241,.15);
              --green:      #10b981;
              --green-dim:  rgba(16,185,129,.12);
              --red:        #f87171;
              --red-dim:    rgba(248,113,113,.12);
              --yellow:     #fbbf24;
              --mono:       'JetBrains Mono', monospace;
              --sans:       'Inter', system-ui, sans-serif;
            }

            html, body { height: 100%; background: var(--bg); color: var(--text); font-family: var(--sans); font-size: 14px; }

            /* ── Layout ─────────────────────────────────────────── */
            .shell   { display: flex; height: 100vh; overflow: hidden; }
            .sidebar { width: 360px; min-width: 280px; display: flex; flex-direction: column; background: var(--surface); border-right: 1px solid var(--border); }
            .detail  { flex: 1; display: flex; flex-direction: column; overflow: hidden; }

            /* ── Sidebar header ─────────────────────────────────── */
            .sidebar-head {
              padding: 20px 20px 16px;
              border-bottom: 1px solid var(--border);
              display: flex; flex-direction: column; gap: 12px;
            }
            .logo { display: flex; align-items: center; gap: 10px; }
            .logo-icon { width: 28px; height: 28px; background: var(--accent); border-radius: 8px;
                         display: flex; align-items: center; justify-content: center; font-size: 14px; }
            .logo-text { font-size: 15px; font-weight: 600; letter-spacing: -.3px; }
            .logo-sub  { font-size: 11px; color: var(--muted); margin-top: 1px; }

            .search {
              display: flex; align-items: center; gap: 8px;
              background: var(--surface2); border: 1px solid var(--border2); border-radius: 8px;
              padding: 8px 12px;
            }
            .search input { background: none; border: none; outline: none; color: var(--text); font: inherit; flex: 1; font-size: 13px; }
            .search input::placeholder { color: var(--muted); }
            .search-icon { color: var(--muted); font-size: 13px; }

            .sidebar-toolbar {
              padding: 8px 12px;
              display: flex; align-items: center; justify-content: space-between;
              border-bottom: 1px solid var(--border);
            }
            .trace-count { font-size: 11px; color: var(--muted); }
            .btn-refresh {
              font: inherit; font-size: 12px; font-weight: 500; color: var(--accent);
              background: var(--accent-dim); border: 1px solid transparent;
              border-radius: 6px; padding: 4px 10px; cursor: pointer; transition: all .15s;
            }
            .btn-refresh:hover { border-color: var(--accent); }

            /* ── Trace list ─────────────────────────────────────── */
            .trace-list { flex: 1; overflow-y: auto; }
            .trace-list::-webkit-scrollbar { width: 4px; }
            .trace-list::-webkit-scrollbar-thumb { background: var(--border2); border-radius: 4px; }

            .trace-item {
              padding: 14px 16px; border-bottom: 1px solid var(--border);
              cursor: pointer; transition: background .1s;
              display: flex; flex-direction: column; gap: 6px;
            }
            .trace-item:hover   { background: var(--surface2); }
            .trace-item.active  { background: var(--accent-dim); border-left: 2px solid var(--accent); padding-left: 14px; }

            .trace-item-top { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
            .trace-name { font-weight: 500; font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
            .badge {
              flex-shrink: 0; font-size: 10px; font-weight: 600; padding: 2px 7px;
              border-radius: 99px; letter-spacing: .3px; text-transform: uppercase;
            }
            .badge-ok  { background: var(--green-dim); color: var(--green); }
            .badge-err { background: var(--red-dim);   color: var(--red);   }
            .badge-live { background: rgba(99,102,241,.15); color: var(--accent); border: 1px solid var(--accent); }

            .trace-item-meta { display: flex; gap: 12px; }
            .meta-tag { font-size: 11px; color: var(--muted); display: flex; align-items: center; gap: 4px; }
            .meta-tag .dot { width: 5px; height: 5px; border-radius: 50%; background: var(--muted); }

            /* ── Empty / loading states ─────────────────────────── */
            .state-box {
              display: flex; flex-direction: column; align-items: center; justify-content: center;
              height: 100%; gap: 12px; color: var(--muted); text-align: center; padding: 32px;
            }
            .state-icon { font-size: 32px; opacity: .4; }
            .state-title { font-size: 14px; font-weight: 500; }
            .state-sub { font-size: 12px; }

            /* ── Detail panel ───────────────────────────────────── */
            .detail-head {
              padding: 20px 24px 16px;
              border-bottom: 1px solid var(--border);
              display: flex; align-items: flex-start; justify-content: space-between; gap: 16px;
            }
            .detail-title { font-size: 16px; font-weight: 600; }
            .detail-id    { font: 11px var(--mono); color: var(--muted); margin-top: 4px; }
            .detail-stats { display: flex; gap: 20px; flex-shrink: 0; }
            .stat { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; }
            .stat-val  { font-size: 18px; font-weight: 700; }
            .stat-label{ font-size: 10px; color: var(--muted); text-transform: uppercase; letter-spacing: .5px; }

            .detail-body { flex: 1; overflow-y: auto; padding: 24px; display: flex; flex-direction: column; gap: 20px; }
            .detail-body::-webkit-scrollbar { width: 4px; }
            .detail-body::-webkit-scrollbar-thumb { background: var(--border2); border-radius: 4px; }

            /* ── Section card ───────────────────────────────────── */
            .card { background: var(--surface); border: 1px solid var(--border); border-radius: 10px; overflow: hidden; }
            .card-head {
              padding: 12px 16px; border-bottom: 1px solid var(--border);
              font-size: 11px; font-weight: 600; color: var(--muted);
              text-transform: uppercase; letter-spacing: .6px;
              display: flex; align-items: center; gap: 8px;
            }

            /* ── Span tree ──────────────────────────────────────── */
            .span-row {
              padding: 10px 16px; border-bottom: 1px solid var(--border);
              cursor: pointer; transition: background .1s;
            }
            .span-row:last-child { border-bottom: none; }
            .span-row:hover { background: var(--surface2); }
            .span-row.expanded { background: var(--surface2); }

            .span-row-top { display: flex; align-items: center; gap: 10px; }
            .span-kind {
              font-size: 10px; font-weight: 600; padding: 2px 7px; border-radius: 99px;
              text-transform: uppercase; letter-spacing: .3px; flex-shrink: 0;
            }
            .kind-llm    { background: rgba(99,102,241,.15); color: #818cf8; }
            .kind-tool   { background: rgba(251,191,36,.1);  color: var(--yellow); }
            .kind-memory { background: rgba(16,185,129,.12); color: var(--green); }
            .kind-agent  { background: rgba(248,113,113,.1); color: var(--red); }
            .kind-span   { background: var(--surface2); color: var(--muted); }

            .span-name { font-size: 13px; font-weight: 500; flex: 1; }
            .span-dur  { font: 12px var(--mono); color: var(--muted); flex-shrink: 0; }
            .span-status { font-size: 10px; flex-shrink: 0; }

            /* ── Span detail (expanded) ──────────────────────────── */
            .span-detail { padding: 12px 16px 14px; display: flex; flex-direction: column; gap: 12px; border-bottom: 1px solid var(--border); }
            .span-detail:last-child { border-bottom: none; }

            .kv-grid { display: grid; grid-template-columns: 130px 1fr; gap: 6px 16px; }
            .kv-key  { font-size: 11px; color: var(--muted); font-weight: 500; padding-top: 2px; }
            .kv-val  { font: 12px var(--mono); color: var(--text); word-break: break-all; white-space: pre-wrap; }

            .pre-box {
              background: var(--bg); border: 1px solid var(--border);
              border-radius: 6px; padding: 10px 12px;
              font: 12px var(--mono); white-space: pre-wrap; word-break: break-word;
              max-height: 200px; overflow-y: auto; color: var(--text);
            }
            .pre-box::-webkit-scrollbar { height: 4px; width: 4px; }
            .pre-box::-webkit-scrollbar-thumb { background: var(--border2); border-radius: 4px; }

            /* ── Timeline bar ───────────────────────────────────── */
            .timeline-wrap { padding: 16px; display: flex; flex-direction: column; gap: 6px; }
            .tl-row { display: flex; align-items: center; gap: 10px; }
            .tl-label { font-size: 11px; color: var(--muted); width: 120px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; flex-shrink: 0; }
            .tl-track { flex: 1; height: 8px; background: var(--surface2); border-radius: 4px; position: relative; }
            .tl-bar   { position: absolute; top: 0; height: 100%; border-radius: 4px; transition: width .3s; }
            .tl-dur   { font: 11px var(--mono); color: var(--muted); width: 60px; text-align: right; flex-shrink: 0; }
          </style>
        </head>
        <body>
        <div class="shell">

          <!-- ── Sidebar ───────────────────────────────────────── -->
          <aside class="sidebar">
            <div class="sidebar-head">
              <div class="logo">
                <div class="logo-icon">⬡</div>
                <div>
                  <div class="logo-text">Trace Explorer</div>
                  <div class="logo-sub">AgentCore Diagnostics</div>
                </div>
              </div>
              <div class="search">
                <span class="search-icon">⌕</span>
                <input id="search" type="text" placeholder="Filter traces…" oninput="filterTraces()" />
              </div>
            </div>
            <div class="sidebar-toolbar">
              <span class="trace-count" id="traceCount">—</span>
              <button class="btn-refresh" onclick="loadTraces()">↻ Refresh</button>
            </div>
            <div class="trace-list" id="traceList">
              <div class="state-box">
                <div class="state-icon">◌</div>
                <div class="state-title">Loading traces…</div>
              </div>
            </div>
          </aside>

          <!-- ── Detail panel ──────────────────────────────────── -->
          <main class="detail" id="detail">
            <div class="state-box">
              <div class="state-icon">◈</div>
              <div class="state-title">Select a trace</div>
              <div class="state-sub">Click any trace on the left to inspect it.</div>
            </div>
          </main>

        </div>

        <script>
        let allTraces = [];
        let selectedId = null;

        // ── SSE live streaming connection ────────────────────────
        function setupLiveStream() {
          const sse = new EventSource('/api/v1/stream');
          sse.onmessage = (e) => {
            try {
              const evt = JSON.parse(e.data);
              handleLiveEvent(evt);
            } catch (err) {
              console.error("SSE parse error", err);
            }
          };
          sse.onerror = () => {
            console.log("SSE disconnected. Reconnecting...");
          };
        }

        function handleLiveEvent(evt) {
          if (evt.eventType === 'TraceStarted') {
            const t = evt.trace;
            if (!allTraces.some(x => x.traceId === t.traceId)) {
              allTraces.unshift({
                traceId: t.traceId,
                name: t.name,
                sessionId: t.sessionId,
                agentName: t.agentName,
                start: t.start,
                durationMs: 0,
                isSuccess: true,
                isLive: true
              });
              renderTraceList(allTraces);
            }
          } else if (evt.eventType === 'SpanStarted' || evt.eventType === 'SpanFinished') {
            const span = evt.span;
            if (selectedId === span.traceId) {
              loadDetail(selectedId, true);
            }
          } else if (evt.eventType === 'TraceFinished') {
            const t = evt.trace;
            const summary = {
              traceId: t.traceId,
              name: t.name,
              sessionId: t.sessionId,
              agentName: t.agentName,
              start: t.start,
              durationMs: t.end ? (new Date(t.end) - new Date(t.start)) : 0,
              isSuccess: t.isSuccess,
              isLive: false
            };
            const idx = allTraces.findIndex(x => x.traceId === t.traceId);
            if (idx === -1) {
              allTraces.unshift(summary);
            } else {
              allTraces[idx] = summary;
            }
            renderTraceList(allTraces);

            if (selectedId === t.traceId) {
              renderDetail(t);
            }
          }
        }

        // ── Data ──────────────────────────────────────────────────

        async function loadTraces() {
          setTraceListHtml('<div class="state-box"><div class="state-icon">◌</div><div class="state-title">Loading…</div></div>');
          try {
            const r = await fetch('/api/v1/traces');
            allTraces = await r.json();
            renderTraceList(allTraces);
          } catch (e) {
            setTraceListHtml(`<div class="state-box" style="color:var(--red)"><div class="state-title">Error loading traces</div><div class="state-sub">${e.message}</div></div>`);
          }
        }

        async function loadDetail(id, silent = false) {
          selectedId = id;
          document.querySelectorAll('.trace-item').forEach(el => el.classList.toggle('active', el.dataset.id === id));
          const detail = document.getElementById('detail');
          if (!silent) {
            detail.innerHTML = '<div class="state-box"><div class="state-icon">◌</div><div class="state-title">Loading…</div></div>';
          }
          try {
            const r = await fetch(`/api/v1/traces/${id}`);
            const trace = await r.json();
            renderDetail(trace);
          } catch (e) {
            if (!silent) {
              detail.innerHTML = `<div class="state-box" style="color:var(--red)"><div class="state-title">Failed to load trace</div></div>`;
            }
          }
        }

        // ── Render: trace list ────────────────────────────────────

        function filterTraces() {
          const q = document.getElementById('search').value.toLowerCase();
          renderTraceList(q ? allTraces.filter(t => t.name?.toLowerCase().includes(q) || t.sessionId?.toLowerCase().includes(q)) : allTraces);
        }

        function renderTraceList(traces) {
          document.getElementById('traceCount').textContent = `${traces.length} trace${traces.length !== 1 ? 's' : ''}`;
          if (!traces.length) {
            setTraceListHtml('<div class="state-box"><div class="state-icon">◈</div><div class="state-title">No traces found</div><div class="state-sub">Run an agent to produce traces.</div></div>');
            return;
          }
          setTraceListHtml(traces.map(t => {
            const dur = formatDur(t.durationMs);
            const time = new Date(t.start).toLocaleTimeString();
            const active = t.traceId === selectedId ? 'active' : '';
            let badge = '';
            if (t.isLive) {
              badge = '<span class="badge badge-live">LIVE</span>';
            } else {
              badge = t.isSuccess ? '<span class="badge badge-ok">OK</span>' : '<span class="badge badge-err">ERR</span>';
            }
            return `
              <div class="trace-item ${active}" data-id="${t.traceId}" onclick="loadDetail('${t.traceId}')">
                <div class="trace-item-top">
                  <span class="trace-name">${esc(t.name)}</span>
                  ${badge}
                </div>
                <div class="trace-item-meta">
                  <span class="meta-tag">${time}</span>
                  <span class="meta-tag"><span class="dot"></span>${dur}</span>
                  <span class="meta-tag"><span class="dot"></span>${esc(t.sessionId?.slice(0,8) ?? '—')}</span>
                </div>
              </div>`;
          }).join(''));
        }

        // ── Render: detail ────────────────────────────────────────

        function renderDetail(trace) {
          const detail = document.getElementById('detail');
          const totalMs = trace.durationMs ?? (trace.end ? (new Date(trace.end) - new Date(trace.start)) : 0);
          const spans = trace.spans ?? [];

          // Stats
          const llmSpans    = spans.filter(s => s.kind === 'LLM');
          const toolSpans   = spans.filter(s => s.kind === 'Tool');
          const totalTokens = llmSpans.reduce((a, s) => a + (s.attributes?.['llm.usage.total_tokens'] ?? 0), 0);

          const isLive = !trace.end;

          const head = `
            <div class="detail-head">
              <div>
                <div class="detail-title">${esc(trace.name)}</div>
                <div class="detail-id">${trace.traceId}</div>
              </div>
              <div class="detail-stats">
                <div class="stat"><span class="stat-val" style="color:var(--accent)">${formatDur(totalMs)}</span><span class="stat-label">Duration</span></div>
                <div class="stat"><span class="stat-val" style="color:var(--yellow)">${totalTokens > 0 ? totalTokens.toLocaleString() : '—'}</span><span class="stat-label">Tokens</span></div>
                <div class="stat"><span class="stat-val" style="color:var(--green)">${toolSpans.length}</span><span class="stat-label">Tool calls</span></div>
                <div class="stat">
                  <span class="stat-val" style="color:${isLive ? 'var(--accent)' : (trace.isSuccess ? 'var(--green)' : 'var(--red)')}">
                    ${isLive ? '●' : (trace.isSuccess ? '✓' : '✗')}
                  </span>
                  <span class="stat-label">Status</span>
                </div>
              </div>
            </div>`;

          // Timeline
          const timelineRows = spans.map(s => {
            const duration = s.durationMs ?? (s.end ? (new Date(s.end) - new Date(s.start)) : 0);
            const pct = totalMs > 0 ? Math.min(100, (duration / totalMs) * 100) : 0;
            const color = kindColor(s.kind);
            return `
              <div class="tl-row">
                <span class="tl-label">${esc(s.name)}</span>
                <div class="tl-track"><div class="tl-bar" style="width:${pct}%;background:${color}"></div></div>
                <span class="tl-dur">${formatDur(duration)}</span>
              </div>`;
          }).join('');

          // Span tree
          const spanRows = spans.map((s, i) => spanRow(s, i)).join('');

          detail.innerHTML = head + `
            <div class="detail-body">
              ${spans.length ? `
              <div class="card">
                <div class="card-head">⏱ Timeline</div>
                <div class="timeline-wrap">${timelineRows}</div>
              </div>
              <div class="card">
                <div class="card-head">◈ Spans (${spans.length})</div>
                ${spanRows}
              </div>` : '<div class="state-box"><div class="state-title">No spans recorded</div></div>'}
            </div>`;
        }

        function spanRow(s, i) {
          const kindCls = 'kind-' + (s.kind ?? 'span').toLowerCase();
          const duration = s.durationMs ?? (s.end ? (new Date(s.end) - new Date(s.start)) : 0);
          const statusColor = !s.end ? 'var(--accent)' : (s.status === 'Success' ? 'var(--green)' : s.status === 'Error' ? 'var(--red)' : 'var(--muted)');
          const attrs = s.attributes ?? {};
          const hasInput  = attrs['span.input'] || s.input;
          const hasOutput = attrs['span.output'] || s.output;
          const hasError  = attrs['span.error'] || s.statusMessage;

          const attrRows = Object.entries(attrs)
            .filter(([k]) => !['span.input','span.output','span.error'].includes(k))
            .map(([k,v]) => `<div class="kv-key">${esc(k)}</div><div class="kv-val">${esc(String(v))}</div>`)
            .join('');

          const detail = `
            <div class="span-detail" id="span-detail-${i}" style="display:none">
              ${hasInput  ? `<div><div class="kv-key" style="margin-bottom:4px">Input</div><div class="pre-box">${esc(hasInput)}</div></div>` : ''}
              ${hasOutput ? `<div><div class="kv-key" style="margin-bottom:4px">Output</div><div class="pre-box">${esc(hasOutput)}</div></div>` : ''}
              ${hasError  ? `<div><div class="kv-key" style="margin-bottom:4px;color:var(--red)">Error</div><div class="pre-box" style="color:var(--red)">${esc(hasError)}</div></div>` : ''}
              ${attrRows  ? `<div class="kv-grid">${attrRows}</div>` : ''}
            </div>`;

          return `
            <div class="span-row" onclick="toggleSpan(${i})" id="span-row-${i}">
              <div class="span-row-top">
                <span class="span-kind ${kindCls}">${s.kind ?? 'Span'}</span>
                <span class="span-name">${esc(s.name)}</span>
                <span class="span-dur">${formatDur(duration)}</span>
                <span class="span-status" style="color:${statusColor}">${!s.end ? '●' : '●'}</span>
              </div>
            </div>
            ${detail}`;
        }

        function toggleSpan(i) {
          const row = document.getElementById(`span-row-${i}`);
          const det = document.getElementById(`span-detail-${i}`);
          const open = det.style.display !== 'none';
          det.style.display = open ? 'none' : 'flex';
          det.style.flexDirection = 'column';
          det.style.gap = '12px';
          row.classList.toggle('expanded', !open);
        }

        // ── Helpers ───────────────────────────────────────────────

        function setTraceListHtml(html) { document.getElementById('traceList').innerHTML = html; }

        function formatDur(ms) {
          if (!ms && ms !== 0) return '—';
          if (ms < 1000) return ms.toFixed(0) + 'ms';
          return (ms / 1000).toFixed(2) + 's';
        }

        function kindColor(kind) {
          switch ((kind ?? '').toLowerCase()) {
            case 'llm':    return '#818cf8';
            case 'tool':   return '#fbbf24';
            case 'memory': return '#10b981';
            case 'agent':  return '#f87171';
            default:       return '#64748b';
          }
        }

        function esc(s) {
          if (s == null) return '';
          return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
        }

        // ── Boot ──────────────────────────────────────────────────
        loadTraces();
        setupLiveStream();
        </script>
        </body>
        </html>
        """;
}
