import { NeighborhoodScoreCard } from "../../../components/NeighborhoodScoreCard";

export default async function MahallePage({
  params,
}: PageProps<"/mahalle/[id]">) {
  const { id } = await params;

  return (
    <main className="flex min-h-screen flex-col items-center gap-8 p-12">
      <div className="w-full max-w-md">
        <NeighborhoodScoreCard neighborhoodId={id} />
      </div>
    </main>
  );
}
