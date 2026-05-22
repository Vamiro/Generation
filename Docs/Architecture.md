# Архитектура системы генерации FPS-карт

> Технический дизайн-документ. Описывает **текущее** состояние проекта.
> При изменении кода — обновлять этот документ. Если документ расходится с кодом — приоритет у кода, и нужно править документ.
>
> Связанные источники:
> - `Docs/ВКР Погиба В.С..md` — текст ВКР (что и зачем).
> - `.cursor/rules/thesis-context.mdc` — короткая сводка MVP и roadmap.

---

## 0. Источники модели бота (для ВКР)

Боевая модель и поведенческая упрощённость бота — **намеренное** решение, опирающееся на канон академических работ по симуляции FPS-баланса. Цель платформы — изучать, как **параметры геометрии** влияют на winrate, а не как ведут себя реальные игроки. Чем меньше поведенческих параметров у бота, тем чище сигнал из геометрии.

**Ключевые работы (минимум, на который нужно ссылаться в ВКР):**

1. **Cardamone, Yannakakis, Togelius, Lanzi (2011).** *"Evolving Interesting Maps for a First Person Shooter"*. Applications of Evolutionary Computation, Springer LNCS. — Самый прямой аналог нашей задачи. 4 бота в headless-deathmatch на Cube 2, fitness = `T_f` (среднее время боя от первого попадания до смерти), боты берутся **готовые из движка** (waypoint-based A*), своя модель боя **не строится**. Главная мысль для нас: *«мы намеренно используем простую theory-driven fitness function»* (раздел 3.1) — простота не баг, а методологический выбор.

2. **Karavolos, Liapis, Yannakakis (2018).** *"Using a Surrogate Model of Gameplay for Automated Level Design"*. IEEE CIG. — Симулируют 10⁵ матчей 1v1 deathmatch на разных картах и классах персонажей, тренируют CNN для предсказания (`kill_ratio`, `duration`). Бот = behaviour tree из готового Unity-ассета `Deathmatch AI Kit`. Параметры боя — 8 чисел: HP, скорость, damage, accuracy (cone), rate of fire, clip size, bullets-per-shot, range. Прямая цитата: *«Such agents are often simplistic (including those used in this paper)»* (раздел II). Это индустриальный канон.

3. **Güttler, Johansson (2003).** *"Spatial principles of level-design in multi-player first-person shooters"*. ACM NetGames. — Теоретическая работа, ввела понятие **collision point** — место, где геометрия принуждает игроков встретиться. В нашем контексте: если геометрия определяет, **где** случится бой, и бой = «кто первым выстрелил, тот выиграл», то геометрия автоматически влияет на winrate через распределение collision points по карте.

**Дополнительно (для контекста):**

- Hullett & Whitehead (2010). *"Design patterns in FPS levels"*. FDG'10. — Каталог дизайн-паттернов (arena, choke point, sniping perch и т.п.), используется Karavolos et al. как описание «что генератор должен уметь воспроизводить».
- Liapis (2018). *"Piecemeal evolution of a first person shooter level"*. — Эволюция многоэтажных FPS-уровней по комнатам с designer-control.

**Какую модель боя мы реализовали и почему:**

| Источник | Что взяли |
|---|---|
| Cardamone 2011 | Headless-симуляция десятков матчей, метрика «время боя», бот без сложной cognition. |
| Karavolos 2018 | 5 числовых параметров: HP, dmg, baseAccuracy + distance falloff (`fullAccuracyRange`, `maxRange`, `minAccuracyAtMaxRange`), reactionTime. Ничего больше. |
| Güttler 2003 | Cover/wall блокируют **LOS-рейкаст**, а не входят в формулу `p_hit` отдельным членом. Это значит, что укрытие работает чисто геометрически: пока стрелок физически не видит цель, бой не начинается. |

**От чего сознательно отказались** (попробовали в Этапах B/C переработки бот-стека, потом удалили):

