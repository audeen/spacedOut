/**
 * SpacedOut - Engineer Station UI Logic
 * Auto-balancing energy sliders, impact preview, heat monitoring
 */

let localEnergy = { drive: 25, shields: 25, sensors: 25, weapons: 25 };
let serverEnergy = { drive: 25, shields: 25, sensors: 25, weapons: 25 };
let isAdjusting = false;
let hasUnappliedChanges = false;

const PRESETS = {
    balanced: { drive: 25, shields: 25, sensors: 25, weapons: 25 },
    drive:    { drive: 50, shields: 17, sensors: 17, weapons: 16 },
    shields:  { drive: 17, shields: 50, sensors: 17, weapons: 16 },
    sensors:  { drive: 17, shields: 17, sensors: 50, weapons: 16 },
    weapons:  { drive: 17, shields: 16, sensors: 17, weapons: 50 },
};

client.on('welcome', () => {
    client.selectRole('Engineer');
});

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('systems-container').addEventListener('click', (e) => {
        const btn = e.target.closest('[data-repair-system]');
        if (btn) startRepair(btn.dataset.repairSystem);
    });
});

client.onState((msg) => {
    if (!msg.data) return;
    const d = msg.data;

    updatePhaseHeaderFromState(msg);
    document.getElementById('timer-display').textContent = formatTime(msg.elapsed_time || 0);

    const hull = d.hull_integrity ?? 100;
    const hullEl = document.getElementById('hull-value');
    hullEl.textContent = `${Math.round(hull)}%`;
    hullEl.className = `status-value ${hull > 60 ? 'text-green' : hull > 30 ? 'text-yellow' : 'text-red'}`;
    document.getElementById('hull-bar').style.width = `${hull}%`;
    document.getElementById('hull-bar').parentElement.className =
        `progress-bar ${hull > 60 ? 'progress-green' : hull > 30 ? 'progress-yellow' : 'progress-red'}`;

    if (d.energy) {
        serverEnergy = {
            drive: d.energy.Drive ?? 25,
            shields: d.energy.Shields ?? 25,
            sensors: d.energy.Sensors ?? 25,
            weapons: d.energy.Weapons ?? 25
        };
        if (!isAdjusting) {
            localEnergy = { ...serverEnergy };
            hasUnappliedChanges = false;
            updateSliders();
        }
    }

    renderSystems(d.systems || {});
    renderEvents(d.active_events || []);
    updateCoolantButtons(d.systems || {});

    const sparesEl = document.getElementById('spare-parts-display');
    if (sparesEl) sparesEl.textContent = d.spare_parts ?? 0;

    updatePoiExtractPanel(d.poi_contacts || [], d.ship_x ?? 0, d.ship_y ?? 0);
});

client.on('paused', () => document.getElementById('paused-overlay').classList.remove('hidden'));
client.on('resumed', () => document.getElementById('paused-overlay').classList.add('hidden'));
client.on('mission_ended', (msg) => {
    document.getElementById('mission-end').classList.remove('hidden');
    document.getElementById('mission-end-title').textContent = 'MISSION BEENDET';
    document.getElementById('mission-end-detail').textContent = `Ergebnis: ${msg.result}`;
});

// --- Auto-Balancing Energy Sliders ---

