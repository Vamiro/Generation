# Архитектура системы генерации FPS-карт

> Технический дизайн-документ. Описывает **текущее** состояние проекта.
> При изменении кода — обновлять этот документ. Если документ расходится с кодом — приоритет у кода, и нужно править документ.
>
> Связанные источники:
> - `Docs/ВКР Погиба В.С..md` — текст ВКР (что и зачем).
> - `.cursor/rules/thesis-context.mdc` — короткая сводка MVP и roadmap.

---

## 1. Цель проекта

Экспериментальная платформа для исследования зависимости **параметров процедурной генерации FPS-карт** от **баланса** матчей. Не игра.

Платформа должна:

1. Генерировать карты по конфигурации (sid + параметры → детерминированная карта).
2. Запускать симуляции матчей 5×5 (атакующие против защитников) с ботами.
3. Собирать метрики (win-rate, длительность раунда, координаты смертей).
4. Позволять корреляционный анализ: какие параметры карты как влияют на исходы матчей.

---

## 2. Высокоуровневая архитектура

```
┌─────────────────────────────────────────────────────────────┐
│                        GameManager                          │
│  (round timer, restart, метрики смертей, time scale)        │
└──────────┬──────────────────────────────────┬───────────────┘
           │                                  │
           ▼                                  ▼
┌─────────────────────┐         ┌─────────────────────────────┐
│   MapGenerator      │         │     TeamManagers            │
│   (partial classes) │         │  AttackerTeamManager        │
│                     │         │  DefenderTeamManager        │
│  → cellTypes[,]     │         │      ↓ управляют            │
│  → floorInstances   │         │     BotComponent ×10        │
│  → BuildOuterWalls  │         │   (5 атак + 5 защ)          │
│  → PlaceCovers      │         └─────────────┬───────────────┘
└──────────┬──────────┘                       │
           │ регистрирует                     │ опрашивает
           ▼                                  ▼
┌─────────────────────────────────────────────────────────────┐
│                        MapManager                           │
│  Список MapZoneComponent → SpawnZones / SiteZones /         │
│       NeutralZones / RoadZones (по типу через GetZonesOf<>) │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                     Storage / StorageData                   │
│   JSON-сериализация persistentDataPath/Storage/*.json       │
│   Сейчас сохраняет: DeathData (координаты смертей)          │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Структура файлов

```
Assets/Scripts/
├── Core/
│   ├── GameManager.cs           — раунд, тайминги, перезапуск, метрики
│   ├── MatchManager.cs          — спавн/остановка матча по хоткеям W/S  ← NEW
│   ├── Storage.cs               — JSON I/O в persistentDataPath
│   └── StorageData.cs           — обобщённая база для сохраняемых данных
│
├── Map/
│   ├── BlockComponent.cs        — enum BlockType + класс блока пола/стены
│   ├── MapManager.cs            — реестр зон по типам (Singleton)
│   ├── MapZoneComponent.cs      — базовый класс зоны (веса для ролей, sample points)
│   ├── MapGenerator.cs                ★ главный partial: поля, GenerateMap, материалы, walls
│   ├── MapGenerator.Layout.cs         ★ partial: PlaceSpawnZones, PlaceSiteZones, MarkNeutralZone, BuildStructuredRoutes
│   ├── MapGenerator.Pathfinding.cs    ★ partial: A* для прокладки дорог, PaintPathBrush
│   ├── MapGenerator.ZoneShaping.cs    ★ partial: обводка зон стенами и сужение входов в choke points
│   ├── MapGenerator.RuntimeZones.cs   ★ partial: извлечение регионов, инстанцирование MapZoneComponent
│   ├── MapGenerator.Validation.cs     ★ partial: проверки достижимости (BFS)
│   ├── MapGenerator.Rooms.cs          ★ partial: PlaceRooms() — галереи вдоль main-дорог  ← NEW
│   ├── MapGenerator.NavMesh.cs        ★ partial: RebuildNavMesh() — runtime-бейк на Geometry  ← NEW
│   ├── MapGenerator.Covers.cs         ★ partial: расстановка укрытий (отключена, в работе)
│   └── Zones/
│       ├── SpawnZoneComponent.cs      — пустой маркер
│       ├── SiteZoneComponent.cs       — пустой маркер
│       ├── NeutralZoneComponent.cs    — пустой маркер
│       ├── RoomZoneComponent.cs       — пустой маркер (галереи)  ← NEW
│       └── RoadZoneComponent.cs       — roadType (Main/Link), roadToSite
│
├── Bot/
│   └── BotComponent.cs          — NavMeshAgent, простая стрельба по LOS, AssignRole, Die
│
├── Team/
│   ├── TeamManager.cs           — базовый класс команды (boss bot list + ссылка на OtherTeam)
│   ├── AttackerTeamManager.cs   — распределяет роли Attacker/Flanker/Scout, ротация при потерях
│   └── DefenderTeamManager.cs   — распределяет 2+2 на сайты + 1 Scout, периодический reposition
│
├── Utilities/
│   ├── Singleton.cs             — generic singleton для plain C# классов
│   ├── MonoSingleton.cs         — generic singleton для MonoBehaviour
│   └── ReadOnlyInInspectorAttribute.cs — read-only поле в инспекторе
│
└── Editor/
    ├── ReadOnlyInInspectorDrawer.cs   — рендер атрибута выше
    └── StorageEditor.cs               — UI для Storage
