/**
 * SpacedOut - Tactical Station UI Logic
 * Radar sweep, fog-of-war, probe deployment, ghost contacts, velocity pips
 */

let contacts = [];
let contactActions = {};
let resourceZones = [];
let selectedContactId = null;
let tacticalOnMainScreen = false;
let probeMode = false;
let _lastActionsStructureKey = '';
let _lastActionsProgressSnapshot = '';
let _lastActionsContactId = null;

let probes = [];
let probeCharges = 3;
let probeMaxCharges = 5;
let probeRechargeTimer = 0;
let probeRechargeTime = 45;

client.on('welcome', () => {
    client.selectRole('Tactical');
});

let shipPos = { x: 100, y: 100, z: 500 };
let sensorRange = 500;
/** Max range if sensors were Operational (no status penalty); for jam annulus vs. sensorRange. */
let sensorRangeNominal = 500;
/** GetStatusMultiplier for Sensors (1 = Operational, lower = degraded). */
let sensorStatusMultRange = 1;
let canvas, ctx;
let sweepAngle = 0;
let animFrame;

let zoom = 1.0;
let panX = 0, panY = 0;
let isDragging = false;
let dragStart = { x: 0, y: 0 };
let panStart = { x: 0, y: 0 };
let touchStartDist = 0;
let touchStartZoom = 1;

let _contactElements = new Map();

document.addEventListener('DOMContentLoaded', () => {
    canvas = document.getElementById('tactical-canvas');
    ctx = canvas.getContext('2d');
    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);
    startAnimation();

    canvas.addEventListener('mousedown', onPointerDown);
    canvas.addEventListener('mousemove', onPointerMove);
    canvas.addEventListener('mouseup', onPointerUp);
    canvas.addEventListener('wheel', onWheel, { passive: false });

    canvas.addEventListener('touchstart', onTouchStart, { passive: false });
    canvas.addEventListener('touchmove', onTouchMove, { passive: false });
    canvas.addEventListener('touchend', onTouchEnd, { passive: false });

    document.getElementById('contacts-container').addEventListener('click', (e) => {
        const item = e.target.closest('[data-contact-id]');
        if (item) selectContact(item.dataset.contactId);
    });
});

function resizeCanvas() {
    const container = canvas.parentElement;
    const size = container.clientWidth;
    canvas.width = size;
    canvas.height = size;
}

function zoomIn() { zoom = Math.min(zoom * 1.4, 5); updateMapInfo(); }
function zoomOut() { zoom = Math.max(zoom / 1.4, 0.5); updateMapInfo(); }
function centerOnShip() { panX = 0; panY = 0; }

function updateMapInfo() {
    const el = document.getElementById('map-info');
    if (el) el.textContent = `Zoom: ${zoom.toFixed(1)}x`;
}

function onWheel(e) { e.preventDefault(); e.deltaY < 0 ? zoomIn() : zoomOut(); }

function getMapParams() {
    const w = canvas.width, h = canvas.height;
    const centerX = w * 0.5 + panX;
    const centerY = h * 0.5 + panY;
    const maxRadius = Math.min(w, h) * 0.42 * zoom;
    const refRange = Math.max(sensorRange, sensorRangeNominal);
    const scale = maxRadius / refRange;
    return { w, h, centerX, centerY, maxRadius, scale };
}

/** C# JsonSerializer sends DiscoveryState as 0–3; UI string checks need a name. */
const _DISCOVERY_NAMES = ['Hidden', 'Detected', 'Probed', 'Scanned'];
function contactDiscovery(c) {
    const d = c.Discovery;
    if (typeof d === 'number' && d >= 0 && d < _DISCOVERY_NAMES.length) return _DISCOVERY_NAMES[d];
    if (typeof d === 'string') return d;
    return 'Hidden';
}

/** Live probe ring covers this contact (2D, same as server); while true, Probed renders full, not ghost. */
function contactCoveredByActiveProbe(c) {
    if (contactDiscovery(c) !== 'Probed') return false;
    const px = c.PositionX, py = c.PositionY;
    for (const p of probes) {
        const dx = px - p.X, dy = py - p.Y;
        const r = p.RevealRadius ?? 150;
        if (Math.hypot(dx, dy) <= r) return true;
    }
    return false;
}

function isProbedGhost(c) {
    return contactDiscovery(c) === 'Probed' && !contactCoveredByActiveProbe(c);
}

/** Sensor-blips (Detected): inner third by default; RadarShowDetectedInFullRange uses full sensor circle. */
function isContactVisibleOnRadar(c) {
    const disc = contactDiscovery(c);
    if (disc === 'Hidden') return false;
    if (c.PreRevealed) return true;
    if (disc === 'Detected' && c.PersistDetectedBeyondSensorRange) return true;
    if (disc !== 'Detected') return true;
    const px = c.PositionX;
    const py = c.PositionY;
    const maxR = c.RadarShowDetectedInFullRange ? sensorRange : sensorRange / 3;
    return Math.hypot(px - shipPos.x, py - shipPos.y) <= maxR;
}