function onSliderInput(changed) {
    isAdjusting = true;
    const keys = ['drive', 'shields', 'sensors', 'weapons'];
    const others = keys.filter(k => k !== changed);

    const newVal = parseInt(document.getElementById(`energy-${changed}`).value);
    const oldVal = localEnergy[changed];
    const delta = newVal - oldVal;

    localEnergy[changed] = newVal;

    const otherTotal = others.reduce((s, k) => s + localEnergy[k], 0);
    if (otherTotal > 0 && delta !== 0) {
        let remaining = -delta;
        for (let i = 0; i < others.length; i++) {
            const k = others[i];
            if (i === others.length - 1) {
                localEnergy[k] = Math.max(0, Math.min(80, localEnergy[k] + remaining));
            } else {
                const share = Math.round(remaining * (localEnergy[k] / otherTotal));
                const clamped = Math.max(0, Math.min(80, localEnergy[k] + share));
                remaining -= (clamped - localEnergy[k]);
                localEnergy[k] = clamped;
            }
        }
    } else if (otherTotal === 0 && delta > 0) {
        const each = Math.floor(-delta / others.length);
        let leftover = -delta - each * others.length;
        others.forEach((k, i) => {
            localEnergy[k] = Math.max(0, localEnergy[k] + each + (i < leftover ? -1 : 0));
        });
    }

    let total = keys.reduce((s, k) => s + localEnergy[k], 0);
    if (total !== 100) {
        const diff = 100 - total;
        for (const k of others) {
            const adj = Math.max(0, Math.min(80, localEnergy[k] + diff));
            if (adj !== localEnergy[k]) {
                localEnergy[k] = adj;
                break;
            }
        }
    }

    hasUnappliedChanges = true;
    updateSliders();

    clearTimeout(window._adjustTimeout);
    window._adjustTimeout = setTimeout(() => { isAdjusting = false; }, 2000);
}

function updateSliders() {
    const keys = ['drive', 'shields', 'sensors', 'weapons'];
    keys.forEach(k => {
        document.getElementById(`energy-${k}`).value = localEnergy[k];
        const valEl = document.getElementById(`energy-${k}-value`);
        valEl.textContent = localEnergy[k];

        const diff = localEnergy[k] - serverEnergy[k];
        if (diff > 0) {
            valEl.className = 'energy-value text-green';
        } else if (diff < 0) {
            valEl.className = 'energy-value text-red';
        } else {
            valEl.className = 'energy-value text-cyan';
        }
    });

    const total = localEnergy.drive + localEnergy.shields + localEnergy.sensors + localEnergy.weapons;
    const totalEl = document.getElementById('energy-total');
    const isValid = total === 100;
    totalEl.innerHTML = `Gesamt: <span class="${isValid ? 'text-cyan' : 'text-red'}">${total}</span> / 100`;
    document.getElementById('apply-energy-btn').disabled = !isValid;

    const hint = document.getElementById('energy-changed-hint');
    hint.classList.toggle('hidden', !hasUnappliedChanges);

    updateImpactPreview();
}

function updateImpactPreview() {
    const baseRef = 25;
    const systems = [
        { key: 'drive', label: 'Geschwindigkeit' },
        { key: 'shields', label: 'Schutz' },
        { key: 'sensors', label: 'Scan-Speed' },
        { key: 'weapons', label: 'Feuerkraft' },
    ];

    systems.forEach(sys => {
        const pct = Math.round((localEnergy[sys.key] / baseRef) * 100);
        const fill = document.getElementById(`impact-${sys.key}-fill`);
        const val = document.getElementById(`impact-${sys.key}-val`);

        fill.style.width = `${Math.min(pct, 200)}%`;

        if (pct > 120) {
            fill.style.background = 'var(--green)';
            val.className = 'impact-value text-green';
        } else if (pct < 60) {
            fill.style.background = 'var(--red)';
            val.className = 'impact-value text-red';
        } else if (pct < 85) {
            fill.style.background = 'var(--yellow)';
            val.className = 'impact-value text-yellow';
        } else {
            fill.style.background = 'var(--cyan)';
            val.className = 'impact-value text-cyan';
        }

        val.textContent = `${pct}%`;
    });
}

function applyPreset(name) {
    const preset = PRESETS[name];
    if (!preset) return;
    isAdjusting = true;
    localEnergy = { ...preset };
    hasUnappliedChanges = true;
    updateSliders();
    clearTimeout(window._adjustTimeout);
    window._adjustTimeout = setTimeout(() => { isAdjusting = false; }, 2000);
}

