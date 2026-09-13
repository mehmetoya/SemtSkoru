"use client";

import { useMutation } from "@tanstack/react-query";
import { fetchAssistantRecommendations } from "../api-client";

export function useAssistant() {
  return useMutation({
    mutationFn: (prompt: string) => fetchAssistantRecommendations(prompt),
  });
}