- Ролевые множители точности (`attackerMultiplier`, `defenderMultiplier` и т.п.) — не имеют геометрического смысла, маскируют сигнал.
- Штраф за движение цели (`targetMovingPenalty`) — добавляет шум без интерпретации.
- `TacticalSlot` с `facingDir`/`influenceRadius`/`coverHeight`/`coverScore`/`coverAngleCosTolerance` (~~аналог `HidingSpot` из CS:GO~~) — оптимизация под нагрузку CS:GO (тысячи ботов), не нужная при 10 ботах в матче. Заменена прямым `Physics.Raycast` для LOS.
- `aimAngleTolerance` (запрет стрельбы пока не довернулся) — добавлял шум от случайной начальной ориентации после `NavMeshAgent.SetDestination`.
- `pushDepthBias`, `repositionMain/Link/StayWeight` — 3 параметра стиля защиты, заменены на одну вероятность `pushToRoadProbability`.

Аргумент: меньше переменных → чище интерпретация результата в анализе «параметр карты → winrate». Если в таблице корреляция `длина main → winrate` = 0.7, это **точно** про длину main, а не про интерференцию `pushDepthBias = 0.4`.

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
│   ├── GameManager.cs           — сбор метрики смертей (DeathData), Save On Quit
│   ├── MatchManager.cs          — match loop, скорость симуляции, headless-режим, хоткеи W/S/H/[/]
│   ├── MatchStats.cs            — MatchStatsCollector (MonoSingleton) + StatsData (JSON)  ← NEW
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
│   ├── MapGenerator.NavMesh.cs        ★ partial: RebuildNavMesh() — runtime-бейк на Geometry
│   ├── MapGenerator.Covers.cs         ★ partial: расстановка укрытий
│   └── Zones/
│       ├── SpawnZoneComponent.cs      — пустой маркер
│       ├── SiteZoneComponent.cs       — пустой маркер
│       ├── NeutralZoneComponent.cs    — пустой маркер
│       ├── RoomZoneComponent.cs       — пустой маркер (галереи)
│       └── RoadZoneComponent.cs       — roadType (Main/Link), roadToSite
│
├── Bot/
│   └── BotComponent.cs          — NavMeshAgent, HP, поворот к цели, scoring, reactionTime, pHit от дистанции
│
├── Team/
│   ├── TeamManager.cs           — базовый класс команды (boss bot list + ссылка на OtherTeam)
│   ├── AttackerTeamManager.cs   — распределяет роли Attacker/Flanker/Scout, ротация при потерях
│   └── DefenderTeamManager.cs   — распределяет защитников по сайтам пропорционально defense-весу + Scout, периодический reposition
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
[9] PlaceCovers (если enableCovers)  — расстановка укрытий внутри зон в Geometry/.
                                       Заполняет общее поле coverOccupancy[,].
[9.5] RefreshZoneSamplePointsAfterCovers — пересобирает samplePoints зарегистрированных
                                       MapZoneComponent-ов, исключая клетки с укрытиями
                                       (чтобы боты не спавнились внутри блока укрытия).
[10] RebuildNavMesh                  — runtime-бейк NavMeshSurface на Geometry/
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

**Карта занятости (`coverOccupancy[,]`):**
- Заполняется в `PlaceCovers` (тот же буфер, что внутренний `hasCover`). Поле `MapGenerator`, видно наружу через `IsCellOccupiedByCover(x, z)`.
- Используется `RuntimeZones.BuildSamplePoints` для исключения клеток с укрытиями из `MapZoneComponent.samplePoints`. Это гарантирует, что `bot.MoveToZone(zone)` / `MatchManager.SpawnBots` не выберут точку прямо в блоке укрытия.
- Edge case: если зона полностью покрыта укрытиями, `BuildSamplePoints` fallback-ит на весь регион (иначе `GetRandomPointInZone()` упал бы на пустом списке).

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

`BotComponent` — минималистичный поведенческий агент. Боевая модель **сознательно** упрощена под задачу ВКР: исследование зависимости winrate от геометрии карты. См. §0 (источники модели) — Cardamone 2011, Karavolos 2018, Güttler 2003.

