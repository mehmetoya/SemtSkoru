import { describe, expect, it } from "vitest";
import { foldTurkish, matchDistrictNames, matchesDistrictName } from "../district-name-match";

// The one thing a search box is expected to do: type a district's name, get that district. These
// pin the folding rules that make that work for how people actually type Turkish.
describe("matchesDistrictName", () => {
  it.each([
    ["Bağcılar", "Bağcılar"],
    ["Bağcılar", "bagcilar"],
    ["Bağcılar", "BAĞCILAR"],
    ["Bağcılar", "bağ"],
    ["Bağcılar", "Bağcılar'da"],
    ["Kadıköy", "kadikoy"],
    ["Şişli", "sisli"],
    ["Üsküdar", "uskudar"],
    ["Gaziosmanpaşa", "gaziosmanpasa"],
  ])("matches %s when typed as %s", (name, query) => {
    expect(matchesDistrictName(name, query)).toBe(true);
  });

  // Substring, not prefix: these are the names people reach for by their distinctive middle.
  it.each([
    ["Küçükçekmece", "çekmece"],
    ["Büyükçekmece", "cekmece"],
    ["Çekmeköy", "köy"],
  ])("matches %s on the inner fragment %s", (name, query) => {
    expect(matchesDistrictName(name, query)).toBe(true);
  });

  it("does not match an unrelated district", () => {
    expect(matchesDistrictName("Bağcılar", "Kadıköy")).toBe(false);
  });

  // An empty box is not a match for all 39 districts - it means "no filter at all".
  it.each(["", "   "])("treats %p as no query rather than matching everything", (query) => {
    expect(matchesDistrictName("Bağcılar", query)).toBe(false);
  });
});

describe("matchDistrictNames", () => {
  const districts = [
    { id: "bagcilar", name: "Bağcılar" },
    { id: "bahcelievler", name: "Bahçelievler" },
    { id: "kadikoy", name: "Kadıköy" },
    { id: "kucukcekmece", name: "Küçükçekmece" },
  ];

  it("returns the ids of every district whose name matches", () => {
    expect(matchDistrictNames(districts, "ba")).toEqual(["bagcilar", "bahcelievler"]);
  });

  it("narrows as the query grows, which is what makes typing feel live", () => {
    expect(matchDistrictNames(districts, "bag")).toEqual(["bagcilar"]);
  });

  // Empty means "not a name query" - the caller falls through to the AI preference search, so
  // this must not be confused with "matched nothing".
  it("returns nothing for an empty query", () => {
    expect(matchDistrictNames(districts, "  ")).toEqual([]);
  });

  it("returns nothing for a preference query that names no district", () => {
    expect(matchDistrictNames(districts, "hava kalitesi iyi olsun")).toEqual([]);
  });
});

describe("foldTurkish", () => {
  // Dotted/dotless i is the case JS's own toLowerCase gets wrong for Turkish, and every keyword
  // and district name starting with one depends on it.
  it("folds the dotted and dotless i to the same letter", () => {
    expect(foldTurkish("İIı")).toBe("iii");
  });
});
