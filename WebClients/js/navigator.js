/**
 * SpacedOut - Navigator Station UI Logic
 * Zoomable/pannable map, heading indicator, distance readouts
 */

const MAP_SIZE = 1000;
let canvas, ctx;

client.on('welcome', () => {
    client.selectRole('Navigator');
});

let shipPos = { x: 100, y: 100 };
let prevShipPos = { x: 100, y: 100 };
let waypoints = [];
let contacts = [];
let currentFlightMode = 'Cruise';
let speedLevel = 2;
let starMapOnMainScreen = false;

// Zoom & pan
let zoom = 1.0;
let panX = 0, panY = 0;
let isDragging = false;
let dragStart = { x: 0, y: 0 };
let panStart = { x: 0, y: 0 };
let lastTapTime = 0;

document.addEventListener('DOMContentLoaded', () => {
    canvas = document.getElementById('map-canvas');
    ctx = canvas.getContext('2d');
    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);

    // Mouse events
    canvas.addEventListener('mousedown', onPointerDown);
    canvas.addEventListener('mousemove', onPointerMove);
    canvas.addEventListener('mouseup', onPointerUp);
    canvas.addEventListener('wheel', onWheel, { passive: false });

    // Touch events
    canvas.addEventListener('touchstart', onTouchStart, { passive: false });
    canvas.addEventListener('touchmove', onTouchMove, { passive: false });
    canvas.addEventListener('touchend', onTouchEnd, { passive: false });

    document.getElementById('waypoint-list').addEventListener('click', (e) => {
        const btn = e.target.closest('[data-wp-id]');
        if (btn) removeWaypoint(btn.dataset.wpId);
    });
});

function resizeCanvas() {
    const container = canvas.parentElement;
    const size = container.clientWidth;
    canvas.width = size;
    canvas.height = size;
    drawMap();
}

// --- Zoom & Pan ---

function zoomIn() {
    zoom = Math.min(zoom * 1.4, 5);
    drawMap();
    updateMapInfo();
}

function zoomOut() {
    zoom = Math.max(zoom / 1.4, 0.5);
    drawMap();
    updateMapInfo();
}

function centerOnShip() {
    const w = canvas.width;
    panX = -(shipPos.x * (w / MAP_SIZE) * zoom - w / 2);
    panY = -(shipPos.y * (w / MAP_SIZE) * zoom - w / 2);
    drawMap();
}

function updateMapInfo() {
    document.getElementById('map-info').textContent = `Zoom: ${zoom.toFixed(1)}x`;
}

function onWheel(e) {
    e.preventDefault();
    if (e.deltaY < 0) zoomIn();
    else zoomOut();
}

// Mouse drag
function onPointerDown(e) {
    isDragging = true;
    dragStart = { x: e.clientX, y: e.clientY };
    panStart = { x: panX, y: panY };
}

function onPointerMove(e) {
    if (!isDragging) return;
    panX = panStart.x + (e.clientX - dragStart.x);
    panY = panStart.y + (e.clientY - dragStart.y);
    drawMap();
}

function onPointerUp(e) {
    const dx = Math.abs(e.clientX - dragStart.x);
    const dy = Math.abs(e.clientY - dragStart.y);
    isDragging = false;

    if (dx < 5 && dy < 5) {
        const rect = canvas.getBoundingClientRect();
        const cx = e.clientX - rect.left;
        const cy = e.clientY - rect.top;
        const mapX = (cx - panX) / ((canvas.width / MAP_SIZE) * zoom);
        const mapY = (cy - panY) / ((canvas.width / MAP_SIZE) * zoom);
        if (mapX >= 0 && mapX <= MAP_SIZE && mapY >= 0 && mapY <= MAP_SIZE) {
            setWaypoint(mapX, mapY);
        }
    }
}

// Touch handling
let touchStartDist = 0;
let touchStartZoom = 1;

