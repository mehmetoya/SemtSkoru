# Veri Kaynakları — Araştırma Sonuçları (Task 4)

Bu doküman, SemtSkoru MVP'sinin dört veri ihtiyacı için gerçekten test edilmiş, doğrulanmış kaynakları kaydeder. **Hiçbir endpoint uydurulmadı** — her biri bu araştırma sırasında gerçek bir HTTP isteğiyle doğrulandı (tarih: 2026-09-11).

**Önemli terminoloji notu:** Kadıköy, Üsküdar, Beşiktaş birer **ilçe**'dir (mahalle değil — bkz. SPEC.md "Terminoloji notu"). Bu doküman boyunca "hedef ilçeler" bu üçünü ifade eder.

---

## 1. Hava Kalitesi — ✅ Doğrulandı, canlı

**Kaynak:** İBB Açık Veri Portalı, Çevre Koruma ve Kontrol Dairesi Başkanlığı.

**İstasyon listesi endpoint'i:**
```
GET https://api.ibb.gov.tr/havakalitesi/OpenDataPortalHandler/GetAQIStations
```
- Kimlik doğrulama: **yok**
- Yanıt: JSON dizi, her öğe `{Id, Name, Adress, Location}` (Location = WKT `POINT(lon lat)`)
- Test tarihinde 28 istasyon döndü; hedef ilçelerin üçü de birebir istasyon adıyla eşleşiyor:
  - `Beşiktaş` → `179cd958-11aa-4e7a-8fa4-6eb2c852c2f6`
  - `Kadıköy` → `ecafeb15-905e-4257-a25a-72accf287e2a`
  - `Üsküdar` → birden fazla istasyon var (`Üsküdar 1`, `Selimiye`, `Kandilli 1`); MVP için `Üsküdar 1` (`a30101a6-349c-4f0d-b965-a68f2c6781e9`) kullanılacak.

**Ölçüm sonuçları endpoint'i:**
```
GET https://api.ibb.gov.tr/havakalitesi/OpenDataPortalHandler/GetAQIByStationId
    ?StationId={guid}&StartDate={dd.MM.yyyy HH:mm:ss}&EndDate={dd.MM.yyyy HH:mm:ss}
```
- Kimlik doğrulama: **yok**
- Tarih formatı `dd.MM.yyyy HH:mm:ss` — başka format `System.FormatException` döndürüyor (test edildi).
- Yanıt: saatlik JSON dizisi, her öğe `{ReadTime, Concentration: {PM10,SO2,O3,NO2,CO}, AQI: {..., AQIIndex, ContaminantParameter, State, Color}}`
- Test isteği (2026-09-01 → 2026-09-10, Kadıköy istasyonu) gerçek, güncel saatlik veri döndürdü.

**Lisans:** İstanbul Büyükşehir Belediyesi Açık Veri Lisansı.

**Güncellik:** Saatlik, canlı.

**Karar:** Bu kaynağı doğrudan kullan. `AirQualityIngestionJob` (Task 7) her istasyon için günlük olarak son 24 saati çekip normalize edecek.

---

## 2. Yeşil Alan / Park Erişimi — ✅ Doğrulandı, yıllık güncelleniyor

**Kaynak:** İBB Açık Veri Portalı, Park ve Bahçeler Dairesi Başkanlığı — "İstanbul Kentsel Açık ve Yeşil Alan Koordinatları" (`kentsel-acik-ve-yesil-alanlar`).

**Doğrudan indirme:**
```
GET https://data.ibb.gov.tr/dataset/82e809cf-9465-407a-91cd-ac745d6fbc95/resource/41ddb7a6-6931-4176-9614-2c2892da5307/download/yaysis_mahal_geo_data.geojson
```
- Kimlik doğrulama: **yok**
- Format: GeoJSON, ~52.6 MB (İstanbul geneli)
- Alanlar: tür (park/yeşil alan tipi), ad, **ilçe**, **mahalle**, koordinat — hem ilçe hem mahalle bazında filtrelenebiliyor.
- Lisans: İstanbul Büyükşehir Belediyesi Açık Veri Lisansı (CKAN `license_id: ibb-license`).
- Son güncelleme: 2025-07-17 (metadata), dosya içeriği 2025-04-08'de yüklenmiş.

**Güncellik notu:** Parklar sık değişmediği için yıllık/aylık güncelleme MVP için yeterli — bu, hava kalitesi/trafik kadar "canlılık" gerektirmeyen tek boyut.

