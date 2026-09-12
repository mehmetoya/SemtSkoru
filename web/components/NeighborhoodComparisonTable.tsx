import type { NeighborhoodScore } from "../lib/types";
import { ScoreBar } from "./ScoreBar";

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
    <div className="overflow-x-auto rounded-2xl border border-slate-200 bg-white p-6 shadow-sm">
      <table className="w-full min-w-xl border-collapse text-left">
        <thead>
          <tr>
            <th className="border-b border-slate-200 pb-3 pr-4"></th>
            <th className="border-b border-slate-200 pb-3 pr-4 text-base font-bold text-slate-900">
              {nameA}
            </th>
            <th className="border-b border-slate-200 pb-3 pr-4 text-base font-bold text-slate-900">
              {nameB}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td className="py-3 pr-4 font-medium text-slate-700">Genel Skor</td>
            <td className="py-3 pr-4 text-2xl font-extrabold text-slate-900">
              {scoreA.overall ?? "—"}
            </td>
            <td className="py-3 pr-4 text-2xl font-extrabold text-slate-900">
              {scoreB.overall ?? "—"}
            </td>
          </tr>
          {DIMENSIONS.map(({ key, label }) => (
            <tr key={key}>
              <td className="border-t border-slate-100 py-3 pr-4 font-medium text-slate-700">
                {label}
              </td>
              <td className="border-t border-slate-100 py-3 pr-4">
                <ScoreBar score={scoreA[key].score} />
              </td>
              <td className="border-t border-slate-100 py-3 pr-4">
                <ScoreBar score={scoreB[key].score} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
