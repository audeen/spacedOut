/**
 * SpacedOut - Gunner Station UI Logic
 * Target selection, firing, weapon modes, ammo management
 */

/** Fixed tiers 0–5: green / yellow / red thresholds at 3 and 5. */
function threatTierCssColor(t) {
    const n = Math.round(Number(t));
    if (n >= 5) return 'var(--red)';
    if (n >= 3) return 'var(--yellow)';
    return 'var(--green)';
}

let selectedGunnerTarget = null;
let gunnerContacts = [];
let weaponMode = 'Precision';
let toolMode = 'Combat';
let drillTarget = '';
let isDefensiveMode = false;
let isAutofire = false;
let lastGunnerShotFeedbackSeq = 0;
let gunnerShotFeedbackSynced = false;

let _lastDrillInfoKey = '';

client.on('welcome', () => {
    client.selectRole('Gunner');
});

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('target-list').addEventListener('click', (e) => {
        const item = e.target.closest('[data-contact-id]');
        if (item) selectTarget(item.dataset.contactId);
    });
});

client.onState((msg) => {
    if (!msg.data) return;
    const d = msg.data;

    updatePhaseHeaderFromState(msg);
    updateEventIndicator(d);
    document.getElementById('timer-display').textContent = formatTime(msg.elapsed_time || 0);

    gunnerContacts = d.contacts || [];
    selectedGunnerTarget = d.selected_target || null;
    weaponMode = d.weapon_mode || 'Precision';
    toolMode = d.tool_mode || 'Combat';
    drillTarget = d.drill_target || '';
    isDefensiveMode = d.is_defensive_mode || false;
    isAutofire = d.is_autofire || false;

    const shotSeq = d.gunner_shot_feedback_seq ?? 0;
    const shotText = d.gunner_shot_feedback || '';
    if (!gunnerShotFeedbackSynced) {
        lastGunnerShotFeedbackSeq = shotSeq;
        gunnerShotFeedbackSynced = true;
    } else if (shotSeq > lastGunnerShotFeedbackSeq && shotText) {
        lastGunnerShotFeedbackSeq = shotSeq;
        let kind = 'hit';
        if (shotText.startsWith('Daneben')) kind = 'miss';
        else if (shotText.startsWith('Vernichtung')) kind = 'kill';
        client.showShotFeedback(shotText, kind);
    }

    const weaponHeat = d.weapon_heat ?? 0;
    const weaponEnergy = d.weapon_energy ?? 25;
    const weaponStatus = d.weapon_status || 'Operational';
    const lockProgress = d.target_lock_progress ?? 0;
    const fireCooldown = d.fire_cooldown ?? 0;

    document.getElementById('weapon-energy').textContent = weaponEnergy;
    const statusEl = document.getElementById('weapon-status');
    statusEl.textContent = getStatusLabel(weaponStatus);
    statusEl.className = `system-status ${getStatusClass(weaponStatus)}`;

    const engRule = d.engagement_rule || 'Standard';
    const engEl = document.getElementById('engagement-display');
    engEl.textContent = engRule.toUpperCase();
    engEl.style.color = engRule === 'Aggressive' ? 'var(--red)' : engRule === 'Defensive' ? 'var(--green)' : 'var(--cyan)';

    // Heat
    const heatEl = document.getElementById('heat-value');
    heatEl.textContent = `${Math.round(weaponHeat)}°`;
    heatEl.className = weaponHeat > 85 ? 'text-red' : weaponHeat > 50 ? 'text-yellow' : 'text-green';
    document.getElementById('heat-bar').style.width = `${Math.round(weaponHeat)}%`;
    const heatBarC = document.getElementById('heat-bar-container');
    heatBarC.className = `progress-bar ${weaponHeat > 85 ? 'progress-red' : weaponHeat > 50 ? 'progress-yellow' : 'progress-green'}`;

    const heatWarnEl = document.getElementById('heat-warning');
    if (heatWarnEl) {
        if (weaponHeat >= 95) {
            heatWarnEl.textContent = '⚠ ÜBERHITZT – Feuern gesperrt!';
            heatWarnEl.style.color = 'var(--red)';
            heatWarnEl.style.display = '';
        } else if (weaponHeat >= 70) {
            heatWarnEl.textContent = 'Hitze kritisch – Effizienz reduziert';
            heatWarnEl.style.color = 'var(--yellow)';
            heatWarnEl.style.display = '';
        } else {
            heatWarnEl.style.display = 'none';
        }
    }

    // Weapon mode buttons
    document.querySelectorAll('#weapon-mode-buttons .btn').forEach(btn => {
        const m = btn.dataset.mode;
        if (m === weaponMode) {
            btn.style.borderColor = 'var(--cyan)';
            btn.style.background = 'rgba(0, 200, 230, 0.15)';
            btn.className = 'btn btn-primary';
        } else {
            btn.style.borderColor = '';
            btn.style.background = '';
            btn.className = 'btn';
        }
    });

    // Target info
    updateTargetInfo(lockProgress, fireCooldown, weaponStatus);

    // Target list
    renderTargetList();

    // Defensive mode
    const defBtn = document.getElementById('defensive-btn');
    if (isDefensiveMode) {
        defBtn.textContent = 'Defensivfeuer: AN';
        defBtn.style.borderColor = 'var(--red)';
        defBtn.style.background = 'rgba(232, 48, 48, 0.15)';
    } else {
        defBtn.textContent = 'Defensivfeuer: AUS';
        defBtn.style.borderColor = '';
        defBtn.style.background = '';
    }

    const autoBtn = document.getElementById('autofire-btn');
    if (autoBtn) {
        if (isAutofire) {
            autoBtn.textContent = 'Autofeuer: AN';
            autoBtn.style.borderColor = 'var(--cyan)';
            autoBtn.style.background = 'rgba(0, 200, 230, 0.15)';
        } else {
            autoBtn.textContent = 'Autofeuer: AUS';
            autoBtn.style.borderColor = '';
            autoBtn.style.background = '';
        }
    }

    // Tool mode
    document.querySelectorAll('#tool-mode-buttons .btn').forEach(btn => {
        const m = btn.dataset.tool;
        if (m === toolMode) {
            btn.style.borderColor = m === 'Mining' ? 'var(--yellow)' : 'var(--cyan)';
            btn.style.background = m === 'Mining' ? 'rgba(230,200,50,0.15)' : 'rgba(0,200,230,0.15)';
            btn.classList.add('btn-primary');
        } else {
            btn.style.borderColor = '';
            btn.style.background = '';
            btn.classList.remove('btn-primary');
        }
    });

    // Drill section
    const drillPanel = document.getElementById('drill-panel');
    if (drillPanel) {
        if (toolMode === 'Mining') {
            drillPanel.style.display = '';
            updateDrillInfo();
        } else {
            drillPanel.style.display = 'none';
        }
    }

    // Hide fire controls in Mining mode
    const fireControls = document.getElementById('fire-controls');
    if (fireControls) fireControls.style.display = toolMode === 'Mining' ? 'none' : '';
    const autoBtnMining = document.getElementById('autofire-btn');
    if (autoBtnMining) autoBtnMining.style.display = toolMode === 'Mining' ? 'none' : '';

    updateDockStatusPanel(d);
});

