"use client";

import { useQuery } from "@tanstack/react-query";
import { fetchNeighborhoodScore } from "../api-client";

export function useNeighborhoodScore(neighborhoodId: string | undefined) {
  return useQuery({
    queryKey: ["neighborhood-score", neighborhoodId],
    queryFn: () => fetchNeighborhoodScore(neighborhoodId!),
    enabled: Boolean(neighborhoodId),
  });
}
