"use client";

import { useQuery } from "@tanstack/react-query";
import { fetchNeighborhoods } from "../api-client";

export function useNeighborhoods() {
  return useQuery({
    queryKey: ["neighborhoods"],
    queryFn: fetchNeighborhoods,
  });
}