**Karar:** `GreenSpaceIngestionJob` (Task 8) bu dosyayı indirip 3 hedef ilçeye filtreleyecek, NetTopologySuite ile ilçe merkezine en yakın parkın mesafesini hesaplayacak. 52 MB'lık dosyanın tamamını sık sık çekmemek için ingestion job'ı haftalık çalışacak şekilde planlanmalı (Task 8'de netleştirilecek).

---

## 3. Trafik Yoğunluğu — ⚠️ Doğrulandı ama sınırlı; kullanıcı kararı alındı

İki kaynak test edildi:

### 3a. Traffic Index Web Service (canlı ama tek boyutlu)
```
GET https://api.ibb.gov.tr/tkmservices/api/TrafficData/v1/TrafficIndexHistory/{day}/{period}
```
- Kimlik doğrulama: **yok**
- Test: `.../TrafficIndexHistory/1/H` → saatlik, **bugünün tarihiyle** canlı veri döndü (`TrafficIndex`, `TrafficIndexDate`)
- **Sorun:** Bu, İstanbul'un tamamı için TEK bir indeks — ilçeye göre ayrım yapmıyor. Üç ilçeyi karşılaştırmak için kullanılamaz.

### 3b. Hourly Traffic Density Data Set (geohash bazlı, ama bayat)
```
GET https://data.ibb.gov.tr/dataset/3ee6d744-5da2-40c8-9cd6-0e3e41f1928f/resource/57cb067b-1a0b-460b-8342-7884bd4537e8/download/traffic_density_202501.csv
```
- Kimlik doğrulama: **yok**
- Format: CSV, alanlar `DATE_TIME, LATITUDE, LONGITUDE, GEOHASH, MINIMUM_SPEED, MAXIMUM_SPEED, AVERAGE_SPEED, NUMBER_OF_VEHICLES` — gerçekten konum bazlı, ilçe ayrımı yapılabilir (test edildi: ~1.76M satır/ay, ~140 MB/ay).
- **En güncel dosya: Ocak 2025** (`traffic_density_202501.csv`) — bugüne (2026-09) göre ~20 ay bayat.
- Veri seti sayfasında açıkça: *"The data will be updated at a later date."*
- Lisans: İstanbul Büyükşehir Belediyesi Açık Veri Lisansı.

**Karar (kullanıcı onayı ile):** Ocak 2025 dosyası kullanılacak. `TrafficIngestionJob` (Task 9) bu CSV'yi bir kez indirip 3 hedef ilçenin geohash hücrelerine filtreleyecek, ortalama hızı "trafik sakinliği" skoruna çevirecek. **UI'da açıkça "trafik skoru Ocak 2025 tarihsel ortalamasına dayanıyor, canlı değil" uyarısı gösterilecek** — `DataSourceMetadata.DataFreshnessStatus` bunu zaten destekliyor (`PublishedAt` = 2025-01, `LastSuccessfulSyncAt` = ingestion tarihi, ikisi arasındaki fark UI'da yansıtılacak).

---

## 4. İlçe Sınırları — ⚠️ Resmi kaynak yok, OSM fallback doğrulandı

İBB Açık Veri Portalı'nda CKAN arama API'si (`package_search`) ile "mahalle sınır", "idari sınır", "il sınırları" gibi sorgular denendi — **idari sınır poligonu içeren bir veri seti bulunamadı**. En yakın sonuç "Muhtarlık Adres Bilgileri" (nokta konumları, poligon değil).

**Fallback:** OpenStreetMap / Nominatim, `polygon_geojson=1` parametresiyle:
```
GET https://nominatim.openstreetmap.org/search?q={ilçe},+İstanbul,+Türkiye&format=jsonv2&polygon_geojson=1&limit=1
```
- Üç hedef ilçenin de (`Kadıköy`, `Üsküdar`, `Beşiktaş`) `type: administrative` polygon geometrisiyle döndüğü test edildi (OSM relation id'leri kaydedildi, bkz. Task 6 seed data).
- Lisans: **ODbL** (OpenStreetMap contributors) — MIT'ten farklı, atıf zorunlu. README'de "Harita verileri © OpenStreetMap katkıda bulunanları" notu gerekecek (Task 18).
- Kullanım politikası: Nominatim'in genel kullanım politikası saniyede 1 istek ile sınırlıyor ve anlamlı bir `User-Agent` başlığı istiyor — 3 sabit ilçe için tek seferlik/nadir bir sorgu olduğundan sorun teşkil etmiyor, ama ingestion job'ı bunu her seferinde değil, yalnızca seed/migration zamanında çağırmalı (canlı sorgu değil, statik olarak `Neighborhood` tablosuna kaydedilecek).

**Karar:** Task 6'da seed data için bu 3 poligon bir kereye mahsus çekilip migration/seed script'ine gömülecek; runtime'da Nominatim'e bağımlılık olmayacak.

---

## 5. Faz 2 — 39 İlçeye Genişleme (Doğrulama, 2026-09-13)

3 hedef ilçeden İstanbul'un tüm 39 ilçesine genişlerken, her kaynağın gerçekten
39 ilçeyi kapsayıp kapsamadığı canlı olarak test edildi — varsayılmadı.

**Yeşil alan — ✅ 39/39 doğrulandı.** Bölüm 2'deki GeoJSON tekrar indirilip
`ILCE` alanının tüm benzersiz değerleri çıkarıldı: tam 39 değer, İstanbul'un
resmi 39 ilçesiyle birebir eşleşiyor, her ilçede en az 1 `TUR=Park` etiketli
kayıt var (Şile 98 ile en fazla, Arnavutköy/Esenyurt/Güngören 1'er ile en az).

**Trafik — ✅ veri seti şehir geneli, kod zaten genel.** CSV'nin kendisi ilçe
sınırlaması içermiyor (bölüm 3b'de zaten belgelenmişti); `TrafficIngestionJob`
de sabit bir ilçe listesi değil, DB'deki `Neighborhood` tablosunun tamamını
okuyor. Tek gerçek engel 36 ilçenin sınır poligonunun eksik olmasıydı, aşağıda
çözüldü. **Performans notu:** 39 ilçeye çıkınca `Geometry.Contains()`'in
düz (prepared olmayan) hali, 1.76M satırlık CSV'yi işlerken kaynağın
bağlantısını zaman aşımına uğratacak kadar yavaşladı (canlı doğrulandı: `curl`
dosyayı 4 saniyede çekiyor, düzeltme öncesi ingestion job'ı ~2 dakikada
%60'ta kesiliyordu) — `NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory`
ile düzeltildi, düzeltme sonrası tam çalışma ~5 saniye.

**Hava kalitesi — ❌ 18/39 ile sınırlı, doğrulandı.** `GetAQIStations` canlı
çağrıldı: 28 istasyon, her istasyonun gerçek `Location` koordinatı (nokta) 39
ilçe poligonuna karşı test edildi (`AirQualityIngestionJob`'da aynı trafik
tekniğiyle) — yalnızca 18 ilçenin sınırları içinde bir istasyon var. Ayrıca
istasyon *adına* veya `Adress` serbest metin alanına göre eşleştirmenin
güvenilmez olduğu canlı olarak doğrulandı: "Kartal" adlı istasyon aslında
Pendik'te kayıtlı, `Adress` formatı tutarsız ("İstanbul / X - Turkey" /
"İstanbul - X" / mobil birim için "İBB HAKİM") — bu yüzden istasyon-ilçe
eşlemesi artık gerçek koordinat + nokta-poligon testiyle yapılıyor, isim/adres
metniyle değil. Kalan 21 ilçe için kullanıcı onayıyla dürüstçe "Veri yok"
gösteriliyor.

**İlçe sınırları (36 yeni) — aynı yöntem, ölçeklendirildi.** Bölüm 4'teki
yöntem (Nominatim, `polygon_geojson=1`, 1 istek/saniye, anlamlı `User-Agent`)
36 ilçe için tekrarlandı. 35/36 ilk denemede doğru sonuç verdi; **Kağıthane**
istisnaydı — düz isim sorgusu (`Kağıthane, İstanbul, Türkiye`) Nominatim'in
en iyi eşleşmesi olarak bir tren istasyonu (Point geometri) döndürdü, idari
sınır değil. Gerçek OSM relation'ı (`R1765894`, "Kâğıthane" — OSM'nin kendi
yazımı, İBB'nin "Kağıthane" yazımından farklı) elle bulunup doğrudan
`/lookup?osm_ids=R1765894` ile çekildi. Adalar ve Şile gerçek çoklu-ada
ilçeler (MultiPolygon) — `Neighborhood.Boundary` kolonu bu yüzden
`geometry(Polygon,4326)`'dan `geometry(Geometry,4326)`'ya genişletildi.

---

## Özet Tablo

| Boyut | Kaynak | Canlı mı? | 39 ilçe kapsıyor mu? | Auth | Lisans | Karar |
|---|---|---|---|---|---|---|
| Hava kalitesi | api.ibb.gov.tr/havakalitesi | ✅ Saatlik | ❌ Yalnızca 18/39 (istasyonu olan) | Yok | İBB Açık Veri Lisansı | Doğrudan kullan; kalan 21 ilçe "Veri yok" |
| Yeşil alan | data.ibb.gov.tr GeoJSON | ⚠️ Yıllık | ✅ 39/39 | Yok | İBB Açık Veri Lisansı | Doğrudan kullan |
| Trafik | data.ibb.gov.tr CSV (Ocak 2025) | ❌ Bayat (~20 ay) | ✅ 38/39 (Adalar'da yol trafiği yok) | Yok | İBB Açık Veri Lisansı | Kullan, UI'da "tarihsel" etiketiyle |
| İlçe sınırı | OpenStreetMap/Nominatim | N/A (statik seed) | ✅ 39/39 | Yok (rate-limit'li) | ODbL (atıf gerekli) | Seed-time'da çek, sakla |
