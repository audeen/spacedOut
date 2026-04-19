/**
 * SpacedOut - WebSocket Client Library
 * Handles connection, reconnection, and message routing.
 */

const TOAST_MAX_TOP = 4;
const TOAST_MAX_BOTTOM = 4;

/** Upper-lane semantic color key (CSS .toast--cat-*). */
function missionLogCategoryFromSource(src) {
    switch (src) {
        case 'CaptainNav':
        case 'Captain':
            return 'comms';
        case 'Navigation':
        case 'Navigator':
            return 'nav';
        case 'Tactical':
            return 'tactical';
        case 'Engineer':
            return 'engineer';
        case 'Gunner':
            return 'combat';
        case 'System':
        case 'Event':
        default:
            return 'system';
    }
}

function missionLogToastSeverity(src, _wt) {
    if (src === 'System') return 'warning';
    return 'info';
}

/** Eingehender Waffenschaden auf das Spielerschiff (MissionController UpdateEnemyAttacks); alle Stationen. */
function isIncomingHullDamageLine(src, rawMsg) {
    if (src !== 'System') return false;
    return /\bTreffer von\b/i.test(rawMsg) && /Hüllenschaden/i.test(rawMsg);
}

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
        this.bottomToastContainer = null;
        this._lastState = null;
        this.decisionToastContainer = null;
        /** @type {Record<string, HTMLElement>} */
        this._decisionToastNodes = {};
        /** CaptainNav: dedupe event toast vs mission_log toast (same title within a few seconds). */
        this._lastCaptainMissionLogToastAt = 0;
        this._lastCaptainMissionLogToastText = '';
    }

    _hasCaptainPendingDecisionToast() {
        return this.role === 'CaptainNav' && this._decisionToastNodes
            && Object.keys(this._decisionToastNodes).length > 0;
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
                this._pruneDecisionToasts(msg);
                break;
            case 'event': {
                let skipToast = false;
                const eventId = msg.event_id || msg.eventId || '';
                if (this.role === 'CaptainNav') {
                    if (typeof eventId === 'string' && eventId.startsWith('event_banner:')) skipToast = true;
                    else if (this._hasCaptainPendingDecisionToast()) skipToast = true;
                    else {
                        const title = (msg.title || '').trim();
                        const now = Date.now();
                        const recent = now - this._lastCaptainMissionLogToastAt < 3500;
                        const prev = this._lastCaptainMissionLogToastText || '';
                        if (title && recent && prev.includes(title)) skipToast = true;
                    }
                }
                if (!skipToast) {
                    this.showToast(`⚠ ${msg.title}`, { type: 'warning', category: 'system', durationMs: 5200 });
                }
                this.emit('event', msg);
                break;
            }
            case 'phase_changed':
                this.showToast(`Phase: ${msg.phase}`, { type: 'info', category: 'system', durationMs: 5500 });
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
                this.showToast('Knoten abgeschlossen', { type: 'info', category: 'system', durationMs: 4200 });
                this.emit('campaign_node_completed', msg);
                break;
            case 'campaign_sector_completed':
                this.showToast('Sektor abgeschlossen!', { type: 'info', category: 'system', durationMs: 5200 });
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
                this.showToast(msg.message, { type: 'danger', durationMs: 5500 });
                this.emit('error', msg);
                break;
            case 'mission_log_line': {
                const wt = msg.web_toast || 'Unspecified';
                if (wt === 'LogOnly') {
                    this.emit('mission_log_line', msg);
                    break;
                }
                const src = msg.source || '';
                const rawMsg = String(msg.message ?? msg.Message ?? '').trim();
                if (!rawMsg) {
                    this.emit('mission_log_line', msg);
                    break;
                }
                if (wt === 'Unspecified' && src === 'Gunner' && this.role === 'Gunner') {
                    this.emit('mission_log_line', msg);
                    break;
                }
                if (this.role === 'CaptainNav' && src === 'System' && /^Funkspruch:/i.test(rawMsg)) {
                    this.emit('mission_log_line', msg);
                    break;
                }
                const text = src && src !== 'System' ? `[${src}] ${rawMsg}` : rawMsg;
                const durationMs = wt === 'ToastProminent' ? 9200 : wt === 'Toast' ? 4200 : 4000;
                const incomingHullDamageToast = isIncomingHullDamageLine(src, rawMsg);
                let useBottomLane = src === 'Gunner' && this.role !== 'Gunner';
                if (incomingHullDamageToast) useBottomLane = true;
                const category = missionLogCategoryFromSource(src);
                let toastType = missionLogToastSeverity(src, wt);
                if (incomingHullDamageToast) toastType = 'danger';
                const opts = {
                    type: toastType,
                    durationMs,
                    category: toastType === 'danger' ? null : category,
                    multiline: text.length > 110,
                };
                if (useBottomLane) {
                    opts.lane = 'bottom';
                    if (toastType !== 'danger') opts.category = 'combat';
                }
                this.showToast(text, opts);
                if (this.role === 'CaptainNav') {
                    this._lastCaptainMissionLogToastAt = Date.now();
                    this._lastCaptainMissionLogToastText = text;
                }
                this.emit('mission_log_line', msg);
                break;
            }
            case 'pending_decision': {
                const d = msg.decision || msg.Decision;
                if (d) this.showDecisionToast(d);
                this.emit('pending_decision', msg);
                break;
            }
            case 'decision_resolved': {
                const style = msg.resolution_style;
                const cinematic = style !== 'toast';
                if (cinematic) {
                    this.showDecisionResolutionOverlay(msg.narrative || '', msg.effects_line || '');
                } else {
                    const n = (msg.narrative || '').trim();
                    const fx = (msg.effects_line || '').trim();
                    const title = (msg.decision_title || '').trim();
                    const parts = [];
                    if (title) parts.push(title);
                    if (n) parts.push(n);
                    if (fx) parts.push(fx);
                    const combined = parts.join('\n\n');
                    if (combined) {
                        const ms = Math.min(14000, Math.max(8800, 900 + combined.length * 28));
                        this.showToast(combined, {
                            type: 'info',
                            category: 'comms',
                            durationMs: ms,
                            multiline: true,
                        });
                    }
                }
                this.emit('decision_resolved', msg);
                break;
            }
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

    /**
     * Floating toasts. Call patterns: `showToast(msg, 'info')`, `showToast(msg, { type, lane, durationMs, category, multiline })`.
     * @param {string} message
     * @param {string|object} typeOrOpts
     * @param {object} [maybeOpts]
     */
    showToast(message, typeOrOpts = 'info', maybeOpts) {
        let type = 'info';
        let opts = {};
        if (typeof typeOrOpts === 'object' && typeOrOpts !== null && !Array.isArray(typeOrOpts)) {
            opts = { ...typeOrOpts };
            type = opts.type || 'info';
        } else {
            type = typeOrOpts;
            if (typeof maybeOpts === 'object' && maybeOpts !== null) opts = { ...maybeOpts };
        }

        const lane = opts.lane || 'top';
        const multiline = !!opts.multiline;
        let durationMs = opts.durationMs;
        if (durationMs == null) {
            if (opts.tier === 'short') durationMs = 2000;
            else if (opts.tier === 'long') durationMs = 9000;
            else durationMs = 4000;
        }

        const category = opts.category || null;
        const maxStack = opts.maxStack ?? (lane === 'bottom' ? TOAST_MAX_BOTTOM : TOAST_MAX_TOP);

        if (lane === 'bottom') {
            this._appendBottomToast(message, { type, durationMs, category, multiline, maxStack });
            return;
        }

        if (!this.toastContainer) {
            this.toastContainer = document.createElement('div');
            this.toastContainer.className = 'toast-container';
            document.body.appendChild(this.toastContainer);
        }
        while (this.toastContainer.children.length >= maxStack) {
            this.toastContainer.removeChild(this.toastContainer.firstChild);
        }

        const toast = document.createElement('div');
        toast.className = 'toast toast--lane-top';
        if (multiline) toast.classList.add('toast--text-long');
        if (type === 'danger') toast.classList.add('toast--type-danger');
        else if (type === 'warning') toast.classList.add('toast--type-warning');
        else toast.classList.add('toast--type-info');
        if (category && type !== 'danger') toast.classList.add(`toast--cat-${category}`);

        toast.textContent = message;
        this.toastContainer.appendChild(toast);

        setTimeout(() => {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, durationMs);
    }

    _appendBottomToast(message, { type, durationMs, category, multiline, maxStack }) {
        if (!this.bottomToastContainer) {
            this.bottomToastContainer = document.createElement('div');
            this.bottomToastContainer.className = 'toast-container toast-container--bottom';
            document.body.appendChild(this.bottomToastContainer);
        }
        while (this.bottomToastContainer.children.length >= maxStack) {
            this.bottomToastContainer.removeChild(this.bottomToastContainer.firstChild);
        }

        const toast = document.createElement('div');
        toast.className = 'toast toast--from-bottom toast--mission-feed';
        if (multiline) toast.classList.add('toast--text-long');
        if (type === 'danger') toast.classList.add('toast--type-danger');
        else if (type === 'warning') toast.classList.add('toast--type-warning');
        else toast.classList.add('toast--type-info');
        if (category && type !== 'danger') toast.classList.add(`toast--cat-${category}`);

        toast.textContent = message;
        this.bottomToastContainer.appendChild(toast);

        const fadeAt = Math.max(400, durationMs - 320);
        setTimeout(() => {
            toast.classList.add('toast--fade-out');
        }, fadeAt);
        setTimeout(() => {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, durationMs);
    }

    /**
     * Eigenes Vollbild (#decision-resolution-overlay), gleiche Optik wie Missionsbriefing.
     * Kein Toast — Seitenlogik setzt Button „Zum Briefing“ / „Verstanden“ via syncDecisionResolutionCta.
     */
    showDecisionResolutionOverlay(narrative, effectsLine) {
        const overlay = document.getElementById('decision-resolution-overlay');
        const textEl = document.getElementById('decision-resolution-text');
        if (!overlay || !textEl) {
            console.warn('[SpacedOut] decision-resolution-overlay fehlt — Auflösung nicht angezeigt.');
            return;
        }

        const titleEl = document.getElementById('decision-resolution-title');
        const effectsEl = document.getElementById('decision-resolution-effects');
        if (titleEl) {
            const def = titleEl.getAttribute('data-default-title') || 'MISSIONSBRIEFING';
            titleEl.textContent = def;
        }
        textEl.textContent = narrative || '—';

        if (effectsEl) {
            const fx = (effectsLine || '').trim();
            if (fx) {
                effectsEl.textContent = fx;
                effectsEl.classList.remove('hidden');
            } else {
                effectsEl.textContent = '';
                effectsEl.classList.add('hidden');
            }
        }

        overlay.classList.remove('hidden');
    }

    resetDecisionResolutionOverlayChrome() {
        const titleEl = document.getElementById('decision-resolution-title');
        const textEl = document.getElementById('decision-resolution-text');
        const effectsEl = document.getElementById('decision-resolution-effects');
        if (titleEl) {
            const def = titleEl.getAttribute('data-default-title');
            if (def) titleEl.textContent = def;
        }
        if (textEl) textEl.textContent = '';
        if (effectsEl) {
            effectsEl.textContent = '';
            effectsEl.classList.add('hidden');
        }
    }

    /** Stellt Titel/Ergebniszeile nach Schließen des Briefing-Overlays wieder her (für nächstes Briefing). */
    resetBriefingOverlayChrome() {
        const titleEl = document.getElementById('briefing-title');
        const textEl = document.getElementById('briefing-text');
        const effectsEl = document.getElementById('briefing-effects');
        if (titleEl) {
            const def = titleEl.getAttribute('data-default-title');
            if (def) titleEl.textContent = def;
        }
        if (textEl) textEl.textContent = '';
        if (effectsEl) {
            effectsEl.textContent = '';
            effectsEl.classList.add('hidden');
        }
    }

    /**
     * Gunner shot result: slides up from bottom (snackbar-style). kind: miss | hit | kill
     */
    showShotFeedback(message, kind = 'hit') {
        if (!this.bottomToastContainer) {
            this.bottomToastContainer = document.createElement('div');
            this.bottomToastContainer.className = 'toast-container toast-container--bottom';
            document.body.appendChild(this.bottomToastContainer);
        }
        while (this.bottomToastContainer.children.length >= TOAST_MAX_BOTTOM) {
            this.bottomToastContainer.removeChild(this.bottomToastContainer.firstChild);
        }

        const toast = document.createElement('div');
        toast.className = 'toast toast--from-bottom toast--shot';
        if (kind === 'miss') {
            toast.classList.add('toast--shot-miss');
        } else if (kind === 'kill') {
            toast.classList.add('toast--shot-kill');
        } else {
            toast.classList.add('toast--shot-hit');
        }
        toast.textContent = message;
        this.bottomToastContainer.appendChild(toast);

        const ms = 2600;
        setTimeout(() => {
            toast.classList.add('toast--fade-out');
        }, ms - 320);
        setTimeout(() => {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
        }, ms);
    }

    /**
     * Kommandant: interaktiver Toast mit denselben Optionen wie das Entscheidungs-Panel.
     * @param {object} decision — { id, title, description, options: [{ id, label, description }] }
     */
    showDecisionToast(decision) {
        const id = decision.id;
        if (!id) return;

        if (!this.decisionToastContainer) {
            this.decisionToastContainer = document.createElement('div');
            this.decisionToastContainer.className = 'toast-container toast-container--decision';
            document.body.appendChild(this.decisionToastContainer);
        }

        if (this._decisionToastNodes[id]) {
            this.removeDecisionToast(id);
        }

        const wrap = document.createElement('div');
        wrap.className = 'toast-decision';
        wrap.dataset.decisionId = id;

        const outer = document.createElement('div');
        outer.className = 'toast-decision__outer';

        const header = document.createElement('div');
        header.className = 'toast-decision__header';

        const icon = document.createElement('span');
        icon.className = 'toast-decision__warn-icon';
        icon.textContent = '⚠';
        const hlabel = document.createElement('span');
        hlabel.className = 'toast-decision__header-title';
        hlabel.textContent = 'AUSSTEHENDE ENTSCHEIDUNGEN';

        const closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'toast-decision__close';
        closeBtn.setAttribute('aria-label', 'Schließen');
        closeBtn.textContent = '×';
        closeBtn.addEventListener('click', () => this.removeDecisionToast(id));

        header.appendChild(icon);
        header.appendChild(hlabel);
        header.appendChild(closeBtn);

        const inner = document.createElement('div');
        inner.className = 'toast-decision__inner';

        const titleEl = document.createElement('div');
        titleEl.className = 'toast-decision__title';
        titleEl.textContent = decision.title || '';

        const descEl = document.createElement('div');
        descEl.className = 'toast-decision__desc';
        descEl.textContent = decision.description || '';

        inner.appendChild(titleEl);
        inner.appendChild(descEl);

        const opts = decision.options || decision.Options || [];
        opts.forEach((opt) => {
            const oid = opt.id || opt.Id;
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'btn btn-primary toast-decision__option';
            const strong = document.createElement('strong');
            strong.textContent = opt.label || opt.Label || '';
            const sub = document.createElement('span');
            sub.className = 'text-dim toast-decision__option-sub';
            sub.textContent = opt.description || opt.Description || '';
            btn.appendChild(strong);
            btn.appendChild(sub);
            btn.addEventListener('click', () => {
                this.sendCommand('ResolveDecision', { decision_id: id, option_id: oid });
                this.removeDecisionToast(id);
            });
            inner.appendChild(btn);
        });

        outer.appendChild(header);
        outer.appendChild(inner);
        wrap.appendChild(outer);
        this.decisionToastContainer.appendChild(wrap);
        this._decisionToastNodes[id] = wrap;
    }

    removeDecisionToast(decisionId) {
        const n = this._decisionToastNodes[decisionId];
        if (n && n.parentNode) n.parentNode.removeChild(n);
        delete this._decisionToastNodes[decisionId];
    }

    _pruneDecisionToasts(msg) {
        if (this.role !== 'CaptainNav' || !msg.data || !Array.isArray(msg.data.pending_decisions)) return;
        const pending = new Set(
            msg.data.pending_decisions.map((d) => d.Id || d.id).filter(Boolean)
        );
        Object.keys(this._decisionToastNodes).forEach((id) => {
            if (!pending.has(id)) this.removeDecisionToast(id);
        });
    }

    updateConnectionStatus(connected) {
        const el = document.getElementById('connection-status');
        if (el) {
            el.textContent = connected ? 'VERBUNDEN' : 'GETRENNT';
            el.style.color = connected ? 'var(--green)' : 'var(--red)';
        }
    }
}

/**
 * M4 Hybrid-Event-Indikator — Rolle sieht einen dezenten Banner, wenn am Sektor
 * ein Pre-Sector-Event aktiv ist oder generell eine Captain-Entscheidung aussteht.
 * Erwartet ein Element mit id "event-indicator" im Rollen-HTML. No-op, wenn nicht vorhanden.
 */
function updateEventIndicator(d) {
    const el = document.getElementById('event-indicator');
    if (!el) return;

    const preActive = d.mission_pre_sector_event_active === true;
    const decisionPending = d.mission_pending_decision === true;
    const title = d.mission_pre_sector_event_title || '';

    if (preActive) {
        el.textContent = title
            ? `Funkspruch: ${title} — Captain entscheidet.`
            : 'Funkspruch eingegangen — Captain entscheidet.';
        el.className = 'event-indicator event-indicator-presector';
        el.style.display = '';
        return;
    }
    if (decisionPending) {
        el.textContent = 'Captain-Entscheidung läuft.';
        el.className = 'event-indicator event-indicator-decision';
        el.style.display = '';
        return;
    }
    el.style.display = 'none';
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