```

---

## 4. Модуль: Map Generation

Главный модуль. Один класс `MapGenerator` разнесён на partial-файлы по функциям.

### 4.1 Структуры данных

**`BlockType`** (`BlockComponent.cs`):
```
Floor, Wall, Empty, None,
Spawn, Site, Neutral, Main, Link, Road, Room
```

- `Empty` — клетка вне игровой области (нет пола, не помечена). При построении карты — стартовое состояние всех клеток.
- `Floor` — общий пол (сейчас не используется напрямую, всегда один из специализированных типов).
- `Wall` — стена. Строится колонной на `outerWallHeight` блоков вверх.
- `Spawn/Site/Neutral` — игровые зоны.
- `Main/Link` — дороги двух типов. Main вдоль периметра карты, Link через нейтральную зону.
- `Room` — галерея/комната вдоль main-дороги. Защищена от перезаписи так же, как Spawn/Site/Neutral.
- `Road` — зарезервирован, не используется.

**Приоритеты `BlockData.Set` / `TryMarkBlock`:**
`Spawn`, `Site`, `Neutral` нельзя перезаписать типами `Main`, `Link`, `Road`, `Room`.

**`BlockData`** — приоритетная замена типа:
- `Spawn/Site/Neutral` нельзя перезаписать на `Main/Link/Road` (защищает зоны от прокладки дорог через них).

**Внутренние поля `MapGenerator`:**

```csharp
BlockType[,]      cellTypes          // логическая сетка (источник истины)
BlockComponent[,] floorInstances     // лениво создаваемые GameObject-ы пола
int[,]            cellWeights        // веса клеток (для A*)
List<List<Vector2Int>> mainRoadPaths    // все main-пути (attacker + defender), для PlaceRooms
List<List<Vector2Int>> attackerMainPaths // main-пути атакующего, для branch-точек link
List<List<Vector2Int>> defenderMainPaths // main-пути защитника, для branch-точки link
List<List<Vector2Int>> linkPaths         // link/mid пути, для link-кубби
Dictionary<BlockType, HashSet<BlockComponent>> zoneBlocks // быстрый доступ к клеткам зоны
```

**Инвариант:** менять `cellTypes` напрямую можно только в `BuildOuterWalls`. Везде ещё — через `TryMarkBlock(x, z, type, weight?, trackZone?)`. Эта функция отвечает за:
- Проверку приоритетов (Spawn/Site/Neutral vs Main/Link).
- Удаление/создание floor instance.
- Поддержание `zoneBlocks` в актуальном виде.

### 4.2 Пайплайн генерации (`MapGenerator.GenerateMap`)

```
[0] InitializeContainers             — создаёт Geometry/ и Zones/ как children генератора
[1] InitializeZoneCollections        — пустые сеты для zoneBlocks
[2] InitializeCellGrid               — все клетки = Empty, инстансов нет
[3] MarkZones                        — рисуем зоны и дороги (пути main сохраняются в mainRoadPaths):
    ├── PlaceSpawnZones              ──→ Spawn x2 (атакующий сверху, защитник снизу)
    ├── PlaceSiteZones               ──→ Site A слева, Site B справа
    ├── MarkNeutralZone              ──→ Neutral в центре
    └── BuildStructuredRoutes
        ├── BuildMainRoutes          ──→ A* spawn↔site, тип Main
        ├── BuildAttackerLinks       ──→ A* attackerSpawn↔neutralCenter, тип Link
        ├── BuildDefenderLink        ──→ A* defenderSpawn↔neutralCenter, тип Link
        └── BuildNeutralToSiteLinks  ──→ A* neutralCenter↔siteEntry, тип Link
