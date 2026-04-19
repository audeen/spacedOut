/**
 * SpacedOut - Captain Station UI Logic
 * Decision urgency, log filtering, event countdowns
 */

let briefingShown = false;
let pendingMissionBriefingText = null;

function isDecisionResolutionOverlayOpen() {
    const el = document.getElementById('decision-resolution-overlay');
    return !!(el && !el.classList.contains('hidden'));
}

function syncDecisionResolutionCta() {
    const btn = document.getElementById('decision-resolution-cta');
    if (!btn) return;
    const has = pendingMissionBriefingText != null && String(pendingMissionBriefingText).length > 0;
    btn.textContent = has ? 'Zum Briefing' : 'Verstanden';
}

function onDecisionResolutionContinue() {
    const dec = document.getElementById('decision-resolution-overlay');
    if (dec) dec.classList.add('hidden');
    client.resetDecisionResolutionOverlayChrome();

    const brief = pendingMissionBriefingText;
    pendingMissionBriefingText = null;
    syncDecisionResolutionCta();

    if (brief != null && String(brief).length > 0) {
        briefingShown = true;
        client.resetBriefingOverlayChrome();
        const el = document.getElementById('briefing-text');
        if (el) el.textContent = brief;
        document.getElementById('briefing-overlay').classList.remove('hidden');
    }
}

let logFilter = 'all';
let currentLog = [];

client.on('welcome', () => {
    client.selectRole('Captain');
});

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('decisions-container').addEventListener('click', (e) => {
        const btn = e.target.closest('[data-decision-id]');
        if (btn) resolveDecision(btn.dataset.decisionId, btn.dataset.optionId);
    });
    document.getElementById('overlays-container').addEventListener('click', (e) => {
        const approveBtn = e.target.closest('[data-approve-id]');
        if (approveBtn) { approveOverlay(approveBtn.dataset.approveId); return; }
        const dismissBtn = e.target.closest('[data-dismiss-id]');
        if (dismissBtn) dismissOverlay(dismissBtn.dataset.dismissId);
    });
});

client.onState((msg) => {
    if (!msg.data) return;
    const d = msg.data;

    updatePhaseHeaderFromState(msg);
    document.getElementById('timer-display').textContent = formatTime(msg.elapsed_time || 0);

    // Hull
    const hull = d.hull_integrity ?? 100;
    const hullEl = document.getElementById('hull-value');
    hullEl.textContent = `${Math.round(hull)}%`;
    hullEl.className = `status-value ${hull > 60 ? 'text-green' : hull > 30 ? 'text-yellow' : 'text-red'}`;
    document.getElementById('hull-bar').style.width = `${hull}%`;
    const hullBar = document.getElementById('hull-bar').parentElement;
    hullBar.className = `progress-bar ${hull > 60 ? 'progress-green' : hull > 30 ? 'progress-yellow' : 'progress-red'}`;

    // Flight mode
    document.getElementById('flight-mode').textContent = (d.flight_mode || 'CRUISE').toUpperCase();

    // Systems summary
    const sysSummary = d.systems_summary || {};
    const sysHtml = Object.entries(sysSummary).map(([key, status]) => {
        return `<span class="system-status ${getStatusClass(status)}">${key}: ${getStatusLabel(status)}</span>`;
    }).join(' ');
    document.getElementById('systems-summary').innerHTML = sysHtml;

    renderDecisions(d.pending_decisions || []);
    renderOverlays(d.overlays || []);
    renderEvents(d.active_events || []);

    currentLog = d.log || [];
    renderLog();

    updateDockPanel(d);
    updateRunResourcesPanel(d);
    updateSectorJumpPanel(msg, d);
});

function updateSectorJumpPanel(msg, d) {
    const panel = document.getElementById('sector-jump-panel');
    const btn = document.getElementById('btn-leave-sector');
    const hint = document.getElementById('sector-jump-hint');
    if (!panel || !btn || !hint) return;

    if (!msg.mission_started) {
        panel.style.display = 'none';
        return;
    }
    const phase = msg.mission_phase || '';
    if (phase === 'Ended') {
        panel.style.display = 'none';
        return;
    }
    panel.style.display = '';
    const ready = !!d.sector_jump_available;
    btn.disabled = !ready;
    hint.textContent = ready
        ? 'Sprungbereit — Sie können den Sektor jetzt verlassen.'
        : 'Am Sprungpunkt positionieren (Abstand typischerweise < 50 m). Relais/Scan ggf. vorher erforderlich.';
}

