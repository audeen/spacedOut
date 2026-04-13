/**
 * SpacedOut - Navigator Station UI Logic
 * Zoomable/pannable map, heading indicator, distance readouts
 */

const MAP_SIZE = 1000;
const LONG_PRESS_MS = 520;
const LONG_PRESS_MOVE_CANCEL = 14;
let canvas, ctx;

client.on('welcome', () => {
    client.selectRole('CaptainNav');
});

let shipPos = { x: 100, y: 100, z: 500 };
let prevShipPos = { x: 100, y: 100, z: 500 };
let waypoints = [];
let contacts = [];
let currentFlightMode = 'Cruise';
let speedLevel = 2;
let starMapOnMainScreen = false;
let waypointAltitude = 500;
let navSensorRange = 500;
let selectedNavContactId = null;
let navAnimFrame = null;

// Zoom & pan
let zoom = 1.0;
let panX = 0, panY = 0;
let isDragging = false;
let dragStart = { x: 0, y: 0 };
let panStart = { x: 0, y: 0 };
let lastTapTime = 0;

/** Screen-space hit test: returns waypoint Id if tap is on an unreached waypoint marker. */
function hitTestWaypointScreen(clientX, clientY) {
    if (!canvas) return null;
    const rect = canvas.getBoundingClientRect();
    const cx = clientX - rect.left;
    const cy = clientY - rect.top;
    const w = canvas.width;
    const s = (w / MAP_SIZE) * zoom;
    const unreached = waypoints.filter(wp => !wp.IsReached);
    const hitR = Math.max(16, 12 * zoom);
    for (const wp of unreached) {
        const wx = panX + wp.X * s;
        const wy = panY + wp.Y * s;
        if (Math.hypot(cx - wx, cy - wy) < hitR) return wp.Id;
    }
    return null;
}

function hitTestContactScreen(clientX, clientY) {
    if (!canvas) return null;
    const rect = canvas.getBoundingClientRect();
    const cx = clientX - rect.left;
    const cy = clientY - rect.top;
    const w = canvas.width;
    const s = (w / MAP_SIZE) * zoom;
    const hitR = Math.max(18, 14 * zoom);

    for (const c of contacts) {
        const sx = panX + c.PositionX * s;
        const sy = panY + c.PositionY * s;
        if (Math.hypot(cx - sx, cy - sy) < hitR) return c.Id;
    }
    return null;
}

function clientToMapCoords(clientX, clientY) {
    const rect = canvas.getBoundingClientRect();
    const cx = clientX - rect.left;
    const cy = clientY - rect.top;
    const w = canvas.width;
    const s = (w / MAP_SIZE) * zoom;
    return {
        mapX: (cx - panX) / s,
        mapY: (cy - panY) / s,
    };
}

let mapLongPressTimer = null;
let mapLongPressStart = null;
let mapLongPressWpId = null;
let mapLongPressFired = false;

function clearMapLongPress() {
    if (mapLongPressTimer) {
        clearTimeout(mapLongPressTimer);
        mapLongPressTimer = null;
    }
    mapLongPressStart = null;
    mapLongPressWpId = null;
}

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
        const btn = e.target.closest('button[data-wp-id]');
        if (btn) removeWaypoint(btn.dataset.wpId);
    });

    setupWaypointListLongPress();
});

let listLongTimer = null;
let listLongStart = null;

function setupWaypointListLongPress() {
    const container = document.getElementById('waypoint-list');

    const cancelListLong = () => {
        if (listLongTimer) {
            clearTimeout(listLongTimer);
            listLongTimer = null;
        }
        listLongStart = null;
    };

    container.addEventListener('pointerdown', (e) => {
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        if (e.target.closest('button[data-wp-id]')) return;
        const row = e.target.closest('.contact-item[data-wp-id]');
        if (!row) return;
        listLongStart = { x: e.clientX, y: e.clientY };
        const wpId = row.dataset.wpId;
        listLongTimer = setTimeout(() => {
            listLongTimer = null;
            listLongStart = null;
            removeWaypoint(wpId);
            if (typeof client.showToast === 'function') {
                client.showToast('Wegpunkt entfernt', 'info');
            }
        }, LONG_PRESS_MS);
    });

    container.addEventListener('pointermove', (e) => {
        if (!listLongTimer || !listLongStart) return;
        if (Math.hypot(e.clientX - listLongStart.x, e.clientY - listLongStart.y) > LONG_PRESS_MOVE_CANCEL) {
            cancelListLong();
        }
    });

    container.addEventListener('pointerup', cancelListLong);
    container.addEventListener('pointercancel', cancelListLong);
    container.addEventListener('pointerleave', (e) => {
        if (e.target === container) cancelListLong();
    });
}

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

// Mouse drag + long-press on waypoint to remove
function onPointerDown(e) {
    if (e.button !== 0) return;
    mapLongPressFired = false;
    clearMapLongPress();
    mapLongPressStart = { x: e.clientX, y: e.clientY };
    const hitId = hitTestWaypointScreen(e.clientX, e.clientY);
    if (hitId) {
        mapLongPressWpId = hitId;
        mapLongPressTimer = setTimeout(() => {
            mapLongPressTimer = null;
            removeWaypoint(hitId);
            mapLongPressFired = true;
            mapLongPressWpId = null;
            mapLongPressStart = null;
            if (typeof client.showToast === 'function') {
                client.showToast('Wegpunkt entfernt', 'info');
            }
        }, LONG_PRESS_MS);
        isDragging = false;
        panStart = { x: panX, y: panY };
        dragStart = { x: e.clientX, y: e.clientY };
        return;
    }
    isDragging = true;
    dragStart = { x: e.clientX, y: e.clientY };
    panStart = { x: panX, y: panY };
}

function onPointerMove(e) {
    if (mapLongPressTimer && mapLongPressStart) {
        const d = Math.hypot(e.clientX - mapLongPressStart.x, e.clientY - mapLongPressStart.y);
        if (d > LONG_PRESS_MOVE_CANCEL) {
            clearMapLongPress();
            isDragging = true;
            panStart = { x: panX, y: panY };
            dragStart = { x: e.clientX, y: e.clientY };
        }
    }
    if (!isDragging) return;
    panX = panStart.x + (e.clientX - dragStart.x);
    panY = panStart.y + (e.clientY - dragStart.y);
    drawMap();
}

