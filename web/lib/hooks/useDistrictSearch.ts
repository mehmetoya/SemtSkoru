"use client";

import { useMutation } from "@tanstack/react-query";
import { fetchDistrictSearch } from "../api-client";

export function useDistrictSearch() {
  return useMutation({
    mutationFn: (query: string) => fetchDistrictSearch(query),
  });
}