function onTouchStart(e) {
    if (e.touches.length === 2) {
        e.preventDefault();
        const t = e.touches;
        touchStartDist = Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);
        touchStartZoom = zoom;
    } else if (e.touches.length === 1) {
        isDragging = true;
        dragStart = { x: e.touches[0].clientX, y: e.touches[0].clientY };
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
        drawMap();
    } else if (isDragging && e.touches.length === 1) {
        panX = panStart.x + (e.touches[0].clientX - dragStart.x);
        panY = panStart.y + (e.touches[0].clientY - dragStart.y);
        drawMap();
    }
}

function onTouchEnd(e) {
    if (isDragging && e.changedTouches.length === 1) {
        const touch = e.changedTouches[0];
        const dx = Math.abs(touch.clientX - dragStart.x);
        const dy = Math.abs(touch.clientY - dragStart.y);

        if (dx < 10 && dy < 10) {
            const now = Date.now();
            if (now - lastTapTime > 300) {
                const rect = canvas.getBoundingClientRect();
                const cx = touch.clientX - rect.left;
                const cy = touch.clientY - rect.top;
                const mapX = (cx - panX) / ((canvas.width / MAP_SIZE) * zoom);
                const mapY = (cy - panY) / ((canvas.width / MAP_SIZE) * zoom);
                if (mapX >= 0 && mapX <= MAP_SIZE && mapY >= 0 && mapY <= MAP_SIZE) {
                    setWaypoint(mapX, mapY);
                }
            }
            lastTapTime = now;
        }
    }
    isDragging = false;
}

// --- Map Drawing ---