function onPointerUp(e) {
    if (e.button !== 0) return;
    if (mapLongPressTimer) {
        clearTimeout(mapLongPressTimer);
        mapLongPressTimer = null;
    }

    if (mapLongPressFired) {
        mapLongPressFired = false;
        isDragging = false;
        mapLongPressWpId = null;
        mapLongPressStart = null;
        return;
    }

    const dx = Math.abs(e.clientX - dragStart.x);
    const dy = Math.abs(e.clientY - dragStart.y);
    isDragging = false;

    if (dx < 5 && dy < 5) {
        const contactHit = hitTestContactScreen(e.clientX, e.clientY);
        if (contactHit) {
            selectNavContact(contactHit);
            mapLongPressWpId = null;
            mapLongPressStart = null;
            return;
        }
        if (selectedNavContactId) {
            deselectNavContact();
            mapLongPressWpId = null;
            mapLongPressStart = null;
            return;
        }
        const { mapX, mapY } = clientToMapCoords(e.clientX, e.clientY);
        if (mapX >= 0 && mapX <= MAP_SIZE && mapY >= 0 && mapY <= MAP_SIZE) {
            if (hitTestWaypointScreen(e.clientX, e.clientY)) {
                return;
            }
            setWaypoint(mapX, mapY);
        }
    }
    mapLongPressWpId = null;
    mapLongPressStart = null;
}

// Touch handling
let touchStartDist = 0;
let touchStartZoom = 1;

function onTouchStart(e) {
    if (e.touches.length === 2) {
        e.preventDefault();
        clearMapLongPress();
        const t = e.touches;
        touchStartDist = Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);
        touchStartZoom = zoom;
        isDragging = false;
    } else if (e.touches.length === 1) {
        mapLongPressFired = false;
        clearMapLongPress();
        const touch = e.touches[0];
        mapLongPressStart = { x: touch.clientX, y: touch.clientY };
        const hitId = hitTestWaypointScreen(touch.clientX, touch.clientY);
        if (hitId) {
            mapLongPressWpId = hitId;
            mapLongPressTimer = setTimeout(() => {
                mapLongPressTimer = null;
                removeWaypoint(hitId);
                mapLongPressFired = true;
                mapLongPressWpId = null;
                mapLongPressStart = null;
                if (typeof client.showToast === 'function') {
                    client.showToast('Wegpunkt entfernt', 'info');
                }
            }, LONG_PRESS_MS);
            isDragging = false;
        } else {
            isDragging = true;
        }
        dragStart = { x: touch.clientX, y: touch.clientY };
        panStart = { x: panX, y: panY };
    }
}

function onTouchMove(e) {
    e.preventDefault();
    if (e.touches.length === 2) {
        clearMapLongPress();
        const t = e.touches;
        const dist = Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);
        zoom = Math.max(0.5, Math.min(5, touchStartZoom * (dist / touchStartDist)));
        updateMapInfo();
        drawMap();
    } else if (mapLongPressTimer && mapLongPressStart && e.touches.length === 1) {
        const touch = e.touches[0];
        const d = Math.hypot(touch.clientX - mapLongPressStart.x, touch.clientY - mapLongPressStart.y);
        if (d > LONG_PRESS_MOVE_CANCEL) {
            clearMapLongPress();
            isDragging = true;
            panStart = { x: panX, y: panY };
            dragStart = { x: touch.clientX, y: touch.clientY };
        }
    } else if (isDragging && e.touches.length === 1) {
        panX = panStart.x + (e.touches[0].clientX - dragStart.x);
        panY = panStart.y + (e.touches[0].clientY - dragStart.y);
        drawMap();
    }
}

