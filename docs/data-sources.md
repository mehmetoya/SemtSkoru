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

## 6. Otopark (İSPARK) — ✅ Doğrulandı, canlı, 4. boyut

**Kaynak:** İBB İSPARK (İstanbul Büyükşehir Belediyesi'nin işlettiği kapalı/açık otoparklar).

**Endpoint:**
```
GET https://api.ibb.gov.tr/ispark/Park
```
- Kimlik doğrulama: **yok**
- Test tarihi: 2026-09-13. Yanıt: JSON dizi, 247 otopark nesnesi. Gerçek alan adları
  (canlı doğrulandı, tahmin edilmedi): `parkID` (int), `parkName` (string), `lat`/`lng`
  (string, "POINT" değil düz ondalık metin), `capacity` (int), `emptyCapacity` (int),
  `workHours` (string), `parkType` (string, ör. "AÇIK OTOPARK"/"KAPALI OTOPARK"),
  `freeTime` (int), `district` (string, büyük harf ilçe adı), `isOpen` (int, 0/1).
- `district` alanı zaten İBB'nin kendi büyük harfli ilçe adı — yeşil alan verisindeki
  `ILCE` alanıyla aynı kural, bu yüzden nokta-poligon testi değil, doğrudan isim eşleştirmesi
  yeterli (`ParkingIngestionJob`, `GreenSpaceIngestionJob` ile aynı teknik).

**Kapsama testi (39 ilçenin tamamına karşı canlı doğrulandı):** 247 otopark, 34 farklı
`district` değeri altında toplanıyor. 33'ü ilçenin Türkçe büyük harfli adıyla birebir
eşleşiyor. Bir gerçek isim uyuşmazlığı bulundu — Kağıthane'nin Nominatim sorunuyla aynı
türden: **Eyüpsultan'ın 9 otoparkı `district: "EYÜP"` ile etiketli**, ilçenin 2019 öncesi
adıyla, hiçbir zaman "EYÜPSULTAN" değil. Bu, `ParkingIngestionJob`'da açık bir isim
takma adı (`EYÜPSULTAN` → `EYÜP`) ile ele alındı — tahmin değil, canlı veriden doğrulanmış
bir düzeltme. Bu düzeltmeyle birlikte **34/39 ilçe** en az bir otoparka sahip; kalan 5 ilçe
(**Adalar, Çatalca, Sancaktepe, Silivri, Şile**) için İSPARK'ın hiç tesisi yok — bu ilçelerde
otopark boyutu dürüstçe "Veri yok" gösteriliyor, tahmin/enterpolasyon yapılmıyor.

**Lisans:** İstanbul Büyükşehir Belediyesi Açık Veri Lisansı.

**Güncellik:** Canlı — yanıt otoparkın o anki doluluk durumunu veriyor, ama nesne başına bir
zaman damgası yok (hava kalitesinin `ReadTime`'ının aksine); bu yüzden `PublishedAt` çekme
anının kendisi olarak kaydediliyor.

**Skor formülü:** İlçedeki her otoparkın boş kapasite oranı (`emptyCapacity / capacity`)
hesaplanıp otoparklar arasında ağırlıksız ortalaması alınıyor (hava kalitesinin çoklu
istasyon ortalamasıyla aynı yaklaşım) — ortalama %100 boşsa 100 puan, tamamen doluysa 0 puan.
Bu, yalnızca İSPARK'ın işlettiği otoparkların küçük bir örneklemi; cadde üstü park durumunu
yansıtmıyor (bkz. `DimensionScoring.ScoreParking` içindeki yorum).

**Karar:** Bu kaynağı doğrudan kullan. `ParkingIngestionJob`, air quality ile aynı günlük
sıklıkla (doluluk hızlı değişen, canlı bir sinyal) tüm otoparkları çekip ilçe bazında
normalize edecek.

---

## 7. Sağlık Erişimi (34 Dakika İstanbul Sağlık İndeksi) — ✅ Doğrulandı, 5. boyut

**Kaynak:** İBB'nin "34 Dakika İstanbul" çok-modlu erişilebilirlik çerçevesinin bir parçası
olan "34 Dakika İstanbul Sağlık İndeksi" (sağlık hizmetlerine erişimi ölçen indeks).

**Endpoint:**
```
GET https://data.ibb.gov.tr/dataset/d68cd520-971c-46c1-98cb-6cb66c940604/resource/d90e11be-d5b3-4df2-ba1f-e4356d336ad7/download/saglik_index.geojson
```
- Kimlik doğrulama: **yok**
- Test tarihi: 2026-09-13. Yanıt: GeoJSON FeatureCollection, 901 Polygon feature — her biri
  bir **mahalle** (ilçe değil). Gerçek property adları (canlı doğrulandı, tahmin edilmedi):
  `ILCE_ADI` (string, büyük harf ilçe adı), `MAHALLE_ADI` (string), `KISI_SAYISI`
  (int, mahalle nüfusu — 901 mahallenin 97'sinde 0), `SAGLIK_INDEX` (float, gerçek gözlemlenen
  aralık 0 - 82.9086).
- Ayrıca gerçek bir metodoloji PDF'i var (TR ve EN). EN sürümü okundu: İBB'nin "34 Dakika
  İstanbul" çerçevesi üç ayrı indeksten oluşuyor — **Diversity Index** (günlük ihtiyaçlara
  erişim çeşitliliği; "Health" fonksiyonu aile sağlığı merkezleri, hastaneler, eczaneler vb.
  sayısını içeriyor), **Affordability Index** ve **Walkability Index** — ve yalnızca bu
  üçünün toplamı olan **Quality of Life Index**'in 0-100 arası olduğu belirtiliyor. PDF,
  `SAGLIK_INDEX`'in kendisinin 0-100 arası bir skala olduğunu **hiçbir yerde iddia etmiyor**;
  bu yüzden `DimensionScoring.ScoreHealthAccess` bunu varsaymıyor (aşağıya bkz.).

**Diğer üç mahalle/ilçe eşleştirmeli boyuttan farkı:** Bu veri **mahalle düzeyinde**
yayınlanıyor, ilçe düzeyinde değil — yeşil alan ve otoparkın aksine (ilçe alanı 1:1 eşleşiyor)
ve hava kalitesi/trafiğin aksine (ilçe sınırına karşı point-in-polygon testi). Bu yüzden
`HealthAccessIngestionJob` önce bir aggregation adımı yapıyor: her ilçenin mahallelerini
`ILCE_ADI`'ya göre grupluyor, sonra `SAGLIK_INDEX`'in **nüfus ağırlıklı ortalamasını**
hesaplıyor: `Σ(SAGLIK_INDEX_i × KISI_SAYISI_i) / Σ(KISI_SAYISI_i)`, nüfusu 0 olan mahalleler
hem pay hem paydadan hariç tutuluyor (ıssız/sanayi/orman bölgesi bir mahallenin endeksi,
ilçenin gerçek ortalamasını sulandırmasın diye).

**Kapsama testi (39 ilçenin tamamına karşı canlı doğrulandı):** 901 mahalle, tam olarak 39
farklı `ILCE_ADI` değeri altında toplanıyor — İBB'nin bu veri setinde İSPARK'ın
EYÜP/EYÜPSULTAN türünden bir isim uyuşmazlığı **yok**: her 39 ilçenin Türkçe büyük harfli adı
(`ToUpper("tr-TR")`) doğrudan bir `ILCE_ADI` değeriyle birebir eşleşiyor (bu veri seti zaten
"EYÜPSULTAN" kullanıyor, İSPARK'ın kullandığı eski "EYÜP" adını değil). Nüfus ağırlıklı
ortalama hesaplandığında **39/39 ilçenin tamamında** en az bir nüfuslu mahalle bulundu — hiçbir
ilçe "Veri yok" durumuna düşmedi (yine de kod, gelecekte bunun değişmesi ihtimaline karşı
0 nüfuslu-mahalle durumunu düzgünce ele alıyor, bkz. `HealthAccessIngestionJob`).

**Lisans:** İstanbul Büyükşehir Belediyesi Açık Veri Lisansı.

**Güncellik:** İBB'nin kendi CKAN API'sine göre (`data.ibb.gov.tr/api/3/action/package_show`)
bu GeoJSON kaynağı en son 2024-02-01'de değiştirildi — yeşil alan gibi yavaş değişen,
periyodik bir kaynak (`SourceCadence.Periodic`), canlı bir akış değil.

**Skor formülü:** İBB, `SAGLIK_INDEX`'in kendisinin 0-100 arası olduğunu hiçbir yerde iddia
etmiyor (yukarıya bkz.) — mahalle düzeyinde gerçek gözlemlenen aralık 0-82.9, ama ilçe
düzeyinde, nüfus ağırlıklı ortalama alındığında bu aralık daha da daralıyor. Canlı doğrulandı
(2026-09-13): gerçek 39 ilçenin nüfus ağırlıklı ortalamaları **10.30 (Şile, en düşük) ile
79.72 (Fatih, en yüksek)** arasında. MVP, bu *gözlemlenen* aralığı 0-100'e doğrusal olarak
ölçekliyor (en düşük ilçe 0, en yüksek ilçe 100 puan alır) — yeşil alanın yürüme mesafesi
varsayımıyla aynı üslupta, dürüstçe belgelenmiş bir MVP varsayımı, resmi bir standart değil
(bkz. `DimensionScoring.ScoreHealthAccess` içindeki yorum). İleride veri yeniden çekildiğinde
gerçek uç değerler değişirse, bu sabitlerin de güncellenmesi gerekir.

**Karar:** Bu kaynağı doğrudan kullan. `HealthAccessIngestionJob`, green space ile aynı
haftalık sıklıkla (yavaş değişen, yayınlanmış bir indeks) tüm mahalleleri çekip ilçe bazında
nüfus ağırlıklı ortalamaya indirgeyecek.

---

## 8. Toplu Taşıma Erişimi (İETT Otobüs Durakları) — ✅ Doğrulandı, 6. boyut

**Kaynak:** İBB'nin İETT "Otobüs Durakları Verisi" (şehir genelindeki gerçek İETT otobüs
duraklarının konumu).

**Endpoint:**
```
GET https://data.ibb.gov.tr/dataset/af3c70e8-82d6-44e2-84cf-2e364c242227/resource/4f28ec8d-7c2d-477b-873d-d17ce5b5e3be/download/iett-otobus-duraklar..geojson
```
- Kimlik doğrulama: **yok**
- Test tarihi: 2026-09-13. Yanıt: GeoJSON FeatureCollection, **15.486 Point feature** — her biri
  gerçek bir otobüs durağı, koordinat sırası `[boylam, enlem]` (İstanbul'un gerçek sınırlarına
  karşı doğrulandı: boylam ~28.6-29.46, enlem ~40.78-41.27). Gerçek property adları (canlı
  doğrulandı): `ID`, `ADI` (durak adı), `DURAK_KODU`, `DURUMU` (canlı doğrulandı: 15.486
  durağın **tamamında** "1" — kullanılabilir bir filtre değil), `DURAK_TIPI` (fiziksel durak
  tipi — bayrak durak, modern durak, cam durak vb.; hepsi gerçek, geçerli duraklar),
  `ILCEID` (**sayısal** bir ilçe kodu — isme resmi/güvenilir bir eşlemesi yok), `MAHALLEID`,
  `SON_GUNCELLEME_TARIHI`/`YAPILIS_TARIHI` (durak başına tarih, çoğu eski/durağan),
  `VERSIYON`, `CEP_VAR`.

**Diğer boyutlardan farkı — string eşleştirme yerine point-in-polygon:** Yeşil alan ve
otoparkın aksine, bu veri setinin `ILCEID` alanı sayısal bir kod ve resmi/güvenilir bir
isim eşlemesi yok (canlı doğrulandı — tahmin edilip kullanılmadı). Bu yüzden
`TransitAccessIngestionJob`, hava kalitesi ve trafiğin kullandığı **aynı point-in-polygon
tekniğini** kullanıyor: her durağın koordinatı, 39 ilçenin gerçek sınır poligonuna karşı
test ediliyor. 15.486 nokta × 39 ilçe (~600 bin test, trafiğin ~1.76 milyon satırından
~100 kat küçük) — canlı ölçüldü: TrafficIngestionJob'ın kullandığı hazırlanmış (prepared)
geometri tekniği bu ölçekte de aynen uygulanıyor (39ms'de tamamlanıyor; naif bir döngü bile
~4 saniyede biter, ama tutarlılık ve pay için aynı teknik tercih edildi). Sınırların hiçbiri
birbiriyle çakışmadığından bir durak en fazla bir ilçeye ait olabiliyor; hiçbir ilçe
sınırına düşmeyen duraklar (canlı doğrulandı: 15.486'nın 159'u, ~%1 — büyük olasılıkla kıyı/
deniz kenarı sınır hassasiyeti) hiçbir ilçeye zorla atanmadan sayılmıyor.

**Skor formülü — yoğunluk, ham sayı değil:** Büyük ve kırsal bir ilçe (örn. Çatalca,
~1.137 km²), sadece daha geniş olduğu için küçük ve merkezi bir ilçeden (örn. Beyoğlu,
~9 km²) her zaman daha fazla ham durak içerebilir — bu yüzden ham durak sayısını doğrudan
skorlamak ilçe büyüklüğünü ödüllendirir, toplu taşıma erişimini değil.
`TransitAccessIngestionJob`, her ilçenin gerçek fiziksel alanını WGS84 sınırından basit bir
equirectangular yaklaşıklıkla hesaplıyor (x = boylam(rad) × R × cos(ortalama enlem),
y = enlem(rad) × R, R = 6371.0088 km, ilçenin kendi centroid enlemi kullanılarak) ve
durak/km² yoğunluğunu skorlanan miktar olarak saklıyor (ham `StopCount` da şeffaflık için
ayrıca tutuluyor). Canlı nokta kontrolü (2026-09-13): bu yaklaşıklık, Çatalca/Silivri/Şile'yi
en büyük üç ilçe olarak buluyor (~1.137/859/782 km², gerçek yayınlanmış rakamlara göre
~1.073/883/757 km²) ve Güngören/Beyoğlu'nu en küçükler arasına koyuyor (~7.3/8.9 km², gerçek
rakamlara göre ~7.2/8.7 km²) — gerçek rakamlara birkaç yüzde yakın, hiçbir zaman büyüklük
mertebesinde yanlış değil, yani İstanbul'un küçük enlem aralığı için dürüst (ama yaklaşık)
bir alan, uydurma bir sayı değil.

