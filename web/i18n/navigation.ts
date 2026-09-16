import { createNavigation } from "next-intl/navigation";
import { routing } from "./routing";

// No `pathnames` config in routing.ts, so these behave like next/navigation's own
// Link/usePathname/useRouter/redirect, just automatically locale-aware (prefixing hrefs
// for /en, leaving Turkish's default-locale hrefs unprefixed, and stripping the /en
// prefix back off of usePathname()'s result so callers can compare paths the same way
// regardless of which locale is active).
export const { Link, redirect, usePathname, useRouter, getPathname } =
  createNavigation(routing);
