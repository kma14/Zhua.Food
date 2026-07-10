# Front-end image: Vite build (Node) -> static files served by nginx, which also
# reverse-proxies the API paths to the `api` service (same-origin -> no CORS in prod).
# Build context = repo root (matches the other images / deploy.yml).
FROM node:22-alpine AS build
WORKDIR /app

# Install deps against the lockfile first (better layer caching).
COPY src/Zhua.Web/package.json src/Zhua.Web/package-lock.json ./
RUN npm ci

# Build the static bundle. No VITE_API_BASE_URL is set on purpose: the app then calls
# RELATIVE URLs (see src/api.ts), which nginx serves same-origin and proxies to the API.
COPY src/Zhua.Web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS final
COPY src/Zhua.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
