/**
 * SpacedOut - Tactical Station UI Logic
 * Radar sweep, scan progress rings, enhanced contact display
 */

let contacts = [];
let selectedContactId = null;
let tacticalOnMainScreen = false;

client.on('welcome', () => {
    client.selectRole('Tactical');
});

let shipPos = { x: 100, y: 100 };
let sensorRange = 500;
let canvas, ctx;
let sweepAngle = 0;
let animFrame;

let _contactElements = new Map();

document.addEventListener('DOMContentLoaded', () => {
    canvas = document.getElementById('tactical-canvas');
    ctx = canvas.getContext('2d');
    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);
    startAnimation();

    document.getElementById('contacts-container').addEventListener('click', (e) => {
        const item = e.target.closest('[data-contact-id]');
        if (item) selectContact(item.dataset.contactId);
    });
});

function resizeCanvas() {
    const container = canvas.parentElement;
    canvas.width = container.clientWidth;
    canvas.height = container.clientHeight || container.clientWidth * 0.67;
}

function startAnimation() {
    function frame() {
        sweepAngle = (sweepAngle + 0.008) % (Math.PI * 2);
        drawTactical();
        animFrame = requestAnimationFrame(frame);
    }
    frame();
}

function drawTactical() {
    if (!ctx) return;
    const w = canvas.width, h = canvas.height;

    ctx.fillStyle = '#030610';
    ctx.fillRect(0, 0, w, h);

    const centerX = w * 0.5;
    const centerY = h * 0.5;
    const maxRadius = Math.min(w, h) * 0.42;
    const scale = maxRadius / sensorRange;

    // Radar sweep
    drawSweepFallback(centerX, centerY, maxRadius);

    // Range circles
    for (let r = 1; r <= 3; r++) {
        const radius = (sensorRange / 3) * r * scale;
        ctx.strokeStyle = `rgba(0, 200, 230, ${0.06 + r * 0.03})`;
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(centerX, centerY, radius, 0, Math.PI * 2);
        ctx.stroke();

        ctx.fillStyle = 'rgba(0,200,230,0.25)';
        ctx.font = '9px monospace';
        ctx.textAlign = 'left';
        ctx.fillText(`${Math.round(sensorRange / 3 * r)}`, centerX + radius + 4, centerY - 2);
    }

    // Cross-hair
    ctx.strokeStyle = 'rgba(0, 200, 230, 0.1)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(centerX - maxRadius, centerY);
    ctx.lineTo(centerX + maxRadius, centerY);
    ctx.moveTo(centerX, centerY - maxRadius);
    ctx.lineTo(centerX, centerY + maxRadius);
    ctx.stroke();

    // Diagonal cross-hair
    ctx.strokeStyle = 'rgba(0, 200, 230, 0.05)';
    const diag = maxRadius * 0.707;
    ctx.beginPath();
    ctx.moveTo(centerX - diag, centerY - diag);
    ctx.lineTo(centerX + diag, centerY + diag);
    ctx.moveTo(centerX + diag, centerY - diag);
    ctx.lineTo(centerX - diag, centerY + diag);
    ctx.stroke();

    // Ship marker
    ctx.fillStyle = '#00d4e8';
    ctx.beginPath();
    ctx.arc(centerX, centerY, 3, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = 'rgba(0, 200, 230, 0.3)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.arc(centerX, centerY, 8, 0, Math.PI * 2);
    ctx.stroke();

    // Contacts
    contacts.forEach(c => {
        const dx = c.PositionX - shipPos.x;
        const dy = c.PositionY - shipPos.y;
        const dist = Math.hypot(dx, dy);
        const cx = centerX + dx * scale;
        const cy = centerY + dy * scale;

        const color = c.Type === 'Friendly' ? '#2ee65a' :
                      c.Type === 'Hostile' ? '#e83030' :
                      c.Type === 'Unknown' ? '#e8d020' :
                      c.Type === 'Anomaly' ? '#4080f0' : '#606880';

        const isSelected = c.Id === selectedContactId;

        // Selection ring
        if (isSelected) {
            ctx.strokeStyle = '#00d4e8';
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(cx, cy, 16, 0, Math.PI * 2);
            ctx.stroke();
        }

        // Scan progress arc
        if (c.ScanProgress > 0 && c.ScanProgress < 100) {
            const scanRadius = 12;
            ctx.strokeStyle = 'rgba(0, 200, 230, 0.6)';
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(cx, cy, scanRadius, -Math.PI / 2,
                    -Math.PI / 2 + (c.ScanProgress / 100) * Math.PI * 2);
            ctx.stroke();

            // Scan glow when actively scanning
            if (c.IsScanning) {
                ctx.strokeStyle = 'rgba(0, 200, 230, 0.2)';
                ctx.lineWidth = 4;
                ctx.beginPath();
                ctx.arc(cx, cy, scanRadius + 3, -Math.PI / 2,
                        -Math.PI / 2 + (c.ScanProgress / 100) * Math.PI * 2);
                ctx.stroke();
            }
        }

        // Contact dot
        const dotSize = isSelected ? 6 : 4;
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(cx, cy, dotSize, 0, Math.PI * 2);
        ctx.fill();

        // Scanning indicator
        if (c.IsScanning) {
            const pulseSize = 6 + Math.sin(Date.now() / 200) * 3;
            ctx.strokeStyle = color;
            ctx.lineWidth = 1;
            ctx.globalAlpha = 0.4;
            ctx.beginPath();
            ctx.arc(cx, cy, pulseSize + 4, 0, Math.PI * 2);
            ctx.stroke();
            ctx.globalAlpha = 1;
        }

        // Velocity vector
        if (c.VelocityX || c.VelocityY) {
            ctx.strokeStyle = color;
            ctx.lineWidth = 1;
            ctx.setLineDash([3, 3]);
            ctx.beginPath();
            ctx.moveTo(cx, cy);
            ctx.lineTo(cx + c.VelocityX * 30, cy + c.VelocityY * 30);
            ctx.stroke();
            ctx.setLineDash([]);
        }

        // Label
        ctx.fillStyle = 'rgba(255,255,255,0.75)';
        ctx.font = '10px sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText(c.DisplayName || c.Id, cx, cy - 18);

        // Threat badge
        if (c.ThreatLevel > 0) {
            const threatColor = c.ThreatLevel > 7 ? '#e83030' :
                               c.ThreatLevel > 4 ? '#e8d020' : '#606880';
            ctx.fillStyle = threatColor;
            ctx.font = 'bold 9px monospace';
            ctx.textAlign = 'left';
            ctx.fillText(`T${Math.round(c.ThreatLevel)}`, cx + dotSize + 4, cy + 3);
        }

        // Distance label
        ctx.fillStyle = 'rgba(255,255,255,0.35)';
        ctx.font = '8px monospace';
        ctx.textAlign = 'center';
        ctx.fillText(`${Math.round(dist)}`, cx, cy + 22);
    });
}

function drawSweepFallback(cx, cy, radius) {
    const sweepLen = 0.6;
    for (let i = 0; i < 20; i++) {
        const angle = sweepAngle - (i / 20) * sweepLen;
        const alpha = (1 - i / 20) * 0.08;
        ctx.strokeStyle = `rgba(0, 200, 230, ${alpha})`;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.lineTo(cx + Math.cos(angle) * radius, cy + Math.sin(angle) * radius);
        ctx.stroke();
    }
}

// --- State Updates ---

client.onState((msg) => {
    if (!msg.data) return;
    const d = msg.data;

    document.getElementById('phase-display').textContent = msg.mission_phase || '---';
    document.getElementById('timer-display').textContent = formatTime(msg.elapsed_time || 0);

    const sensorStatus = d.sensor_status || 'Operational';
    document.getElementById('sensor-status').innerHTML =
        `<span class="system-status ${getStatusClass(sensorStatus)}">${getStatusLabel(sensorStatus)}</span>`;

    document.getElementById('sensor-energy').textContent = d.sensor_energy ?? 33;

    sensorRange = d.sensor_range ?? 500;
    document.getElementById('sensor-range').textContent = Math.round(sensorRange);

    shipPos = { x: d.ship_x ?? 100, y: d.ship_y ?? 100 };
    contacts = d.contacts || [];

    tacticalOnMainScreen = d.tactical_on_main_screen || false;
    updateTacticalButton();

    renderContacts();
    renderEvents(d.active_events || []);
    updateContactActions();
});

client.on('paused', () => document.getElementById('paused-overlay').classList.remove('hidden'));
client.on('resumed', () => document.getElementById('paused-overlay').classList.add('hidden'));
client.on('mission_ended', (msg) => {
    document.getElementById('mission-end').classList.remove('hidden');
    document.getElementById('mission-end-title').textContent = 'MISSION BEENDET';
    document.getElementById('mission-end-detail').textContent = `Ergebnis: ${msg.result}`;
    cancelAnimationFrame(animFrame);
});

function renderContacts() {
    const container = document.getElementById('contacts-container');

    if (contacts.length === 0) {
        if (_contactElements.size > 0) {
            _contactElements.forEach(el => el.remove());
            _contactElements.clear();
        }
        if (!container.querySelector('.text-dim')) {
            container.innerHTML = '<div class="text-dim text-center" style="padding:12px;">Keine Kontakte</div>';
        }
        return;
    }

    const placeholder = container.querySelector('.text-dim');
    if (placeholder) placeholder.remove();

    const currentIds = new Set(contacts.map(c => c.Id));
    for (const [id, el] of _contactElements) {
        if (!currentIds.has(id)) {
            el.remove();
            _contactElements.delete(id);
        }
    }

    contacts.forEach((c, index) => {
        let el = _contactElements.get(c.Id);
        const isSelected = c.Id === selectedContactId;
        const scanning = c.IsScanning;

        if (!el) {
            el = document.createElement('div');
            el.dataset.contactId = c.Id;
            _contactElements.set(c.Id, el);
        }

        if (container.children[index] !== el) {
            if (index < container.children.length) {
                container.insertBefore(el, container.children[index]);
            } else {
                container.appendChild(el);
            }
        }

        const wantClass = `contact-item${isSelected ? ' selected' : ''}${scanning ? ' scanning-active' : ''}`;
        if (el.className !== wantClass) el.className = wantClass;

        const scanStr = c.ScanProgress < 100 ? `Scan: ${Math.round(c.ScanProgress)}%` : c.Type;
        const dist = Math.round(Math.hypot(c.PositionX - shipPos.x, c.PositionY - shipPos.y));
        const circumference = 2 * Math.PI * 16;
        const dashOffset = circumference - (c.ScanProgress / 100) * circumference;

        let inner;
        if (c.ScanProgress < 100 && c.ScanProgress > 0) {
            inner =
                `<div class="scan-ring"><svg viewBox="0 0 40 40">` +
                `<circle class="scan-ring-bg" cx="20" cy="20" r="16"/>` +
                `<circle class="scan-ring-fill" cx="20" cy="20" r="16" ` +
                `stroke-dasharray="${circumference}" stroke-dashoffset="${dashOffset}"/>` +
                `</svg><div class="scan-ring-text">${Math.round(c.ScanProgress)}%</div></div>` +
                `<div class="contact-info"><div class="contact-name">${c.DisplayName}${scanning ? ' ◉' : ''}</div>` +
                `<div class="contact-meta">${scanStr} · Dist: ${dist} · T:${Math.round(c.ThreatLevel)}</div></div>`;
        } else {
            inner =
                `<div class="contact-dot ${getContactDotClass(c.Type)}"></div>` +
                `<div class="contact-info"><div class="contact-name">${c.DisplayName}${scanning ? ' ◉' : ''}</div>` +
                `<div class="contact-meta">${scanStr} · Dist: ${dist} · T:${Math.round(c.ThreatLevel)}</div></div>`;
        }

        if (el._lastInner !== inner) {
            el.innerHTML = inner;
            el._lastInner = inner;
        }
    });
}

let _lastTacEventsKey = '';

function renderEvents(events) {
    const panel = document.getElementById('events-panel');
    const container = document.getElementById('events-container');
    const active = events.filter(e => e.IsActive);

    const key = active.map(e => `${e.Id}:${Math.round(e.TimeRemaining)}`).join('|');
    if (key === _lastTacEventsKey) return;
    _lastTacEventsKey = key;

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

function selectContact(contactId) {
    selectedContactId = contactId === selectedContactId ? null : contactId;
    renderContacts();
    updateContactActions();
}

function updateContactActions() {
    const panel = document.getElementById('contact-actions');
    if (!selectedContactId) {
        panel.style.display = 'none';
        return;
    }

    const contact = contacts.find(c => c.Id === selectedContactId);
    if (!contact) {
        panel.style.display = 'none';
        return;
    }

    panel.style.display = '';
    document.getElementById('selected-contact-name').textContent = contact.DisplayName;
    document.getElementById('threat-slider').value = Math.round(contact.ThreatLevel);
    document.getElementById('threat-value').textContent = Math.round(contact.ThreatLevel);

    const scanBtn = document.getElementById('scan-btn');
    if (contact.ScanProgress >= 100) {
        scanBtn.disabled = true;
        scanBtn.textContent = 'Scan komplett';
    } else if (contact.IsScanning) {
        scanBtn.disabled = true;
        scanBtn.textContent = `Scanne... ${Math.round(contact.ScanProgress)}%`;
    } else {
        scanBtn.disabled = false;
        scanBtn.textContent = 'Scannen';
    }
}

function scanSelected() {
    if (!selectedContactId) return;
    client.sendCommand('ScanContact', { contact_id: selectedContactId });
    client.showToast('Scan gestartet', 'info');
}

function markSelected() {
    if (!selectedContactId) return;
    client.sendCommand('MarkContact', { contact_id: selectedContactId });
    client.showToast('Marker angefordert', 'info');
}

function setThreatLevel() {
    if (!selectedContactId) return;
    const level = parseInt(document.getElementById('threat-slider').value);
    client.sendCommand('SetThreatPriority', {
        contact_id: selectedContactId,
        threat_level: level
    });
    client.showToast(`Bedrohungsstufe: ${level}`, 'info');
}

function raiseTacticalWarning() {
    const message = prompt('Taktische Warnung eingeben:');
    if (message && message.trim()) {
        client.sendCommand('RaiseTacticalWarning', { message: message.trim() });
        client.showToast('Warnung gesendet', 'warning');
    }
}

function toggleTacticalOnMainScreen() {
    client.sendCommand('ToggleTacticalOnMainScreen', {});
}

function updateTacticalButton() {
    const btn = document.getElementById('toggle-tactical-btn');
    if (!btn) return;
    if (tacticalOnMainScreen) {
        btn.textContent = 'Taktik ausblenden';
        btn.style.borderColor = 'var(--cyan)';
        btn.style.background = 'rgba(0, 200, 230, 0.15)';
    } else {
        btn.textContent = 'Taktik auf Hauptschirm';
        btn.style.borderColor = '';
        btn.style.background = '';
    }
}

client.connect();