[3.5] PlaceRooms                     — галереи вдоль main-дорог (из mainRoadPaths)
[4] ShapeZoneEnclosures              — для каждой Site/Neutral/Spawn/Room:
                                       сужает дорожные «выходы» до maxEntranceWidth клеток,
                                       лишнее → Wall или префаб укрытия
[5] ValidateGeneratedLayout          — BFS: spawn→site→neutral достижимы?
[6] BuildAndRegisterZoneObjects      — флуд-филл регионов в RuntimeZones,
                                       создаёт MapZoneComponent в Zones/ + регистрирует в MapManager
[7] UpdateMap                        — выставляет материалы полам
[8] BuildOuterWalls                  — расширение Empty-границы вокруг floor на outerWallThickness;
                                       все Wall-клетки → колонны блоков высотой outerWallHeight в Geometry/
[9] PlaceCovers (если enableCovers)  — расстановка укрытий внутри зон в Geometry/
[10] RebuildNavMesh                  — runtime-бейк NavMeshSurface на Geometry/  ← NEW
```

**Иерархия объектов карты (children генератора):**

```
MapGenerator
 ├── Geometry/        ← вся физика: пол, стены, укрытия. Здесь же висит NavMeshSurface.
 └── Zones/           ← MapZoneComponent (Spawn/Site/Neutral/Road/Room/Pocket). Триггер-коллайдеры.
```

Разделение позволяет:
- NavMesh бейкать только по реальной геометрии (`NavMeshSurface.collectObjects = Children`).
- Зоны иметь триггер-коллайдеры для bounds — они не блокируют ботов.
- При R-регенерации оба контейнера уничтожаются вместе с children и создаются заново.

### 4.3 Слой Layout (`MapGenerator.Layout.cs`)

Размечает зоны и дороги.

**Структура путей (после изменений):**

```
BuildMainRoutes
  ├── attackerSpawn → siteA  (→ attackerMainPaths[0])
  ├── attackerSpawn → siteB  (→ attackerMainPaths[1])
  ├── defenderSpawn → siteA  (→ defenderMainPaths[0])
  └── defenderSpawn → siteB  (→ defenderMainPaths[1])

BuildAttackerLinks
  └── [attackerMainPaths[i][branchIdx]] → neutralCenter  (ответвление, аналог mid на Valorant)
      branchIdx = attackerLinkBranchFraction * path.Count  (по умолчанию ~33%)

BuildDefenderLink
  └── [defenderMainPaths[rand][branchIdx]] → neutralCenter
      branchIdx = defenderLinkBranchFraction * path.Count  (по умолчанию ~70% = ближе к сайту → короткий mid)

BuildNeutralToSiteLinks
  ├── neutralCenter → edge(siteA)
  └── neutralCenter → edge(siteB)
