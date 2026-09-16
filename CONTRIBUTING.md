# Katkı Sağlama

SemtSkoru açık kaynak bir proje; hata bildirimleri, veri kaynağı düzeltmeleri ve kod katkıları memnuniyetle karşılanır.

## Hata bildirme / özellik önerisi

Yeni bir [issue](https://github.com/mehmetoya/SemtSkoru/issues) açın. Hata bildirirken şunları eklemek çözümü hızlandırır: hangi sayfa/endpoint, beklenen ve gerçekleşen davranış, mümkünse ekran görüntüsü. Bir veri kaynağının değiştiğini/bozulduğunu düşünüyorsanız (örn. İBB bir endpoint'i kaldırdı), ilgili canlı URL'i de ekleyin.

## Geliştirme ortamı

Kurulum adımları için [`README.md`](README.md#kurulum-yaklaşık-5-dakika)'ya bakın (~5 dakika, .NET 10 + Node.js + Docker).

## Değişiklik gönderme

1. Fork'layıp bir feature branch açın.
2. Değişikliğinizi yapın — aşağıdaki ilkelere bakın.
3. İlgili testleri çalıştırın (bkz. aşağı) ve hepsinin geçtiğinden emin olun.
4. PR açın; PR açıklamasında neyi neden değiştirdiğinizi kısaca anlatın.

CI (build + test) her PR'da otomatik çalışır; yeşil olmadan merge edilmez.

### Testler

```bash
# Backend: unit + Testcontainers ile gerçek Postgres+PostGIS'e karşı integration
cd backend && dotnet test

# Frontend: component testleri
cd web && npm test

# Frontend: tip kontrolü + lint
cd web && npx tsc --noEmit && npm run lint
```

Yeni bir scoring boyutu, endpoint veya ingestion job'ı ekliyorsanız test eklemeniz beklenir — mevcut `tests/` altındaki dosyalar örnek alınabilir.

### Kod ilkeleri

Bunlar bu projenin kimliğinin bir parçası, PR review'da öncelikle bunlara bakılır:

- **Hiçbir veri uydurulmaz.** Bir boyut için gerçek veri yoksa arayüzde dürüstçe "Veri yok" gösterilir — tahmin, enterpolasyon veya varsayılan değerle doldurma yapılmaz. AI özellikleri (Asistan, ilçe özeti) yalnızca gerçek skorlara dayanır, kendi bilgisinden İstanbul hakkında bir şey uydurmaz.
- **Her veri kaynağı canlı doğrulanır.** Kod yazmadan önce gerçek bir HTTP isteğiyle endpoint/format/lisans kontrol edilir (bkz. [`docs/data-sources.md`](docs/data-sources.md)) — dokümantasyona veya varsayıma güvenilmez.
- **Backend Clean Architecture katmanlarına uyar:** Domain hiçbir dış bağımlılığa sahip değildir; Application, Domain'e bağımlıdır; Infrastructure dış kaynaklara (DB, API'ler) erişir; Api yalnızca bunları birbirine bağlar. Bağımlılık yönü tersine çevrilmez.
- **Frontend asla dış veri kaynağına doğrudan istek atmaz** — yalnızca backend API'sini tüketir.
- Commit mesajları kısa, "neden" odaklı ve conventional-commit tarzı önekler kullanır (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `perf:`) — bkz. `git log`.

## Lisans

Katkılarınız, projenin [MIT lisansı](LICENSE) altında yayınlanır.
