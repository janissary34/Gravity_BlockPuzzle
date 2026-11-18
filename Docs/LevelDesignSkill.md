# Mobile Puzzle Level Design — AI Skill (v1.0.0)

> **Kaynak & Standart:** Dahili seviye tasarımı, zorluk eğrisi, mekanik müfredatı ve oturum mimarisi referans dokümanı.
> Tam skill metni: `.claude/skills/mobile-puzzle-level-design/SKILL.md`. Bu dosya özet + projeye eşlemedir; sayısal hedefler HYPOTHESIS'tir, ölçülmüş değer değildir.

---

## 1. Temel İşletim Sözleşmesi (Operating Contract)
Bulmaca seviyesi tasarlarken tahtayı rastgele doldurmak yerine şu sıra izlenir:
1. **Ürün Sınıflandırması:** FAST (kısa seviyeler, hızlı akış), STAGED (tek seviye içinde birden fazla alt aşama), LONG (uzun taktiksel seviyeler) veya HYBRID.
2. **6 Mekanik Katman:** Hedef, Kaynak/Kuyruk, Erişim/Görünürlük, Kapasite, Kısıtlar ve Kurtarma (Rescue).
3. **Ölçülebilir Hedef Eğrisi:** Temiz kazanma süresi (clean run), ilk deneme kazanma oranı (first-attempt win rate), tekrar deneme yükü (retry burden).
4. **Müfredat Planı:** Her yeni mekanik için `TANIT (Introduce) → PRATİK (Practice) → VARYASYON (Vary) → KOMBİNASYON (Combine) → TEST (Mastery) → DİNLENME (Rest)`. Yeni mekanik tanıtımı asla ani zorluk sıçramasıyla (Hard/Super Hard) birleştirilmez.
5. **Sawtooth (Testere Dişi) Zorluk Eğrisi:** Düz doğrusal artış yerine; `Kolay → Orta → Zor → Rahatlama → Yeni Varyasyon → Çok Zor → Rahatlama`.

---

## 2. Altı Mekanik Katman (Mechanical Layers)
| Katman | Tasarımcı Sorusu | Örnek Parametreler |
| :--- | :--- | :--- |
| **Hedef (Goal)** | Seviyeyi ne temizler? | Hedef blok sayısı, renk hedefleri, parçalanan birimler |
| **Kaynak (Supply)** | Ne seçilebilir ve hangi sırayla gelir? | Tahtadaki parçalar, kutu/tünel içerikleri, parça şekilleri |
| **Erişim (Access)** | Hangi parçalar görünür/hareket ettirilebilir? | Öncelik sırası, kilitler, engel arkasında kalanlar, kutu kapakları |
| **Kapasite (Capacity)** | Oyuncu nerede manevra yapabilir? | Izgara boyutları ($W \times H$), darboğazlar, serbest hareket alanı |
| **Kısıtlar (Constraints)** | Seçimi ne tehlikeli kılar? | Süre sayacı (Timer), Bomba geri sayımı, Buz vuruş sayısı, Yay/Joint bağları |
| **Kurtarma (Rescue)** | Hata nasıl geri alınabilir / çözülebilir? | Freeze Timer (zamanı durdurma), Hammer (tekil blok kırma), Rocket (hat temizleme), Revive/Play On (+60s) |

---

## 3. Süre Bantları (Duration Targets)
* **FAST:**
  * L1–5: 15–35s (çekirdek eğlenceyi hızlıca yaşat)
  * L6–15: 30–65s (öğrenme ve temel ustalık)
  * L16–30: 45–90s (ilk stratejik baskı)
  * L31–50: 60–110s (ilk derinlik ve kombinasyonlar)
  * L51–100: 70–140s (olgun döngü)

---

## 4. Yeni Mekanik Tanıtımı: Görsel Baskınlık ≠ Zorluk
Yeni bir özellik tanıtılırken kural:
* Özellik **ekranda bolca yer kaplamalı ve görsel olarak baskın olmalı**, fakat **bilişsel ve kural baskısı en düşük seviyede** tutulmalıdır.
* Örneğin, Buz Blok veya Bomba ilk kez geldiğinde süre çok cömert olmalı, tahtadaki manevra alanı geniş tutulmalı, oyuncunun yeni mekaniği risksiz denemesi sağlanmalıdır.

---

## 5. Gravity BlockPuzzle İçin Eşleme

### Ürün sınıfı
* **FAST + timer.** Level = tek tahta (stage yok). Süre `GravityLevelDefinition.timeLimit`; şu an tüm level'larda 300s.
* Kurtarma: Freeze Timer, Hammer, Rocket booster'ları (level başına sayı `rocket/hammer/timerBoosterCount`).
* Zorluk etiketi: `LevelDifficulty { Normal, Hard, VeryHard }` — yalnız sunum metadatası, kuralı değiştirmez.

### Tanıtım seviyeleri (kaynak: `Assets/Config/Boosters/*RewardConfig.asset` → `presentationLevel`)
Seviye numarası = `LevelSequence` sırası + 1 (`GravityLevelRuntime.CurrentLevelNumber`), dosya adı değil.

| Seviye | Özellik | Tür |
| :--- | :--- | :--- |
| 1–3 | Sürükleme + gravity + shredder | Çekirdek loop |
| 4 | Freeze Timer | Booster |
| 6 | Hammer | Booster |
| 8 | Ice Block (`PieceSpecialBlockType.Ice`) | Engel / dayanıklılık |
| 10 | Rocket | Booster |
| 15 | Bomb Block (`PieceSpecialBlockType.Bomb`) | Aciliyet kısıtı |
| 25 | Elevator reveal | Erişim / reveal |
| 40 | Box reveal | Erişim / reveal |

### Mevcut durumun skill'e göre değerlendirmesi (2026-11 itibarıyla)
* **Reveal sıralama uyumsuzluğu:** `elevator_level` sekansta 17. sırada, `box_level` 23. sırada; ödül/tanıtım popup'ları ise 25 ve 40'ta. Oyuncu mekaniği tanıtımından 8–17 level önce görüyor. Ya level'lar taşınmalı ya `presentationLevel` düzeltilmeli.
* **Test level'ı sekansta:** `visual_test_level` 31. sırada (ice+bomb+box birlikte). Oyuncu akışından çıkarılmalı.
* **Zorluk eğrisi yok:** Hiçbir level `Hard`/`VeryHard` etiketli değil. Skill önerisi: ilk Hard ~20–25, ilk VeryHard ~29–30, sonrasında 10 level'da ~1 Hard + 1 VeryHard, her zirveden sonra rahatlama level'ı.
* **Timer baskısı yok:** Tüm level'lar 300s. Kısa FAST level için bu süre pratikte kısıt üretmiyor; Freeze Timer booster'ının öğrettiği sorun oyunda hissedilmiyor. Süre, hedef clean-run'ın ~1.5–2.5 katına göre level bazında ayarlanmalı (önce ölç).
* **Tanıtım sıklığı:** 4/6/8/10 aralığı çok sık (iki level'da bir yeni şey); skill L1–15 için ~3–6 level öneriyor ve booster tanıtımından sonra bir booster'sız pekiştirme level'ı istiyor.
* **Bomb (15) sonrası:** 16, 17, 19'da tekrar var, iyi. Ice (8) sonrası yalnız 9'da tekrar edip 29'a kadar kayboluyor; arada pekiştirme yok.
* **Telemetri yok:** clean_run_sec, ilk deneme kazanma, kayıp anındaki ilerleme ölçülmüyor; skill'deki hedefler kalibre edilemez.
