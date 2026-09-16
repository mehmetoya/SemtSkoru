import createMiddleware from "next-intl/middleware";
import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";
import { routing } from "./i18n/routing";

// Next.js 16 renamed Middleware to Proxy (this file must be named `proxy.ts`, not
// `middleware.ts`, or routing silently fails - the `createMiddleware` API itself is
// unchanged). See node_modules/next/dist/docs/01-app/01-getting-started/16-proxy.md and
// next-intl's own example, which already ships a `proxy.ts`:
// https://github.com/amannn/next-intl/blob/main/examples/example-app-router/src/proxy.ts
const handleI18nRouting = createMiddleware(routing);

export default function proxy(request: NextRequest) {
  // The share/download score-card images (app/mahalle/[id]/kart, app/karsilastir/kart)
  // render a fixed, Turkish-only, timestamped PNG snapshot regardless of site locale (see
  // lib/score-card-image.ts) - they live outside app/[locale] entirely and must never be
  // rewritten or redirected by locale negotiation.
  if (request.nextUrl.pathname.endsWith("/kart")) {
    return NextResponse.next();
  }

  return handleI18nRouting(request);
}

export const config = {
  // Same exclusions next-intl's own docs/examples use: API routes, Next internals, and
  // any request for a file with an extension (favicons, sitemap.xml, robots.txt, etc).
  matcher: "/((?!api|_next|_vercel|.*\\..*).*)",
};
