"use client";

import { useMutation } from "@tanstack/react-query";
import { fetchAsistanOnerileri } from "../api-client";

export function useAsistan() {
  return useMutation({
    mutationFn: (prompt: string) => fetchAsistanOnerileri(prompt),
  });
}
