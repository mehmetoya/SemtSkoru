// Runs synchronously while the HTML is parsed, before first paint, so there's no flash of
// the wrong theme. See node_modules/next/dist/docs/01-app/02-guides/preventing-flash-before-hydration.md
// ("Themes" section) - this is that exact pattern, adapted to toggle a `dark` class instead
// of a data-theme attribute, since Tailwind's `dark:` variant here is configured off `.dark`.
const THEME_INIT_SCRIPT = `(function(){try{var t=localStorage.getItem("theme");var d=t?t==="dark":window.matchMedia("(prefers-color-scheme: dark)").matches;document.documentElement.classList.toggle("dark",d)}catch(e){}})()`;

export function ThemeScript() {
  return <script suppressHydrationWarning dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }} />;
}