function drawMap() {
    if (!ctx) return;
    const w = canvas.width, h = canvas.height;
    const baseScale = w / MAP_SIZE;
    const s = baseScale * zoom;

    ctx.fillStyle = '#050812';
    ctx.fillRect(0, 0, w, h);

    ctx.save();
    ctx.translate(panX, panY);

    // Grid
    ctx.strokeStyle = 'rgba(40, 60, 120, 0.15)';
    ctx.lineWidth = 1;
    const gridStep = 100;
    for (let i = 0; i <= MAP_SIZE / gridStep; i++) {
        const p = i * gridStep * s;
        ctx.beginPath(); ctx.moveTo(p, 0); ctx.lineTo(p, MAP_SIZE * s); ctx.stroke();
        ctx.beginPath(); ctx.moveTo(0, p); ctx.lineTo(MAP_SIZE * s, p); ctx.stroke();

        ctx.fillStyle = 'rgba(60,80,140,0.3)';
        ctx.font = '9px monospace';
        ctx.textAlign = 'left';
        ctx.fillText(`${i * gridStep}`, p + 2, 10);
    }

    // Route lines
    const unreached = waypoints.filter(wp => !wp.IsReached);
    if (unreached.length > 0) {
        ctx.strokeStyle = 'rgba(0, 200, 230, 0.35)';
        ctx.lineWidth = 2;
        ctx.setLineDash([8, 5]);
        ctx.beginPath();
        ctx.moveTo(shipPos.x * s, shipPos.y * s);
        unreached.forEach(wp => ctx.lineTo(wp.X * s, wp.Y * s));
        ctx.stroke();
        ctx.setLineDash([]);
    }

    // Contacts
    contacts.forEach(c => {
        const cx = c.PositionX * s;
        const cy = c.PositionY * s;
        const color = c.Type === 'Friendly' ? '#2ee65a' :
                      c.Type === 'Hostile' ? '#e83030' :
                      c.Type === 'Unknown' ? '#e8d020' :
                      c.Type === 'Anomaly' ? '#4080f0' : '#606880';

        // Range ring for unknown contacts
        if (c.Type === 'Unknown') {
            ctx.strokeStyle = 'rgba(232, 208, 32, 0.12)';
            ctx.lineWidth = 1;
            ctx.setLineDash([3, 3]);
            ctx.beginPath();
            ctx.arc(cx, cy, 40 * zoom, 0, Math.PI * 2);
            ctx.stroke();
            ctx.setLineDash([]);
        }

        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(cx, cy, 5 * Math.max(1, zoom * 0.7), 0, Math.PI * 2);
        ctx.fill();

        // Velocity vector
        if (c.VelocityX || c.VelocityY) {
            ctx.strokeStyle = color;
            ctx.lineWidth = 1;
            ctx.setLineDash([3, 3]);
            ctx.beginPath();
            ctx.moveTo(cx, cy);
            ctx.lineTo(cx + c.VelocityX * 30 * zoom, cy + c.VelocityY * 30 * zoom);
            ctx.stroke();
            ctx.setLineDash([]);
        }

        ctx.fillStyle = 'rgba(255,255,255,0.7)';
        ctx.font = `${Math.max(9, 10 * Math.min(zoom, 1.5))}px monospace`;
        ctx.textAlign = 'center';
        ctx.fillText(c.DisplayName || c.Id, cx, cy - 10 * Math.max(1, zoom * 0.5));
    });

    // Waypoints
    waypoints.forEach((wp, i) => {
        const wx = wp.X * s;
        const wy = wp.Y * s;
        const reached = wp.IsReached;
        const sz = 7 * Math.max(1, zoom * 0.6);

        ctx.strokeStyle = reached ? 'rgba(0,200,230,0.25)' : '#00d4e8';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(wx - sz, wy - sz);
        ctx.lineTo(wx + sz, wy - sz);
        ctx.lineTo(wx + sz, wy + sz);
        ctx.lineTo(wx - sz, wy + sz);
        ctx.closePath();
        ctx.stroke();

        if (!reached) {
            ctx.fillStyle = 'rgba(0,200,230,0.12)';
            ctx.fill();

            // Distance from ship
            const dist = Math.round(Math.hypot(wp.X - shipPos.x, wp.Y - shipPos.y));
            ctx.fillStyle = 'rgba(0,200,230,0.6)';
            ctx.font = `${Math.max(8, 9 * Math.min(zoom, 1.5))}px monospace`;
            ctx.fillText(`${dist}`, wx, wy + sz + 12);
        }

        ctx.fillStyle = reached ? 'rgba(255,255,255,0.25)' : '#fff';
        ctx.font = `${Math.max(9, 10 * Math.min(zoom, 1.5))}px sans-serif`;
        ctx.textAlign = 'center';
        ctx.fillText(wp.Label || `WP${i+1}`, wx, wy - sz - 4);
    });

    // Sensor range circle
    ctx.strokeStyle = 'rgba(0, 200, 230, 0.08)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.arc(shipPos.x * s, shipPos.y * s, 350 * s, 0, Math.PI * 2);
    ctx.stroke();

    // Ship heading vector
    const heading = Math.atan2(shipPos.y - prevShipPos.y, shipPos.x - prevShipPos.x);
    const moving = Math.hypot(shipPos.x - prevShipPos.x, shipPos.y - prevShipPos.y) > 0.1;

    const sx = shipPos.x * s;
    const sy = shipPos.y * s;

    // Ship heading line
    if (moving && currentFlightMode !== 'Hold') {
        const lineLen = 25 * zoom;
        ctx.strokeStyle = 'rgba(0, 200, 230, 0.4)';
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.moveTo(sx, sy);
        ctx.lineTo(sx + Math.cos(heading) * lineLen, sy + Math.sin(heading) * lineLen);
        ctx.stroke();
    }

    // Ship triangle (rotated to heading)
    const shipSize = 8 * Math.max(1, zoom * 0.5);
    ctx.save();
    ctx.translate(sx, sy);
    if (moving) ctx.rotate(heading + Math.PI / 2);
    ctx.fillStyle = '#00d4e8';
    ctx.beginPath();
    ctx.moveTo(0, -shipSize);
    ctx.lineTo(shipSize * 0.65, shipSize * 0.65);
    ctx.lineTo(-shipSize * 0.65, shipSize * 0.65);
    ctx.closePath();
    ctx.fill();
    ctx.restore();

    // Ship label
    ctx.fillStyle = '#00d4e8';
    ctx.font = `bold ${Math.max(10, 11 * Math.min(zoom, 1.5))}px sans-serif`;
    ctx.textAlign = 'center';
    ctx.fillText('SCHIFF', sx, sy + shipSize + 14);

    ctx.restore();
}

