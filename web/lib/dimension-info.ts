// Verbatim summaries of the real scoring formulas in
// backend/src/SemtSkoru.Application/Scoring/DimensionScoring.cs - not invented copy.
export const DIMENSION_METHODOLOGY: Record<
  "airQuality" | "greenSpace" | "transportation" | "parking" | "healthAccess" | "transitAccess",
  string
> = {
  airQuality:
    "İBB'nin canlı hava kalitesi istasyonundan alınan AQI (0-500, EPA standardı) değeri, EPA'nın 6 kategorisi (İyi → Tehlikeli) eşit dilimlere bölünüp ters çevrilerek 0-100'e taşınır. Düşük AQI = yüksek skor.",
  greenSpace:
    "En yakın parka olan mesafeye göre doğrusal azalan bir skor: 0 metre 100 puan, 2000 metre (~25 dakika yürüyüş) ve üzeri 0 puan.",
  transportation:
    "İlçedeki ortalama trafik hızına göre doğrusal bir skor: İstanbul'un şehir içi hız sınırı olan 50 km/sa ve üzeri 100 puan, durma noktası (0 km/sa) 0 puan.",
  parking:
    "İBB İSPARK otoparklarının ilçedeki ortalama boş kapasite oranı (boş kapasite / toplam kapasite, otopark başına ağırlıksız ortalama): tamamen boş %100 doluluk oranı 100 puan, tamamen dolu 0 puan. Yalnızca İSPARK'ın işlettiği otoparkları kapsar, küçük bir örneklemdir ve cadde üstü park durumunu yansıtmaz.",
  healthAccess:
    "İBB'nin '34 Dakika İstanbul Sağlık İndeksi' verisi mahalle bazında yayınlanır; ilçedeki her mahallenin sağlık erişim endeksi, o mahallenin nüfusuyla ağırlıklandırılarak ilçe ortalaması hesaplanır (nüfusu 0 olan mahalleler ortalamaya dahil edilmez). İBB bu endeksin kendisinin 0-100 arası olduğunu belirtmiyor; MVP, gerçek 39 ilçe üzerinden gözlemlenen en düşük ve en yüksek ilçe ortalamasını 0-100'e doğrusal olarak ölçekler (en düşük ilçe 0, en yüksek ilçe 100 puan alır).",
  transitAccess:
    "İETT'nin şehir genelindeki otobüs durağı verisi (15.486 durak), her ilçenin sınırına karşı nokta-içinde-mi testiyle eşleştirilir (durakların ilçe kodu alanı güvenilir bir isim eşlemesi sunmadığı için). Ham durak sayısı yerine, durak sayısının ilçenin gerçek yüzölçümüne (km², enlem ortalamasına göre ölçeklenmiş basit bir düzlem yaklaşıklığıyla hesaplanır) bölünmesiyle bulunan yoğunluk (durak/km²) kullanılır — büyük bir ilçe sadece geniş olduğu için haksız avantaj kazanmasın diye. Bu yoğunluğun gerçek 39 ilçe üzerinden gözlemlenen en düşük ve en yüksek değeri 0-100'e doğrusal olarak ölçeklenir (en düşük ilçe 0, en yüksek ilçe 100 puan alır) — resmi bir standart değil, dürüstçe belgelenmiş bir MVP varsayımı.",
};
