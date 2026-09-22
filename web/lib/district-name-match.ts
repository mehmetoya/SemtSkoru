// Matching district names in the browser, against the list the home page already rendered.
//
// Deliberately NOT a backend call. The home page server-renders all 39 districts and hands them
// to NeighborhoodSearchableList, so every name a visitor could type is already sitting in memory:
// a round trip (let alone an AI one) would add latency and a dependency on Gemini being up to
// answer a question the page can already answer itself, instantly, on every keystroke. The AI
// search endpoint stays for what it is actually good at - turning "temiz hava ve yeşil alan" into
// real dimension scores - which is a different question from "where is Bağcılar".

// Lowercases, drops apostrophes and folds Turkish diacritics, so "Bağcılar", "bagcilar" and
// "BAĞCILAR'da" all reduce to the same thing. Mirrors the backend's own Fold in
// DistrictSearchService.cs, for the same reason: people type district names without diacritics all
// the time, and a search that only matched the perfectly-accented spelling would look broken.
// The dotted/dotless i pair is mapped explicitly before lowercasing, because JS's toLowerCase does
// not know that "İ" folds to "i" in Turkish.
export function foldTurkish(value: string): string {
  return value
    .replace(/[İI]/g, "i")
    .replace(/ı/g, "i")
    .replace(/[Şş]/g, "s")
    .replace(/[Ğğ]/g, "g")
    .replace(/[Üü]/g, "u")
    .replace(/[Öö]/g, "o")
    .replace(/[Çç]/g, "c")
    .replace(/['’]/g, "")
    .toLowerCase()
    // Catches any remaining accented forms without a second table.
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .normalize("NFC");
}

// Substring rather than prefix matching: "çekmece" should find both Küçükçekmece and
// Büyükçekmece, and "köy" should find Çekmeköy and Kadıköy. With 39 fixed items there is no cost
// to being generous, and a typeahead that ignores the middle of a name reads as broken.
//
// The second test covers the case that breaks a plain substring check: Turkish case suffixes make
// the QUERY longer than the name it refers to, so "Bağcılar'da" folds to "bagcilarda", which
// "bagcilar" cannot contain. Accepting a query that starts with the whole name handles every such
// ending ("-da", "-a", "-ın", ...) without a suffix table, and cannot pull in another district,
// since it still has to begin with this one's full name.
export function matchesDistrictName(districtName: string, query: string): boolean {
  const foldedQuery = foldTurkish(query).trim();
  if (foldedQuery.length === 0) {
    return false;
  }
  const foldedName = foldTurkish(districtName);
  return foldedName.includes(foldedQuery) || foldedQuery.startsWith(foldedName);
}

// The ids of every district whose name matches, in the order they were given (the home page
// already sorts them by name). An empty result means "this query is not about a district name" -
// the caller is what decides whether that should fall through to the AI preference search.
export function matchDistrictNames<T extends { id: string; name: string }>(
  districts: readonly T[],
  query: string,
): string[] {
  if (foldTurkish(query).trim().length === 0) {
    return [];
  }
  return districts.filter((district) => matchesDistrictName(district.name, query)).map((d) => d.id);
}
