// Verbatim summaries of the real scoring formulas in
// backend/src/SemtSkoru.Application/Scoring/DimensionScoring.cs - not invented copy.
export const DIMENSION_METHODOLOGY: Record<
  "airQuality" | "greenSpace" | "transportation",
  string
> = {
  airQuality:
    "İBB'nin canlı hava kalitesi istasyonundan alınan AQI (0-500, EPA standardı) değeri, EPA'nın 6 kategorisi (İyi → Tehlikeli) eşit dilimlere bölünüp ters çevrilerek 0-100'e taşınır. Düşük AQI = yüksek skor.",
  greenSpace:
    "En yakın parka olan mesafeye göre doğrusal azalan bir skor: 0 metre 100 puan, 2000 metre (~25 dakika yürüyüş) ve üzeri 0 puan.",
  transportation:
    "İlçedeki ortalama trafik hızına göre doğrusal bir skor: İstanbul'un şehir içi hız sınırı olan 50 km/sa ve üzeri 100 puan, durma noktası (0 km/sa) 0 puan.",
};
