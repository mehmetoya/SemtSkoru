// Runs synchronously while the HTML is parsed, before first paint, so there's no flash of
// the wrong theme. See node_modules/next/dist/docs/01-app/02-guides/preventing-flash-before-hydration.md
// ("Themes" section) - this is that exact pattern, adapted to toggle a `dark` class instead
// of a data-theme attribute, since Tailwind's `dark:` variant here is configured off `.dark`.
const THEME_INIT_SCRIPT = `(function(){try{var t=localStorage.getItem("theme");var d=t?t==="dark":window.matchMedia("(prefers-color-scheme: dark)").matches;document.documentElement.classList.toggle("dark",d)}catch(e){}})()`;

export function ThemeScript() {
  // React warns in dev whenever JSX renders a <script> tag, since scripts inserted via
  // client-side DOM updates never execute. This one only ever needs to run once, during the
  // initial HTML parse on the server-rendered page - the text/plain type on the client tells
  // React (and any later re-render) not to treat it as a script to (re-)execute.
  return (
    <script
      type={typeof window === "undefined" ? "text/javascript" : "text/plain"}
      suppressHydrationWarning
      dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }}
    />
  );
}