function applyEnergy() {
    const total = localEnergy.drive + localEnergy.shields + localEnergy.sensors + localEnergy.weapons;
    if (total !== 100) {
        client.showToast('Energie muss genau 100 ergeben!', 'danger');
        return;
    }

    client.sendCommand('SetEnergyDistribution', {
        drive: localEnergy.drive,
        shields: localEnergy.shields,
        sensors: localEnergy.sensors,
        weapons: localEnergy.weapons
    });
    isAdjusting = false;
    hasUnappliedChanges = false;
    updateSliders();
    client.showToast('Energieverteilung angewendet', 'info');
}

// --- System Status Rendering ---

let _lastSystemsKey = '';

function renderSystems(systems) {
    const container = document.getElementById('systems-container');
    const systemNames = { Drive: 'Antrieb', Shields: 'Schilde', Sensors: 'Sensorik', Weapons: 'Waffen' };
    const systemIcons = { Drive: '⚡', Shields: '🛡', Sensors: '📡', Weapons: '🔫' };

    const key = Object.entries(systems).map(([k, s]) =>
        `${k}:${s.Status}:${Math.round(s.Heat ?? 0)}:${s.IsRepairing}:${Math.round(s.RepairProgress ?? 0)}:${Math.round(s.CoolantCooldown ?? 0)}`
    ).join('|');
    if (key === _lastSystemsKey) return;
    _lastSystemsKey = key;

    let html = '';
    for (const [key, sys] of Object.entries(systems)) {
        const name = systemNames[key] || key;
        const icon = systemIcons[key] || '⚙';
        const status = sys.Status || 'Operational';
        const heat = sys.Heat ?? 0;
        const repairing = sys.IsRepairing || false;
        const repairProg = sys.RepairProgress ?? 0;

        const heatColor = heat > 80 ? 'text-red' : heat > 50 ? 'text-yellow' : 'text-green';
        const heatBarClass = heat > 80 ? 'progress-red' : heat > 50 ? 'progress-yellow' : 'progress-green';
        const heatPulse = heat > 80 ? 'heat-pulse' : '';

        const repairEta = repairing ? Math.ceil((100 - repairProg) / 8) : 0;

        html += `
            <div class="system-card ${status !== 'Operational' ? 'system-damaged' : ''} ${heatPulse}">
                <div class="status-row">
                    <span style="font-size:15px; font-weight:600;">${icon} ${name}</span>
                    <span class="system-status ${getStatusClass(status)}">${getStatusLabel(status)}</span>
                </div>

                <div class="heat-section">
                    <div class="status-row" style="padding:2px 0;">
                        <span class="status-label">Hitze</span>
                        <span class="${heatColor}" style="font-family:var(--font-mono); font-size:14px; font-weight:700;">
                            ${Math.round(heat)}°
                        </span>
                    </div>
                    <div class="progress-bar ${heatBarClass}" style="height:6px;">
                        <div class="progress-fill" style="width:${Math.round(heat)}%"></div>
                    </div>
                    <div style="display:flex; justify-content:space-between; font-size:9px; color:var(--dim); margin-top:2px;">
                        <span>0</span>
                        <span style="color:var(--yellow);">80</span>
                        <span style="color:var(--red);">95</span>
                        <span>100</span>
                    </div>
                </div>

                ${repairing ? `
                    <div class="repair-section">
                        <div class="status-row" style="padding:2px 0;">
                            <span style="color:var(--cyan); font-size:13px; font-weight:600;">
                                Reparatur läuft
                            </span>
                            <span class="text-cyan" style="font-family:var(--font-mono); font-size:13px;">
                                ${Math.round(repairProg)}% · ~${repairEta}s
                            </span>
                        </div>
                        <div class="progress-bar progress-cyan" style="height:6px;">
                            <div class="progress-fill repair-fill-animated" style="width:${Math.round(repairProg)}%"></div>
                        </div>
                    </div>
                ` : ''}

                ${status !== 'Operational' && !repairing ? `
                    <button class="btn btn-success btn-sm btn-block mt-4" data-repair-system="${key}">
                        Reparatur starten
                    </button>
                ` : ''}
            </div>
        `;
    }
    container.innerHTML = html;
}

