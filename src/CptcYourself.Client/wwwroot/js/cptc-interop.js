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

    // Encrypt apiKey with password and store result in IndexedDB.
    // Returns { encryptedApiKey, salt, iv } (all hex strings).
    async saveApiKey(apiKey, password) {
        const salt    = crypto.getRandomValues(new Uint8Array(16));
        const iv      = crypto.getRandomValues(new Uint8Array(12));
        const key     = await deriveKey(password, salt);
        const enc     = new TextEncoder();
        const cipher  = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, enc.encode(apiKey));
        const record  = {
            encryptedApiKey: bytesToHex(new Uint8Array(cipher)),
            salt:            bytesToHex(salt),
            iv:              bytesToHex(iv)
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

    // Remove stored settings (reset / forget API key).
    async clearSettings() {
        await idbDelete();
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