function onTouchEnd(e) {
    if (mapLongPressTimer) {
        clearTimeout(mapLongPressTimer);
        mapLongPressTimer = null;
    }

    if (mapLongPressFired) {
        mapLongPressFired = false;
        isDragging = false;
        mapLongPressWpId = null;
        mapLongPressStart = null;
        return;
    }

    if (e.touches.length > 0) {
        return;
    }

    if (e.changedTouches.length === 1) {
        const touch = e.changedTouches[0];
        const dx = Math.abs(touch.clientX - dragStart.x);
        const dy = Math.abs(touch.clientY - dragStart.y);

        if (dx < 10 && dy < 10) {
            const now = Date.now();
            if (now - lastTapTime > 300) {
                const contactHit = hitTestContactScreen(touch.clientX, touch.clientY);
                if (contactHit) {
                    selectNavContact(contactHit);
                } else if (selectedNavContactId) {
                    deselectNavContact();
                } else {
                    const { mapX, mapY } = clientToMapCoords(touch.clientX, touch.clientY);
                    if (mapX >= 0 && mapX <= MAP_SIZE && mapY >= 0 && mapY <= MAP_SIZE) {
                        if (!hitTestWaypointScreen(touch.clientX, touch.clientY)) {
                            setWaypoint(mapX, mapY);
                        }
                    }
                }
            }
            lastTapTime = now;
        }
    }
    isDragging = false;
    mapLongPressWpId = null;
    mapLongPressStart = null;
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

    // Fog of war
    const fogSensorR = navSensorRange * s;
    const fogCx = shipPos.x * s;
    const fogCy = shipPos.y * s;
    for (let ring = 0; ring < 20; ring++) {
        const r = fogSensorR + ring * 12;
        if (r > MAP_SIZE * s * 1.5) break;
        const alpha = Math.min(ring * 0.03, 0.4);
        ctx.strokeStyle = `rgba(3, 5, 18, ${alpha})`;
        ctx.lineWidth = 12;
        ctx.beginPath();
        ctx.arc(fogCx, fogCy, r, 0, Math.PI * 2);
        ctx.stroke();
    }

    // Contacts
    contacts.forEach(c => {
        const cx = c.PositionX * s;
        const cy = c.PositionY * s;
        const cAlt = (c.PositionZ ?? 500) - shipPos.z;

        const isDetectedOnly = c.Discovery === 'Detected';
        const isSelected = c.Id === selectedNavContactId;
        const baseAlpha = isDetectedOnly ? 0.45 : 1.0;

        const color = c.Type === 'Friendly' ? '#2ee65a' :
            c.Type === 'Hostile' ? '#e83030' :
                c.Type === 'Unknown' ? '#e8d020' :
                    c.Type === 'Anomaly' ? '#4080f0' : '#606880';

        if (Math.abs(cAlt) > 5) {
            const stemLen = Math.max(25, Math.min(Math.abs(cAlt) * 0.4, 80)) * zoom;
            const stemDir = cAlt > 0 ? -1 : 1;
            const stemColor = cAlt > 0 ? `rgba(255,140,25,${0.55 * baseAlpha})` : `rgba(75,140,255,${0.55 * baseAlpha})`;
            const tickColor = cAlt > 0 ? `rgba(255,140,25,${0.9 * baseAlpha})` : `rgba(75,140,255,${0.9 * baseAlpha})`;
            const startY = cy + stemDir * 5 * Math.max(1, zoom * 0.7);
            const endY = cy + stemDir * stemLen;
            ctx.strokeStyle = stemColor; ctx.lineWidth = 2;
            ctx.beginPath(); ctx.moveTo(cx, startY); ctx.lineTo(cx, endY); ctx.stroke();
            ctx.strokeStyle = tickColor; ctx.lineWidth = 2.5;
            ctx.beginPath(); ctx.moveTo(cx - 6, endY); ctx.lineTo(cx + 6, endY); ctx.stroke();
            ctx.fillStyle = tickColor;
            ctx.font = `bold ${Math.max(9, 10 * Math.min(zoom, 1.5))}px monospace`;
            ctx.textAlign = 'left';
            ctx.fillText(`${cAlt >= 0 ? '+' : ''}${Math.round(cAlt)}`, cx + 9, endY + 4);
        } else {
            const altColor = `rgba(180,200,220,${0.45 * baseAlpha})`;
            ctx.fillStyle = altColor;
            ctx.font = `${Math.max(8, 9 * Math.min(zoom, 1.5))}px monospace`;
            ctx.textAlign = 'left';
            const dotSz = (isSelected ? 6 : isDetectedOnly ? 3.5 : 5) * Math.max(1, zoom * 0.7);
            ctx.fillText(`±0`, cx + dotSz + 4, cy + 12);
        }

        // Selection ring
        if (isSelected) {
            const pulse = 14 + Math.sin(Date.now() / 300) * 3;
            const ringR = pulse * Math.max(1, zoom * 0.7);
            ctx.strokeStyle = '#00d4e8';
            ctx.lineWidth = 2;
            ctx.beginPath(); ctx.arc(cx, cy, ringR, 0, Math.PI * 2); ctx.stroke();
            ctx.fillStyle = 'rgba(0, 212, 232, 0.06)';
            ctx.beginPath(); ctx.arc(cx, cy, ringR, 0, Math.PI * 2); ctx.fill();
        }

        const dotSize = (isSelected ? 6 : isDetectedOnly ? 3.5 : 5) * Math.max(1, zoom * 0.7);
        ctx.globalAlpha = baseAlpha;
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.arc(cx, cy, dotSize, 0, Math.PI * 2);
        ctx.fill();
        ctx.globalAlpha = 1;

        // Scan progress arc
        if (c.ScanProgress > 0 && c.ScanProgress < 100) {
            const scanR = 10 * Math.max(1, zoom * 0.7);
            ctx.strokeStyle = 'rgba(0, 200, 230, 0.5)';
            ctx.lineWidth = 1.5;
            ctx.beginPath();
            ctx.arc(cx, cy, scanR, -Math.PI / 2, -Math.PI / 2 + (c.ScanProgress / 100) * Math.PI * 2);
            ctx.stroke();
        }

        // Scanning pulse
        if (c.IsScanning) {
            const ps = (5 + Math.sin(Date.now() / 200) * 3) * Math.max(1, zoom * 0.7);
            ctx.strokeStyle = color; ctx.lineWidth = 1; ctx.globalAlpha = 0.35;
            ctx.beginPath(); ctx.arc(cx, cy, ps + 4, 0, Math.PI * 2); ctx.stroke();
            ctx.globalAlpha = 1;
        }

        // Velocity pip
        const vx = c.VelocityX || 0, vy = c.VelocityY || 0;
        if (Math.abs(vx) > 0.1 || Math.abs(vy) > 0.1) {
            const speed = Math.sqrt(vx * vx + vy * vy);
            const dirX = vx / speed, dirY = vy / speed;
            const lineLen = Math.min(speed * 15 * s, 50 * zoom);
            ctx.strokeStyle = 'rgba(230, 220, 50, 0.3)';
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(cx, cy);
            ctx.lineTo(cx + dirX * lineLen, cy + dirY * lineLen);
            ctx.stroke();

            for (let pi = 1; pi <= 3; pi++) {
                const t = pi * 5;
                const px = cx + vx * t * s;
                const py = cy + vy * t * s;
                const pipSize = Math.max(0.8, (2 - pi * 0.4) * Math.max(1, zoom * 0.5));
                ctx.fillStyle = `rgba(230, 220, 50, ${0.7 - pi * 0.15})`;
                ctx.beginPath(); ctx.arc(px, py, pipSize, 0, Math.PI * 2); ctx.fill();
            }
        }

        ctx.textAlign = 'center';
        if (isDetectedOnly) {
            ctx.fillStyle = `rgba(255,255,255,0.35)`;
            ctx.font = `${Math.max(9, 10 * Math.min(zoom, 1.5))}px monospace`;
            ctx.fillText('???', cx, cy - 10 * Math.max(1, zoom * 0.5));
        } else {
            ctx.fillStyle = isSelected ? '#ffffff' : `rgba(255,255,255,${0.7 * baseAlpha})`;
            ctx.font = `${isSelected ? 'bold ' : ''}${Math.max(9, 10 * Math.min(zoom, 1.5))}px monospace`;
            ctx.fillText(c.DisplayName || c.Id, cx, cy - 10 * Math.max(1, zoom * 0.5));
        }
    });

    // Waypoints
    waypoints.forEach((wp, i) => {
        const wx = wp.X * s;
        const wy = wp.Y * s;
        const reached = wp.IsReached;
        const sz = 7 * Math.max(1, zoom * 0.6);
        const wpAlt = (wp.Z ?? 500) - shipPos.z;

        if (!reached && Math.abs(wpAlt) > 5) {
            const stemLen = Math.max(25, Math.min(Math.abs(wpAlt) * 0.4, 80)) * zoom;
            const stemDir = wpAlt > 0 ? -1 : 1;
            const stemColor = wpAlt > 0 ? 'rgba(255,140,25,0.55)' : 'rgba(75,140,255,0.55)';
            const tickColor = wpAlt > 0 ? 'rgba(255,140,25,0.9)' : 'rgba(75,140,255,0.9)';
            const startY = wy + stemDir * sz;
            const endY = wy + stemDir * stemLen;
            ctx.strokeStyle = stemColor;
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.moveTo(wx, startY);
            ctx.lineTo(wx, endY);
            ctx.stroke();
            ctx.strokeStyle = tickColor;
            ctx.lineWidth = 2.5;
            ctx.beginPath();
            ctx.moveTo(wx - 6, endY);
            ctx.lineTo(wx + 6, endY);
            ctx.stroke();
            ctx.strokeStyle = stemColor;
            ctx.lineWidth = 1.5;
            ctx.beginPath();
            ctx.moveTo(wx - 4, wy);
            ctx.lineTo(wx + 4, wy);
            ctx.stroke();
            ctx.fillStyle = tickColor;
            ctx.font = `bold ${Math.max(9, 10 * Math.min(zoom, 1.5))}px monospace`;
            ctx.textAlign = 'left';
            ctx.fillText(`${wpAlt >= 0 ? '+' : ''}${Math.round(wpAlt)}`, wx + 9, endY + 4);
        }

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

            const dxw = wp.X - shipPos.x, dyw = wp.Y - shipPos.y;
            const dzw = (wp.Z ?? 500) - shipPos.z;
            const dist = Math.round(Math.sqrt(dxw * dxw + dyw * dyw + dzw * dzw));
            ctx.fillStyle = 'rgba(0,200,230,0.6)';
            ctx.font = `${Math.max(8, 9 * Math.min(zoom, 1.5))}px monospace`;
            ctx.textAlign = 'center';
            ctx.fillText(`${dist}`, wx, wy + sz + 12);
        }

        ctx.fillStyle = reached ? 'rgba(255,255,255,0.25)' : '#fff';
        ctx.font = `${Math.max(9, 10 * Math.min(zoom, 1.5))}px sans-serif`;
        ctx.textAlign = 'center';
        ctx.fillText(wp.Label || `WP${i + 1}`, wx, wy - sz - 4);
    });

    // Sensor range circle
    ctx.strokeStyle = 'rgba(0, 200, 230, 0.12)';
    ctx.lineWidth = 1.5;
    ctx.setLineDash([4, 4]);
    ctx.beginPath();
    ctx.arc(shipPos.x * s, shipPos.y * s, navSensorRange * s, 0, Math.PI * 2);
    ctx.stroke();
    ctx.setLineDash([]);

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

    // Ship label + altitude
    ctx.fillStyle = '#00d4e8';
    ctx.font = `bold ${Math.max(10, 11 * Math.min(zoom, 1.5))}px sans-serif`;
    ctx.textAlign = 'center';
    ctx.fillText('SCHIFF', sx, sy + shipSize + 14);
    const shipAltDiff = Math.round(shipPos.z - 500);
    const shipAltLabel = `ALT ${shipAltDiff >= 0 ? '+' : ''}${shipAltDiff}`;
    ctx.fillStyle = 'rgba(0,212,232,0.6)';
    ctx.font = `${Math.max(8, 9 * Math.min(zoom, 1.5))}px monospace`;
    ctx.fillText(shipAltLabel, sx, sy + shipSize + 24);

    ctx.restore();
}

