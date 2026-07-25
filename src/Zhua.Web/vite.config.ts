import { defineConfig } from "vite";

const apiTarget = process.env.VITE_API_BASE_URL || "http://localhost:8080";

export default defineConfig({
  server: {
    proxy: {
      "/categories": apiTarget,
      "/products": apiTarget,
      "/deals": apiTarget,
      "/stores": apiTarget,
      "/health": apiTarget,
      "/admin": apiTarget,
      "/match-candidates": apiTarget,
      "/items": apiTarget,
      "/reports": apiTarget,
      "/openapi": apiTarget
    }
  },
  preview: {
    proxy: {
      "/categories": apiTarget,
      "/products": apiTarget,
      "/deals": apiTarget,
      "/stores": apiTarget,
      "/health": apiTarget,
      "/admin": apiTarget,
      "/match-candidates": apiTarget,
      "/items": apiTarget,
      "/reports": apiTarget,
      "/openapi": apiTarget
    }
  }
});
