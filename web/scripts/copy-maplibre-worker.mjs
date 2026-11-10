// maplibre-gl resolves its Web Worker script URL from its own bundled module's
// import.meta.url; under Next.js/Turbopack that URL doesn't match /^https?:/, so
// resolution silently fails and the worker ends up importing the page's own HTML,
// which throws immediately - every GeoJSON source then hangs forever (isSourceLoaded
// never reaches true, confirmed live: choropleth districts never render). Copying the
// worker script (and the shared chunk it imports) into public/ gives it a real,
// static http(s) URL that NeighborhoodMap.tsx points to explicitly via setWorkerUrl().
import { copyFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const src = join(__dirname, "..", "node_modules", "maplibre-gl", "dist");
const dest = join(__dirname, "..", "public");

mkdirSync(dest, { recursive: true });
for (const file of ["maplibre-gl-worker.mjs", "maplibre-gl-shared.mjs"]) {
  copyFileSync(join(src, file), join(dest, file));
  console.log(`copied ${file} -> public/`);
}
