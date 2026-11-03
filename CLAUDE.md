@AGENTS.md

## Claude Code notu
- AGENTS.md içindeki "Independent technical review workflow" bölümü Codex'e özeldir ve Claude Code için GEÇERSİZDİR. `agy`/Gemini incelemesi çalıştırma, bunu şart koşma.
- AGENTS.md'deki diğer tüm mühendislik kuralları, Docs/ workflow'u ve runtime kısıtları aynen geçerlidir.
- Unity Editor'ü çalıştıramazsın, derleme hatalarını göremezsin; hata olursa kullanıcı Console çıktısını verir.
- Dosya taşıma/silme işlemlerinde .meta dosyalarını birlikte yönet, GUID'leri değiştirme.
- .unity ve .prefab dosyalarını doğrudan düzenleme; sadece oku. Bu değişiklikler Editor'de yapılır.