**Боевой контракт (5 параметров):**

```
p_hit = baseAccuracy * distanceFalloff(dist)

distanceFalloff:
  dist ≤ fullAccuracyRange → 1.0
  fullAccuracyRange < dist ≤ maxRange → линейный спад 1.0 → minAccuracyAtMaxRange
  dist > maxRange → minAccuracyAtMaxRange (минимум, не уходим в 0)
```

| Параметр | Дефолт | Что регулирует |
|---|---|---|
| `baseAccuracy` | 0.5 | Шанс попасть на близкой дистанции. Сохраняет старый баланс времён `chanceToShoot = 0.5`. |
| `fullAccuracyRange` | 8 м | До какой дистанции спад не работает. |
| `maxRange` | 40 м | На какой дистанции спад достигает минимума. |
| `minAccuracyAtMaxRange` | 0.25 | Точность на длинных дистанциях. |
| `reactionTime` | 0.3 сек | Задержка между выстрелами. Общая для всех ботов. |

**HP/урон:**
- `maxHealth = 1` (default), `damagePerShot = 1` → один успешный выстрел = смерть. Каждая дуэль = "кто первый попал".
- Можно поставить HP > 1 в инспекторе, но смысла для исследования геометрии в этом мало.

**Чего сознательно НЕТ в формуле `p_hit`** (см. §0 — обоснование):
- Ролевых множителей точности.
- Штрафа за движение цели.
- Cover-bonus отдельным членом. Cover работает геометрически через `Physics.Raycast` LOS: если cover между стрелком и целью, `HasLineOfSightTo` возвращает false и бой не начинается. Это прямая реализация принципа Güttler 2003 — геометрия определяет collision points, а бой = "что случилось в этой точке".

**Атрибуты:**
- `NavMeshAgent` — передвижение. В `Awake` ставится `agent.updateRotation = false`, чтобы бот мог "целиться" в неподвижного врага после остановки агента.
- `role` (Attacker / Defender / Flanker / Scout) — назначается `TeamManager` в рантайме.
- **Прицеливание:** `aimTurnSpeed` (град/сек) — скорость поворота тела к цели. Влияет визуально и косвенно на тайминг (доворот занимает время), но НЕ блокирует выстрел (раньше был `aimAngleTolerance` — удалён).
- **Выбор цели:** `sightRange`, `distancePriorityWeight`, `threatPriorityBonus`.
- **Reposition tick:** `repositionDelay`, `repositionJitter` — таймер, после которого бот в режиме idle меняет точку в текущей зоне.
- **Скорости по ролям:** `attackerSpeed / defenderSpeed / flankerSpeed / scoutSpeed` — индивидуальные `NavMeshAgent.speed`.
- `deathEffect` — префаб эффекта смерти.

**Поведение (`Update` + `FixedUpdate`):**
- `FixedUpdate`: для каждого живого врага считается `score = distance * distancePriorityWeight − (целится в нас ? threatPriorityBonus : 0)`. Цель = минимальный score среди тех, кто в `sightRange` и в LOS.
- `Update`:
  - **Бой** (`_target != null`): `FaceTarget()` (поворот через `Quaternion.RotateTowards`) + накопление `reactionTime`. Когда таймер выкипел — `Shoot()`. Выстрел проверяет LOS, бросает `Random.value > pHit`, при попадании зовёт `victim.ReceiveDamage(damagePerShot)`.
  - **Idle** (`_target == null`): `HandleIdleReposition()` копит таймер только пока `IsOnPosition`. По достижении `repositionDelay × (1 ± jitter)` — `agent.SetDestination(_currentZone.GetRandomPointInZone())`.
- `ReceiveDamage(int)` — публичный API.
- `Die()` уведомляет вражескую команду о смене целевого сайта (`TeamManager.NotifyDefenders`), сохраняет точку смерти в `GameManager`, шлёт `MatchStatsCollector.OnBotDied`, удаляется.