// --- State Updates ---

client.onState((msg) => {
    if (!msg.data) return;
    const d = msg.data;

    updatePhaseHeaderFromState(msg);
    document.getElementById('timer-display').textContent = formatTime(msg.elapsed_time || 0);

    prevShipPos = { ...shipPos };
    shipPos = { x: d.ship_x ?? 100, y: d.ship_y ?? 100, z: d.ship_z ?? 500 };
    currentFlightMode = d.flight_mode || 'Cruise';
    speedLevel = d.speed_level ?? 2;
    contacts = d.contacts || [];
    navSensorRange = d.sensor_range ?? 500;

    const altDiff = Math.round(shipPos.z - 500);
    const altStr = altDiff >= 0 ? `+${altDiff}` : `${altDiff}`;
    document.getElementById('position-display').textContent =
        `${Math.round(shipPos.x)}, ${Math.round(shipPos.y)}`;
    document.getElementById('altitude-display').textContent = altStr;
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

    // Distance to next waypoint (3D)
    const nextWp = waypoints.find(wp => !wp.IsReached);
    if (nextWp) {
        const dxw = nextWp.X - shipPos.x, dyw = nextWp.Y - shipPos.y;
        const dzw = (nextWp.Z ?? 500) - shipPos.z;
        const dist = Math.round(Math.sqrt(dxw * dxw + dyw * dyw + dzw * dzw));
        document.getElementById('next-wp-dist').textContent = dist;
    } else {
        document.getElementById('next-wp-dist').textContent = '---';
    }

    starMapOnMainScreen = d.star_map_on_main_screen || false;
    updateStarMapButton();

    updateFlightModeButtons();
    renderWaypoints();
    if (selectedNavContactId) updateNavContactPanel();
    drawMap();

    // Captain data (merged)
    renderDecisions(d.pending_decisions || []);
    renderOverlays(d.overlays || []);
    renderCaptainEvents(d.active_events || []);
    currentLog = d.log || [];
    renderLog();

    const hull = d.hull_integrity ?? 100;
    const hullEl = document.getElementById('hull-value');
    if (hullEl) {
        hullEl.textContent = `${Math.round(hull)}%`;
        hullEl.className = `status-value ${hull > 60 ? 'text-green' : hull > 30 ? 'text-yellow' : 'text-red'}`;
    }
    const hullBar = document.getElementById('hull-bar');
    if (hullBar) {
        hullBar.style.width = `${hull}%`;
        hullBar.parentElement.className = `progress-bar ${hull > 60 ? 'progress-green' : hull > 30 ? 'progress-yellow' : 'progress-red'}`;
    }

    const sysSummary = d.systems_summary || {};
    const sysEl = document.getElementById('systems-summary');
    if (sysEl) {
        sysEl.innerHTML = Object.entries(sysSummary).map(([key, status]) =>
            `<span class="system-status ${getStatusClass(status)}">${key}: ${getStatusLabel(status)}</span>`
        ).join(' ');
    }

    const ruleEl = document.getElementById('current-engagement-rule');
    if (ruleEl) ruleEl.textContent = (d.engagement_rule || 'Standard').toUpperCase();
    updateEngagementButtons(d.engagement_rule || 'Standard');

    // Run map: track mission state and show/hide overlay as fallback
    if (msg.mission_started) {
        missionActive = true;
        if (runMapVisible) hideRunMap();
    } else if (d.run_active && runMapData && !runMapVisible) {
        missionActive = false;
        showRunMap();
    }
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

// ── Run Map (between missions / transit) ────────────────────────

let runMapData = null;
let selectedNodeId = null;
let runCanvas = null;
let runCtx = null;
let runMapVisible = false;
let missionActive = false;
let _runPulse = 0;
let _runAnimFrame = null;

const RUN_TYPE_FILL = {
    Start:   '#4da6f2',
    Story:   '#f2f2ff',
    Side:    '#737a8c',
    Station: '#33bf73',
    Hostile: '#e65926',
    Anomaly: '#8c40d9',
    End:     '#f2d933',
};

const RUN_TYPE_ICONS = {
    Start: '▶', Story: '◆', Side: '◇',
    Station: '⛽', Hostile: '⊕', Anomaly: '◉', End: '★',
};

const RUN_STATE_BORDER = {
    Reachable: '#00d4e8',
    Completed: '#2ee65a',
    Failed:    '#e83030',
    Locked:    '#e89020',
    Visible:   '#606880',
};

client.on('run_state_update', (msg) => {
    runMapData = msg.data;
    if (runMapData && runMapData.nodes && runMapData.nodes.length > 0 && !missionActive) {
        showRunMap();
    }
});

client.on('mission_started', () => {
    missionActive = true;
    hideRunMap();
});

client.on('mission_ended', () => {
    missionActive = false;
});

function showRunMap() {
    const overlay = document.getElementById('run-map-overlay');
    if (!overlay) return;
    overlay.classList.remove('hidden');
    runMapVisible = true;
    selectedNodeId = null;
    document.getElementById('node-info-panel').classList.add('hidden');

    if (!runCanvas) {
        runCanvas = document.getElementById('run-map-canvas');
        runCtx = runCanvas.getContext('2d');
        runCanvas.addEventListener('click', onRunMapClick);
        const ro = new ResizeObserver(() => resizeRunCanvas());
        ro.observe(runCanvas.parentElement);
    }

    updateRunMapResources();
    resizeRunCanvas();
    startRunPulse();
}

function hideRunMap() {
    const overlay = document.getElementById('run-map-overlay');
    if (overlay) overlay.classList.add('hidden');
    runMapVisible = false;
    stopRunPulse();
}

function resizeRunCanvas() {
    if (!runCanvas) return;
    const container = runCanvas.parentElement;
    runCanvas.width = container.clientWidth;
    runCanvas.height = container.clientHeight;
    drawRunMap();
}

function updateRunMapResources() {
    if (!runMapData) return;
    const res = runMapData.resources || {};
    document.getElementById('run-map-title').textContent =
        `EINSATZPLANUNG · ${runMapData.definition_id || ''}`;
    const visited = (runMapData.visited || []).length;
    const total = (runMapData.nodes || []).filter(n => n.state !== 'Hidden').length;
    document.getElementById('run-map-subtitle').textContent =
        `Fortschritt: ${visited}/${total} Knoten · Tiefe: ${runMapData.current_depth ?? 0}`;
    document.getElementById('resource-hull').textContent =
        `Hull: ${res.Hull ?? 10}`;
    document.getElementById('resource-parts').textContent =
        `Ersatzteile: ${res.SpareParts ?? 0}`;
    document.getElementById('resource-ammo').textContent =
        `Munition: ${res.Ammo ?? 0}`;
    document.getElementById('resource-data').textContent =
        `Daten: ${res.ScienceData ?? 0}`;
}

function startRunPulse() {
    if (_runAnimFrame) return;
    let last = performance.now();
    function tick(now) {
        const dt = (now - last) / 1000;
        last = now;
        _runPulse = (_runPulse + dt * 2.5) % (Math.PI * 2);
        if (runMapVisible && runMapData) drawRunMap();
        _runAnimFrame = requestAnimationFrame(tick);
    }
    _runAnimFrame = requestAnimationFrame(tick);
}

function stopRunPulse() {
    if (_runAnimFrame) {
        cancelAnimationFrame(_runAnimFrame);
        _runAnimFrame = null;
    }
}

function runNodePos(node, ox, oy, mapW, mapH) {
    return {
        x: ox + (node.layout_x ?? 0) * mapW,
        y: oy + (node.layout_y ?? 0) * mapH,
    };
}

function hexAlpha(color, alpha) {
    if (color.startsWith('#')) {
        const r = parseInt(color.slice(1, 3), 16);
        const g = parseInt(color.slice(3, 5), 16);
        const b = parseInt(color.slice(5, 7), 16);
        return `rgba(${r},${g},${b},${alpha})`;
    }
    const m = color.match(/rgba?\(\s*(\d+),\s*(\d+),\s*(\d+)/);
    if (m) return `rgba(${m[1]},${m[2]},${m[3]},${alpha})`;
    return color;
}

function drawRunMap() {
    if (!runCtx || !runMapData) return;
    const w = runCanvas.width, h = runCanvas.height;
    const nodes = runMapData.nodes || [];
    const ctx = runCtx;

    ctx.fillStyle = '#080d1a';
    ctx.fillRect(0, 0, w, h);

    // Cyan border
    ctx.strokeStyle = 'rgba(0, 212, 232, 0.35)';
    ctx.lineWidth = 1.5;
    ctx.strokeRect(0, 0, w, h);

    // Title
    ctx.fillStyle = 'rgba(0, 212, 232, 0.85)';
    ctx.font = '12px monospace';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'top';
    ctx.fillText(`RUN · ${runMapData.definition_id || ''}`, 10, 8);

    if (nodes.length === 0) return;

    const ox = 40, oy = 28;
    const mapW = w - 80, mapH = h - 50;
    const nodeMap = {};
    nodes.forEach(n => { nodeMap[n.Id] = n; });

    // Edges
    nodes.forEach(node => {
        if (node.state === 'Hidden') return;
        const from = runNodePos(node, ox, oy, mapW, mapH);
        (node.NextNodeIds || []).forEach(nextId => {
            const next = nodeMap[nextId];
            if (!next || next.state === 'Hidden') return;
            const to = runNodePos(next, ox, oy, mapW, mapH);

            const onPath = node.state === 'Completed' &&
                (next.state === 'Completed' || next.state === 'Reachable');
            const avail = node.state === 'Completed' && next.state === 'Reachable';

            if (onPath || avail) {
                const pulse = avail ? 0.45 + Math.sin(_runPulse) * 0.2 : 0.55;
                ctx.strokeStyle = `rgba(0, 212, 232, ${pulse})`;
                ctx.lineWidth = avail ? 2.5 : 2;
            } else {
                ctx.strokeStyle = 'rgba(51, 77, 128, 0.35)';
                ctx.lineWidth = 1;
            }

            ctx.beginPath();
            ctx.moveTo(from.x, from.y);
            ctx.lineTo(to.x, to.y);
            ctx.stroke();
        });
    });

    // Nodes
    nodes.forEach(node => {
        if (node.state === 'Hidden') return;

        const p = runNodePos(node, ox, oy, mapW, mapH);
        const know = node.knowledge || 'Unknown';
        const typeColor = RUN_TYPE_FILL[node.type] || '#606880';
        let fill = typeColor;
        let border = RUN_STATE_BORDER[node.state] || '#808090';
        let radius = 18;

        switch (node.state) {
            case 'Completed':
                fill = hexAlpha(typeColor, 0.55);
                border = '#2ee65a';
                break;
            case 'Failed':
                fill = 'rgba(232, 48, 48, 0.45)';
                border = '#e83030';
                break;
            case 'Reachable':
                border = '#00d4e8';
                radius = 20;
                break;
            case 'Locked':
                border = '#e89020';
                fill = hexAlpha(typeColor, 0.5);
                break;
            case 'Visible':
                border = '#606880';
                fill = hexAlpha(typeColor, 0.42);
                break;
        }

        if (know === 'Unknown') {
            fill = 'rgba(56, 61, 71, 0.95)';
            border = 'rgba(102, 115, 128, 0.85)';
        } else if (know === 'Detected') {
            fill = 'rgba(64, 82, 122, 0.82)';
            border = 'rgba(115, 140, 191, 0.9)';
        }

        // Glow ring
        ctx.beginPath();
        ctx.arc(p.x, p.y, radius + 2, 0, Math.PI * 2);
        ctx.fillStyle = hexAlpha(border, 0.35);
        ctx.fill();

        // Main circle
        ctx.beginPath();
        ctx.arc(p.x, p.y, radius, 0, Math.PI * 2);
        ctx.fillStyle = fill;
        ctx.fill();

        // Current node: yellow ring
        if (runMapData.current_node_id && runMapData.current_node_id === node.Id) {
            ctx.beginPath();
            ctx.arc(p.x, p.y, radius + 6, 0, Math.PI * 2);
            ctx.strokeStyle = 'rgba(242, 217, 51, 0.9)';
            ctx.lineWidth = 2;
            ctx.stroke();
        } else if (selectedNodeId === node.Id) {
            ctx.beginPath();
            ctx.arc(p.x, p.y, radius + 5, 0, Math.PI * 2);
            ctx.strokeStyle = 'rgba(255, 255, 255, 0.55)';
            ctx.lineWidth = 1.5;
            ctx.stroke();
        }

        // Icon or fog label
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        if (know === 'Unknown') {
            ctx.fillStyle = '#ffffff';
            ctx.font = '14px monospace';
            ctx.fillText('·', p.x, p.y);
        } else if (know === 'Detected') {
            ctx.fillStyle = '#c0c8e0';
            ctx.font = '9px monospace';
            const fogLabel = (node.Id.charCodeAt(0) & 1) === 0 ? 'Signal' : 'Echo';
            ctx.fillText(fogLabel, p.x, p.y);
        } else {
            ctx.fillStyle = '#ffffff';
            ctx.font = `${radius * 0.85}px sans-serif`;
            ctx.fillText(RUN_TYPE_ICONS[node.type] || '?', p.x, p.y);
        }

        // Label below node
        if (know === 'Identified' && node.Title) {
            const label = node.Title.length > 16 ? node.Title.substring(0, 14) + '..' : node.Title;
            ctx.fillStyle = node.state === 'Reachable' ? '#e0e2f0' : 'rgba(224, 226, 240, 0.5)';
            ctx.font = '10px sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'top';
            ctx.fillText(label, p.x, p.y + radius + 4);
        }
    });
}

function onRunMapClick(e) {
    if (!runMapData) return;
    const rect = runCanvas.getBoundingClientRect();
    const cx = e.clientX - rect.left;
    const cy = e.clientY - rect.top;

    const nodes = runMapData.nodes || [];
    const w = runCanvas.width, h = runCanvas.height;
    const ox = 40, oy = 28;
    const mapW = w - 80, mapH = h - 50;

    let closest = null;
    let closestDist = Infinity;

    nodes.forEach(node => {
        if (node.state === 'Hidden') return;
        if (node.state !== 'Reachable') return;
        const p = runNodePos(node, ox, oy, mapW, mapH);
        const dist = Math.hypot(cx - p.x, cy - p.y);
        if (dist < 30 && dist < closestDist) {
            closest = node;
            closestDist = dist;
        }
    });

    if (closest) {
        selectedNodeId = closest.Id;
        showRunNodeInfo(closest);
        drawRunMap();
    }
}

function showRunNodeInfo(node) {
    const panel = document.getElementById('node-info-panel');
    panel.classList.remove('hidden');

    const icon = RUN_TYPE_ICONS[node.type] || '?';
    const title = node.Title || node.Id;
    document.getElementById('node-info-title').textContent = `${icon} ${title}`;

    const riskStars = '★'.repeat(node.RiskRating || 0) + '☆'.repeat(Math.max(0, 5 - (node.RiskRating || 0)));
    document.getElementById('node-info-desc').textContent =
        `Risiko: ${riskStars}`;
    document.getElementById('node-info-meta').textContent =
        `Typ: ${node.type} · Tiefe: ${node.Depth ?? '?'}`;

    const btn = document.getElementById('node-select-btn');
    btn.disabled = false;
    btn.textContent = 'Kurs setzen';
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
        const dxw = wp.X - shipPos.x, dyw = wp.Y - shipPos.y;
        const dzw = (wp.Z ?? 500) - shipPos.z;
        const dist = Math.round(Math.sqrt(dxw * dxw + dyw * dyw + dzw * dzw));
        const wpAlt = Math.round((wp.Z ?? 500) - 500);
        const altStr = Math.abs(wpAlt) > 10 ? ` · ALT ${wpAlt >= 0 ? '+' : ''}${wpAlt}` : '';
        return `<div class="contact-item" data-wp-id="${wp.Id}">` +
            `<div class="contact-dot" style="background:var(--cyan);"></div>` +
            `<div class="contact-info">` +
            `<div class="contact-name">${wp.Label}</div>` +
            `<div class="contact-meta">(${Math.round(wp.X)}, ${Math.round(wp.Y)}) · Dist: ${dist}${altStr}</div>` +
            `</div>` +
            `<button type="button" class="btn btn-danger btn-sm" data-wp-id="${wp.Id}">✕</button>` +
            `</div>`;
    }).join('');

    if (html !== _lastWaypointsHtml) {
        _lastWaypointsHtml = html;
        container.innerHTML = html;
    }
}

function setWaypoint(x, y) {
    const altSlider = document.getElementById('wp-altitude');
    const z = altSlider ? parseInt(altSlider.value) : Math.round(shipPos.z);
    client.sendCommand('SetWaypoint', { x: Math.round(x), y: Math.round(y), z });
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

// --- Contact Selection ---

function selectNavContact(contactId) {
    selectedNavContactId = contactId === selectedNavContactId ? null : contactId;
    updateNavContactPanel();
    drawMap();
    startNavAnimation();
}

function deselectNavContact() {
    selectedNavContactId = null;
    updateNavContactPanel();
    drawMap();
}

function startNavAnimation() {
    if (navAnimFrame) return;
    function frame() {
        if (!selectedNavContactId) {
            navAnimFrame = null;
            return;
        }
        drawMap();
        navAnimFrame = requestAnimationFrame(frame);
    }
    navAnimFrame = requestAnimationFrame(frame);
}

function updateNavContactPanel() {
    const panel = document.getElementById('nav-contact-panel');
    if (!selectedNavContactId) {
        panel.style.display = 'none';
        return;
    }

    const c = contacts.find(ct => ct.Id === selectedNavContactId);
    if (!c) {
        panel.style.display = 'none';
        selectedNavContactId = null;
        return;
    }

    panel.style.display = '';

    const color = c.Type === 'Friendly' ? '#2ee65a' :
                  c.Type === 'Hostile' ? '#e83030' :
                  c.Type === 'Unknown' ? '#e8d020' :
                  c.Type === 'Anomaly' ? '#4080f0' : '#606880';

    const titleEl = document.getElementById('nav-contact-title');
    titleEl.innerHTML = `<span style="color:${color};">●</span> ${c.DisplayName || c.Id}`;

    const cdx = c.PositionX - shipPos.x;
    const cdy = c.PositionY - shipPos.y;
    const cdz = (c.PositionZ ?? 500) - shipPos.z;
    const dist = Math.round(Math.sqrt(cdx * cdx + cdy * cdy + cdz * cdz));
    const bearing = Math.round((Math.atan2(cdy, cdx) * 180 / Math.PI + 360) % 360);
    const altDiff = Math.round(cdz);
    const altStr = altDiff >= 0 ? `+${altDiff}` : `${altDiff}`;
    const speed = Math.sqrt((c.VelocityX || 0) ** 2 + (c.VelocityY || 0) ** 2);

    const isScanned = c.Discovery === 'Scanned';
    const scanPct = Math.round(c.ScanProgress);

    let rows = '';

    rows += `<div class="status-row"><span class="status-label">Entfernung</span><span class="status-value text-cyan" style="font-size:16px;">${dist}</span></div>`;
    rows += `<div class="status-row"><span class="status-label">Peilung</span><span class="status-value" style="font-size:14px;">${bearing}°</span></div>`;

    if (Math.abs(cdz) > 5) {
        rows += `<div class="status-row"><span class="status-label">Höhendiff.</span><span class="status-value" style="font-size:14px; color:${cdz > 0 ? '#ff8c19' : '#4b8cff'};">${altStr}</span></div>`;
    }

    rows += `<div class="status-row"><span class="status-label">Position</span><span class="status-value" style="font-size:13px;">${Math.round(c.PositionX)}, ${Math.round(c.PositionY)}</span></div>`;

    if (isScanned) {
        rows += `<div class="status-row"><span class="status-label">Typ</span><span class="status-value" style="font-size:14px; color:${color};">${c.Type}</span></div>`;

        if (c.ThreatLevel > 0) {
            const tc = c.ThreatLevel > 7 ? '#e83030' : c.ThreatLevel > 4 ? '#e8d020' : '#2ee65a';
            rows += `<div class="status-row"><span class="status-label">Bedrohung</span><span class="status-value" style="font-size:14px; color:${tc};">Stufe ${Math.round(c.ThreatLevel)}</span></div>`;
        }

        if (speed > 0.1) {
            const velBearing = Math.round((Math.atan2(c.VelocityY, c.VelocityX) * 180 / Math.PI + 360) % 360);
            rows += `<div class="status-row"><span class="status-label">Geschwindigkeit</span><span class="status-value" style="font-size:13px;">${speed.toFixed(1)} (${velBearing}°)</span></div>`;
        }
    } else {
        rows += `<div class="status-row"><span class="status-label">Scan</span><span class="status-value" style="font-size:14px;">${scanPct}%${c.IsScanning ? ' ◉' : ''}</span></div>`;
        rows += `<div style="font-size:11px; color:rgba(255,255,255,0.35); margin-top:4px;">Weitere Daten erfordern vollständigen Scan durch Taktik.</div>`;
    }

    document.getElementById('nav-contact-details').innerHTML = rows;

    const targetBtn = document.getElementById('nav-target-btn');
    targetBtn.disabled = false;
    targetBtn.textContent = 'Ziel setzen';
}

function setContactAsTarget() {
    if (!selectedNavContactId) return;
    const c = contacts.find(ct => ct.Id === selectedNavContactId);
    if (!c) return;

    const z = c.PositionZ ?? 500;

    const unreached = waypoints.filter(wp => !wp.IsReached);
    unreached.forEach(wp => {
        client.sendCommand('RemoveWaypoint', { waypoint_id: wp.Id });
    });

    setTimeout(() => {
        client.sendCommand('SetWaypoint', { x: Math.round(c.PositionX), y: Math.round(c.PositionY), z: Math.round(z) });
        client.showToast(`Kurs auf ${c.DisplayName || c.Id} gesetzt`, 'info');
    }, 50);
}

// ── Captain functions (merged) ──

let briefingShown = false;
let logFilter = 'all';
let currentLog = [];

client.on('mission_started', (msg) => {
    if (!briefingShown && msg.briefing) {
        briefingShown = true;
        const el = document.getElementById('briefing-text');
        if (el) el.textContent = msg.briefing;
        const ov = document.getElementById('briefing-overlay');
        if (ov) ov.classList.remove('hidden');
    }
});

function dismissBriefing() {
    const el = document.getElementById('briefing-overlay');
    if (el) el.classList.add('hidden');
}

let _lastDecisionIds = '';
function renderDecisions(decisions) {
    const panel = document.getElementById('decisions-panel');
    const container = document.getElementById('decisions-container');
    if (!panel || !container) return;
    const ids = decisions.map(d => d.Id).join(',');
    if (ids === _lastDecisionIds) return;
    _lastDecisionIds = ids;
    if (decisions.length === 0) { panel.style.display = 'none'; container.innerHTML = ''; return; }
    panel.style.display = '';
    container.innerHTML = decisions.map(dec => `
        <div class="decision-card urgent">
            <div class="decision-title">${dec.Title}</div>
            <div class="decision-desc">${dec.Description}</div>
            <div class="decision-options">
                ${dec.Options.map(opt => `
                    <button class="btn btn-primary btn-block" onclick="resolveDecision('${dec.Id}','${opt.Id}')" style="text-align:left;">
                        <strong>${opt.Label}</strong>
                        <span class="text-dim" style="font-size:11px; margin-left:8px;">${opt.Description}</span>
                    </button>
                `).join('')}
            </div>
        </div>
    `).join('');
}

let _lastOverlaysKey = '';
function renderOverlays(overlays) {
    const panel = document.getElementById('overlays-panel');
    const container = document.getElementById('overlays-container');
    if (!panel || !container) return;
    const pending = overlays.filter(o => !o.ApprovedByCaptain && !o.Dismissed);
    const key = pending.map(o => `${o.Id}:${Math.round(o.RemainingTime)}`).join('|');
    if (key === _lastOverlaysKey) return;
    _lastOverlaysKey = key;
    if (pending.length === 0) { panel.style.display = 'none'; container.innerHTML = ''; return; }
    panel.style.display = '';
    container.innerHTML = pending.map(overlay => `
        <div class="overlay-item">
            <div style="flex:1;">
                <div class="${overlay.Category === 'Warning' ? 'text-red' : 'text-cyan'}" style="font-size:12px;">[${overlay.SourceStation}] ${overlay.Category}</div>
                <div style="font-size:14px;">${overlay.Text}</div>
                <div class="text-dim" style="font-size:10px;">Prio: ${overlay.Priority} · ${Math.round(overlay.RemainingTime)}s</div>
            </div>
            <div style="display:flex; gap:6px;">
                <button class="btn btn-success btn-sm" onclick="approveOverlay('${overlay.Id}')">✓</button>
                <button class="btn btn-danger btn-sm" onclick="dismissOverlay('${overlay.Id}')">✕</button>
            </div>
        </div>
    `).join('');
}

let _lastCaptainEventsKey = '';
function renderCaptainEvents(events) {
    const panel = document.getElementById('captain-events-panel');
    const container = document.getElementById('captain-events-container');
    if (!panel || !container) return;
    const active = events.filter(e => e.IsActive);
    const key = active.map(e => `${e.Id}:${Math.round(e.TimeRemaining)}`).join('|');
    if (key === _lastCaptainEventsKey) return;
    _lastCaptainEventsKey = key;
    if (active.length === 0) { panel.style.display = 'none'; container.innerHTML = ''; return; }
    panel.style.display = '';
    container.innerHTML = active.map(evt => {
        const pct = evt.TimeRemaining > 0 ? Math.min(100, (evt.TimeRemaining / 180) * 100) : 0;
        return `<div class="alert alert-warning">
            <div style="display:flex; justify-content:space-between;"><strong>${evt.Title}</strong>
            ${evt.TimeRemaining > 0 ? `<span style="font-family:var(--font-mono); font-size:12px;">${Math.round(evt.TimeRemaining)}s</span>` : ''}</div>
            <span style="font-size:12px;">${evt.Description}</span>
            ${evt.TimeRemaining > 0 ? `<div class="event-timer-bar"><div class="event-timer-fill" style="width:${Math.round(pct)}%"></div></div>` : ''}
        </div>`;
    }).join('');
}

function setLogFilter(filter) {
    logFilter = filter;
    document.querySelectorAll('.log-filter-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.filter === filter);
    });
    renderLog();
}

function renderLog() {
    const container = document.getElementById('log-container');
    if (!container) return;
    let entries = currentLog;
    if (logFilter !== 'all') entries = entries.filter(e => e.Source === logFilter);
    entries = entries.slice(-25).reverse();
    container.innerHTML = entries.map(entry => {
        const sourceColor = { 'System': 'var(--yellow)', 'CaptainNav': 'var(--cyan)', 'Engineer': 'var(--orange)', 'Tactical': 'var(--red)', 'Gunner': 'var(--purple)', 'Navigation': 'var(--blue)' }[entry.Source] || 'var(--cyan)';
        return `<div class="log-entry"><span class="log-time">${formatTime(entry.Timestamp)}</span><span class="log-source" style="color:${sourceColor};">${entry.Source}</span> ${entry.Message}</div>`;
    }).join('');
}

function approveOverlay(overlayId) { client.sendCommand('ApproveOverlay', { overlay_id: overlayId }); }
function dismissOverlay(overlayId) { client.sendCommand('DismissOverlay', { overlay_id: overlayId }); }
function resolveDecision(decisionId, optionId) { client.sendCommand('ResolveDecision', { decision_id: decisionId, option_id: optionId }); }
function requestStatus(target) { client.sendCommand('RequestStatus', { target }); client.showToast(`Status angefordert: ${target}`, 'info'); }

function setEngagementRule(rule) { client.sendCommand('SetEngagementRule', { rule }); }
function updateEngagementButtons(current) {
    document.querySelectorAll('#engagement-buttons .btn').forEach(btn => {
        const r = btn.dataset.rule;
        if (r === current) { btn.style.borderColor = 'var(--cyan)'; btn.style.background = 'rgba(0, 200, 230, 0.15)'; }
        else { btn.style.borderColor = ''; btn.style.background = ''; }
    });
}

function setCourseToContact(mode) {
    if (!selectedNavContactId) return;
    client.sendCommand('SetCourseToContact', { contact_id: selectedNavContactId, mode });
    client.showToast(mode === 'flee' ? 'Fluchtvektor gesetzt' : 'Angriffsvektor gesetzt', 'info');
}

client.connect();