function sendLeaveSector() {
    client.sendCommand('LeaveSector', {});
    client.showToast('Sprung angefordert', 'info');
}

function updateRunResourcesPanel(d) {
    const panel = document.getElementById('run-resources-panel');
    if (!panel) return;
    if (!d.run_active || !d.run_resources) {
        panel.style.display = 'none';
        return;
    }
    const res = d.run_resources;
    panel.style.display = 'block';
    const set = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v ?? 0; };
    set('rr-parts', res.SpareParts ?? 0);
    set('rr-data', res.ScienceData ?? 0);
    set('rr-fuel', res.Fuel ?? 0);
    set('rr-credits', res.Credits ?? 0);

    const perkRow = document.getElementById('rr-perk-row');
    const perkEl = document.getElementById('rr-perk');
    const perkName = d.perk_name || '';
    if (perkRow && perkEl) {
        if (perkName) {
            perkRow.style.display = '';
            perkEl.textContent = perkName;
        } else {
            perkRow.style.display = 'none';
        }
    }
}

function updateDockStock(d) {
    const set = (id, v) => { const el = document.getElementById(id); if (el) el.textContent = v; };
    if (!d.run_active || !d.run_resources) {
        set('dock-stock-fuel', '—');
        set('dock-stock-parts', '—');
        set('dock-stock-data', '—');
        set('dock-stock-credits', '—');
        return;
    }
    const res = d.run_resources;
    set('dock-stock-fuel', res.Fuel ?? 0);
    set('dock-stock-parts', res.SpareParts ?? 0);
    set('dock-stock-data', res.ScienceData ?? 0);
    set('dock-stock-credits', res.Credits ?? 0);
}

function updateDockPanel(d) {
    const panel = document.getElementById('dock-panel');
    const statusLine = document.getElementById('dock-status-line');
    const buyContainer = document.getElementById('dock-buy-container');
    if (!panel || !statusLine || !buyContainer) return;

    const dock = d.dock;
    const docked = !!d.mission_docked;
    if (!dock || dock.Available === false) {
        panel.style.display = 'none';
        return;
    }

    panel.style.display = 'block';
    if (docked) {
        statusLine.textContent = `Angedockt — Rep 1 Ersatzteil = ${dock.HullPerPart} Hülle`;
        statusLine.className = 'text-green';
        buyContainer.style.display = 'block';
        document.getElementById('dock-fuel-price').textContent = `${dock.FuelPrice} Cr / Einheit (Kauf)`;
        document.getElementById('dock-fuel-sell-price').textContent = `${dock.FuelSellPrice} Cr / Einheit (Ankauf)`;
        document.getElementById('dock-parts-price').textContent = `${dock.PartsPrice} Cr / Einheit (Kauf)`;
        document.getElementById('dock-parts-sell-price').textContent = `${dock.PartsSellPrice} Cr / Einheit (Ankauf)`;
        document.getElementById('dock-data-price').textContent = `${dock.DataPrice} Cr / Einheit (Kauf)`;
        document.getElementById('dock-data-sell-price').textContent = `${dock.DataSellPrice} Cr / Einheit (Ankauf)`;
        updateDockStock(d);
    } else {
        const dist = typeof d.dock_distance === 'number' ? d.dock_distance : -1;
        const sl = d.speed_level ?? 2;
        const range = dock.ProximityRange ?? 60;
        const maxSpeed = dock.MaxSpeedLevel ?? 2;
        let hint;
        if (dist < 0) hint = 'Andockmast in diesem Sektor — anfliegen für Handel.';
        else if (dist > range) hint = `Andockmast: ${dist.toFixed(0)} m · Speed ${sl} — näher heran (≤${range} m).`;
        else if (sl > maxSpeed) hint = `Andockmast: ${dist.toFixed(0)} m · Speed ${sl} — zu schnell (≤${maxSpeed}).`;
        else hint = `Andockmast: ${dist.toFixed(0)} m · Speed ${sl} — Andocken läuft...`;
        statusLine.textContent = hint;
        statusLine.className = 'text-cyan';
        buyContainer.style.display = 'none';
    }
}

let _dockTradeLastKey = '';
let _dockTradeLastAt = 0;

