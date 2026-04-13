/**
 * SpacedOut - WebSocket Client Library
 * Handles connection, reconnection, and message routing.
 */
class SpacedOutClient {
    constructor() {
        this.ws = null;
        this.clientId = null;
        this.role = null;
        this.connected = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 20;
        this.handlers = {};
        this.stateHandler = null;
        this.toastContainer = null;
        this._lastState = null;
    }

    connect() {
        const host = window.location.hostname || 'localhost';
        const port = window.location.port || '8080';
        const url = `ws://${host}:${port}/ws`;

        console.log(`[SpacedOut] Connecting to ${url}...`);

        try {
            this.ws = new WebSocket(url);
        } catch (e) {
            console.error('[SpacedOut] WebSocket creation failed:', e);
            this.scheduleReconnect();
            return;
        }

        this.ws.onopen = () => {
            console.log('[SpacedOut] Connected');
            this.connected = true;
            this.reconnectAttempts = 0;
            this.emit('connected');
            this.updateConnectionStatus(true);
        };

        this.ws.onclose = () => {
            console.log('[SpacedOut] Disconnected');
            this.connected = false;
            this.emit('disconnected');
            this.updateConnectionStatus(false);
            this.scheduleReconnect();
        };

        this.ws.onerror = (err) => {
            console.error('[SpacedOut] WebSocket error:', err);
        };

        this.ws.onmessage = (event) => {
            try {
                const msg = JSON.parse(event.data);
                this.handleMessage(msg);
            } catch (e) {
                console.error('[SpacedOut] Parse error:', e);
            }
        };
    }

    scheduleReconnect() {
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            console.log('[SpacedOut] Max reconnect attempts reached');
            return;
        }
        const delay = Math.min(1000 * Math.pow(1.5, this.reconnectAttempts), 10000);
        this.reconnectAttempts++;
        console.log(`[SpacedOut] Reconnecting in ${Math.round(delay)}ms (attempt ${this.reconnectAttempts})`);
        setTimeout(() => this.connect(), delay);
    }

    handleMessage(msg) {
        switch (msg.type) {
            case 'welcome':
                this.clientId = msg.client_id;
                this.emit('welcome', msg);
                break;
            case 'role_assigned':
                this.role = msg.role;
                this.emit('role_assigned', msg);
                break;
            case 'role_status':
                this.emit('role_status', msg);
                break;
            case 'state_update':
                this._lastState = msg;
                if (this.stateHandler) this.stateHandler(msg);
                this.emit('state_update', msg);
                break;
            case 'event':
                this.showToast(`⚠ ${msg.title}`, 'warning');
                this.emit('event', msg);
                break;
            case 'phase_changed':
                this.showToast(`Phase: ${msg.phase}`, 'info');
                this.emit('phase_changed', msg);
                break;
            case 'mission_started':
                this.emit('mission_started', msg);
                break;
            case 'mission_ended':
                this.emit('mission_ended', msg);
                break;
            case 'sector_map_update':
                this.emit('sector_map_update', msg);
                break;
            case 'run_state_update':
                this.emit('run_state_update', msg);
                break;
            case 'campaign_node_completed':
                this.showToast(`Knoten abgeschlossen`, 'info');
                this.emit('campaign_node_completed', msg);
                break;
            case 'campaign_sector_completed':
                this.showToast(`Sektor abgeschlossen!`, 'info');
                this.emit('campaign_sector_completed', msg);
                break;
            case 'campaign_ended':
                this.emit('campaign_ended', msg);
                break;
            case 'paused':
                this.emit('paused', msg);
                break;
            case 'resumed':
                this.emit('resumed', msg);
                break;
            case 'error':
                this.showToast(msg.message, 'danger');
                this.emit('error', msg);
                break;
            default:
                this.emit(msg.type, msg);
        }
    }

    send(data) {
        if (this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.ws.send(JSON.stringify(data));
        }
    }

    selectRole(role) {
        this.send({ type: 'select_role', role });
    }

    sendCommand(command, data = {}) {
        this.send({ type: 'command', command, data });
    }

    on(event, handler) {
        if (!this.handlers[event]) this.handlers[event] = [];
        this.handlers[event].push(handler);
    }

    emit(event, data) {
        const handlers = this.handlers[event] || [];
        handlers.forEach(h => {
            try { h(data); } catch(e) { console.error(`Handler error (${event}):`, e); }
        });
    }

    onState(handler) {
        this.stateHandler = handler;
    }

    getLastState() {
        return this._lastState;
    }

    // Toast notifications
    showToast(message, type = 'info') {
        if (!this.toastContainer) {
            this.toastContainer = document.createElement('div');
            this.toastContainer.className = 'toast-container';
            document.body.appendChild(this.toastContainer);
        }

        const toast = document.createElement('div');
        toast.className = `toast`;
        toast.style.borderColor = type === 'danger' ? 'var(--red)' :
                                  type === 'warning' ? 'var(--yellow)' : 'var(--cyan)';
        toast.textContent = message;
        this.toastContainer.appendChild(toast);

        setTimeout(() => {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, 3500);
    }

    updateConnectionStatus(connected) {
        const el = document.getElementById('connection-status');
        if (el) {
            el.textContent = connected ? 'VERBUNDEN' : 'GETRENNT';
            el.style.color = connected ? 'var(--green)' : 'var(--red)';
        }
    }
}

// Shared utility functions
function formatTime(seconds) {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

/** Main screen shows mission title in sandbox mode; structured missions show phase name. */
function updatePhaseHeaderFromState(msg) {
    const el = document.getElementById('phase-display');
    if (!el) return;
    if (msg.mission_phase === 'Ended') {
        el.textContent = 'BEENDET';
        return;
    }
    const structured = msg.use_structured_phases === true;
    el.textContent = structured ? (msg.mission_phase || '---') : (msg.mission_title || 'EINSATZ');
}

function getStatusClass(status) {
    switch (status) {
        case 'Operational': return 'status-operational';
        case 'Degraded': return 'status-degraded';
        case 'Damaged': return 'status-damaged';
        case 'Offline': return 'status-offline';
        default: return '';
    }
}

function getStatusLabel(status) {
    switch (status) {
        case 'Operational': return 'OK';
        case 'Degraded': return 'EINGESCHRÄNKT';
        case 'Damaged': return 'BESCHÄDIGT';
        case 'Offline': return 'OFFLINE';
        default: return status;
    }
}

function getContactDotClass(type) {
    switch (type) {
        case 'Friendly': return 'dot-friendly';
        case 'Hostile': return 'dot-hostile';
        case 'Unknown': return 'dot-unknown';
        case 'Anomaly': return 'dot-anomaly';
        default: return 'dot-neutral';
    }
}

// Global client instance
const client = new SpacedOutClient();