**Геометрический эффект "преимущество защитника" (без отдельного параметра!):**

Если защитник пришёл в hold-зону раньше атакера, он стоит, его `agent.velocity = 0`, его `transform.rotation` направлен туда, куда повернул его NavMeshAgent на последнем сегменте пути (обычно — в сторону входа в зону, потому что туда он ехал). Атакер вбегает в зону → они видят друг друга в один и тот же момент → у обоих стартует `reactionTime`. Но защитник уже **смотрит примерно в направлении атакера**, поэтому угол доворота через `FaceTarget` для него минимален; атакер же доворачивается дольше → его выстрел запаздывает на величину времени поворота. Это даёт защитнику **бесплатное преимущество**, которое определяется **геометрией пути защитника к hold-точке** (если путь шёл "в сторону входа", защитник смотрит правильно).

То же про **холдер vs entry-fragger**: защитник, идущий по diagonal через сайт, в момент встречи может смотреть в стену, и тогда атакер выигрывает entry. Это **тоже определяется геометрией зоны** (форма сайта, расположение входов).

Эти эффекты «работают бесплатно» через `NavMeshAgent`-rotation и `aimTurnSpeed`, без отдельных параметров типа "holderBonus".

**Гарантии для воспроизводимости (важно для калибровки):**
- Все случайности (`Random.value` в `Shoot()`, jitter в `ScheduleNextReposition`) используют `UnityEngine.Random` — попадают под `Random.InitState(currentGenerationSeed)`, выставленный `MapGenerator`-ом.
- Скоринг цели детерминирован при равных входах (нет `Random` в `PickBestTarget`).

---

## 6. Модуль: Teams

### `TeamManager` (базовый)
- Хранит `bots`, `OtherTeam`, `targetSite`.
- `NotifyDefenders` / `NotifyDefendersAboutRotate` — переподчинение ботов под смену атакуемого сайта.

### `AttackerTeamManager`
- Распределение ролей вынесено в инспектор (`attackerCount / flankerCount / scoutCount`, default 2/2/1). Хвост сверх суммы (если `botsPerTeam` больше) получает Attacker.
- Атакеры идут по Main, фланкеры по Link, скаут случайно по дорогам или в нейтральной (`scoutNeutralProbability` в инспекторе, default 0.5). Все движения — обычный `bot.MoveToZone(zone)` → случайная точка зоны.
- **Выбор атакуемого сайта — weighted random по `site.GetWeight(BotRole.Attacker)`** (значения берутся из `MapManager.siteAWeights`/`siteBWeights`, см. §8). Если все веса 0 — uniform random. На референс-карте: `siteAWeights.attack = 1.0`, `siteBWeights.attack = 1.5` → атакеры в 1.5 раза чаще выбирают B.
- Ротация цели: при разнице в численности `rotationThreshold` (инспектор, default 2) — все становятся Attacker и идут на новый сайт.
- Когда все боты пришли на исходные позиции — синхронный заход на сайт через `bot.MoveToZone(targetSite)`.

