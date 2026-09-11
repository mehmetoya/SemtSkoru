import type { NeighborhoodScore } from "../lib/types";

const DIMENSIONS = [
  { key: "airQuality", label: "Hava Kalitesi" },
  { key: "greenSpace", label: "Yeşil Alan" },
  { key: "transportation", label: "Ulaşım" },
] as const;

export function NeighborhoodComparisonTable({
  nameA,
  nameB,
  scoreA,
  scoreB,
}: {
  nameA: string;
  nameB: string;
  scoreA: NeighborhoodScore;
  scoreB: NeighborhoodScore;
}) {
  return (
    <table className="w-full max-w-md border-collapse text-left">
      <thead>
        <tr>
          <th className="border-b border-slate-200 py-2"></th>
          <th className="border-b border-slate-200 py-2">{nameA}</th>
          <th className="border-b border-slate-200 py-2">{nameB}</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td className="py-2 font-medium text-slate-700">Genel Skor</td>
          <td className="py-2 text-xl font-bold text-slate-900">
            {scoreA.overall ?? "—"}
          </td>
          <td className="py-2 text-xl font-bold text-slate-900">
            {scoreB.overall ?? "—"}
          </td>
        </tr>
        {DIMENSIONS.map(({ key, label }) => (
          <tr key={key}>
            <td className="border-t border-slate-100 py-2 font-medium text-slate-700">
              {label}
            </td>
            <td className="border-t border-slate-100 py-2">
              {scoreA[key].score ?? "Veri yok"}
            </td>
            <td className="border-t border-slate-100 py-2">
              {scoreB[key].score ?? "Veri yok"}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