// --- State Updates ---

client.onState((msg) => {
    if (!msg.data) return;
    const d = msg.data;

    document.getElementById('phase-display').textContent = msg.mission_phase || '---';
    document.getElementById('timer-display').textContent = formatTime(msg.elapsed_time || 0);

    prevShipPos = { ...shipPos };
    shipPos = { x: d.ship_x ?? 100, y: d.ship_y ?? 100 };
    currentFlightMode = d.flight_mode || 'Cruise';
    speedLevel = d.speed_level ?? 2;
    contacts = d.contacts || [];

    document.getElementById('position-display').textContent =
        `${Math.round(shipPos.x)}, ${Math.round(shipPos.y)}`;
    document.getElementById('speed-display').textContent = speedLevel;
    document.getElementById('current-flight-mode').textContent =
        currentFlightMode.toUpperCase();
    document.getElementById('drive-energy').textContent = d.drive_energy ?? 34;

    const driveStatus = d.drive_status || 'Operational';
    document.getElementById('drive-status').innerHTML =
        `<span class="system-status ${getStatusClass(driveStatus)}">${getStatusLabel(driveStatus)}</span>`;

    if (d.route) {
        waypoints = d.route.Waypoints || [];
        const eta = d.route.EstimatedTimeRemaining;
        document.getElementById('eta-display').textContent =
            eta && eta < 999999 ? formatTime(eta) : '--:--';
        const risk = d.route.RiskValue || 0;
        const riskEl = document.getElementById('risk-display');
        riskEl.textContent = risk.toFixed(1);
        riskEl.className = `status-value ${risk > 6 ? 'text-red' : risk > 3 ? 'text-yellow' : 'text-green'}`;
    }

    // Distance to next waypoint
    const nextWp = waypoints.find(wp => !wp.IsReached);
    if (nextWp) {
        const dist = Math.round(Math.hypot(nextWp.X - shipPos.x, nextWp.Y - shipPos.y));
        document.getElementById('next-wp-dist').textContent = dist;
    } else {
        document.getElementById('next-wp-dist').textContent = '---';
    }

    starMapOnMainScreen = d.star_map_on_main_screen || false;
    updateStarMapButton();

    updateFlightModeButtons();
    renderWaypoints();
    drawMap();
});

client.on('paused', () => document.getElementById('paused-overlay').classList.remove('hidden'));
client.on('resumed', () => document.getElementById('paused-overlay').classList.add('hidden'));
client.on('mission_ended', (msg) => {
    document.getElementById('mission-end').classList.remove('hidden');
    document.getElementById('mission-end-title').textContent = 'MISSION BEENDET';
    document.getElementById('mission-end-detail').textContent = `Ergebnis: ${msg.result}`;
    setTimeout(() => {
        document.getElementById('mission-end').classList.add('hidden');
    }, 3000);
});

// ── Sector Map (Campaign) ───────────────────────────────────────

let sectorMapData = null;
let selectedNodeId = null;
let sectorCanvas = null;
let sectorCtx = null;

client.on('sector_map_update', (msg) => {
    sectorMapData = msg.data;
    if (sectorMapData && sectorMapData.nodes && sectorMapData.nodes.length > 0) {
        showSectorMap();
    }
});

client.on('mission_started', () => {
    hideSectorMap();
});

client.on('campaign_ended', (msg) => {
    const overlay = document.getElementById('sector-map-overlay');
    if (overlay) {
        document.getElementById('sector-title').textContent = 'KAMPAGNE BEENDET';
        document.getElementById('sector-subtitle').textContent =
            msg.result === 'victory' ? 'Alle Sektoren durchquert!' : 'Mission gescheitert.';
    }
});

