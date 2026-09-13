"use client";

import { useState, type FormEvent } from "react";
import { useAsistan } from "../lib/hooks/useAsistan";
import { AsistanOneriKart } from "./AsistanOneriKart";

const ORNEK_ISTEKLER = [
  "Çocuklu bir aileyim, yeşil alan ve sağlık erişimi önemli, bütçem sınırlı.",
  "Uzaktan çalışıyorum, hava kalitesi ve toplu taşımaya yakınlık önemli.",
];

const MAX_ISTEK_UZUNLUGU = 600;

export function AsistanClient() {
  const [prompt, setPrompt] = useState("");
  const { mutate, data, isPending, isError, reset } = useAsistan();

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (prompt.trim().length === 0 || isPending) {
      return;
    }
    mutate(prompt);
  }

  function handleClear() {
    reset();
    setPrompt("");
  }

  return (
    <div className="mt-6">
      <form onSubmit={handleSubmit} className="flex flex-col gap-3">
        <label htmlFor="asistan-istek" className="text-sm font-medium text-slate-700 dark:text-slate-300">
          Ne arıyorsun?
        </label>
        <textarea
          id="asistan-istek"
          value={prompt}
          onChange={(event) => setPrompt(event.target.value)}
          rows={4}
          maxLength={MAX_ISTEK_UZUNLUGU}
          placeholder={ORNEK_ISTEKLER[0]}
          className="w-full resize-y rounded-xl border border-slate-300 bg-white p-3 text-sm text-slate-900 shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-100"
        />
        <div className="flex flex-wrap items-center gap-3">
          <button
            type="submit"
            disabled={isPending || prompt.trim().length === 0}
            className="rounded-full bg-blue-600 px-5 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-400"
          >
            {isPending ? "Öneriler hazırlanıyor…" : "Öner"}
          </button>
          {(data || isError) && (
            <button
              type="button"
              onClick={handleClear}
              className="text-sm text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200"
            >
              Temizle
            </button>
          )}
        </div>
      </form>

      <div className="mt-3 flex flex-wrap items-center gap-2 text-xs text-slate-500 dark:text-slate-400">
        <span>Örnek:</span>
        {ORNEK_ISTEKLER.map((ornek) => (
          <button
            key={ornek}
            type="button"
            onClick={() => setPrompt(ornek)}
            className="rounded-full bg-slate-100 px-2.5 py-1 text-left hover:bg-slate-200 dark:bg-slate-800 dark:hover:bg-slate-700"
          >
            {ornek}
          </button>
        ))}
      </div>

      {isPending && (
        <p role="status" className="mt-6 text-sm text-slate-500 dark:text-slate-400">
          Gerçek skor verileriniz üzerinden değerlendiriliyor…
        </p>
      )}

      {isError && (
        <p className="mt-6 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-800 dark:bg-red-950/40 dark:text-red-300">
          Bir şeyler ters gitti. Lütfen daha sonra tekrar deneyin.
        </p>
      )}

      {data && data.status !== "Ok" && (
        <p className="mt-6 rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-800 dark:bg-amber-950/50 dark:text-amber-300">
          {data.message ?? "Şu anda güvenilir bir öneri oluşturulamadı."}
        </p>
      )}

      {data && data.status === "Ok" && data.recommendations.length > 0 && (
        <div className="mt-8">
          <p className="mb-4 inline-flex items-center gap-1.5 rounded-full bg-violet-50 px-3 py-1.5 text-xs font-medium text-violet-800 dark:bg-violet-950/40 dark:text-violet-300">
            ✨ Google Gemini ile oluşturuldu — SemtSkoru&apos;nun gerçek skor verilerine dayanır
          </p>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            {data.recommendations.map((oneri) => (
              <AsistanOneriKart key={oneri.neighborhoodId} oneri={oneri} />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