let _lastEngEventsKey = '';

function renderEvents(events) {
    const panel = document.getElementById('events-panel');
    const container = document.getElementById('events-container');
    const active = events.filter(e => e.IsActive);

    const key = active.map(e => `${e.Id}:${Math.round(e.TimeRemaining)}`).join('|');
    if (key === _lastEngEventsKey) return;
    _lastEngEventsKey = key;

    if (active.length === 0) {
        panel.style.display = 'none';
        container.innerHTML = '';
        return;
    }
    panel.style.display = '';
    container.innerHTML = active.map(evt => `
        <div class="alert alert-warning">
            <strong>${evt.Title}</strong>
            ${evt.TimeRemaining > 0 ? `<span style="float:right; font-family:var(--font-mono);">${Math.round(evt.TimeRemaining)}s</span>` : ''}
            <br><span style="font-size:12px;">${evt.Description}</span>
        </div>
    `).join('');
}

function startRepair(system) {
    client.sendCommand('StartRepair', { system });
    client.showToast(`Reparatur gestartet: ${system}`, 'info');
}

function emergencyShutdown(system) {
    client.sendCommand('TriggerEmergencyShutdown', { system });
    client.showToast(`Notabschaltung: ${system}`, 'danger');
}

function raiseWarning() {
    const message = prompt('Warnmeldung eingeben:');
    if (message && message.trim()) {
        client.sendCommand('RaiseSystemWarning', { message: message.trim() });
        client.showToast('Warnung gesendet', 'warning');
    }
}

function updateCoolantButtons(systems) {
    const sysKeys = ['Drive', 'Shields', 'Sensors', 'Weapons'];
    sysKeys.forEach(k => {
        const btn = document.getElementById(`coolant-btn-${k}`);
        if (!btn) return;
        const sys = systems[k];
        const cd = sys?.CoolantCooldown ?? 0;
        if (cd > 0) {
            btn.disabled = true;
            btn.textContent = `${Math.ceil(cd)}s`;
        } else {
            btn.disabled = false;
            const icons = { Drive: '⚡', Shields: '🛡', Sensors: '📡', Weapons: '🔫' };
            btn.textContent = `${icons[k]} Kühlen`;
        }
    });
}

function coolantPulse(system) {
    client.sendCommand('CoolantPulse', { system });
    client.showToast(`Kühlpuls: ${system}`, 'info');
}

function overchargeSystem(system) {
    client.sendCommand('OverchargeSystem', { system });
    client.showToast(`Overcharge: ${system}`, 'warning');
}

function convertSparesToAmmo() {
    client.sendCommand('ConvertSparesToAmmo', {});
    client.showToast('Ersatzteile zu Munition konvertiert', 'info');
}