### `DefenderTeamManager`
- Распределение защитников по сайтам — **weighted по `site.GetWeight(BotRole.Defender)`** (из `MapManager.siteAWeights`/`siteBWeights.defense`, см. §8). Если все веса 0 — равное по `defendersPerSite` на каждый сайт. Если веса заданы — пропорциональное распределение через largest-remainder method.
- `scoutCount` (инспектор, default 1) — сколько ботов идут в Scout (нейтраль/дороги). Остаток после защитников.
- `initialPushCount` (`IntRange`, default 0..2) — сколько защитников каждого сайта **сразу на старте** идут на main вместо site-hold. Геометрический параметр: определяет, как часто атакеры встречают защитника на дороге vs на сайте. Минимум 1 бот всегда остаётся на site-hold (clamp к `count - 1`).
- `AssignRoleSafe` для роли `Defender` → `bot.AssignRole(BotRole.Defender, zone)` → `MoveToZone(zone)` → случайная точка сайта.
- Каждые `repositionDelay` сек (default 10) защитник с вероятностью `pushToRoadProbability` (default 0.5) выходит на дорогу. Выбор Main vs Link — по `roadPickMainBias` (default 0.67). **Эта пара параметров заменила прежние 3 веса `repositionMain/Link/Stay`** — упрощение в рамках §0.

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
- `simulationSpeed` (Range 0.1–**50**, default 1.0) — множитель `Time.timeScale`. Применяется каждый кадр в `Update`, поэтому ползунок в инспекторе работает в Play Mode мгновенно. `OnValidate` также применяет значение сразу. Программный доступ — `MatchManager.Instance.SetSimulationSpeed(x)` / `SimulationSpeed`. Реальный потолок зависит от CPU.
- `scaleFixedDeltaTime` (default true) — автоматически масштабирует `Time.fixedDeltaTime` пропорционально скорости. Это сохраняет частоту FixedUpdate в реальном времени (физика/боты остаются плавными при 5×–10×). При false — FixedUpdate вызывается чаще, увеличивая CPU-нагрузку, но боты «думают» чаще per-game-second.
- `simulationSpeedStep` + `enableSpeedHotkeys` — хоткеи в Play Mode: `[` уменьшает скорость на шаг, `]` увеличивает, `\` сбрасывает к 1.0.
- При `OnDisable` `MatchManager` восстанавливает `Time.timeScale = 1` и `Time.fixedDeltaTime` к запомненному при `Awake` значению — чтобы Edit Mode / следующая сцена не унаследовали ускорение.
- `MatchManager` — единственный владелец `Time.timeScale`. Других писателей не должно быть.

**Headless-режим (для батч-симуляций):**
- `headlessMode` (default false), `headlessTargetFps` (1–60, default 10). Хоткей **H** в Play Mode — toggle.
- Включает: `QualitySettings.vSyncCount = 0`, `Application.targetFrameRate = headlessTargetFps`, отключает все камеры в сцене.
- Идея: при `simulationSpeed = 50×` и низком render-fps Unity не тратит CPU на рендер, а каждый кадр выполняет много шагов физики/логики → симуляция бежит максимально быстро.
- `OnDisable` восстанавливает исходные `vSyncCount`/`targetFrameRate`/камеры.

**Метрика матчей:** при старте матча `MatchManager` зовёт `MatchStatsCollector.Instance.OnMatchStarted()`, при конце — `OnMatchEnded(outcome, durationSeconds, attackerSiteName, attackersAlive, defendersAlive)`. См. подсекцию 7.1.

**Read-only в инспекторе:** `matchesPlayed`, `currentState` — для дебага серии.

**Спавн команд:**
- Спавн с **меньшим** Z считается атакерским — это соответствует `MapGenerator.Layout.PlaceSpawnZones`, где `attackerZ = innerPadding + halfZ` (нижняя кромка карты), а `defenderZ = height - 1 - innerPadding - ...` (верхняя кромка). Если меняете геометрию спавнов в Layout — синхронно правьте сравнение в `MatchManager.TryStartMatch`.
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
Лёгкий компонент. Отвечает только за сбор метрики смертей и сохранение на выход:
- `SaveDeathPosition(Vector3)` — записать в `DeathData.Instance.DeathPositions`.
- `SaveDeathPositions()` — записать `DeathData` в Storage. Зовётся автоматически на `OnApplicationQuit` и `OnDestroy`.
- `IncreaseDeathCount()` — оставлен пустым для обратной совместимости со старыми вызовами.

Что **удалено** (раньше тут было, теперь нет):
- `Update`-таймер `roundTime` и вызов `RestartGame` → перезагрузки сцены. Конфликтовал с `MatchManager.matchTimeout` и приводил к спонтанному перезапуску всей сцены посреди серии. Жизненным циклом матчей и `Time.timeScale` теперь владеет **только** `MatchManager` (см. 6.1, «Скорость симуляции»).
- Поля `timeScale`, `roundTime` в `GameManager` — убраны.
- Аварийный стоп по `_deathCount > 5000` — убран (`maxMatches` в `MatchManager` выполняет эту роль).

### `BotComponent.Die` — метки смертей
- Префаб `deathEffect` инстанцируется как child ленивого GameObject **`DeathMarkers`** в корне сцены (`GameObject.Find("DeathMarkers")` → создание при первой смерти).
- `DontDestroyOnLoad` **не используется** — старый код висел в persistent-иерархии и тёк. Теперь метки переживают R-регенерацию карты (т.к. `DeathMarkers` не child мап-генератора), но удаляются вместе со сценой.

### `Storage`
- JSON I/O в `Application.persistentDataPath/Storage/*.json`.
- `Storage.Load<T>()` / `Storage.Save(value)` / `Storage.Delete(name)`.
- В Edit-меню кнопка `Storage/Clear` (Editor-only).

### `DeathData : StorageData<DeathData>`
- Список `Vector3 DeathPositions`.
- Сохраняется автоматически на `OnApplicationQuit` и `OnDestroy` GameManager-а.

### 7.1 `MatchStatsCollector` (Core/MatchStats.cs)

MonoSingleton, отвечает за сбор/агрегацию/отображение/сохранение метрик матчей.

**События, на которые подписан:**
- `OnMatchStarted()` — от `MatchManager` после успешного `TryStartMatch`. Сбрасывает per-match счётчики (first-blood, deaths).
- `OnBotDied(side, role, position, matchTimeSeconds)` — от `BotComponent.Die`. Сторону определяет по типу TeamManager-а (`is AttackerTeamManager` → Attackers). Время — `MatchManager.CurrentMatchTime`. Зона смерти определяется через `MapManager.Zones` — попадание в любой из `BoxCollider`-сегментов (потому что зона теперь = несколько сегментов).
- `OnMatchEnded(outcome, durationSeconds, attackerTargetSiteName, attackersAliveAtEnd, defendersAliveAtEnd)` — от `MatchManager` в `TickStateMachine` при завершении матча. `outcome ∈ {AttackersWin, DefendersWin, Timeout}`.

**Метрики (read-only в инспекторе):**

| Поле | Что показывает |
|---|---|
| `matchesPlayed` | Всего сыгранных матчей (включая таймауты) |
| `decisiveMatches` | Матчей, завершившихся победой одной из сторон (= `attackerWins + defenderWins`). Знаменатель для всех винрейтов и средних |
| `attackerWins`, `defenderWins` | Базовые счётчики побед |
| `timeouts` | Сколько раундов завершились по таймауту. **Не участвуют** в винрейтах/средних — только счётчик |
| `attackerWinRate` / `defenderWinRate` | Проценты от `decisiveMatches` (без таймаутов) |
| `avgRoundDuration` | Среднее время матча в игровых секундах. Считается **только** по `decisiveMatches` — иначе таймауты завышали бы среднее до `matchTimeout` |
| `avgTimeToFirstBlood` | Среднее время до первой смерти; «N/M матчей» — сколько решающих матчей имели kills |
| `avgWinnerSurvivors` | Среднее число выживших ботов победившей стороны (proxy для KD), только по `decisiveMatches` |
| `siteAttackStats` | Per-site: сколько раз атакеры выбрали этот сайт + winrate. **Таймауты не входят** ни в `attacks`, ни в `wins` |
| `deathsAttackerRole` / `deathsFlankerRole` / `deathsScoutRole` / `deathsDefenderRole` | Накопленные смерти по ролям (включая таймаутные матчи) |
| `topKillZones` | Топ-5 зон по количеству смертей (включая таймаутные матчи) |

> Решение: таймаут — это «недоигранный» раунд (`elapsed >= matchTimeout`). Если включать его в средние, `avgRoundDuration` всегда подтягивается к потолку, `avgWinnerSurvivors` к 0 (или к составу команды, если считать выживших стороны без победы), а винрейты обеих сторон занижаются. Поэтому в статистику попадают только матчи с явным победителем, а количество таймаутов сохраняется отдельным счётчиком как сигнал, что параметры карты/ботов нужно настраивать (слишком короткий `matchTimeout` или слишком пассивные боты).

**Управление в инспекторе:**
- `autoSaveOnFinish` (default true) — на `StopLoop` (S) и `OnApplicationQuit` сохраняет всю серию в Storage.
- `resetOnMapRegen` (default true) — `MapGenerator.RegenerateMapRoutine` ищет `MatchStatsCollector` и при этом флаге вызывает `ResetAll()`. Логика: новая карта = новая статистика.

**`StatsData : StorageData<StatsData>`** — JSON-дамп:
- `List<MatchRecord> Matches` (MatchNumber, Outcome, DurationSeconds, AttackerTargetSite, AttackersAliveAtEnd, DefendersAliveAtEnd, Deaths, TimeToFirstBlood).
- Файл `Application.persistentDataPath/Storage/StatsData.json`. `Storage.Save(Instance)` перезаписывает целиком.

**Хоткеи (через MatchManager):**

| Клавиша | Действие |
|---|---|
| `[` / `]` | Замедлить / ускорить симуляцию на шаг |
| `\` | Сбросить скорость к 1× |
| **H** | Toggle headless-режима (рендер off, FPS = `headlessTargetFps`) |
| W / S / R | Start / Stop loop / Regenerate map (как и раньше) |

---

## 8. Зональные веса (`MapManager`)

Веса для разных ролей **централизованы в `MapManager`**, а не размазаны по зонам. Это упрощает калибровку: на референс-карте (Dust2/Anubis) баланс правится в одном месте, и **переживает регенерацию процедурной карты** (R) — `MapManager` живёт между регенерациями, а зоны пересоздаются.

**Структура (`MapManager.cs`):**

```csharp
[Serializable]
public struct ZoneRoleWeights { public float attack, defense, flank, scout; }

[SerializeField] private ZoneRoleWeights siteAWeights = ZoneRoleWeights.Default; // (1,1,1,1)
[SerializeField] private ZoneRoleWeights siteBWeights = ZoneRoleWeights.Default;
```

**Идентификация сайтов:** A = сайт с минимальным `SpawnId`, B = со следующим. На процедурной карте `SpawnId` назначается генератором детерминированно (см. `MapGenerator.RuntimeZones`). На референс-карте — пользователь выставляет `SpawnId = 0` / `1` в инспекторе `SiteZoneComponent` вручную.

**Публичный API:**

| Метод | Возвращает |
|---|---|
| `MapManager.GetWeight(zone, role)` | Вес зоны для роли. Для не-Site-зон или сайтов с индексом ≥ 2 — `0`. |
| `MapZoneComponent.GetWeight(role)` | Тонкая обёртка над `MapManager.GetWeight(this, role)`. Сохранена для обратной совместимости. |

**Где используются (Этап 1 калибровочного блока):**

| Точка | Файл | Поведение |
|---|---|---|
| Выбор атакуемого сайта | `AttackerTeamManager.PickWeightedSite` | Weighted random по `siteA.GetWeight(Attacker)` vs `siteB`. Если все веса 0 → uniform random. |
| Распределение защитников по сайтам | `DefenderTeamManager.ComputeDefendersDistribution` | Пропорциональное распределение через largest-remainder method. Если все веса 0 → равно по `defendersPerSite` на сайт. |

**Что НЕ использует пока (точки будущей интеграции):**
- `flank` / `scout` — не учитываются при выборе зоны для Flanker/Scout.
- Веса дорог (`RoadZoneComponent`) — сейчас выбор маршрута игнорирует веса (атакер всегда Main, фланкер всегда Link). Если потребуется — добавить поля `mainRoad*Weights` / `linkRoad*Weights` в `MapManager` и читать в `AttackerTeamManager.FindPreferredRoad`.
- Нейтрали и спавны — весов в `MapManager` сейчас нет (никто не читает).

**Способ калибровки на референс-карте:**
1. Расставить сайты вручную (например, скопировав геометрию Dust2). В инспекторе каждой `SiteZoneComponent` выставить `SpawnId = 0` для A, `SpawnId = 1` для B.
2. В инспекторе `MapManager` (один объект на сцене) выставить `siteAWeights` и `siteBWeights` под реальную карту. Например, для Dust2-like, где B-сайт чуть слабее держится: `siteAWeights.defense = 1.1`, `siteBWeights.defense = 0.9`.
3. Запустить серию матчей через `MatchManager.maxMatches` в headless-режиме, посмотреть `StatsData.json` и `siteAttackStats`.
4. Подкрутить веса и повторить.

**Почему не ScriptableObject:** для MVP достаточно одного балансного профиля на сцену. Если в Этапе 5 (batch-симуляция) понадобится менять профили без правки сцены — выделить `ZoneRoleWeights` в `MapBalanceProfile : ScriptableObject` и хранить ссылку в `MapManager`. Архитектурно это +5 строк изменений и совместимо с текущим API.

---

## 9. Известные ограничения

| Ограничение | Где | Почему |
|---|---|---|
| Веса зон частично интегрированы | `AttackerTeamManager` / `DefenderTeamManager` | `attack`-вес — для выбора сайта атакерами; `defense`-вес — для распределения защитников. `flank` / `scout` и веса дорог/спавнов/нейтрали — пока не используются. Веса централизованы в `MapManager`, не в зонах. См. §8. |
| Расстановка укрытий нестабильна | `MapGenerator.Covers.cs` | в активной переработке |
| Комнаты есть только тип «галерея», нет перекрёстков и ниш | `MapGenerator.Rooms.cs` | Следующая итерация Этапа 1 |
| Карты слишком однообразны | весь Layout | расширить пространство параметров |
| Нет batch-симуляций | `MatchManager` гоняет серию матчей (`maxMatches`) + headless-режим, `MatchStatsCollector` пишет per-match метрики в Storage. Не хватает только runner-а по списку карт (param sweep). | Этап 5 roadmap |
| Метрики только смерти + per-match агрегаты | `DeathData` (координаты) + `StatsData` (исход, длительность, kill-zones) | следующий шаг — kill-heatmap из StatsData |
| Нет автокалибровки ботов | `BotComponent` | Этап 6 roadmap. Боевая модель упрощена до 5 параметров (см. §5): `baseAccuracy`, `fullAccuracyRange`, `maxRange`, `minAccuracyAtMaxRange`, `reactionTime`. Калибровка = подбор этих 5 чисел + `initialPushCount` + `pushToRoadProbability` под winrate ≈ 50:50 на референс-карте. Прежние этапы B (HitModel со штрафами cover/movement/role) и C (TacticalSlot) **откатаны** в пользу минималистичной модели по канону Cardamone 2011 / Karavolos 2018 — см. §0. |
| Не сохраняются параметры карты | — | нужен `MapGenerationProfile` ScriptableObject (Этап 4) |

---

## 10. Правила обновления документа

1. **Любая правка структуры файлов** (новый файл, переименование, перемещение) → обновить раздел 3.
2. **Любое изменение пайплайна `GenerateMap`** → обновить раздел 4.2.
3. **Любой новый `BlockType`** → обновить раздел 4.1.
4. **Любая новая партиал-секция** `MapGenerator.<Feature>.cs` → добавить подраздел 4.X.
5. **Любой новый параметр в инспекторе** → добавить в соответствующий подраздел (4.3, 4.5 и т.п.).
6. **Завершение этапа roadmap** → обновить статус в `.cursor/rules/thesis-context.mdc` и зафиксировать здесь в разделе 9.

Если документ перестаёт умещаться в одну страницу — разделить на тематические файлы в `Docs/architecture/` и оставить здесь только индекс.