**Kapsama testi (39 ilçenin tamamına karşı canlı doğrulandı):** Point-in-polygon eşleştirmesi
sonucunda **39/39 ilçenin tamamında** en az bir durak bulundu — hiçbir ilçe "Veri yok"
durumuna düşmedi (yine de kod, gelecekte bunun değişmesi ihtimaline karşı sıfır-durak
durumunu düzgünce ele alıyor, bkz. `TransitAccessIngestionJob`).

**Lisans:** İstanbul Büyükşehir Belediyesi Açık Veri Lisansı.

**Güncellik:** İBB'nin kendi CKAN API'sine göre bu GeoJSON kaynağı en son 2026-03-18'de
değiştirildi, ama fiziksel otobüs durağı altyapısı pratikte yavaş değişir — bu yüzden green
space/health access ile aynı haftalık sıklıkla (`SourceCadence.Periodic`) planlandı, canlı
bir akış gibi değil.

**Skor aralığı:** Canlı doğrulandı (2026-09-13): gerçek 39 ilçenin durak yoğunluğu **0.28
durak/km² (Çatalca, en düşük) ile 20.13 durak/km² (Şişli, en yüksek)** arasında. MVP, bu
*gözlemlenen* aralığı 0-100'e doğrusal olarak ölçekliyor (en düşük ilçe 0, en yüksek ilçe
100 puan alır) — sağlık erişiminin bounds'larıyla aynı üslupta, dürüstçe belgelenmiş bir MVP
varsayımı, resmi bir standart değil (bkz. `DimensionScoring.ScoreTransitAccess` içindeki
yorum).

