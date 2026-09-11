"use client";

import { useQuery } from "@tanstack/react-query";
import { fetchNeighborhoodComparison } from "../api-client";

export function useCompareNeighborhoods(
  a: string | undefined,
  b: string | undefined,
) {
  return useQuery({
    queryKey: ["neighborhood-compare", a, b],
    queryFn: () => fetchNeighborhoodComparison(a!, b!),
    enabled: Boolean(a) && Boolean(b),
  });
}