```

**Органичность дорог (waypoints):**

Вместо прямого пути spawn→site используется многосегментный подход:

```
spawn → [waypoint₁ ⊥offset] → [waypoint₂ ⊥offset] → site
```

Каждый waypoint = точка на прямой линии spawn→site, смещённая перпендикулярно на случайную величину `mainWaypointOffset`. A* решает каждый сегмент отдельно — форма «изгиба» задаётся через цель, а не через cost-функцию. Параметры: `mainWaypointCount` (0–2), `mainWaypointOffset` (клеток), `linkWaypointCount` (0–1), `linkWaypointOffset`.

Размечает зоны и дороги.

Ключевые параметры в инспекторе:
- **Размеры**: `width`, `height`, `blockSize`, `innerPadding`.
- **Зоны (min/max — независимый рандом по X и Z)**:
  - `spawnZoneSizeMin/Max`
  - `siteZoneSizeMin/Max`
  - `neutralZoneSizeMin/Max`
- **Позиционирование зон**: `spawnHorizontalOffset`, `siteLineBiasToDefender` (сайты ближе к защитникам), `siteVerticalJitter`, `siteCenterDriftMax`, `siteMinCenterDistanceMultiplier`.
- **Нейтральная зона**: `generateNeutralZone`, `neutralZoneBiasToSites`, `neutralZoneHorizontalJitter`, `neutralZoneVerticalJitter`.
- **Форма зон**: `zoneShapeMode` (Square / Brush), `useCircularZoneBrush`.

`PaintZoneByRect(startX, startZ, sizeX, sizeZ, type)` — единая точка для отрисовки зоны: либо квадрат (Square), либо вписанный эллипс/квадрат-кисть (Brush).

### 4.4 Слой Pathfinding (`MapGenerator.Pathfinding.cs`)

A* с учётом штрафов и предпочтений.

Поведение:
- **Может проходить через `Empty`** — это нужно, чтобы дорога прокладывалась сквозь пустоту. Нельзя проходить через `Wall`.
- Штрафы: `astarTurnPenalty`, `astarRoadReusePenalty`, `astarCrossTypePenalty`, `astarBorderPenalty`, `astarLinkCenterPenalty`, `astarMainOuterBias`, `astarRandomJitter`.
- Тай-брейк для предпочтительного направления: `astarMainHorizontalBias` (main любит горизонталь), `astarLinkHorizontalBias` (link любит вертикаль).

`PaintPathBrush(x, z, width, type, weight?)` рисует дорогу с заданной шириной (квадратная или круглая кисть по `useCircularPathBrush`).

### 4.5 Слой ZoneShaping (`MapGenerator.ZoneShaping.cs`)

После прокладки дорог сайты/нейтральная/спавны открыты со всех сторон. Этот этап:

1. Для каждой Spawn/Site/Neutral региона перебирает граничные клетки.
2. Группирует подряд идущие клетки-«потенциальные входы» (где снаружи Main или Link) по сторонам и **по типу дороги** (Main и Link отдельно).
3. Если сегмент длиннее `maxEntranceWidth` — обрезает излишки:
   - `entranceFillMode = Wall` → лишние клетки превращаются в Wall.
   - `entranceFillMode = Cover` → ставится префаб укрытия (если `coverPrefab` назначен).

Параметры:
- `enableZoneEnclosures`, `maxEntranceWidth`, `entranceFillMode`.

### 4.6 Слой Validation (`MapGenerator.Validation.cs`)

Проверки после полной разметки:
- Существуют клетки Main и Link.
- Site не упирается в самый край карты.
- Из каждого спавна достижим каждый сайт (BFS).
- Из каждого спавна достижима нейтральная зона.
- Из нейтральной зоны достижим каждый центр сайта.

Использует `IsWalkableCell` (не Wall и не Empty) — то есть валидация смотрит на финальную геометрию.

### 4.7 Слой RuntimeZones (`MapGenerator.RuntimeZones.cs`)

После разметки:
1. Для каждого типа из `RuntimeZoneTypes` извлекает связные регионы через flood-fill.
2. Сортирует регионы (стабильный порядок: сначала по типу, потом по координатам).
3. Для каждого региона создаёт GameObject с подходящим компонентом (`SpawnZoneComponent` / `SiteZoneComponent` / `RoadZoneComponent` (+ roadType) / `NeutralZoneComponent` / `RoomZoneComponent`).
4. **Декомпозирует** регион на минимальный набор axis-aligned прямоугольников (`DecomposeRegionIntoRectangles`, greedy) и для каждого вешает отдельный `BoxCollider` (`isTrigger = true`). Это даёт изогнутой дороге несколько компактных сегментов-коллайдеров вместо одного огромного AABB над пустотой. Прямая дорога шириной N → 1 прямоугольник; изогнутая L-образная → 2; зона с круглой кистью → ступенчатая декомпозиция.
5. Заполняет `samplePoints` (мировые координаты центров каждой клетки региона) — нужны ботам для `GetRandomPointInZone()`. В отсутствие sample-points fallback выбирает случайный сегмент-коллайдер, взвешенный по площади.
6. Регистрирует в `MapManager`.
7. **`AssignRoadTargets`**: каждой дороге назначается ближайший сайт (`roadToSite`). Если для какого-то сайта нет дорог конкретного типа — fallback на ближайшую любого типа.

**Инвариант:** один `MapZoneComponent` = одна логическая зона, но **может содержать несколько `BoxCollider`-сегментов** (`BoxColliders` — `IReadOnlyList<BoxCollider>`). Триггер по любому из них означает попадание в зону. `RoomZoneComponent` для комнат/карманов на дороге — отдельные зоны (комната = свой flood-fill регион типа `Room`/`Pocket`), их коллайдеры не сливаются с дорогой.

### 4.8 Слой Rooms (`MapGenerator.Rooms.cs`)

`PlaceRooms()` размещает комнаты двух типов вдоль main-дорог:

**Тип А — Gallery (25%–65% пути):** прямоугольная выпуклость перпендикулярно дороге. Ломает длинный sight-line на main-коридоре. Аналог mid-галерей.

**Тип Б — Pre-site room (70%–88% пути):** небольшая комната у входа в сайт. Staging area для атакующих, off-site hold для защитников. Аналог Hookah/Showers в Valorant.

Параметры: `enableRooms`, `roomsPerMainRoad`, `roomSizeMin/Max`, `roomOffsetFromRoad`, `enablePreSiteRooms`, `preSiteRoomSizeMin/Max`.

Инварианты `CanPlaceRoom`: комната не может перекрывать Spawn/Site/Neutral/Wall, и должна быть минимум `outerWallThickness` клеток от края карты (гарантия места для внешней стены).

### 4.8.1 Слой NavMesh (`MapGenerator.NavMesh.cs`)

`RebuildNavMesh()` запускается последним шагом `GenerateMap()`. На `Geometry/` лениво вешается `NavMeshSurface` (`Unity.AI.Navigation`) с настройками:

- `collectObjects = Children` — собирает только children контейнера Geometry.
- `useGeometry = PhysicsColliders` — бейк по коллайдерам пола/стен/укрытий.
- `agentTypeID` — параметр в инспекторе (по умолчанию 0 = Humanoid). Должен совпадать с `NavMeshAgent.agentTypeID` у `Bot.prefab`.

Параметры:
- `buildNavMesh` (bool) — выключить можно в дебаг-целях.
- `navMeshAgentTypeId` — int, как в Window/AI/Navigation/Agents.

При R-регенерации контейнер `Geometry/` уничтожается вместе с `NavMeshSurface`, и при следующей генерации компонент пересоздаётся. `MatchManager.StartLoop()` отказывается стартовать, если в сцене нет ни одной NavMesh-триангуляции (цикл сразу выключается с ошибкой в лог).

### 4.9 Слой Covers (`MapGenerator.Covers.cs`)

**Сейчас отключён** (`enableCovers = false` по умолчанию). Будет переписан после финализации комнат (см. roadmap).

Параметры (могут меняться):
- `enableCovers`, `coverableZones` (Site / Neutral / Spawn), `coverHeight`, `coverMaxFillRatio`, `coverMaxPerZone`, `coverMinSpacing`, плюс параметры конкретного алгоритма.

### 4.10 Внешние стены (`MapGenerator.cs`)

`BuildOuterWalls()` — трёхфазный алгоритм:

**Фаза 1 — Контурный обход:** для каждой Floor-клетки (не Empty, не Wall) смотрим 4 соседа. Если сосед Empty → помечаем его как Wall. Если сосед **за краем карты** → ставим Wall на соседе в противоположном направлении (внутри карты). Это гарантирует стену даже если зона вплотную к краю.

**Фаза 2 — Утолщение:** flood-fill ещё `outerWallThickness - 1` слоёв от контурных Wall-клеток, включая choke-стены от ShapeZoneEnclosures.

**Фаза 3 — Колонны:** по всем Wall-клеткам создаём физические блоки высотой `outerWallHeight` (уровни 0..H−1).

Параметры: `outerWallThickness`, `outerWallHeight`.

### 4.10 Регенерация карты

`Update`: при нажатии **R** запускается `RegenerateMapRoutine`. Если в сцене найден `MatchManager` — сначала `StopLoop()` (иначе боты держат ссылки на разрушаемые зоны, а NavMesh под ними тоже исчезает). Затем уничтожает всех children генератора, чистит `MapManager.ClearZones()`, ждёт кадр, вызывает `GenerateMap`.

### 4.11 Сид и воспроизводимость

- `useFixedSeed` + `generationSeed` → детерминированная генерация.
- При отключённом `useFixedSeed` сид = `Guid.NewGuid().GetHashCode()`.
- Применённый сид логируется в `currentGenerationSeed` (read-only в инспекторе).

---

## 5. Модуль: Bots

`BotComponent` — простой агент.

Атрибуты:
- `NavMeshAgent` для передвижения.
- `role` (Attacker / Defender / Flanker / Scout).
- `reactionTime`, `chanceToShoot` — параметры боя.
- `deathEffect` — префаб эффекта смерти.

Поведение (`Update` + `FixedUpdate`):
- В `FixedUpdate` для каждого бота вражеской команды бросает рейкаст. Если первое попадание — этот бот, фиксирует как `_target`.
- В `Update` после `reactionTime` совершает выстрел: рейкаст в цель, проверка `chanceToShoot`, если попало — цель `Die()`.
- На `Die()` уведомляет вражескую команду о смене целевого сайта (через `TeamManager.NotifyDefenders`), сохраняет точку смерти в `GameManager`, удаляется.

**Ограничения текущей модели:**
- Нет понятия здоровья, кд оружия, экономики, способностей.
- Нет «прятания за укрытием» (LOS блокируется только статичной геометрией стен).
- Нет звуковых сигналов, флешек и т.п.

---

## 6. Модуль: Teams

### `TeamManager` (базовый)
- Хранит `bots`, `OtherTeam`, `targetSite`.
- `NotifyDefenders` / `NotifyDefendersAboutRotate` — переподчинение ботов под смену атакуемого сайта.

### `AttackerTeamManager`
- Распределение ролей: 2 Attacker, 2 Flanker, 1 Scout.
- Атакеры идут по Main, фланкеры по Link, скаут случайно по дорогам или в нейтральной.
- Ротация цели: при существенной разнице в численности (`_rotationThreshold = 2`) — все становятся Attacker и идут на новый сайт.
- Когда все боты пришли на исходные позиции — синхронный заход на сайт.

### `DefenderTeamManager`
- Распределение: по 2 защитника на каждый сайт + 1 Scout.
- Каждые `_delay = 10` сек случайно репозиционирует часть защитников ближе к Main или Link (имитация ротации).

---

## 6.1. Модуль: MatchManager (цикл матчей)

`MatchManager` (`MonoSingleton`) владеет жизненным циклом матчей. Реализован как state-машина:

```
Idle ── W ──▶ Running ── (one team empty | timeout) ──▶ Cooldown ──▶ Running ──▶ ...
  ▲                                                          │
  └──────────── S / R / maxMatches reached ─────────────────┘
