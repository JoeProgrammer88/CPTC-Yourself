# CPTC Yourself

**CPTC Yourself** is a browser-based Blazor WebAssembly application that lets students in the Center for Advanced Manufacturing Technologies (CAMT) building at Clover Park Technical College (CPTC) take a selfie and see themselves thriving in a career that matches their chosen program of study. Using Google's Gemini and Imagen 3 AI models, the app generates a career-inspiration image styled to the user's preferred art style and genre.

All API credentials are encrypted with AES-256-GCM using a password the user chooses and stored only in the browser's IndexedDB — nothing is ever sent to a server.

---

## Features

- Selfie capture via the device camera
- Program of study selector (9 CPTC programs)
- Art style and genre selector
- AI-generated career visualization powered by Gemini + Imagen 3
- Choice of **Google AI Studio** or **Vertex AI** as the API backend
- Credentials encrypted client-side and persisted in IndexedDB

---

## Requirements

| Requirement | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 or later |
| A modern browser (Chrome, Edge, Firefox, Safari) | — |
| A Google AI Studio API key **or** a GCP project with Vertex AI enabled | — |

---

## Running Locally

1. **Clone the repository**

   ```bash
   git clone https://github.com/JoeProgrammer88/CPTC-Yourself.git
   cd CPTC-Yourself
   ```

2. **Run the app**

   ```bash
   cd src/CptcYourself.Client
   dotnet watch
   ```

   The app will open automatically at `https://localhost:7173` (or `http://localhost:5045`).

3. **First-time setup** — navigate to **Settings** (⚙️) and configure your API credentials (see below). Then return to the home page, enter your encryption password when prompted, select a program, art style, and genre, and take a photo.

---

## Configuring Google AI Studio

Google AI Studio is the simplest option and requires only a free API key. Note that **Imagen 3 image generation requires billing to be enabled** on your Google account even when using AI Studio.

1. Go to [Google AI Studio](https://aistudio.google.com) and sign in.
2. Click **Get API key** → **Create API key**.
3. Copy the key (it starts with `AIza`).
4. In the app, open **Settings** (⚙️).
5. Select **Google AI Studio** as the provider.
6. Paste the API key into the **Google AI Studio API Key** field.
7. Choose an encryption password (minimum 8 characters), confirm it, and click **Save & Encrypt**.

> **Enabling billing for Imagen 3**
> 1. Go to the [Google Cloud Console](https://console.cloud.google.com).
> 2. Select (or create) the project linked to your AI Studio API key.
> 3. Navigate to **Billing** and attach a billing account.
> 4. The same API key will then work for Imagen 3 image generation.

---

## Configuring Vertex AI

Vertex AI uses your GCP project directly via a service account and supports both Gemini and Imagen 3 without a separate AI Studio key.

### 1. Enable the required APIs

In the [Google Cloud Console](https://console.cloud.google.com) for your project:

- **Vertex AI API** (`aiplatform.googleapis.com`)
- **Cloud Resource Manager API** (needed for token exchange)

Navigate to **APIs & Services → Library** and enable both.

### 2. Create a service account

1. Go to **IAM & Admin → Service Accounts**.
2. Click **Create Service Account**, give it a name (e.g. `cptc-yourself`), and click **Create and Continue**.
3. Assign the role **Vertex AI User** (`roles/aiplatform.user`).
4. Click **Done**.

### 3. Download a service account key

1. Click the service account you just created.
2. Go to the **Keys** tab → **Add Key → Create new key**.
3. Choose **JSON** and click **Create**. A `.json` file downloads automatically.
4. Open the file and copy its entire contents.

### 4. Configure the app

1. In the app, open **Settings** (⚙️).
2. Select **Vertex AI** as the provider.
3. Enter your **GCP Project ID** (found on the Cloud Console home page).
4. Enter your **Location** (e.g. `us-central1`). Imagen 3 is available in `us-central1` and `europe-west4`.
5. Paste the full contents of the service account JSON file into the **Service Account Key JSON** field.
6. Choose an encryption password (minimum 8 characters), confirm it, and click **Save & Encrypt**.

> **Security note:** The service account key is encrypted with AES-256-GCM before being stored in IndexedDB. The plaintext key only exists in memory while the app is unlocked.

---

## Project Structure

```
src/
└── CptcYourself.Client/        # Blazor WebAssembly app
    ├── Components/
    │   └── PhotoCapture.razor  # Camera capture component
    ├── Models/
    │   ├── AppSettings.cs      # Persisted (encrypted) settings model
    │   └── ArtOptions.cs       # ArtStyle, ArtGenre, CptcProgram enums
    ├── Pages/
    │   ├── Home.razor           # Main capture + generation page
    │   ├── Settings.razor       # API credential configuration
    │   └── Unlock.razor         # Session unlock (password entry)
    ├── Services/
    │   ├── AppStateService.cs   # In-memory session state
    │   ├── CameraService.cs     # JS interop for camera
    │   ├── CryptoStorageService.cs  # Encrypt/decrypt + IndexedDB
    │   └── GoogleAiService.cs   # Gemini + Imagen API calls
    └── wwwroot/
        └── js/
            └── cptc-interop.js  # AES-GCM crypto, IndexedDB, camera, Vertex token
```

---