function screenToMap(clientX, clientY) {
    const rect = canvas.getBoundingClientRect();
    const sx = clientX - rect.left;
    const sy = clientY - rect.top;
    const { centerX, centerY, scale } = getMapParams();
    return {
        x: shipPos.x + (sx - centerX) / scale,
        y: shipPos.y + (sy - centerY) / scale,
    };
}

function hitTestContactScreen(clientX, clientY) {
    if (!canvas) return null;
    const rect = canvas.getBoundingClientRect();
    const cx = clientX - rect.left;
    const cy = clientY - rect.top;
    const { centerX, centerY, scale } = getMapParams();
    const hitR = Math.max(16, 12 * zoom);

    for (const c of contacts) {
        if (!isContactVisibleOnRadar(c)) continue;
        const isGhost = isProbedGhost(c);
        const dx = (isGhost ? c.SnapshotX : c.PositionX) - shipPos.x;
        const dy = (isGhost ? c.SnapshotY : c.PositionY) - shipPos.y;
        const sx = centerX + dx * scale;
        const sy = centerY + dy * scale;
        if (Math.hypot(cx - sx, cy - sy) < hitR) return c.Id;
    }
    return null;
}

function onPointerDown(e) {
    if (e.button !== 0) return;
    isDragging = true;
    dragStart = { x: e.clientX, y: e.clientY };
    panStart = { x: panX, y: panY };
}

function onPointerMove(e) {
    if (!isDragging) return;
    panX = panStart.x + (e.clientX - dragStart.x);
    panY = panStart.y + (e.clientY - dragStart.y);
}

function onPointerUp(e) {
    if (e.button !== 0) return;
    const dx = Math.abs(e.clientX - dragStart.x);
    const dy = Math.abs(e.clientY - dragStart.y);
    isDragging = false;

    if (dx < 5 && dy < 5) {
        if (probeMode) {
            const pos = screenToMap(e.clientX, e.clientY);
            deployProbeAt(pos.x, pos.y);
            return;
        }
        const hitId = hitTestContactScreen(e.clientX, e.clientY);
        if (hitId) selectContact(hitId);
    }
}

function onTouchStart(e) {
    if (e.touches.length === 2) {
        e.preventDefault();
        const t = e.touches;
        touchStartDist = Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);
        touchStartZoom = zoom;
        isDragging = false;
    } else if (e.touches.length === 1) {
        isDragging = true;
        const touch = e.touches[0];
        dragStart = { x: touch.clientX, y: touch.clientY };
        panStart = { x: panX, y: panY };
    }
}

function onTouchMove(e) {
    e.preventDefault();
    if (e.touches.length === 2) {
        const t = e.touches;
        const dist = Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);
        zoom = Math.max(0.5, Math.min(5, touchStartZoom * (dist / touchStartDist)));
        updateMapInfo();
    } else if (isDragging && e.touches.length === 1) {
        panX = panStart.x + (e.touches[0].clientX - dragStart.x);
        panY = panStart.y + (e.touches[0].clientY - dragStart.y);
    }
}

function onTouchEnd(e) {
    if (e.touches.length > 0) return;
    if (e.changedTouches.length === 1) {
        const touch = e.changedTouches[0];
        const dx = Math.abs(touch.clientX - dragStart.x);
        const dy = Math.abs(touch.clientY - dragStart.y);
        if (dx < 10 && dy < 10) {
            if (probeMode) {
                const pos = screenToMap(touch.clientX, touch.clientY);
                deployProbeAt(pos.x, pos.y);
            } else {
                const hitId = hitTestContactScreen(touch.clientX, touch.clientY);
                if (hitId) selectContact(hitId);
            }
        }
    }
    isDragging = false;
}

function startAnimation() {
    function frame() {
        sweepAngle = (sweepAngle + 0.008) % (Math.PI * 2);
        drawTactical();
        animFrame = requestAnimationFrame(frame);
    }
    frame();
}

// --- Drawing ---

function drawTactical() {
    if (!ctx) return;
    const { w, h, centerX, centerY, maxRadius, scale } = getMapParams();
    const zf = Math.max(1, zoom * 0.7);

    ctx.fillStyle = '#030610';
    ctx.fillRect(0, 0, w, h);

    drawFogOfWar(centerX, centerY, maxRadius, scale);
    drawSweepFallback(centerX, centerY, maxRadius);
    drawRangeRings(centerX, centerY, scale);
    drawCrosshairs(centerX, centerY, maxRadius);
    drawResourceZones(centerX, centerY, scale);
    drawProbeRings(centerX, centerY, scale);
    drawShipMarker(centerX, centerY, zf);
    drawContacts(centerX, centerY, scale, zf);
}