function updateDockStatusPanel(d) {
    const panel = document.getElementById('dock-status-panel');
    const text = document.getElementById('dock-status-text');
    if (!panel || !text) return;

    const dock = d.dock;
    if (!dock || dock.Available === false) {
        panel.style.display = 'none';
        return;
    }

    panel.style.display = 'block';
    if (d.mission_docked) {
        text.textContent = 'Angedockt: Waffen gesichert — keine Feuerfreigabe.';
        text.className = 'text-yellow';
    } else {
        text.textContent = 'Station-Sektor — Dock in Reichweite.';
        text.className = 'text-cyan';
    }
}

function updateTargetInfo(lockProgress, fireCooldown, weaponStatus) {
    const infoEl = document.getElementById('target-info');
    const lockContainer = document.getElementById('lock-progress-container');
    const fireBtn = document.getElementById('fire-btn');
    const ceaseBtn = document.getElementById('cease-fire-btn');

    if (!selectedGunnerTarget) {
        infoEl.innerHTML = '<div class="text-dim" style="text-align:center;">Kein Ziel ausgewählt</div>';
        lockContainer.style.display = 'none';
        fireBtn.disabled = true;
        ceaseBtn.style.display = 'none';
        return;
    }

    const contact = gunnerContacts.find(c => c.Id === selectedGunnerTarget);
    if (!contact) {
        infoEl.innerHTML = '<div class="text-dim" style="text-align:center;">Ziel verloren</div>';
        lockContainer.style.display = 'none';
        fireBtn.disabled = true;
        ceaseBtn.style.display = 'none';
        return;
    }

    const shipX = client._lastState?.data?.ship_x ?? 0;
    const shipY = client._lastState?.data?.ship_y ?? 0;
    const dx = contact.PositionX - shipX;
    const dy = contact.PositionY - shipY;
    const dist = Math.round(Math.sqrt(dx * dx + dy * dy));

    const color = contact.Type === 'Hostile' ? '#e83030' :
                  contact.Type === 'Friendly' ? '#2ee65a' :
                  contact.Type === 'Unknown' ? '#e8d020' : '#606880';

    let badges = '';
    if (contact.IsDesignated) badges += '<span style="color:var(--cyan); font-size:11px; margin-left:6px;">[DESIGNIERT]</span>';
    if (contact.HasWeakness) badges += '<span style="color:var(--green); font-size:11px; margin-left:6px;">[SCHWACHSTELLE]</span>';

    const hpPct = contact.MaxHitPoints > 0 ? Math.round((contact.HitPoints / contact.MaxHitPoints) * 100) : 100;
    const hpColor = hpPct > 60 ? 'var(--green)' : hpPct > 30 ? 'var(--yellow)' : 'var(--red)';

    infoEl.innerHTML = `
        <div style="font-size:16px; font-weight:700;"><span style="color:${color};">●</span> ${contact.DisplayName || contact.Id}${badges}</div>
        <div class="status-row"><span class="status-label">Entfernung</span><span class="status-value text-cyan" style="font-size:16px;">${dist}</span></div>
        <div class="status-row"><span class="status-label">Bedrohung</span><span class="status-value" style="color:${threatTierCssColor(contact.ThreatLevel)};">${Math.round(contact.ThreatLevel)}</span></div>
        <div class="status-row"><span class="status-label">Integrität</span><span class="status-value" style="color:${hpColor};">${hpPct}%</span></div>
        <div class="progress-bar" style="height:4px; margin-top:4px;"><div class="progress-fill" style="width:${hpPct}%; background:${hpColor};"></div></div>
    `;

    // Lock progress
    if (lockProgress < 100) {
        lockContainer.style.display = '';
        document.getElementById('lock-progress-value').textContent = `${Math.round(lockProgress)}%`;
        document.getElementById('lock-progress-bar').style.width = `${Math.round(lockProgress)}%`;
        fireBtn.disabled = true;
    } else {
        lockContainer.style.display = 'none';
        const heat = client._lastState?.data?.weapon_heat ?? 0;
        const canFire = fireCooldown <= 0 && weaponStatus !== 'Offline' && heat < 95;
        fireBtn.disabled = !canFire;
    }

    ceaseBtn.style.display = '';
}