function showSectorMap() {
    const overlay = document.getElementById('sector-map-overlay');
    overlay.classList.remove('hidden');
    selectedNodeId = null;
    document.getElementById('node-info-panel').classList.add('hidden');

    if (!sectorCanvas) {
        sectorCanvas = document.getElementById('sector-map-canvas');
        sectorCtx = sectorCanvas.getContext('2d');
        sectorCanvas.addEventListener('click', onSectorMapClick);
        const ro = new ResizeObserver(() => resizeSectorCanvas());
        ro.observe(sectorCanvas.parentElement);
    }

    updateSectorMapResources();
    resizeSectorCanvas();
}

function hideSectorMap() {
    document.getElementById('sector-map-overlay').classList.add('hidden');
}

function resizeSectorCanvas() {
    if (!sectorCanvas) return;
    const container = sectorCanvas.parentElement;
    sectorCanvas.width = container.clientWidth;
    sectorCanvas.height = container.clientHeight;
    drawSectorMap();
}

function updateSectorMapResources() {
    if (!sectorMapData) return;
    document.getElementById('sector-title').textContent =
        `SEKTORKARTE · ${sectorMapData.sector_name || ''}`;
    document.getElementById('sector-subtitle').textContent =
        `Sektor ${(sectorMapData.sector_index || 0) + 1}/${sectorMapData.sectors_total || '?'} · Schwierigkeit: ${sectorMapData.difficulty || 1}`;
    document.getElementById('resource-hull').textContent =
        `Hull: ${Math.round(sectorMapData.hull || 100)}%`;
    document.getElementById('resource-fuel').textContent =
        `Fuel: ${sectorMapData.fuel ?? 10}`;
    document.getElementById('resource-scrap').textContent =
        `Scrap: ${sectorMapData.scrap ?? 0}`;
}

const NODE_COLORS = {
    Start: '#00d4e8',
    Navigation: '#4080f0',
    ScanAnomaly: '#b34df0',
    DebrisField: '#e89020',
    Encounter: '#e83030',
    DistressSignal: '#e8d020',
    Station: '#2ee65a',
    EliteEncounter: '#ff3366',
    Boss: '#ffd700',
};

const NODE_ICONS = {
    Start: '▶', Navigation: '◇', ScanAnomaly: '◉',
    DebrisField: '⚠', Encounter: '⊕', DistressSignal: '☆',
    Station: '⛽', EliteEncounter: '☠', Boss: '♛',
};

