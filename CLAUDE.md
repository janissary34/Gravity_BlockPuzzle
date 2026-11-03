@AGENTS.md

## Claude Code notu
- AGENTS.md içindeki "Independent technical review workflow" bölümü Codex'e özeldir ve Claude Code için GEÇERSİZDİR. `agy`/Gemini incelemesi çalıştırma, bunu şart koşma.
- AGENTS.md'deki diğer tüm mühendislik kuralları, Docs/ workflow'u ve runtime kısıtları aynen geçerlidir.
- Unity Editor'ü çalıştıramazsın, derleme hatalarını göremezsin; hata olursa kullanıcı Console çıktısını verir.
- Dosya taşıma/silme işlemlerinde .meta dosyalarını birlikte yönet, GUID'leri değiştirme.
- .unity ve .prefab dosyalarını doğrudan düzenleme; sadece oku. Bu değişiklikler Editor'de yapılır.

## Proje özeti
- Unity 2022.3.62f2, URP 14 (2D), DOTween `Assets/Plugins/Demigiant`, legacy Input Manager.
- Giriş `Splash.unity`; her level `Scene1` yeniden yüklenerek başlar (`SplashSceneLoader`). Level index yalnızca statik bellekte (`GravityLevelRuntime`), PlayerPrefs'e yazılmaz.
- Composition root: `Bootstrap/RuntimePieceFactoryBootstrap.Awake`. `PrototypeBootstrap.cs` = statik yardımcılar + `PrototypeBoard` (grid işlemleri, timer, bomb, win/fail, sahne geçişi).
- Tick sahipleri: `PuzzleDragController.Update` (input, map-exit capture) ve `FixedUpdate` (grid gravity); `BlockShredder.FixedUpdate` (freefall → cutter); `PrototypeBoard.Update` (timer, win/fail).
- Sahiplik ve invariant'lar: önce `Docs/Architecture.md`, `Docs/Presentation.md`, `Docs/Decisions/`.

## Klasör haritası (gerçek durum)
- `Assets/Scripts/` kökü: taşınmamış büyük sınıflar — `PuzzlePiece` (~4.5k satır), `PrototypeBootstrap` (~3.2k), `PuzzleDragController`, `BlockShredder`, booster'lar, HUD.
- `Level/`: level tanımı + statik `GravityLevelRuntime` (level inşası, kampanya durumu).
- `Gameplay/Reveal`, `Gameplay/Tutorial`: AGENTS.md layout'unda olmayan ek klasörler.
- `Assets/Editor/` authoring menüleri; `Assets/Tests/Editor`, `Assets/Tests/PlayMode`.
- Oyun kodu için asmdef yok (`Assembly-CSharp`).

## Doğrulanmış tuzaklar
- `Packages/com.ardacelik.game-foundation` Assets'ten hiç kullanılmıyor. Oyun `Infrastructure/Pooling`, `Core/StateMachine`, `UISafeArea` kopyalarını kullanır; doğru kopyayı düzenle.
- `Core/Events` kanalları ve `Gameplay/Gravity/GridGravityPlanner` kullanılmıyor. Gerçek gravity planlayıcısı `PuzzleDragController.TryCommitIndependentGravityGroup`.
- `BlockPiece.prefab` composite'i Outlines; spawn'da `RuntimePieceFactory.ConfigureComposite` Polygons'a çevirir. Prefab değerine güvenme.
- `ShredderPhysicsFreefall` (layer 9) yalnız kendisiyle çarpışır; freefall'da engel/harita teması yok (ADR-005, kasıtlı).
- Joint'ler map-exit'te değil, ancak cutter capture'da kırılır (`BlockShredder.TryCaptureFreefallPieceAtCutter`).
- `RuntimePieceFactoryBootstrap` ve `PrototypeBoard` içinde `#if UNITY_EDITOR` `AssetDatabase` fallback'leri var; Editor'de çalışan referans build'de null olabilir.
- Script execution order'lar 0 (yalnız `TweenBootstrap` -1000). Farklı nesnelerin Awake/OnEnable sırasına güvenme.
- Statik erişimciler (`PrototypeBoard.Active`, `*.Instance`, `BoosterInventoryRuntime`, `PuzzlePiece.ActivePieces`, statik `*Pool`) mevcut borçtur; yenisini ekleme.

## Komutlar
- Testler: Editor > Window > General > Test Runner (EditMode `Assets/Tests/Editor`, PlayMode `Assets/Tests/PlayMode`).
- Level ekleme/büyütme sonrası: `Gravity Puzzle/Level Authoring/Rebuild Level Runtime Root`.
- Box preview kapasitesi değişince: `Gravity Puzzle/Art/Refresh Box Reveal Preview Slots`.
