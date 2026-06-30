# Zhua.Web

React/Vite frontend for the daily price-finds dashboard.

## Local run

```bash
cd src/Zhua.Web
npm install
npm run dev
```

The Vite dev server runs on `http://localhost:5173`.

To point the frontend at a local API, create `.env.local`:

```text
VITE_API_BASE_URL=http://localhost:8080
```

Until the backend insights endpoint exists, the app falls back to local sample data so the frontend can be reviewed.
