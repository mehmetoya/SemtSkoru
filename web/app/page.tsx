"use client";

import Link from "next/link";
import { useNeighborhoods } from "../lib/hooks/useNeighborhoods";

export default function Home() {
  const { data: neighborhoods, isPending, isError } = useNeighborhoods();

  return (
    <main className="flex min-h-screen flex-col items-center gap-8 p-12">
      <h1 className="text-2xl font-semibold text-slate-800">SemtSkoru</h1>

      {isPending && <p role="status">Yükleniyor…</p>}
      {isError && (
        <p className="text-red-700">İlçe listesi yüklenirken bir hata oluştu.</p>
      )}

      {neighborhoods && (
        <ul className="flex flex-col gap-2">
          {neighborhoods.map((neighborhood) => (
            <li key={neighborhood.id}>
              <Link
                href={`/mahalle/${neighborhood.id}`}
                className="text-lg text-blue-700 underline"
              >
                {neighborhood.name}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
