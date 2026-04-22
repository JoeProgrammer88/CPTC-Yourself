// cptc-interop.js
// Handles: AES-GCM key derivation (PBKDF2), encrypt/decrypt, IndexedDB storage, camera

const DB_NAME    = 'cptc-db';
const DB_VERSION = 1;
const STORE_NAME = 'settings';
const SETTINGS_KEY = 'app-settings';

// ── IndexedDB helpers ──────────────────────────────────────────────────────────

function openDb() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = e => {
            const db = e.target.result;
            if (!db.objectStoreNames.contains(STORE_NAME))
                db.createObjectStore(STORE_NAME);
        };
        req.onsuccess = e => resolve(e.target.result);
        req.onerror   = e => reject(e.target.error);
    });
}

async function idbPut(value) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx  = db.transaction(STORE_NAME, 'readwrite');
        const req = tx.objectStore(STORE_NAME).put(value, SETTINGS_KEY);
        req.onsuccess = () => resolve();
        req.onerror   = e  => reject(e.target.error);
    });
}

async function idbGet() {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx  = db.transaction(STORE_NAME, 'readonly');
        const req = tx.objectStore(STORE_NAME).get(SETTINGS_KEY);
        req.onsuccess = e => resolve(e.target.result ?? null);
        req.onerror   = e => reject(e.target.error);
    });
}

async function idbDelete() {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx  = db.transaction(STORE_NAME, 'readwrite');
        const req = tx.objectStore(STORE_NAME).delete(SETTINGS_KEY);
        req.onsuccess = () => resolve();
        req.onerror   = e  => reject(e.target.error);
    });
}

// ── Crypto helpers ─────────────────────────────────────────────────────────────

function hexToBytes(hex) {
    const bytes = new Uint8Array(hex.length / 2);
    for (let i = 0; i < bytes.length; i++)
        bytes[i] = parseInt(hex.substr(i * 2, 2), 16);
    return bytes;
}

function bytesToHex(bytes) {
    return Array.from(bytes).map(b => b.toString(16).padStart(2, '0')).join('');
}

async function deriveKey(password, saltBytes) {
    const enc      = new TextEncoder();
    const keyMat   = await crypto.subtle.importKey('raw', enc.encode(password),
                         { name: 'PBKDF2' }, false, ['deriveKey']);
    return crypto.subtle.deriveKey(
        { name: 'PBKDF2', salt: saltBytes, iterations: 310_000, hash: 'SHA-256' },
        keyMat,
        { name: 'AES-GCM', length: 256 },
        false,
        ['encrypt', 'decrypt']
    );
}

// ── Public API (called from Blazor via IJSRuntime) ────────────────────────────