function drawSectorMap() {
    if (!sectorCtx || !sectorMapData) return;
    const w = sectorCanvas.width, h = sectorCanvas.height;
    const nodes = sectorMapData.nodes || [];
    const edges = sectorMapData.edges || [];

    sectorCtx.fillStyle = '#050812';
    sectorCtx.fillRect(0, 0, w, h);

    if (nodes.length === 0) return;

    const maxX = Math.max(...nodes.map(n => n.x), 1);
    const maxY = Math.max(...nodes.map(n => n.y), 1);
    const margin = 40;
    const scaleX = (w - margin * 2) / maxX;
    const scaleY = (h - margin * 2) / maxY;

    function nodePos(node) {
        return {
            x: margin + node.x * scaleX,
            y: margin + node.y * scaleY,
        };
    }

    // Draw edges
    edges.forEach(e => {
        const fromNode = nodes.find(n => n.id === e.from);
        const toNode = nodes.find(n => n.id === e.to);
        if (!fromNode || !toNode) return;

        const from = nodePos(fromNode);
        const to = nodePos(toNode);

        const isPath = (fromNode.status === 'Completed' || fromNode.status === 'Current')
                     && (toNode.status === 'Completed' || toNode.status === 'Current');
        const isAvailable = fromNode.status === 'Current' && toNode.status === 'Available';

        sectorCtx.strokeStyle = isPath ? 'rgba(0, 212, 232, 0.6)' :
                                isAvailable ? 'rgba(0, 212, 232, 0.35)' :
                                'rgba(40, 60, 120, 0.3)';
        sectorCtx.lineWidth = isPath ? 3 : isAvailable ? 2 : 1;
        sectorCtx.beginPath();
        sectorCtx.moveTo(from.x, from.y);
        sectorCtx.lineTo(to.x, to.y);
        sectorCtx.stroke();
    });

    // Draw nodes
    nodes.forEach(node => {
        const p = nodePos(node);
        const color = NODE_COLORS[node.type] || '#606880';
        const radius = node.status === 'Current' ? 18 :
                       node.status === 'Available' ? 15 : 12;
        const isSelected = node.id === selectedNodeId;

        // Background circle
        let alpha = 0.08;
        if (node.status === 'Completed') alpha = 0.25;
        else if (node.status === 'Current') alpha = 0.35;
        else if (node.status === 'Available') alpha = 0.15;

        sectorCtx.fillStyle = hexAlpha(color, alpha);
        sectorCtx.beginPath();
        sectorCtx.arc(p.x, p.y, radius, 0, Math.PI * 2);
        sectorCtx.fill();

        // Border
        sectorCtx.strokeStyle = isSelected ? '#ffffff' :
                                node.status === 'Completed' ? hexAlpha('#2ee65a', 0.6) :
                                node.status === 'Current' ? color :
                                node.status === 'Available' ? hexAlpha(color, 0.6) :
                                hexAlpha('#606880', 0.25);
        sectorCtx.lineWidth = isSelected ? 3 : node.status === 'Current' ? 2.5 : 1.5;
        sectorCtx.beginPath();
        sectorCtx.arc(p.x, p.y, radius, 0, Math.PI * 2);
        sectorCtx.stroke();

        // Inner color dot
        sectorCtx.fillStyle = color;
        sectorCtx.beginPath();
        sectorCtx.arc(p.x, p.y, radius * 0.35, 0, Math.PI * 2);
        sectorCtx.fill();

        // Icon
        sectorCtx.fillStyle = '#ffffff';
        sectorCtx.font = `${radius * 0.9}px sans-serif`;
        sectorCtx.textAlign = 'center';
        sectorCtx.textBaseline = 'middle';
        sectorCtx.fillText(NODE_ICONS[node.type] || '?', p.x, p.y);

        // Label
        if (node.status !== 'Locked' && node.status !== 'Skipped') {
            const label = node.label.length > 16 ? node.label.substring(0, 14) + '..' : node.label;
            sectorCtx.fillStyle = node.status === 'Available' ? '#e0e2f0' : 'rgba(224, 226, 240, 0.5)';
            sectorCtx.font = '10px sans-serif';
            sectorCtx.textAlign = 'center';
            sectorCtx.textBaseline = 'top';
            sectorCtx.fillText(label, p.x, p.y + radius + 4);
        }
    });
}

function hexAlpha(hex, alpha) {
    const r = parseInt(hex.slice(1, 3), 16);
    const g = parseInt(hex.slice(3, 5), 16);
    const b = parseInt(hex.slice(5, 7), 16);
    return `rgba(${r},${g},${b},${alpha})`;
}

function onSectorMapClick(e) {
    if (!sectorMapData) return;
    const rect = sectorCanvas.getBoundingClientRect();
    const cx = e.clientX - rect.left;
    const cy = e.clientY - rect.top;

    const nodes = sectorMapData.nodes || [];
    const w = sectorCanvas.width, h = sectorCanvas.height;
    const maxX = Math.max(...nodes.map(n => n.x), 1);
    const maxY = Math.max(...nodes.map(n => n.y), 1);
    const margin = 40;
    const scaleX = (w - margin * 2) / maxX;
    const scaleY = (h - margin * 2) / maxY;

    let closest = null;
    let closestDist = Infinity;

    nodes.forEach(node => {
        if (node.status !== 'Available') return;
        const px = margin + node.x * scaleX;
        const py = margin + node.y * scaleY;
        const dist = Math.hypot(cx - px, cy - py);
        if (dist < 30 && dist < closestDist) {
            closest = node;
            closestDist = dist;
        }
    });

    if (closest) {
        selectedNodeId = closest.id;
        showNodeInfo(closest);
        drawSectorMap();
    }
}