function dockBuy(resource, qty) {
    const key = `buy:${resource}:${qty}`;
    const t = Date.now();
    if (key === _dockTradeLastKey && t - _dockTradeLastAt < 450) return;
    _dockTradeLastKey = key;
    _dockTradeLastAt = t;
    client.sendCommand('DockBuyResource', { resource, qty });
}

function dockSell(resource, qty) {
    const key = `sell:${resource}:${qty}`;
    const t = Date.now();
    if (key === _dockTradeLastKey && t - _dockTradeLastAt < 450) return;
    _dockTradeLastKey = key;
    _dockTradeLastAt = t;
    client.sendCommand('DockSellResource', { resource, qty });
}

client.on('mission_started', (msg) => {
    if (msg.briefing) {
        pendingMissionBriefingText = msg.briefing;
    }
    if (isDecisionResolutionOverlayOpen()) {
        syncDecisionResolutionCta();
        return;
    }
    if (!briefingShown && msg.briefing) {
        briefingShown = true;
        pendingMissionBriefingText = null;
        client.resetBriefingOverlayChrome();
        document.getElementById('briefing-text').textContent = msg.briefing;
        document.getElementById('briefing-overlay').classList.remove('hidden');
    }
});

client.on('decision_resolved', (msg) => {
    if (msg && msg.resolution_style === 'toast') return;
    syncDecisionResolutionCta();
});

client.on('paused', () => document.getElementById('paused-overlay').classList.remove('hidden'));
client.on('resumed', () => document.getElementById('paused-overlay').classList.add('hidden'));

client.on('mission_ended', (msg) => {
    const overlay = document.getElementById('mission-end');
    overlay.classList.remove('hidden');

    let title = 'MISSION BEENDET';
    let color = 'var(--yellow)';
    if (msg.primary_objective === 'Completed') {
        title = 'MISSION ERFOLGREICH';
        color = 'var(--green)';
    } else if (msg.primary_objective === 'Failed') {
        title = 'MISSION GESCHEITERT';
        color = 'var(--red)';
    }

    document.getElementById('mission-end-title').textContent = title;
    document.getElementById('mission-end-title').style.color = color;
    document.getElementById('mission-end-detail').innerHTML =
        `Primärziel: ${msg.primary_objective}<br>` +
        `Sekundärziel: ${msg.secondary_objective}<br>` +
        `Hülle: ${Math.round(msg.hull_integrity)}%<br>` +
        `Zeit: ${formatTime(msg.elapsed_time)}`;
});

function dismissBriefing() {
    document.getElementById('briefing-overlay').classList.add('hidden');
    client.resetBriefingOverlayChrome();
}

// --- Decisions with urgency ---

let _lastDecisionIds = '';

function renderDecisions(decisions) {
    const panel = document.getElementById('decisions-panel');
    const container = document.getElementById('decisions-container');

    const ids = decisions.map(d => d.Id).join(',');
    if (ids === _lastDecisionIds) return;
    _lastDecisionIds = ids;

    if (decisions.length === 0) {
        panel.style.display = 'none';
        container.innerHTML = '';
        return;
    }
    panel.style.display = '';
    container.innerHTML = '';

    decisions.forEach(dec => {
        const card = document.createElement('div');
        card.className = 'decision-card urgent';
        card.innerHTML = `
            <div style="display:flex; justify-content:space-between; align-items:center;">
                <div class="decision-title">${dec.Title}</div>
                <span class="decision-timer">Entscheidung nötig</span>
            </div>
            <div class="decision-desc">${dec.Description}</div>
            <div class="decision-options">
                ${dec.Options.map(opt => `
                    <button class="btn btn-primary btn-block" data-decision-id="${dec.Id}" data-option-id="${opt.Id}"
                            style="text-align:left; justify-content:flex-start; flex-direction:column; align-items:flex-start;">
                        <strong>${opt.Label}</strong>
                        <span class="text-dim" style="font-size:11px;">${opt.Description}</span>
                        ${opt.FlavorHint ? `<span class="text-cyan" style="font-size:11px; font-style:italic;">${opt.FlavorHint}</span>` : ''}
                    </button>
                `).join('')}
            </div>
        `;
        container.appendChild(card);
    });
}

// --- Overlays ---

let _lastOverlaysKey = '';