function updatePoiExtractPanel(poiContacts, shipX, shipY) {
    const panel = document.getElementById('poi-extract-panel');
    const info = document.getElementById('poi-extract-info');
    if (!panel || !info) return;

    if (poiContacts.length === 0) {
        panel.style.display = 'none';
        info.replaceChildren();
        return;
    }

    panel.style.display = '';

    const activeIds = new Set(poiContacts.map((c) => c.Id));
    info.querySelectorAll('[data-poi-row]').forEach((el) => {
        if (!activeIds.has(el.dataset.poiRow)) el.remove();
    });

    let anyRow = false;

    poiContacts.forEach((c) => {
        const dx = c.PositionX - shipX;
        const dy = c.PositionY - shipY;
        const dist = Math.round(Math.sqrt(dx * dx + dy * dy));
        const phase = c.PoiPhase || 'None';
        const id = c.Id;
        const escId = typeof CSS !== 'undefined' && CSS.escape ? CSS.escape(id) : id.replace(/"/g, '');

        if (phase === 'Extracting') {
            anyRow = true;
            let row = info.querySelector(`[data-poi-row="${escId}"]`);
            const progress = Math.round(c.PoiProgress || 0);
            if (!row || row.dataset.poiMode !== 'extracting') {
                if (row) row.remove();
                row = document.createElement('div');
                row.dataset.poiRow = id;
                row.dataset.poiMode = 'extracting';
                row.style.marginBottom = '8px';
                row.innerHTML = `
                    <div class="poi-extract-title" style="font-size:14px;font-weight:600;color:var(--green);"></div>
                    <div class="progress-bar progress-green" style="height:6px;margin-top:4px;">
                        <div class="progress-fill poi-extract-progress-fill" style="width:0%"></div>
                    </div>
                    <div class="text-dim poi-extract-pct mt-4" style="font-size:12px;"></div>`;
                row.querySelector('.poi-extract-title').textContent = `Extraktion: ${c.DisplayName || ''}`;
                info.appendChild(row);
            }
            const fill = row.querySelector('.poi-extract-progress-fill');
            const pctEl = row.querySelector('.poi-extract-pct');
            if (fill) fill.style.width = `${progress}%`;
            if (pctEl) pctEl.textContent = `${progress}% · Dist: ${dist}`;
            return;
        }

        if (phase === 'Analyzed' || phase === 'Opened') {
            anyRow = true;
            let row = info.querySelector(`[data-poi-row="${escId}"]`);
            const ut = c.UsesTractorBeam;
            const legacyBoth = ut === undefined;

            if (!row || row.dataset.poiMode !== 'actions') {
                if (row) row.remove();
                row = document.createElement('div');
                row.dataset.poiRow = id;
                row.dataset.poiMode = 'actions';
                row.style.marginBottom = '8px';

                const title = document.createElement('div');
                title.style.fontSize = '13px';
                title.style.fontWeight = '600';
                const nameSpan = document.createElement('span');
                nameSpan.textContent = c.DisplayName || '';
                const distSpan = document.createElement('span');
                distSpan.className = 'text-dim poi-extract-dist';
                distSpan.textContent = ` (Dist: ${dist})`;
                title.appendChild(nameSpan);
                title.appendChild(distSpan);

                const grid = document.createElement('div');
                grid.className = 'btn-grid btn-grid-2';
                grid.style.marginTop = '4px';

                const addTractor = () => {
                    const b = document.createElement('button');
                    b.type = 'button';
                    b.className = 'btn btn-success btn-sm';
                    b.textContent = 'Traktorstrahl';
                    b.addEventListener('click', () => activateTractor(id));
                    grid.appendChild(b);
                };
                const addExtract = () => {
                    const b = document.createElement('button');
                    b.type = 'button';
                    b.className = 'btn btn-primary btn-sm';
                    b.textContent = 'Extraktion';
                    b.addEventListener('click', () => extractResource(id));
                    grid.appendChild(b);
                };

                if (legacyBoth) {
                    addTractor();
                    addExtract();
                } else if (ut === true) {
                    addTractor();
                } else {
                    addExtract();
                }

                row.appendChild(title);
                row.appendChild(grid);
                info.appendChild(row);
            } else {
                const distSpan = row.querySelector('.poi-extract-dist');
                if (distSpan) distSpan.textContent = ` (Dist: ${dist})`;
            }
        }
    });

    if (!anyRow) {
        if (!info.querySelector('.poi-wait-placeholder')) {
            info.replaceChildren();
            const wait = document.createElement('div');
            wait.className = 'text-dim poi-wait-placeholder';
            wait.style.textAlign = 'center';
            wait.textContent = 'Warte auf Gunner/Tactical...';
            info.appendChild(wait);
        }
    } else {
        const wait = info.querySelector('.poi-wait-placeholder');
        if (wait) wait.remove();
    }
}

function activateTractor(contactId) {
    client.sendCommand('ActivateTractor', { contact_id: contactId });
    client.showToast('Traktorstrahl aktiviert', 'info');
}

function extractResource(contactId) {
    client.sendCommand('ExtractResource', { contact_id: contactId });
    client.showToast('Extraktion gestartet', 'info');
}

client.connect();
