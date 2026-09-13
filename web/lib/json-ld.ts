// JSON.stringify doesn't escape "<", so a value containing the literal substring "</script>"
// would break out of the JSON-LD block into raw HTML. Nothing interpolated into a JSON-LD
// object today is end-user input (district names/ids come from the backend's own fixed seed
// data), but escaping "<" costs nothing and keeps this safe if that ever changes.
export function jsonLdScript(data: unknown): string {
  return JSON.stringify(data).replace(/</g, "\\u003c");
}
