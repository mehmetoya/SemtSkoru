"use client";

import { useMutation } from "@tanstack/react-query";
import { fetchAssistantRecommendations } from "../api-client";

export function useAssistant(locale: string) {
  return useMutation({
    mutationFn: (prompt: string) => fetchAssistantRecommendations(prompt, locale),
  });
}