let _lastTargetListKey = '';
function renderTargetList() {
    const container = document.getElementById('target-list');
    if (gunnerContacts.length === 0) {
        const emptyMsg = toolMode === 'Mining'
            ? 'Keine bohrbaren POI-Ziele (analysiert & mit Bohrer)'
            : 'Keine feindlichen oder priorisierten Ziele';
        const emptyKey = `empty-${toolMode}`;
        if (_lastTargetListKey !== emptyKey) {
            container.innerHTML = `<div class="text-dim" style="text-align:center; padding:8px; font-size:13px;">${emptyMsg}</div>`;
            _lastTargetListKey = emptyKey;
        }
        return;
    }

    const sorted = [...gunnerContacts].sort((a, b) => b.ThreatLevel - a.ThreatLevel);
    const key = sorted.map(c => `${c.Id}:${Math.round(c.ThreatLevel)}:${c.IsDesignated}:${Math.round(c.HitPoints)}`).join('|');
    if (key === _lastTargetListKey) return;
    _lastTargetListKey = key;

    container.innerHTML = sorted.map(c => {
        const isSelected = c.Id === selectedGunnerTarget;
        const color = c.Type === 'Hostile' ? '#e83030' :
                      c.Type === 'Friendly' ? '#2ee65a' :
                      c.Type === 'Unknown' ? '#e8d020' : '#606880';

        const shipX = client._lastState?.data?.ship_x ?? 0;
        const shipY = client._lastState?.data?.ship_y ?? 0;
        const dx = c.PositionX - shipX;
        const dy = c.PositionY - shipY;
        const dist = Math.round(Math.sqrt(dx * dx + dy * dy));

        const badges = (c.IsDesignated ? '★ ' : '') + (c.HasWeakness ? '◎ ' : '');
        const targeting = c.IsTargetingPlayer ? '<span style="color:var(--red); font-size:10px;"> ⚠ ZIELT AUF UNS</span>' : '';

        return `<div class="contact-item ${isSelected ? 'selected' : ''}" data-contact-id="${c.Id}" style="${isSelected ? 'border-left:3px solid var(--cyan); background:rgba(0,200,230,0.08);' : ''}">
            <div class="contact-dot" style="background:${color};"></div>
            <div class="contact-info">
                <div class="contact-name">${badges}${c.DisplayName || c.Id}${targeting}</div>
                <div class="contact-meta">Dist: ${dist} · Stufe: ${Math.round(c.ThreatLevel)} · HP: ${Math.round(c.HitPoints)}</div>
            </div>
        </div>`;
    }).join('');
}