function renderOverlays(overlays) {
    const panel = document.getElementById('overlays-panel');
    const container = document.getElementById('overlays-container');

    const pending = overlays.filter(o => !o.ApprovedByCaptain && !o.Dismissed);
    const key = pending.map(o => `${o.Id}:${Math.round(o.RemainingTime)}`).join('|');
    if (key === _lastOverlaysKey) return;
    _lastOverlaysKey = key;

    if (pending.length === 0) {
        panel.style.display = 'none';
        container.innerHTML = '';
        return;
    }
    panel.style.display = '';
    container.innerHTML = '';

    pending.forEach(overlay => {
        const item = document.createElement('div');
        item.className = 'overlay-item';
        const catColor = overlay.Category === 'Warning' ? 'text-red' :
                         overlay.Category === 'Tactical' ? 'text-yellow' : 'text-cyan';
        item.innerHTML = `
            <div style="flex:1;">
                <div class="${catColor}" style="font-size:12px;">[${overlay.SourceStation}] ${overlay.Category}</div>
                <div style="font-size:14px;">${overlay.Text}</div>
                <div class="text-dim" style="font-size:10px; margin-top:2px;">
                    Prio: ${overlay.Priority} · ${Math.round(overlay.RemainingTime)}s
                </div>
            </div>
            <div style="display:flex; gap:6px;">
                <button class="btn btn-success btn-sm" data-approve-id="${overlay.Id}">✓</button>
                <button class="btn btn-danger btn-sm" data-dismiss-id="${overlay.Id}">✕</button>
            </div>
        `;
        container.appendChild(item);
    });
}

// --- Events with countdown bars ---

let _lastEventsKey = '';

function renderEvents(events) {
    const panel = document.getElementById('events-panel');
    const container = document.getElementById('events-container');

    const active = events.filter(e => e.IsActive);
    const key = active.map(e => `${e.Id}:${Math.round(e.TimeRemaining)}`).join('|');
    if (key === _lastEventsKey) return;
    _lastEventsKey = key;

    if (active.length === 0) {
        panel.style.display = 'none';
        container.innerHTML = '';
        return;
    }
    panel.style.display = '';
    container.innerHTML = '';

    active.forEach(evt => {
        const maxDuration = 180;
        const pct = evt.TimeRemaining > 0 ? Math.min(100, (evt.TimeRemaining / maxDuration) * 100) : 0;
        const div = document.createElement('div');
        div.className = 'alert alert-warning';
        div.innerHTML = `
            <div style="display:flex; justify-content:space-between; align-items:center;">
                <strong>${evt.Title}</strong>
                ${evt.TimeRemaining > 0 ? `<span style="font-family:var(--font-mono); font-size:12px;">${Math.round(evt.TimeRemaining)}s</span>` : ''}
            </div>
            <span style="font-size:12px;">${evt.Description}</span>
            ${evt.TimeRemaining > 0 ? `
                <div class="event-timer-bar">
                    <div class="event-timer-fill" style="width:${Math.round(pct)}%"></div>
                </div>
            ` : ''}
        `;
        container.appendChild(div);
    });
}

// --- Log with filtering ---

function setLogFilter(filter) {
    logFilter = filter;
    document.querySelectorAll('.log-filter-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.filter === filter);
    });
    renderLog();
}

function renderLog() {
    const container = document.getElementById('log-container');
    let entries = currentLog;

    if (logFilter !== 'all') {
        entries = entries.filter(e => e.Source === logFilter);
    }

    entries = entries.slice(-25).reverse();

    container.innerHTML = entries.map(entry => {
        const sourceColor = {
            'System': 'var(--yellow)',
            'Captain': 'var(--cyan)',
            'Navigator': 'var(--green)',
            'Engineer': 'var(--orange)',
            'Tactical': 'var(--red)',
            'Navigation': 'var(--blue)',
        }[entry.Source] || 'var(--cyan)';

        return `
            <div class="log-entry">
                <span class="log-time">${formatTime(entry.Timestamp)}</span>
                <span class="log-source" style="color:${sourceColor};">${entry.Source}</span>
                ${entry.Message}
            </div>
        `;
    }).join('');
}

// --- Commands ---

function approveOverlay(overlayId) {
    client.sendCommand('ApproveOverlay', { overlay_id: overlayId });
}

function dismissOverlay(overlayId) {
    client.sendCommand('DismissOverlay', { overlay_id: overlayId });
}

function resolveDecision(decisionId, optionId) {
    client.sendCommand('ResolveDecision', { decision_id: decisionId, option_id: optionId });
}

function requestStatus(target) {
    client.sendCommand('RequestStatus', { target });
    client.showToast(`Status angefordert: ${target}`, 'info');
}

client.connect();
