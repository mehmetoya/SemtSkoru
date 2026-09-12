import type { MetadataRoute } from "next";
import { fetchNeighborhoods } from "../lib/api-client";
import { SITE_URL } from "../lib/site";

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const neighborhoods = await fetchNeighborhoods().catch(() => []);

  return [
    { url: SITE_URL, changeFrequency: "daily", priority: 1 },
    { url: `${SITE_URL}/karsilastir`, changeFrequency: "weekly", priority: 0.8 },
    ...neighborhoods.map((n) => ({
      url: `${SITE_URL}/mahalle/${n.id}`,
      changeFrequency: "daily" as const,
      priority: 0.9,
    })),
  ];
}