```

**Состояния:**
- `Idle` — цикл выключен, команд/ботов на сцене нет.
- `Running` — идёт матч. `AttackerTeamManager` и `DefenderTeamManager` живут как children `MatchManager`.
- `Cooldown` — ждём `betweenMatchesDelay` секунд после конца матча. Команды уже уничтожены.

**Условия конца матча (`Running` → `Cooldown`):**
- `LiveBotsCount` одной из команд == 0 → победа другой.
- `elapsed >= matchTimeout` → ничья.

**Хоткеи:**

| Клавиша | Действие | Кто обрабатывает |
|---|---|---|
| **W** | `StartLoop()` — включает цикл и сразу запускает первый матч. Игнорируется, если цикл уже идёт. | `MatchManager` |
| **S** | `StopLoop()` — выключает цикл, уничтожает команды/ботов. Карта не трогается. | `MatchManager` |
| **R** | Перегенерация карты. Перед регенерацией вызывает `MatchManager.StopLoop()` (через `FindObjectOfType`, без ленивого создания). | `MapGenerator` |

**Параметры в инспекторе:**
- `botPrefab` — префаб бота с `BotComponent` и `NavMeshAgent`.
- `botsPerTeam` — состав каждой команды (по умолчанию 5).
- `snapSpawnToNavMesh`, `navMeshSampleRadius` — спавнить бота строго на NavMesh.
- `matchTimeout` — максимум секунд на матч (ничья).
- `betweenMatchesDelay` — пауза между матчами в `Cooldown`.
- `maxMatches` — лимит серии (0 = бесконечно). По достижении — цикл выключается сам.

**Скорость симуляции (live-управление):**
- `simulationSpeed` (Range 0.1–20, default 1.0) — множитель `Time.timeScale`. Применяется каждый кадр в `Update`, поэтому ползунок в инспекторе работает в Play Mode мгновенно. `OnValidate` также применяет значение сразу. Программный доступ — `MatchManager.Instance.SetSimulationSpeed(x)` / `SimulationSpeed`.
- `scaleFixedDeltaTime` (default true) — автоматически масштабирует `Time.fixedDeltaTime` пропорционально скорости. Это сохраняет частоту FixedUpdate в реальном времени (физика/боты остаются плавными при 5×–10×). При false — FixedUpdate вызывается чаще, увеличивая CPU-нагрузку, но боты «думают» чаще per-game-second.
- `simulationSpeedStep` + `enableSpeedHotkeys` — хоткеи в Play Mode: `[` уменьшает скорость на шаг, `]` увеличивает, `\` сбрасывает к 1.0.
- При `OnDisable` `MatchManager` восстанавливает `Time.timeScale = 1` и `Time.fixedDeltaTime` к запомненному при `Awake` значению — чтобы Edit Mode / следующая сцена не унаследовали ускорение.
- `GameManager.timeScale` устаревший — установка значения в `Start()` GameManager-а перетирается ApplySimulationSpeed-ом MatchManager-а на следующем кадре. `MatchManager` владеет таймскейлом.

**Read-only в инспекторе:** `matchesPlayed`, `currentState` — для дебага серии.

**Спавн команд:**
- Спавн с бо́льшим Z считается атакерским (соответствует `MapGenerator.Layout.PlaceSpawnZones`).
- Боты инстанцируются из `botPrefab`, родителем становится `TeamManager.gameObject` (children менеджера команды).
- При конце матча `Destroy(teamManager.gameObject)` — дети-боты уходят вместе с ним.
- `StartLoop` валидирует условия: наличие префаба, минимум 2 SpawnZone, наличие NavMesh-триангуляции. При сбое цикл выключается и логируется ошибка.

**Изменения в `TeamManager`:**
- `TeamManager.Update` больше **не** запускает `GameManager.RestartGame` при пустом `bots` — это конфликтовало с loop-логикой. Update пустой (виртуальный, для подклассов).
- Добавлено свойство `LiveBotsCount` (счёт ненулевых ссылок в `bots`), которым `MatchManager` определяет конец матча.
- `SetBots(List<BotComponent>)` и `SetOtherTeam(TeamManager)` — для заполнения состава из кода. Поле `bots` остаётся `[SerializeField]` для инспекторного сценария (`SampleScene`).

---

## 7. Модуль: Storage / GameManager

### `GameManager`
- `timeScale` — ускорение симуляции.
- `roundTime` — длительность раунда (с учётом `timeScale`).
- При истечении раунда / уничтожении одной из команд → перезагружает сцену через `SceneManager.LoadScene`.
- При смерти бота сохраняет позицию в `DeathData.Instance.DeathPositions`.
- Аварийный стоп: если `_deathCount > 5000` — ставит `Time.timeScale = 0`.

### `Storage`
- JSON I/O в `Application.persistentDataPath/Storage/*.json`.
- `Storage.Load<T>()` / `Storage.Save(value)` / `Storage.Delete(name)`.
- В Edit-меню кнопка `Storage/Clear` (Editor-only).

### `DeathData : StorageData<DeathData>`
- Список `Vector3 DeathPositions`.
- Сохраняется автоматически на `OnApplicationQuit` и `OnDestroy` GameManager-а.

---

## 8. Зональные веса (`MapZoneComponent`)

Каждая зона имеет 4 веса для разных ролей:
- `attackWeight`, `defenseWeight`, `flankWeight`, `scoutWeight`.

`GetWeight(role)` → возвращает соответствующий вес.

**Текущее состояние:** веса задаются вручную через инспектор после генерации (значения по умолчанию = 0). Боты пока их не используют для принятия решений. Это **точка интеграции на Этапе 4 roadmap** (параметризация + унификация весов).

---

## 9. Известные ограничения

| Ограничение | Где | Почему |
|---|---|---|
| Веса зон не интегрированы | `AttackerTeamManager` / `DefenderTeamManager` | TODO Этап 4–6 ВКР |
| Расстановка укрытий нестабильна | `MapGenerator.Covers.cs` | в активной переработке |
| Комнаты есть только тип «галерея», нет перекрёстков и ниш | `MapGenerator.Rooms.cs` | Следующая итерация Этапа 1 |
| Карты слишком однообразны | весь Layout | расширить пространство параметров |
| Нет batch-симуляций | `GameManager.RestartGame` просто перезагружает сцену | Этап 5 roadmap |
| Нет автокалибровки ботов | `BotComponent` | Этап 6 roadmap |
| Не сохраняются параметры карты | — | нужен `MapGenerationProfile` ScriptableObject (Этап 4) |
| Метрики только смерти | `DeathData` | расширить: win-rate, длительность раунда, маршруты |

---

## 10. Правила обновления документа

1. **Любая правка структуры файлов** (новый файл, переименование, перемещение) → обновить раздел 3.
2. **Любое изменение пайплайна `GenerateMap`** → обновить раздел 4.2.
3. **Любой новый `BlockType`** → обновить раздел 4.1.
4. **Любая новая партиал-секция** `MapGenerator.<Feature>.cs` → добавить подраздел 4.X.
5. **Любой новый параметр в инспекторе** → добавить в соответствующий подраздел (4.3, 4.5 и т.п.).
6. **Завершение этапа roadmap** → обновить статус в `.cursor/rules/thesis-context.mdc` и зафиксировать здесь в разделе 9.

Если документ перестаёт умещаться в одну страницу — разделить на тематические файлы в `Docs/architecture/` и оставить здесь только индекс.
