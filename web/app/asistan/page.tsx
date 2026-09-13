import type { Metadata } from "next";
import { AssistantClient } from "../../components/AssistantClient";

export const metadata: Metadata = {
  title: "AI Semt Asistanı",
  description:
    "Tercihlerini birkaç cümleyle yaz; SemtSkoru'nun 39 ilçe için hesapladığı gerçek İBB açık veri skorlarına dayanan AI destekli ilçe önerilerini gör.",
  alternates: { canonical: "/asistan" },
};

export default function AssistantPage() {
  return (
    <main className="mx-auto max-w-5xl px-4 py-10 sm:px-6 sm:py-14">
      <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-100">
        AI Semt Asistanı
      </h1>
      <p className="mt-2 max-w-2xl text-sm text-slate-600 dark:text-slate-400">
        Ne aradığını birkaç cümleyle yaz, SemtSkoru&apos;nun 39 ilçe için hesapladığı gerçek
        skorlara bakarak sana 2-3 ilçe önersin. Öneriler her zaman gerçek İBB açık verisine
        dayanır — model, elindeki sayıların dışında İstanbul hakkında hiçbir şey uydurmaz.
      </p>
      <div className="mt-4 flex flex-wrap gap-2 text-xs font-medium text-slate-600 dark:text-slate-300">
        <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">39 ilçe</span>
        <span className="rounded-full bg-slate-100 px-2.5 py-1 dark:bg-slate-800">İBB Açık Veri Portalı</span>
        <span className="rounded-full bg-violet-50 px-2.5 py-1 text-violet-800 dark:bg-violet-950/40 dark:text-violet-300">
          Google Gemini destekli
        </span>
      </div>
      <AssistantClient />
    </main>
  );
}