function showNodeInfo(node) {
    const panel = document.getElementById('node-info-panel');
    panel.classList.remove('hidden');

    document.getElementById('node-info-title').textContent =
        `${NODE_ICONS[node.type] || '?'} ${node.label}`;
    document.getElementById('node-info-desc').textContent = node.description;

    const fuelCost = node.fuel_cost ?? 1;
    const hasFuel = (sectorMapData.fuel ?? 0) >= fuelCost;
    document.getElementById('node-info-meta').textContent =
        `Typ: ${node.type} · Schwierigkeit: ${node.difficulty} · Treibstoff: ${fuelCost}`;

    const btn = document.getElementById('node-select-btn');
    btn.disabled = !hasFuel;
    btn.textContent = hasFuel ? 'Kurs setzen' : 'Nicht genug Treibstoff';
}

function confirmNodeSelection() {
    if (!selectedNodeId) return;
    client.sendCommand('SelectNode', { node_id: selectedNodeId });
    client.showToast('Kurs gesetzt – Mission wird geladen...', 'info');
}

function updateFlightModeButtons() {
    document.querySelectorAll('#flight-mode-buttons .btn').forEach(btn => {
        const mode = btn.dataset.mode;
        if (mode === currentFlightMode) {
            btn.style.borderColor = 'var(--cyan)';
            btn.style.background = 'rgba(0, 200, 230, 0.15)';
        } else {
            btn.style.borderColor = '';
            btn.style.background = '';
        }
    });
}

let _lastWaypointsHtml = '';

function renderWaypoints() {
    const container = document.getElementById('waypoint-list');
    const unreached = waypoints.filter(wp => !wp.IsReached);

    if (unreached.length === 0) {
        if (_lastWaypointsHtml !== 'empty') {
            container.innerHTML = '<div class="text-dim" style="font-size:13px;text-align:center;padding:8px;">Keine aktiven Waypoints</div>';
            _lastWaypointsHtml = 'empty';
        }
        return;
    }

    const html = unreached.map((wp, i) => {
        const dist = Math.round(Math.hypot(wp.X - shipPos.x, wp.Y - shipPos.y));
        return `<div class="contact-item">` +
            `<div class="contact-dot" style="background:var(--cyan);"></div>` +
            `<div class="contact-info">` +
            `<div class="contact-name">${wp.Label}</div>` +
            `<div class="contact-meta">(${Math.round(wp.X)}, ${Math.round(wp.Y)}) · Dist: ${dist}</div>` +
            `</div>` +
            `<button class="btn btn-danger btn-sm" data-wp-id="${wp.Id}">✕</button>` +
            `</div>`;
    }).join('');

    if (html !== _lastWaypointsHtml) {
        _lastWaypointsHtml = html;
        container.innerHTML = html;
    }
}

function setWaypoint(x, y) {
    client.sendCommand('SetWaypoint', { x: Math.round(x), y: Math.round(y) });
}

function removeWaypoint(wpId) {
    client.sendCommand('RemoveWaypoint', { waypoint_id: wpId });
}

function setFlightMode(mode) {
    client.sendCommand('ChangeFlightMode', { mode });
}

function highlightRoute() {
    client.sendCommand('HighlightRoute', {});
    client.showToast('Route an Hauptschirm gesendet', 'info');
}

function toggleStarMapOnMainScreen() {
    client.sendCommand('ToggleStarMapOnMainScreen', {});
}

function updateStarMapButton() {
    const btn = document.getElementById('toggle-starmap-btn');
    if (!btn) return;
    if (starMapOnMainScreen) {
        btn.textContent = 'Sektorkarte ausblenden';
        btn.style.borderColor = 'var(--cyan)';
        btn.style.background = 'rgba(0, 200, 230, 0.15)';
    } else {
        btn.textContent = 'Sektorkarte auf Hauptschirm';
        btn.style.borderColor = '';
        btn.style.background = '';
    }
}

client.connect();