// Commands
function selectTarget(contactId) {
    client.sendCommand('SelectTarget', { contact_id: contactId });
}

function fire() {
    client.sendCommand('Fire', {});
}

function ceaseFire() {
    client.sendCommand('CeaseFire', {});
}

function setWeaponMode(mode) {
    client.sendCommand('SetWeaponMode', { mode });
}

function toggleDefensiveMode() {
    client.sendCommand('SetDefensiveMode', { enabled: !isDefensiveMode });
}

function toggleAutofire() {
    client.sendCommand('SetAutofire', { enabled: !isAutofire });
}

function setToolMode(mode) {
    client.sendCommand('SetToolMode', { mode });
    client.showToast(mode === 'Mining' ? 'Werkzeugmodus: MINING' : 'Werkzeugmodus: COMBAT', 'info');
}

/** Sends DrillTarget with the clicked contact id (server does not require prior selected_target sync). */
function startDrillForContact(contactId) {
    if (!contactId) return;
    selectTarget(contactId);
    client.sendCommand('DrillTarget', { contact_id: contactId });
    client.showToast('Bohrung gestartet', 'info');
}

function drillSelected(contactId) {
    const id = contactId || selectedGunnerTarget;
    if (!id) return;
    client.sendCommand('DrillTarget', { contact_id: id });
    client.showToast('Bohrung gestartet', 'info');
}

function escapeHtml(s) {
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function updateDrillInfo() {
    const drillInfo = document.getElementById('drill-info');
    if (!drillInfo) return;

    if (drillTarget) {
        const contact = gunnerContacts.find(c => c.Id === drillTarget);
        if (!contact) {
            const key = 'lost';
            if (_lastDrillInfoKey !== key) {
                _lastDrillInfoKey = key;
                drillInfo.innerHTML = '<div class="text-dim">Bohrziel verloren</div>';
            }
            return;
        }

        const title = escapeHtml(contact.DisplayName || contact.Id);
        const structureNeeded = _lastDrillInfoKey !== `drilling:${drillTarget}`
            || !drillInfo.querySelector('.drill-progress-fill');

        if (structureNeeded) {
            _lastDrillInfoKey = `drilling:${drillTarget}`;
            const pr = Math.round(contact.PoiProgress || 0);
            drillInfo.innerHTML = `
                <div style="font-size:14px; font-weight:600; color:var(--yellow);">Bohrziel: ${title}</div>
                <div class="progress-bar progress-yellow" style="height:6px; margin-top:4px;">
                    <div class="progress-fill drill-progress-fill" style="width:${pr}%"></div>
                </div>
                <div class="text-dim mt-4 drill-progress-label" style="font-size:12px;">${pr}%</div>
            `;
            return;
        }

        const pr = Math.round(contact.PoiProgress || 0);
        const fill = drillInfo.querySelector('.drill-progress-fill');
        const label = drillInfo.querySelector('.drill-progress-label');
        if (fill) fill.style.width = `${pr}%`;
        if (label) label.textContent = `${pr}%`;
        return;
    }

    const poiTargets = gunnerContacts.filter(c => c.PoiType && c.PoiPhase === 'Analyzed');
    if (poiTargets.length > 0) {
        const key = `pick:${poiTargets.map(c => c.Id).sort().join(',')}`;
        if (key !== _lastDrillInfoKey) {
            _lastDrillInfoKey = key;
            drillInfo.innerHTML = poiTargets.map(c =>
                `<button type="button" class="btn btn-warning btn-block btn-sm" onclick='startDrillForContact(${JSON.stringify(c.Id)})'>
                    Bohren: ${escapeHtml(c.DisplayName || c.Id)}
                </button>`
            ).join('');
        }
    } else {
        const key = 'empty';
        if (key !== _lastDrillInfoKey) {
            _lastDrillInfoKey = key;
            drillInfo.innerHTML = '<div class="text-dim" style="text-align:center;">Kein POI bereit für Bohrung</div>';
        }
    }
}

client.on('paused', () => document.getElementById('paused-overlay').classList.remove('hidden'));
client.on('resumed', () => document.getElementById('paused-overlay').classList.add('hidden'));
client.on('mission_ended', (msg) => {
    document.getElementById('mission-end').classList.remove('hidden');
    document.getElementById('mission-end-title').textContent = 'MISSION BEENDET';
    document.getElementById('mission-end-detail').textContent = `Ergebnis: ${msg.result}`;
});

client.connect();