**Karar:** Bu kaynağı doğrudan kullan. `TransitAccessIngestionJob`, health access/green space
ile aynı haftalık sıklıkla tüm durakları çekip point-in-polygon ile ilçelere eşleştirecek ve
durak/km² yoğunluğunu hesaplayacak.

---

## Özet Tablo

| Boyut | Kaynak | Canlı mı? | 39 ilçe kapsıyor mu? | Auth | Lisans | Karar |
|---|---|---|---|---|---|---|
| Hava kalitesi | api.ibb.gov.tr/havakalitesi | ✅ Saatlik | ❌ Yalnızca 18/39 (istasyonu olan) | Yok | İBB Açık Veri Lisansı | Doğrudan kullan; kalan 21 ilçe "Veri yok" |
| Yeşil alan | data.ibb.gov.tr GeoJSON | ⚠️ Yıllık | ✅ 39/39 | Yok | İBB Açık Veri Lisansı | Doğrudan kullan |
| Trafik | data.ibb.gov.tr CSV (Ocak 2025) | ❌ Bayat (~20 ay) | ✅ 38/39 (Adalar'da yol trafiği yok) | Yok | İBB Açık Veri Lisansı | Kullan, UI'da "tarihsel" etiketiyle |
| Otopark | api.ibb.gov.tr/ispark | ✅ Canlı | ❌ Yalnızca 34/39 (tesisi olan) | Yok | İBB Açık Veri Lisansı | Doğrudan kullan; kalan 5 ilçe "Veri yok" |
| Sağlık erişimi | data.ibb.gov.tr GeoJSON (mahalle bazlı) | ⚠️ Durağan (son değişiklik 2024-02-01) | ✅ 39/39 (nüfus ağırlıklı ortalamayla) | Yok | İBB Açık Veri Lisansı | Doğrudan kullan; mahalleden ilçeye aggregate et |
| Toplu taşıma erişimi | data.ibb.gov.tr GeoJSON (İETT durakları) | ⚠️ Durağan (son değişiklik 2026-03-18) | ✅ 39/39 (point-in-polygon ile) | Yok | İBB Açık Veri Lisansı | Doğrudan kullan; durak/km² yoğunluğunu skorla |
| İlçe sınırı | OpenStreetMap/Nominatim | N/A (statik seed) | ✅ 39/39 | Yok (rate-limit'li) | ODbL (atıf gerekli) | Seed-time'da çek, sakla |