function drawFogOfWar(cx, cy, maxRadius, scale) {
    const rEff = sensorRange * scale;
    const rNom = sensorRangeNominal * scale;
    const jamSeverity = Math.max(0, Math.min(1, 1 - (sensorStatusMultRange ?? 1)));

    // Sensor interference: lost annulus between effective (reliable) and nominal (theoretical max)
    if (rNom > rEff + 0.5 && jamSeverity > 0.02) {
        const aFill = 0.06 + jamSeverity * 0.14;
        ctx.fillStyle = `rgba(220, 75, 35, ${aFill})`;
        ctx.beginPath();
        ctx.arc(cx, cy, rNom, 0, Math.PI * 2);
        ctx.arc(cx, cy, rEff, 0, Math.PI * 2, true);
        ctx.fill();

        ctx.strokeStyle = `rgba(255, 130, 70, ${0.2 + jamSeverity * 0.25})`;
        ctx.lineWidth = 1;
        ctx.setLineDash([5, 5]);
        ctx.beginPath();
        ctx.arc(cx, cy, rNom, 0, Math.PI * 2);
        ctx.stroke();
        ctx.setLineDash([]);
    }

    // Deep fog outside nominal sensor disc
    for (let ring = 0; ring < 15; ring++) {
        const r = rNom + ring * 10;
        if (r > maxRadius + 60) break;
        const alpha = Math.min(ring * 0.04, 0.5);
        ctx.strokeStyle = `rgba(3, 5, 18, ${alpha})`;
        ctx.lineWidth = 10;
        ctx.beginPath();
        ctx.arc(cx, cy, r, 0, Math.PI * 2);
        ctx.stroke();
    }

    ctx.strokeStyle = 'rgba(0, 200, 230, 0.15)';
    ctx.lineWidth = 1.5;
    ctx.setLineDash([4, 4]);
    ctx.beginPath();
    ctx.arc(cx, cy, rEff, 0, Math.PI * 2);
    ctx.stroke();
    ctx.setLineDash([]);
}

function drawSweepFallback(cx, cy, radius) {
    for (let i = 0; i < 20; i++) {
        const angle = sweepAngle - (i / 20) * 0.6;
        const alpha = (1 - i / 20) * 0.08;
        ctx.strokeStyle = `rgba(0, 200, 230, ${alpha})`;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.lineTo(cx + Math.cos(angle) * radius, cy + Math.sin(angle) * radius);
        ctx.stroke();
    }
}

function drawRangeRings(cx, cy, scale) {
    for (let r = 1; r <= 3; r++) {
        const radius = (sensorRange / 3) * r * scale;
        ctx.strokeStyle = `rgba(0, 200, 230, ${0.06 + r * 0.03})`;
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(cx, cy, radius, 0, Math.PI * 2);
        ctx.stroke();

        ctx.fillStyle = 'rgba(0,200,230,0.25)';
        ctx.font = `${Math.max(9, 10 * Math.min(zoom, 1.5))}px monospace`;
        ctx.textAlign = 'left';
        ctx.fillText(`${Math.round(sensorRange / 3 * r)}`, cx + radius + 4, cy - 2);
    }
}

function drawCrosshairs(cx, cy, maxRadius) {
    ctx.strokeStyle = 'rgba(0, 200, 230, 0.1)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(cx - maxRadius, cy); ctx.lineTo(cx + maxRadius, cy);
    ctx.moveTo(cx, cy - maxRadius); ctx.lineTo(cx, cy + maxRadius);
    ctx.stroke();

    ctx.strokeStyle = 'rgba(0, 200, 230, 0.05)';
    const diag = maxRadius * 0.707;
    ctx.beginPath();
    ctx.moveTo(cx - diag, cy - diag); ctx.lineTo(cx + diag, cy + diag);
    ctx.moveTo(cx + diag, cy - diag); ctx.lineTo(cx - diag, cy + diag);
    ctx.stroke();
}

function drawResourceZones(cx, cy, scale) {
    if (!resourceZones || resourceZones.length === 0) return;

    resourceZones.forEach(zone => {
        const dx = zone.X - shipPos.x;
        const dy = zone.Y - shipPos.y;
        const sx = cx + dx * scale;
        const sy = cy + dy * scale;
        const sr = zone.MapRadius * scale;

        const hex = zone.MapColorHex || '#ffffff';
        const r = parseInt(hex.slice(1, 3), 16);
        const g = parseInt(hex.slice(3, 5), 16);
        const b = parseInt(hex.slice(5, 7), 16);
        const density = zone.Density ?? 0.5;
        const isScanned = zone.Discovery === 'Scanned';

        // Filled area
        const fillAlpha = 0.06 + density * 0.08;
        ctx.fillStyle = `rgba(${r},${g},${b},${fillAlpha})`;
        ctx.beginPath();
        ctx.arc(sx, sy, sr, 0, Math.PI * 2);
        ctx.fill();

        // Border
        const borderAlpha = 0.2 + density * 0.15;
        ctx.strokeStyle = `rgba(${r},${g},${b},${borderAlpha})`;
        ctx.lineWidth = 1.5;
        ctx.setLineDash([6, 4]);
        ctx.beginPath();
        ctx.arc(sx, sy, sr, 0, Math.PI * 2);
        ctx.stroke();
        ctx.setLineDash([]);

        // Label when fully scanned
        if (isScanned) {
            ctx.fillStyle = `rgba(${r},${g},${b},0.7)`;
            ctx.font = `${Math.max(10, 11 * Math.min(zoom, 1.5))}px monospace`;
            ctx.textAlign = 'center';
            ctx.fillText(zone.ResourceType, sx, sy + 4);
        }
    });
}