window.cptcInterop = {

    // Encrypt secret with password and store result in IndexedDB.
    // For Vertex AI, project_id is extracted automatically from the service account JSON.
    // Returns { encryptedApiKey, salt, iv, provider, vertexProjectId, vertexLocation }.
    async saveApiKey(secret, password, provider) {
        const salt    = crypto.getRandomValues(new Uint8Array(16));
        const iv      = crypto.getRandomValues(new Uint8Array(12));
        const key     = await deriveKey(password, salt);
        const enc     = new TextEncoder();
        const cipher  = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, enc.encode(secret));

        // Auto-extract project_id from service account JSON when using Vertex AI
        let vertexProjectId = '';
        if (provider === 'VertexAi') {
            try {
                const sa = JSON.parse(secret);
                vertexProjectId = sa.project_id ?? '';
            } catch { /* invalid JSON — will surface as an error at runtime */ }
        }

        const record  = {
            encryptedApiKey:  bytesToHex(new Uint8Array(cipher)),
            salt:             bytesToHex(salt),
            iv:               bytesToHex(iv),
            provider:         provider ?? 'AiStudio',
            vertexProjectId:  vertexProjectId,
            vertexLocation:   'us-central1'
        };
        await idbPut(record);
        return record;
    },

    // Load stored record from IndexedDB. Returns null if none exists.
    async loadSettings() {
        return await idbGet();
    },

    // Decrypt stored API key using the supplied password.
    // Returns the plain-text API key string, or throws on wrong password.
    async decryptApiKey(encryptedHex, saltHex, ivHex, password) {
        const salt    = hexToBytes(saltHex);
        const iv      = hexToBytes(ivHex);
        const cipher  = hexToBytes(encryptedHex);
        const key     = await deriveKey(password, salt);
        const plain   = await crypto.subtle.decrypt({ name: 'AES-GCM', iv }, key, cipher);
        return new TextDecoder().decode(plain);
    },

    // Remove stored settings (reset / forget credentials).
    async clearSettings() {
        await idbDelete();
    },

    // ── Audio helpers ──────────────────────────────────────────────────────────

    // Wraps raw signed-16-bit PCM audio (base64-encoded) in a WAV container and
    // returns a Blob Object URL suitable for use in an <audio> element.
    // Caller is responsible for revoking the URL via revokeBlobUrl() when done.
    pcmToWavBlobUrl(pcmBase64, sampleRate, numChannels, bitsPerSample) {
        const pcm        = Uint8Array.from(atob(pcmBase64), c => c.charCodeAt(0));
        const headerSize = 44;
        const buffer     = new ArrayBuffer(headerSize + pcm.length);
        const view       = new DataView(buffer);

        const write4 = (offset, str) => {
            for (let i = 0; i < 4; i++) view.setUint8(offset + i, str.charCodeAt(i));
        };

        write4(0,  'RIFF');
        view.setUint32(4,  36 + pcm.length,                              true);
        write4(8,  'WAVE');
        write4(12, 'fmt ');
        view.setUint32(16, 16,                                           true); // chunk size (PCM)
        view.setUint16(20, 1,                                            true); // format (PCM)
        view.setUint16(22, numChannels,                                  true);
        view.setUint32(24, sampleRate,                                   true);
        view.setUint32(28, sampleRate * numChannels * bitsPerSample / 8, true); // byte rate
        view.setUint16(32, numChannels * bitsPerSample / 8,              true); // block align
        view.setUint16(34, bitsPerSample,                                true);
        write4(36, 'data');
        view.setUint32(40, pcm.length,                                   true);
        new Uint8Array(buffer, headerSize).set(pcm);

        const blob = new Blob([buffer], { type: 'audio/wav' });
        return URL.createObjectURL(blob);
    },

    // Revoke a Blob Object URL created by pcmToWavBlobUrl to free memory.
    revokeBlobUrl(url) {
        if (url) URL.revokeObjectURL(url);
    },

    // ── Vertex AI access token ─────────────────────────────────────────────────

    // Accepts a service account JSON string, returns a short-lived access token.
    // Caches the token until 1 minute before expiry.
    async getVertexAccessToken(serviceAccountJson) {
        if (window._vertexTokenCache) {
            const { token, expiry } = window._vertexTokenCache;
            if (Date.now() < expiry - 60_000) return token;
        }

        const sa  = JSON.parse(serviceAccountJson);
        const now = Math.floor(Date.now() / 1000);

        // Build JWT header + payload (base64url encoded)
        const b64url = obj =>
            btoa(JSON.stringify(obj))
                .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

        const header  = { alg: 'RS256', typ: 'JWT' };
        const payload = {
            iss:   sa.client_email,
            scope: 'https://www.googleapis.com/auth/cloud-platform',
            aud:   'https://oauth2.googleapis.com/token',
            exp:   now + 3600,
            iat:   now
        };
        const toSign = `${b64url(header)}.${b64url(payload)}`;

        // Import private key (PEM → DER → CryptoKey)
        const pemBody    = sa.private_key.replace(/-----[^-]+-----/g, '').replace(/\s/g, '');
        const der        = Uint8Array.from(atob(pemBody), c => c.charCodeAt(0));
        const privateKey = await crypto.subtle.importKey(
            'pkcs8', der,
            { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' },
            false, ['sign']
        );

        // Sign and build JWT
        const sig    = await crypto.subtle.sign('RSASSA-PKCS1-v1_5', privateKey, new TextEncoder().encode(toSign));
        const sigB64 = btoa(String.fromCharCode(...new Uint8Array(sig)))
                           .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
        const jwt = `${toSign}.${sigB64}`;

        // Exchange JWT for access token
        const resp = await fetch('https://oauth2.googleapis.com/token', {
            method:  'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body:    `grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer&assertion=${jwt}`
        });
        if (!resp.ok) throw new Error(`Vertex token exchange failed: ${await resp.text()}`);

        const data = await resp.json();
        window._vertexTokenCache = {
            token:  data.access_token,
            expiry: (now + data.expires_in) * 1000
        };
        return data.access_token;
    },

    // ── Camera helpers ─────────────────────────────────────────────────────────

    async startCamera(videoElementId) {
        const video = document.getElementById(videoElementId);
        if (!video) throw new Error(`Video element #${videoElementId} not found`);
        const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false });
        video.srcObject = stream;
        await video.play();
    },

    stopCamera(videoElementId) {
        const video = document.getElementById(videoElementId);
        if (video && video.srcObject) {
            video.srcObject.getTracks().forEach(t => t.stop());
            video.srcObject = null;
        }
    },

    // Captures a frame from the video element, returns base64 JPEG (no prefix).
    capturePhoto(videoElementId, quality = 0.92) {
        const video  = document.getElementById(videoElementId);
        if (!video)  throw new Error(`Video element #${videoElementId} not found`);
        const canvas = document.createElement('canvas');
        canvas.width  = video.videoWidth;
        canvas.height = video.videoHeight;
        canvas.getContext('2d').drawImage(video, 0, 0);
        const dataUrl = canvas.toDataURL('image/jpeg', quality);
        // Strip the data:image/jpeg;base64, prefix
        return dataUrl.split(',')[1];
    }
};