function drawProbeRings(cx, cy, scale) {
    const t = Date.now() / 1000;
    probes.forEach(p => {
        const px = cx + (p.X - shipPos.x) * scale;
        const py = cy + (p.Y - shipPos.y) * scale;
        const r = p.RevealRadius * scale;
        const pulse = 1 + Math.sin(t * 3) * 0.1;

        ctx.strokeStyle = `rgba(50, 230, 100, ${0.25 + Math.sin(t * 3) * 0.1})`;
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.arc(px, py, r * pulse, 0, Math.PI * 2);
        ctx.stroke();

        const frac = p.RemainingTime / 25;
        ctx.strokeStyle = `rgba(50, 230, 100, 0.5)`;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(px, py, 5, -Math.PI / 2, -Math.PI / 2 + frac * Math.PI * 2);
        ctx.stroke();
    });
}

function drawShipMarker(cx, cy, zf) {
    ctx.fillStyle = '#00d4e8';
    ctx.beginPath();
    ctx.arc(cx, cy, 3 * Math.max(1, zoom * 0.5), 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = 'rgba(0, 200, 230, 0.3)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.arc(cx, cy, 8 * Math.max(1, zoom * 0.5), 0, Math.PI * 2);
    ctx.stroke();
}

function drawContacts(cx, cy, scale, zf) {
    contacts.forEach(c => {
        const disc = contactDiscovery(c);
        if (disc === 'Hidden') return;
        if (!isContactVisibleOnRadar(c)) return;

        const isGhost = isProbedGhost(c);
        const isScanned = disc === 'Scanned';
        const displayX = isGhost ? (c.SnapshotX ?? c.PositionX) : c.PositionX;
        const displayY = isGhost ? (c.SnapshotY ?? c.PositionY) : c.PositionY;
        const displayZ = isGhost ? (c.SnapshotZ ?? c.PositionZ ?? 500) : (c.PositionZ ?? 500);

        const dx = displayX - shipPos.x;
        const dy = displayY - shipPos.y;
        const dz = displayZ - shipPos.z;
        const dist3d = Math.sqrt(dx * dx + dy * dy + dz * dz);
        const sx = cx + dx * scale;
        const sy = cy + dy * scale;

        const baseAlpha = isGhost ? 0.35 : 1.0;
        const color = isGhost ? 'rgba(130,150,170,0.4)' :
            c.Type === 'Friendly' ? '#2ee65a' :
                c.Type === 'Hostile' ? '#e83030' :
                    c.Type === 'Unknown' ? '#e8d020' :
                        c.Type === 'Anomaly' ? '#4080f0' : '#606880';

        const isSelected = c.Id === selectedContactId;
        const dotSize = (isSelected ? 6 : isGhost ? 3 : 4) * Math.max(1, zoom * 0.7);

        // Altitude stems & label
        if (Math.abs(dz) > 5) {
            const stemLen = Math.max(25, Math.min(Math.abs(dz) * 0.4, 80)) * zoom;
            const stemDir = dz > 0 ? -1 : 1;
            const stemColor = dz > 0 ? `rgba(255,140,25,${0.55 * baseAlpha})` : `rgba(75,140,255,${0.55 * baseAlpha})`;
            const tickColor = dz > 0 ? `rgba(255,140,25,${0.9 * baseAlpha})` : `rgba(75,140,255,${0.9 * baseAlpha})`;
            ctx.strokeStyle = stemColor; ctx.lineWidth = 2;
            ctx.beginPath(); ctx.moveTo(sx, sy + stemDir * 5 * zf); ctx.lineTo(sx, sy + stemDir * stemLen); ctx.stroke();
            ctx.strokeStyle = tickColor; ctx.lineWidth = 2.5;
            ctx.beginPath(); ctx.moveTo(sx - 6, sy + stemDir * stemLen); ctx.lineTo(sx + 6, sy + stemDir * stemLen); ctx.stroke();
            ctx.fillStyle = tickColor;
            ctx.font = `bold ${Math.max(9, 10 * Math.min(zoom, 1.5))}px monospace`;
            ctx.textAlign = 'left';
            ctx.fillText(`${dz >= 0 ? '+' : ''}${Math.round(dz)}`, sx + 9, sy + stemDir * stemLen + 4);
        } else {
            const altColor = `rgba(180,200,220,${0.45 * baseAlpha})`;
            ctx.fillStyle = altColor;
            ctx.font = `${Math.max(8, 9 * Math.min(zoom, 1.5))}px monospace`;
            ctx.textAlign = 'left';
            ctx.fillText(`±0`, sx + dotSize + 4, sy + 12);
        }

        if (isSelected) {
            ctx.strokeStyle = '#00d4e8'; ctx.lineWidth = 2;
            ctx.beginPath(); ctx.arc(sx, sy, 16 * zf, 0, Math.PI * 2); ctx.stroke();
        }

        // Scan arc
        if (c.ScanProgress > 0 && c.ScanProgress < 100) {
            const scanR = 12 * zf;
            ctx.strokeStyle = `rgba(0,200,230,${0.6 * baseAlpha})`; ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(sx, sy, scanR, -Math.PI / 2, -Math.PI / 2 + (c.ScanProgress / 100) * Math.PI * 2);
            ctx.stroke();
            if (c.IsScanning) {
                ctx.strokeStyle = `rgba(0,200,230,${0.2 * baseAlpha})`; ctx.lineWidth = 4;
                ctx.beginPath();
                ctx.arc(sx, sy, scanR + 3, -Math.PI / 2, -Math.PI / 2 + (c.ScanProgress / 100) * Math.PI * 2);
                ctx.stroke();
            }
        }

        // Dot
        ctx.globalAlpha = baseAlpha;
        ctx.fillStyle = color;
        ctx.beginPath(); ctx.arc(sx, sy, dotSize, 0, Math.PI * 2); ctx.fill();
        ctx.globalAlpha = 1;

        if (c.IsScanning) {
            const ps = (6 + Math.sin(Date.now() / 200) * 3) * zf;
            ctx.strokeStyle = color; ctx.lineWidth = 1; ctx.globalAlpha = 0.4;
            ctx.beginPath(); ctx.arc(sx, sy, ps + 4, 0, Math.PI * 2); ctx.stroke();
            ctx.globalAlpha = 1;
        }

        // Velocity pip
        if (!isGhost) {
            drawVelocityPip(sx, sy, c, scale, zf);
        }

        // Labels
        const fontSize = Math.max(10, 11 * Math.min(zoom, 1.5));
        ctx.textAlign = 'center';
        if (isGhost) {
            ctx.fillStyle = 'rgba(180,200,160,0.5)';
            ctx.font = `${Math.max(8, 9 * Math.min(zoom, 1.5))}px monospace`;
            ctx.fillText('SNAPSHOT', sx, sy - 10 * zf - 8);
        } else if (isScanned) {
            ctx.fillStyle = `rgba(255,255,255,${0.75 * baseAlpha})`;
            ctx.font = `${fontSize}px sans-serif`;
            ctx.fillText(c.DisplayName || c.Id, sx, sy - 10 * zf - 8);
        } else {
            ctx.fillStyle = `rgba(255,255,255,${0.4})`;
            ctx.font = `${fontSize}px sans-serif`;
            ctx.fillText('???', sx, sy - 10 * zf - 8);
        }

        if (c.ThreatLevel > 0 && isScanned) {
            const t = Math.round(c.ThreatLevel);
            const tc = t >= 5 ? '#e83030' : t >= 3 ? '#e8d020' : '#606880';
            ctx.fillStyle = tc;
            ctx.font = `bold ${Math.max(9, 10 * Math.min(zoom, 1.5))}px monospace`;
            ctx.textAlign = 'left';
            ctx.fillText(`T${Math.round(c.ThreatLevel)}`, sx + dotSize + 4, sy + 3);
        }

        ctx.fillStyle = `rgba(255,255,255,${0.35 * baseAlpha})`;
        ctx.font = `${Math.max(8, 9 * Math.min(zoom, 1.5))}px monospace`;
        ctx.textAlign = 'center';
        ctx.fillText(`${Math.round(dist3d)}`, sx, sy + 12 * zf + 10);
    });
}

function drawVelocityPip(sx, sy, c, scale, zf) {
    const vx = c.VelocityX || 0, vy = c.VelocityY || 0;
    if (Math.abs(vx) < 0.1 && Math.abs(vy) < 0.1) return;

    const speed = Math.sqrt(vx * vx + vy * vy);
    const lineLen = Math.min(speed * 15 * scale, 60 * zoom);
    const dirX = vx / speed, dirY = vy / speed;

    ctx.strokeStyle = 'rgba(230, 220, 50, 0.3)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(sx, sy);
    ctx.lineTo(sx + dirX * lineLen, sy + dirY * lineLen);
    ctx.stroke();

    for (let i = 1; i <= 3; i++) {
        const t = i * 5;
        const px = sx + vx * t * scale;
        const py = sy + vy * t * scale;
        const pipSize = Math.max(0.8, (2 - i * 0.4) * Math.max(1, zoom * 0.5));
        ctx.fillStyle = `rgba(230, 220, 50, ${0.7 - i * 0.15})`;
        ctx.beginPath(); ctx.arc(px, py, pipSize, 0, Math.PI * 2); ctx.fill();
    }
}

// --- Probe Mode ---

function toggleProbeMode() {
    probeMode = !probeMode;
    const btn = document.getElementById('probe-mode-btn');
    if (btn) {
        btn.textContent = probeMode ? 'Sonden-Modus: AN' : 'Sonde senden';
        btn.style.borderColor = probeMode ? 'var(--green)' : '';
        btn.style.background = probeMode ? 'rgba(50, 230, 100, 0.15)' : '';
    }
    if (canvas) canvas.style.cursor = probeMode ? 'crosshair' : '';
}

function deployProbeAt(x, y) {
    if (probeCharges <= 0) {
        client.showToast('Keine Sonden verfügbar!', 'danger');
        return;
    }
    client.sendCommand('DeployProbe', { x, y });
    client.showToast(`Sonde gesendet: (${Math.round(x)}, ${Math.round(y)})`, 'info');
    probeMode = false;
    const btn = document.getElementById('probe-mode-btn');
    if (btn) { btn.textContent = 'Sonde senden'; btn.style.borderColor = ''; btn.style.background = ''; }
    if (canvas) canvas.style.cursor = '';
}

// --- State Updates ---

client.onState((msg) => {
    if (!msg.data) return;
    const d = msg.data;

    updatePhaseHeaderFromState(msg);
    updateEventIndicator(d);
    document.getElementById('timer-display').textContent = formatTime(msg.elapsed_time || 0);

    const sensorStatus = d.sensor_status || 'Operational';
    document.getElementById('sensor-status').innerHTML =
        `<span class="system-status ${getStatusClass(sensorStatus)}">${getStatusLabel(sensorStatus)}</span>`;

    document.getElementById('sensor-energy').textContent = d.sensor_energy ?? 33;

    sensorRange = d.sensor_range ?? 500;
    sensorRangeNominal = d.sensor_range_nominal ?? sensorRange;
    sensorStatusMultRange = typeof d.sensor_status_mult_range === 'number' ? d.sensor_status_mult_range : 1;
    document.getElementById('sensor-range').textContent = Math.round(sensorRange);
    const jamLeg = document.getElementById('tactical-jam-legend');
    if (jamLeg) {
        const showJam = sensorRangeNominal > sensorRange + 1 && (1 - sensorStatusMultRange) > 0.02;
        jamLeg.style.display = showJam ? 'inline' : 'none';
    }

    shipPos = { x: d.ship_x ?? 100, y: d.ship_y ?? 100, z: d.ship_z ?? 500 };
    contacts = d.contacts || [];
    contactActions = d.contact_actions || {};
    resourceZones = d.resource_zones || [];

    probes = d.probes || [];
    probeCharges = d.probe_charges ?? 3;
    probeMaxCharges = d.probe_max_charges ?? 5;
    probeRechargeTimer = d.probe_recharge_timer ?? 0;
    probeRechargeTime = d.probe_recharge_time ?? 45;

    updateProbePanel();

    tacticalOnMainScreen = d.tactical_on_main_screen || false;
    updateTacticalButton();

    renderContacts();
    renderEvents(d.active_events || []);
    updateContactActions();

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
        text.textContent = 'Angedockt: Station — Waffen gesichert.';
        text.className = 'text-green';
    } else {
        text.textContent = 'Station-Dock in Sektor — Kontakt „Andockmast" markiert.';
        text.className = 'text-cyan';
    }
}

function updateProbePanel() {
    const chargesEl = document.getElementById('probe-charges');
    if (chargesEl) chargesEl.textContent = `${probeCharges} / ${probeMaxCharges}`;

    const rechargeEl = document.getElementById('probe-recharge');
    if (rechargeEl) {
        if (probeCharges < probeMaxCharges) {
            const remaining = Math.ceil(probeRechargeTime - probeRechargeTimer);
            rechargeEl.textContent = `${remaining}s`;
        } else {
            rechargeEl.textContent = 'VOLL';
        }
    }

    const btn = document.getElementById('probe-mode-btn');
    if (btn && !probeMode) btn.disabled = probeCharges <= 0;
}

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

    if (selectedContactId) {
        const sel = contacts.find(c => c.Id === selectedContactId);
        if (sel && !isContactVisibleOnRadar(sel)) {
            selectedContactId = null;
            updateContactActions();
        }
    }

    const visible = contacts.filter(c => contactDiscovery(c) !== 'Hidden' && isContactVisibleOnRadar(c));

    if (visible.length === 0) {
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

    const currentIds = new Set(visible.map(c => c.Id));
    for (const [id, el] of _contactElements) {
        if (!currentIds.has(id)) {
            el.remove();
            _contactElements.delete(id);
        }
    }

    visible.forEach((c, index) => {
        let el = _contactElements.get(c.Id);
        const isSelected = c.Id === selectedContactId;
        const scanning = c.IsScanning;
        const disc = contactDiscovery(c);
        const isGhost = isProbedGhost(c);
        const isScanned = disc === 'Scanned';

        if (!el) {
            el = document.createElement('div');
            el.dataset.contactId = c.Id;
            _contactElements.set(c.Id, el);
        }

        if (container.children[index] !== el) {
            if (index < container.children.length) container.insertBefore(el, container.children[index]);
            else container.appendChild(el);
        }

        const ghostClass = isGhost ? ' ghost-contact' : '';
        const wantClass = `contact-item${isSelected ? ' selected' : ''}${scanning ? ' scanning-active' : ''}${ghostClass}`;
        if (el.className !== wantClass) el.className = wantClass;

        const displayX = isGhost ? (c.SnapshotX ?? c.PositionX) : c.PositionX;
        const displayY = isGhost ? (c.SnapshotY ?? c.PositionY) : c.PositionY;
        const displayZ = isGhost ? (c.SnapshotZ ?? c.PositionZ ?? 500) : (c.PositionZ ?? 500);
        const cdx = displayX - shipPos.x, cdy = displayY - shipPos.y;
        const cdz = displayZ - shipPos.z;
        const dist = Math.round(Math.sqrt(cdx * cdx + cdy * cdy + cdz * cdz));
        const circumference = 2 * Math.PI * 16;
        const dashOffset = circumference - (c.ScanProgress / 100) * circumference;

        const navBadge = (isScanned && c.ReleasedToNav) ? ' <span style="color:var(--green);font-size:10px;">[NAV]</span>' : '';
        const identityRevealed = c.PreRevealed || c.ScanProgress >= 100;
        const identified = identityRevealed;
        const name = isGhost ? `[SNAPSHOT] ${identified ? (c.DisplayName || c.Id) : '???'}` :
            identified ? c.DisplayName + navBadge : '???';
        const scanStr = c.ScanProgress < 100 ? `Scan: ${Math.round(c.ScanProgress)}%` :
            isScanned ? c.Type : '---';
        const threatStr = identityRevealed ? String(Math.round(c.ThreatLevel)) : '?';

        let inner;
        if (c.ScanProgress < 100 && c.ScanProgress > 0) {
            inner =
                `<div class="scan-ring"><svg viewBox="0 0 40 40">` +
                `<circle class="scan-ring-bg" cx="20" cy="20" r="16"/>` +
                `<circle class="scan-ring-fill" cx="20" cy="20" r="16" ` +
                `stroke-dasharray="${circumference}" stroke-dashoffset="${dashOffset}"/>` +
                `</svg><div class="scan-ring-text">${Math.round(c.ScanProgress)}%</div></div>` +
                `<div class="contact-info"><div class="contact-name">${name}${scanning ? ' ◉' : ''}</div>` +
                `<div class="contact-meta">${scanStr} · Dist: ${dist} · T:${threatStr}</div></div>`;
        } else {
            inner =
                `<div class="contact-dot ${getContactDotClass(c.Type)}"></div>` +
                `<div class="contact-info"><div class="contact-name">${name}${scanning ? ' ◉' : ''}</div>` +
                `<div class="contact-meta">${scanStr} · Dist: ${dist} · T:${threatStr}</div></div>`;
        }

        if (el._lastInner !== inner) { el.innerHTML = inner; el._lastInner = inner; }
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

    if (active.length === 0) { panel.style.display = 'none'; container.innerHTML = ''; return; }
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

function actionTooltip(action) {
    return action.Tooltip ?? action.tooltip ?? '';
}

/** Actions identity without Label/Progress (labels embed live percentages and would force full DOM rebuild every tick). */
function stableActionsStructureKey(actions) {
    return JSON.stringify((actions || []).map((a) => ({
        Id: a.Id,
        Command: a.Command,
        Style: a.Style,
        Disabled: a.Disabled,
        Active: a.Active,
        Group: a.Group,
        Confirm: a.Confirm,
        Tooltip: actionTooltip(a),
    })));
}

function makeActionKey(action) {
    return `${action.Group || 'default'}::${action.Id || ''}`;
}

function syncActionButtonProgressFromActions(actions) {
    const container = document.getElementById('action-buttons-container');
    if (!container) return;
    for (const action of actions) {
        const key = makeActionKey(action);
        let btn = null;
        for (const b of container.querySelectorAll('button[data-action-key]')) {
            if (b.dataset.actionKey === key) {
                btn = b;
                break;
            }
        }
        if (!btn) continue;
        btn.textContent = action.Label;
        btn.title = actionTooltip(action);
        btn.disabled = !!action.Disabled;
        if (action.Progress != null && action.Progress < 100) {
            btn.style.backgroundImage =
                `linear-gradient(90deg, rgba(0,200,230,0.15) ${action.Progress}%, transparent ${action.Progress}%)`;
        } else {
            btn.style.backgroundImage = '';
        }
    }
}

function updateContactActions() {
    const panel = document.getElementById('contact-actions');
    if (!selectedContactId) {
        panel.style.display = 'none';
        _lastActionsStructureKey = '';
        _lastActionsProgressSnapshot = '';
        _lastActionsContactId = null;
        return;
    }

    const contact = contacts.find(c => c.Id === selectedContactId);
    if (!contact || contactDiscovery(contact) === 'Hidden') {
        panel.style.display = 'none';
        _lastActionsStructureKey = '';
        _lastActionsProgressSnapshot = '';
        _lastActionsContactId = null;
        return;
    }

    const actions = contactActions[selectedContactId];
    if (!actions || actions.length === 0) {
        panel.style.display = 'none';
        _lastActionsStructureKey = '';
        _lastActionsProgressSnapshot = '';
        _lastActionsContactId = null;
        return;
    }

    panel.style.display = '';
    const identityRevealed = contact.PreRevealed || contact.ScanProgress >= 100;
    document.getElementById('selected-contact-name').textContent =
        identityRevealed ? (contact.DisplayName || '???') : 'Unbekannt';

    const structureKey = stableActionsStructureKey(actions);
    const progressSnap = actions.map((a) => `${a.Id}:${a.Progress ?? ''}:${a.Label}`).join('|');

    if (structureKey === _lastActionsStructureKey && selectedContactId === _lastActionsContactId) {
        if (progressSnap !== _lastActionsProgressSnapshot) {
            _lastActionsProgressSnapshot = progressSnap;
            syncActionButtonProgressFromActions(actions);
        }
        return;
    }

    _lastActionsStructureKey = structureKey;
    _lastActionsProgressSnapshot = progressSnap;
    _lastActionsContactId = selectedContactId;

    const container = document.getElementById('action-buttons-container');
    container.innerHTML = '';

    const groups = {};
    for (const action of actions) {
        const g = action.Group || 'default';
        if (!groups[g]) groups[g] = [];
        groups[g].push(action);
    }

    for (const groupName of Object.keys(groups)) {
        const items = groups[groupName];
        const row = document.createElement('div');
        row.className = 'action-row';
        if (items.length > 1) row.classList.add(`action-row-${Math.min(items.length, 3)}`);

        for (const action of items) {
            row.appendChild(buildButtonAction(action));
        }

        container.appendChild(row);
    }
}

function buildButtonAction(action) {
    const btn = document.createElement('button');
    const styleMap = {
        primary: 'btn-primary', danger: 'btn-danger',
        warning: 'btn-warning', success: 'btn-success', default: '',
    };
    btn.className = `btn btn-block ${styleMap[action.Style] || ''}`;
    btn.dataset.actionKey = makeActionKey(action);
    btn.textContent = action.Label;
    const tip = actionTooltip(action);
    if (tip) btn.title = tip;
    btn.disabled = action.Disabled || false;

    if (action.Active) {
        const colorMap = {
            success: { border: 'var(--green)', bg: 'rgba(50, 230, 100, 0.15)' },
            danger: { border: 'var(--red)', bg: 'rgba(232, 48, 48, 0.15)' },
            primary: { border: 'var(--cyan)', bg: 'rgba(0, 200, 230, 0.15)' },
            warning: { border: 'var(--orange)', bg: 'rgba(232, 144, 32, 0.15)' },
        };
        const colors = colorMap[action.Style] || colorMap.success;
        btn.style.borderColor = colors.border;
        btn.style.background = colors.bg;
    }

    if (action.Progress != null && action.Progress < 100) {
        btn.style.backgroundImage =
            `linear-gradient(90deg, rgba(0,200,230,0.15) ${action.Progress}%, transparent ${action.Progress}%)`;
    }

    if (action.Command) {
        btn.addEventListener('click', () => {
            if (!selectedContactId) return;
            if (action.Confirm && !confirm(action.Label + '?')) return;
            const payload = { contact_id: selectedContactId, ...(action.Data || {}) };
            client.sendCommand(action.Command, payload);
        });
    }

    return btn;
}

function setSensorMode(mode) {
    client.sendCommand('SetSensorMode', { mode });
    client.showToast(mode === 'active' ? 'Aktive Sensoren' : 'Passive Sensoren', 'info');
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
